from __future__ import annotations

import re
from datetime import date
from decimal import Decimal, InvalidOperation
from pathlib import Path

CATEGORIES = ['住宿', '车票', '市交', '退票费', '其他']
SUPPORTED = {'.pdf', '.docx', '.doc', '.jpg', '.jpeg', '.png'}


def money(value):
    if value is None or str(value).strip() == '':
        return None
    try:
        amount = Decimal(str(value).replace(',', '').strip())
        if not amount.is_finite() or amount < 0 or amount > Decimal('999999999.99'):
            raise ValueError('金额必须在 0 至 999999999.99 之间')
        if amount != amount.quantize(Decimal('0.01')):
            raise ValueError('金额最多两位小数')
        return amount.quantize(Decimal('0.01'))
    except InvalidOperation as exc:
        raise ValueError('金额格式无效') from exc


def valid_date(value):
    try:
        return date.fromisoformat(value).isoformat()
    except (ValueError, TypeError):
        return None


def validate(record):
    issues = []
    if record.get('error'):
        return [record['error']]
    if record.get('kind') not in ('invoice', 'attachment'):
        issues.append('请确认是发票还是付款附件')
    if not record.get('category', '').strip():
        issues.append('请选择分类')
    if record.get('kind') == 'invoice':
        if not valid_date(record.get('date')):
            issues.append('请核对开票日期')
        number = record.get('number', '')
        if not isinstance(number, str) or not re.fullmatch(r'\d{8,30}', number):
            issues.append('请核对发票号码（8–30 位数字）')
        try:
            if money(record.get('amount')) is None:
                issues.append('请核对票面金额')
        except ValueError as exc:
            issues.append(str(exc))
    if not record.get('reviewed') and record.get('warnings'):
        issues.append('请查看原件并确认识别提示')
    start, end, count = record.get('page_start', 1), record.get('page_end', 1), record.get('page_count', 1)
    if not isinstance(start, int) or not isinstance(end, int) or not 1 <= start <= end <= count:
        issues.append('页范围超出原文件')
    if record.get('requires_split') and not record.get('crop'):
        issues.append('本页包含多张票据，请裁剪后拆分并分别核对')
    crop = record.get('crop')
    if crop is not None:
        if not isinstance(crop, list) or len(crop) != 4 or any(not isinstance(x, (float, int)) for x in crop):
            issues.append('裁剪范围无效')
        elif not (0 <= crop[0] < crop[2] <= 1 and 0 <= crop[1] < crop[3] <= 1):
            issues.append('裁剪范围必须在页面内')
    if record.get('resolution') not in (None, 'keep'):
        issues.append('重复项确认值无效')
    return issues


def reconcile(records):
    """Recompute statuses after every edit; never trust a UI-provided status."""
    result = [dict(row) for row in records]
    by_number = {}
    for row in result:
        row['issues'] = validate(row)
        row['duplicate_of'] = None
        row['status'] = 'error' if row.get('error') else ('pending' if row['issues'] else 'ready')
        if row.get('kind') == 'invoice' and isinstance(row.get('number'), str) and re.fullmatch(r'\d{8,30}', row['number']):
            by_number.setdefault(row['number'], []).append(row)
    for group in by_number.values():
        if len(group) < 2:
            continue
        def fields(r):
            try:
                amount = money(r.get('amount'))
            except ValueError:
                amount = None
            return r.get('date'), amount, r.get('category'), r.get('kind')
        complete = all(not r['issues'] for r in group)
        same = complete and len({fields(r) for r in group}) == 1
        if same:
            # Keep the first usable record; contents remain visible in the table.
            for row in group[1:]:
                row['status'] = 'duplicate'
                row['duplicate_of'] = group[0]['id']
                row['issues'] = ['同号且日期、金额一致，导出时自动排除']
        else:
            unresolved = [r for r in group if r.get('resolution') != 'keep']
            if unresolved:
                for row in group:
                    row['status'] = 'conflict'
                    row['issues'] = list(dict.fromkeys(row['issues'] + ['同号票据字段冲突，请修订、移除或逐项确认保留']))
    return result


def sort_key(row):
    return (row.get('date') or '9999-12-31', row.get('travel_time') or '', row.get('number') or '', row['id'])


def safe_name(value):
    name = re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', value).strip(' .')[:70] or '其他'
    if name.upper().split('.')[0] in {'CON', 'PRN', 'AUX', 'NUL', *(f'COM{i}' for i in range(1, 10)), *(f'LPT{i}' for i in range(1, 10))}:
        name = '_' + name
    return name


def inside(path, directory):
    try:
        Path(path).resolve().relative_to(Path(directory).resolve())
        return True
    except ValueError:
        return False
