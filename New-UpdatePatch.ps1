# Prabhupāda Connect - Automated Lightweight Update Patch Generator
# Generates a tiny update zip (~3.5 MB) containing updated application binaries
# without the 235 MB corpus database or user data.

[CmdletBinding()]
param(
    [switch]$FullRuntime = $false
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   Prabhupada Connect - Lightweight Update Generator     " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check .NET SDK
Write-Host "[1/4] Checking .NET SDK..." -ForegroundColor Yellow
try {
    $dotnetVer = & dotnet --version
    Write-Host "      Found .NET SDK version: $dotnetVer" -ForegroundColor Green
} catch {
    Write-Error "ERROR: .NET SDK was not found! Please install .NET 10 SDK."
    exit 1
}

# Step 2: Publish Application in Self-Contained Release Mode
Write-Host "[2/4] Publishing application binaries (Release / win-x64)..." -ForegroundColor Yellow

# Kill running instances before publishing
Get-Process -Name "VedaBaseModern2.UI" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

$projPath = "$PSScriptRoot\App\VedaBaseModern2.UI\VedaBaseModern2.UI.csproj"
$publishDir = "$PSScriptRoot\App\VedaBaseModern2.UI\bin\Release\net10.0-windows10.0.26100.0\win-x64\publish"

& dotnet publish $projPath -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    Write-Error "ERROR: Release publish failed!"
    exit 1
}
Write-Host "      Publish completed successfully." -ForegroundColor Green

# Step 3: Prepare staging area for the patch
Write-Host "[3/4] Assembling update archive..." -ForegroundColor Yellow
$distDir = "$PSScriptRoot\dist"
if (-not (Test-Path $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$patchZip = "$distDir\PrabhupadaConnect-Update.zip"
if (Test-Path $patchZip) {
    Remove-Item $patchZip -Force
}

$tempStage = "$env:TEMP\PrabhupadaConnect_PatchStage"
if (Test-Path $tempStage) {
    Remove-Item $tempStage -Recurse -Force
}
New-Item -ItemType Directory -Path $tempStage | Out-Null

try {
    if ($FullRuntime) {
        Write-Host "      Mode: Full Runtime (all assemblies except database)..." -ForegroundColor Cyan
        # Copy everything except *.db
        Get-ChildItem -Path $publishDir | Where-Object { $_.Extension -ne ".db" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $tempStage -Recurse -Force
        }
    } else {
        Write-Host "      Mode: Fast App Patch (~3.5 MB, app code + assets + WinUI resources)..." -ForegroundColor Cyan
        # Fast app update: app assemblies, resources, assets, configs, xbf
        Get-ChildItem -Path $publishDir | Where-Object {
            $_.Name -like "VedaBaseModern2*" -or
            $_.Name -eq "Assets" -or
            $_.Name -like "*.pri" -or
            $_.Name -like "*.xbf" -or
            $_.Name -like "*.runtimeconfig.json" -or
            $_.Name -like "*.deps.json" -or
            $_.Name -like "Microsoft.Data.Sqlite*"
        } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $tempStage -Recurse -Force
        }
    }

    # Compress stage into PrabhupadaConnect-Update.zip
    Compress-Archive -Path "$tempStage\*" -DestinationPath $patchZip -CompressionLevel Optimal
}
finally {
    if (Test-Path $tempStage) {
        Remove-Item $tempStage -Recurse -Force -ErrorAction SilentlyContinue
    }
}

# Step 4: Verification & Summary
Write-Host "[4/4] Verifying update package..." -ForegroundColor Yellow
$zipItem = Get-Item $patchZip
$zipSizeMB = [math]::Round($zipItem.Length / 1MB, 2)

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "   UPDATE PACKAGE GENERATED SUCCESSFULLY!                " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " Archive File : $patchZip" -ForegroundColor White
Write-Host " Package Size : $zipSizeMB MB" -ForegroundColor White
Write-Host ""
Write-Host "To deploy this update to users:" -ForegroundColor Cyan
Write-Host " 1. Create a new GitHub Release (e.g. v2.0.1)" -ForegroundColor White
Write-Host " 2. Attach 'dist\PrabhupadaConnect-Update.zip' to the Release assets" -ForegroundColor White
Write-Host " 3. All existing users can click '⚡ Fast In-App Update' in Settings" -ForegroundColor White
Write-Host "    and it will automatically download ($zipSizeMB MB), apply, and restart!" -ForegroundColor White
Write-Host "==========================================================" -ForegroundColor Green
