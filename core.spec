# Python core is intentionally a console worker: Electron consumes JSON on pipes.
from PyInstaller.utils.hooks import collect_all

datas, binaries, hiddenimports = collect_all('rapidocr_onnxruntime')
a = Analysis(['core/worker.py'], pathex=['core'], binaries=binaries, datas=datas,
             hiddenimports=hiddenimports, hookspath=[], runtime_hooks=[],
             excludes=['matplotlib', 'scipy', 'pandas', 'torch', 'tkinter', 'IPython', 'pytest'],
             noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name='invoice-core', debug=False,
          bootloader_ignore_signals=False, strip=False, upx=False, console=True)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name='invoice-core')
