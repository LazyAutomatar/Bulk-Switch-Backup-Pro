param(
    [ValidateSet("all", "win", "mac", "linux", "win-x64", "osx-arm64", "osx-x64", "linux-x64", "linux-arm64")]
    [string]$Target = "all",
    [string]$Configuration = "Release",
    [string]$Version,
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')][string]$Repo = "LazyAutomatar/Bulk-Switch-Backup-Pro"
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Join-Path $ScriptDir "BulkBackup.Desktop"
$DistDir = Join-Path $ScriptDir "dist"
if (-not $Version) {
    [xml]$project = Get-Content (Join-Path $ProjectDir "BulkBackup.Desktop.csproj") -Raw
    $Version = [string]$project.Project.PropertyGroup.Version
}
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') { throw "Invalid release version." }
$releaseNotes = & (Join-Path $ScriptDir 'scripts/release-notes.ps1') -Version $Version

if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
}

Write-Host "=================================================================" -ForegroundColor Cyan
Write-Host " Bulk Switch Backup Pro - Multi-Platform Cross-Compiler Pipeline" -ForegroundColor Cyan
Write-Host " Target: $Target | Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "=================================================================" -ForegroundColor Cyan

function Get-InnoCompiler {
    $possiblePaths = @(
        "C:\Users\$env:USERNAME\AppData\Local\Programs\Antigravity IDE\resources\app\node_modules\innosetup\bin\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
        "${env:LOCALAPPDATA}\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($p in $possiblePaths) {
        if (Test-Path $p) { return $p }
    }
    $innoCmd = Get-Command "iscc" -ErrorAction SilentlyContinue
    if ($innoCmd) { return $innoCmd.Source }
    return $null
}

$buildWin = $Target -eq "all" -or $Target -eq "win" -or $Target -eq "win-x64"
$buildMacArm = $Target -eq "all" -or $Target -eq "mac" -or $Target -eq "osx-arm64"
$buildMacX64 = $Target -eq "all" -or $Target -eq "mac" -or $Target -eq "osx-x64"
$buildLinuxX64 = $Target -eq "all" -or $Target -eq "linux" -or $Target -eq "linux-x64"
$buildLinuxArm64 = $Target -eq "all" -or $Target -eq "linux" -or $Target -eq "linux-arm64"

$results = [System.Collections.Generic.List[PSCustomObject]]::new()

