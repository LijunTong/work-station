<#
.SYNOPSIS
    构建 Linux 安装包（.deb + 绿色版 tar.gz）。

.DESCRIPTION
    三步：
      1. dotnet publish 出 linux-x64 **目录式**自包含产物
      2. make-linux-packages.py 打成 .deb 与 .tar.gz
      3. verify-linux-packages.py 按 Debian 格式规范逐项核对

    为什么是目录式而不是单文件：单文件 + EnableCompressionInSingleFile
    每次启动都要把原生库解压到 /tmp，桌面应用的启动延迟不能接受；
    Debian 惯例本来就是 /opt/app 下放文件 + /usr/bin 放符号链接。

    AppImage 需要 mksquashfs（只有 Linux 有），所以那一步留给
    tools\linux\build-appimage.sh 在 Linux 上跑。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools\linux\build-linux-packages.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)

if (-not $SkipPublish) {
    Write-Host "=== publish linux-x64"
    & (Join-Path $root 'tools\publish.ps1') -Rid linux-x64
    if ($LASTEXITCODE -ne 0) {
        throw "publish 失败（exit $LASTEXITCODE）"
    }
}

Write-Host "=== 打包"
& python (Join-Path $PSScriptRoot 'make-linux-packages.py')
if ($LASTEXITCODE -ne 0) {
    throw "打包失败（exit $LASTEXITCODE）"
}

Write-Host "=== 校验"
& python (Join-Path $PSScriptRoot 'verify-linux-packages.py')
if ($LASTEXITCODE -ne 0) {
    throw "校验失败（exit $LASTEXITCODE）"
}

Write-Host ""
Write-Host "=== 产物"
Get-ChildItem (Join-Path $root 'dist\linux') -File |
    ForEach-Object { Write-Host ("  {0,12:N0}  {1}" -f $_.Length, $_.Name) }
