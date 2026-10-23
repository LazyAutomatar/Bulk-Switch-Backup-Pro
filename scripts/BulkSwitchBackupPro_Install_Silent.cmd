@echo off
setlocal
rem Bulk Switch Backup Pro - Author: Yash Nalawde
rem Place beside BulkSwitchBackupPro_Setup.exe or pass the installer path as argument 1.
set "INSTALLER=%~dp0BulkSwitchBackupPro_Setup.exe"
if not "%~1"=="" set "INSTALLER=%~f1"
if not exist "%INSTALLER%" (
  echo Installer not found: "%INSTALLER%"
  echo Download BulkSwitchBackupPro_Setup.exe into the same folder as this launcher.
  exit /b 2
)
rem Never reboot or launch the application after unattended installation.
rem Optional arguments 2 and 3 override the install directory and log path.
set "INSTALLDIR="
if not "%~2"=="" set INSTALLDIR=/DIR="%~f2"
set "INSTALLLOG=%TEMP%\BulkSwitchBackupPro-Silent-Install.log"
if not "%~3"=="" set "INSTALLLOG=%~f3"
start "" /wait "%INSTALLER%" /SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /NORESTARTAPPLICATIONS /LOG="%INSTALLLOG%" %INSTALLDIR%
set "INSTALLRESULT=%ERRORLEVEL%"
echo Installer exit code: %INSTALLRESULT%. Log: "%INSTALLLOG%"
exit /b %INSTALLRESULT%
