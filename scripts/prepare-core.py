"""Stage the verified image-only OpenCV wheel without changing development dependencies."""
import hashlib
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path, PurePosixPath

root = Path(__file__).resolve().parents[1]
wheel_directory = root / 'build/headless-wheels'
wheel_directory.mkdir(parents=True, exist_ok=True)
subprocess.run([sys.executable, '-m', 'pip', 'download', '--only-binary=:all:', '--no-deps',
                'opencv-python-headless==5.0.0.93', '--dest', str(wheel_directory)], check=True)
# Independent PyPI release hashes, pinned alongside the version.
hashes = {
    'opencv_python_headless-5.0.0.93-cp37-abi3-win_amd64.whl': '829717b6a95554f273e49e357cee3b3a2a26b6f4842fbc1bed2b45bdd8f87e0e',
    'opencv_python_headless-5.0.0.93-cp37-abi3-manylinux_2_28_x86_64.whl': 'ed709fdf9aa0bd1f2ed8549e71d19449b03a675bb581eb292285f6861953be37',
    'opencv_python_headless-5.0.0.93-cp37-abi3-macosx_13_0_arm64.whl': '030ca5e0837a2963ab36ef896baa9767eb8d2b83353fb28af5a521e40dd8756f',
    'opencv_python_headless-5.0.0.93-cp37-abi3-macosx_14_0_x86_64.whl': '1e55af3abfb462eeeabe5c775f12bdb36216d8a93a3583d69e6bd6e1d6ba7d00',
}
from packaging.tags import sys_tags
from packaging.utils import parse_wheel_filename
compatible = set(sys_tags())
wheels = [p for p in wheel_directory.glob('*.whl') if parse_wheel_filename(p.name)[3] & compatible]
if len(wheels) != 1:
    raise RuntimeError('Expected one compatible headless OpenCV wheel')
wheel = wheels[0]
if wheel.name not in hashes or hashlib.sha256(wheel.read_bytes()).hexdigest() != hashes[wheel.name]:
    raise RuntimeError('Headless OpenCV wheel does not match its pinned release SHA-256')
target = root / 'build/core-deps'
if target.exists():
    shutil.rmtree(target)
target.mkdir()
with zipfile.ZipFile(wheel) as archive:
    for name in archive.namelist():
        path = PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts:
            raise RuntimeError('Unsafe wheel member')
    archive.extractall(target)
licenses = root / 'vendor/licenses/OpenCV-headless'
licenses.mkdir(parents=True, exist_ok=True)
for source in target.rglob('LICENSE*'):
    if source.is_file():
        shutil.copyfile(source, licenses / source.name)
(licenses / 'wheel-sha256.txt').write_text(f'{hashes[wheel.name]}  {wheel.name}\n', encoding='utf-8')
print(f'Staged verified headless OpenCV: {wheel.name}')
