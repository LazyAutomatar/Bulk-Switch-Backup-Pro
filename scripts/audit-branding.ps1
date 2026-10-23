<#
.SYNOPSIS
    Audits the entire codebase and documentation for solo project branding compliance.
.DESCRIPTION
    Ensures that no legacy corporate references exist in any tracked source or markdown file,
    verifies that author attribution is strictly 'Yash Nalawde', and distribution channel
    is 'LazyAutomatar/Bulk-Switch-Backup-Pro'.
#>

param(
    [switch]$VerboseOutput
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ">>> Running solo project branding & attribution audit..." -ForegroundColor Cyan

# Construct search term dynamically to avoid self-matching
$forbiddenWord = [System.Text.Encoding]::UTF8.GetString(@(99, 111, 110, 118, 101, 110, 116, 117, 115))
$spoofWord = "Lazy" + "Automator"

# 1. Scan for forbidden term across all non-ignored source files (excluding scripts/ itself)
$files = Get-ChildItem -Path $root -Recurse -File | Where-Object {
    $_.FullName -notmatch '[\\/](\.git|bin|obj|dist|publish|scripts|artifacts|TestResults)[\\/]'
}

foreach ($file in $files) {
    try {
        $matches = Select-String -Path $file.FullName -Pattern $forbiddenWord -CaseSensitive:$false
        if ($matches) {
            foreach ($m in $matches) {
                $rel = Resolve-Path -Relative -Path $file.FullName
                $violations.Add("Forbidden keyword '$forbiddenWord' found in $rel (line $($m.LineNumber)): $($m.Line.Trim())")
            }
        }
    } catch { }
}

# 2. Check that developer attribution is Yash Nalawde
$checks = @(
    @{ Path = "BulkBackup.Desktop/ViewModels/MainViewModel.cs"; Pattern = 'developerName = "Yash Nalawde"'; Name = "MainViewModel.DeveloperName" },
    @{ Path = "BulkBackup.Desktop/Views/MainWindow.axaml"; Pattern = 'By Yash Nalawde'; Name = "MainWindow TitleBar Tag" },
    @{ Path = "installer.iss"; Pattern = '#define MyAppPublisher "Yash Nalawde"'; Name = "Inno Setup Publisher" }
)

foreach ($c in $checks) {
    $targetPath = Join-Path $root $c.Path
    if (Test-Path $targetPath) {
        $content = Get-Content $targetPath -Raw
        if ($content -notmatch $c.Pattern) {
            $violations.Add("Attribution mismatch in $($c.Path): Expected pattern '$($c.Pattern)' for $($c.Name)")
        }
    } else {
        $violations.Add("Missing file for attribution check: $($c.Path)")
    }
}

# 3. Check for unintended repo typo in non-test source/doc files
$docFiles = $files
foreach ($file in $docFiles) {
    try {
        $typos = Select-String -Path $file.FullName -Pattern $spoofWord -CaseSensitive:$true
        if ($typos) {
            foreach ($t in $typos) {
                $rel = Resolve-Path -Relative -Path $file.FullName
                $violations.Add("Typo '$spoofWord' (should be LazyAutomatar) in $rel (line $($t.LineNumber)): $($t.Line.Trim())")
            }
        }
    } catch { }
}

# Report results
if ($violations.Count -eq 0) {
    Write-Host " Branding Audit PASSED: 100% clean solo project attribution to Yash Nalawde." -ForegroundColor Green
    exit 0
} else {
    Write-Host "`n Branding Audit FAILED ($($violations.Count) violations found):" -ForegroundColor Red
    foreach ($v in $violations) {
        Write-Host "  - $v" -ForegroundColor Yellow
    }
    exit 1
}
