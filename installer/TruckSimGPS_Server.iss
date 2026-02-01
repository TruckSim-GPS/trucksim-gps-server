; TruckSim GPS Telemetry Server - Inno Setup Script
; Requires Inno Setup 6.x or later

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

[Setup]
AppId={{A6A64D09-D8A9-4E29-9B86-0D0C81810DB0}
AppName=TruckSim GPS Telemetry Server
AppVersion={#AppVersion}
AppPublisher=TruckSim GPS
AppPublisherURL=https://trucksimgps.com/
DefaultDirName={autopf}\TruckSim GPS Telemetry Server
DefaultGroupName=TruckSim GPS
PrivilegesRequired=admin
UsedUserAreasWarning=no
OutputDir=Output
OutputBaseFilename=TruckSimGPS_Server_Setup_{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
RestartApplications=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64compatible
SetupIconFile=..\source\Funbit.Ets.Telemetry.Server\Resources\app_icon.ico
UninstallDisplayIcon={app}\TruckSimGPS_Server.exe

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"
Name: "startupentry"; Description: "Start with &Windows (minimized to tray)"; GroupDescription: "Startup:"; Flags: checkedonce

[Files]
Source: "..\source\Funbit.Ets.Telemetry.Server\bin\Release\TruckSimGPS_Server.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\source\Funbit.Ets.Telemetry.Server\bin\Release\TruckSimGPS_Server.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\source\Funbit.Ets.Telemetry.Server\bin\Release\dependencies\*"; DestDir: "{app}\dependencies"; Flags: ignoreversion recursesubdirs
Source: "..\source\Funbit.Ets.Telemetry.Server\bin\Release\TruckSimGPSPlugins\*"; DestDir: "{app}\TruckSimGPSPlugins"; Flags: ignoreversion recursesubdirs
Source: "vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\TruckSim GPS Telemetry Server"; Filename: "{app}\TruckSimGPS_Server.exe"
Name: "{group}\Uninstall TruckSim GPS Telemetry Server"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TruckSim GPS Telemetry Server"; Filename: "{app}\TruckSimGPS_Server.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "TruckSimGPS"; ValueData: """{app}\TruckSimGPS_Server.exe"" -minimized"; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Visual C++ Redistributable..."; Check: not IsVCRedistInstalled; Flags: waituntilterminated
Filename: "netsh"; Parameters: "advfirewall firewall add rule name=""TRUCKSIM GPS TELEMETRY SERVER (PORT 31377)"" dir=in action=allow protocol=TCP localport=31377 remoteip=localsubnet"; StatusMsg: "Configuring firewall..."; Flags: runhidden waituntilterminated
Filename: "{app}\TruckSimGPS_Server.exe"; Description: "Launch TruckSim GPS Telemetry Server"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "netsh"; Parameters: "advfirewall firewall delete rule name=""TRUCKSIM GPS TELEMETRY SERVER (PORT 31377)"""; Flags: runhidden; RunOnceId: "RemoveFirewallRule"
Filename: "netsh"; Parameters: "http delete urlacl url=http://+:31377/"; Flags: runhidden; RunOnceId: "RemoveUrlAcl"

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\dev"
Type: filesandordirs; Name: "{app}\docs"

[Code]
function IsVCRedistInstalled: Boolean;
var
  Installed: Cardinal;
  BuildNumber: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
    begin
      if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Bld', BuildNumber) then
      begin
        Result := BuildNumber >= 27000;
      end;
    end;
  end;
end;

function ExtractJsonValue(const Json, Key: String): String;
var
  SearchKey: String;
  StartPos, EndPos: Integer;
begin
  Result := '';
  SearchKey := '"' + Key + '"';
  StartPos := Pos(SearchKey, Json);
  if StartPos > 0 then
  begin
    StartPos := StartPos + Length(SearchKey);
    { Skip past colon and whitespace to opening quote }
    while (StartPos <= Length(Json)) and ((Json[StartPos] = ':') or (Json[StartPos] = ' ') or (Json[StartPos] = '"')) do
    begin
      if Json[StartPos] = '"' then
      begin
        StartPos := StartPos + 1;
        Break;
      end;
      StartPos := StartPos + 1;
    end;
    EndPos := StartPos;
    while (EndPos <= Length(Json)) and (Json[EndPos] <> '"') do
      EndPos := EndPos + 1;
    Result := Copy(Json, StartPos, EndPos - StartPos);
  end;
end;

procedure RemovePluginDlls(const GamePath: String);
var
  DllPath: String;
begin
  if (GamePath = '') or (GamePath = 'N/A') then
    Exit;

  DllPath := GamePath + '\bin\win_x64\plugins\trucksim-gps-telemetry.dll';
  if FileExists(DllPath) then
    DeleteFile(DllPath);

  DllPath := GamePath + '\bin\win_x86\plugins\trucksim-gps-telemetry.dll';
  if FileExists(DllPath) then
    DeleteFile(DllPath);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  LocalAppDataPath, SettingsFile, SettingsJson: String;
  Ets2Path, AtsPath: String;
  Lines: TArrayOfString;
  I: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    { Read game paths from Settings.json and remove plugin DLLs before deleting settings }
    SettingsFile := ExpandConstant('{localappdata}\TruckSim GPS Telemetry Server\Settings.json');
    if FileExists(SettingsFile) then
    begin
      if LoadStringsFromFile(SettingsFile, Lines) then
      begin
        SettingsJson := '';
        for I := 0 to GetArrayLength(Lines) - 1 do
          SettingsJson := SettingsJson + Lines[I];

        Ets2Path := ExtractJsonValue(SettingsJson, 'Ets2GamePath');
        AtsPath := ExtractJsonValue(SettingsJson, 'AtsGamePath');

        if MsgBox('Do you want to remove the telemetry plugin from your game folders?', mbConfirmation, MB_YESNO) = IDYES then
        begin
          RemovePluginDlls(Ets2Path);
          RemovePluginDlls(AtsPath);
        end;
      end;
    end;
  end;

  if CurUninstallStep = usPostUninstall then
  begin
    if MsgBox('Do you want to remove saved settings (game paths, preferences)?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      LocalAppDataPath := ExpandConstant('{localappdata}\TruckSim GPS Telemetry Server');
      if DirExists(LocalAppDataPath) then
        DelTree(LocalAppDataPath, True, True, True);
    end;
  end;
end;
