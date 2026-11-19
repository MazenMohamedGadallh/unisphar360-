# ==============================================================
# start-all.ps1 - start the FULL UniSphere stack with one command.
#
#   Python RAG API -> http://127.0.0.1:8000   (rag-chatbot/rag-chatbot/api.py)
#   .NET API       -> http://localhost:5080   (Properties/launchSettings.json)
#   React app      -> http://localhost:5173   (frontend/vite.config.ts)
#
# The .NET + React part reuses start-dev.ps1 unchanged, so this script
# only owns the Python service: it starts it, waits for GET /health,
# and stops that one process tree on shutdown (never anything else).
#
# Stop: press Ctrl+C in this window (or just close the window).
# Logs: rag-api.log / api-dev.log / vite-dev.log   (errors: *.err.log)
# ==============================================================

$root = $PSScriptRoot

$ragDir = Join-Path $root 'rag-chatbot\rag-chatbot'
$ragPy  = Join-Path $ragDir '.venv-new\Scripts\python.exe'
$ragLog = Join-Path $root 'rag-api.log'
$ragErr = Join-Path $root 'rag-api.err.log'

# True when something is already listening on the port (uvicorn binds
# 127.0.0.1, so the IPv4 probe is the one that matters here).
function Test-Port([int]$Port) {
    $probes = @(
        @{ Family = [System.Net.Sockets.AddressFamily]::InterNetwork;    Address = [System.Net.IPAddress]::Loopback },
        @{ Family = [System.Net.Sockets.AddressFamily]::InterNetworkV6; Address = [System.Net.IPAddress]::IPv6Loopback }
    )
    foreach ($probe in $probes) {
        try {
            $client = New-Object System.Net.Sockets.TcpClient($probe.Family)
            $client.Connect($probe.Address, $Port)
            $client.Close()
            return $true
        } catch {
            # Nothing listening on this address family - try the other one.
        }
    }
    return $false
}

# --- Fail fast on pre-flight problems --------------------------
if (-not (Test-Path $ragPy)) {
    Write-Host "Python venv not found:" -ForegroundColor Red
    Write-Host "  $ragPy" -ForegroundColor Red
    Write-Host 'Create it first:  python -m venv .venv-new  (inside rag-chatbot\rag-chatbot)' -ForegroundColor Red
    Write-Host 'then install requirements:  .\.venv-new\Scripts\pip install -r requirements.txt' -ForegroundColor Red
    exit 1
}

if (Test-Port 8000) {
    Write-Host 'Port 8000 is already in use - the RAG service may already be running.' -ForegroundColor Red
    Write-Host 'Stop that process, then run this script again.' -ForegroundColor Red
    exit 1
}

# --- Start the Python RAG service -------------------------------
Set-Content -Path $ragLog -Value ''
Set-Content -Path $ragErr -Value ''

Write-Host 'Starting UniSphere (full stack)...' -ForegroundColor Cyan
Write-Host '  Python RAG  http://127.0.0.1:8000'
Write-Host '  .NET API    http://localhost:5080'
Write-Host '  React app   http://localhost:5173'
Write-Host ''

$rag = Start-Process -FilePath $ragPy `
    -ArgumentList '-m', 'uvicorn', 'api:app', '--host', '127.0.0.1', '--port', '8000' `
    -WorkingDirectory $ragDir `
    -RedirectStandardOutput $ragLog -RedirectStandardError $ragErr `
    -PassThru -NoNewWindow

# --- Wait for GET /health (max 60 s) ---------------------------
$healthUp = $false
$deadline = (Get-Date).AddSeconds(60)
while ((-not $healthUp) -and ((Get-Date) -lt $deadline)) {
    if ($rag.HasExited) { break }
    try {
        $response = Invoke-WebRequest -Uri 'http://127.0.0.1:8000/health' -UseBasicParsing -TimeoutSec 2
        if ($response.StatusCode -eq 200) { $healthUp = $true }
    } catch {
        # Not answering yet - model/FAISS loading can take a few seconds.
        Start-Sleep -Milliseconds 500
    }
}

if (-not $healthUp) {
    Write-Host ''
    Write-Host 'RAG service FAILED to start.' -ForegroundColor Red
    if ($rag.HasExited) { Write-Host "  uvicorn exited early (code $($rag.ExitCode))." }
    else { Write-Host '  http://127.0.0.1:8000/health did not answer in 60 s.' }

    Write-Host ''
    Write-Host '--- rag-api.err.log (last 15 lines) ---' -ForegroundColor DarkGray
    Get-Content $ragErr -Tail 15 -ErrorAction SilentlyContinue

    if (-not $rag.HasExited) { $null = & taskkill.exe /PID $rag.Id /T /F 2>&1 }
    exit 1
}

Write-Host 'Python RAG service is up (http://127.0.0.1:8000/health).' -ForegroundColor Green
Write-Host ''

# --- Start .NET + React (reuses the approved dev script) --------
# start-dev.ps1 checks ports 5080/5173, starts both apps, waits for
# them and stops them on Ctrl+C. Our finally below stops Python so a
# Ctrl+C shuts down all three services - and only those three.
$devOk = $true
try {
    & (Join-Path $root 'start-dev.ps1')
    if (-not $?) { $devOk = $false }
} finally {
    if ($rag -and -not $rag.HasExited) {
        $null = & taskkill.exe /PID $rag.Id /T /F 2>&1
        Write-Host 'Stopped the Python RAG service.' -ForegroundColor Cyan
    }
}

if (-not $devOk) { exit 1 }
