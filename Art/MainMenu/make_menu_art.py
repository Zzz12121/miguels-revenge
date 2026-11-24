"""
主畫面（MainMenu）的美術：水墨風格，取材自遊戲裡的古畫（祠堂的四幅畫、弔玄清的詩）。
輸出到 Assets/_Project/Resources/MainMenu/，MainMenu.cs 在執行時疊起來、加上動畫。

    python Art/MainMenu/make_menu_art.py            # 產生全部
    python Art/MainMenu/make_menu_art.py --preview  # 另外拼一張 1920x1080 的預覽圖（scratch 用，不進專案）

產生的圖：
  Paper.jpg        1920x1080 舊宣紙（米黃、纖維、四周暗下來）
  InkWash.png      主角背後的一大團潑墨（暈開的墨、深淺層次、乾筆的絲、噴濺的墨點）
  RedSun.png       朱紅色的圓（主角頭後面），邊緣是毛筆刷出來的
  Paint_*.png      三幅古畫（海上鬥龍、與民同醉、百足現世）轉成「只有墨」的圖層（紙的顏色變透明），Poem.png 一樣
  Title.png        「Miguel's Revenge」墨色標題（筆刷的毛邊）+ Subtitle.png「神使之復仇」
  TitleCut1.png    同一個標題，「Miguel」被爪子抓破（字被刮掉三道）；TitleCut2.png 再加上「Revenge」被一刀斜斜砍開（上半截沿著刀口滑開一點）
  TitleClaws.png   抓在 Miguel 上的三道朱紅爪痕；TitleSlash.png 砍在 Revenge 上的一道刀痕（細、兩頭尖，帶一點血霧）
                   這四張和 Title.png 一樣大，疊在同一個位置
  Menu_*.png       三個選項的字（標楷體）
  BrushStroke.png  選到的選項後面那一筆（朱紅色乾筆橫刷）
  TaoistInk.png    主角背後的道士水墨法相（道士拿巨筆的去背照轉成墨：衣服的深淺變成墨的濃淡，邊緣暈開，下半身散成霧）
  InkDot.png       飄的墨點（粒子）
  Hero.png         主角（舉刀過頂、正臉看鏡頭的去背照，裁掉多餘的透明邊）
"""
import os
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
from scipy import ndimage

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
OUT = os.path.join(ROOT, 'Assets', '_Project', 'Resources', 'MainMenu')
PAINT = os.path.join(ROOT, 'Assets', '_Project', 'Materials', 'JapaneseVillage', 'Paintings')
HERO = r'C:\Users\Asus\Pictures\MiguelsRevenge_海報截圖\第三批_人物照\玩家\舉刀過頂_正臉看鏡頭_去背.png'
TAOIST = r'C:\Users\Asus\Pictures\MiguelsRevenge_海報截圖\第二批_道士出招與下劈\道士出招\01b_交叉運筆墨光_臉朝右_筆指兩點鐘_去背.png'
FONTS = r'C:\Windows\Fonts'
rng = np.random.default_rng(7)


