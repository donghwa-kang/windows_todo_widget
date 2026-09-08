#define AppVersion "1.0.0"
[Setup]
AppId={{96231E6D-9DAE-4A12-897A-64C2B0F0480A}
AppName=Windows Todo Widget
AppVersion={#AppVersion}
AppPublisher=donghwa-kang
AppPublisherURL=https://github.com/donghwa-kang/windows_todo_widget
DefaultDirName={localappdata}\Programs\WindowsTodoWidget
DefaultGroupName=Windows Todo Widget
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir=..\artifacts\release
OutputBaseFilename=windows_todo_widget-setup-{#AppVersion}-win-x64
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\TerminalWidget.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
AppMutex=Local\TerminalWidget.Desktop.v1
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면 바로가기 만들기"; GroupDescription: "바로가기:"

[Files]
Source: "..\artifacts\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,*.log,*.bak,*.tmp,workspace*.json,notion-cache*.json,klas-cache*.json"

[Icons]
Name: "{group}\Windows Todo Widget"; Filename: "{app}\TerminalWidget.exe"; Parameters: "--window"; WorkingDir: "{app}"
Name: "{group}\Windows Todo Widget 제거"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Windows Todo Widget"; Filename: "{app}\TerminalWidget.exe"; Parameters: "--window"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\TerminalWidget.exe"; Parameters: "--window"; Description: "Windows Todo Widget 실행"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupValue: String;
begin
  if CurUninstallStep = usUninstall then begin
    // 다른 위치의 위젯이 등록한 자동 시작 값은 건드리지 않습니다.
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TerminalWidget', StartupValue) then
      if CompareText(StartupValue, '"' + ExpandConstant('{app}\TerminalWidget.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'TerminalWidget');
  end;
end;
