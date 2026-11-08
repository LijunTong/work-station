#!/bin/bash
# 在 Linux 上把工作台打成 AppImage（单文件、双击即用、跨发行版）。
#
# 为什么单独一个脚本：AppImage 需要 mksquashfs 把文件树压成 squashfs 镜像，
# 而这个工具只有 Linux 上有（Windows 上没有可用的二进制）。所以这一步
# 必须在 Linux 机器上跑。
#
# 用法：
#   ./build-appimage.sh                 # 需要本目录是解压后的绿色版
#   ./build-appimage.sh /path/to/dir    # 指定目录
#
# 依赖（Debian/Ubuntu）：
#   sudo apt install squashfs-tools
#
# 不装 AppImage 也能用：同目录下的 install.sh 装到用户目录，效果一样。

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"

if [ ! -x "$HERE/WorkStation" ]; then
    echo "找不到 $HERE/WorkStation —— 请先解压绿色版 tar.gz" >&2
    exit 1
fi

if ! command -v mksquashfs >/dev/null 2>&1; then
    cat >&2 <<'EOF'
缺少 mksquashfs。装一下：

    sudo apt install squashfs-tools        # Debian / Ubuntu
    sudo dnf install squashfs-tools        # Fedora
    sudo pacman -S squashfs-tools          # Arch

装完再跑一次本脚本。
EOF
    exit 1
fi

APP_NAME="WorkStation"
APP_VERSION="1.0.2"
BUILD_DIR="$(mktemp -d)"
APPDIR="$BUILD_DIR/${APP_NAME}.AppDir"
OUT="${APP_NAME}-${APP_VERSION}-x86_64.AppImage"

cleanup() { rm -rf "$BUILD_DIR"; }
trap cleanup EXIT

echo "==> 准备 AppDir"
mkdir -p "$APPDIR"

# 应用本体（排除打包辅助文件）
for f in "$HERE"/*; do
    base="$(basename "$f")"
    case "$base" in
        install.sh|uninstall.sh|workstation.desktop|README.txt|build-appimage.sh|icons) continue ;;
    esac
    cp -r "$f" "$APPDIR/"
done
chmod +x "$APPDIR/WorkStation"

# 图标
mkdir -p "$APPDIR/usr/share/icons/hicolor/256x256/apps"
cp "$HERE/icons/workstation-256.png" "$APPDIR/usr/share/icons/hicolor/256x256/apps/workstation.png"
cp "$HERE/icons/workstation-256.png" "$APPDIR/${APP_NAME}.png"

# 桌面文件
mkdir -p "$APPDIR/usr/share/applications"
cat > "$APPDIR/${APP_NAME}.desktop" <<'EOF'
[Desktop Entry]
Type=Application
Name=WorkStation 工作台
Name[zh_CN]=WorkStation 工作台
Comment=个人工作台：项目、任务、看板与提醒
Exec=WorkStation
Icon=WorkStation
Terminal=false
Categories=Office;ProjectManagement;
StartupWMClass=WorkStation
EOF
cp "$APPDIR/${APP_NAME}.desktop" "$APPDIR/usr/share/applications/workstation.desktop"

# AppImage 运行时（自解压器）
echo "==> 下载 AppImage 运行时"
RUNTIME="runtime-x86_64"
if [ ! -f "$HERE/$RUNTIME" ]; then
    curl -fsSL -o "$HERE/$RUNTIME" \
        "https://github.com/AppImage/AppImageKit/releases/download/continuous/runtime-x86_64" \
        || { echo "运行时下载失败，可手动下载后放到本目录：$RUNTIME" >&2; exit 1; }
fi
chmod +x "$HERE/$RUNTIME"

echo "==> 压缩成 squashfs"
mksquashfs "$APPDIR" "$BUILD_DIR/${APP_NAME}.squashfs" \
    -root-owned -noappend -quiet -comp gzip

echo "==> 组装 AppImage"
cat "$HERE/$RUNTIME" "$BUILD_DIR/${APP_NAME}.squashfs" > "$OUT"
chmod +x "$OUT"

echo
echo "完成：$OUT"
echo "  chmod +x $OUT && ./$OUT"
