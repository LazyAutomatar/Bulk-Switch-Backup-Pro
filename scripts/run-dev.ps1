<#
.SYNOPSIS
    Launches Bulk Switch Backup Pro in local development/debug mode.
.DESCRIPTION
    Quickly builds and starts the Avalonia desktop application without packaging or installer steps.
.EXAMPLE
    .\scripts\run-dev.ps1
    .\scripts\run-dev.ps1 -Configuration Release
#>

param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$proj = Join-Path $root "BulkBackup.Desktop/BulkBackup.Desktop.csproj"

Write-Host ">>> Launching Bulk Switch Backup Pro ($Configuration)..." -ForegroundColor Cyan
dotnet run --project $proj -c $Configuration @args
