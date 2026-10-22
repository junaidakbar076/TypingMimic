#ifndef Mode
  #error Mode must be Online or Offline
#endif
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#if Mode == "Online"
  #include RuntimeInclude
#endif

[Setup]
AppId={{E6FE3AD9-7637-4A41-98C2-99E66DAC41D8}
AppName=TypingMimic
AppVersion={#AppVersion}
AppVerName=TypingMimic {#AppVersion} ({#Mode} setup)
DefaultDirName={localappdata}\Programs\TypingMimic
DefaultGroupName=TypingMimic
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#InstallerOutput}
OutputBaseFilename=TypingMimic-{#AppVersion}-{#Mode}-Setup
Compression=lzma2/ultra64
SolidCompression=yes
LZMANumBlockThreads=2
WizardStyle=modern
SetupIconFile={#SourcePath}\..\src\TypingMimic.App\app.ico
UninstallDisplayIcon={app}\{#Mode}\TypingMimic.exe
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
InfoBeforeFile={#SourcePath}\{#Mode}-info.txt

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}\{#Mode}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourcePath}\..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TypingMimic"; Filename: "{app}\{#Mode}\TypingMimic.exe"
Name: "{autodesktop}\TypingMimic"; Filename: "{app}\{#Mode}\TypingMimic.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Mode}\TypingMimic.exe"; Description: "Launch TypingMimic"; Flags: nowait postinstall skipifsilent unchecked

#if Mode == "Online"
[Code]
var
  RuntimeDownload: TDownloadWizardPage;

function FrameworkPresentAt(Root: Integer; Framework: String): Boolean;
var
  Versions: TArrayOfString;
  I: Integer;
  Installed: Cardinal;
  RegistryPath: String;
begin
  Result := False;
  RegistryPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\' + Framework;
  if RegGetValueNames(Root, RegistryPath, Versions) then
    for I := 0 to GetArrayLength(Versions) - 1 do
      if (Pos('8.0.', Versions[I]) = 1) and (Pos('-', Versions[I]) = 0) then
        if RegQueryDWordValue(Root, RegistryPath, Versions[I], Installed) then
          if Installed = 1 then begin
            Result := True;
            Exit;
          end;
end;

function RuntimePresent: Boolean;
begin
  Result :=
    (FrameworkPresentAt(HKLM64, 'Microsoft.WindowsDesktop.App') or
     FrameworkPresentAt(HKLM32, 'Microsoft.WindowsDesktop.App')) and
    (FrameworkPresentAt(HKLM64, 'Microsoft.NETCore.App') or
     FrameworkPresentAt(HKLM32, 'Microsoft.NETCore.App'));
end;

procedure InitializeWizard;
begin
  RuntimeDownload := CreateDownloadPage('Installing .NET Desktop Runtime',
    'Downloading the Microsoft runtime required by TypingMimic. Windows may ask for administrator approval.', nil);
  RuntimeDownload.ShowBaseNameInsteadOfUrl := True;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if RuntimePresent then begin
    Log('Compatible .NET 8 x64 Desktop Runtime already installed; no download needed.');
    Exit;
  end;
  RuntimeDownload.Clear;
  RuntimeDownload.Add('{#RuntimeUrl}', 'windowsdesktop-runtime.exe', '{#RuntimeSha256}');
  RuntimeDownload.Show;
  try
    try
      RuntimeDownload.Download;
      if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
          '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
        Result := 'Could not start the .NET installer: ' + SysErrorMessage(ExitCode) +
          '. Approve the administrator prompt, or use the Offline setup.'
      else if (ExitCode <> 0) and (ExitCode <> 3010) and (ExitCode <> 1641) then
        Result := 'Microsoft .NET installation failed (exit code ' + IntToStr(ExitCode) +
          '). Retry or use the Offline setup.'
      else if (ExitCode = 3010) or (ExitCode = 1641) then begin
        NeedsRestart := True;
        Result := 'Windows needs to restart after installing .NET. Restart and run this setup again.';
      end
      else if not RuntimePresent then
        Result := 'The .NET 8 x64 Desktop Runtime could not be detected after installation. Restart and retry, or use the Offline setup.';
    except
      Result := 'Could not download or verify the Microsoft runtime: ' + GetExceptionMessage +
        '. Check your internet connection and retry, or use the Offline setup.';
    end;
  finally
    RuntimeDownload.Hide;
  end;
end;
#endif
