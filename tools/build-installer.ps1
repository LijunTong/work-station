<#
.SYNOPSIS
    构建 WorkStation 的 Windows 安装包（Inno Setup）。

.DESCRIPTION
    两步：
      1. dotnet publish 出**自包含**单文件（自带 .NET 运行时）
      2. ISCC 编译成标准 Windows 安装程序

    为什么用自包含：应用自带运行时，安装过程不需要检测运行时、
    不需要联网下载任何东西 —— 装完就能直接用。上一版「脚本 + 手动
    下载运行时」的方案要多点几个界面，还得担心下载失败。

    产物：dist\installer\WorkStation-Setup-<版本>.exe

    依赖：Inno Setup 6（ISCC.exe）。没装的话脚本会提示下载地址。

.PARAMETER Configuration
    Debug / Release，默认 Release。

.PARAMETER SkipPublish
    跳过 publish，直接用已有的 dist\win-x64 产物编译安装包。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\build-installer.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\WorkStation.App\WorkStation.App.csproj'
$portableOut = Join-Path $root 'dist\win-x64'
$iss = Join-Path $PSScriptRoot 'installer\WorkStation.iss'

if (-not (Test-Path -LiteralPath $project)) {
    throw "not found: $project"
}
if (-not (Test-Path -LiteralPath $iss)) {
    throw "not found: $iss"
}

# ---------------------------------------------------------------- Inno Setup
function Find-Iscc {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c -PathType Leaf) { return $c }
    }
    # 兜底：在 PATH 里找
    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw @"
未找到 Inno Setup 编译器（ISCC.exe）。

安装包需要它来编译。请到 https://jrsoftware.org/isdl.php 下载安装
「Inno Setup 6」，然后重新运行本脚本。
"@
}
Write-Host "Inno Setup: $iscc"

# --------------------------------------------------------- 语言包编码兜底
# Inno Setup 按 BOM 判断 .isl 的编码，没 BOM 就按 ANSI 读，中文会整段乱码。
# 语言包是第三方下载的原始文件（可能没 BOM），这里统一补上，免得哪天
# 重新下载一次就静默变乱码。
$islPath = Join-Path $PSScriptRoot 'installer\lang\ChineseSimplified.isl'
if (Test-Path -LiteralPath $islPath) {
    $islBytes = [System.IO.File]::ReadAllBytes($islPath)
    $hasBom = $islBytes.Length -ge 3 -and $islBytes[0] -eq 0xEF -and $islBytes[1] -eq 0xBB -and $islBytes[2] -eq 0xBF
    if (-not $hasBom) {
        $text = [System.IO.File]::ReadAllText($islPath, [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::WriteAllText($islPath, $text, [System.Text.UTF8Encoding]::new($true))
        Write-Host "语言包：已补 UTF-8 BOM"
    }
}

# ------------------------------------------------------------------ publish
if (-not $SkipPublish) {
    Write-Host "=== publish 自包含单文件 -> $portableOut"
    & (Join-Path $PSScriptRoot 'publish.ps1') -Rid win-x64 -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) {
        throw "publish 失败（exit $LASTEXITCODE）"
    }
}

$exe = Join-Path $portableOut 'WorkStation.exe'
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "发布产物里没有 WorkStation.exe：$portableOut"
}
$sizeMb = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
Write-Host ("  WorkStation.exe  {0} MB（自包含，含运行时）" -f $sizeMb)

# ------------------------------------------------------------------- compile
# ISCC 里的相对路径（Source、MessagesFile、OutputDir）是以**脚本所在目录**
# 为基准的，但实际解析用的是进程当前工作目录 —— 从别处调用就会找不到文件。
# 所以先切到 .iss 所在目录再编译。
#
# 关键：**不能走管道**。`| Out-File` / `2>&1 |` 会让 ISCC 的 stdout 变成管道句柄，
# 此时它解析不到自己的工作目录，直接以 exit 2 失败（"系统找不到指定的文件"）。
# 实测：直接 & 调用成功，一旦接管道就失败；**文件重定向**（> / 2>）也成功。
#
# 也不能用 Start-Process：这台机器的环境变量里 HTTP_PROXY 与 http_proxy 并存，
# .NET 的环境字典大小写不敏感，Start-Process 构造进程时会直接抛
# "已添加项。字典中的关键字:HTTP_PROXY"。
Write-Host "=== 编译安装包"
$issDir = Split-Path -Parent $iss
$issLog = Join-Path $env:TEMP 'ws-iscc.log'
$issErrLog = Join-Path $env:TEMP 'ws-iscc.err.log'

$prevEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    Push-Location -LiteralPath $issDir
    & $iscc $iss > $issLog 2> $issErrLog
    $isccExit = $LASTEXITCODE
    Pop-Location
}
finally {
    $ErrorActionPreference = $prevEap
    if ((Get-Location).Path -eq $issDir) { Pop-Location }
}

# 无论成败都把 ISCC 的输出回显出来
$logText = ''
foreach ($f in @($issLog, $issErrLog)) {
    if (Test-Path -LiteralPath $f) {
        $logText += (Get-Content -LiteralPath $f -Raw -Encoding Default)
        Remove-Item -LiteralPath $f -Force -ErrorAction SilentlyContinue
    }
}

if ($isccExit -ne 0) {
    Write-Host $logText
    throw "ISCC 编译失败（exit $isccExit）"
}

# 编译成功，但把警告也打出来（ISCC 的 Hint/Warning 走 stderr）
$warnLines = ($logText -split "`r?`n") | Where-Object { $_ -match '^\s*(Warning|Error|Hint)' }
if ($warnLines) {
    Write-Host "--- ISCC 提示 ---"
    $warnLines | ForEach-Object { Write-Host "  $_" }
}

$built = Get-ChildItem -Path (Join-Path $root 'dist\installer\WorkStation-Setup-*.exe') -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($built) {
    Write-Host ("=== 完成: {0}  ({1:N1} MB)" -f $built.FullName, ($built.Length / 1MB))
} else {
    Write-Host "=== 完成（未找到产物，检查 dist\installer）"
}
