"""Package only managed application binaries, never a private runtime or model."""
from pathlib import Path
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile

root = Path(__file__).resolve().parents[1]
source = root / 'build/lightweight'
release = root / 'release'
target = release / 'lightweight'
if target.exists():
    shutil.rmtree(target)
target.mkdir(parents=True)
for file in source.iterdir():
    if file.suffix.lower() in {'.exe', '.dll', '.config'}:
        shutil.copy2(file, target / file.name)
assert (target / '发票整理助手.exe').is_file()
assert not any(p.suffix == '.winmd' for p in target.iterdir())
shutil.copy2(root / 'docs/使用说明.md', target / '使用说明.md')
notices = target / 'licenses'
notices.mkdir()
shutil.copy2(root / 'LICENSE', notices / 'Application-AGPL-3.0.txt')
for name in ('MIT.txt', 'Apache-2.0.txt'):
    shutil.copy2(root / 'lightweight/licenses' / name, notices / name)

assets = json.loads((root / 'lightweight/obj/project.assets.json').read_text())
folders = [Path(p) for p in assets['packageFolders']]
components = []
for package, info in assets['libraries'].items():
    if info['type'] != 'package':
        continue
    runtime = assets['targets']['.NETFramework,Version=v4.8'][package].get('runtime', {})
    if not any(Path(p).name.lower() in {f.name.lower() for f in target.glob('*.dll')} for p in runtime):
        continue
    directory = next((folder / info['path'] for folder in folders if (folder / info['path']).exists()), None)
    if directory is None:
        raise RuntimeError('Missing package notice: ' + package)
    metadata = ET.parse(next(directory.glob('*.nuspec'))).getroot()
    def field(key):
        return next((node.text or '' for node in metadata.iter() if node.tag.split('}')[-1] == key), '')
    license_text = field('license')
    if not license_text and field('licenseUrl') == 'https://github.com/dotnet/corefx/blob/master/LICENSE.TXT':
        license_text = 'MIT'
    if license_text not in {'MIT', 'Apache-2.0'}:
        raise RuntimeError('Review dependency license: ' + package + ' ' + license_text)
    components.append({'package': package, 'license': license_text, 'copyright': field('copyright'), 'authors': field('authors'), 'project': field('projectUrl')})
(notices / 'components.json').write_text(json.dumps(components, ensure_ascii=False, indent=2), encoding='utf-8')
(notices / 'SOURCE.txt').write_text('Source and build instructions: https://github.com/baron-wong1/-\nVersion: 0.2.0\nSystem .NET Framework, Windows OCR/PDF services and installed Office are not redistributed.\n', encoding='utf-8')
zip_path = release / 'InvoiceAssistant-0.2.0-Light-Windows-x64.zip'
with zipfile.ZipFile(zip_path, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for path in sorted(target.rglob('*')):
        if path.is_file():
            z.write(path, path.relative_to(target))
files = [{'name': str(p.relative_to(target)), 'bytes': p.stat().st_size} for p in sorted(target.rglob('*')) if p.is_file()]
report = {'version': '0.2.0', 'zip_bytes': zip_path.stat().st_size, 'unpacked_bytes': sum(f['bytes'] for f in files), 'files': files}
if report['zip_bytes'] > 10_000_000:
    raise RuntimeError('Lightweight ZIP exceeds 10 MB budget')
(release / 'footprint.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
sha = hashlib.sha256(zip_path.read_bytes()).hexdigest()
(release / 'SHA256.txt').write_text(sha + '  ' + zip_path.name + '\n', encoding='utf-8')
print(json.dumps({k: v for k, v in report.items() if k != 'files'}, ensure_ascii=True))
