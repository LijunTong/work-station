# 发布脚本：按 RID 产出桌面三平台的可分发产物。
#
#   用法：
#     tools\publish.ps1                      # 一次出 win-x64 / osx-arm64 / linux-x64
#     tools\publish.ps1 -Rid win-x64         # 只出一个
#
# 产物形态：框架依赖 + 单文件。
#   · 目标机只需安装 .NET 10 运行时（Avalonia 是普通 NuGet 包，不需要 WindowsDesktop 桌面运行时）
#   · IncludeNativeLibrariesForSelfExtract 让 Skia / HarfBuzz / SQLite 的原生库也进同一个 exe
#   · DebugType=None 不把 pdb 打进发布目录
#
# 注意：macOS 的 .app bundle 组装与 Gatekeeper 说明在 docs/README 里，本脚本只负责出可执行文件。
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

foreach ($rid in $Rid) {
  $out = Join-Path (Join-Path $root $OutDir) $rid
  Write-Host "=== publish $rid -> $out"
  & dotnet publish $project `
    -c $Configuration `
    -r $rid `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -o $out
  if ($LASTEXITCODE -ne 0) {
    throw "publish failed for $rid (exit $LASTEXITCODE)"
  }
  # SkiaSharp / HarfBuzzSharp 的原生包会附带 .pdb，那是调试符号，不进发布包
  Get-ChildItem -LiteralPath $out -Filter *.pdb -File | Remove-Item -Force
  $files = Get-ChildItem -LiteralPath $out -File
  $total = ($files | Measure-Object -Property Length -Sum).Sum
  Write-Host ("  {0} files, {1:N1} MB" -f $files.Count, ($total / 1MB))
  foreach ($f in ($files | Sort-Object Name)) {
    Write-Host ("  {0,12:N0}  {1}" -f $f.Length, $f.Name)
  }
}

Write-Host "done. requires: .NET 10 runtime on the target machine"
