from __future__ import annotations

import hashlib
import io
import os
import shutil
import subprocess
import sys
import tempfile
import uuid
from pathlib import Path

import fitz
from PIL import Image, ImageOps

from .models import SUPPORTED, inside, reconcile
from .parser import parse

EXCLUDED = {'.git', 'node_modules', '.venv', 'venv', '__pycache__', 'dist', 'build', 'release', 'releases', 'outputs', 'output', '整理结果', '.invoice-assistant-cache'}
_ocr = None


def digest(path):
    h = hashlib.sha256()
    with open(path, 'rb') as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(part)
    return h.hexdigest()


def resource_root():
    return Path(os.environ.get('INVOICE_RESOURCES', getattr(sys, '_MEIPASS', Path(__file__).resolve().parents[2])))


def soffice_path():
    root = resource_root()
    candidates = [root / 'libreoffice/program/soffice.exe', root / 'libreoffice/program/soffice',
                  root / 'libreoffice/Contents/MacOS/soffice']
    configured = os.environ.get('INVOICE_SOFFICE')
    if configured:
        candidates.insert(0, Path(configured))
    # Frozen releases must use bundled dependencies, not silently rely on the host.
    if not getattr(sys, 'frozen', False):
        found = shutil.which('soffice')
        if found:
            candidates.append(Path(found))
        candidates.append(Path('/Applications/LibreOffice.app/Contents/MacOS/soffice'))
    return next((p for p in candidates if p.is_file()), None)


def ocr_text(page, clip=None):
    global _ocr
    if _ocr is None:
        from rapidocr_onnxruntime import RapidOCR
        _ocr = RapidOCR(intra_op_num_threads=2, inter_op_num_threads=2)
    pix = page.get_pixmap(matrix=fitz.Matrix(2, 2), clip=clip, alpha=False)
    result, _ = _ocr(pix.tobytes('png'))
    if not result:
        return '', 0
    return '\n'.join(item[1] for item in result), min(float(item[2]) for item in result)


def convert_word(path, cache, file_hash):
    executable = soffice_path()
    if not executable:
        raise RuntimeError('Word 转换组件不可用，请使用完整发行包，或在开发环境安装 LibreOffice')
    target = cache / (file_hash + '.pdf')
    if target.exists():
        return target
    with tempfile.TemporaryDirectory(prefix='word-', dir=cache) as directory:
        work = Path(directory)
        profile = work / 'profile'
        profile.mkdir()
        (profile / 'user').mkdir()
        (profile / 'user/registrymodifications.xcu').write_text('''<?xml version="1.0" encoding="UTF-8"?>
<oor:items xmlns:oor="http://openoffice.org/2001/registry">
<item oor:path="/org.openoffice.Office.Common/Security/Scripting"><prop oor:name="MacroSecurityLevel" oor:op="fuse"><value>3</value></prop></item>
<item oor:path="/org.openoffice.Office.Common/Misc"><prop oor:name="FirstRun" oor:op="fuse"><value>false</value></prop></item>
</oor:items>''', encoding='utf-8')
        # A unique neutral filename also avoids command options and same-name collisions.
        staged = work / ('document' + path.suffix.lower())
        shutil.copyfile(path, staged)
        command = [str(executable), '-env:UserInstallation=' + profile.as_uri(), '--headless', '--nologo',
                   '--nodefault', '--nofirststartwizard', '--norestore', '--convert-to', 'pdf:writer_pdf_Export',
                   '--outdir', str(work), str(staged)]
        try:
            proc = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                    creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            try:
                stdout, stderr = proc.communicate(timeout=120)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.communicate()
                raise RuntimeError('Word 转换超时，请检查原文件')
            converted = work / 'document.pdf'
            if proc.returncode or not converted.is_file():
                raise RuntimeError('Word 转换失败：文件可能损坏、加密或格式不受支持')
            with fitz.open(converted) as doc:
                if not doc.page_count:
                    raise RuntimeError('Word 转换没有生成页面')
            shutil.move(converted, target)
        except OSError as exc:
            raise RuntimeError('无法启动 Word 转换组件') from exc
    return target


