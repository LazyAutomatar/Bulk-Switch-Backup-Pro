<#
.SYNOPSIS
    Publishes a prepared release by committing, tagging, and pushing to origin.
.DESCRIPTION
    Requires -ConfirmPublish to prevent accidental publishing during troubleshooting cycles.
.EXAMPLE
    .\scripts\publish-release.ps1 -Version 2.5.0 -ConfirmPublish
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,

    [switch]$ConfirmPublish
)

$ErrorActionPreference = "Stop"
$cleanVer = $Version.TrimStart('v', 'V')
$tag = "v$cleanVer"

if (-not $ConfirmPublish) {
    Write-Host "`n[SAFETY STOP] -ConfirmPublish was not specified." -ForegroundColor Yellow
    Write-Host "Per project protocol, do NOT create releases during ongoing changes/troubleshooting." -ForegroundColor Yellow
    Write-Host "When you are ready to cut and publish the final build as one, run:" -ForegroundColor Cyan
    Write-Host "  .\scripts\publish-release.ps1 -Version $cleanVer -ConfirmPublish`n" -ForegroundColor Green
    exit 1
}

Write-Host ">>> Committing and publishing release $tag..." -ForegroundColor Cyan

# 1. Commit staged changes if any
$status = git status --porcelain
if ($status) {
    git add -A
    git commit -m "chore(release): cut version $tag with aggregated changelog"
    Write-Host " Committed staged release changes." -ForegroundColor Green
}

# 2. Push main
Write-Host "Pushing main to origin..." -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) { throw "Failed to push main branch." }

# 3. Create annotated tag
$existingTags = git tag -l $tag
if ($existingTags) {
    throw "Tag $tag already exists in local repository!"
}

git tag -a $tag -m "Release $tag: Aggregated production build"
Write-Host " Created tag $tag." -ForegroundColor Green

# 4. Push tag
Write-Host "Pushing tag $tag to trigger release pipeline..." -ForegroundColor Cyan
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw "Failed to push tag $tag." }

Write-Host "`n Successfully triggered release pipeline for $tag!" -ForegroundColor Green
