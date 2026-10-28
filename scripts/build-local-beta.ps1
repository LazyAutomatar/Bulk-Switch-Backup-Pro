<#
.SYNOPSIS
Build a tested portable Windows beta without changing versions or publishing anything.
#>
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $root 'artifacts/local-beta' }
$app = Join-Path $output 'app'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& (Join-Path $PSScriptRoot 'test-all.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Local beta validation failed.' }
& dotnet publish (Join-Path $root 'BulkBackup.Desktop/BulkBackup.Desktop.csproj') -c Release -r win-x64 `
    --self-contained true -o $app -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeAllContentForSelfExtract=true -p:DefineConstants=LOCAL_BETA -p:DebugType=none -p:DebugSymbols=false `
    -p:IncludeSourceRevisionId=false *> (Join-Path $output 'publish.log')
if ($LASTEXITCODE -ne 0) { throw "Beta publish failed. See $output/publish.log" }
@'
Bulk Switch Backup Pro - LOCAL BETA

Run BulkSwitchBackupPro.exe from this folder. This is a portable test build;
the title bar identifies it as Local beta. Product version numbers are unchanged.
No release tag, public upload or installer is created by this build.

Test:
1. Run Monitor: Auto is selected and visible; the dropdown is wider.
2. Device table: status wraps, progress labels stay separate from the bar,
   and horizontal scrolling exposes the wider topology columns.
3. Command Reference: select any of the 29 platforms to inspect its actual
   diagnostic commands, paging setup and required configuration capture.
4. Custom CLI Command Script: edits are preserved when switching modes.

5. Zoom controls: repeated plus/minus clicks must not resize or maximize the window.
   At smaller window sizes, the header remains visible while content can scroll.

Versioning and stable publication will happen after this beta is accepted.

Vault and legacy-switch regression checks:
6. Saved Credentials: create a master password (12+ characters), save a test
   profile, Apply to Session, Lock, and Unlock. The populated table must not crash.
   Existing Windows profiles can be imported explicitly after creating the vault.
7. A device with SSH unavailable and Telnet enabled should connect on port 23
   and display Telnet. Test only on a trusted management network.
8. SG350 repeated Username/Password prompts should finish before backup commands.
9. C1000 must appear as Switch and receive IOS commands, never AP wired commands
   in Auto mode. SG350 should use terminal datadump and its Small Business profile.
'@ | Set-Content (Join-Path $app 'LOCAL-BETA-README.txt') -Encoding utf8
$exe = Join-Path $app 'BulkSwitchBackupPro.exe'
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  BulkSwitchBackupPro.exe" | Set-Content (Join-Path $app 'SHA256SUMS.txt') -Encoding utf8
$zip = Join-Path $output 'BulkSwitchBackupPro-local-beta-win-x64.zip'
Compress-Archive -Path (Join-Path $app '*') -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host "Local beta ready: $exe"
Write-Host "Portable ZIP: $zip"

$iscc = @(
    "C:\Users\$env:USERNAME\AppData\Local\Programs\Antigravity IDE\resources\app\node_modules\innosetup\bin\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) { $iscc = (Get-Command iscc -ErrorAction SilentlyContinue).Source }
if ($iscc) {
    Write-Host "Compiling Local Beta Installer with Inno Setup..." -ForegroundColor Cyan
    $version = ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion -split '\.')[0..2] -join '.'
    & $iscc /Q "/DMyAppVersion=$version" "/DMyAppSourceDir=$app" "/DMyAppOutputDir=$output" /DBetaBuild (Join-Path $root 'installer.iss')
    if ($LASTEXITCODE -eq 0) {
        $installer = Join-Path $output 'BulkSwitchBackupPro_LocalBeta_Setup.exe'
        Write-Host "Beta Installer ready: $installer"
    }
}

$artifacts = @($exe, $zip)
$installerPath = Join-Path $output 'BulkSwitchBackupPro_LocalBeta_Setup.exe'
if (Test-Path $installerPath) { $artifacts += $installerPath }

$artifacts | ForEach-Object {
    "$((Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
} | Set-Content (Join-Path $output 'SHA256SUMS.txt') -Encoding utf8
