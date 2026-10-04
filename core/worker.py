"""One operation per process. stdin request; UTF-8 JSON-lines events on stdout."""
import json
import os
import sys
import traceback
from invoice_assistant.importer import import_files, preview, recognize, doctor
from invoice_assistant.exporter import export_files
from invoice_assistant.models import reconcile


def emit(event, **data):
    print(json.dumps(dict(event=event, **data), ensure_ascii=False), flush=True)


def main():
    if hasattr(sys.stdin, 'reconfigure'):
        sys.stdin.reconfigure(encoding='utf-8')
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    try:
        request = json.loads(sys.stdin.readline())
        op = request['op']
        emit('started', pid=os.getpid())
        if op == 'import':
            result = import_files(request, emit)
        elif op == 'export':
            result = export_files(request, emit)
        elif op == 'reconcile':
            result = dict(records=reconcile(request['records']))
        elif op == 'preview':
            result = preview(request)
        elif op == 'recognize':
            result = recognize(request)
        elif op == 'doctor':
            result = doctor()
        else:
            raise ValueError('未知操作')
        emit('result', result=result)
    except Exception as exc:
        traceback.print_exc(file=sys.stderr)
        emit('error', message=str(exc))
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
