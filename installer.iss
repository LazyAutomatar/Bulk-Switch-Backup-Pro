#ifdef BetaBuild
#define MyAppName "Bulk Switch Backup Pro (Local Beta)"
#else
#define MyAppName "Bulk Switch Backup Pro"
#endif
#ifndef MyAppVersion
#define MyAppVersion "3.0.5"
#endif
#define MyAppPublisher "Yash Nalawde"
#define MyAppURL "https://github.com/LazyAutomatar/Bulk-Switch-Backup-Pro"
#define MyAppExeName "BulkSwitchBackupPro.exe"
#ifndef MyAppSourceDir
#define MyAppSourceDir "dist\win-x64"
#endif
#ifndef MyAppOutputDir
#define MyAppOutputDir "dist"
#endif

[Setup]
; Unique application identifier
#ifdef BetaBuild
AppId={{B24A6C15-88C3-4ED3-83C4-AB73DCBB1251}
#else
AppId={{144AB1DE-E5FB-4A96-BB38-046A06273849}
#endif
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
#ifdef BetaBuild
DefaultDirName={localappdata}\Programs\BulkSwitchBackupProBeta
#else
DefaultDirName={autopf}\{#MyAppName}
#endif
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir={#MyAppOutputDir}
#ifdef BetaBuild
OutputBaseFilename=BulkSwitchBackupPro_LocalBeta_Setup
#else
OutputBaseFilename=BulkSwitchBackupPro_Setup_v{#MyAppVersion}
#endif
SetupIconFile=BulkBackup.Desktop\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup Wizard
VersionInfoProductName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent


