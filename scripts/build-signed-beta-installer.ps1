<#
.SYNOPSIS
Package an existing portable local beta with explicit development signing, without publishing.
#>
param(
    [string]$BetaDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/window-zoom-beta'),
    [string]$OutputDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/window-zoom-signed'),
    [switch]$AllowDevelopmentCertificate
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path ([IO.Path]::GetFullPath($BetaDirectory)) 'app'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$app = Join-Path $output 'app'
if ($source -eq $app) { throw 'Use a separate output directory to preserve the original beta.' }
if (-not (Test-Path -LiteralPath (Join-Path $source 'BulkSwitchBackupPro.exe'))) { throw 'Build the portable beta first.' }
& "$PSScriptRoot/sign-windows-artifact.ps1" -CheckCertificateOnly -AllowDevelopmentCertificate:$AllowDevelopmentCertificate
New-Item -ItemType Directory -Path $app -Force | Out-Null
Copy-Item (Join-Path $source '*') -Destination $app -Recurse -Force
$exe = Join-Path $app 'BulkSwitchBackupPro.exe'
$version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -split '\.')[0..2] -join '.'
& "$PSScriptRoot/sign-windows-artifact.ps1" -FilePath $exe -CertificateOutputDirectory $output -AllowDevelopmentCertificate:$AllowDevelopmentCertificate
"$((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant())  BulkSwitchBackupPro.exe" |
    Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
$iscc = @("${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "${env:ProgramFiles}\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup 6 is required.' }
& $iscc /Q "/DMyAppVersion=$version" "/DMyAppSourceDir=$app" "/DMyAppOutputDir=$output" /DBetaBuild (Join-Path $root 'installer.iss')
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed: $LASTEXITCODE" }
$installer = Join-Path $output 'BulkSwitchBackupPro_LocalBeta_Setup.exe'
& "$PSScriptRoot/sign-windows-artifact.ps1" -FilePath $installer -CertificateOutputDirectory $output -AllowDevelopmentCertificate:$AllowDevelopmentCertificate
$archive = Join-Path $output 'BulkSwitchBackupPro-signed-local-beta-win-x64.zip'
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $archive -Force
@($exe, $installer, $archive) | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetRelativePath($output, $_).Replace('\', '/'))"
} | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
Write-Host "Signed local beta installer: $installer"
