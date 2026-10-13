$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath (Split-Path -Parent $PSScriptRoot)
$env:OLLAMA_URL = 'http://127.0.0.1:11434'
$env:OLLAMA_MODEL = 'qwen3:8b'
$env:HF_HOME = Join-Path $PSScriptRoot 'data/huggingface'
$env:TEMP = Join-Path $PSScriptRoot 'data/tmp'
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
$ollama = Join-Path $env:LOCALAPPDATA 'Programs/Ollama/ollama.exe'
try { Invoke-RestMethod "$env:OLLAMA_URL/api/tags" -TimeoutSec 2 | Out-Null }
catch {
    Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden `
        -RedirectStandardOutput (Join-Path $PSScriptRoot 'data/ollama-out.log') `
        -RedirectStandardError (Join-Path $PSScriptRoot 'data/ollama-error.log') | Out-Null
    Start-Sleep -Seconds 10
}
Write-Host 'Backend: http://127.0.0.1:8000 | Model: qwen3:8b'
& (Join-Path $PSScriptRoot '.venv/Scripts/python.exe') -m uvicorn backend.app:app --host 127.0.0.1 --port 8000
