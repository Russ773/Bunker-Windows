; Build with: iscc /DAppVersion=1.0.18 /DSourceDir=..\publish installer\Bunker.iss
#ifndef AppVersion
  #define AppVersion "1.0.18"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; Keep this ID the same forever: it is how updates replace the existing install.
AppId={{B6F3C7E2-4A51-4C8E-9D2B-5E1A7C3F0A11}
AppName=Bunker
AppVersion={#AppVersion}
AppVerName=Bunker {#AppVersion}
AppPublisher=Bunker
AppPublisherURL=https://github.com/Russ773/Bunker-Windows
AppSupportURL=https://github.com/Russ773/Bunker-Windows/issues
AppUpdatesURL=https://github.com/Russ773/Bunker-Windows/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\Bunker
DefaultGroupName=Bunker
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=Bunker-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
UninstallDisplayName=Bunker
UninstallDisplayIcon={app}\Bunker.exe

; Look and feel
WizardStyle=modern
SetupIconFile=..\src\Bunker.ico
WizardImageFile=images\wizard-164.bmp,images\wizard-328.bmp
WizardSmallImageFile=images\small-55.bmp,images\small-110.bmp
WizardImageBackColor=$171311
WizardImageAlphaFormat=defined

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
BeveledLabel=Bunker {#AppVersion}
WelcomeLabel1=Welcome to Bunker
WelcomeLabel2=Bunker lets you watch your Rust server's console and manage it from your desktop, without having to be in the game.%n%nThis will install Bunker {#AppVersion} on your computer. It's a good idea to close any running copy of Bunker before you continue.
FinishedHeadingLabel=Bunker is ready
FinishedLabelNoIcons=Bunker has been installed.
FinishedLabel=Bunker has been installed. You can start it from the Start menu or a shortcut.

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Bunker"; Filename: "{app}\Bunker.exe"
Name: "{group}\Uninstall Bunker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Bunker"; Filename: "{app}\Bunker.exe"; Tasks: desktopicon

[UninstallDelete]
; Everything left in the install folder, including files Bunker created while running.
Type: filesandordirs; Name: "{app}"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"

[Run]
; No skipifsilent: after an update (silent install) the app starts again by itself.
Filename: "{app}\Bunker.exe"; Description: "Launch Bunker"; Flags: nowait postinstall

[Code]
// Uninstalling removes everything Bunker put on the PC: the program, and (after asking) the settings and data it saved.
// All of that data lives in one folder, %APPDATA%\Bunker: saved servers and their encrypted passwords, player notes,
// widgets, schedules and the item pictures.

function DataFolder(): String;
var
  Custom: String;
begin
  // BUNKER_DATA_DIR is the folder Bunker itself uses when it is set (for testing). Only trusted if it really looks like a Bunker data folder.
  Custom := GetEnv('BUNKER_DATA_DIR');
  if (Custom <> '') and FileExists(Custom + '\settings.json') then
    Result := Custom
  else
    Result := ExpandConstant('{userappdata}\Bunker');
end;

// The installer Bunker downloads when it updates itself is left in the temp folder.
procedure RemoveDownloadedInstallers();
var
  Temp: String;
  Found: TFindRec;
begin
  Temp := ExpandConstant('{%TEMP}');
  if (Temp <> '') and FindFirst(Temp + '\Bunker-Setup-*.exe', Found) then
  begin
    try
      repeat
        DeleteFile(Temp + '\' + Found.Name);
      until not FindNext(Found);
    finally
      FindClose(Found);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Folder: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  RemoveDownloadedInstallers();
  Folder := DataFolder();
  if not DirExists(Folder) then Exit;
  // A silent uninstall means "remove everything"; otherwise ask once, with Yes as the default.
  if UninstallSilent() or (MsgBox('Bunker has been removed.' + #13#10#13#10 +
      'Do you also want to delete everything it saved for you? This includes your saved servers and their passwords, player notes, schedules, widgets and the Rust item pictures.' + #13#10#13#10 +
      'This cannot be undone.', mbConfirmation, MB_YESNO) = IDYES) then
    DelTree(Folder, True, True, True);
end;
