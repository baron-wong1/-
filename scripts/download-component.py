"""Bounded ranged download for large official build components, with TLS validation."""
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path
import re
import sys
import threading
import time
import urllib.parse
import urllib.request


def download(url, destination, chunk_size=1024 * 1024):
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != 'https' and not (parsed.scheme == 'http' and parsed.hostname == '127.0.0.1'):
        raise ValueError('Component download requires HTTPS')

    def fetch(start, end):
        failure = None
        for attempt in range(3):
            try:
                request = urllib.request.Request(url, headers={'User-Agent':'InvoiceAssistant-build/0.1',
                                                               'Range':f'bytes={start}-{end}'})
                with urllib.request.urlopen(request, timeout=30) as response:
                    header = response.headers.get('Content-Range', '')
                    match = re.fullmatch(r'bytes (\d+)-(\d+)/(\d+)', header)
                    if response.status != 206 or not match:
                        raise RuntimeError('Server does not support a bounded ranged download')
                    actual_start, actual_end, total = map(int, match.groups())
                    expected_end = min(end, total - 1)
                    if actual_start != start or actual_end != expected_end:
                        raise RuntimeError('Component range response mismatch')
                    data = response.read(expected_end - start + 2)
                    if len(data) != expected_end - start + 1:
                        raise RuntimeError('Component range was truncated')
                    return start, total, data
            except Exception as exc:
                failure = exc
                if attempt < 2:
                    time.sleep(attempt + 1)
        raise RuntimeError(f'Component chunk at {start} failed: {failure}')

    destination = Path(destination)
    temporary = destination.with_suffix(destination.suffix + '.partial')
    lock = threading.Lock()
    try:
        first_start, total, first = fetch(0, chunk_size - 1)
        if total <= 0 or total > 2 * 1024 ** 3:
            raise RuntimeError('Component size is outside supported bounds')
        with temporary.open('wb') as output:
            output.truncate(total)
            output.write(first)
            completed = len(first)
            print(f'Download component: {total // (1024 * 1024)} MiB, 1 MiB ranges', flush=True)
            with ThreadPoolExecutor(max_workers=6) as executor:
                futures = [executor.submit(fetch, start, min(start + chunk_size - 1, total - 1))
                           for start in range(chunk_size, total, chunk_size)]
                for index, future in enumerate(as_completed(futures)):
                    start, reported_total, data = future.result()
                    if reported_total != total:
                        raise RuntimeError('Component size changed during download')
                    with lock:
                        output.seek(start)
                        output.write(data)
                    completed += len(data)
                    if index % 20 == 0 or completed == total:
                        print(f'Downloaded {completed // (1024 * 1024)} / {total // (1024 * 1024)} MiB', flush=True)
        temporary.replace(destination)
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == '__main__':
    download(*sys.argv[1:3])
    print('Download complete; caller must verify pinned SHA-256 and Authenticode signature.')
