#define AppExeName "GibddExamSimulator.exe"
#define AppDisplayName "АРМ кандидата в водители — неофициальный тренажёр"

#ifndef AppVersion
  #define AppVersion "2.0.4"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\..\..\outputs"
#endif

[Setup]
AppId={{B3F4D2AB-2E82-46F0-A86E-92A76835B4DF}
AppName={#AppDisplayName}
AppVersion={#AppVersion}
AppVerName={#AppDisplayName} {#AppVersion}
AppPublisher=GibddExamSimulator Project
AppComments=Неофициальный тренажёр. Не является программным обеспечением МВД России или Госавтоинспекции.
AppCopyright=Copyright (C) 2026 GibddExamSimulator Project
DefaultDirName={autopf}\GibddExamSimulator
DefaultGroupName=АРМ кандидата в водители
DisableProgramGroupPage=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousLanguage=yes
UsePreviousTasks=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
ChangesAssociations=no
ChangesEnvironment=no
Uninstallable=yes
CreateUninstallRegKey=yes
UninstallDisplayName={#AppDisplayName}
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\assets\branding\windows-app.ico
OutputDir={#OutputDir}
OutputBaseFilename=GibddExamSimulator-Setup-{#AppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=classic
RedirectionGuard=yes
VersionInfoCompany=GibddExamSimulator Project
VersionInfoDescription=Установщик неофициального тренажёра теоретического экзамена
VersionInfoProductName={#AppDisplayName}
VersionInfoProductVersion={#AppVersion}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\АРМ кандидата в водители"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "Запустить режим кандидата"
Name: "{autodesktop}\АРМ кандидата в водители"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppDisplayName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser
