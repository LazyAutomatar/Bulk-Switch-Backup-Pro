@echo off
setlocal enabledelayedexpansion

title Bulk Switch Backup Pro - Build and Distribution Pipeline

echo ========================================================
echo     Bulk Switch Backup Pro: Build and Distribution      
echo ========================================================
echo.

REM 1. Ensure dotnet CLI is available
where dotnet >nul 2>nul
if %ERRORLEVEL% neq 0 (
    echo [ERROR] .NET SDK is not found in your PATH!
    echo Please install the .NET 8 SDK or add dotnet to your system PATH.
    echo.
    pause
    exit /b 1
)

REM Navigate to repository root
cd /d "%~dp0"

REM 2. Check if BulkSwitchBackupPro.exe is currently running
tasklist /fi "imagename eq BulkSwitchBackupPro.exe" 2>nul | find /i "BulkSwitchBackupPro.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [INFO] Detected running instance of BulkSwitchBackupPro.exe.
    echo Closing running instance to allow binary overwrite...
    taskkill /f /im BulkSwitchBackupPro.exe >nul 2>nul
    timeout /t 1 /nobreak >nul
    echo.
)

REM 3. Build Solution in Release mode
echo [1/3] Compiling solution in Release configuration...
echo --------------------------------------------------------
dotnet build BulkBackup.sln -c Release --nologo --verbosity normal

if %ERRORLEVEL% neq 0 (
    echo.
    echo ********************************************************
    echo  [BUILD FAILED] One or more projects failed to compile!
    echo  Build has been ABORTED. Review error output above.
    echo ********************************************************
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ========================================================
echo  [BUILD PASSED] Solution compiled successfully!
echo ========================================================
echo.

REM 4. Build and publish Release standalone single-file executable into ./Publish and ./dist
echo [2/3] Publishing standalone single-file Release executable to .\Publish...
echo --------------------------------------------------------
dotnet publish BulkBackup.App/BulkBackup.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./Publish

if %ERRORLEVEL% neq 0 (
    echo.
    echo ********************************************************
    echo  [PUBLISH FAILED] Packaging error occurred!
    echo  Review the error output above.
    echo ********************************************************
    echo.
    pause
    exit /b %ERRORLEVEL%
)

echo [3/3] Preparing distribution directory .\dist...
echo --------------------------------------------------------
if not exist "dist" mkdir "dist"
copy /y "Publish\BulkSwitchBackupPro.exe" "dist\BulkSwitchBackupPro.exe" >nul

echo.
echo ========================================================
echo  [SUCCESS] Application published for distribution!
echo  Locations:
echo    - Publish\BulkSwitchBackupPro.exe
echo    - dist\BulkSwitchBackupPro.exe
echo ========================================================
echo.

if exist "Publish\BulkSwitchBackupPro.exe" (
    for %%F in ("Publish\BulkSwitchBackupPro.exe") do (
        set /a sizeMB=%%~zF / 1048576
        echo Binary Name: BulkSwitchBackupPro.exe
        echo Binary Size: !sizeMB! MB [%%~zF bytes]
        echo Timestamp:   %%~tF
    )
)

echo.
echo Standalone distribution package is ready!
