"""
把 Linux 产物打成标准安装包。

产出两种：

  1. **.deb**（Debian/Ubuntu/深度uos/UOS 等）
     `dpkg -i workstation_1.0.1_amd64.deb` 就能装；图形化环境下双击
     也会被「软件」中心识别。装完：
       · /opt/workstation/          应用本体
       · /usr/bin/workstation       指向它的符号链接（命令行直接敲 workstation）
       · /usr/share/applications/   桌面菜单项
       · /usr/share/icons/hicolor/  各尺寸图标
     卸载：`apt remove workstation`（或 dpkg -r），数据留在
     ~/.local/share/work-station 不动。

  2. **.tar.gz 绿色版**（Arch/Fedora/openSUSE 等没有 deb 的发行版）
     解压后 `bash install.sh` 建软链和桌面图标，或者直接跑里面的
     WorkStation 也能用。

为什么不用现成工具：这台机器上没有 WSL、没有 Docker daemon，也没有
dpkg-deb / ar / mksquashfs。.deb 本身就是「ar 归档 + 两个 tar.gz」，
ar 头格式固定、tar/gzip Python 原生支持，所以在 Windows 上手写完全可行，
产出的包与 dpkg-deb 生成的没有区别。

用法：
    python tools/linux/make-linux-packages.py
"""

from __future__ import annotations

import gzip
import hashlib
import io
import os
import pathlib
import shutil
import struct
import sys
import tarfile
import time
from datetime import datetime, timezone

try:
    from PIL import Image
except ImportError:
    Image = None

HERE = pathlib.Path(__file__).parent
ROOT = HERE.parent.parent

APP_NAME = "workstation"
APP_TITLE = "WorkStation 工作台"
APP_VERSION = "1.0.1"
ARCH = "amd64"
MAINTAINER = "Joont"
MAINTAINER_EMAIL = "joont@local"

SRC = ROOT / "dist" / "linux-x64"
ICON_PNG = ROOT / "src" / "WorkStation.App" / "Assets" / "app.png"
OUT_DIR = ROOT / "dist" / "linux"

# 安装前缀。Debian 惯例：程序本体放 /opt，命令行入口放 /usr/bin。
OPT_DIR = "/opt/workstation"

# 依赖清单。
#
# 这里列的是**系统级共享库**，从发布产物的 ELF DT_NEEDED 里实际解析出来的，
# 不是拍脑袋写的：
#   · libfontconfig1  ← SkiaSharp 靠它找字体，缺了直接起不来
#   · liblttng-ust1   ← .NET 的 EventPipe tracing 硬链接了它
#   · libstdc++6 / libgcc-s1 / libc6 ← C++ 运行时与 libc
#
# X11 / GL 那几个（Avalonia 的窗口后端）是**运行时 dlopen** 的，ELF 里看不到，
# 但没有它们开不了窗口，所以照样列上。
#
# libicu 用「或」列出多个版本：.NET 8+ 在 Linux 上是 dlopen 系统 ICU 的，
# 而 ICU 的 soname 带版本号，各发行版不同（Ubuntu 22.04 是 libicu70，
# 24.04 是 libicu74，Debian 12 是 libicu72）。写死一个版本会让别的
# 发行版装不上，用 | 列出候选，dpkg 只要有一个满足就装。
DEPENDS = (
    "libc6, "
    "libgcc-s1, "
    "libstdc++6, "
    "libfontconfig1, "
    "liblttng-ust1, "
    "libicu74 | libicu73 | libicu72 | libicu71 | libicu70 | libicu67 | libicu66, "
    "libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libxinerama1, "
    "libsm6, libice6, libgl1, libegl1, "
    "zlib1g"
)

DESCRIPTION = (
    "个人工作台：项目、任务、看板与提醒。\n"
    "  · 项目 / 任务 / 看板三种视图，任务有截止时间和提醒排期\n"
    "  · 提醒到点弹通知，托盘常驻\n"
    "  · 支持明暗主题、自定义主题色与字体\n"
    "  · 数据全部存在本地，不联网"
)

DESKTOP_ENTRY = f"""[Desktop Entry]
Type=Application
Version=1.0
Name={APP_TITLE}
Name[zh_CN]={APP_TITLE}
Comment=个人工作台：项目、任务、看板与提醒
Comment[zh_CN]=个人工作台：项目、任务、看板与提醒
Exec=workstation %U
Icon=workstation
Terminal=false
Categories=Office;ProjectManagement;
Keywords=task;project;board;todo;reminder;
StartupNotify=true
StartupWMClass=WorkStation
"""

