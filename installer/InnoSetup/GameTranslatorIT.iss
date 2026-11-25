; Script Inno Setup 6 per GameTranslator IT (installatore EXE classico).
; Richiede Inno Setup 6: https://jrsoftware.org/isinfo.php

#define MyAppName "GameTranslator IT"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.3"
#endif
#define MyAppPublisher "GameTranslator IT"
#define MyAppExeName "GameTranslatorIT.App.exe"
#define MyAppId "{0A2515B7-1D77-458A-9825-66621AC7C180}"

[Setup]
AppId={{#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
AppPublisher={#MyAppPublisher}
; La cartella di destinazione viene scelta dall'utente nella pagina dedicata.
; {userpf} ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¨ sempre scrivibile senza diritti di amministratore.
DefaultDirName={userpf}\GameTranslatorIT
DisableDirPage=no
DisableProgramGroupPage=yes
OutputDir=..\..\dist\installer
OutputBaseFilename=GameTranslatorIT-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
; L'interfaccia dell'installatore ÃƒÆ’Ã†â€™Ãƒâ€šÃ‚Â¨ solo in italiano.
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Files]
Source: "..\..\dist\GameTranslatorIT\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LibreTranslate\SetupLibreTranslate.ps1"; DestDir: "{app}\tools"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crea una icona sul desktop"; GroupDescription: "Icone:"

[Run]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\tools\SetupLibreTranslate.ps1"""; Description: "Installa il motore di traduzione locale (richiede Python 3)"; Flags: postinstall nowait skipifsilent
Filename: "{app}\{#MyAppExeName}"; Description: "Avvia {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"
