"""
日式村莊祠堂裡的四幅古畫與一首詩（程式畫的水墨畫，原創）。

    python Art/JapaneseVillage/generate_paintings.py               # 四幅畫從 Paintings_Source.png 切出來
    python Art/JapaneseVillage/generate_paintings.py --procedural  # 四幅畫改用下面的程式重畫（舊版）

四幅畫目前用的是使用者提供的 Paintings_Source.png（2 × 2 拼成一張，左上起：海上鬥龍、與民同醉、百足現世、黑衣奪骸），
切開後做舊（wear：撕破的透明邊、缺角、蟲洞、水漬、摺痕、泛黃），輸出 RGBA。
詩和後院大門的漩渦一律由程式產生。輸出到 Assets/_Project/Materials/JapaneseVillage/Paintings/：
  P1_SeaDragon.png      其一　海上鬥龍：道士在海上與龍戰鬥
  P2_Feast.png          其二　與民同醉：道士與村民飲酒作樂
  P3_Centipede.png      其三　百足現世：蜈蚣怪物出現，擊敗道士
  P4_BlackRobes.png     其四　黑衣奪骸：黑衣人集團奪走道士的遺體
  Poem.png              弔玄清（詩）
  ../T_JV_DojoGateVortex.png  村長家後院大門的傳送門光面（墨色漩渦，有透明度）
同樣的程式每次畫出一樣的圖（固定亂數種子）。文字用 Windows 內建的標楷體 kaiu.ttf。
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Materials', 'JapaneseVillage', 'Paintings')
FONT = r'C:\Windows\Fonts\kaiu.ttf'

SS = 2                      # 先用兩倍大小畫，最後縮小（邊緣比較平滑）
INK = (0.07, 0.065, 0.06)
INK_GREY = (0.32, 0.31, 0.29)
PAPER_TONE = (0.87, 0.80, 0.66)
BONE = (0.86, 0.82, 0.70)
RED = (0.72, 0.17, 0.10)
SEAL_RED = (0.70, 0.12, 0.08)
BLOOD = (0.55, 0.07, 0.05)
INDIGO = (0.26, 0.34, 0.42)
OCHRE = (0.70, 0.52, 0.28)
PINK = (0.90, 0.64, 0.66)


# ---------------------------------------------------------------- 基本工具

def smooth_noise(w, h, cell, seed):
    rng = np.random.default_rng(seed)
    small = rng.random((max(2, h // cell), max(2, w // cell))).astype(np.float32)
    img = Image.fromarray((small * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
    return np.asarray(img, np.float32) / 255.0


def catmull(points, spacing=1.5):
    """把控制點連成平滑曲線，回傳間距約 spacing 的點"""
    pts = [points[0]] + list(points) + [points[-1]]
    out = []
    for i in range(1, len(pts) - 2):
        p0, p1, p2, p3 = (np.array(p, float) for p in pts[i - 1:i + 3])
        seg = max(2, int(np.linalg.norm(p2 - p1) / spacing))
        for k in range(seg):
            t = k / seg
            t2, t3 = t * t, t * t * t
            out.append(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    out.append(np.array(points[-1], float))
    return out


def normals(path):
    res = []
    for i in range(len(path)):
        a = path[max(0, i - 1)]
        b = path[min(len(path) - 1, i + 1)]
        d = b - a
        n = np.linalg.norm(d)
        d = d / n if n > 1e-6 else np.array([1.0, 0.0])
        res.append((d, np.array([-d[1], d[0]])))
    return res


class Painting:
    def __init__(self, w, h, seed):
        self.W, self.H = w, h
        self.w, self.h = w * SS, h * SS
        self.rng = random.Random(seed)
        self.seed = seed
        self.rgb = self.paper()
        self.dry = smooth_noise(self.w, self.h, 3, seed + 7) * 0.6 + smooth_noise(self.w, self.h, 18, seed + 8) * 0.4

    # 舊絹紙：底色、斑駁、纖維、四周變暗、霉斑
    def paper(self):
        w, h = self.w, self.h
        n1 = smooth_noise(w, h, 260, self.seed + 1)
        n2 = smooth_noise(w, h, 50, self.seed + 2)
        n3 = smooth_noise(w, h, 4, self.seed + 3)
        shade = 0.9 + 0.07 * n1 + 0.04 * n2 + 0.035 * (n3 - 0.5)
        rgb = np.array(PAPER_TONE, np.float32)[None, None, :] * shade[..., None]
        yy, xx = np.mgrid[0:h, 0:w]
        edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy)) / min(w, h)
        vig = np.clip(edge / 0.14, 0, 1) ** 0.6
        rgb *= (0.8 + 0.2 * vig)[..., None]
        brown = np.array((0.55, 0.40, 0.24), np.float32)
        rgb = rgb * (1 - (1 - vig[..., None]) * 0.25) + brown * (1 - vig[..., None]) * 0.25
        # 絹的橫紋
        fib = smooth_noise(w, 1, 1, self.seed + 4)
        lines = np.repeat(smooth_noise(1, h, 2, self.seed + 5), w, axis=1)
        rgb *= (0.985 + 0.03 * (lines - 0.5))[..., None]
        img = Image.new('L', (w, h), 0)
        d = ImageDraw.Draw(img)
        for _ in range(26):
            x, y = self.rng.random() * w, self.rng.random() * h
            r = (4 + self.rng.random() * 28) * SS
            d.ellipse([x - r, y - r * 0.8, x + r, y + r * 0.8], fill=int(40 + self.rng.random() * 50))
        a = np.asarray(img.filter(ImageFilter.GaussianBlur(6 * SS)), np.float32) / 255
        rgb = rgb * (1 - a[..., None] * 0.5) + brown * a[..., None] * 0.5
        return rgb

    def P(self, x, y):
        return (x * SS, y * SS)

    def mask(self):
        m = Image.new('L', (self.w, self.h), 0)
        return m, ImageDraw.Draw(m)

    def put(self, m, color, alpha=1.0, blur=0.0, dry=0.0):
        box = m.getbbox()
        if box is None or alpha <= 0:
            return
        # 只處理有畫到的範圍（模糊會往外擴，先留邊）
        pad = int(blur * SS * 3) + 2
        x0, y0 = max(0, box[0] - pad), max(0, box[1] - pad)
        x1, y1 = min(self.w, box[2] + pad), min(self.h, box[3] + pad)
        m = m.crop((x0, y0, x1, y1))
        if blur:
            m = m.filter(ImageFilter.GaussianBlur(blur * SS))
        a = np.asarray(m, np.float32) / 255.0 * alpha
        if dry:
            a = a * np.clip(1 - dry * (self.dry[y0:y1, x0:x1] - 0.35) * 2.2, 0, 1)
        c = np.array(color, np.float32)
        sub = self.rgb[y0:y1, x0:x1]
        self.rgb[y0:y1, x0:x1] = sub * (1 - a[..., None]) + c * a[..., None]

    def put_alpha(self, a, color):
        c = np.array(color, np.float32)
        self.rgb = self.rgb * (1 - a[..., None]) + c * a[..., None]

    def gradient(self, y0, y1, a0, a1, color, noise=0.0, cell=120):
        yy = np.linspace(0, 1, self.h)[:, None] * self.H
        t = np.clip((yy - y0) / (y1 - y0), 0, 1)
        a = (a0 + (a1 - a0) * t) * np.ones((1, self.w))
        if noise:
            a = a * (1 - noise + noise * 2 * smooth_noise(self.w, self.h, cell * SS, self.rng.randint(0, 9999)))
        self.put_alpha(np.clip(a, 0, 1).astype(np.float32), color)

    # 筆觸：沿著曲線蓋圓點，寬度從 w0 變到 w1，兩端收筆
    def stroke(self, d, pts, w0, w1=None, taper=0.2, spacing=1.2, jitter=0.0):
        if w1 is None:
            w1 = w0
        path = catmull(pts, spacing) if len(pts) > 2 else [np.array(pts[0], float) + (np.array(pts[1], float) - np.array(pts[0], float)) * t
                                                            for t in np.linspace(0, 1, max(2, int(np.hypot(pts[1][0] - pts[0][0], pts[1][1] - pts[0][1]) / spacing)))]
        n = len(path)
        for i, p in enumerate(path):
            t = i / max(1, n - 1)
            wd = w0 + (w1 - w0) * t
            if taper:
                wd *= min(1.0, t / taper + 0.15, (1 - t) / taper + 0.15)
            r = max(0.4, wd * 0.5) * SS
            x, y = p[0] * SS, p[1] * SS
            if jitter:
                x += self.rng.uniform(-jitter, jitter) * SS
                y += self.rng.uniform(-jitter, jitter) * SS
            d.ellipse([x - r, y - r, x + r, y + r], fill=255)

    def line(self, pts, w0, color=INK, alpha=0.9, w1=None, taper=0.2, dry=0.25, blur=0.0):
        m, d = self.mask()
        self.stroke(d, pts, w0, w1, taper)
        self.put(m, color, alpha, blur, dry)

    def poly(self, pts, color, alpha=1.0, blur=0.0, dry=0.0, outline=None, ow=3.0):
        m, d = self.mask()
        d.polygon([self.P(*p) for p in pts], fill=255)
        self.put(m, color, alpha, blur, dry)
        if outline is not None:
            self.line(list(pts) + [pts[0]], ow, outline, 0.85, taper=0)

    def ellipse(self, cx, cy, rx, ry, color, alpha=1.0, blur=0.0, dry=0.0):
        m, d = self.mask()
        d.ellipse([(cx - rx) * SS, (cy - ry) * SS, (cx + rx) * SS, (cy + ry) * SS], fill=255)
        self.put(m, color, alpha, blur, dry)

    def wash(self, pts, color, alpha, blur):
        self.poly(pts, color, alpha, blur=blur, dry=0.3)

    def splatter(self, cx, cy, radius, count, size, color, alpha=0.9):
        m, d = self.mask()
        for _ in range(count):
            ang = self.rng.random() * math.tau
            r = radius * self.rng.random() ** 0.6
            s = size * (0.3 + self.rng.random())
            x, y = cx + math.cos(ang) * r, cy + math.sin(ang) * r
            d.ellipse([(x - s) * SS, (y - s) * SS, (x + s) * SS, (y + s) * SS], fill=255)
        self.put(m, color, alpha)

    # 直書題字 + 紅色印章
    def inscription(self, text, x, y, size, color=INK, seal='玄清', seal_size=None):
        font = ImageFont.truetype(FONT, int(size * SS))
        m, d = self.mask()
        for i, ch in enumerate(text):
            d.text((x * SS, (y + i * size * 1.12) * SS), ch, font=font, fill=255, anchor='mt')
        self.put(m, color, 0.9, dry=0.15)
        if seal:
            s = seal_size or size * 0.95
            sy = y + len(text) * size * 1.12 + size * 0.35
            self.seal(seal, x, sy, s)

    def seal(self, text, x, y, size):
        m, d = self.mask()
        d.rectangle([(x - size * 0.5) * SS, y * SS, (x + size * 0.5) * SS, (y + size) * SS], fill=255)
        font = ImageFont.truetype(FONT, int(size * 0.42 * SS))
        chars = list(text)
        # 兩個字：直排；四個字：田字格（印章右上起讀）
        if len(chars) == 2:
            spots = [(x, y + size * 0.27), (x, y + size * 0.73)]
        else:
            spots = [(x + size * 0.22, y + size * 0.27), (x + size * 0.22, y + size * 0.73), (x - size * 0.22, y + size * 0.27), (x - size * 0.22, y + size * 0.73)]
        for ch, (cx, cy) in zip(chars, spots):
            d.text((cx * SS, cy * SS), ch, font=font, fill=0, anchor='mm')
        self.put(m, SEAL_RED, 0.85, dry=0.35)

    def save(self, name):
        rgb = np.clip(self.rgb, 0, 1)
        img = Image.fromarray((rgb * 255).astype(np.uint8), 'RGB').resize((self.W, self.H), Image.LANCZOS)
        os.makedirs(OUT, exist_ok=True)
        path = os.path.join(OUT, name)
        img.save(path, optimize=True)
        print('saved', path)


# ---------------------------------------------------------------- 人物

def figure(p, x, y, h, pose='stand', robe=(0.62, 0.66, 0.68), robe_alpha=0.55, angle=0.0, flip=1,
           arm_l=200.0, arm_r=160.0, item_r=None, item_l=None, hood=False, hat=None, black=False, wind=0.0):
    """
    一個人：(x, y) 是腳底（坐姿是臀部），h 是站姿身高。
    arm_l / arm_r：手臂角度（度，0 = 往下垂，90 = 往側面平舉，180 = 舉高）。
    item：'sword'、'cup'、'fan'、'flute'、'lantern'、'pole'、'brokensword'。
    angle：整個人旋轉（倒地用 90）。black：整身黑衣（黑衣人）。
    """
    ca, sa = math.cos(math.radians(angle)), math.sin(math.radians(angle))

    def T(u, v):
        u *= flip
        return (x + (u * ca - v * sa) * h, y - (u * sa + v * ca) * h)

    seated = pose in ('seat', 'seat_cup', 'seat_flute')
    top = 0.5 if seated else 0.78
    head_v = top + 0.09
    ink_alpha = 0.92
    body_color = INK if black else robe
    body_alpha = 0.93 if black else robe_alpha

    # 身體（長袍）
    if seated:
        robe_pts = [(-0.1, top), (0.1, top), (0.18, 0.25), (0.34, 0.04), (0.05, -0.02), (-0.34, 0.03), (-0.2, 0.25)]
    else:
        hem = 0.21 + wind * 0.08
        robe_pts = [(-0.1, top), (0.1, top), (0.13, 0.45), (hem, 0.02), (0.05, -0.01), (-hem - wind * 0.15, 0.03), (-0.14 - wind * 0.08, 0.45)]
    pts = [T(u, v) for u, v in robe_pts]
    p.poly(pts, body_color, body_alpha, dry=0.2)
    p.line(pts[:4], 3.2, INK, ink_alpha, taper=0.1)
    p.line(pts[3:] + [pts[0]], 3.0, INK, ink_alpha, taper=0.1)
    if not black:
        # 腰帶和衣褶
        p.line([T(-0.11, top - 0.2), T(0.0, top - 0.22), T(0.12, top - 0.2)], 4.5, RED if not hood else INK, 0.75, taper=0)
        p.line([T(0.02, top - 0.24), T(0.04, 0.3), T(0.07, 0.06)], 1.6, INK, 0.6)
        p.line([T(-0.05, top - 0.25), T(-0.09, 0.28), T(-0.14, 0.06)], 1.6, INK, 0.5)

    # 手臂（寬袖子）
    hands = {}
    for side, ang, item in ((-1, arm_l, item_l), (1, arm_r, item_r)):
        sx, sy = side * 0.09, top - 0.02
        rad = math.radians(ang)
        hx, hy = sx + side * math.sin(rad) * 0.3, sy - math.cos(rad) * 0.3
        hands[side] = (hx, hy)
        sleeve = [(sx - 0.05, sy + 0.02), (sx + 0.05, sy - 0.02), (hx + 0.06 * side, hy - 0.05), (hx - 0.02 * side, hy - 0.12 - wind * 0.05), (hx - 0.05 * side, hy + 0.02)]
        spts = [T(u, v) for u, v in sleeve]
        p.poly(spts, body_color, body_alpha, dry=0.25)
        p.line(spts + [spts[0]], 2.6, INK, ink_alpha * 0.9, taper=0)
        if not black:
            p.ellipse(*T(hx, hy), 0.025 * h, 0.025 * h, (0.85, 0.72, 0.58), 0.9)
        draw_item(p, item, T, hx, hy, side, ang, h)

    # 頭
    hx_, hy_ = T(0, head_v)
    if black or hood:
        p.ellipse(hx_, hy_, 0.075 * h, 0.085 * h, INK, 0.95)
        p.line([T(-0.05, head_v + 0.03), T(0.0, head_v + 0.13), T(0.06, head_v + 0.02)], 3, INK, 0.9, taper=0)
        if black:
            p.line([T(0.0, head_v - 0.01), T(0.055, head_v - 0.0)], 2.2, BONE, 0.5, taper=0)   # 面罩縫隙
    else:
        p.ellipse(hx_, hy_, 0.058 * h, 0.065 * h, (0.86, 0.74, 0.60), 0.95)
        p.line([T(-0.055, head_v), T(-0.04, head_v + 0.06), T(0.03, head_v + 0.07), T(0.058, head_v + 0.01)], 2.4, INK, 0.9, taper=0)
        if hat == 'knot':          # 道士的髮髻 + 簪子
            p.ellipse(*T(0, head_v + 0.085), 0.03 * h, 0.03 * h, INK, 0.95)
            p.line([T(-0.06, head_v + 0.09), T(0.06, head_v + 0.08)], 2.2, INK, 0.9, taper=0)
        elif hat == 'kasa':        # 斗笠
            p.poly([T(-0.17, head_v + 0.02), T(0, head_v + 0.14), T(0.17, head_v + 0.02)], OCHRE, 0.85, dry=0.3, outline=INK, ow=2.2)
        elif hat == 'bun':
            p.ellipse(*T(-0.02, head_v + 0.07), 0.04 * h, 0.03 * h, INK, 0.95)
        else:
            p.line([T(-0.05, head_v + 0.04), T(0.0, head_v + 0.075), T(0.05, head_v + 0.04)], 5, INK, 0.9, taper=0)
        if wind and hat == 'knot':  # 飄帶
            p.line([T(0.0, head_v + 0.07), T(-0.2, head_v + 0.1), T(-0.38, head_v + 0.02)], 3, robe if not black else INK, 0.8)
    return hands


def draw_item(p, item, T, hx, hy, side, ang, h):
    if item is None:
        return
    rad = math.radians(ang)
    dx, dy = side * math.sin(rad), -math.cos(rad)
    if item == 'sword':
        p.line([T(hx, hy), T(hx + dx * 0.6, hy + dy * 0.6)], 3.2, (0.55, 0.57, 0.6), 0.95, taper=0.05)
        p.line([T(hx, hy), T(hx + dx * 0.6, hy + dy * 0.6)], 1.4, INK, 0.9, taper=0.05)
        p.line([T(hx - dy * 0.05, hy + dx * 0.05), T(hx + dy * 0.05, hy - dx * 0.05)], 3.5, INK, 0.9, taper=0)
    elif item == 'brokensword':
        p.line([T(hx, hy), T(hx + dx * 0.22, hy + dy * 0.22)], 3, (0.5, 0.52, 0.55), 0.95, taper=0)
    elif item == 'cup':
        cx, cy = T(hx, hy + 0.04)
        p.poly([(cx - 0.035 * h, cy - 0.03 * h), (cx + 0.035 * h, cy - 0.03 * h), (cx + 0.02 * h, cy + 0.02 * h), (cx - 0.02 * h, cy + 0.02 * h)], RED, 0.9, outline=INK, ow=1.5)
    elif item == 'fan':
        cx, cy = T(hx, hy)
        m, d = p.mask()
        r = 0.16 * h
        d.pieslice([(cx - r) * SS, (cy - r) * SS, (cx + r) * SS, (cy + r) * SS], 200, 340, fill=255)
        p.put(m, RED, 0.8, dry=0.2)
        p.line([(cx, cy), (cx - r * 0.9, cy - r * 0.35)], 1.5, INK, 0.8, taper=0)
        p.line([(cx, cy), (cx + r * 0.9, cy - r * 0.35)], 1.5, INK, 0.8, taper=0)
    elif item == 'flute':
        p.line([T(hx - 0.05, hy), T(hx + 0.28 * side, hy + 0.12)], 2.6, OCHRE, 0.95, taper=0)
    elif item == 'lantern':
        lx, ly = T(hx, hy - 0.2)
        p.line([T(hx, hy), T(hx, hy - 0.13)], 1.4, INK, 0.9, taper=0)
        p.ellipse(lx, ly, 0.11 * h, 0.17 * h, (0.95, 0.55, 0.2), 0.35, blur=16)
        p.ellipse(lx, ly, 0.05 * h, 0.07 * h, RED, 0.95)
        p.line([(lx - 0.05 * h, ly), (lx + 0.05 * h, ly)], 1.2, INK, 0.5, taper=0)
    elif item == 'spear':
        p.line([T(hx, hy - 0.4), T(hx + 0.05 * side, hy + 0.55)], 2.4, INK, 0.95, taper=0)
        p.poly([T(hx + 0.05 * side - 0.025, hy + 0.55), T(hx + 0.06 * side, hy + 0.7), T(hx + 0.05 * side + 0.025, hy + 0.55)], INK, 0.95)


# ---------------------------------------------------------------- 風景元素

def ruyi_cloud(p, cx, cy, r, alpha=0.55):
    """捲雲：一個漩渦 + 底下拖長的雲尾"""
    pts = []
    for k in range(60):
        t = k / 59
        ang = t * math.tau * 1.4
        rr = r * (0.15 + t * 0.85)
        pts.append((cx + math.cos(ang) * rr, cy + math.sin(ang) * rr * 0.7))
    p.line(pts, 3.2, INK_GREY, alpha)
    p.line([(cx - r * 1.0, cy + r * 0.62), (cx + r * 1.4, cy + r * 0.62), (cx + r * 2.8, cy + r * 0.45)], 2.4, INK_GREY, alpha * 0.8)
    p.ellipse(cx + r * 0.6, cy + r * 0.25, r * 1.8, r * 0.7, INK_GREY, 0.12, blur=r * 0.5)


def wave_crest(p, x, y, s, alpha=0.85, color=INK):
    """浪頭：往上捲起再往前倒下，前端有爪狀浪花"""
    main = [(x - s * 1.4, y + s * 0.5), (x - s * 0.7, y + s * 0.15), (x - s * 0.1, y - s * 0.55), (x + s * 0.45, y - s * 0.75),
            (x + s * 0.85, y - s * 0.5), (x + s * 0.8, y - s * 0.2), (x + s * 0.55, y - s * 0.12)]
    p.poly(main + [(x + s * 0.2, y + s * 0.2), (x - s * 0.5, y + s * 0.5)], INDIGO, 0.25, blur=1)
    p.line(main, max(2.5, s * 0.06), color, alpha, taper=0.12)
    for k in (0.25, 0.5):
        inner = [(px + (x - px) * k * 0.3, py + s * k * 0.35) for px, py in main[:5]]
        p.line(inner, max(1.4, s * 0.03), color, alpha * 0.7)
    # 浪花的爪
    for i in range(5):
        a0 = -0.2 + i * 0.35
        bx, by = x + s * (0.45 + 0.35 * math.cos(a0 * 2)), y - s * (0.55 + 0.2 * math.sin(a0))
        p.line([(bx, by), (bx + s * 0.12, by + s * 0.02), (bx + s * 0.18, by + s * 0.12)], max(1.6, s * 0.035), color, alpha * 0.9)
    p.splatter(x + s * 0.7, y - s * 0.6, s * 0.35, 18, s * 0.02, PAPER_TONE, 0.9)


def rock(p, pts, alpha=0.9):
    p.poly(pts, INK_GREY, 0.55, dry=0.6)
    p.line(pts + [pts[0]], 4.5, INK, alpha, taper=0.05)
    cx = sum(q[0] for q in pts) / len(pts)
    for i in range(4):
        a = pts[i % len(pts)]
        p.line([a, ((a[0] + cx) / 2 + p.rng.uniform(-10, 10), (a[1] + pts[0][1]) / 2 + 20)], 2.5, INK, 0.6)


def bamboo(p, x, y_bottom, height, w, alpha, lean=0.0):
    segs = int(height / (w * 7))
    for i in range(segs):
        y0 = y_bottom - i * height / segs
        y1 = y_bottom - (i + 1) * height / segs + w * 0.4
        x0, x1 = x + lean * (y_bottom - y0), x + lean * (y_bottom - y1)
        p.line([(x0, y0), (x1, y1)], w, INK, alpha, taper=0.0, dry=0.35)
    for _ in range(int(height / 60)):
        ly = y_bottom - p.rng.random() * height * 0.9 - height * 0.1
        lx = x + lean * (y_bottom - ly)
        direction = p.rng.choice((-1, 1))
        for k in range(p.rng.randint(2, 4)):
            ang = math.radians(p.rng.uniform(15, 55)) * direction
            length = w * p.rng.uniform(5, 8)
            ex, ey = lx + math.sin(ang) * length, ly + math.cos(ang) * length * 0.35 + k * w
            p.line([(lx, ly), ((lx + ex) / 2, (ly + ey) / 2 - w * 0.3), (ex, ey)], w * 0.9, INK, alpha * 0.95, taper=0.45)


def moon(p, cx, cy, r, glow_alpha=0.25):
    p.ellipse(cx, cy, r * 1.8, r * 1.8, (0.95, 0.9, 0.75), glow_alpha, blur=r * 0.7)
    p.ellipse(cx, cy, r, r, (0.96, 0.92, 0.8), 0.95)


def night_sky(p, y_end, alpha, moon_at=None, r=90):
    """moon_at 附近留白：整片夜色，月亮周圍不塗"""
    yy, xx = np.mgrid[0:p.h, 0:p.w] / SS
    t = np.clip(1 - yy / y_end, 0, 1) ** 0.8
    a = alpha * t * (0.75 + 0.5 * smooth_noise(p.w, p.h, 140 * SS, p.rng.randint(0, 999)))
    if moon_at:
        d = np.hypot(xx - moon_at[0], yy - moon_at[1])
        a *= np.clip((d - r * 1.1) / (r * 1.6), 0, 1)
    p.put_alpha(a.astype(np.float32), INK)


# ---------------------------------------------------------------- 其一　海上鬥龍

def dragon(p, spine, w_head, w_tail, head_dir):
    path = catmull(spine, 1.5)
    nrm = normals(path)
    n = len(path)
    widths = [w_head + (w_tail - w_head) * (i / (n - 1)) ** 0.8 for i in range(n)]

    # 背鰭（身體上側的鋸齒）
    m, d = p.mask()
    for i in range(10, n - 20, 16):
        c, (tg, nm) = path[i], nrm[i]
        wdt = widths[i]
        base1 = c + nm * wdt * 0.42 - tg * wdt * 0.22
        base2 = c + nm * wdt * 0.42 + tg * wdt * 0.22
        tip = c + nm * wdt * 0.95 + tg * wdt * 0.25
        d.polygon([p.P(*base1), p.P(*tip), p.P(*base2)], fill=255)
    p.put(m, INK, 0.9, dry=0.3)

    # 身體
    m, d = p.mask()
    for i in range(n):
        r = widths[i] * 0.5 * SS
        x, y = path[i][0] * SS, path[i][1] * SS
        d.ellipse([x - r, y - r, x + r, y + r], fill=255)
    p.put(m, INK, 0.93, dry=0.25)

    # 腹甲（下側淺色）
    m, d = p.mask()
    for i in range(0, n, 2):
        c, (tg, nm) = path[i], nrm[i]
        q = c - nm * widths[i] * 0.22
        r = widths[i] * 0.2 * SS
        d.ellipse([q[0] * SS - r, q[1] * SS - r, q[0] * SS + r, q[1] * SS + r], fill=255)
    p.put(m, OCHRE, 0.55, dry=0.3)
    for i in range(6, n, 12):
        c, (tg, nm) = path[i], nrm[i]
        a = c - nm * widths[i] * 0.4
        b = c - nm * widths[i] * 0.05
        p.line([a, b], 1.4, INK, 0.6, taper=0)

    # 鱗片：身上淡色的小弧
    m, d = p.mask()
    for i in range(4, n - 4, 7):
        c, (tg, nm) = path[i], nrm[i]
        for k in (-0.05, 0.18, 0.32):
            q = c + nm * widths[i] * k + tg * (3 if (i // 7) % 2 else -3)
            r = widths[i] * 0.11
            d.arc([(q[0] - r) * SS, (q[1] - r) * SS, (q[0] + r) * SS, (q[1] + r) * SS],
                  int(math.degrees(math.atan2(tg[1], tg[0]))) - 70, int(math.degrees(math.atan2(tg[1], tg[0]))) + 70, fill=255, width=int(2.2 * SS))
    p.put(m, (0.55, 0.52, 0.47), 0.8)

    # 四隻腳（每隻三根爪）
    for t, side in ((0.16, -1), (0.24, 1), (0.52, -1), (0.6, 1)):
        i = int(t * (n - 1))
        c, (tg, nm) = path[i], nrm[i]
        wdt = widths[i]
        knee = c - nm * wdt * 0.9 * (1 if side < 0 else -0.6) + tg * wdt * 0.6 * side
        foot = knee - nm * wdt * 0.5 + tg * wdt * 0.5
        p.line([c, knee, foot], wdt * 0.32, INK, 0.93, taper=0.05)
        for k in (-1, 0, 1):
            ang = math.atan2(tg[1], tg[0]) + k * 0.6 - 0.8
            tip = foot + np.array([math.cos(ang), math.sin(ang)]) * wdt * 0.45
            mid = (foot + tip) / 2 + np.array([-math.sin(ang), math.cos(ang)]) * wdt * 0.08
            p.line([foot, mid, tip], wdt * 0.1, INK, 0.95, taper=0.35)

    # 頭：細長的吻、張開的嘴、鹿角、鬚、鬃毛
    head = path[0]
    hd = np.array(head_dir, float)
    hd /= np.linalg.norm(hd)
    up = np.array([hd[1], -hd[0]])
    if up[1] > 0:
        up = -up
    s = w_head
    def H(a, b):
        q = head + hd * a * s + up * b * s
        return (q[0], q[1])
    # 鬃毛（火焰狀）
    for k in range(7):
        base = H(-0.4 - k * 0.12, 0.3)
        tip = H(-0.9 - k * 0.18, 0.9 + 0.25 * math.sin(k))
        p.line([base, H(-0.7 - k * 0.15, 0.7), tip], s * 0.12, INK, 0.9, taper=0.4)
    upper = [H(-0.4, 0.45), H(0.3, 0.42), H(1.05, 0.25), H(1.25, 0.08), H(0.95, -0.02), H(0.2, 0.02), H(-0.4, -0.2)]
    lower = [H(-0.2, -0.15), H(0.3, -0.12), H(0.95, -0.38), H(0.82, -0.5), H(0.1, -0.42), H(-0.35, -0.38)]
    p.poly(upper, INK, 0.95, dry=0.2)
    p.poly(lower, INK, 0.95, dry=0.2)
    p.poly([H(0.25, 0.0), H(0.9, -0.02), H(0.85, -0.3), H(0.25, -0.12)], (0.55, 0.12, 0.08), 0.9)   # 口腔
    for k in range(5):                                                                                 # 牙
        a = 0.3 + k * 0.13
        p.poly([H(a, 0.02), H(a + 0.05, -0.08), H(a + 0.09, 0.02)], BONE, 0.95)
    # 眼睛
    ex, ey = H(0.25, 0.28)
    p.ellipse(ex, ey, s * 0.12, s * 0.08, (0.92, 0.75, 0.3), 0.95)
    p.ellipse(ex, ey, s * 0.04, s * 0.06, INK, 0.95)
    # 鹿角
    for off in (0.0, 0.22):
        a0 = H(-0.15 - off, 0.42)
        a1 = H(-0.6 - off, 1.15)
        a2 = H(-1.0 - off, 1.55)
        p.line([a0, a1, a2], s * 0.13, (0.5, 0.42, 0.3), 0.95, taper=0.3)
        p.line([a1, H(-0.35 - off, 1.45)], s * 0.08, (0.5, 0.42, 0.3), 0.95, taper=0.4)
    # 長鬚
    for sign, ph in ((1, 0.0), (-1, 1.2)):
        root = H(1.0, 0.12 if sign > 0 else -0.3)
        pts = [root]
        for k in range(1, 9):
            pts.append(H(1.0 - k * 0.35, (0.12 if sign > 0 else -0.3) - k * 0.18 + 0.25 * math.sin(k * 0.9 + ph)))
        p.line(pts, s * 0.05, INK, 0.9, taper=0.5)
    return path


def flame_pearl(p, cx, cy, r):
    p.ellipse(cx, cy, r * 3, r * 3, (0.95, 0.55, 0.2), 0.3, blur=r)
    for k in range(9):
        ang = k / 9 * math.tau
        p.line([(cx + math.cos(ang) * r, cy + math.sin(ang) * r),
                (cx + math.cos(ang + 0.4) * r * 1.8, cy + math.sin(ang + 0.4) * r * 1.8),
                (cx + math.cos(ang + 0.2) * r * 2.4, cy + math.sin(ang + 0.2) * r * 2.4)], 4, (0.85, 0.35, 0.12), 0.85, taper=0.5)
    p.ellipse(cx, cy, r, r, (0.92, 0.62, 0.25), 0.95)
    p.ellipse(cx - r * 0.3, cy - r * 0.3, r * 0.3, r * 0.3, (1, 0.95, 0.8), 0.8)


def paint_sea_dragon():
    p = Painting(1536, 1024, 101)
    # 風暴的天空
    p.gradient(0, 640, 0.55, 0.05, INK, noise=0.5, cell=90)
    for cx, cy, r in ((260, 150, 55), (520, 90, 40), (1240, 120, 60), (980, 200, 38), (1420, 330, 45)):
        ruyi_cloud(p, cx, cy, r, 0.55)
    # 閃電
    p.line([(1150, 40), (1110, 150), (1150, 175), (1080, 320)], 3.5, (0.95, 0.9, 0.7), 0.9, taper=0.1, dry=0)
    # 海面
    p.gradient(560, 1024, 0.18, 0.5, INDIGO, noise=0.4, cell=60)
    p.wash([(0, 610), (1536, 590), (1536, 1024), (0, 1024)], INDIGO, 0.12, 30)
    # 遠處的浪
    for i in range(14):
        x = 80 + i * 110 + p.rng.uniform(-20, 20)
        wave_crest(p, x, 640 + p.rng.uniform(-10, 15), 22, 0.55, INK_GREY)
    # 龍
    spine = [(900, 330), (1010, 250), (1130, 300), (1190, 430), (1290, 470), (1380, 380), (1420, 260), (1500, 210), (1560, 240)]
    dragon(p, spine, 74, 14, head_dir=(-1.0, 0.45))
    # 龍珠在道士和龍中間
    flame_pearl(p, 640, 400, 24)
    # 近景的大浪 + 道士站的礁石
    for x, y, s in ((780, 860, 120), (1100, 820, 140), (1400, 880, 110), (560, 960, 90), (1250, 990, 80), (930, 1000, 85)):
        wave_crest(p, x, y, s, 0.9)
    rock(p, [(170, 1024), (210, 790), (290, 720), (380, 700), (450, 750), (500, 860), (540, 1024)])
    figure(p, 365, 712, 230, pose='stand', robe=(0.6, 0.66, 0.7), arm_r=128, item_r='sword', arm_l=230, hat='knot', wind=0.7)
    # 劍氣
    p.line([(470, 530), (560, 470), (610, 420)], 6, (0.95, 0.9, 0.75), 0.8, taper=0.3, dry=0)
    p.splatter(300, 760, 120, 40, 3, PAPER_TONE, 0.85)
    p.inscription('海上鬥龍', 90, 70, 52)
    p.save('P1_SeaDragon.png')


# ---------------------------------------------------------------- 其二　與民同醉

def sakura_branch(p, pts, blossoms=70):
    p.line(pts, 16, INK, 0.92, w1=5, taper=0.05, dry=0.45)
    path = catmull(pts, 4)
    for i in range(6, len(path) - 4, 14):
        a = path[i]
        tip = (a[0] + p.rng.uniform(-60, 80), a[1] + p.rng.uniform(20, 90))
        p.line([a, ((a[0] + tip[0]) / 2 + 10, (a[1] + tip[1]) / 2), tip], 5, INK, 0.9, w1=2, taper=0.1)
    for _ in range(blossoms):
        a = path[p.rng.randrange(len(path))]
        cx, cy = a[0] + p.rng.uniform(-70, 90), a[1] + p.rng.uniform(-20, 100)
        r = p.rng.uniform(7, 13)
        for k in range(5):
            ang = k / 5 * math.tau + p.rng.random()
            p.ellipse(cx + math.cos(ang) * r * 0.7, cy + math.sin(ang) * r * 0.7, r * 0.55, r * 0.55, PINK, 0.85)
        p.ellipse(cx, cy, r * 0.25, r * 0.25, RED, 0.8)


def paint_feast():
    p = Painting(1536, 1024, 202)
    moon_xy = (1230, 190)
    night_sky(p, 700, 0.62, moon_xy, 85)
    moon(p, *moon_xy, 85)
    # 遠山
    p.wash([(0, 560), (180, 470), (360, 520), (560, 430), (760, 520), (980, 450), (1200, 540), (1400, 470), (1536, 520), (1536, 640), (0, 640)], INK_GREY, 0.35, 12)
    # 左上的櫻花枝
    sakura_branch(p, [(-20, 60), (140, 110), (300, 90), (450, 150), (600, 140)], 80)
    # 燈籠串
    rope = [(160, 250), (420, 300), (700, 320), (980, 300), (1180, 330)]
    p.line(rope, 2, INK, 0.85, taper=0)
    for x, y in ((290, 290), (560, 322), (840, 322), (1080, 322)):
        p.line([(x, y - 8), (x, y + 18)], 1.5, INK, 0.9, taper=0)
        p.ellipse(x, y + 52, 62, 70, (0.95, 0.55, 0.2), 0.28, blur=24)
        p.ellipse(x, y + 52, 28, 36, RED, 0.95)
        p.line([(x - 26, y + 52), (x + 26, y + 52)], 1.2, INK, 0.5, taper=0)
        p.poly([(x - 14, y + 14), (x + 14, y + 14), (x + 14, y + 20), (x - 14, y + 20)], INK, 0.9)
    # 地面（木平台）
    p.wash([(0, 790), (1536, 770), (1536, 1024), (0, 1024)], OCHRE, 0.3, 6)
    for k in range(5):
        y = 800 + k * 45
        p.line([(0, y), (1536, y - 20)], 1.6, INK, 0.35, taper=0)
    # 矮桌、酒罈、酒杯
    p.poly([(640, 760), (1040, 760), (1080, 790), (600, 790)], (0.45, 0.28, 0.18), 0.9, outline=INK, ow=3)
    for x in (630, 1050):
        p.line([(x, 790), (x, 840)], 6, INK, 0.9, taper=0)
    for x, s in ((660, 1.0), (1010, 0.8)):
        p.ellipse(x, 720, 42 * s, 48 * s, OCHRE, 0.95)
        p.ellipse(x, 678 - (1 - s) * 30, 16 * s, 7 * s, INK, 0.9)
        p.line([(x - 40 * s, 720), (x + 40 * s, 720)], 2, INK, 0.5, taper=0)
        p.ellipse(x, 720, 42 * s, 48 * s, INK, 0.0)
    for x in (800, 860, 910):
        p.poly([(x - 12, 748), (x + 12, 748), (x + 8, 760), (x - 8, 760)], RED, 0.9, outline=INK, ow=1.2)
    # 人們
    figure(p, 840, 745, 260, pose='seat_cup', robe=(0.6, 0.66, 0.7), arm_r=165, item_r='cup', arm_l=60, hat='knot')   # 道士舉杯
    figure(p, 520, 800, 230, pose='seat', robe=(0.42, 0.3, 0.22), arm_r=120, item_r='cup', arm_l=40, hat='bun', flip=1)
    figure(p, 1180, 800, 240, pose='seat_flute', robe=INDIGO, robe_alpha=0.7, arm_l=95, arm_r=100, item_r='flute', hat=None, flip=-1)
    figure(p, 1380, 840, 300, pose='stand', robe=(0.7, 0.3, 0.25), arm_l=170, item_l='fan', arm_r=140, hat='bun', wind=0.4, flip=-1)
    figure(p, 300, 850, 300, pose='stand', robe=(0.35, 0.42, 0.3), arm_l=150, arm_r=200, item_r='cup', hat='kasa')         # 漁夫戴斗笠
    figure(p, 160, 880, 170, pose='stand', robe=(0.72, 0.5, 0.3), arm_l=140, arm_r=60, hat='bun')                      # 跳起來的小孩
    p.inscription('與民同醉', 1450, 380, 50)
    p.save('P2_Feast.png')


# ---------------------------------------------------------------- 其三　百足現世

def centipede(p, spine, w_head, w_tail):
    path = catmull(spine, 1.5)
    nrm = normals(path)
    n = len(path)
    step = 26
    idx = list(range(n - 1, 0, -step))   # 從尾巴畫到頭，頭蓋在最上面
    for i in idx:
        c, (tg, nm) = path[i], nrm[i]
        t = i / (n - 1)
        wdt = w_head + (w_tail - w_head) * t
        # 兩邊的腳：往外再往地面彎，前端有鉤
        for side in (-1, 1):
            root = c + nm * wdt * 0.42 * side
            # 不管身體怎麼彎，腳都往下撐向地面（往外一點、往下一節、前端往前勾）
            out = nm * side
            knee = root + out * wdt * 0.35 + np.array([0, wdt * 0.18])
            foot = knee + np.array([0, wdt * 0.62]) + out * wdt * 0.12
            hook = foot + tg * wdt * 0.18 * side
            p.line([root, knee], wdt * 0.13, INK, 0.95, taper=0)
            p.line([knee, foot, hook], wdt * 0.1, INK, 0.95, taper=0.25)
            p.line([root, knee], wdt * 0.06, BONE, 0.9, taper=0)
            p.line([knee, foot], wdt * 0.045, BONE, 0.85, taper=0.25)
        # 骨甲：一節一節的白骨，深色縫隙
        ang = math.atan2(tg[1], tg[0])
        m, d = p.mask()
        rx, ry = step * 0.85, wdt * 0.55
        poly = []
        for k in range(24):
            a = k / 24 * math.tau
            lx, ly = math.cos(a) * rx, math.sin(a) * ry
            poly.append(((c[0] + lx * math.cos(ang) - ly * math.sin(ang)) * SS, (c[1] + lx * math.sin(ang) + ly * math.cos(ang)) * SS))
        d.polygon(poly, fill=255)
        p.put(m, INK, 0.95)
        m, d = p.mask()
        poly2 = [((x / SS - c[0]) * 0.82 + c[0]) * SS for x, _ in poly]
        poly2 = [(poly2[k], ((poly[k][1] / SS - c[1]) * 0.78 + c[1]) * SS) for k in range(len(poly))]
        d.polygon(poly2, fill=255)
        p.put(m, BONE, 0.95, dry=0.35)
        # 骨甲上的脊和裂紋
        p.line([c - nm * wdt * 0.35, c + nm * wdt * 0.35], 2.0, INK, 0.7, taper=0)
        p.line([c + tg * 4 - nm * wdt * 0.1, c + tg * 9 + nm * wdt * 0.2], 1.3, INK, 0.5, taper=0)
    # 頭
    head = path[0]
    tg, nm = nrm[0]
    fwd = -tg
    s = w_head
    def H(a, b):
        q = head + fwd * a * s + nm * b * s
        return (q[0], q[1])
    p.poly([H(-0.3, -0.62), H(0.45, -0.55), H(0.75, 0.0), H(0.45, 0.55), H(-0.3, 0.62)], INK, 0.95)
    p.poly([H(-0.2, -0.48), H(0.35, -0.42), H(0.6, 0.0), H(0.35, 0.42), H(-0.2, 0.48)], BONE, 0.95, dry=0.3)
    for b in (-0.25, 0.25):                                                   # 發紅光的眼睛
        ex, ey = H(0.35, b)
        p.ellipse(ex, ey, s * 0.22, s * 0.22, (0.95, 0.25, 0.1), 0.35, blur=s * 0.1)
        p.ellipse(ex, ey, s * 0.07, s * 0.07, (0.9, 0.12, 0.06), 0.98)
    for b in (-1, 1):                                                         # 鐮刀狀的大顎
        p.line([H(0.55, b * 0.35), H(1.0, b * 0.8), H(1.45, b * 0.72), H(1.65, b * 0.38)], s * 0.16, INK, 0.97, taper=0.35)
        p.line([H(0.6, b * 0.37), H(1.0, b * 0.72), H(1.4, b * 0.66)], s * 0.06, BONE, 0.9, taper=0.4)
    for b in (-1, 1):                                                         # 觸角
        p.line([H(0.6, b * 0.2), H(1.2, b * 0.9), H(1.5, b * 1.8), H(1.4, b * 2.6)], s * 0.05, INK, 0.9, taper=0.5)


def paint_centipede():
    p = Painting(1536, 1024, 303)
    # 黑夜 + 地平線上的紅光
    p.gradient(0, 820, 0.78, 0.35, INK, noise=0.45, cell=70)
    p.wash([(0, 640), (1536, 600), (1536, 760), (0, 790)], (0.62, 0.14, 0.08), 0.28, 40)
    # 遠處被撞碎的山
    p.wash([(0, 700), (150, 560), (300, 640), (470, 540), (620, 650), (800, 700), (800, 760), (0, 760)], INK, 0.55, 6)
    # 地面與裂縫
    p.wash([(0, 780), (1536, 740), (1536, 1024), (0, 1024)], (0.38, 0.33, 0.27), 0.65, 10)
    for k in range(9):
        x0 = 1250 + p.rng.uniform(-60, 60)
        pts = [(x0, 860)]
        for j in range(5):
            last = pts[-1]
            pts.append((last[0] - p.rng.uniform(40, 110), last[1] + p.rng.uniform(-25, 40)))
        p.line(pts, 3.5, INK, 0.85, taper=0.2)
    p.ellipse(1320, 870, 170, 45, INK, 0.95, blur=4)                         # 蟲鑽出來的地洞
    # 百足：從地洞鑽出，弓起身體，頭往下壓向道士
    spine = [(620, 470), (760, 330), (930, 260), (1110, 300), (1250, 420), (1320, 580), (1330, 740), (1320, 880)]
    centipede(p, spine, 100, 70)
    # 飛散的土石
    for _ in range(18):
        x, y = 1320 + p.rng.uniform(-260, 260), 820 + p.rng.uniform(-260, 30)
        r = p.rng.uniform(6, 20)
        p.poly([(x - r, y), (x, y - r * 0.8), (x + r * 0.9, y - r * 0.1), (x + r * 0.2, y + r * 0.7)], INK, 0.85)
    p.splatter(1320, 820, 240, 120, 3, INK, 0.8)
    # 倒在地上的道士、斷劍、血跡
    p.ellipse(420, 905, 180, 26, INK, 0.5, blur=8)
    figure(p, 280, 905, 240, pose='stand', robe=(0.6, 0.66, 0.7), robe_alpha=0.75, angle=-88, arm_r=100, item_r='brokensword', arm_l=40, hat='knot')
    p.line([(610, 930), (700, 905)], 3.2, (0.55, 0.57, 0.6), 0.95, taper=0.05)   # 斷掉的劍身
    p.splatter(450, 900, 90, 60, 4, BLOOD, 0.9)
    p.splatter(560, 880, 50, 25, 3, BLOOD, 0.85)
    p.inscription('百足現世', 90, 70, 52, color=(0.88, 0.84, 0.74))
    p.save('P3_Centipede.png')


# ---------------------------------------------------------------- 其四　黑衣奪骸

def paint_black_robes():
    p = Painting(1536, 1024, 404)
    moon_xy = (330, 170)
    night_sky(p, 900, 0.55, moon_xy, 70)
    moon(p, *moon_xy, 70, glow_alpha=0.18)
    for y, x0, x1 in ((150, 160, 520), (195, 240, 640)):                       # 遮住月亮的雲
        p.line([(x0, y), ((x0 + x1) / 2, y - 12), (x1, y + 4)], 16, INK_GREY, 0.75, taper=0.3)
    # 遠方山頂上的道場（淡淡的屋頂剪影）和鳥居
    p.wash([(860, 600), (1100, 470), (1300, 430), (1536, 470), (1536, 640), (860, 640)], INK_GREY, 0.35, 14)
    roof = [(1150, 455), (1210, 420), (1330, 412), (1390, 448), (1360, 446), (1335, 432), (1205, 438), (1180, 458)]
    p.poly(roof, INK, 0.55, blur=1.5)
    p.poly([(1215, 440), (1325, 434), (1320, 470), (1220, 474)], INK, 0.35, blur=1.5)
    for x in (1040, 1110):
        p.line([(x, 640), (x + 4, 520)], 7, RED, 0.55, taper=0)
    p.line([(1020, 525), (1075, 515), (1130, 525)], 8, RED, 0.6, taper=0)
    p.line([(1030, 545), (1122, 545)], 5, RED, 0.55, taper=0)
    # 霧
    for y in (520, 610, 690):
        p.wash([(0, y), (1536, y - 30), (1536, y + 40), (0, y + 60)], PAPER_TONE, 0.35, 30)
    # 左邊的竹林
    for x, a, w in ((40, 0.45, 9), (120, 0.55, 11), (215, 0.9, 14), (290, 0.6, 10), (365, 0.95, 16), (450, 0.5, 9)):
        bamboo(p, x, 1024, 1024, w, a, lean=p.rng.uniform(-0.02, 0.02))
    # 小路
    p.wash([(380, 1024), (560, 880), (860, 760), (1100, 700), (1180, 700), (980, 790), (760, 900), (680, 1024)], (0.42, 0.37, 0.3), 0.45, 8)
    for k in range(16):
        x = 580 + k * 34
        y = 900 - k * 12
        p.ellipse(x + (8 if k % 2 else -8), y, 5, 3, INK, 0.6)
    # 黑衣人：前面一個提燈，中間兩個抬著道士，後面一個拿槍回頭看，最後一個跟著
    figure(p, 1080, 760, 210, black=True, arm_l=160, arm_r=120, item_r='lantern', flip=1, wind=0.3)
    carriers = ((930, 805), (720, 865))
    for x, y in carriers:
        figure(p, x, y, 220, black=True, arm_l=110, arm_r=115, flip=1, wind=0.2)
    # 擔架和上面的遺體
    a, b = (carriers[1][0] + 25, carriers[1][1] - 150), (carriers[0][0] - 10, carriers[0][1] - 152)
    p.line([(a[0] - 40, a[1] + 6), (b[0] + 40, b[1] - 6)], 7, (0.42, 0.3, 0.2), 0.95, taper=0)
    mid = ((a[0] + b[0]) / 2, (a[1] + b[1]) / 2 - 8)
    figure(p, mid[0] - 105, mid[1], 210, robe=(0.84, 0.82, 0.76), robe_alpha=0.95, angle=-84, arm_l=10, arm_r=10, hat='knot')
    p.line([(mid[0] - 30, mid[1] + 18), (mid[0] - 10, mid[1] + 80)], 2.5, (0.84, 0.82, 0.76), 0.9)   # 垂下來的袖子
    figure(p, 560, 925, 230, black=True, arm_l=180, item_l='spear', arm_r=120, flip=-1, wind=0.25)
    figure(p, 470, 975, 200, black=True, arm_l=130, arm_r=140, flip=1, wind=0.25)
    # 烏鴉
    for x, y, s in ((780, 230, 1.0), (860, 200, 0.8), (930, 260, 0.7), (1020, 180, 0.9)):
        p.line([(x - 18 * s, y - 6 * s), (x - 6 * s, y + 2 * s), (x, y), (x + 6 * s, y + 2 * s), (x + 20 * s, y - 8 * s)], 3.5 * s, INK, 0.9, taper=0.2)
    p.inscription('黑衣奪骸', 1460, 70, 50)
    p.save('P4_BlackRobes.png')


# ---------------------------------------------------------------- 詩

POEM_TITLE = '弔玄清'
POEM = [
    '海上斬龍劍未寒',
    '醉歌村裡夜方闌',
    '百足破土穿雲至',
    '一夜青衫血染殘',
    '黑衣攜骨歸何處',
    '道場深鎖萬重關',
    '誰開長者後園門',
    '為取先生遺骨還',
]


def paint_poem():
    p = Painting(1400, 1000, 505)
    size = 70
    font = ImageFont.truetype(FONT, int(size * SS))
    small = ImageFont.truetype(FONT, int(44 * SS))
    m, d = p.mask()
    x = 1250
    for i, ch in enumerate(POEM_TITLE):                       # 題目，右起直書
        d.text((x * SS, (120 + i * size * 1.15) * SS), ch, font=font, fill=255, anchor='mt')
    x -= 130
    for line in POEM:
        for i, ch in enumerate(line):
            d.text((x * SS, (150 + i * size * 1.15) * SS), ch, font=font, fill=255, anchor='mt')
        x -= 112
    for i, ch in enumerate('村人記'):                         # 落款
        d.text(((x + 20) * SS, (540 + i * 50) * SS), ch, font=small, fill=255, anchor='mt')
    p.put(m, INK, 0.9, dry=0.2)
    p.seal('無名', x + 20, 700, 62)
    p.save('Poem.png')


# ---------------------------------------------------------------- 後院大門的墨色漩渦

def paint_gate_vortex(size=1024, seed=606):
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    u, v = (xx / size - 0.5) * 2, (yy / size - 0.5) * 2
    r = np.sqrt(u * u + v * v)
    ang = np.arctan2(v, u)
    # 往中心捲進去的旋臂：角度 + 半徑的對數，做出螺旋紋
    swirl = np.sin(ang * 3 + np.log(r + 0.03) * 7.5)
    n = smooth_noise(size, size, 24, seed) * 0.6 + smooth_noise(size, size, 6, seed + 1) * 0.4
    arms = np.clip(swirl * 0.5 + 0.5 + (n - 0.5) * 0.9, 0, 1) ** 2.2
    core = np.clip(1 - r / 0.35, 0, 1) ** 1.5
    ink = np.array((0.04, 0.035, 0.06), np.float32)
    violet = np.array((0.42, 0.32, 0.62), np.float32)
    pale = np.array((0.86, 0.82, 0.95), np.float32)
    rgb = ink[None, None, :] * (1 - arms[..., None]) + violet[None, None, :] * arms[..., None]
    rgb = rgb * (1 - core[..., None] * 0.9) + ink * core[..., None] * 0.9
    rim = np.clip(1 - np.abs(r - 0.72) / 0.12, 0, 1) * (0.5 + 0.5 * n)
    rgb = rgb * (1 - rim[..., None] * 0.35) + pale * rim[..., None] * 0.35
    # 四邊淡出：門洞是長方形，透明度用方形的距離
    edge = np.clip((1 - np.maximum(np.abs(u), np.abs(v))) / 0.25, 0, 1)
    alpha = edge * np.clip(0.55 + arms * 0.45 + core * 0.4, 0, 1)
    img = np.dstack([np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)])
    path = os.path.join(os.path.dirname(OUT), 'T_JV_DojoGateVortex.png')
    Image.fromarray((img * 255).astype(np.uint8), 'RGBA').filter(ImageFilter.GaussianBlur(2.5)).save(path, optimize=True)
    print('saved', path)


# ---------------------------------------------------------------- 使用者提供的四幅畫

SOURCE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'Paintings_Source.png')
SOURCE_ORDER = ['P1_SeaDragon.png', 'P2_Feast.png', 'P3_Centipede.png', 'P4_BlackRobes.png']   # 左上、右上、左下、右下


def wear(img, seed):
    """
    讓畫看起來破舊：邊緣撕得參差不齊（透明）、缺一角、幾個蟲蛀的小洞、水漬的黃圈、
    摺痕、整體泛黃和邊緣焦黑。回傳 RGBA。
    """
    rgb = np.asarray(img.convert('RGB'), np.float32) / 255.0
    h, w = rgb.shape[:2]
    rng = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)

    # 撕破的邊：離邊緣的距離加上兩層雜訊，小於門檻的地方透明
    edge = np.minimum(np.minimum(xx, w - 1 - xx), np.minimum(yy, h - 1 - yy))
    rough = smooth_noise(w, h, 18, seed) * 14 + smooth_noise(w, h, 5, seed + 1) * 5
    torn = edge - rough
    # 缺一角：隨機一個角被撕掉一大塊
    corner = [(0, 0), (w, 0), (0, h), (w, h)][rng.integers(4)]
    cd = np.hypot(xx - corner[0], yy - corner[1]) - smooth_noise(w, h, 10, seed + 2) * 40
    torn = np.minimum(torn, cd - rng.uniform(70, 110))
    # 蟲蛀的小洞
    for _ in range(rng.integers(3, 6)):
        hx, hy, r = rng.uniform(0.08, 0.92) * w, rng.uniform(0.08, 0.92) * h, rng.uniform(3, 9)
        hole = np.hypot(xx - hx, yy - hy) - r - smooth_noise(w, h, 4, int(hx)) * 4
        torn = np.minimum(torn, hole)
    alpha = np.clip((torn - 1.0) / 2.0, 0.0, 1.0)

    # 泛黃：整張乘上舊紙的顏色，越靠近破邊越焦黑
    yellow = np.array([1.0, 0.93, 0.78], np.float32)
    out = rgb * (0.82 + 0.18 * yellow)
    burn = np.clip(1.0 - torn / 26.0, 0.0, 1.0) ** 1.6
    out *= (1.0 - 0.65 * burn)[..., None] * np.array([1.0, 0.92, 0.82])[None, None, :] + 0.65 * burn[..., None] * np.array([0.35, 0.22, 0.12])[None, None, :]

    # 水漬：幾個淡黃褐色的圓，邊緣一圈比較深
    for _ in range(rng.integers(2, 4)):
        sx, sy, sr = rng.uniform(0.1, 0.9) * w, rng.uniform(0.1, 0.9) * h, rng.uniform(40, 120)
        d = np.hypot(xx - sx, yy - sy) / sr + (smooth_noise(w, h, 30, int(sx * 7)) - 0.5) * 0.35
        inside = np.clip(1.0 - d, 0.0, 1.0) * 0.12
        ring = np.exp(-((d - 1.0) / 0.05) ** 2) * 0.22
        stain = (inside + ring)[..., None]
        out = out * (1.0 - stain) + stain * np.array([0.55, 0.38, 0.18])[None, None, :]

    # 摺痕：一直一橫，亮一條暗一條
    for vertical in (True, False):
        pos = (w if vertical else h) * rng.uniform(0.42, 0.58)
        coord = xx if vertical else yy
        wobble = (smooth_noise(w, h, 60, seed + (3 if vertical else 4)) - 0.5) * 6
        d = coord - pos + wobble
        crease = np.exp(-(d / 1.2) ** 2) * 0.22 - np.exp(-((d - 2.2) / 1.6) ** 2) * 0.12
        out = out * (1.0 - crease[..., None] * 0.6) + crease[..., None] * 0.35

    # 斑駁的褪色和霉點
    fade = smooth_noise(w, h, 40, seed + 5)
    out = out * (0.9 + 0.1 * fade[..., None])
    spots = (smooth_noise(w, h, 3, seed + 6) > 0.93).astype(np.float32) * (smooth_noise(w, h, 50, seed + 7) > 0.55)
    out = out * (1.0 - spots[..., None] * 0.25)

    out = np.clip(out, 0.0, 1.0)
    rgba = np.dstack([out, alpha]).astype(np.float32)
    return Image.fromarray((rgba * 255 + 0.5).astype(np.uint8), 'RGBA')


def split_source(seam=2):
    """把 2 × 2 的原圖切成四張（中間的接縫線各往內讓 seam 像素），再做舊（wear）"""
    img = Image.open(SOURCE).convert('RGB')
    w, h = img.size
    cx, cy = w // 2, h // 2
    boxes = [(0, 0, cx - seam, cy - seam), (cx + seam, 0, w, cy - seam),
             (0, cy + seam, cx - seam, h), (cx + seam, cy + seam, w, h)]
    os.makedirs(OUT, exist_ok=True)
    for i, (name, box) in enumerate(zip(SOURCE_ORDER, boxes)):
        path = os.path.join(OUT, name)
        worn = wear(img.crop(box), 400 + i * 17)
        worn.save(path, optimize=True)
        print('saved', path, worn.size)


if __name__ == '__main__':
    import sys
    paint_gate_vortex()
    if '--procedural' in sys.argv:
        paint_sea_dragon()
        paint_feast()
        paint_centipede()
        paint_black_robes()
    else:
        split_source()
    paint_poem()
