"""Project-local launcher used by Unity Play mode and the desktop startup script."""
import os
from pathlib import Path
import subprocess
import sys
import httpx

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / 'backend' / 'data'
DATA.mkdir(parents=True, exist_ok=True)
(DATA / 'tmp').mkdir(exist_ok=True)
os.chdir(ROOT)
os.environ.setdefault('HF_HOME', str(DATA / 'huggingface'))
os.environ['TEMP'] = os.environ['TMP'] = str(DATA / 'tmp')
os.environ.setdefault('OLLAMA_MODEL', 'qwen3:8b')
os.environ.setdefault('OLLAMA_URL', 'http://127.0.0.1:11434')

def reachable(url):
    try:
        with httpx.Client(timeout=2, trust_env=False) as client:
            return client.get(url).is_success
    except httpx.HTTPError:
        return False

def main():
    # A named mutex prevents duplicate editor launches racing to bind port 8000.
    if os.name == 'nt':
        import ctypes
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.CreateMutexW.argtypes = [ctypes.c_void_p, ctypes.c_bool, ctypes.c_wchar_p]
        kernel.CreateMutexW.restype = ctypes.c_void_p
        mutex = kernel.CreateMutexW(None, False, 'Local\\GearboxTrainingBackend8000')
        if not mutex:
            raise ctypes.WinError(ctypes.get_last_error())
        if ctypes.get_last_error() == 183:
            return
    if not reachable(os.environ['OLLAMA_URL'].rstrip('/') + '/api/tags'):
        ollama = Path(os.environ.get('LOCALAPPDATA', '')) / 'Programs/Ollama/ollama.exe'
        if ollama.is_file():
            with open(DATA / 'local-ollama.log', 'a', encoding='utf-8') as log:
                subprocess.Popen([str(ollama), 'serve'], stdout=log, stderr=log,
                                 creationflags=subprocess.CREATE_NO_WINDOW)
    if reachable('http://127.0.0.1:8000/health'):
        return
    with open(DATA / 'local-backend.log', 'a', encoding='utf-8') as log:
        result = subprocess.run([sys.executable, '-m', 'uvicorn', 'backend.app:app',
                                 '--host', '127.0.0.1', '--port', '8000'], stdout=log, stderr=log)
    sys.exit(result.returncode)

if __name__ == '__main__':
    main()
