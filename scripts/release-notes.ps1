param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ChangelogPath,
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
if (-not $ChangelogPath) {
    $root = if ($PSScriptRoot) { Split-Path -Parent $PSScriptRoot } else { (Get-Location).Path }
    $ChangelogPath = Join-Path $root 'CHANGELOG.md'
}
$cleanVersion = $Version.TrimStart('v', 'V')
$text = [IO.File]::ReadAllText($ChangelogPath).Replace("`r`n", "`n")
$escapedVersion = [regex]::Escape($cleanVersion)
$section = [regex]::Match($text, "(?ms)^## \[v?$escapedVersion\][^\n]*\n(?<notes>.*?)(?=^## |\z)")
if (-not $section.Success -or [string]::IsNullOrWhiteSpace($section.Groups['notes'].Value)) {
    throw "No release notes found for v$cleanVersion in $ChangelogPath"
}
$notes = $section.Groups['notes'].Value.Trim()
if ($OutputPath) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $notes + "`n") }
$notes
