import re
import unicodedata
from datetime import date, datetime
from .models import CATEGORIES, money

NUMBER = re.compile(r'(?:发\s*票\s*(?:号\s*码|号)|票据号码)\s*[:：]?\s*((?:\d[ \t]*){8,30})(?!\d)')
DATE = re.compile(r'(?<!\d)(20\d{2})\s*[年/.-]\s*(\d{1,2})\s*[月/.-]\s*(\d{1,2})\s*日?')
AMOUNT = r'(\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)'


def parse(text, folder=''):
    text = unicodedata.normalize('NFKC', text).replace('﹕', ':')
    compact = re.sub(r'[ \t]', '', text)
    numbers = list(dict.fromkeys(re.sub(r'\s', '', m.group(1)) for m in NUMBER.finditer(text)))
    # 数电票 may place the 20-digit identifier on a separate line.
    if not numbers:
        numbers = list(dict.fromkeys(re.findall(r'(?<!\d)(\d{20})(?!\d)', compact)))
    warnings = []
    if len(numbers) > 1:
        warnings.append('本页出现多个发票号码，请裁剪拆分票据并逐张核对')
    invoice_date = None
    labelled = re.search(r'开票日期\s*[:：]?\s*([^\n]{0,40}(?:\n[^\n]{0,30})?)', compact)
    scope = labelled.group(1) if labelled else compact
    dates = []
    for match in DATE.finditer(scope):
        try:
            parsed = date(*map(int, match.groups())).isoformat()
            if parsed not in dates:
                dates.append(parsed)
        except ValueError:
            continue
    if dates:
        invoice_date = dates[0]
        if not labelled:
            warnings.append('日期未带开票标签，请核对是否为行程或交易日期')
    amounts = []
    patterns = [
        r'(?:小写)[)）]?[:：]?\s*[¥￥]?\s*' + AMOUNT,
        r'价税合计(?:[^\d¥￥\n]{0,15})[:：]?\s*[¥￥]?\s*' + AMOUNT,
        r'(?:票价|退票费|实收金额|合计金额|含税金额)[:：]?\s*[¥￥]?\s*' + AMOUNT,
    ]
    for pattern in patterns:
        found = re.findall(pattern, compact)
        if found:
            amounts = list(dict.fromkeys(found))
            break
    if not amounts:
        found = re.findall(r'[¥￥]\s*' + AMOUNT, compact)
        if len(set(found)) == 1:
            amounts = found
            warnings.append('未找到金额标签，请核对是否为价税合计或票价')
    amount = None
    if len(amounts) == 1:
        try:
            amount = str(money(amounts[0]))
        except ValueError:
            pass
    elif len(amounts) > 1:
        warnings.append('本页存在多个候选金额，请人工核对')
    if len(numbers) > 1:
        # Never silently assign the first ticket's fields to a multi-ticket page.
        amount = invoice_date = None
    kind = 'invoice'
    if not numbers and any(t in compact for t in ('支付成功', '付款成功', '交易详情', '转账成功', '微信支付', '支付宝')):
        kind = 'attachment'
        warnings.append('疑似付款凭证，请确认作为附件，不计入金额')
    category = classify(compact, folder)
    travel = re.search(r'(20\d{2}[-/]\d{1,2}[-/]\d{1,2})\s*(\d{1,2}:\d{2})', text)
    return dict(number=numbers[0] if len(numbers) == 1 else '', date=invoice_date, amount=amount,
                kind=kind, category=category, warnings=warnings, requires_split=len(numbers) > 1,
                travel_time=normalize_travel(travel.group(0)) if travel else '')


def classify(text, folder):
    if any(word in text for word in ('退票', '退票费')):
        return '退票费'
    if any(word in text for word in ('住宿', '酒店', '宾馆', '客房', '房费')):
        return '住宿'
    if any(word in text for word in ('铁路', '高铁', '动车', '航空', '机票', '民航', '火车')):
        return '车票'
    if any(word in text for word in ('出租', '网约车', '滴滴', '市内交通', '地铁', '公交')):
        return '市交'
    for category in CATEGORIES[:-1]:
        if category in folder or (category == '退票费' and '退票' in folder):
            return category
    return '其他'


def normalize_travel(value):
    try:
        return datetime.strptime(value.replace('/', '-'), '%Y-%m-%d %H:%M').isoformat(timespec='minutes')
    except ValueError:
        return ''
