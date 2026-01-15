; WisprClone Installer Script for Inno Setup
; https://jrsoftware.org/isinfo.php

#define MyAppName "WisprClone"
#define MyAppVersion "1.2.0"
#define MyAppPublisher "WisprClone"
#define MyAppExeName "WisprClone.exe"
#define MyAppDescription "Cross-platform voice-to-text transcription tool"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
AppId={{8F4E3B2A-1C5D-4E6F-9A8B-7C2D1E3F4A5B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output settings
OutputDir=.\output
OutputBaseFilename=WisprClone-Setup-{#MyAppVersion}
; Compression settings for smaller installer
Compression=lzma2/ultra64
SolidCompression=yes
; Modern wizard style
WizardStyle=modern
; Require admin for Program Files installation
PrivilegesRequired=admin
; 64-bit only
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
; Uninstall settings
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; Version info
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDescription}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
; Disable directory page for simpler install
DisableDirPage=no
; Show "Ready to Install" page
DisableReadyPage=no
; Update/replace files even if same version (for reinstalls)
SetupMutex={#MyAppName}SetupMutex

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start {#MyAppName} when Windows starts"; GroupDescription: "Startup Options:"

[Files]
; Include all files from the publish directory
Source: "..\src\WisprClone.Avalonia\bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu icons
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Comment: "{#MyAppDescription}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop icon (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; Comment: "{#MyAppDescription}"

[Registry]
; Windows startup entry (optional)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon
; Store installed version for upgrade checking
Root: HKLM; Subkey: "Software\{#MyAppName}"; ValueType: string; ValueName: "InstalledVersion"; ValueData: "{#MyAppVersion}"; Flags: uninsdeletekey

[Run]
; Launch application after installation (optional)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up user settings folder on uninstall (optional - commented out to preserve settings)
; Type: filesandordirs; Name: "{localappdata}\{#MyAppName}"

[Code]
// Compare version strings (returns: -1 if V1 < V2, 0 if equal, 1 if V1 > V2)
function CompareVersions(V1, V2: String): Integer;
var
  P1, P2: Integer;
  N1, N2: Integer;
  S1, S2: String;
begin
  Result := 0;
  S1 := V1;
  S2 := V2;

  while (Length(S1) > 0) or (Length(S2) > 0) do
  begin
    // Extract first number from S1
    P1 := Pos('.', S1);
    if P1 > 0 then
    begin
      N1 := StrToIntDef(Copy(S1, 1, P1 - 1), 0);
      S1 := Copy(S1, P1 + 1, Length(S1));
    end
    else
    begin
      N1 := StrToIntDef(S1, 0);
      S1 := '';
    end;

    // Extract first number from S2
    P2 := Pos('.', S2);
    if P2 > 0 then
    begin
      N2 := StrToIntDef(Copy(S2, 1, P2 - 1), 0);
      S2 := Copy(S2, P2 + 1, Length(S2));
    end
    else
    begin
      N2 := StrToIntDef(S2, 0);
      S2 := '';
    end;

    // Compare
    if N1 < N2 then
    begin
      Result := -1;
      Exit;
    end
    else if N1 > N2 then
    begin
      Result := 1;
      Exit;
    end;
  end;
end;

// Check if the application process is running
function IsAppRunning(): Boolean;
var
  ResultCode: Integer;
begin
  // Use tasklist to check if process exists
  Result := False;
  if Exec('cmd.exe', '/c tasklist /FI "IMAGENAME eq {#MyAppExeName}" 2>NUL | find /I "{#MyAppExeName}" >NUL', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := (ResultCode = 0);
  end;
end;

// Stop the running application
function StopApp(): Boolean;
var
  ResultCode: Integer;
  RetryCount: Integer;
begin
  Result := True;
  RetryCount := 0;

  while IsAppRunning() and (RetryCount < 5) do
  begin
    // Try to close gracefully first, then force
    if RetryCount < 2 then
      Exec('taskkill.exe', '/IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    else
      Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    Sleep(1000);
    RetryCount := RetryCount + 1;
  end;

  Result := not IsAppRunning();
end;

// Get the currently installed version from registry
function GetInstalledVersion(): String;
var
  InstalledVersion: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM, 'Software\{#MyAppName}', 'InstalledVersion', InstalledVersion) then
    Result := InstalledVersion;
end;

function InitializeSetup(): Boolean;
var
  InstalledVersion: String;
  VersionCompare: Integer;
begin
  Result := True;

  // Check for existing installation and version
  InstalledVersion := GetInstalledVersion();

  if InstalledVersion <> '' then
  begin
    VersionCompare := CompareVersions('{#MyAppVersion}', InstalledVersion);

    // Prevent downgrade
    if VersionCompare < 0 then
    begin
      MsgBox('A newer version of {#MyAppName} (' + InstalledVersion + ') is already installed.' + #13#10 + #13#10 +
             'This installer contains version {#MyAppVersion}.' + #13#10 + #13#10 +
             'Please uninstall the existing version first if you want to downgrade.',
             mbError, MB_OK);
      Result := False;
      Exit;
    end;

    // Same version - confirm reinstall
    if VersionCompare = 0 then
    begin
      if MsgBox('{#MyAppName} version {#MyAppVersion} is already installed.' + #13#10 + #13#10 +
                'Do you want to reinstall it?', mbConfirmation, MB_YESNO) = IDNO then
      begin
        Result := False;
        Exit;
      end;
    end;
  end;

  // Check if application is running and stop it
  if IsAppRunning() then
  begin
    if MsgBox('{#MyAppName} is currently running.' + #13#10 + #13#10 +
              'The installer needs to close it to continue. Do you want to close it now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not StopApp() then
      begin
        MsgBox('Could not close {#MyAppName}. Please close it manually and try again.',
               mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end
    else
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;

  // Check if application is running and stop it
  if IsAppRunning() then
  begin
    if MsgBox('{#MyAppName} is currently running.' + #13#10 + #13#10 +
              'The uninstaller needs to close it to continue. Do you want to close it now?',
              mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not StopApp() then
      begin
        MsgBox('Could not close {#MyAppName}. Please close it manually and try again.',
               mbError, MB_OK);
        Result := False;
        Exit;
      end;
    end
    else
    begin
      Result := False;
      Exit;
    end;
  end;
end;
