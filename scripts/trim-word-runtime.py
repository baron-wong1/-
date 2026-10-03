"""Remove named interactive resources; retain all rendering/filter DLLs, fonts and licenses."""
import json
import re
import shutil
import sys
from pathlib import Path


def size(path):
    return sum(p.stat().st_size for p in path.rglob('*') if p.is_file()) if path.is_dir() else path.stat().st_size


def trim(root):
    root = Path(root).resolve()
    if not (root / 'program/soffice.exe').is_file():
        raise ValueError('Expected extracted Windows LibreOffice runtime')
    before = size(root)
    candidates = [(root / name, reason) for name, reason in {
        'share/gallery': 'Interactive gallery assets; DOC/DOCX images are embedded in the document',
        'share/template': 'New-document templates; input documents carry their own styles',
        'share/autotext': 'Interactive AutoText insertion',
        'share/wizards': 'Interactive document creation wizards',
        'share/Scripts': 'Optional scripting examples; macros are disabled',
        'share/extensions/nlpsolver': 'Spreadsheet optimization extension',
        'share/extensions/wiki-publisher': 'Interactive publishing extension',
    }.items()]
    candidates.extend((p, 'Office scripting Python stdlib; conversion uses C++ filters with macros disabled')
                      for p in (root / 'program').glob('python-core-*') if p.is_dir())
    candidates.extend((p, 'Alternative interactive icon theme; retain default Colibre theme')
                      for p in (root / 'share/config').glob('images_*.zip') if 'colibre' not in p.name.lower())
    resources = root / 'program/resource'
    if resources.exists():
        candidates.extend((p, 'Unused Office UI translation') for p in resources.iterdir()
                          if p.is_dir() and re.fullmatch(r'[a-z]{2}(?:[-_][A-Za-z0-9]+)*', p.name)
                          and not p.name.lower().startswith(('en', 'zh')))
    removed = []
    for path, reason in candidates:
        if not path.exists():
            continue
        if path.is_symlink() or not path.resolve().is_relative_to(root):
            raise ValueError('Unexpected runtime resource link')
        removed.append({'path':path.relative_to(root).as_posix(), 'bytes':size(path), 'reason':reason})
        if path.is_dir():
            shutil.rmtree(path)
        else:
            path.unlink()
    after = size(root)
    return {'before_bytes':before,'after_bytes':after,'removed_bytes':before-after,'removed':removed}


if __name__ == '__main__':
    report = trim(sys.argv[1])
    Path(sys.argv[2]).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(report, ensure_ascii=False, indent=2))
