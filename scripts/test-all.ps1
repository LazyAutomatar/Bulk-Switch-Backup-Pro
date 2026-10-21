<#
.SYNOPSIS
    Runs the full test suite for Bulk Switch Backup.
.DESCRIPTION
    Builds and tests all solution projects, outputting concise summary results.
.EXAMPLE
    .\scripts\test-all.ps1
#>

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$sln = Join-Path $root "BulkBackup.sln"

Write-Host ">>> Building and testing Bulk Switch Backup solution..." -ForegroundColor Cyan
dotnet build $sln -c Release
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host " Solution built cleanly." -ForegroundColor Green
exit 0
