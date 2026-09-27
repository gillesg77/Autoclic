; Installeur AutoClic — Inno Setup 6.1 ou supérieur.
;
; Compilation :
;   ISCC.exe installer\AutoClic.iss
; La publication doit avoir été faite au préalable :
;   dotnet publish src/AutoClic.App -c Release -r win-x64 -o publish
;
; Le Windows App SDK est embarqué dans la publication ; la seule dépendance
; extérieure est le .NET 8 Desktop Runtime, détecté puis téléchargé si absent.

#define AppName       "AutoClic"
#define AppVersion    "0.1.4"
#define AppPublisher  "gillesg77"
#define AppExe        "AutoClic.exe"

; Lien permanent de Microsoft : il redirige toujours vers le dernier correctif de
; la branche 8.0. Vérifié au moment de l'écriture : 8.0.31.
#define DotNetUrl     "https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe"

[Setup]
AppId={{7C3A9E14-5B62-4D08-9F31-2A6E84D1C907}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
; Sans cela, setup.exe part sans numéro de version : impossible de dire,
; devant un fichier téléchargé, lequel des deux on tient.
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoCompany={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
OutputDir=..\dist
OutputBaseFilename=AutoClic-{#AppVersion}-setup
SetupIconFile=..\src\AutoClic.App\Assets\AutoClic.ico
LicenseFile=..\LICENSE
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes

; L'application est publiée pour win-x64 uniquement.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; Requis : écriture dans Program Files, et installation éventuelle du runtime.
PrivilegesRequired=admin

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; La GPL impose que la licence accompagne le binaire distribué.
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PageTelechargement: TDownloadWizardPage;

{ Détection du .NET 8 Desktop Runtime.

  Par le dossier du framework partagé, et non par le registre : sur le poste de
  développement, la clé HKLM\SOFTWARE\dotnet\Setup\InstalledVersions était absente
  alors que le runtime 8.0.31 était bien installé. Le dossier, lui, ne ment pas. }
function DotNetDesktopPresent: Boolean;
var
  Base: String;
  Dossiers: TFindRec;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');

  if not DirExists(Base) then
    Exit;

  if FindFirst(Base + '\8.*', Dossiers) then
  begin
    try
      repeat
        if (Dossiers.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
        begin
          Result := True;
          Exit;
        end;
      until not FindNext(Dossiers);
    finally
      FindClose(Dossiers);
    end;
  end;
end;

function SuiviTelechargement(const FileName, URL: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if ProgressMax <> 0 then
    PageTelechargement.SetProgress(Progress, ProgressMax);
  Result := True;
end;

{ Télécharge le runtime et l'installe. Renvoie '' si tout s'est bien passé, sinon le
  message à montrer. Progression indique s'il y a un assistant pour l'afficher. }
function PoserRuntime(Progression: Boolean): String;
var
  CodeRetour: Integer;
begin
  Result := '';

  try
    if Progression then
    begin
      PageTelechargement.Clear;
      PageTelechargement.Add('{#DotNetUrl}', 'windowsdesktop-runtime.exe', '');
      PageTelechargement.Show;
      try
        PageTelechargement.Download;
      finally
        PageTelechargement.Hide;
      end;
    end
    else
      DownloadTemporaryFile('{#DotNetUrl}', 'windowsdesktop-runtime.exe', '', nil);
  except
    { Derrière un proxy d'entreprise, le téléchargement peut échouer : on le dit
      clairement plutôt que d'installer une application qui ne démarrera pas. }
    Result :=
      'Le .NET 8 Desktop Runtime n''a pas pu être téléchargé.' + #13#10#13#10 +
      AddPeriod(GetExceptionMessage) + #13#10#13#10 +
      'Installez-le manuellement depuis dotnet.microsoft.com, puis relancez cette installation.';
    Exit;
  end;

  if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
              '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, CodeRetour) then
    CodeRetour := -1;

  { 0 = installé, 3010 = installé, redémarrage nécessaire. }
  if (CodeRetour <> 0) and (CodeRetour <> 3010) then
    Result :=
      Format('L''installation du .NET 8 Desktop Runtime a échoué (code %d).', [CodeRetour]) + #13#10#13#10 +
      'Installez-le manuellement depuis dotnet.microsoft.com, puis relancez cette installation.';
end;

procedure InitializeWizard;
begin
  PageTelechargement := CreateDownloadPage(
    'Composant requis',
    'Téléchargement du .NET 8 Desktop Runtime',
    @SuiviTelechargement);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Souci: String;
begin
  Result := True;

  if (CurPageID <> wpReady) or WizardSilent or DotNetDesktopPresent then
    Exit;

  Souci := PoserRuntime(True);

  if Souci <> '' then
  begin
    SuppressibleMsgBox(Souci, mbCriticalError, MB_OK, IDOK);
    Result := False;
  end;
end;

{ Filet pour l'installation silencieuse.

  En mode silencieux aucune page d'assistant n'existe, donc NextButtonClick n'est
  jamais appelé : sans ceci, une installation automatisée poserait AutoClic sans son
  runtime, et l'application ne démarrerait pas — sans que rien ne l'ait signalé.
  PrepareToInstall, lui, est appelé dans les deux modes. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';

  if WizardSilent and not DotNetDesktopPresent then
    Result := PoserRuntime(False);
end;
