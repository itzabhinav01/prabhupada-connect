# VedaBaseModern2 Launcher - Cleans up stale WebView2 processes before starting
# Run this instead of running the .exe directly to avoid the "blank screen" issue

$exePath = "$PSScriptRoot\App\VedaBaseModern2.UI\bin\Release\net10.0-windows10.0.26100.0\win-x64\VedaBaseModern2.UI.exe"

# Step 1: Kill any previous instance of the app
$prev = Get-Process -Name "VedaBaseModern2.UI" -ErrorAction SilentlyContinue
if ($prev) {
    Write-Host "Stopping previous VedaBaseModern2.UI instance..."
    $prev | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

# Step 2: Kill any orphaned msedgewebview2 processes associated with our user data folder
$wv2Procs = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
    Where-Object { $_.CommandLine -like "*VedaBaseModern*" }
if ($wv2Procs) {
    Write-Host "Stopping $($wv2Procs.Count) orphaned WebView2 process(es)..."
    $wv2Procs | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
}

# Step 3: Launch the app
if (Test-Path $exePath) {
    Write-Host "Launching VedaBaseModern2..."
    Start-Process $exePath -WorkingDirectory $PSScriptRoot
} else {
    Write-Warning "Executable not found at: $exePath"
    Write-Host "Please build the project first: dotnet build C:\VedaBaseModern2\App\VedaBaseModern2.UI\VedaBaseModern2.UI.csproj -c Release"
}
