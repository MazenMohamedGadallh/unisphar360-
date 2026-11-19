# ==============================================================
# start-dev.ps1 - start BOTH UniSphere apps with one command.
#
#   .NET API   ->  http://localhost:5080  (Properties/launchSettings.json)
#   React app  ->  http://localhost:5173  (frontend/vite.config.ts)
#
# Stop: press Ctrl+C in this window (or just close the window).
# Logs: api-dev.log / vite-dev.log   (errors: *.err.log)
# ==============================================================

$root = $PSScriptRoot

$apiLog = Join-Path $root 'api-dev.log'
$webLog = Join-Path $root 'vite-dev.log'
$apiErr = Join-Path $root 'api-dev.err.log'
$webErr = Join-Path $root 'vite-dev.err.log'

# True when something is already listening on the port.
# Vite listens on ::1 (IPv6 loopback) while the API listens on
# 0.0.0.0 (IPv4), so probe each family with a matching socket.
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

# --- Fail fast if a previous run is still holding a port -----
foreach ($port in @(5080, 5173)) {
    if (Test-Port $port) {
        Write-Host "Port $port is already in use - another instance is probably still running." -ForegroundColor Red
        Write-Host 'Close its window (or stop that process), then run this script again.' -ForegroundColor Red
        exit 1
    }
}

# --- Repair npm's .cmd shims (known Windows quirk) ------------
# This repo lives in a path containing '&' (ML&DL). npm generates
# .cmd shims with the unquoted line: SET dp0=%~dp0
# After expansion cmd cuts the path at the '&', so "npm run dev"
# fails with "Cannot find module 'D:\vite\bin\vite.js'".
# Re-quote it here (idempotent) so startup keeps working even
# after a fresh "npm install" regenerates the shims.
$binDir = Join-Path $root 'frontend\node_modules\.bin'
if (Test-Path $binDir) {
    foreach ($shim in (Get-ChildItem $binDir -Filter '*.cmd')) {
        $text = [IO.File]::ReadAllText($shim.FullName)
        if ($text.Contains('SET dp0=%~dp0')) {
            [IO.File]::WriteAllText($shim.FullName, $text.Replace('SET dp0=%~dp0', 'SET "dp0=%~dp0"'))
            Write-Host "Repaired npm shim: $($shim.Name)" -ForegroundColor Yellow
        }
    }
}

# --- Start both apps ------------------------------------------
foreach ($f in @($apiLog, $webLog, $apiErr, $webErr)) { Set-Content -Path $f -Value '' }

Write-Host 'Starting UniSphere...' -ForegroundColor Cyan
Write-Host '  .NET API   http://localhost:5080'
Write-Host '  React app  http://localhost:5173'
Write-Host ''

# 1) .NET API (port 5080 comes from the launch profile)
$api = Start-Process -FilePath 'dotnet' `
    -ArgumentList 'run', '--launch-profile', 'UniSphere.Api' `
    -WorkingDirectory $root `
    -RedirectStandardOutput $apiLog -RedirectStandardError $apiErr `
    -PassThru -NoNewWindow

# 2) React dev server (npm run dev -> vite, port pinned to 5173)
$web = Start-Process -FilePath 'cmd.exe' `
    -ArgumentList '/c', 'npm', 'run', 'dev' `
    -WorkingDirectory (Join-Path $root 'frontend') `
    -RedirectStandardOutput $webLog -RedirectStandardError $webErr `
    -PassThru -NoNewWindow

# --- Wait until both ports answer (max 120 s) ------------------
$deadline = (Get-Date).AddSeconds(120)
$apiUp = $false
$webUp = $false
while ((-not ($apiUp -and $webUp)) -and ((Get-Date) -lt $deadline)) {
    if ($api.HasExited -or $web.HasExited) { break }
    if (-not $apiUp) { $apiUp = Test-Port 5080 }
    if (-not $webUp) { $webUp = Test-Port 5173 }
    if (-not ($apiUp -and $webUp)) { Start-Sleep -Milliseconds 500 }
}

# --- Startup failed? Show why, then clean up -------------------
if ($api.HasExited -or $web.HasExited -or -not $apiUp -or -not $webUp) {
    Write-Host ''
    Write-Host 'Startup FAILED.' -ForegroundColor Red
    if ($api.HasExited) { Write-Host "  .NET API exited early (code $($api.ExitCode))." }
    if ($web.HasExited) { Write-Host "  React app exited early (code $($web.ExitCode))." }
    if (-not $apiUp -and -not $api.HasExited) { Write-Host '  .NET API did not answer on port 5080 in time.' }
    if (-not $webUp -and -not $web.HasExited) { Write-Host '  React app did not answer on port 5173 in time.' }

    Write-Host ''
    Write-Host '--- api-dev.err.log (last 15 lines) ---' -ForegroundColor DarkGray
    Get-Content $apiErr -Tail 15 -ErrorAction SilentlyContinue
    Write-Host '--- vite-dev.err.log (last 15 lines) ---' -ForegroundColor DarkGray
    Get-Content $webErr -Tail 15 -ErrorAction SilentlyContinue

    foreach ($proc in @($api, $web)) {
        if ($proc -and -not $proc.HasExited) { $null = & taskkill.exe /PID $proc.Id /T /F 2>&1 }
    }
    exit 1
}

# --- Both are up ----------------------------------------------
Write-Host 'Both apps are running:' -ForegroundColor Green
Write-Host '  Open in browser:  http://localhost:5173'
Write-Host '  Register page:    http://localhost:5173/register'
Write-Host '  API base URL:     http://localhost:5080  (frontend/.env)'
Write-Host ''
Write-Host 'Logs: api-dev.log / vite-dev.log  (errors: *.err.log)'
Write-Host 'Press Ctrl+C to stop both apps.'
Write-Host ''

# --- Stay alive until Ctrl+C or a crash ------------------------
try {
    while (-not ($api.HasExited -or $web.HasExited)) {
        Start-Sleep -Seconds 1
    }
    if ($api.HasExited) { Write-Host ".NET API stopped (code $($api.ExitCode))." -ForegroundColor Yellow }
    if ($web.HasExited) { Write-Host "React app stopped (code $($web.ExitCode))." -ForegroundColor Yellow }
} finally {
    # Stop anything still running (Ctrl+C, a crash, or port kill).
    foreach ($proc in @($api, $web)) {
        if ($proc -and -not $proc.HasExited) { $null = & taskkill.exe /PID $proc.Id /T /F 2>&1 }
    }
    Write-Host 'Stopped both apps.' -ForegroundColor Cyan
}
