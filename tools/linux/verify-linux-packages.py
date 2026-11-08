"""
校验 Linux 安装包的结构是否真的能被 dpkg / tar 正确处理。

这台机器上没有 dpkg，所以自己按 Debian 的格式规范逐项核对：

  · ar 归档头：成员名、时间戳、权限、长度是否对得上
  · debian-binary 内容是否为 "2.0\\n"
  · control.tar.gz：control 字段是否齐全、md5sums 是否与 data.tar.gz 里的文件一致
  · data.tar.gz：文件权限（apphost 要 755）、符号链接目标是否正确

用法：
    python tools/linux/verify-linux-packages.py
"""

from __future__ import annotations

import hashlib
import io
import pathlib
import sys
import tarfile

ROOT = pathlib.Path(__file__).parent.parent.parent
OUT_DIR = ROOT / "dist" / "linux"

errors: list[str] = []
checks = 0


def check(condition: bool, message: str) -> bool:
    global checks
    checks += 1
    if condition:
        print(f"  ✓ {message}")
    else:
        print(f"  ✗ {message}")
        errors.append(message)
    return condition


def read_ar(path: pathlib.Path) -> list[tuple[str, bytes]]:
    """按 ar 格式解析归档，返回 [(成员名, 内容)]。"""
    data = path.read_bytes()
    if not data.startswith(b"!<arch>\n"):
        raise ValueError("缺少 ar 魔数")

    members: list[tuple[str, bytes]] = []
    pos = 8
    while pos < len(data):
        header = data[pos:pos + 60]
        if len(header) < 60:
            raise ValueError(f"@{pos} 处的 ar 头不完整")
        name = header[0:16].decode("ascii").strip()
        mtime = header[16:28].decode("ascii").strip()
        uid = header[28:34].decode("ascii").strip()
        gid = header[34:40].decode("ascii").strip()
        mode = header[40:48].decode("ascii").strip()
        size = int(header[48:58].decode("ascii").strip())
        magic = header[58:60]

        if magic != b"`\n":
            raise ValueError(f"成员 {name} 的 ar 头结束符不对：{magic!r}")
        if not mtime.isdigit() or not uid.isdigit() or not gid.isdigit():
            raise ValueError(f"成员 {name} 的 mtime/uid/gid 不是数字")
        if not mode.isdigit() or len(mode) > 8:
            raise ValueError(f"成员 {name} 的 mode 不是八进制数字串：{mode}")

        body = data[pos + 60:pos + 60 + size]
        if len(body) != size:
            raise ValueError(f"成员 {name} 长度不符：头说 {size}，实际 {len(body)}")

        members.append((name, body))
        pos += 60 + size
        if size % 2 == 1:
            pos += 1  # 偶数字节对齐
    return members


def md5_of(tar: tarfile.TarFile, member: tarfile.TarInfo) -> str:
    fh = tar.extractfile(member)
    assert fh is not None
    h = hashlib.md5()
    for chunk in iter(lambda: fh.read(1 << 20), b""):
        h.update(chunk)
    return h.hexdigest()


