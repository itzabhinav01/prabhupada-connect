param (
    [Parameter(Mandatory=$true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) {
    Write-Host "[ERROR] File not found: $Path" -ForegroundColor Red
    exit 1
}

$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Csproj = Join-Path $ProjectDir "CorpusPipeline.csproj"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "VedaBaseModern 2 — Book Ingestion Pipeline" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "Target Book File: $Path"

dotnet run --project $Csproj -- import-book $Path
if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[SUCCESS] Book successfully added to VedaBaseModern 2 corpus!" -ForegroundColor Green
    Write-Host "Listing current corpus summary:"
    dotnet run --project $Csproj -- list-books
} else {
    Write-Host "`n[FAILED] Book import failed with exit code $LASTEXITCODE" -ForegroundColor Red
    exit $LASTEXITCODE
}