POSTINST = """#!/bin/sh
set -e

# 桌面菜单缓存刷新。没有 update-desktop-database 的系统会跳过（不致命）。
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi

# 图标缓存
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -f -t /usr/share/icons/hicolor || true
fi

exit 0
"""

PRERM = """#!/bin/sh
set -e

if [ "$1" = "remove" ] || [ "$1" = "upgrade" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -f -t /usr/share/icons/hicolor || true
    fi
fi

exit 0
"""

POSTRM = """#!/bin/sh
set -e

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
if command -v gtk-update-icon-cache >/dev/null 2>&1; then
    gtk-update-icon-cache -q -f -t /usr/share/icons/hicolor || true
fi

exit 0
"""

ICON_SIZES = [16, 24, 32, 48, 64, 128, 256]


# --------------------------------------------------------------------- ar 归档
def write_ar(entries: list[tuple[str, bytes]], path: pathlib.Path) -> None:
    """写 GNU 风格的 ar 归档。

    每个成员是 60 字节的头 + 内容，头里的内容长度是十进制 ASCII，
    成员之间用换行（\\n）对齐到偶数字节。

    Debian 的 .deb 就是 ar 归档，dpkg 用的是同一套格式。
    """
    buf = io.BytesIO()
    # 魔数 "!<arch>\n" **只在整个归档开头出现一次**。
    # 每个成员前面只有 60 字节的头 —— 早先每个成员都写一遍魔数，
    # 第一个成员位置刚好对、第二个开始全部错位，dpkg 直接读不了这个包。
    buf.write(b"!<arch>\n")

    for name, payload in entries:
        name_bytes = name.encode("utf-8")
        if len(name_bytes) > 16:
            raise ValueError(f"ar 成员名过长: {name}")

        # 60 字节头：
        #   0-15  成员名（补空格）
        #  16-27  mtime（十进制，12 字节）
        #  28-33  owner id（6 字节）
        #  34-39  group id（6 字节）
        #  40-47  mode（八进制，8 字节）
        #  48-57  长度（十进制，10 字节）
        #  58-59  "`\n"
        header = (
            name_bytes.ljust(16, b" ")
            + str(int(time.time())).encode("ascii").ljust(12, b" ")
            + b"0".ljust(6, b" ")
            + b"0".ljust(6, b" ")
            + b"100644".ljust(8, b" ")
            + str(len(payload)).encode("ascii").ljust(10, b" ")
            + b"`\n"
        )
        assert len(header) == 60, len(header)
        buf.write(header)
        buf.write(payload)
        # ar 成员按偶数字节对齐
        if len(payload) % 2 == 1:
            buf.write(b"\n")

    path.write_bytes(buf.getvalue())


def make_tar_gz(files: list, path: pathlib.Path) -> None:
    """打 tar.gz。

    files 里每一项是 (归档内路径, 源, 权限)，其中「源」可以是三种：
      · pathlib.Path —— 普通文件，按其内容打包
      · bytes        —— 内容直接给 bytes
      · str          —— **符号链接目标**，写成 tar 的符号链接条目

    符号链接必须走最后一种：Windows 上建不出 Linux 符号链接，
    但 tar 格式本来就支持（type '2'），dpkg 解包时会在目标系统上创建。
    """
    with tarfile.open(path, "w:gz", compresslevel=9) as tar:
        for name, src, mode in files:
            info = tarfile.TarInfo(name)
            info.mode = mode
            info.mtime = int(time.time())
            info.uid = 0
            info.gid = 0
            info.uname = "root"
            info.gname = "root"

            if isinstance(src, str):
                # 符号链接
                info.type = tarfile.SYMTYPE
                info.linkname = src
                info.size = 0
                tar.addfile(info)
            elif isinstance(src, bytes):
                info.size = len(src)
                info.type = tarfile.REGTYPE
                tar.addfile(info, io.BytesIO(src))
            else:
                info.size = src.stat().st_size
                info.type = tarfile.REGTYPE
                with src.open("rb") as fh:
                    tar.addfile(info, fh)


def md5sums(files: list[tuple[str, pathlib.Path]]) -> str:
    """生成 control/md5sums 的内容：每行 "<md5>  <路径>"。"""
    lines = []
    for name, src in files:
        h = hashlib.md5()
        with src.open("rb") as fh:
            for chunk in iter(lambda: fh.read(1 << 20), b""):
                h.update(chunk)
        lines.append(f"{h.hexdigest()}  {name}")
    return "\n".join(lines) + "\n"


