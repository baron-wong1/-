"""Build synthetic inputs for the Windows native integration test; never ship them."""
from pathlib import Path
import shutil
import sys
import zipfile
import fitz
from PIL import Image, ImageDraw, ImageFont

root = Path(sys.argv[1]) / 'fixtures'
root.mkdir(parents=True, exist_ok=True)

def invoice(path, number, amount, category, width=500, height=250, rotation=0):
    with fitz.open() as doc:
        page = doc.new_page(width=width, height=height)
        for y, text in enumerate([f'发票号码：{number}', '开票日期：2026年10月3日', category, f'价税合计（小写）￥{amount}']):
            page.insert_text((18, 35 + y * 28), text, fontname='china-s', fontsize=12)
        page.draw_rect(fitz.Rect(20, height - 65, 95, height - 15), color=(0.8, 0.1, 0.1))
        page.insert_text((25, height - 35), 'TEST', color=(0.8, 0.1, 0.1))
        page.set_rotation(rotation)
        doc.save(root / path)

invoice('invoice.pdf', '00123456789012345678', '123.45', '酒店住宿')
shutil.copyfile(root / 'invoice.pdf', root / 'copy.pdf')
invoice('long.pdf', '00888888888888888888', '10.10', '铁路客票', 220, 700)
invoice('rotated.pdf', '00999999999999999999', '1.00', '市内交通', 250, 500, 90)
with fitz.open(root / 'invoice.pdf') as source, fitz.open() as doc:
    doc.insert_pdf(source); doc.insert_pdf(source); doc.save(root / 'pages.pdf')
with fitz.open() as doc:
    page = doc.new_page(width=500, height=500)
    page.insert_text((20, 50), '发票号码：12345678', fontname='china-s', fontsize=14)
    page.insert_text((20, 280), '发票号码：87654321', fontname='china-s', fontsize=14)
    doc.save(root / 'multiple.pdf')
(root / 'bad.pdf').write_bytes(b'not a PDF')
image = Image.new('RGB', (600, 400), 'white')
draw = ImageDraw.Draw(image)
font = ImageFont.truetype('C:/Windows/Fonts/arial.ttf', 40)
draw.text((20, 40), 'PAYMENT RECEIPT', fill='black', font=font)
draw.text((20, 100), '12345678', fill='black', font=font)
image.save(root / 'receipt.png'); image.save(root / 'receipt.jpg')
image.save(root / 'ocr-english.png')
with zipfile.ZipFile(root / 'invoice.docx', 'w') as z:
    z.writestr('[Content_Types].xml', '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/></Types>')
    z.writestr('_rels/.rels', '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
    z.writestr('word/document.xml', '<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body><w:p><w:r><w:t>Synthetic invoice</w:t></w:r></w:p></w:body></w:document>')