def fbm(h, w, scale, octaves=5, seed=0):
    """分形雜訊（0~1）：幾層不同大小的平滑雜訊疊起來"""
    r = np.random.default_rng(seed)
    out = np.zeros((h, w))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        s = max(1, int(scale / (2 ** o)))
        g = r.random((h // s + 3, w // s + 3))
        z = ndimage.zoom(g, s, order=3)[:h, :w]
        out += z * amp
        total += amp
        amp *= 0.5
    out /= total
    return (out - out.min()) / (out.max() - out.min() + 1e-9)


def save_rgba(arr, name):
    Image.fromarray(np.clip(arr * 255, 0, 255).astype(np.uint8), 'RGBA').save(os.path.join(OUT, name), optimize=True)


# ---------------- 宣紙 ----------------
def paper():
    H, W = 1080, 1920
    base = np.array([0.88, 0.83, 0.72])
    n1 = fbm(H, W, 256, 6, 1)
    fiber = ndimage.gaussian_filter(rng.random((H, W)), (0.6, 4.0))  # 橫向的纖維
    fiber = (fiber - fiber.mean()) / (fiber.std() + 1e-9)
    stain = fbm(H, W, 512, 4, 2)
    img = base[None, None, :] * (0.93 + 0.09 * n1[..., None]) + 0.012 * fiber[..., None]
    img -= np.clip(stain - 0.62, 0, 1)[..., None] * np.array([0.10, 0.12, 0.16]) * 0.9  # 黃褐色的舊漬
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.sqrt(((xx - W * 0.5) / (W * 0.62)) ** 2 + ((yy - H * 0.5) / (H * 0.62)) ** 2)
    vig = np.clip((d - 0.55) / 0.6, 0, 1) ** 1.6
    img = img * (1 - vig[..., None] * 0.55) + vig[..., None] * np.array([0.18, 0.12, 0.08]) * 0.55 * 0.3
    Image.fromarray(np.clip(img * 255, 0, 255).astype(np.uint8), 'RGB').save(os.path.join(OUT, 'Paper.jpg'), quality=92)


# ---------------- 潑墨 ----------------
def ink_wash():
    S = 1400
    yy, xx = np.mgrid[0:S, 0:S] / S
    alpha = np.zeros((S, S))
    # 幾團大小不同的墨，邊緣用雜訊扭曲（暈開）
    blobs = [(0.5, 0.52, 0.34, 1.0), (0.36, 0.38, 0.2, 0.85), (0.66, 0.66, 0.22, 0.8), (0.58, 0.3, 0.16, 0.7), (0.32, 0.7, 0.17, 0.75)]
    warp = fbm(S, S, 180, 5, 11)
    warp2 = fbm(S, S, 60, 4, 12)
    for i, (cx, cy, r, k) in enumerate(blobs):
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / r
        d = d + (warp - 0.5) * 0.9 + (warp2 - 0.5) * 0.25
        alpha = np.maximum(alpha, np.clip(1.1 - d, 0, 1) ** 0.8 * k)
    # 深淺：中間深、往外淡，加上墨的層次（像水墨一筆一筆暈開）
    tone = fbm(S, S, 120, 5, 13)
    alpha = alpha * (0.55 + 0.45 * tone)
    alpha = np.clip(alpha * 1.25, 0, 1)
    # 乾筆的絲：橫斜的細紋
    streak = ndimage.gaussian_filter(rng.random((S, S)), (2.5, 45))
    streak = (streak - streak.mean()) / streak.std()
    sweep = np.clip(1 - np.abs((yy - 0.35 - (xx - 0.5) * 0.45)) / 0.12, 0, 1) * np.clip((xx - 0.12) / 0.2, 0, 1) * np.clip((0.95 - xx) / 0.2, 0, 1)
    alpha = np.maximum(alpha, np.clip(streak * 0.45 + 0.1, 0, 1) * sweep * 0.75)
    # 噴濺的墨點
    for _ in range(55):
        a = rng.random() * np.pi * 2
        rr = 0.3 + rng.random() * 0.17
        cx, cy = 0.5 + np.cos(a) * rr, 0.52 + np.sin(a) * rr * 0.9
        rad = (rng.random() ** 4) * 0.009 + 0.0015
        stretch = 1 + rng.random() * 2.5   # 沿著往外的方向拉長
        dx, dy = xx - cx, yy - cy
        along = dx * np.cos(a) + dy * np.sin(a)
        across = -dx * np.sin(a) + dy * np.cos(a)
        d = np.sqrt((along / stretch) ** 2 + across ** 2)
        alpha = np.maximum(alpha, np.clip((rad - d) / 0.0015, 0, 1) * (0.55 + 0.4 * rng.random()))
    alpha = np.clip(alpha, 0, 1)
    # 邊緣暈開的那一圈比較淡、帶一點藍灰（墨加水）
    col = np.zeros((S, S, 4))
    ink = np.array([0.05, 0.05, 0.07])
    wet = np.array([0.22, 0.24, 0.28])
    t = np.clip(alpha * 1.6, 0, 1)[..., None]
    col[..., :3] = wet * (1 - t) + ink * t
    col[..., 3] = alpha
    save_rgba(col, 'InkWash.png')


def red_sun():
    S = 600
    yy, xx = np.mgrid[0:S, 0:S] / S
    d = np.sqrt((xx - 0.5) ** 2 + (yy - 0.5) ** 2)
    edge = fbm(S, S, 40, 4, 21)
    a = np.clip((0.46 + (edge - 0.5) * 0.04 - d) / 0.01, 0, 1)
    dry = ndimage.gaussian_filter(rng.random((S, S)), (3.0, 40))
    dry = (dry - dry.mean()) / dry.std()
    a *= np.clip(0.9 + dry * 0.07, 0, 1)
    col = np.zeros((S, S, 4))
    col[..., :3] = np.array([0.72, 0.13, 0.08])
    col[..., 3] = a * 0.95
    save_rgba(col, 'RedSun.png')


# ---------------- 古畫 → 墨的圖層 ----------------
def ink_layer(src, name, keep_red=True):
    im = Image.open(src).convert('RGBA')
    a = np.asarray(im).astype(float) / 255
    rgb, src_a = a[..., :3], a[..., 3]
    lum = rgb @ np.array([0.3, 0.59, 0.11])
    paper_l = np.percentile(lum[src_a > 0.5], 55) if (src_a > 0.5).any() else 0.8
    ink = np.clip((paper_l - 0.06 - lum) / (paper_l - 0.12), 0, 1) ** 1.3
    hh, ww = lum.shape
    yy, xx = np.mgrid[0:hh, 0:ww]
    d = np.sqrt(((xx - ww / 2) / (ww * 0.5)) ** 2 + ((yy - hh / 2) / (hh * 0.5)) ** 2)
    ink *= np.clip((1.0 - d) / 0.35, 0, 1)
    out = np.zeros_like(a)
    red = (rgb[..., 0] - rgb[..., 1] > 0.18) & keep_red
    out[..., :3] = np.where(red[..., None], rgb, np.array([0.06, 0.05, 0.05]))
    out[..., 3] = np.where(red, src_a, ink * src_a)
    save_rgba(out, name)


# ---------------- 字 ----------------
def rough_text(text, font_path, size, color, pad=40, rough=0.9, seed=0, spacing=0, dry_amount=0.15, pad_y=None):
    font = ImageFont.truetype(font_path, size)
    dummy = ImageDraw.Draw(Image.new('L', (10, 10)))
    bbox = dummy.textbbox((0, 0), text, font=font)
    py = pad if pad_y is None else pad_y
    w, h = bbox[2] - bbox[0] + pad * 2 + spacing * len(text), bbox[3] - bbox[1] + py * 2
    m = Image.new('L', (w, h), 0)
    d = ImageDraw.Draw(m)
    if spacing:
        x = pad - bbox[0]
        for ch in text:
            d.text((x, py - bbox[1]), ch, font=font, fill=255)
            x += d.textlength(ch, font=font) + spacing
    else:
        d.text((pad - bbox[0], py - bbox[1]), text, font=font, fill=255)
    a = np.asarray(m).astype(float) / 255
    # 筆刷的毛邊：用雜訊把邊緣咬掉一點、再加一點橫向的乾筆紋
    n = fbm(h, w, 12, 3, seed)
    a = np.clip((ndimage.gaussian_filter(a, 1.2) - 0.5 + (n - 0.5) * 0.35 * rough) * 3 + 0.5, 0, 1)
    dry = ndimage.gaussian_filter(np.random.default_rng(seed).random((h, w)), (1.5, 10))
    dry = (dry - dry.mean()) / dry.std()
    a *= np.clip(0.95 + dry * dry_amount * rough, 0, 1)
    out = np.zeros((h, w, 4))
    out[..., :3] = np.array(color)
    out[..., 3] = a
    return out


def brush_stroke():
    W, H = 900, 150
    yy, xx = np.mgrid[0:H, 0:W]
    u, v = xx / W, yy / H
    # 一筆從左往右：起筆重、收筆變細變乾
    thick = 0.36 * (1 - 0.55 * u) + 0.06 * np.sin(u * 9)
    center = 0.5 + 0.06 * np.sin(u * 3 + 0.5)
    d = np.abs(v - center) / np.maximum(thick, 0.05)
    edge = fbm(H, W, 18, 3, 31)
    a = np.clip((1 - d) * 3 + (edge - 0.5) * 1.2, 0, 1)
    bristle = ndimage.gaussian_filter(rng.random((H, W)), (2.2, 60))
    bristle = (bristle - bristle.mean()) / bristle.std()
    gaps = np.clip((bristle + 1.6 - u * 2.4) * 1.5, 0, 1)   # 越往右越乾，絲之間開始有空隙
    a *= gaps * np.clip(1.0 - u * 0.2, 0, 1)
    a *= np.clip(u / 0.04, 0, 1) * np.clip((1 - u) / 0.12, 0, 1)
    out = np.zeros((H, W, 4))
    out[..., :3] = np.array([0.66, 0.12, 0.07])
    out[..., 3] = a
    save_rgba(out, 'BrushStroke.png')


TITLE_TEXT, TITLE_SIZE, TITLE_PAD, TITLE_PAD_Y = "Miguel's Revenge", 150, 50, 110


def stroke_mask(H, W, x0, y0, x1, y1, width, bend, seed, taper=0.7):
    """一筆：從 (x0, y0) 到 (x1, y1)，中間寬 width、兩頭尖，往旁邊彎 bend，邊緣有毛邊。回傳 0~1"""
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    dx, dy = x1 - x0, y1 - y0
    L = np.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    px, py = xx - x0, yy - y0
    t = (px * ux + py * uy) / L
    tc = np.clip(t, 0, 1)
    across = -px * uy + py * ux - np.sin(tc * np.pi) * bend
    w = width * np.sin(tc * np.pi) ** taper
    d = np.abs(across) / np.maximum(w * 0.5, 0.4)
    edge = fbm(H, W, 10, 3, seed)
    return np.clip((1 - d) * 4 + (edge - 0.5) * 1.6, 0, 1) * ((t > 0) & (t < 1))


def title_set():
    """標題四件組：乾淨的、被爪子抓破的、再被刀砍開的、紅色爪痕、刀痕（全部一樣大）"""
    ink = (0.07, 0.06, 0.06)
    base = rough_text(TITLE_TEXT, os.path.join(FONTS, 'BOOKOSBI.TTF'), TITLE_SIZE, ink, pad=TITLE_PAD, pad_y=TITLE_PAD_Y,
                      seed=61, rough=0.8, dry_amount=0.06)
    H, W = base.shape[:2]
    save_rgba(base, 'Title.png')
    font = ImageFont.truetype(os.path.join(FONTS, 'BOOKOSBI.TTF'), TITLE_SIZE)
    dd = ImageDraw.Draw(Image.new('L', (10, 10)))
    bb = dd.textbbox((0, 0), TITLE_TEXT, font=font)
    ox = TITLE_PAD - bb[0]
    top, bottom = TITLE_PAD_Y, TITLE_PAD_Y + (bb[3] - bb[1])         # 字的上下緣（圖裡的 y）
    miguel = (ox + 10, ox + dd.textlength('Miguel', font=font))      # 「Miguel」的左右
    rev0 = ox + dd.textlength("Miguel's ", font=font)                 # 「Revenge」的左邊
    rev1 = ox + dd.textlength(TITLE_TEXT, font=font)

    # 爪痕：三道從右上往左下抓過 Miguel（貓爪：往下抓、微微彎），超出字的上下
    claws = np.zeros((H, W))
    core = np.zeros((H, W))
    span = miguel[1] - miguel[0]
    for i, (fx, w) in enumerate([(0.3, 26), (0.52, 30), (0.74, 24)]):
        cx = miguel[0] + span * fx
        x0, y0 = cx + 95, top - 55 + i * 6
        x1, y1 = cx - 75, bottom + 60 - i * 8
        claws = np.maximum(claws, stroke_mask(H, W, x0, y0, x1, y1, w, 10, 91 + i))
        core = np.maximum(core, stroke_mask(H, W, x0, y0, x1, y1, w * 1.9, 10, 95 + i, taper=0.5))
    # 字被刮掉的地方比紅色的爪痕寬一點（爪痕旁邊露出一點紙）
    a1 = base[..., 3] * (1 - np.clip(core * 1.2, 0, 1))
    cut1 = base.copy()
    cut1[..., 3] = a1
    save_rgba(cut1, 'TitleCut1.png')
    out = np.zeros((H, W, 4))
    out[..., :3] = np.array([0.68, 0.1, 0.06])
    out[..., 3] = claws
    save_rgba(out, 'TitleClaws.png')

    # 刀痕：一刀從左下往右上斜斜砍過 Revenge；刀口上面那半截沿著刀口往右上滑開一點
    sx0, sy0 = rev0 - 30, bottom + 48
    sx1, sy1 = rev1 + 40, top - 48
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    dx, dy = sx1 - sx0, sy1 - sy0
    L = np.hypot(dx, dy)
    ux, uy = dx / L, dy / L
    side = -(xx - sx0) * uy + (yy - sy0) * ux        # < 0 = 刀口上面
    slide = np.array([ux, uy]) * 22 + np.array([0, -5])
    a2 = a1.copy()
    shifted = ndimage.shift(a1, (slide[1], slide[0]), order=1)
    upper = side < 0
    region = (xx > rev0 - 60) & (xx < rev1 + 60)
    a2 = np.where(upper & region, shifted, a2)
    a2 *= np.clip(np.abs(side) / 5.0, 0, 1) * region + (~region)   # 刀口本身 5 px 是空的
    cut2 = base.copy()
    cut2[..., 3] = a2
    save_rgba(cut2, 'TitleCut2.png')
    line = stroke_mask(H, W, sx0, sy0, sx1, sy1, 13, 0, 99, taper=0.6)
    mist = np.clip(ndimage.gaussian_filter(line, 6) * 1.4, 0, 1) * fbm(H, W, 20, 3, 98) * 0.45
    out = np.zeros((H, W, 4))
    out[..., :3] = np.array([0.55, 0.06, 0.04])
    out[..., 3] = np.clip(np.maximum(line, mist), 0, 1)
    save_rgba(out, 'TitleSlash.png')


def claw_marks():
    """三道往右下抓的爪痕：中間粗、兩頭尖、微微彎，邊緣是毛筆的毛邊，尾端甩出幾滴"""
    W, H = 420, 380
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    a = np.zeros((H, W))
    edge = fbm(H, W, 14, 3, 71)
    for i, (x0, y0, x1, y1, width) in enumerate([(70, 40, 250, 330, 30), (150, 25, 320, 320, 34), (235, 35, 385, 300, 26)]):
        # 沿著一條微彎的線：t = 0（起爪）→ 1（收尾），寬度 sin(πt)^0.7
        dx, dy = x1 - x0, y1 - y0
        L = np.hypot(dx, dy)
        ux, uy = dx / L, dy / L
        px, py = xx - x0, yy - y0
        t = (px * ux + py * uy) / L
        bend = np.sin(np.clip(t, 0, 1) * np.pi) * 18           # 往外彎
        across = -px * uy + py * ux - bend
        w = width * np.clip(np.sin(np.clip(t, 0, 1) * np.pi), 0, 1) ** 0.7 * (1 - 0.25 * t)
        d = np.abs(across) / np.maximum(w * 0.5, 0.5)
        stroke = np.clip((1 - d) * 4 + (edge - 0.5) * 2.2, 0, 1) * ((t > 0) & (t < 1))
        a = np.maximum(a, stroke)
    dry = ndimage.gaussian_filter(rng.random((H, W)), (6, 6))
    dry = (dry - dry.mean()) / dry.std()
    a *= np.clip(0.92 + dry * 0.08, 0, 1)
    for _ in range(9):  # 甩出去的血 / 墨點
        cx, cy, r = 260 + rng.random() * 140, 270 + rng.random() * 90, 2 + rng.random() ** 2 * 7
        a = np.maximum(a, np.clip((r - np.hypot(xx - cx, yy - cy)) / 1.5, 0, 1) * 0.9)
    out = np.zeros((H, W, 4))
    out[..., :3] = np.array([0.66, 0.1, 0.06])
    out[..., 3] = a
    save_rgba(out, 'ClawMarks.png')


def taoist_ink():
    """道士的去背照 → 水墨法相：亮的地方（白袍）是淡墨、暗的地方是濃墨，輪廓暈開、往下散成霧"""
    im = Image.open(TAOIST).convert('RGBA')
    im = im.crop(im.getbbox())
    im.thumbnail((1600, 1600), Image.LANCZOS)
    a = np.asarray(im).astype(float) / 255
    rgb, sil = a[..., :3], a[..., 3]
    H, W = sil.shape
    lum = rgb @ np.array([0.3, 0.59, 0.11])
    # 局部對比：細節（衣褶、潑墨的污點、筆毛）留下來
    local = lum - ndimage.gaussian_filter(lum, 12) * 0.6
    density = np.clip(0.42 + (0.55 - local) * 1.1, 0.18, 1.0)
    # 輪廓暈開：外面一圈淡淡的、被雜訊吃掉的毛邊
    soft = ndimage.gaussian_filter(sil, 7)
    n = fbm(H, W, 40, 4, 81)
    halo = np.clip((soft - 0.15 + (n - 0.5) * 0.5) * 1.6, 0, 1)
    inner = np.clip((ndimage.gaussian_filter(sil, 1.5) - 0.5 + (n - 0.5) * 0.35) * 3 + 0.5, 0, 1)
    alpha = np.maximum(inner * density, halo * 0.22)
    # 下半身散成霧：越往下越淡、越被雜訊吃掉
    yy = np.mgrid[0:H, 0:W][0] / H
    fog = np.clip((yy - 0.48) / 0.45, 0, 1)
    mist = fbm(H, W, 90, 5, 82)
    alpha *= np.clip(1 - fog * (0.7 + mist * 0.8), 0, 1)
    # 乾筆的絲（直的，像墨從上往下流）
    streak = ndimage.gaussian_filter(rng.random((H, W)), (40, 5))
    streak = (streak - streak.mean()) / streak.std()
    alpha *= np.clip(0.95 + streak * 0.05, 0, 1)
    out = np.zeros((H, W, 4))
    out[..., :3] = np.array([0.06, 0.07, 0.09])
    out[..., 3] = np.clip(alpha, 0, 1)
    save_rgba(out, 'TaoistInk.png')


def seal():
    S = 220
    m = Image.new('L', (S, S), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((10, 10, S - 10, S - 10), radius=14, fill=255)
    font = ImageFont.truetype(os.path.join(FONTS, 'kaiu.ttf'), 82)
    # 直寫兩個字（白字 = 刻掉）
    for i, ch in enumerate('復仇'):
        d.text((S / 2, 62 + i * 92), ch, font=font, fill=0, anchor='mm')
    a = np.asarray(m).astype(float) / 255
    n = fbm(S, S, 10, 3, 41)
    a = np.clip((a - 0.5 + (n - 0.5) * 0.5) * 4 + 0.5, 0, 1) * np.clip(0.8 + fbm(S, S, 30, 3, 42) * 0.3, 0, 1)
    out = np.zeros((S, S, 4))
    out[..., :3] = np.array([0.72, 0.12, 0.08])
    out[..., 3] = a
    save_rgba(out, 'Seal.png')


def ink_dot():
    S = 64
    yy, xx = np.mgrid[0:S, 0:S] / S
    d = np.sqrt((xx - 0.5) ** 2 + (yy - 0.5) ** 2) / 0.5
    n = fbm(S, S, 8, 3, 51)
    a = np.clip((1 - d - (n - 0.5) * 0.5) * 2.2, 0, 1)
    out = np.zeros((S, S, 4))
    out[..., :3] = 0.06
    out[..., 3] = a
    save_rgba(out, 'InkDot.png')


def hero():
    im = Image.open(HERO).convert('RGBA')
    im = im.crop(im.getbbox())
    im.save(os.path.join(OUT, 'Hero.png'), optimize=True)


def text_assets():
    ink = (0.07, 0.06, 0.06)
    title_set()
    save_rgba(rough_text('神使之復仇', os.path.join(FONTS, 'kaiu.ttf'), 56, (0.55, 0.11, 0.07), seed=62, rough=0.6, spacing=22), 'Subtitle.png')
    for name, text, sp in [('Menu_Start', '開始遊戲', 10), ('Menu_Options', '選項', 88), ('Menu_Quit', '離開遊戲', 10)]:
        save_rgba(rough_text(text, os.path.join(FONTS, 'kaiu.ttf'), 78, ink, seed=sum(map(ord, name)) % 1000, rough=0.55, spacing=sp), name + '.png')


def preview(path):
    """把圖層照 MainMenu.cs 的擺法拼起來（不含動畫），看整體構圖"""
    W, H = 1920, 1080
    canvas = Image.open(os.path.join(OUT, 'Paper.jpg')).convert('RGBA')

    def put(name, cx, cy, height, alpha=1.0, width=None):
        im = Image.open(os.path.join(OUT, name)).convert('RGBA')
        w = width or int(im.width * height / im.height)
        h = height if width is None else int(im.height * width / im.width)
        im = im.resize((w, h), Image.LANCZOS)
        if alpha < 1:
            a = np.asarray(im).astype(float)
            a[..., 3] *= alpha
            im = Image.fromarray(a.astype(np.uint8), 'RGBA')
        canvas.alpha_composite(im, (int(cx - w / 2), int(cy - h / 2)))

    for name, cx, cy, h, al in LAYOUT_PAINTINGS:
        put(name, cx, cy, h, al)
    put('InkWash.png', 1390, 560, 1250, 0.92)
    put('RedSun.png', 1420, 300, 430, 0.95)
    put('TaoistInk.png', *TAOIST_LAYOUT)
    put('Hero.png', 1400, 640, 1250)
    th = TITLE_DISPLAY_HEIGHT
    put('TitleCut2.png', 540, 250, th)
    put('TitleClaws.png', 540, 250, th)
    put('TitleSlash.png', 540, 250, th)
    put('Subtitle.png', 470, 355, 64)
    put('BrushStroke.png', 380, 560, 105, 0.9, width=560)
    for i, n in enumerate(['Menu_Start', 'Menu_Options', 'Menu_Quit']):
        put(n + '.png', 330, 560 + i * 120, 92)
    canvas.convert('RGB').save(path, quality=90)


# 標題四件組顯示的高度（MainMenu.cs 用同樣的數字）：字跟以前一樣大（以前 221 px 高、上下各留 40 的圖顯示成 165）
TITLE_DISPLAY_HEIGHT = round(165 / 221 * (141 + 2 * 110))

# 道士法相（MainMenu.cs 用同樣的數字）：中心 x、中心 y、高、透明度
TAOIST_LAYOUT = (1560, 690, 1350, 0.7)

# 古畫在背景的位置（MainMenu.cs 用同樣的數字）：名稱、中心 x、中心 y（1920x1080，左上原點）、高、透明度
LAYOUT_PAINTINGS = [
    ('Paint_SeaDragon.png', 1640, 190, 460, 0.42),
    ('Paint_Centipede.png', 640, 900, 480, 0.24),
    ('Paint_Poem.png', 190, 470, 820, 0.06),
    ('Paint_Feast.png', 1080, 960, 300, 0.18),
]

if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    if '--preview-only' not in sys.argv:
        paper()
        ink_wash()
        red_sun()
        for src, name in [('P1_SeaDragon', 'Paint_SeaDragon'), ('P2_Feast', 'Paint_Feast'), ('P3_Centipede', 'Paint_Centipede'),
                          ('Poem', 'Paint_Poem')]:
            ink_layer(os.path.join(PAINT, src + '.png'), name + '.png')
        text_assets()
        brush_stroke()
        taoist_ink()
        ink_dot()
        hero()
    if '--preview' in sys.argv or '--preview-only' in sys.argv:
        preview(sys.argv[-1] if sys.argv[-1].endswith('.jpg') else os.path.join(OUT, '..', 'menu_preview.jpg'))
    print('ok')
