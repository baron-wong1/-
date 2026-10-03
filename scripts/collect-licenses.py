"""Copy installed dependency license notices to the distribution, fail if missing."""
import json
import shutil
import urllib.request
from importlib.metadata import distributions
from pathlib import Path

root = Path(__file__).resolve().parents[1]
output = root / 'vendor/licenses'
output.mkdir(parents=True, exist_ok=True)
shutil.copyfile(root / 'LICENSE', output / 'APPLICATION-AGPL-3.0.txt')
shutil.copyfile(root / 'docs/THIRD_PARTY_NOTICES.md', output / 'THIRD_PARTY_NOTICES.md')
manifest = []
for dist in distributions():
    name = dist.metadata['Name']
    files = [p for p in dist.files or [] if any(word in str(p).lower() for word in ('license', 'copying', 'notice'))
             and str(p).lower().endswith(('.txt', '.md', 'license', 'copying', 'notice'))]
    if not files:
        continue
    directory = output / name
    directory.mkdir(exist_ok=True)
    for index, file in enumerate(files):
        source = dist.locate_file(file)
        if source.is_file():
            shutil.copyfile(source, directory / f'{index:02d}-{Path(file).name}')
    manifest.append({'name':name,'version':dist.version,'license':dist.metadata.get('License')})
# Some wheels omit their license files; include upstream copies from pinned versions.
upstream = {
    'PyMuPDF-AGPL-3.0.txt': 'https://raw.githubusercontent.com/pymupdf/PyMuPDF/1.26.6/COPYING',
    'RapidOCR-Apache-2.0.txt': 'https://raw.githubusercontent.com/RapidAI/RapidOCR/v1.4.4/LICENSE',
    'PaddleOCR-models-Apache-2.0.txt': 'https://raw.githubusercontent.com/PaddlePaddle/PaddleOCR/v2.7.3/LICENSE',
    'openpyxl-MIT.txt': 'https://raw.githubusercontent.com/ericgazoni/openpyxl/master/LICENCE.rst',
}
for filename, url in upstream.items():
    with urllib.request.urlopen(url, timeout=60) as response:
        data = response.read()
    if len(data) < 100:
        raise RuntimeError(f'Missing license: {url}')
    (output / filename).write_bytes(data)
for name in ('electron', 'react', 'react-dom', 'lucide-react'):
    package = root / 'node_modules' / name
    license_files = list(package.glob('LICENSE*'))
    if not license_files:
        raise RuntimeError(f'Missing Node dependency license: {name}')
    folder = output / name
    folder.mkdir(exist_ok=True)
    for file in license_files:
        shutil.copyfile(file, folder / file.name)
(output / 'python-dependencies.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
