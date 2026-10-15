; Installeur NHMonitor (Inno Setup 6).
; Compilation : build\build-release.ps1 (ou ISCC.exe /DAppVersion=1.0.0 installer\NHMonitor.iss)
;
; Choix de sécurité :
; - installation forcée dans Program Files (DisableDirPage) : dossier non modifiable sans droits admin.
;   L'application tourne en administrateur ; installée dans un dossier de l'utilisateur, n'importe quel
;   programme pourrait remplacer l'exécutable et obtenir ces droits ;
; - PawnIO est téléchargé depuis sa source officielle, version figée, et vérifié par SHA-256 avant exécution ;
; - les programmes système sont appelés par chemin complet ({sys}), jamais via le PATH.

#define AppName "NHMonitor"
#define AppExe "NHMonitor.exe"
#define AppUrl "https://github.com/NiicOw974/NHMonitor"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

#define PawnIOVersion "2.2.0"
#define PawnIOUrl "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe"
#define PawnIOSha256 "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032"

[Setup]
AppId={{8F6C3D2A-5B7E-4C1F-9A3D-6E2B1C4F7A90}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Nicolas Hoareau
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={autopf}\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=NHMonitor-Setup-{#AppVersion}
SetupIconFile=..\assets\NHMonitor.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
VersionInfoVersion={#AppVersion}
VersionInfoCompany=Nicolas Hoareau
VersionInfoDescription={#AppName} Setup
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.TaskOptions=Options :
english.TaskOptions=Options:
french.TaskStartup=Démarrer NHMonitor avec Windows
english.TaskStartup=Start NHMonitor with Windows
french.TaskPawnIO=Installer le driver signé PawnIO {#PawnIOVersion} pour les températures CPU (téléchargé depuis github.com/namazso)
english.TaskPawnIO=Install the signed PawnIO {#PawnIOVersion} driver for CPU temperatures (downloaded from github.com/namazso)
french.ConfiguringStartup=Configuration du démarrage automatique...
english.ConfiguringStartup=Configuring automatic startup...
french.LaunchApp=Lancer NHMonitor
english.LaunchApp=Launch NHMonitor
french.PawnIOFailed=PawnIO n'a pas pu être installé (%1). NHMonitor fonctionnera sans les températures CPU ; vous pourrez installer PawnIO plus tard avec : winget install namazso.PawnIO
english.PawnIOFailed=PawnIO could not be installed (%1). NHMonitor will work without CPU temperatures; you can install PawnIO later with: winget install namazso.PawnIO

[Tasks]
Name: "startup"; Description: "{cm:TaskStartup}"; GroupDescription: "{cm:TaskOptions}"
Name: "pawnio"; Description: "{cm:TaskPawnIO}"; GroupDescription: "{cm:TaskOptions}"; Check: not IsPawnIOInstalled
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--enable-startup"; Flags: runhidden waituntilterminated; Tasks: startup; StatusMsg: "{cm:ConfiguringStartup}"
Filename: "{app}\{#AppExe}"; Parameters: "--disable-startup"; Flags: runhidden waituntilterminated; Tasks: not startup
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée par NHMonitor lui-même (/AUTOUPDATE=1) : on relance l'application à la fin.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsAutoUpdate

[UninstallDelete]
; Installeurs téléchargés par la mise à jour automatique.
Type: filesandordirs; Name: "{app}\updates"

[UninstallRun]
Filename: "{app}\{#AppExe}"; Parameters: "--disable-startup"; Flags: runhidden waituntilterminated; RunOnceId: "DisableStartup"

[Code]
function IsAutoUpdate: Boolean;
begin
  Result := ExpandConstant('{param:AUTOUPDATE|0}') = '1';
end;

function IsPawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

procedure StopRunningApp;
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  StopRunningApp;

  if WizardIsTaskSelected('pawnio') then
  begin
    try
      // Version figée + empreinte SHA-256 : Inno Setup refuse le fichier s'il ne correspond pas.
      DownloadTemporaryFile('{#PawnIOUrl}', 'PawnIO_setup.exe', '{#PawnIOSha256}', nil);
    except
      SuppressibleMsgBox(FmtMessage(CustomMessage('PawnIOFailed'), [GetExceptionMessage]), mbError, MB_OK, IDOK);
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Installer: String;
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('pawnio') then
  begin
    Installer := ExpandConstant('{tmp}\PawnIO_setup.exe');
    if FileExists(Installer) then
    begin
      if not Exec(Installer, '-install -silent', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
        SuppressibleMsgBox(FmtMessage(CustomMessage('PawnIOFailed'), [SysErrorMessage(ResultCode)]), mbError, MB_OK, IDOK)
      else if ResultCode <> 0 then
        SuppressibleMsgBox(FmtMessage(CustomMessage('PawnIOFailed'), ['code ' + IntToStr(ResultCode)]), mbError, MB_OK, IDOK);
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  StopRunningApp;
  Result := True;
end;
