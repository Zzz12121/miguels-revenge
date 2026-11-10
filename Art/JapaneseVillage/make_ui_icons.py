"""
換武器 HUD 用的簡單圖示（白色剪影 + 深色外框，任何背景都看得清楚）：

    python Art/JapaneseVillage/make_ui_icons.py

輸出到 Assets/_Project/Resources/UI/：
  JV_Icon_Bow.png    弓（弓身 + 弓弦）
  JV_Icon_Arrow.png  一支箭（直立，用來排出剩下幾支）
  JV_Icon_Knife.png  小刀（箭射完之後）
"""
import math
import os

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Project", "Resources", "UI"))
SS = 4  # 先畫 4 倍大再縮小，邊緣比較平滑
WHITE = (245, 240, 228, 255)
OUTLINE = (20, 16, 12, 255)


def finish(img, size, name):
    """白色圖形加一圈深色外框，縮小存檔"""
    alpha = img.split()[3]
    outline = alpha.filter(ImageFilter.MaxFilter(SS * 2 + 1))
    base = Image.new("RGBA", img.size, (0, 0, 0, 0))
    base.paste(OUTLINE, (0, 0), outline)
    base.alpha_composite(img)
    base = base.resize(size, Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name)
    base.save(path, optimize=True)
    print("saved", path, base.size)


def bow():
    w, h = 64 * SS, 64 * SS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 弓身：左邊鼓起的弧線，兩端稍微反曲
    pts = []
    for i in range(41):
        t = i / 40.0
        y = h * (0.08 + 0.84 * t)
        bulge = math.sin(math.pi * t)
        x = w * (0.62 - 0.36 * bulge + 0.05 * math.sin(math.pi * 2 * t) * 0)
        pts.append((x, y))
    d.line(pts, fill=WHITE, width=int(5.5 * SS), joint="curve")
    # 握把：中間粗一點
    mx, my = pts[20]
    d.rounded_rectangle([mx - 4 * SS, my - 7 * SS, mx + 4 * SS, my + 7 * SS], radius=2 * SS, fill=WHITE)
    # 弓弦：兩端連起來
    d.line([pts[0], pts[-1]], fill=WHITE, width=int(1.8 * SS))
    # 搭在弦上的箭
    d.line([(w * 0.14, h * 0.5), (w * 0.92, h * 0.5)], fill=WHITE, width=int(2.2 * SS))
    d.polygon([(w * 0.97, h * 0.5), (w * 0.86, h * 0.44), (w * 0.86, h * 0.56)], fill=WHITE)
    for k in (0, 1):
        x0 = w * (0.62 + 0.07 * k)
        d.polygon([(x0, h * 0.5), (x0 + 0.06 * w, h * 0.42), (x0 + 0.09 * w, h * 0.42), (x0 + 0.03 * w, h * 0.5)], fill=WHITE)
        d.polygon([(x0, h * 0.5), (x0 + 0.06 * w, h * 0.58), (x0 + 0.09 * w, h * 0.58), (x0 + 0.03 * w, h * 0.5)], fill=WHITE)
    finish(img, (64, 64), "JV_Icon_Bow.png")


def arrow():
    w, h = 12 * SS, 40 * SS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cx = w / 2
    d.line([(cx, h * 0.14), (cx, h * 0.92)], fill=WHITE, width=int(2 * SS))
    d.polygon([(cx, h * 0.02), (cx - 3.6 * SS, h * 0.18), (cx + 3.6 * SS, h * 0.18)], fill=WHITE)
    for y in (0.74, 0.82):
        d.polygon([(cx, h * y), (cx - 4 * SS, h * (y + 0.1)), (cx - 4 * SS, h * (y + 0.16)), (cx, h * (y + 0.07))], fill=WHITE)
        d.polygon([(cx, h * y), (cx + 4 * SS, h * (y + 0.1)), (cx + 4 * SS, h * (y + 0.16)), (cx, h * (y + 0.07))], fill=WHITE)
    finish(img, (12, 40), "JV_Icon_Arrow.png")


def knife():
    w, h = 64 * SS, 64 * SS
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # 斜放的短刀：左下是握把、右上是刀尖
    def P(u, v):
        # u 沿刀身（0 = 柄尾，1 = 刀尖），v 垂直刀身
        ang = math.radians(-45)
        ox, oy = w * 0.18, h * 0.82
        L = w * 0.92
        x = ox + u * L * math.cos(ang) - v * w * math.sin(ang)
        y = oy + u * L * math.sin(ang) + v * w * math.cos(ang)
        return (x, y)
    d.polygon([P(0.0, -0.035), P(0.3, -0.035), P(0.3, 0.035), P(0.0, 0.035)], fill=WHITE)            # 握把
    d.polygon([P(0.3, -0.1), P(0.34, -0.1), P(0.34, 0.1), P(0.3, 0.1)], fill=WHITE)                  # 護手
    d.polygon([P(0.34, -0.05), P(0.86, -0.05), P(1.0, 0.03), P(0.34, 0.05)], fill=WHITE)             # 刀身
    finish(img, (64, 64), "JV_Icon_Knife.png")


if __name__ == "__main__":
    bow()
    arrow()
    knife()
