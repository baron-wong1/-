"""Run the built worker protocol, OCR and Word conversions without a Python child."""
import json
import os
import subprocess
import sys
import tempfile
import zipfile
from pathlib import Path

import fitz
from openpyxl import load_workbook

exe = Path(sys.argv[1]).resolve()
resources = Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else exe.parent

def call(request):
    result = subprocess.run([str(exe)], input=json.dumps(request, ensure_ascii=False)+'\n',
                            capture_output=True, text=True, encoding='utf-8', timeout=180,
                            env={**os.environ,'INVOICE_RESOURCES':str(resources)})
    events = [json.loads(line) for line in result.stdout.splitlines() if line.startswith('{')]
    if result.returncode != 0 or not events or events[-1]['event'] != 'result':
        raise RuntimeError(f'Worker failed: {result.stderr[-3000:]} {events[-1:]}')
    return events[-1]['result']

health = call({'op':'doctor'})
assert health['ocr'] and health['frozen'], health
if os.name == 'nt':
    assert health['word'], 'Packaged LibreOffice missing'
with tempfile.TemporaryDirectory(prefix='invoice-smoke-') as directory:
    root=Path(directory)
    pdf=root/'synthetic.pdf'
    doc=fitz.open();page=doc.new_page(width=600,height=260)
    content='电子发票\n发票号码:00123456789012345678\n开票日期:2026年09月03日\n住宿服务\n价税合计(小写):￥123.45'
    page.insert_text((20,35),content,fontname='china-s',fontsize=15,lineheight=1.6)
    pix=page.get_pixmap(matrix=fitz.Matrix(2,2));scan=root/'scan.png';pix.save(scan)
    from PIL import Image
    jpeg=root/'scan.jpg'
    with Image.open(scan) as image:
        image.save(jpeg,quality=95)
    doc.save(pdf);doc.close()
    rows=call({'op':'import','paths':[str(pdf),str(scan),str(jpeg)],'cache':str(root/'cache')})['records']
    assert len(rows)==3 and all(r['number']=='00123456789012345678' and r['amount']=='123.45' for r in rows), rows
    row={**rows[0],'reviewed':True}
    result=call({'op':'export','records':[row],'person':'合成测试','month':'2026-09','destination':str(root/'out')})
    assert result['count']==1 and result['amount']=='123.45'
    wb=load_workbook(next(p for p in result['files'] if p.endswith('.xlsx')))
    assert wb.active['F3'].value=='00123456789012345678'
    if health['word']:
        # Generate DOCX then a legacy DOC in this platform's prepared runtime.
        source=root/'word.docx'
        from xml.sax.saxutils import escape
        paragraphs=''.join(f'<w:p><w:r><w:t>{escape(line)}</w:t></w:r></w:p>' for line in content.splitlines())
        with zipfile.ZipFile(source,'w') as z:
            z.writestr('[Content_Types].xml','<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
            z.writestr('_rels/.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
            z.writestr('word/document.xml',f'<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>{paragraphs}<w:sectPr/></w:body></w:document>')
        converted=call({'op':'import','paths':[str(source)],'cache':str(root/'cache')})['records']
        assert converted[0]['number']=='00123456789012345678' and not converted[0].get('error'),converted
        soffice=resources/'libreoffice/program/soffice.exe'
        if soffice.is_file():
            legacy=root/'legacy';legacy.mkdir()
            subprocess.run([str(soffice),'-env:UserInstallation='+(root/'profile').as_uri(),'--headless','--convert-to','doc:MS Word 97','--outdir',str(legacy),str(source)],check=True,timeout=120,capture_output=True)
            converted=call({'op':'import','paths':[str(legacy/'word.doc')],'cache':str(root/'cache')})['records']
            assert converted[0]['number']=='00123456789012345678' and not converted[0].get('error'),converted
print('Built core verified: offline OCR, PDF, Excel, identity checks; Word when bundled.')
