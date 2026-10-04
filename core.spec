# Python core is intentionally a console worker: Electron consumes JSON on pipes.
from PyInstaller.utils.hooks import collect_all
from pathlib import Path
import sys

headless = Path(SPECPATH) / 'build/core-deps'
if not (headless / 'cv2').is_dir():
    raise RuntimeError('Run scripts/prepare-core.py before packaging the core')
sys.path.insert(0, str(headless))

datas, binaries, hiddenimports = collect_all('rapidocr_onnxruntime')
a = Analysis(['core/worker.py'], pathex=[str(headless), 'core'], binaries=binaries, datas=datas,
             hiddenimports=hiddenimports, hookspath=[], runtime_hooks=[],
             excludes=['matplotlib', 'scipy', 'pandas', 'torch', 'tkinter', 'IPython', 'pytest', 'fontTools', 'lxml'],
             noarchive=False)
extensions = [entry for entry in a.binaries if Path(entry[0].replace('\\', '/')).name.startswith('cv2')
              and entry[0].endswith(('.pyd', '.so'))]
if not extensions or any(not Path(entry[1]).resolve().is_relative_to(headless.resolve()) for entry in extensions):
    raise RuntimeError('OpenCV native extension must come from the verified headless wheel')
# OpenCV loads this Windows plugin only for VideoCapture/VideoWriter. OCR never uses video.
before = len(a.binaries)
a.binaries = [entry for entry in a.binaries
              if not Path(entry[0].replace('\\', '/')).name.lower().startswith('opencv_videoio_ffmpeg')]
print(f'Excluded {before - len(a.binaries)} optional OpenCV video plugins')
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name='invoice-core', debug=False,
          bootloader_ignore_signals=False, strip=False, upx=False, console=True)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name='invoice-core')
