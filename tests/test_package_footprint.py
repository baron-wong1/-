"""Windows console encoding must not block packaging a Chinese-named application."""
import json
import os
import subprocess
import sys
import zipfile
from pathlib import Path


def test_zip_inventory_handles_chinese_on_legacy_console(tmp_path):
    archive=tmp_path/'package.zip'
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED) as output:
        output.writestr('resources/使用说明.md','使用说明')
        output.writestr('resources/core/invoice-core.exe',b'fixture')
    report=tmp_path/'report.json'
    command=Path(__file__).resolve().parents[1]/'scripts/package-footprint.py'
    result=subprocess.run([sys.executable,str(command),'--zip',str(archive),'--label','windows_zip',
                           '--report',str(report)],capture_output=True,
                          env={**os.environ,'PYTHONIOENCODING':'cp1252','PYTHONUTF8':'0'})
    assert result.returncode==0, result.stderr.decode(errors='replace')
    assert '使用说明.md' in result.stdout.decode('utf-8')
    inventory=json.loads(report.read_text(encoding='utf-8'))['windows_zip']
    assert inventory['zip_bytes']==archive.stat().st_size
    assert inventory['groups']['core']['bytes']==7
