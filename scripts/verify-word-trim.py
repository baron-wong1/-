"""Compare real DOCX/DOC conversion before/after pruning: text, pictures, tables and pagination."""
import hashlib
import json
import subprocess
import sys
import zipfile
from pathlib import Path

import fitz
from PIL import Image, ImageDraw

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'core'))
from invoice_assistant.importer import convert_word, digest, soffice_path

mode, directory = sys.argv[1:3]
root=Path(directory).resolve()
root.mkdir(parents=True, exist_ok=True)
source=root/'rich.docx'
if mode=='baseline':
    picture=root/'stamp.png'
    image=Image.new('RGB',(240,140),'white');draw=ImageDraw.Draw(image)
    draw.ellipse((10,10,230,130),outline='red',width=5)
    draw.text((90,65),'TEST SEAL',fill='red')
    image.save(picture)
    def para(text):
        from xml.sax.saxutils import escape
        return f'<w:p><w:r><w:rPr><w:rFonts w:ascii="Liberation Serif" w:eastAsia="Noto Sans CJK SC"/><w:sz w:val="24"/></w:rPr><w:t>{escape(text)}</w:t></w:r></w:p>'
    paragraphs=''.join(para(t) for t in ['电子发票 / Invoice', '发票号码:00123456789012345678',
                     '开票日期:2026年09月03日','价税合计(小写):￥123.45'])
    table='<w:tbl><w:tblPr><w:tblW w:w="8000" w:type="dxa"/><w:tblBorders>'+''.join(
        f'<w:{edge} w:val="single" w:sz="8" w:color="000000"/>' for edge in ('top','left','bottom','right','insideH','insideV'))+'</w:tblBorders></w:tblPr><w:tblGrid><w:gridCol w:w="4000"/><w:gridCol w:w="4000"/></w:tblGrid>'
    for cells in [('项目 / Item','金额 / Amount'),('住宿服务','123.45')]:
        table+='<w:tr>'+''.join('<w:tc><w:tcPr><w:tcW w:w="4000" w:type="dxa"/></w:tcPr>'+para(t)+'</w:tc>' for t in cells)+'</w:tr>'
    table+='</w:tbl>'
    drawing='''<w:p><w:r><w:drawing><wp:inline><wp:extent cx="2286000" cy="1333500"/><wp:docPr id="1" name="Synthetic seal"/><a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture"><pic:pic><pic:nvPicPr><pic:cNvPr id="1" name="stamp.png"/><pic:cNvPicPr/></pic:nvPicPr><pic:blipFill><a:blip r:embed="rId1"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="2286000" cy="1333500"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>'''
    hyphenation='''<w:p><w:pPr><w:ind w:right="8000"/><w:suppressAutoHyphens w:val="false"/></w:pPr><w:r><w:rPr><w:rFonts w:ascii="Liberation Serif"/><w:sz w:val="24"/><w:lang w:val="en-US"/></w:rPr><w:t>reimbursement organization accommodation transportation communication</w:t></w:r></w:p>'''
    body=paragraphs+table+drawing+'<w:p><w:r><w:br w:type="page"/></w:r></w:p>'+para('第二页附件 / Page two')+para('中文、English、数字 1234567890 与 ￥ 金额均须保留。')+hyphenation
    document=f'''<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"><w:body>{body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/></w:sectPr></w:body></w:document>'''
    with zipfile.ZipFile(source,'w') as archive:
        archive.writestr('[Content_Types].xml','<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Default Extension="png" ContentType="image/png"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/settings.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml"/></Types>')
        archive.writestr('_rels/.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>')
        archive.writestr('word/_rels/document.xml.rels','<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/image" Target="media/stamp.png"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings" Target="settings.xml"/></Relationships>')
        archive.writestr('word/document.xml',document)
        archive.writestr('word/settings.xml','<w:settings xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:autoHyphenation w:val="true"/></w:settings>')
        archive.writestr('word/media/stamp.png',picture.read_bytes())
    subprocess.run([str(soffice_path()),'-env:UserInstallation='+(root/'legacy-profile').as_uri(),
                    '--headless','--convert-to','doc:MS Word 97','--outdir',str(root),str(source)],
                   check=True,capture_output=True,timeout=120)

cache=root/mode
cache.mkdir(exist_ok=True)
result={}
for path in (source,root/'rich.doc'):
    pdf=convert_word(path,cache,digest(path))
    with fitz.open(pdf) as doc:
        text=''.join(p.get_text() for p in doc)
        assert len(doc)==2 and '00123456789012345678' in text and '住宿服务' in text and '第二页附件' in text, text
        assert doc[0].get_images(), 'Embedded image lost'
        second_page=doc[1].get_text()
        result[path.suffix]={'pages':len(doc),'text_sha256':hashlib.sha256(text.encode()).hexdigest(),
                            'hyphenation_marks':sum(second_page.count(c) for c in ('-', '\u00ad', '\u2010')),
                            'page_pixels_sha256':[hashlib.sha256(p.get_pixmap(matrix=fitz.Matrix(1.5,1.5),alpha=False).samples).hexdigest() for p in doc]}
snapshot=root/'baseline.json'
if mode=='baseline':
    snapshot.write_text(json.dumps(result,indent=2),encoding='utf-8')
else:
    assert result==json.loads(snapshot.read_text(encoding='utf-8')), 'Word conversion differs after runtime pruning'
print(f'Word {mode} verified: DOCX/DOC Chinese, table, image and 2-page layout; pixel and text hashes matched' if mode!='baseline' else 'Word baseline recorded: DOCX/DOC Chinese, table, image and 2-page layout')
print('Hyphenation marks:', {kind:data['hyphenation_marks'] for kind,data in result.items()})