function Publish-Target {
    param(
        [string]$Rid,
        [string]$Category,
        [string]$DisplayName
    )

    Write-Host "`n>>> [Building $DisplayName] RID: $Rid..." -ForegroundColor Yellow
    $publishOut = Join-Path $ProjectDir "bin\$Configuration\net10.0\$Rid\publish"

    dotnet publish "$ProjectDir\BulkBackup.Desktop.csproj" `
        -c $Configuration `
        -r $Rid `
        --self-contained true `
        /p:PublishSingleFile=true `
        /p:EnableCompressionInSingleFile=true `
        /p:SatelliteResourceLanguages=en `
        /p:DebugType=none `
        /p:DebugSymbols=false `
        /p:IncludeSourceRevisionId=false

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed for $DisplayName ($Rid)"
    }

    if ($Category -eq "windows") {
        # Stop any currently running instance that locks the dist executable
        Get-Process "BulkSwitchBackupPro" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800

        $winTargetDir = Join-Path $DistDir "win-x64"
        if (Test-Path $winTargetDir) { Remove-Item -Recurse -Force $winTargetDir }
        New-Item -ItemType Directory -Force -Path $winTargetDir | Out-Null

        Get-ChildItem -Path $publishOut | ForEach-Object {
            Copy-Item $_.FullName -Destination $winTargetDir -Recurse -Force
        }

        # Sync standalone .exe to dist root
        $mainExe = Join-Path $winTargetDir "BulkSwitchBackupPro.exe"
        if (Test-Path $mainExe) {
            $destExe = Join-Path $DistDir "BulkSwitchBackupPro.exe"
            for ($attempt = 1; $attempt -le 5; $attempt++) {
                try {
                    if (Test-Path $destExe) { Remove-Item -Force $destExe -ErrorAction SilentlyContinue }
                    Copy-Item $mainExe -Destination $destExe -Force
                    break
                } catch {
                    if ($attempt -eq 5) { throw $_ }
                    Start-Sleep -Seconds 1
                }
            }
            Get-ChildItem -Path $winTargetDir -Filter "*.dll" | ForEach-Object {
                Copy-Item $_.FullName -Destination $DistDir -Force
            }
        }

        # Create Windows Zip package
        $winZip = Join-Path $DistDir "BulkSwitchBackupPro-win-x64.zip"
        if (Test-Path $winZip) { Remove-Item -Force $winZip }
        Write-Host "Creating $winZip..." -ForegroundColor Gray
        for ($attempt = 1; $attempt -le 5; $attempt++) {
            try {
                Compress-Archive -Path "$winTargetDir\*" -DestinationPath $winZip -CompressionLevel Optimal
                break
            } catch {
                if ($attempt -eq 5) { throw $_ }
                Write-Host "Waiting for file scan/sync release (attempt $attempt/5)..." -ForegroundColor Yellow
                Start-Sleep -Seconds 2
            }
        }

        $zipSize = [math]::Round((Get-Item $winZip).Length / 1MB, 2)
        $exeSize = [math]::Round((Get-Item $mainExe).Length / 1MB, 2)

        $results.Add([PSCustomObject]@{
            Platform = "Windows x64 (.exe)"
            Package = "dist\BulkSwitchBackupPro.exe"
            Size = "$exeSize MB"
        })
        $results.Add([PSCustomObject]@{
            Platform = "Windows x64 (.zip)"
            Package = "dist\BulkSwitchBackupPro-win-x64.zip"
            Size = "$zipSize MB"
        })

        # Build Windows Setup Installer (.exe installer)
        $iscc = Get-InnoCompiler
        $issScript = Join-Path $ScriptDir "installer.iss"
        if ($iscc -and (Test-Path $issScript)) {
            Write-Host "`n>>> [Building Windows Setup Installer (exe)]..." -ForegroundColor Yellow
            & $iscc "/DMyAppVersion=$Version" "$issScript" /Q
            if ($LASTEXITCODE -eq 0) {
                $setupVer = Join-Path $DistDir "BulkSwitchBackupPro_Setup_v$Version.exe"
                $setupGen = Join-Path $DistDir "BulkSwitchBackupPro_Setup.exe"
                if (Test-Path $setupVer) {
                    Copy-Item $setupVer -Destination $setupGen -Force
                    $setupSize = [math]::Round((Get-Item $setupVer).Length / 1MB, 2)
                    $results.Add([PSCustomObject]@{
                        Platform = "Windows Installer (.exe)"
                        Package = "dist\BulkSwitchBackupPro_Setup_v$Version.exe"
                        Size = "$setupSize MB"
                    })
                }
            }
        }
    }
    elseif ($Category -eq "macos") {
        $bundleRoot = Join-Path $DistDir "$Rid\BulkSwitchBackupPro.app"
        $contentsDir = Join-Path $bundleRoot "Contents"
        $macosDir = Join-Path $contentsDir "MacOS"
        $resourcesDir = Join-Path $contentsDir "Resources"

        if (Test-Path $bundleRoot) { Remove-Item -Recurse -Force $bundleRoot }
        New-Item -ItemType Directory -Force -Path $macosDir | Out-Null
        New-Item -ItemType Directory -Force -Path $resourcesDir | Out-Null

        # Info.plist
        Copy-Item "$ProjectDir\Info.plist" "$contentsDir\Info.plist"

        # Binary & native libraries
        Get-ChildItem -Path $publishOut | ForEach-Object {
            Copy-Item $_.FullName -Destination $macosDir -Recurse -Force
        }

        # App Icon
        if (Test-Path "$ProjectDir\Assets\app.png") {
            Copy-Item "$ProjectDir\Assets\app.png" "$resourcesDir\app.png"
        }

        # Create quick-install instruction file
        $installGuide = Join-Path $DistDir "$Rid\How to Install on macOS.txt"
        @"
Bulk Switch Backup Pro for macOS
=================================
To install:
1. Drag 'BulkSwitchBackupPro.app' into your /Applications folder.
2. Launch 'Bulk Switch Backup Pro' from Launchpad or Applications.
"@ | Set-Content -Path $installGuide -Encoding UTF8

        # Zip package
        $macZip = Join-Path $DistDir "BulkSwitchBackupPro-$Rid.zip"
        if (Test-Path $macZip) { Remove-Item -Force $macZip }
        Write-Host "Creating $macZip..." -ForegroundColor Gray
        Compress-Archive -Path "$DistDir\$Rid\*" -DestinationPath $macZip -CompressionLevel Optimal

        $zipSize = [math]::Round((Get-Item $macZip).Length / 1MB, 2)
        $results.Add([PSCustomObject]@{
            Platform = "$DisplayName (.app & .zip)"
            Package = "dist\BulkSwitchBackupPro-$Rid.zip"
            Size = "$zipSize MB"
        })
    }
    elseif ($Category -eq "linux") {
        $linuxTargetDir = Join-Path $DistDir $Rid
        if (Test-Path $linuxTargetDir) { Remove-Item -Recurse -Force $linuxTargetDir }
        New-Item -ItemType Directory -Force -Path $linuxTargetDir | Out-Null

        Get-ChildItem -Path $publishOut | ForEach-Object {
            Copy-Item $_.FullName -Destination $linuxTargetDir -Recurse -Force
        }

        # Include Linux installer and uninstaller scripts
        if (Test-Path "$ProjectDir\Assets\install.sh") {
            Copy-Item "$ProjectDir\Assets\install.sh" "$linuxTargetDir\install.sh" -Force
        }
        if (Test-Path "$ProjectDir\Assets\uninstall.sh") {
            Copy-Item "$ProjectDir\Assets\uninstall.sh" "$linuxTargetDir\uninstall.sh" -Force
        }
        if (Test-Path "$ProjectDir\Assets\app.png") {
            Copy-Item "$ProjectDir\Assets\app.png" "$linuxTargetDir\app.png" -Force
        }

        $tarGz = Join-Path $DistDir "BulkSwitchBackupPro-$Rid.tar.gz"
        $zip = Join-Path $DistDir "BulkSwitchBackupPro-$Rid.zip"
        if (Test-Path $tarGz) { Remove-Item -Force $tarGz }
        if (Test-Path $zip) { Remove-Item -Force $zip }

        $tarCmd = Get-Command "tar" -ErrorAction SilentlyContinue
        if ($tarCmd) {
            Write-Host "Creating $tarGz..." -ForegroundColor Gray
            & $tarCmd.Source -czf $tarGz -C $DistDir $Rid
            if ($LASTEXITCODE -ne 0) { throw "Archive creation failed for $Rid" }
            $tarSize = [math]::Round((Get-Item $tarGz).Length / 1MB, 2)
            $results.Add([PSCustomObject]@{
                Platform = "$DisplayName (with installer)"
                Package = "dist\BulkSwitchBackupPro-$Rid.tar.gz"
                Size = "$tarSize MB"
            })
        }
        else {
            Write-Host "Creating $zip..." -ForegroundColor Gray
            Compress-Archive -Path "$linuxTargetDir\*" -DestinationPath $zip -CompressionLevel Optimal
            $zipSize = [math]::Round((Get-Item $zip).Length / 1MB, 2)
            $results.Add([PSCustomObject]@{
                Platform = "$DisplayName (with installer)"
                Package = "dist\BulkSwitchBackupPro-$Rid.zip"
                Size = "$zipSize MB"
            })
        }
    }
}

