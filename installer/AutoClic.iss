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
#define AppVersion    "0.1.2"
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

procedure InitializeWizard;
begin
  PageTelechargement := CreateDownloadPage(
    'Composant requis',
    'Téléchargement du .NET 8 Desktop Runtime',
    @SuiviTelechargement);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  CodeRetour: Integer;
begin
  Result := True;

  if (CurPageID <> wpReady) or DotNetDesktopPresent then
    Exit;

  PageTelechargement.Clear;
  PageTelechargement.Add('{#DotNetUrl}', 'windowsdesktop-runtime.exe', '');
  PageTelechargement.Show;

  try
    try
      PageTelechargement.Download;
    except
      { Derrière un proxy d'entreprise, le téléchargement peut échouer : on le dit
        clairement plutôt que d'installer une application qui ne démarrera pas. }
      SuppressibleMsgBox(
        'Le .NET 8 Desktop Runtime n''a pas pu être téléchargé.' + #13#10#13#10 +
        AddPeriod(GetExceptionMessage) + #13#10#13#10 +
        'Installez-le manuellement depuis dotnet.microsoft.com, puis relancez cette installation.',
        mbCriticalError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;

    if not Exec(ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
                '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, CodeRetour) then
      CodeRetour := -1;

    { 0 = installé, 3010 = installé, redémarrage nécessaire. }
    if (CodeRetour <> 0) and (CodeRetour <> 3010) then
    begin
      SuppressibleMsgBox(
        Format('L''installation du .NET 8 Desktop Runtime a échoué (code %d).', [CodeRetour]) + #13#10#13#10 +
        'Installez-le manuellement depuis dotnet.microsoft.com, puis relancez cette installation.',
        mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    PageTelechargement.Hide;
  end;
end;
