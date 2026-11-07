; WorkStation 工作台 —— Windows 安装包定义
;
; 用 Inno Setup 6 编译（ISCC.exe）。产物是标准 Windows 安装程序：
; 有开始菜单 / 桌面快捷方式、注册表安装信息、自己的卸载器，
; 在「设置 → 应用 → 已安装的应用」里能看到，也能从那里卸载。
;
; 关键设计：应用本身是**自包含发布**（自带 .NET 运行时），所以安装过程
; 不需要检测运行时、不需要联网下载任何东西 —— 装完就能用。
; 这是上一版「脚本 + 手动下载运行时」方案最大的痛点：用户要多点几个界面，
; 还得担心下载失败。
;
; 编译：tools\build-installer.ps1

#define AppName "WorkStation 工作台"
#define AppNameEn "WorkStation"
#define AppVersion "1.0.1"
#define AppPublisher "Joont"
#define AppExeName "WorkStation.exe"

[Setup]
AppId={{7C4E8A3B-9D51-4F6A-B2E7-3A1C5D8E9F04}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppComments=个人工作台：项目、任务、看板与提醒

; 默认装到用户目录，不需要管理员权限。
; 团队内部工具大多数人是自己用，装到 Program Files 反而要弹 UAC。
DefaultDirName={localappdata}\Programs\{#AppNameEn}
DefaultGroupName={#AppNameEn}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; 欢迎页（默认就是开的，写出来是为了在 .iss 里一眼看到没被谁关掉）
DisableWelcomePage=no

; 单用户安装：注册表写到 HKCU，不需要提权。
; 不开 PrivilegesRequiredOverridesAllowed —— 那样会多出「仅为您安装 / 为所有用户安装」
; 一页，对一个数据存在 %APPDATA% 的个人工具来说这选择没有意义，反而让人犹豫。
; 真要装成全局的可以命令行加 /ALLUSERS。
PrivilegesRequired=lowest

; 架构：本项目只发布 win-x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

OutputDir=..\..\dist\installer
OutputBaseFilename=WorkStation-Setup-{#AppVersion}

; 图标与美术
SetupIconFile=..\..\src\WorkStation.App\Assets\app.ico
; 自定义向导横幅：默认那张 "Install" 图标很旧，跟应用的蓝色调不搭。
; 由 tools\installer\make-wizard-images.py 生成（应用图标 + 主色渐变）。
WizardImageFile=wizard\wizard.bmp
WizardSmallImageFile=wizard\wizard-small.bmp
WizardStyle=modern
WizardSizePercent=120

; 注意：Inno Setup 6 没有 WizardColor 这个指令，向导配色是固定的系统风格，
; 想换色得改 SetupCustomStyle.e32（要重新编译 C++，不值得）。

; 压缩：LZMA2 慢速档，体积优先
Compression=lzma2/max
SolidCompression=yes

; 安装日志，出问题时能查
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

; 「设置 → 应用」里显示的信息
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplaySize=50000000

[Languages]
Name: "chinesesimp"; MessagesFile: "lang\ChineseSimplified.isl"

[CustomMessages]
chinesesimp.WelcomeLabel1=欢迎安装 {#AppName}
chinesesimp.WelcomeLabel2=即将在您的计算机上安装 {#AppName} {#AppVersion}。%n%n应用已内置运行环境，无需另外安装任何组件。%n%n建议您在继续安装前关闭所有其他应用程序。
chinesesimp.SelectDirLabel3=程序将安装到以下文件夹。
chinesesimp.FinishedHeadingLabel=安装向导完成
chinesesimp.FinishedLabel=安装已完成。
chinesesimp.FinishedNoIconsLabel=您可以选择立即启动 {#AppName}，或者稍后从开始菜单打开。
chinesesimp.ClickFinish=要现在启动 {#AppName} 吗？
chinesesimp.WizardReady=正在准备安装 {#AppName}…
chinesesimp.Installing=正在安装 {#AppName}…
chinesesimp.Extracting=正在解压文件…
chinesesimp.RestartProgram=现在启动 {#AppName}
chinesesimp.LaunchingApp=正在启动 {#AppName}…
chinesesimp.FinishedRestartLabel=如需稍后启动 {#AppName}，请从开始菜单打开。
chinesesimp.UninstallDeleteData=是否同时删除所有本地数据？%n%n数据文件（任务、项目、设置）保存在：%n%1%n%n选择「否」将保留数据，重新安装后可以继续使用。
chinesesimp.YesDeleteData=是，删除数据
chinesesimp.NoDeleteData=否，保留数据

[Files]
; 主程序（自包含单文件发布 —— 自带 .NET 运行时，目标机零依赖）
Source: "..\..\dist\win-x64\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; 开始菜单
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
; 桌面
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 卸载后清掉安装目录残留（不含用户数据，用户数据在 %APPDATA%\work-station）
Type: filesandordirs; Name: "{app}"

[Code]
// 安装过程中的自定义逻辑。

// 用户数据目录。AppPaths.cs 里也是这个路径，两边必须一致 ——
// 卸载时按这个路径删，删错了就等于没删。
function GetDataDir: String;
begin
  Result := ExpandConstant('{userappdata}\work-station');
end;

function InitializeSetup(): Boolean;
begin
  Result := True;

  // 已经是 64 位 Windows 吗？本项目只发布了 win-x64。
  if not IsWin64 then
  begin
    MsgBox('本程序仅支持 64 位 Windows。' + #13#10 +
           '当前系统是 32 位，无法安装。', mbCriticalError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // 安装目录里确实躺着一个可执行文件才算装成功
    if not FileExists(ExpandConstant('{app}\{#AppExeName}')) then
      MsgBox('安装似乎未完成：找不到主程序。', mbError, MB_OK);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
end;

// 卸载时问一句：数据要不要一起删。
// 默认保留 —— 误卸载不该丢工作台里的任务和项目。
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataDir := GetDataDir;
    if DirExists(DataDir) then
    begin
      // 自定义消息里有 %1（数据目录占位），要用 FmtMessage 传参，
      // ExpandConstant 只会原样留下 "%1"。
      if MsgBox(
        FmtMessage('{cm:UninstallDeleteData}', [DataDir]),
        mbConfirmation, MB_YESNO) = IDYES then
      begin
        DelTree(DataDir, True, True, True);
      end;
    end;
  end;
end;
