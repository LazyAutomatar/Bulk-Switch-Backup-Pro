param(
    [string]$Version = "3.1.0",
    [string]$Repo = "LazyAutomatar/Bulk-Switch-Backup-Pro"
)

$ErrorActionPreference = "Stop"
$cleanVer = $Version.TrimStart('v', 'V')
$tag = "v$cleanVer"

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Publishing Bulk Switch Backup Pro $tag to $Repo" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

# 1. Retrieve GitHub credential from Windows Credential Manager
$typeDef = @"
using System;
using System.Runtime.InteropServices;
public class WinCredReader {
    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);
    [DllImport("Advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
    public static extern void CredFree(IntPtr credentialPtr);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }
    public static string GetPassword(string target) {
        IntPtr credPtr;
        if (CredRead(target, 1, 0, out credPtr)) {
            var cred = (CREDENTIAL)Marshal.PtrToStructure(credPtr, typeof(CREDENTIAL));
            byte[] blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, cred.CredentialBlobSize);
            CredFree(credPtr);
            return System.Text.Encoding.Unicode.GetString(blob);
        }
        return null;
    }
}
"@

if (-not ([System.Management.Automation.PSTypeName]'WinCredReader').Type) {
    Add-Type -TypeDefinition $typeDef
}

$token = [WinCredReader]::GetPassword("git:https://github.com")
if (-not $token) {
    $token = $env:GITHUB_TOKEN
}
if (-not $token) {
    throw "Could not retrieve GitHub token from Windows Credential Manager (git:https://github.com) or GITHUB_TOKEN environment variable."
}

$headers = @{
    Authorization = "Bearer $token"
    "User-Agent" = "PowerShell-BulkBackupPublisher"
    Accept = "application/vnd.github.v3+json"
}

# 2. Extract release notes from CHANGELOG.md
$releaseNotes = & "$PSScriptRoot/release-notes.ps1" -Version $cleanVer
Write-Host "Loaded release notes for ${tag}:" -ForegroundColor Gray
Write-Host $releaseNotes -ForegroundColor DarkGray

# 3. Sync documentation, EULA, and release assets to public repo
Write-Host "`n>>> Syncing documentation and manifest to public repository..." -ForegroundColor Yellow
$tempSyncDir = Join-Path $env:TEMP "bsb-public-repo-sync-$tag"
if (Test-Path $tempSyncDir) { Remove-Item -Recurse -Force $tempSyncDir }

$authUrl = "https://x-access-token:${token}@github.com/${Repo}.git"
git clone $authUrl $tempSyncDir
if ($LASTEXITCODE -ne 0) { throw "Failed to clone $Repo" }

Push-Location $tempSyncDir
try {
    git checkout main
    
    # Copy public documentation
    $root = Split-Path -Parent $PSScriptRoot
    Copy-Item (Join-Path $root "README.md") -Destination "README.md" -Force
    if (Test-Path (Join-Path $root "EULA.txt")) { Copy-Item (Join-Path $root "EULA.txt") -Destination "EULA.txt" -Force }
    if (Test-Path (Join-Path $root "LICENSE.md")) { Copy-Item (Join-Path $root "LICENSE.md") -Destination "LICENSE.md" -Force }
    if (Test-Path (Join-Path $root "CHANGELOG.md")) { Copy-Item (Join-Path $root "CHANGELOG.md") -Destination "CHANGELOG.md" -Force }
    if (Test-Path (Join-Path $root "PLATFORM_SUPPORT.md")) { Copy-Item (Join-Path $root "PLATFORM_SUPPORT.md") -Destination "PLATFORM_SUPPORT.md" -Force }
    if (Test-Path (Join-Path $root "ASSESSMENT_EXPORTS.md")) { Copy-Item (Join-Path $root "ASSESSMENT_EXPORTS.md") -Destination "ASSESSMENT_EXPORTS.md" -Force }
    if (Test-Path (Join-Path $root "Logo.png")) { Copy-Item (Join-Path $root "Logo.png") -Destination "Logo.png" -Force }
    
    if (Test-Path (Join-Path $root "Assets")) {
        if (-not (Test-Path "Assets")) { New-Item -ItemType Directory -Path "Assets" -Force | Out-Null }
        Copy-Item (Join-Path $root "Assets\*") -Destination "Assets" -Recurse -Force
    }

    # Copy update manifest
    if (-not (Test-Path "dist")) { New-Item -ItemType Directory -Path "dist" -Force | Out-Null }
    Copy-Item (Join-Path $root "dist\update_manifest.json") -Destination "dist\update_manifest.json" -Force
    if (-not (Test-Path (Join-Path $root "dist\BulkSwitchBackupPro_CodeSigning.cer")) -and (Test-Path "dist\BulkSwitchBackupPro_CodeSigning.cer")) {
        Remove-Item "dist\BulkSwitchBackupPro_CodeSigning.cer" -Force
    }

    git config user.name "Yash Nalawde"
    git config user.email "146139825+Majordean0303@users.noreply.github.com"
    git add -A
    $hasChanges = (git status --porcelain).Length -gt 0
    if ($hasChanges) {
        git commit -m "docs: release $tag documentation and update manifest"
        git push origin main
        Write-Host " Pushed updated documentation and manifest to $Repo main branch." -ForegroundColor Green
    } else {
        Write-Host " Public repo main branch is already up to date." -ForegroundColor Cyan
    }

    # Tag on public repo
    $existingPublicTags = git tag -l $tag
    if ($existingPublicTags) {
        Write-Host " Tag $tag already exists on public repo. Deleting remote tag to ensure clean release..." -ForegroundColor Yellow
        git push origin :refs/tags/$tag
        git tag -d $tag
    }
    git tag -a $tag -m "Bulk Switch Backup Pro $tag"
    git push origin $tag
    Write-Host " Pushed tag $tag to $Repo." -ForegroundColor Green
}
finally {
    Pop-Location
    if (Test-Path $tempSyncDir) { Remove-Item -Recurse -Force $tempSyncDir }
}

