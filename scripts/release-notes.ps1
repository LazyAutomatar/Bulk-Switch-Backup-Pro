param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$ChangelogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md'),
    [string]$OutputPath
)
$ErrorActionPreference = 'Stop'
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
