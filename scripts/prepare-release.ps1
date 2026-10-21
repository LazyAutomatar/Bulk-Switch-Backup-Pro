<#
.SYNOPSIS
    Prepares a new release version across all project files, changelog, and viewmodels.
.DESCRIPTION
    Promotes [Unreleased] notes in CHANGELOG.md to the new version, updates project versions,
    runs tests and branding audit, and stages git changes.
.EXAMPLE
    .\scripts\prepare-release.ps1 -Version 2.5.1
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$cleanVer = $Version.TrimStart('v', 'V')

if ($cleanVer -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
    throw "Invalid SemVer format: '$Version'. Expected format: 2.5.1 or 2.5.1-beta.1"
}

Write-Host ">>> Preparing Bulk Switch Backup Pro v$cleanVer..." -ForegroundColor Cyan

# 1. Run branding audit
& (Join-Path $PSScriptRoot "audit-branding.ps1")
if ($LASTEXITCODE -ne 0) { throw "Branding audit failed. Fix violations before preparing release." }

# 2. Update BulkBackup.Desktop/BulkBackup.Desktop.csproj
$csprojPath = Join-Path $root "BulkBackup.Desktop/BulkBackup.Desktop.csproj"
$csproj = [System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)
$csproj = [regex]::new('<Version>.*?</Version>').Replace($csproj, "<Version>$cleanVer</Version>", 1)
$csproj = [regex]::new('<AssemblyVersion>.*?</AssemblyVersion>').Replace($csproj, "<AssemblyVersion>$cleanVer.0</AssemblyVersion>", 1)
$csproj = [regex]::new('<FileVersion>.*?</FileVersion>').Replace($csproj, "<FileVersion>$cleanVer.0</FileVersion>", 1)
[System.IO.File]::WriteAllText($csprojPath, $csproj, [System.Text.Encoding]::UTF8)
Write-Host " Updated $csprojPath -> <Version>$cleanVer</Version>" -ForegroundColor Green

# 3. Update UpdateService.cs DefaultCurrentVersion
$updateServicePath = Join-Path $root "BulkBackup.Desktop/Services/UpdateService.cs"
if (Test-Path $updateServicePath) {
    $usText = [System.IO.File]::ReadAllText($updateServicePath, [System.Text.Encoding]::UTF8)
    $usText = [regex]::new('public const string DefaultCurrentVersion = ".*?";').Replace($usText, "public const string DefaultCurrentVersion = `"$cleanVer`";", 1)
    [System.IO.File]::WriteAllText($updateServicePath, $usText, [System.Text.Encoding]::UTF8)
    Write-Host " Updated $updateServicePath -> DefaultCurrentVersion = $cleanVer" -ForegroundColor Green
}

# 4. Update MainViewModel.cs appVersion
$mvmPath = Join-Path $root "BulkBackup.Desktop/ViewModels/MainViewModel.cs"
if (Test-Path $mvmPath) {
    $mvmText = [System.IO.File]::ReadAllText($mvmPath, [System.Text.Encoding]::UTF8)
    $mvmText = [regex]::new('private string appVersion = ".*?";').Replace($mvmText, "private string appVersion = `"$cleanVer Enterprise`";", 1)
    [System.IO.File]::WriteAllText($mvmPath, $mvmText, [System.Text.Encoding]::UTF8)
    Write-Host " Updated $mvmPath -> appVersion = $cleanVer Enterprise" -ForegroundColor Green
}

# 5. Run test suite
& (Join-Path $PSScriptRoot "test-all.ps1")
if ($LASTEXITCODE -ne 0) { throw "Tests failed! Fix tests before completing release preparation." }

# 6. Stage updated files
git add -A
Write-Host "`n Successfully prepared v$cleanVer! Changes staged in git." -ForegroundColor Green
Write-Host "Next step: Run .\scripts\publish-release.ps1 -Version $cleanVer to commit, tag, and publish." -ForegroundColor Yellow
