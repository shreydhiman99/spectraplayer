; Spectraplayer setup program (Inno Setup 6). Built by installer\Build-Installer.ps1, which publishes the app first.
; Everything the app needs is inside: the .NET runtime, the video engine (libmpv), the speech engine and, optionally,
; the speech model, so a fresh computer needs nothing else.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

#define AppName     "Spectraplayer"
#define Publisher   "NeuSpectra"
#define ExeName     "Spectraplayer.exe"
#define ModelFile   "ggml-base.en-q5_1.bin"

; one line per file type for the "Open with" list
#define OpenWithType(Ext) 'Root: HKA; Subkey: "Software\Classes\Applications\Spectraplayer.exe\SupportedTypes"; ValueType: string; ValueName: ".' + Ext + '"; ValueData: ""; Tasks: openwith'

[Setup]
AppId={{B9566147-6E40-4346-AF73-C1A2F8BA133A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#Publisher}
AppCopyright=© 2026 NeuSpectra
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#Publisher}
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} Setup

; Installs for the current user only: no administrator prompt and no "for whom?" question. The program goes to
; %LOCALAPPDATA%\Programs\Spectraplayer unless the user picks another folder.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

OutputDir=..\artifacts\installer
OutputBaseFilename=Spectraplayer-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes

WizardStyle=modern
; Inno Setup 6 hides the Welcome page by default; it is where the large NeuSpectra / Spectraplayer picture is shown.
DisableWelcomePage=no
SetupIconFile=..\src\SpectraPlayer.App\Assets\spectraplayer.ico
UninstallDisplayIcon={app}\{#ExeName}
; the NeuSpectra and Spectraplayer pictures, in several sizes so they stay sharp on high-DPI screens
WizardImageFile=images\wizard-164x314.bmp,images\wizard-192x386.bmp,images\wizard-246x459.bmp,images\wizard-328x628.bmp
WizardSmallImageFile=images\wizard-small-55x55.bmp,images\wizard-small-64x68.bmp,images\wizard-small-83x80.bmp,images\wizard-small-110x106.bmp

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] by NeuSpectra on your computer.%n%nSpectraplayer is a simple video player for studying: a tab for each subject, bookmarks and notes, and transcripts made on your own computer.%n%nIt is recommended that you close other applications before continuing.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "openwith"; Description: "Show {#AppName} in the ""Open with"" list for video and audio files"; GroupDescription: "Windows integration:"; Flags: unchecked
#ifdef HasModel
Name: "speechmodel"; Description: "Include the speech model, so transcripts work offline straight away (57 MB)"; GroupDescription: "Transcripts:"
#endif

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
#ifdef HasModel
; uninsneveruninstall: the model is the user's data, kept on uninstall unless they choose to delete everything
Source: "..\artifacts\model\{#ModelFile}"; DestDir: "{code:DataDir}\models"; Flags: ignoreversion uninsneveruninstall; Tasks: speechmodel
#endif

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\Applications\Spectraplayer.exe"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#AppName}"; Tasks: openwith; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Spectraplayer.exe\shell\open\command"; ValueType: string; ValueData: """{app}\{#ExeName}"" ""%1"""; Tasks: openwith
{#OpenWithType("mp4")}
{#OpenWithType("mkv")}
{#OpenWithType("avi")}
{#OpenWithType("mov")}
{#OpenWithType("wmv")}
{#OpenWithType("flv")}
{#OpenWithType("webm")}
{#OpenWithType("m4v")}
{#OpenWithType("ts")}
{#OpenWithType("m2ts")}
{#OpenWithType("mts")}
{#OpenWithType("mpg")}
{#OpenWithType("mpeg")}
{#OpenWithType("3gp")}
{#OpenWithType("ogv")}
{#OpenWithType("vob")}
{#OpenWithType("mp3")}
{#OpenWithType("flac")}
{#OpenWithType("wav")}
{#OpenWithType("m4a")}
{#OpenWithType("aac")}
{#OpenWithType("ogg")}
{#OpenWithType("opus")}

[Run]
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
{ Where the app keeps its library and models. The setup can be pointed elsewhere with /DATADIR=... (the app then needs
  the SPECTRAPLAYER_DATA environment variable set to the same folder). }
function DataDir(Param: String): String;
begin
  Result := ExpandConstant('{param:DATADIR|{localappdata}\Spectraplayer}');
end;

{ Uninstalling removes the program. The user's own data (tabs, bookmarks, notes, transcripts, watch progress and the
  speech model) is only deleted if they say so. Bookmark files next to their videos are never touched. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Dir: String;
begin
  if CurUninstallStep <> usPostUninstall then Exit;
  Dir := DataDir('');
  if (not UninstallSilent) and DirExists(Dir) then
    if MsgBox('Also delete your Spectraplayer library: tabs, notes, transcripts, watch progress and the speech model?' + #13#10 + #13#10 +
              Dir + #13#10 + #13#10 +
              'Choose No to keep it for a later reinstall. Bookmark files next to your videos are not affected either way.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(Dir, True, True, True);
end;