# ------------------------------------------------------------------- 图标处理
def render_icons(target_dir: pathlib.Path) -> list[tuple[str, pathlib.Path]]:
    """把应用图标渲染成 hicolor 各尺寸，返回 (归档内路径, 临时文件)。"""
    if Image is None:
        raise RuntimeError("需要 Pillow 才能渲染图标（pip install pillow）")

    target_dir.mkdir(parents=True, exist_ok=True)
    src = Image.open(ICON_PNG).convert("RGBA")

    out = []
    for size in ICON_SIZES:
        png = target_dir / f"workstation-{size}.png"
        src.resize((size, size), Image.LANCZOS).save(png, format="PNG")
        out.append((f"usr/share/icons/hicolor/{size}x{size}/apps/workstation.png", png))
    return out


# --------------------------------------------------------------------- .deb
def build_deb(app_dir: pathlib.Path, deb_path: pathlib.Path, tmp: pathlib.Path) -> None:
    """组装 .deb。

    结构：
        debian-binary          内容固定是 "2.0\\n"
        control.tar.gz         control / md5sums / postinst / prerm / postrm
        data.tar.gz            文件树
    """
    data_dir = tmp / "data"
    control_dir = tmp / "control"
    for d in (data_dir, control_dir):
        if d.exists():
            shutil.rmtree(d)
        d.mkdir(parents=True)

    # ---- data.tar.gz 的内容清单
    data_files: list[tuple[str, pathlib.Path | bytes | str, int]] = []
    md5_targets: list[tuple[str, pathlib.Path]] = []

    # 1) 应用本体 -> /opt/workstation/
    for src in sorted(app_dir.iterdir()):
        if src.is_dir():
            continue
        mode = 0o755 if src.suffix == "" or src.suffix == ".so" else 0o644
        # 无扩展名的那个是 apphost，必须可执行
        if src.name == "WorkStation":
            mode = 0o755
        elif src.suffix == ".so":
            mode = 0o755
        data_files.append((f"opt/workstation/{src.name}", src, mode))
        md5_targets.append((f"opt/workstation/{src.name}", src))

    # 2) 命令行入口（符号链接）
    #
    # 不在 Windows 上真的建链接再打进去 —— 建符号链接要特权（而且 Windows 的
    # 符号链接语义跟 Linux 不一样，路径还可能变成盘符形式）。
    # tar 格式本身就支持符号链接条目（type '2'），直接写进去最干净，
    # dpkg 解包时会在目标系统上创建真正的 Linux 符号链接。
    data_files.append(("usr/bin/workstation", f"{OPT_DIR}/WorkStation", 0o777))

    # 3) 桌面菜单项
    desktop_path = data_dir / "usr" / "share" / "applications" / "workstation.desktop"
    desktop_path.parent.mkdir(parents=True, exist_ok=True)
    desktop_path.write_text(DESKTOP_ENTRY, encoding="utf-8")
    data_files.append(("usr/share/applications/workstation.desktop", desktop_path, 0o644))

    # 4) 图标
    icons = render_icons(tmp / "icons")
    for name, png in icons:
        dest = data_dir / name
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(png, dest)
        data_files.append((name, dest, 0o644))

    # 5) 文档
    doc_dir = data_dir / "usr" / "share" / "doc" / "workstation"
    doc_dir.mkdir(parents=True, exist_ok=True)
    copyright_txt = f"{APP_TITLE}\nCopyright (C) 2026 {MAINTAINER}\n\n本软件按 MIT 许可发布。\n"
    (doc_dir / "copyright").write_text(copyright_txt, encoding="utf-8")
    changelog = f"{APP_TITLE} ({APP_VERSION}) unstable; urgency=medium\n\n  * 首个 Linux 安装包\n\n -- {MAINTAINER} <{MAINTAINER_EMAIL}>  {datetime.now(timezone.utc).strftime('%a, %d %b %Y %H:%M:%S +0000')}\n"
    (doc_dir / "changelog.Debian.gz").write_bytes(
        gzip.compress(changelog.encode("utf-8"), mtime=0))
    data_files.append(("usr/share/doc/workstation/copyright", doc_dir / "copyright", 0o644))
    data_files.append(("usr/share/doc/workstation/changelog.Debian.gz", doc_dir / "changelog.Debian.gz", 0o644))

    # ---- control.tar.gz
    installed_size = sum(
        (len(src) if isinstance(src, (bytes, str)) else src.stat().st_size)
        for _, src, _ in data_files
    )
    control_text = (
        f"Package: {APP_NAME}\n"
        f"Version: {APP_VERSION}\n"
        f"Section: utils\n"
        f"Priority: optional\n"
        f"Architecture: {ARCH}\n"
        f"Maintainer: {MAINTAINER} <{MAINTAINER_EMAIL}>\n"
        f"Installed-Size: {installed_size // 1024}\n"
        f"Depends: {DEPENDS}\n"
        f"Homepage: https://github.com/joont/workstation\n"
        f"Description: 个人工作台：项目、任务、看板与提醒\n"
        f" {DESCRIPTION}\n"
    )
    (control_dir / "control").write_text(control_text, encoding="utf-8")
    (control_dir / "md5sums").write_text(md5sums(md5_targets), encoding="utf-8")
    for name, body in (("postinst", POSTINST), ("prerm", PRERM), ("postrm", POSTRM)):
        p = control_dir / name
        p.write_text(body, encoding="utf-8")
        os.chmod(p, 0o755)

    control_files = [
        ("control", control_dir / "control", 0o644),
        ("md5sums", control_dir / "md5sums", 0o644),
        ("postinst", control_dir / "postinst", 0o755),
        ("prerm", control_dir / "prerm", 0o755),
        ("postrm", control_dir / "postrm", 0o755),
    ]

    # ---- 打包
    data_tar = tmp / "data.tar.gz"
    control_tar = tmp / "control.tar.gz"
    make_tar_gz(data_files, data_tar)
    make_tar_gz(control_files, control_tar)

    write_ar([
        ("debian-binary", b"2.0\n"),
        ("control.tar.gz", control_tar.read_bytes()),
        ("data.tar.gz", data_tar.read_bytes()),
    ], deb_path)

    print(f"  {deb_path.name}  {deb_path.stat().st_size / 1024 / 1024:.1f} MB")


