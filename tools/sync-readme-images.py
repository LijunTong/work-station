"""把 VisualCheck 生成的截图同步到 docs/images（README 里引用的那套）。

用法（仓库根目录执行）：

    python tools/sync-readme-images.py

先跑一次截图用例产出 .shots/：

    dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj --filter FullyQualifiedName~VisualCheck

截图命名映射表在下面 IMAGE_MAP 里 —— .shots/ 里的文件名带序号前缀（s01-…），
README 里用的是语义化名字，两边靠这张表对应。
"""

from __future__ import annotations

import pathlib
import shutil
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parent
SHOTS = ROOT / ".shots"
IMAGES = ROOT / "docs" / "images"

# .shots 里的文件名 -> docs/images 里的文件名
IMAGE_MAP = {
    "s14-dashboard.png": "dashboard.png",
    "s01-board-light.png": "board-light.png",
    "s05-board-dark.png": "board-dark.png",
    "s02-tasks-light.png": "tasks.png",
    "s10-projects.png": "projects.png",
    "s03-reminders-light.png": "reminders.png",
    "s22-custom-theme-settings.png": "custom-theme.png",
    "s21-custom-theme-dialog.png": "custom-theme-dialog.png",
    "s20-palette.png": "command-palette.png",
}


def main() -> int:
    if not SHOTS.is_dir():
        print(f"找不到截图目录：{SHOTS}")
        print("先跑一次：dotnet test tests/WorkStation.Tests/WorkStation.Tests.csproj "
              "--filter FullyQualifiedName~VisualCheck")
        return 1

    IMAGES.mkdir(parents=True, exist_ok=True)

    missing: list[str] = []
    for src_name, dst_name in IMAGE_MAP.items():
        src = SHOTS / src_name
        if not src.is_file():
            missing.append(src_name)
            continue

        dst = IMAGES / dst_name
        shutil.copy2(src, dst)
        print(f"  {src_name}  ->  docs/images/{dst_name}  ({dst.stat().st_size // 1024} KB)")

    if missing:
        print()
        print("缺少这些截图（先跑 VisualCheck 用例）：")
        for name in missing:
            print(f"  - {name}")
        return 1

    # 反向检查：docs/images 里有没有 README 不再引用的孤儿
    readme = (ROOT / "README.md").read_text(encoding="utf-8")
    orphans = [
        p.name for p in IMAGES.glob("*.png")
        if f"docs/images/{p.name}" not in readme
    ]
    if orphans:
        print()
        print("docs/images 里这些图 README 没引用（可删）：")
        for name in orphans:
            print(f"  - {name}")

    print()
    print(f"完成，{len(IMAGE_MAP)} 张图已同步到 {IMAGES}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
