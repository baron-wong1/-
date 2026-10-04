"""Exercise the boundary between disposable UI assets and required rendering resources."""
import importlib.util
from pathlib import Path

import pytest

spec=importlib.util.spec_from_file_location('trim_runtime',Path(__file__).resolve().parents[1]/'scripts/trim-word-runtime.py')
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def test_trim_requires_windows_office_root(tmp_path):
    with pytest.raises(ValueError,match='Windows LibreOffice'):
        module.trim(tmp_path)


def test_trim_preserves_filters_fonts_licenses_and_default_resources(tmp_path):
    kept=['program/soffice.exe','program/swlo.dll','program/python3.dll','LICENSE',
          'share/fonts/truetype/NotoSansCJKsc-Regular.otf','share/registry/writer.xcd',
          'share/config/images_colibre.zip','program/resource/en-US/messages.mo',
          'program/resource/zh-CN/messages.mo','share/extensions/dict-en/hyph_en_US.dic',
          'share/extensions/dict-en/LICENSE','share/extensions/dict-en/dictionaries.xcu']
    removed=['share/gallery/sound.wav','share/template/sample.ott','program/python-core-3.12/lib/a.py',
             'share/config/images_breeze.zip','program/resource/fr/messages.mo','LibreOffice.msi',
             'share/extensions/dict-en/en_US.dic','share/extensions/dict-en/en_US.aff',
             'share/extensions/dict-de/th_de_DE_v2.dat','share/extensions/dict-de/th_de_DE_v2.idx']
    for name in kept+removed:
        path=tmp_path/name
        path.parent.mkdir(parents=True,exist_ok=True)
        path.write_bytes(b'fixture')
    result=module.trim(tmp_path)
    assert all((tmp_path/name).read_bytes()==b'fixture' for name in kept)
    assert all(not (tmp_path/name).exists() for name in removed)
    assert result['removed_bytes']==len(removed)*len(b'fixture')
    assert result['before_bytes']-result['after_bytes']==result['removed_bytes']
