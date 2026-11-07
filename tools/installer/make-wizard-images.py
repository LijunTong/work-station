"""
生成 Inno Setup 向导用的横幅图。

Inno Setup 6 的 [Setup] 段可以指定：
    WizardImageFile=...        欢迎页 / 完成页左侧的大图（推荐 164x314）
    WizardSmallImageFile=...   其它页面左上角的小图（推荐 55x58）

默认那张 "Install" 图标很旧，跟应用的蓝色调完全不搭。这里用应用自己的
图标 + 主色渐变生成一套，看着才像自家产品。

用法：python tools/installer/make-wizard-images.py
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).parent
ROOT = HERE.parent.parent
ICON = ROOT / "src" / "WorkStation.App" / "Assets" / "app.ico"

# 应用主色（图标里的那个蓝）
PRIMARY = (47, 155, 255)
PRIMARY_DARK = (10, 89, 247)

WIZARD_W, WIZARD_H = 164, 314
SMALL_W, SMALL_H = 55, 58


def vertical_gradient(size, top, bottom):
    """自上而下的线性渐变。"""
    w, h = size
    grad = Image.new("RGB", (1, h))
    px = grad.load()
    for y in range(h):
        t = y / max(1, h - 1)
        px[0, y] = (
            round(top[0] + (bottom[0] - top[0]) * t),
            round(top[1] + (bottom[1] - top[1]) * t),
            round(top[2] + (bottom[2] - top[2]) * t),
        )
    return grad.resize((w, h), Image.BILINEAR)


def load_icon(size):
    """从 .ico 里取指定尺寸的那张，透明通道保留。"""
    im = Image.open(ICON)
    # PIL 只在 im.size 等于目标尺寸时才给出对应的那张
    for candidate in sorted(im.info.get("sizes", []), reverse=True):
        im.size = candidate
        im = im.convert("RGBA")
        if candidate[0] >= size:
            break
    return im.resize((size, size), Image.LANCZOS)


def make_wizard_image():
    """欢迎页 / 完成页的大图。"""
    base = vertical_gradient((WIZARD_W, WIZARD_H), PRIMARY, PRIMARY_DARK).convert("RGBA")

    # 右上角几团柔光，避免纯渐变太死板
    glow = Image.new("RGBA", base.size, (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    for cx, cy, r, alpha in ((140, 40, 60, 46), (30, 250, 80, 30), (150, 210, 45, 26)):
        gd.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 255, 255, alpha))
    glow = glow.filter(ImageFilter.GaussianBlur(22))
    base = Image.alpha_composite(base, glow)

    # 应用图标居中偏上，带一点投影
    icon = load_icon(96)
    shadow = Image.new("RGBA", base.size, (0, 0, 0, 0))
    sd = ImageDraw.Draw(shadow)
    sd.rounded_rectangle((34, 74, 130, 170), radius=18, fill=(0, 0, 0, 70))
    shadow = shadow.filter(ImageFilter.GaussianBlur(10))
    base = Image.alpha_composite(base, shadow)
    base.alpha_composite(icon, (34, 68))

    # 底部一条白色细线 + 应用名，视觉上收个尾
    dd = ImageDraw.Draw(base)
    dd.rectangle((0, WIZARD_H - 3, WIZARD_W, WIZARD_H), fill=(255, 255, 255, 200))
    return base


def make_small_image():
    """其它页面左上角的小图。"""
    base = vertical_gradient((SMALL_W, SMALL_H), PRIMARY, PRIMARY_DARK).convert("RGBA")
    icon = load_icon(44)
    base.alpha_composite(icon, (6, 7))
    return base


def save_bmp24(img, path):
    """存成 Inno Setup 能吃的 BMP 格式。

    WizardImageFile 只接受**24 位、无压缩**的 BMP（不带 alpha 通道）。
    PIL 默认会存成 32 位带 alpha 的，Inno 会直接拒绝或显示异常，
    所以这里显式转成 RGB 再存。
    """
    rgb = img.convert("RGB")
    rgb.save(path, format="BMP")
    with Image.open(path) as check:
        print(f"  {path.name}  {check.size}  mode={check.mode}")


def main():
    out = HERE / "wizard"
    out.mkdir(exist_ok=True)
    print("生成向导图片：")
    save_bmp24(make_wizard_image(), out / "wizard.bmp")
    save_bmp24(make_small_image(), out / "wizard-small.bmp")
    print(f"输出目录：{out}")


if __name__ == "__main__":
    main()
