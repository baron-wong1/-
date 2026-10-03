import sys
import zipfile
from pathlib import Path

import fitz
import pytest


@pytest.fixture
def pdf_factory(tmp_path):
    def make(name='invoice.pdf', number='00123456789012345678', amount='123.45', day='2026年09月03日',
             category='住宿服务', pages=1, size=(600, 260), text=None):
        path = tmp_path / name
        doc = fitz.open()
        for i in range(pages):
            page = doc.new_page(width=size[0], height=size[1])
            content = text if text is not None else f'电子发票\n发票号码: {number}\n开票日期: {day}\n{category}\n价税合计(小写): ￥{amount}'
            page.insert_text((20, 35), content, fontname='china-s', fontsize=15, lineheight=1.6)
        doc.save(path)
        doc.close()
        return path
    return make


@pytest.fixture
def docx_factory(tmp_path):
    def make(name='invoice.docx'):
        from xml.sax.saxutils import escape
        path = tmp_path / name
        paragraphs = ''.join(f'<w:p><w:r><w:t>{escape(t)}</w:t></w:r></w:p>' for t in
                             ['电子发票', '发票号码: 00123456789012345678', '开票日期: 2026年09月03日', '住宿服务', '价税合计(小写): ￥123.45'])
        with zipfile.ZipFile(path, 'w') as z:
            z.writestr('[Content_Types].xml', '<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
            z.writestr('_rels/.rels', '<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
            z.writestr('word/document.xml', f'<?xml version="1.0"?><w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{paragraphs}<w:sectPr/></w:body></w:document>')
        return path
    return make
