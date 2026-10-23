<#
.SYNOPSIS
    Runs the offline core suite and builds the existing Avalonia desktop application.
#>
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'test-core.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$buildLog = Join-Path $root 'artifacts/desktop-build.log'
& dotnet build (Join-Path $root 'BulkBackup.Desktop/BulkBackup.Desktop.csproj') -c $Configuration --nologo *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Write-Host "Avalonia desktop build FAILED. Log: $buildLog"
    exit 1
}
Write-Host "Avalonia desktop build PASSED. Log: $buildLog"
exit 0