# Execute builds according to selected targets
if ($buildWin) {
    Publish-Target -Rid "win-x64" -Category "windows" -DisplayName "Windows (64-bit)"
}
if ($buildMacArm) {
    Publish-Target -Rid "osx-arm64" -Category "macos" -DisplayName "macOS Apple Silicon (M1/M2/M3/M4)"
}
if ($buildMacX64) {
    Publish-Target -Rid "osx-x64" -Category "macos" -DisplayName "macOS Intel (x64)"
}
if ($buildLinuxX64) {
    Publish-Target -Rid "linux-x64" -Category "linux" -DisplayName "Linux (x64)"
}
if ($buildLinuxArm64) {
    Publish-Target -Rid "linux-arm64" -Category "linux" -DisplayName "Linux (ARM64)"
}

Write-Host "`n=================================================================" -ForegroundColor Green
Write-Host " All Selected Platforms & Installers Published Successfully!" -ForegroundColor Green
Write-Host "=================================================================" -ForegroundColor Green

$results | Format-Table -Property Platform, Package, Size -AutoSize
Write-Host "All distribution packages ready in: $DistDir`n" -ForegroundColor Cyan



if ($buildWin) {
    $installer = Join-Path $DistDir 'BulkSwitchBackupPro_Setup.exe'
    if (-not (Test-Path $installer)) { throw 'Windows update manifest requires a successfully built Inno Setup installer.' }
    $manifest = [ordered]@{
        version = $Version; title = "Bulk Switch Backup Pro $Version"
        releaseNotes = $releaseNotes
        publishedAt = [DateTime]::UtcNow.ToString('o')
        downloadUrl = "https://github.com/$Repo/releases/download/v$Version/BulkSwitchBackupPro_Setup.exe"
        sha256 = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        fileSizeBytes = (Get-Item $installer).Length
    }
    $manifest | ConvertTo-Json | Set-Content (Join-Path $DistDir 'update_manifest.json') -Encoding utf8
}
