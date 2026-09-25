# Prabhupāda Connect Launcher - Cleans up stale WebView2 processes before starting
# Run this or double-click Launch.bat to start the app cleanly

$releaseExe = "$PSScriptRoot\App\VedaBaseModern2.UI\bin\Release\net10.0-windows10.0.26100.0\win-x64\VedaBaseModern2.UI.exe"
$debugExe = "$PSScriptRoot\App\VedaBaseModern2.UI\bin\Debug\net10.0-windows10.0.26100.0\win-x64\VedaBaseModern2.UI.exe"

$exePath = if (Test-Path $releaseExe) { $releaseExe } elseif (Test-Path $debugExe) { $debugExe } else { $null }

# Step 1: Kill any previous instance of the app
$prev = Get-Process -Name "VedaBaseModern2.UI" -ErrorAction SilentlyContinue
if ($prev) {
    Write-Host "Stopping previous instance..." -ForegroundColor Yellow
    $prev | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# Step 2: Kill any orphaned msedgewebview2 processes associated with our user data folder
$wv2Procs = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*VedaBaseModern*" }
if ($wv2Procs) {
    Write-Host "Stopping $($wv2Procs.Count) background WebView2 process(es)..." -ForegroundColor Yellow
    $wv2Procs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
}

# Step 3: Launch the app
if ($exePath -and (Test-Path $exePath)) {
    Write-Host "Launching Prabhupāda Connect..." -ForegroundColor Green
    Start-Process $exePath -WorkingDirectory (Split-Path $exePath -Parent)
} else {
    Write-Warning "Executable not found! Building Release package now..."
    dotnet build "$PSScriptRoot\App\VedaBaseModern2.UI\VedaBaseModern2.UI.csproj" -c Release
    if (Test-Path $releaseExe) {
        Write-Host "Launching Prabhupāda Connect..." -ForegroundColor Green
        Start-Process $releaseExe -WorkingDirectory (Split-Path $releaseExe -Parent)
    }
}