def image_pdf(path, cache, file_hash):
    target = cache / (file_hash + '.pdf')
    if target.exists():
        return target
    with Image.open(path) as original:
        image = ImageOps.exif_transpose(original).convert('RGB')
        width, height = image.size
        if width <= 0 or height <= 0:
            raise RuntimeError('图片为空')
        data = io.BytesIO()
        image.save(data, format='PNG')
        doc = fitz.open()
        # Keep proportional dimensions, with a manageable maximum PDF canvas.
        scale = min(1, 2000 / max(width, height))
        page = doc.new_page(width=width * scale, height=height * scale)
        page.insert_image(page.rect, stream=data.getvalue())
        doc.save(target)
        doc.close()
    return target


def discover(paths, excluded):
    files, errors = [], []
    seen = set()
    for raw in paths:
        path = Path(raw).expanduser().resolve()
        if any(inside(path, directory) for directory in excluded):
            errors.append((path, '已排除程序、缓存或输出目录'))
            continue
        if path.is_dir():
            found = False
            for root, dirs, names in os.walk(path, followlinks=False):
                dirs[:] = sorted(d for d in dirs if d not in EXCLUDED and not d.startswith(('发票整理_', '.invoice-export-'))
                                 and not Path(root, d).is_symlink()
                                 and not any(inside(Path(root, d), directory) for directory in excluded))
                for name in sorted(names):
                    candidate = Path(root, name)
                    if candidate.suffix.lower() in SUPPORTED and not candidate.is_symlink():
                        found = True
                        if candidate not in seen:
                            files.append(candidate)
                            seen.add(candidate)
            if not found:
                errors.append((path, '文件夹内没有可导入的 PDF、Word 或图片'))
        elif path.is_file() and path.suffix.lower() in SUPPORTED:
            if path not in seen:
                files.append(path)
                seen.add(path)
        else:
            errors.append((path, '文件不存在或格式不受支持'))
    return files, errors


def error_record(path, message, file_hash=''):
    return dict(id=uuid.uuid4().hex, source=str(path), filename=path.name, source_hash=file_hash,
                number='', date=None, amount=None, category='其他', kind='invoice', warnings=[],
                page_start=1, page_end=1, page_count=1, error=message, reviewed=False)


