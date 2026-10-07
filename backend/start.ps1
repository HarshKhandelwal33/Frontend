$ErrorActionPreference = 'Stop'
$backendDirectory = $PSScriptRoot
$projectDirectory = Split-Path -Parent $backendDirectory
$pythonExecutable = Join-Path $backendDirectory '.venv/Scripts/python.exe'
Set-Location -LiteralPath $projectDirectory
if (-not (Test-Path -LiteralPath $pythonExecutable)) {
    python -m venv (Join-Path $backendDirectory '.venv')
    if ($LASTEXITCODE -ne 0) { throw 'Python 3.10+ is required to create the backend environment.' }
}
& $pythonExecutable -m pip install -r (Join-Path $backendDirectory 'requirements.txt')
if ($LASTEXITCODE -ne 0) { throw 'Backend dependency installation failed.' }
& $pythonExecutable -m uvicorn backend.app:app --host 127.0.0.1 --port 8000
