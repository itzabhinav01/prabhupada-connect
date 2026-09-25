# Prabhupāda Connect - Automated 1-Click Installer Generator
# Builds the Release binaries and packages everything into a single standalone setup exe

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Prabhupada Connect - Standalone Installer Generator   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check .NET SDK
Write-Host "[1/5] Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVer = & dotnet --version
    Write-Host "      Found .NET SDK version: $dotnetVer" -ForegroundColor Green
} catch {
    Write-Error "ERROR: .NET SDK was not found! Please install .NET 10 SDK from https://dotnet.microsoft.com/"
    exit 1
}

# Step 2: Check Database File (ensure not unpulled Git LFS pointer)
Write-Host "[2/5] Checking scripture database..." -ForegroundColor Yellow
$dbPath = "$PSScriptRoot\Database\prabhupada_corpus.db"
if (-not (Test-Path $dbPath)) {
    Write-Error "ERROR: Database file not found at $dbPath"
    exit 1
}
$dbSize = (Get-Item $dbPath).Length
if ($dbSize -lt 10000000) { # Less than 10MB means unpulled Git LFS pointer
    Write-Host "      Detected Git LFS pointer. Pulling full database via git lfs..." -ForegroundColor Yellow
    git lfs pull
    $dbSize = (Get-Item $dbPath).Length
    if ($dbSize -lt 10000000) {
        Write-Error "ERROR: Could not pull the full database file via git lfs. Please run 'git lfs pull'."
        exit 1
    }
}
$dbSizeMB = [math]::Round($dbSize / 1MB, 1)
Write-Host "      Corpus database verified ($dbSizeMB MB)." -ForegroundColor Green

# Step 3: Check Inno Setup (ISCC.exe)
Write-Host "[3/5] Locating Inno Setup compiler (ISCC.exe)..." -ForegroundColor Yellow
$isccPath = $null

# Check PATH
$cmd = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($cmd) {
    $isccPath = $cmd.Source
}

# Check common install locations
if (-not $isccPath) {
    $commonLocations = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles (x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles (x86)\Inno Setup 5\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 5\ISCC.exe"
    )
    foreach ($loc in $commonLocations) {
        if (Test-Path $loc) {
            $isccPath = $loc
            break
        }
    }
}

# If still not found, offer to install via winget
if (-not $isccPath) {
    Write-Host "      Inno Setup not detected on PATH. Installing via winget..." -ForegroundColor Yellow
    try {
        winget install --id JRSoftware.InnoSetup -e --silent --accept-source-agreements --accept-package-agreements
        # Re-check locations after install
        foreach ($loc in $commonLocations) {
            if (Test-Path $loc) {
                $isccPath = $loc
                break
            }
        }
    } catch {
        Write-Warning "winget install failed or was cancelled."
    }
}

if (-not $isccPath -or -not (Test-Path $isccPath)) {
    Write-Error "ERROR: Inno Setup compiler (ISCC.exe) could not be found. Please install Inno Setup from https://jrsoftware.org/isdl.php"
    exit 1
}
Write-Host "      Found Inno Setup at: $isccPath" -ForegroundColor Green

# Step 4: Build Application in Release Mode
Write-Host "[4/5] Building Prabhupada Connect in Release mode..." -ForegroundColor Yellow

# Kill running instances before building
Get-Process -Name "VedaBaseModern2.UI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$projPath = "$PSScriptRoot\App\VedaBaseModern2.UI\VedaBaseModern2.UI.csproj"
& dotnet build $projPath -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Release build failed!"
    exit 1
}
Write-Host "      Release build completed successfully." -ForegroundColor Green

# Step 5: Compile Standalone Setup Executable
Write-Host "[5/5] Compiling single bundle setup executable..." -ForegroundColor Yellow
$distDir = "$PSScriptRoot\dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$issPath = "$PSScriptRoot\installer\PrabhupadaConnect.iss"
& "$isccPath" "$issPath"
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Inno Setup compilation failed!"
    exit 1
}

$setupExe = "$distDir\PrabhupadaConnect-Setup-v2.0.exe"
if (Test-Path $setupExe) {
    $exeSizeMB = [math]::Round((Get-Item $setupExe).Length / 1MB, 1)
    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host "   SUCCESS! STANDALONE INSTALLER CREATED SUCCESSFULLY!   " -ForegroundColor Green
    Write-Host "==========================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "File Name : PrabhupadaConnect-Setup-v2.0.exe" -ForegroundColor Cyan
    Write-Host "File Size : $exeSizeMB MB" -ForegroundColor Cyan
    Write-Host "Location  : $setupExe" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "HOW TO SHARE WITH YOUR FRIEND ON WHATSAPP:" -ForegroundColor Yellow
    Write-Host "1. Open WhatsApp (Desktop, Web, or Phone)."
    Write-Host "2. Click the '+' or Paperclip attachment icon in your chat."
    Write-Host "3. Select 'Document' (NOT Photo/Video)."
    Write-Host "4. Choose this file: $setupExe"
    Write-Host ""
    Write-Host "WHAT YOUR FRIEND DOES ON THEIR LAPTOP:" -ForegroundColor Yellow
    Write-Host "1. Double-click PrabhupadaConnect-Setup-v2.0.exe."
    Write-Host "2. Click Next -> It installs to 'C:\vedabase versions\modern vedabase v2'."
    Write-Host "3. An icon appears on their Desktop. When clicked, the app launches!"
    Write-Host ""
    
    # Open Explorer to dist folder
    explorer.exe "/select,`"$setupExe`""
} else {
    Write-Error "ERROR: Installer executable was not generated at $setupExe"
    exit 1
}
