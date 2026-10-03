"""Produce a reproducible size inventory without including any document contents."""
import argparse
import json
import zipfile
from collections import defaultdict
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--root')
parser.add_argument('--zip')
parser.add_argument('--label', required=True)
parser.add_argument('--report', required=True)
args = parser.parse_args()
entries = []
if args.root:
    root = Path(args.root)
    for path in root.rglob('*'):
        if path.is_file():
            entries.append({'path':path.relative_to(root).as_posix(),'bytes':path.stat().st_size})
    groups = defaultdict(int)
    for entry in entries:
        parts=entry['path'].split('/')
        groups['/'.join(parts[:2]) if len(parts)>2 else parts[0]] += entry['bytes']
    result = {'bytes':sum(e['bytes'] for e in entries), 'groups':dict(sorted(groups.items(),key=lambda x:-x[1])),
              'largest_files':sorted(entries,key=lambda e:-e['bytes'])[:20]}
else:
    path = Path(args.zip)
    groups=defaultdict(lambda:{'bytes':0,'compressed_bytes':0})
    with zipfile.ZipFile(path) as archive:
        for entry in archive.infolist():
            parts=entry.filename.replace('\\','/').split('/')
            if 'resources' in parts:
                rest=parts[parts.index('resources')+1:]
                group=rest[0] if rest else 'electron'
            else:
                group='electron'
            groups[group]['bytes'] += entry.file_size
            groups[group]['compressed_bytes'] += entry.compress_size
    result={'zip_bytes':path.stat().st_size,'groups':dict(groups)}
output=Path(args.report)
report=json.loads(output.read_text(encoding='utf-8')) if output.exists() else {}
report[args.label]=result
output.parent.mkdir(parents=True,exist_ok=True)
output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(args.label, json.dumps(result,ensure_ascii=False,indent=2))
