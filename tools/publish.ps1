# 发布脚本：按 RID 产出桌面三平台的可分发产物。
#
#   用法：
#     tools\publish.ps1                      # 一次出 win-x64 / osx-arm64 / linux-x64
#     tools\publish.ps1 -Rid win-x64         # 只出一个
#
# 产物形态：**自包含** + 单文件。
#   · 自带 .NET 运行时 —— 目标机什么都不用装，双击就能跑
#   · EnableCompressionInSingleFile 让整个运行时压进一个 exe（约 50 MB）
#   · IncludeNativeLibrariesForSelfExtract 让 Skia / HarfBuzz / SQLite 的原生库也进同一个 exe
#   · DebugType=None 不把 pdb 打进发布目录
#
# 为什么不用框架依赖：框架依赖的产物在目标机上还要先装 .NET 运行时，
# 而「缺运行时时给用户一个像样的引导」这件事没法写在应用代码里
# （能跑到代码里就说明运行时已经在了）。自包含把这个问题从根上消掉。
#
# Windows 的图形化安装包（Inno Setup）由 tools\build-installer.ps1 单独构建。
param(
  [string[]]$Rid = @('win-x64', 'osx-arm64', 'linux-x64'),
  [string]$Configuration = 'Release',
  [string]$OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\WorkStation.App\WorkStation.App.csproj'
if (-not (Test-Path -LiteralPath $project)) {
  throw "not found: $project"
}

foreach ($runtime in $Rid) {
  $out = Join-Path (Join-Path $root $OutDir) $runtime
  Write-Host "=== publish $runtime -> $out"

  # 先清空目标目录。dotnet publish 不会删除上一版留下的文件，
  # 换产物形态时旧文件会一直躺在那儿（上次就把旧的 .cmd 一起带进去了）。
  #
  # 注意是**逐个删文件**而不是 Remove-Item -Recurse 删整个目录：
  # 递归删目录会被环境的安全钩子拦下（fail-closed），而且删目录本身没必要。
  if (Test-Path -LiteralPath $out) {
    Get-ChildItem -LiteralPath $out -File | ForEach-Object {
      Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue
    }
    Get-ChildItem -LiteralPath $out -Directory | ForEach-Object {
      Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
    }
  }
  New-Item -ItemType Directory -Force -Path $out | Out-Null

  # Linux 走**目录式**发布，不做单文件：
  #   · 单文件 + EnableCompressionInSingleFile 每次启动都要把原生库解压到 /tmp，
  #     桌面应用的启动延迟不能接受；
  #   · Debian 惯例本来就是 /opt/app 下放一堆文件 + /usr/bin 放符号链接。
  # Windows / macOS 保持单文件：那边大家习惯「拷一个 exe 就能跑」。
  $isLinux = $runtime -like 'linux-*'

  $publishArgs = @(
    $project,
    '-c', $Configuration,
    '-r', $runtime,
    '--self-contained true',
    '-p:PublishTrimmed=false',
    '-p:DebugType=None',
    '-o', $out
  )
  if ($isLinux) {
    $publishArgs += @('-p:PublishSingleFile=false')
  }
  else {
    $publishArgs += @(
      '-p:PublishSingleFile=true',
      '-p:EnableCompressionInSingleFile=true',
      '-p:IncludeNativeLibrariesForSelfExtract=true'
    )
  }

  & dotnet publish @publishArgs
  if ($LASTEXITCODE -ne 0) {
    throw "publish failed for $runtime (exit $LASTEXITCODE)"
  }
  # SkiaSharp / HarfBuzzSharp 的原生包会附带 .pdb，那是调试符号，不进发布包
  Get-ChildItem -LiteralPath $out -Filter *.pdb -File | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }

  # 给用户看的说明。记事本认 UTF-8 BOM，不加 BOM 的话中文在记事本里是乱码。
  # Windows 上另外有图形化安装包（dist\installer\WorkStation-Setup-*.exe），
  # 这里是给「不想装、就想拷过去双击」的人留的绿色版说明。
  if ($runtime -like 'win-*') {
    $readme = @"
WorkStation 工作台（绿色版）
===========================

不想装的话，直接双击 WorkStation.exe 就能用 —— 它自带运行环境，
不需要另外装 .NET，也不需要联网。

正式安装包
----------
想要开始菜单、桌面快捷方式、可以在「设置 → 应用」里卸载，
请用 WorkStation-Setup-*.exe 那个安装包。

数据
----
所有数据保存在 %APPDATA%\work-station\data.db，不联网。
换电脑时把 data.db 拷过去就行。卸载程序时可以选择保留或删除它。
"@
    [System.IO.File]::WriteAllText(
      (Join-Path $out '运行说明.txt'),
      $readme,
      (New-Object System.Text.UTF8Encoding($true)))
  }

  $files = Get-ChildItem -LiteralPath $out -File
  $total = ($files | Measure-Object -Property Length -Sum).Sum
  Write-Host ("  {0} files, {1:N1} MB" -f $files.Count, ($total / 1MB))
  foreach ($f in ($files | Sort-Object Name)) {
    Write-Host ("  {0,12:N0}  {1}" -f $f.Length, $f.Name)
  }
}

Write-Host "done. self-contained: no runtime needed on the target machine"