def import_files(request, emit):
    cache = Path(request['cache']).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    existing = request.get('records', [])
    seen_hashes = {row.get('source_hash') for row in existing if not row.get('error')}
    excluded = [cache, *request.get('excluded', [])]
    files, errors = discover(request['paths'], excluded)
    records = []
    skipped = []
    for path, reason in errors:
        row = error_record(path, reason)
        records.append(row)
        emit('item', record=row)
    for index, path in enumerate(files):
        emit('progress', current=index + 1, total=len(files), filename=path.name)
        file_hash = ''
        try:
            file_hash = digest(path)
            if file_hash in seen_hashes:
                skipped.append(path.name)
                continue
            render_path = path
            if path.suffix.lower() in ('.doc', '.docx'):
                render_path = convert_word(path, cache, file_hash)
            elif path.suffix.lower() != '.pdf':
                render_path = image_pdf(path, cache, file_hash)
            render_hash = digest(render_path)
            with fitz.open(render_path) as doc:
                if doc.is_encrypted:
                    raise RuntimeError('文件已加密，请先导出未加密副本')
                if not doc.page_count:
                    raise RuntimeError('文件没有页面')
                for page_index, page in enumerate(doc):
                    try:
                        text = page.get_text(sort=True)
                        parsed = parse(text, str(path.parent))
                        method = 'text'
                        if not parsed.get('requires_split') and (len(text.strip()) < 20 or not parsed['number'] or not parsed['amount'] or not parsed['date']):
                            scanned, confidence = ocr_text(page)
                            if scanned:
                                # Prefer labelled text fields; OCR supplies only missing fields.
                                ocr_parsed = parse(scanned, str(path.parent))
                                for field in ('number', 'date', 'amount'):
                                    if not parsed[field]:
                                        parsed[field] = ocr_parsed[field]
                                if len(text.strip()) < 20:
                                    parsed = ocr_parsed
                                parsed['warnings'] = list(dict.fromkeys(parsed['warnings'] + ocr_parsed['warnings']))
                                method = 'ocr' if len(text.strip()) < 20 else 'text+ocr'
                                parsed['warnings'].append('包含 OCR 识别结果，请对照原件核对数字和日期')
                                if confidence < 0.85:
                                    parsed['warnings'].append('部分文字识别置信度较低')
                        row = dict(id=uuid.uuid4().hex, source=str(path), filename=path.name,
                                   source_hash=file_hash, render_path=str(render_path), render_hash=render_hash,
                                   page_start=page_index + 1, page_end=page_index + 1, page_count=doc.page_count,
                                   crop=None, reviewed=False, method=method, **parsed)
                        if doc.page_count > 1:
                            row['warnings'].append('多页材料：请确认每张票的页范围；连续页可合并，空白或附件页可移除')
                        records.append(row)
                        emit('item', record=row)
                    except Exception as exc:
                        row = error_record(path, f'第 {page_index + 1} 页识别失败：{exc}', file_hash)
                        records.append(row)
                        emit('item', record=row)
            seen_hashes.add(file_hash)
        except Exception as exc:
            row = error_record(path, str(exc), file_hash)
            records.append(row)
            emit('item', record=row)
    return dict(records=reconcile(existing + records), skipped=skipped)


def clip_rect(page, crop):
    if crop is None:
        return page.rect
    x0, y0, x1, y1 = crop
    return fitz.Rect(page.rect.x0 + x0 * page.rect.width, page.rect.y0 + y0 * page.rect.height,
                     page.rect.x0 + x1 * page.rect.width, page.rect.y0 + y1 * page.rect.height)


def check_source(row):
    if digest(row['source']) != row['source_hash']:
        raise RuntimeError(f"原文件已变更，请重新导入：{row['filename']}")
    if digest(row['render_path']) != row['render_hash']:
        raise RuntimeError(f"预览文件已变更，请重新导入：{row['filename']}")


def preview(request):
    row = request['record']
    check_source(row)
    with fitz.open(row['render_path']) as doc:
        index = request.get('page', row['page_start']) - 1
        page = doc[index]
        pix = page.get_pixmap(matrix=fitz.Matrix(min(1.8, 1500 / max(page.rect.width, page.rect.height))), alpha=False)
        import base64
        return dict(image='data:image/png;base64,' + base64.b64encode(pix.tobytes('png')).decode(),
                    width=pix.width, height=pix.height, page_count=doc.page_count)


def recognize(request):
    row = request['record']
    check_source(row)
    with fitz.open(row['render_path']) as doc:
        page = doc[row['page_start'] - 1]
        clip = clip_rect(page, row.get('crop'))
        text = page.get_text(clip=clip, sort=True)
        result = parse(text, str(Path(row['source']).parent))
        if len(text.strip()) < 20 or not all(result[k] for k in ('number', 'date', 'amount')):
            text, _ = ocr_text(page, clip)
            result = parse(text, str(Path(row['source']).parent))
            result['warnings'].append('OCR 识别，请核对数字和日期')
        return dict(record={**row, **result, 'reviewed': False, 'resolution': None})


def doctor():
    from rapidocr_onnxruntime import RapidOCR
    engine = RapidOCR(intra_op_num_threads=2, inter_op_num_threads=2)
    return dict(ocr=engine is not None, word=soffice_path() is not None, python=sys.version.split()[0],
                frozen=bool(getattr(sys, 'frozen', False)))