def verify_deb(path: pathlib.Path) -> None:
    print(f"\n=== {path.name} ({path.stat().st_size / 1024 / 1024:.1f} MB)")

    members = read_ar(path)
    names = [n for n, _ in members]
    check(names == ["debian-binary", "control.tar.gz", "data.tar.gz"],
          f"ar 成员顺序正确：{names}")

    debian_binary = dict(members)["debian-binary"]
    check(debian_binary == b"2.0\n", f"debian-binary 内容 = {debian_binary!r}")

    # ---- control.tar.gz
    control_tar = tarfile.open(fileobj=io.BytesIO(dict(members)["control.tar.gz"]), mode="r:gz")
    control_names = control_tar.getnames()
    check("control" in control_names, f"control.tar.gz 含 control：{control_names}")

    control = control_tar.extractfile("control").read().decode("utf-8")
    required_fields = ["Package:", "Version:", "Architecture:", "Maintainer:",
                       "Depends:", "Description:"]
    for field in required_fields:
        check(any(line.startswith(field) for line in control.splitlines()),
              f"control 有 {field}")

    pkg = next(line for line in control.splitlines() if line.startswith("Package:"))
    check(pkg == "Package: workstation", f"{pkg}")
    ver = next(line for line in control.splitlines() if line.startswith("Version:"))
    check(ver == "Version: 1.0.2", f"{ver}")
    arch = next(line for line in control.splitlines() if line.startswith("Architecture:"))
    check(arch == "Architecture: amd64", f"{arch}")

    # 维护脚本
    for script in ("postinst", "prerm", "postrm"):
        if script in control_names:
            m = control_tar.getmember(script)
            check(m.mode & 0o111 == 0o111, f"{script} 可执行（mode={oct(m.mode)}）")

    # ---- data.tar.gz
    data_tar = tarfile.open(fileobj=io.BytesIO(dict(members)["data.tar.gz"]), mode="r:gz")
    members_map = {m.name: m for m in data_tar.getmembers()}

    check("opt/workstation/WorkStation" in members_map, "应用本体在 /opt/workstation/WorkStation")
    check("usr/bin/workstation" in members_map, "命令行入口 /usr/bin/workstation")
    check("usr/share/applications/workstation.desktop" in members_map, "桌面菜单项")
    check("usr/share/icons/hicolor/256x256/apps/workstation.png" in members_map, "256x256 图标")
    check("usr/share/icons/hicolor/16x16/apps/workstation.png" in members_map, "16x16 图标")

    # 符号链接
    link = members_map.get("usr/bin/workstation")
    if link is not None:
        check(link.issym(), f"/usr/bin/workstation 是符号链接（type={link.type}）")
        check(link.linkname == "/opt/workstation/WorkStation",
              f"符号链接指向 {link.linkname}")

    # 权限
    apphost = members_map.get("opt/workstation/WorkStation")
    if apphost is not None:
        check(apphost.mode & 0o111 == 0o111, f"apphost 可执行（mode={oct(apphost.mode)}）")
    so = members_map.get("opt/workstation/libcoreclr.so")
    if so is not None:
        check(so.mode & 0o111 == 0o111, f"原生库可执行（mode={oct(so.mode)}）")

    # ---- md5sums 与实际文件对得上
    if "md5sums" in control_names:
        sums = control_tar.extractfile("md5sums").read().decode("utf-8")
        lines = [l for l in sums.splitlines() if l.strip()]
        check(len(lines) > 0, f"md5sums 有 {len(lines)} 条")

        mismatched = []
        for line in lines:
            digest, _, name = line.partition("  ")
            m = members_map.get(name)
            if m is None:
                mismatched.append(f"{name}: 不在 data.tar.gz 里")
                continue
            if md5_of(data_tar, m) != digest:
                mismatched.append(f"{name}: 校验和不符")
        check(not mismatched, f"md5sums 与实际内容一致（{len(lines)} 个文件）")
        for m in mismatched[:5]:
            print(f"      {m}")


def verify_tarball(path: pathlib.Path) -> None:
    print(f"\n=== {path.name} ({path.stat().st_size / 1024 / 1024:.1f} MB)")

    with tarfile.open(path, "r:gz") as tar:
        names = tar.getnames()
        top = {n.split("/")[0] for n in names}
        check(len(top) == 1, f"单一顶层目录：{top}")

        prefix = next(iter(top))
        for expected in ["WorkStation", "install.sh", "uninstall.sh",
                         "workstation.desktop", "README.txt"]:
            check(f"{prefix}/{expected}" in names, f"含 {expected}")

        inst = tar.getmember(f"{prefix}/install.sh")
        check(inst.mode & 0o111 == 0o111, f"install.sh 可执行（mode={oct(inst.mode)}）")

        app = tar.getmember(f"{prefix}/WorkStation")
        check(app.mode & 0o111 == 0o111, f"WorkStation 可执行（mode={oct(app.mode)}）")

        icons = [n for n in names if "/icons/" in n and n.endswith(".png")]
        check(len(icons) == 7, f"含 7 个尺寸的图标：{len(icons)}")

        # install.sh 的内容检查
        script = tar.extractfile(f"{prefix}/install.sh").read().decode("utf-8")
        check("ln -sf" in script, "install.sh 会建软链")
        check("share/applications" in script, "install.sh 会装桌面菜单项")
        check(".local" in script, "install.sh 装到用户目录（不需要 root）")


def main() -> int:
    debs = sorted(OUT_DIR.glob("*.deb"))
    tars = sorted(OUT_DIR.glob("*.tar.gz"))

    if not debs and not tars:
        print(f"{OUT_DIR} 里没有安装包，先跑 tools\\linux\\make-linux-packages.py")
        return 1

    for d in debs:
        verify_deb(d)
    for t in tars:
        verify_tarball(t)

    print()
    if errors:
        print(f"✗ {len(errors)} / {checks} 项不通过")
        for e in errors:
            print(f"   - {e}")
        return 1
    print(f"✓ {checks} 项全部通过")
    return 0


if __name__ == "__main__":
    sys.exit(main())
