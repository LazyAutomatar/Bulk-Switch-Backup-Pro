@echo off
setlocal enabledelayedexpansion

title Bulk Switch Backup Pro - Test and Build Pipeline

echo ========================================================
echo     Bulk Switch Backup Pro: Automated Build Pipeline    
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

REM 2. Check if BulkBackup.App.exe is currently running
tasklist /fi "imagename eq BulkBackup.App.exe" 2>nul | find /i "BulkBackup.App.exe" >nul
if %ERRORLEVEL% equ 0 (
    echo [INFO] Detected running instance of BulkBackup.App.exe.
    echo Closing running instance to allow binary overwrite...
    taskkill /f /im BulkBackup.App.exe >nul 2>nul
    timeout /t 1 /nobreak >nul
    echo.
)

REM 3. Build Solution in Release mode
echo [1/2] Compiling solution in Release configuration...
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

REM 4. Build and publish Release standalone executable into ./Publish
echo [2/2] Publishing standalone Release executable to .\Publish...
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

echo.
echo ========================================================
echo  [SUCCESS] Application built and published successfully!
echo  Location: Publish\BulkBackup.App.exe
echo ========================================================
echo.

if exist "Publish\BulkBackup.App.exe" (
    for %%F in ("Publish\BulkBackup.App.exe") do (
        set /a sizeMB=%%~zF / 1048576
        echo Binary Size: !sizeMB! MB [%%~zF bytes]
        echo Timestamp:   %%~tF
    )
)

echo.
echo Done! Press any key to exit.
pause >nul
