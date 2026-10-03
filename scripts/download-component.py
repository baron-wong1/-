"""Streaming HTTPS fallback for Windows TLS backend differences. No TLS bypass."""
import shutil
import sys
import urllib.request
from pathlib import Path

url, destination = sys.argv[1:3]
request = urllib.request.Request(url, headers={'User-Agent':'InvoiceAssistant-build/0.1'})
with urllib.request.urlopen(request, timeout=30) as response, Path(destination).open('wb') as output:
    shutil.copyfileobj(response, output, 1024 * 1024)
print('Official component download complete; caller must verify pinned hash and signature.')