# 4. Check if release already exists on public repo; if so, fetch it or create new
Write-Host "`n>>> Creating or retrieving GitHub release for $tag..." -ForegroundColor Yellow
$releaseUrl = "https://api.github.com/repos/$Repo/releases"
$existingRelease = $null
try {
    $existingRelease = Invoke-RestMethod -Uri "$releaseUrl/tags/$tag" -Headers $headers -Method Get -ErrorAction SilentlyContinue
} catch { }

if ($existingRelease) {
    Write-Host " Found existing release for $tag (ID: $($existingRelease.id)). Updating details..." -ForegroundColor Cyan
    $releaseId = $existingRelease.id
    $patchBody = @{
        name = "Bulk Switch Backup Pro $tag"
        body = $releaseNotes
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri "$releaseUrl/$releaseId" -Headers $headers -Method Patch -Body $patchBody -ContentType "application/json"
} else {
    Write-Host " Creating new release for $tag..." -ForegroundColor Cyan
    $postBody = @{
        tag_name = $tag
        name = "Bulk Switch Backup Pro $tag"
        body = $releaseNotes
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    $release = Invoke-RestMethod -Uri $releaseUrl -Headers $headers -Method Post -Body $postBody -ContentType "application/json"
    $releaseId = $release.id
    Write-Host " Created release ID: $releaseId" -ForegroundColor Green
}

# 5. Upload release assets
Write-Host "`n>>> Uploading release assets to $tag (Release ID: $releaseId)..." -ForegroundColor Yellow
$uploadBaseUrl = "https://uploads.github.com/repos/$Repo/releases/$releaseId/assets"

$assetsToUpload = @(
    "dist\update_manifest.json",
    "dist\BulkSwitchBackupPro.exe",
    "dist\BulkSwitchBackupPro_Setup.exe",
    "dist\BulkSwitchBackupPro_Setup_v$cleanVer.exe",
    "dist\BulkSwitchBackupPro-win-x64.zip",
    "dist\BulkSwitchBackupPro_CodeSigning.cer",
    "dist\BulkSwitchBackupPro_Install_Silent.cmd",
    "dist\BulkSwitchBackupPro-linux-x64.tar.gz",
    "dist\BulkSwitchBackupPro-linux-arm64.tar.gz",
    "dist\BulkSwitchBackupPro-osx-arm64.zip",
    "dist\BulkSwitchBackupPro-osx-x64.zip"
)

# Fetch existing assets in this release
$existingAssets = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/$releaseId/assets" -Headers $headers -Method Get

foreach ($relPath in $assetsToUpload) {
    $fullPath = Join-Path $PSScriptRoot "..\$relPath"
    if (-not (Test-Path $fullPath)) {
        Write-Host " Skipping (file not found): $relPath" -ForegroundColor DarkGray
        continue
    }

    $fileName = Split-Path -Leaf $fullPath
    $fileBytes = [System.IO.File]::ReadAllBytes($fullPath)
    $fileSizeMb = [math]::Round($fileBytes.Length / 1MB, 2)

    # Check if asset already exists in release
    $existing = $existingAssets | Where-Object { $_.name -eq $fileName }
    if ($existing) {
        Write-Host " Replacing existing asset: $fileName (ID: $($existing.id))..." -ForegroundColor Yellow
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/assets/$($existing.id)" -Headers $headers -Method Delete
    }

    $contentType = switch -Regex ($fileName) {
        '\.json$'   { 'application/json' }
        '\.exe$'    { 'application/octet-stream' }
        '\.zip$'    { 'application/zip' }
        '\.tar\.gz$' { 'application/gzip' }
        '\.cer$'    { 'application/x-x509-ca-cert' }
        '\.cmd$'    { 'text/plain' }
        default     { 'application/octet-stream' }
    }

    Write-Host " Uploading $fileName ($fileSizeMb MB)..." -ForegroundColor Cyan
    $uploadUri = "https://uploads.github.com/repos/$Repo/releases/$releaseId/assets?name=$fileName"

    $respJson = & curl.exe -s -S -X POST `
        -H "Authorization: Bearer $token" `
        -H "Accept: application/vnd.github.v3+json" `
        -H "Content-Type: $contentType" `
        --data-binary "@$fullPath" `
        "$uploadUri"

    if ($LASTEXITCODE -ne 0) {
        throw "Failed to upload $fileName via curl.exe (Exit code: $LASTEXITCODE)"
    }
    Write-Host "  Uploaded: $fileName successfully." -ForegroundColor Green
}

Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host " Bulk Switch Backup Pro $tag Successfully Released to Public Repo!" -ForegroundColor Green
Write-Host " View Release: https://github.com/$Repo/releases/tag/$tag" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green
