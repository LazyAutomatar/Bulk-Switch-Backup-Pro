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
    echo Please install the .NET 10 SDK or add dotnet to your system PATH.
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

REM 4. Build and publish Release standalone single-file executable using publish-all.ps1
echo [2/2] Running production publishing pipeline (win-x64)...
echo --------------------------------------------------------
powershell -NoProfile -ExecutionPolicy Bypass -File .\publish-all.ps1 -Target win-x64 -Configuration Release

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

echo.
echo ========================================================
echo  [SUCCESS] Application published for distribution!
echo  Location: dist\BulkSwitchBackupPro.exe
echo ========================================================
echo.

if exist "dist\BulkSwitchBackupPro.exe" (
    for %%F in ("dist\BulkSwitchBackupPro.exe") do (
        set /a sizeMB=%%~zF / 1048576
        echo Binary Name: BulkSwitchBackupPro.exe
        echo Binary Size: !sizeMB! MB [%%~zF bytes]
        echo Timestamp:   %%~tF
    )
)

echo.
echo Standalone distribution package is ready!
pause