# ----------------------------------------------------------------- tar.gz 绿色版
def build_tarball(app_dir: pathlib.Path, tar_path: pathlib.Path, tmp: pathlib.Path) -> None:
    """打包绿色版 tar.gz。

    解压后目录结构：
        workstation-1.0.1-linux-x86_64/
            WorkStation            应用本体
            install.sh             建软链 + 桌面图标
            uninstall.sh           撤掉上面那些
            workstation.desktop    桌面菜单项
            icons/                 各尺寸图标
    """
    stage = tmp / f"{APP_NAME}-{APP_VERSION}-linux-x86_64"
    if stage.exists():
        shutil.rmtree(stage)
    stage.mkdir(parents=True)

    for src in sorted(app_dir.iterdir()):
        if src.is_dir():
            continue
        shutil.copy2(src, stage / src.name)

    icon_dir = stage / "icons"
    icon_dir.mkdir()
    for name, png in render_icons(tmp / "icons"):
        # 名字里要带尺寸：install.sh 里按 "workstation-<尺寸>.png" 取大小，
        # 全部叫 workstation.png 会互相覆盖（只留下最后一个）。
        # 归档内路径形如 usr/share/icons/hicolor/16x16/apps/workstation.png，
        # 尺寸在倒数第三段（[-3]）。
        size = name.split("/")[-3].split("x")[0]
        shutil.copy2(png, icon_dir / f"workstation-{size}.png")

    (stage / "workstation.desktop").write_text(DESKTOP_ENTRY, encoding="utf-8")

    install_sh = """#!/bin/sh
# 把工作台装到当前用户目录下。不需要 root，不动系统目录。
set -e

HERE="$(cd "$(dirname "$0")" && pwd)"
PREFIX="${HOME}/.local"

echo "安装到 $PREFIX/opt/workstation ..."
mkdir -p "$PREFIX/opt" "$PREFIX/bin" "$PREFIX/share/applications" "$PREFIX/share/icons/hicolor"

rm -rf "$PREFIX/opt/workstation"
mkdir -p "$PREFIX/opt/workstation"
cp -r "$HERE"/. "$PREFIX/opt/workstation/"
rm -f "$PREFIX/opt/workstation/install.sh" "$PREFIX/opt/workstation/uninstall.sh"
rm -rf "$PREFIX/opt/workstation/icons" "$PREFIX/opt/workstation/workstation.desktop"
chmod +x "$PREFIX/opt/workstation/WorkStation"

ln -sf "$PREFIX/opt/workstation/WorkStation" "$PREFIX/bin/workstation"

cp "$HERE/workstation.desktop" "$PREFIX/share/applications/workstation.desktop"

for f in "$HERE"/icons/*.png; do
    base="$(basename "$f")"           # workstation-256.png
    size="${base#workstation-}"; size="${size%.png}"
    d="$PREFIX/share/icons/hicolor/${size}x${size}/apps"
    mkdir -p "$d"
    cp "$f" "$d/workstation.png"
done

if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q "$PREFIX/share/applications" || true
fi

echo
echo "装好了。启动方式："
echo "  · 命令行：$PREFIX/bin/workstation"
echo "  · 图形界面：开始菜单里搜「工作台」"
echo
echo "把 $PREFIX/bin 加进 PATH 就能直接敲 workstation 运行。"
echo "卸载：bash $HERE/uninstall.sh"
"""
    uninstall_sh = """#!/bin/sh
set -e
PREFIX="${HOME}/.local"
rm -rf "$PREFIX/opt/workstation"
rm -f "$PREFIX/bin/workstation"
rm -f "$PREFIX/share/applications/workstation.desktop"
rm -rf "$PREFIX/share/icons/hicolor"/*/apps/workstation.png
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q "$PREFIX/share/applications" || true
fi
echo "已卸载（数据仍在 ~/.local/share/work-station）"
"""
    (stage / "install.sh").write_text(install_sh, encoding="utf-8")
    (stage / "uninstall.sh").write_text(uninstall_sh, encoding="utf-8")
    os.chmod(stage / "install.sh", 0o755)
    os.chmod(stage / "uninstall.sh", 0o755)

    # AppImage 构建脚本也带上：AppImage 需要 mksquashfs（只有 Linux 有），
    # 所以这一步只能在 Linux 上跑，把脚本塞进来，用户解压就能生成。
    appimage_sh = HERE / "build-appimage.sh"
    if appimage_sh.exists():
        shutil.copy2(appimage_sh, stage / "build-appimage.sh")

    readme = f"""{APP_TITLE} {APP_VERSION}（Linux 绿色版）
{'-' * 40}

解压后：

    bash install.sh

会装到 ~/.local 下（不需要管理员权限，不动系统目录），
然后在开始菜单里就能看到「{APP_TITLE}」，也可以敲 workstation 启动。

不想装也行：直接运行 ./WorkStation 也能用（第一次启动会慢一点）。

依赖：libc6、libfontconfig1、libstdc++6、libicu、libx11-6 等常见桌面库。
中文显示需要系统装有中文字体（如 fonts-noto-cjk）。

数据：~/.local/share/work-station，不联网。
"""
    (stage / "README.txt").write_text(readme, encoding="utf-8")

    # 直接用 tarfile 打包（stage 目录本身作为顶层目录）。
    #
    # 权限要**手动写**：Windows 的文件系统不存 Unix 权限位，
    # os.chmod 改了也白改，tarfile.gettarinfo 拿到的 st_mode 只会是 0o666。
    # 所以这里自己构造 TarInfo，按规则给权限 —— 解包到 Linux 上才是对的。
    exec_names = {"WorkStation", "install.sh", "uninstall.sh", "build-appimage.sh"}
    with tarfile.open(tar_path, "w:gz", compresslevel=9) as tar:
        for path in sorted(stage.rglob("*")):
            arc = path.relative_to(tmp).as_posix()
            info = tarfile.TarInfo(arc)
            info.uid = info.gid = 0
            info.uname = info.gname = "root"
            info.mtime = int(time.time())

            if path.is_dir():
                info.type = tarfile.DIRTYPE
                info.mode = 0o755
                info.size = 0
                tar.addfile(info)
            elif path.is_file():
                info.type = tarfile.REGTYPE
                info.mode = 0o755 if path.name in exec_names else 0o644
                info.size = path.stat().st_size
                with path.open("rb") as fh:
                    tar.addfile(info, fh)

    print(f"  {tar_path.name}  {tar_path.stat().st_size / 1024 / 1024:.1f} MB")


def main() -> int:
    if not SRC.exists():
        print(f"找不到发布产物：{SRC}")
        print("先跑 tools\\publish.ps1 -Rid linux-x64")
        return 1
    if not (SRC / "WorkStation").exists():
        print(f"产物里没有 WorkStation 可执行文件：{SRC}")
        return 1

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    tmp = OUT_DIR / ".staging"
    tmp.mkdir(exist_ok=True)

    print("打包 Linux 安装包：")
    deb = OUT_DIR / f"{APP_NAME}_{APP_VERSION}_{ARCH}.deb"
    build_deb(SRC, deb, tmp)

    tarball = OUT_DIR / f"{APP_NAME}-{APP_VERSION}-linux-x86_64.tar.gz"
    build_tarball(SRC, tarball, tmp)

    shutil.rmtree(tmp, ignore_errors=True)
    print(f"输出目录：{OUT_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
