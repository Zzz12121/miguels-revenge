"""
選項（設定）畫面的美術，跟主畫面同一套風格。輸出到 Assets/_Project/Resources/MainMenu/，ControlsSettingsMenu.cs 執行時套上去。

    python Art/MainMenu/make_settings_art.py              # 產生
    python Art/MainMenu/make_settings_art.py --preview    # 另外拼一張預覽圖（scratch 用，不進專案）

產生的圖：
  Settings_Parchment.png  整個設定畫面底下那一大張皮紙：黃褐色、皮革一樣的斑駁和細紋，四邊撕得破破爛爛、有脫線，
                          邊上有幾道裂口、紙上破了幾個小洞（洞口焦黑），底下帶一點影子
  Settings_Cloth.png      每個按鈕的那塊破布（九宮格切：四邊的毛邊不會被拉長）：麻布的織紋、邊緣破損、兩端脫線
  Pause_Parchment.png     遊戲中 Esc 暫停選單（和 Game Over / 通關畫面）底下那張直的皮紙（九宮格切，高度跟著按鈕數量變）
  Loading_Cloth.png       「開始遊戲」載入畫面的進度條：一長條破布（底是原色，進度的部分染成朱紅）
  ../UI/Move_SpearCharge.png、Move_SpearPlunge.png  長槍兩招（破陣突刺、飛身貫地）的冷卻圖示（跟跳劈、盾滑的圖示同一種金色圓形）
"""
import os
import sys
import numpy as np
from PIL import Image
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from make_menu_art import fbm, save_rgba, OUT  # noqa: E402

# ControlsSettingsMenu.cs 用同樣的數字：皮紙在畫面上的大小、布的九宮格邊
PARCHMENT_SIZE = (1720, 990)
CLOTH_BORDER = (56, 40, 56, 40)   # 左、下、右、上（px）


def ragged_mask(H, W, inset, seed, rough=1.0, tears=0, holes=0, threads=0):
    """長方形往內縮 inset 的範圍，邊緣撕得不規則；tears 道往內的裂口、holes 個小洞、threads 條往外的脫線。回傳 (alpha, 離邊緣多近 0~1)"""
    r = np.random.default_rng(seed)
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    # 到長方形邊的距離（裡面是正的）
    d = np.minimum.reduce([xx - inset, W - 1 - inset - xx, yy - inset, H - 1 - inset - yy])
    big = (fbm(H, W, max(8, min(H, W) // 6), 4, seed) - 0.5) * inset * 1.3 * rough
    small = (fbm(H, W, 6, 3, seed + 1) - 0.5) * inset * 0.5 * rough
    d = d + big + small
    d_edge = d.copy()
    rim = np.zeros((H, W))
    # 裂口：從邊上往內撕的 V 字
    for _ in range(tears):
        side = r.integers(4)
        depth = r.uniform(0.6, 1.6) * inset + r.uniform(10, 40)
        half = r.uniform(6, 16)
        if side in (0, 1):   # 上 / 下
            x0 = r.uniform(0.12, 0.88) * W
            edge_y = inset if side == 0 else H - 1 - inset
            into = (yy - edge_y) if side == 0 else (edge_y - yy)
            lean = r.uniform(-0.4, 0.4)
            across = np.abs(xx - x0 - into * lean)
            width = half * np.clip(1 - into / depth, 0, 1)
            cut = (into > -inset) & (into < depth) & (across < width)
        else:                # 左 / 右
            y0 = r.uniform(0.15, 0.85) * H
            edge_x = inset if side == 2 else W - 1 - inset
            into = (xx - edge_x) if side == 2 else (edge_x - xx)
            lean = r.uniform(-0.4, 0.4)
            across = np.abs(yy - y0 - into * lean)
            width = half * np.clip(1 - into / depth, 0, 1)
            cut = (into > -inset) & (into < depth) & (across < width)
        d = np.where(cut, np.minimum(d, -2), d)
    # 小洞（洞口不規則）
    for _ in range(holes):
        cx, cy = r.uniform(0.08, 0.92) * W, r.uniform(0.1, 0.9) * H
        rad = r.uniform(5, 13)
        warp = (fbm(H, W, 5, 2, int(r.integers(1000))) - 0.5) * rad * 1.2
        dh = np.hypot(xx - cx, yy - cy) - rad + warp
        d = np.minimum(d, dh)
        rim = np.maximum(rim, np.clip(1 - dh / 7, 0, 1) * (dh > -1))   # 洞口焦黑的一圈
    alpha = np.clip(d / 1.5 + 0.5, 0, 1)
    # 脫線：從邊上往外拖的細線
    thread = np.zeros((H, W))
    for _ in range(threads):
        side = r.integers(4)
        L = r.uniform(0.25, 0.6) * inset
        if side == 0:   x0, y0, dx, dy = r.uniform(0.05, 0.95) * W, inset + 4, r.uniform(-0.4, 0.4), -1
        elif side == 1: x0, y0, dx, dy = r.uniform(0.05, 0.95) * W, H - 1 - inset - 4, r.uniform(-0.4, 0.4), 1
        elif side == 2: x0, y0, dx, dy = inset + 4, r.uniform(0.05, 0.95) * H, -1, r.uniform(-0.4, 0.4)
        else:           x0, y0, dx, dy = W - 1 - inset - 4, r.uniform(0.05, 0.95) * H, 1, r.uniform(-0.4, 0.4)
        n = int(L * 2)
        bend = r.uniform(-0.03, 0.03)
        x, y = x0, y0
        for k in range(n):
            ix, iy = int(round(x)), int(round(y))
            if 0 <= ix < W and 0 <= iy < H:
                thread[iy, ix] = max(thread[iy, ix], 0.85 * (1 - k / n) ** 0.5)
            x += dx * 0.5
            y += dy * 0.5
            dx, dy = dx - dy * bend, dy + dx * bend
    thread = np.clip(ndimage.gaussian_filter(thread, 0.6) * 2.2, 0, 1)
    alpha = np.maximum(alpha, thread)
    # 離邊緣多近（給邊緣變髒、變暗用）
    near = np.maximum(np.clip(1 - d_edge / (inset * 1.4), 0, 1), rim)
    return alpha, near, thread


def parchment():
    W, H = PARCHMENT_SIZE
    pad = 40   # 外圍留給影子
    TW, TH = W + pad * 2, H + pad * 2
    inset = pad + 26
    alpha, near, thread = ragged_mask(TH, TW, inset, 301, rough=1.0, tears=6, holes=4, threads=60)
    base = np.array([0.79, 0.67, 0.48])
    mottle = fbm(TH, TW, 220, 6, 302)
    patch = fbm(TH, TW, 90, 4, 303)
    # 皮革的細紋：細的雜訊取「接近 0.5 的地方」當成皺紋
    crease_n = fbm(TH, TW, 160, 3, 304)
    crease = np.clip(1 - np.abs(crease_n - 0.5) * 30, 0, 1) ** 2
    grain = fbm(TH, TW, 4, 2, 307)
    fiber = ndimage.gaussian_filter(np.random.default_rng(305).random((TH, TW)), (0.7, 3.5))
    fiber = (fiber - fiber.mean()) / (fiber.std() + 1e-9)
    img = base[None, None] * (0.86 + 0.2 * mottle[..., None])
    img -= np.clip(patch - 0.6, 0, 1)[..., None] * np.array([0.22, 0.2, 0.16])          # 深色的舊斑
    img -= crease[..., None] * np.array([0.05, 0.045, 0.035])                            # 幾道摺痕
    img *= (0.965 + 0.07 * grain[..., None])                                             # 皮革的顆粒
    img += 0.012 * fiber[..., None]
    stain = fbm(TH, TW, 400, 3, 306)
    img -= np.clip(stain - 0.66, 0, 1)[..., None] * np.array([0.25, 0.3, 0.35])          # 水漬（偏褐）
    # 邊緣：越靠近破口越暗、越髒（燒焦 / 磨損）
    img *= (1 - near[..., None] ** 1.6 * 0.55)
    img = np.where(thread[..., None] > 0.05, img * 0.9, img)
    # 影子：皮紙的形狀模糊、往右下偏
    sh = ndimage.shift(ndimage.gaussian_filter(alpha, 9), (10, 8), order=1) * 0.5
    out = np.zeros((TH, TW, 4))
    a = alpha + sh * (1 - alpha)
    out[..., :3] = img * (alpha / np.maximum(a, 1e-4))[..., None]
    out[..., 3] = a
    save_rgba(out, 'Settings_Parchment.png')


def cloth(W=384, H=128, name='Settings_Cloth.png', seed=401, threads=22):
    inset = 12
    alpha, near, thread = ragged_mask(H, W, inset, seed, rough=0.9, tears=0, holes=0, threads=threads)
    # 兩端（左右）再撕得多一點：九宮格切開後左右兩端不會被拉長
    yy, xx = np.mgrid[0:H, 0:W].astype(float)
    base = np.array([0.88, 0.81, 0.66])
    # 麻布的織紋：細的橫豎線
    weave = 0.5 + 0.25 * np.sin(xx * np.pi / 2.2) + 0.25 * np.sin(yy * np.pi / 2.2 + 0.7)
    slub = ndimage.gaussian_filter(np.random.default_rng(402).random((H, W)), (0.5, 6))   # 粗細不均的紗
    slub = (slub - slub.mean()) / (slub.std() + 1e-9)
    mottle = fbm(H, W, 64, 4, 403)
    img = base[None, None] * (0.9 + 0.12 * mottle[..., None]) - (weave[..., None] - 0.5) * 0.06 + 0.015 * slub[..., None]
    stain = fbm(H, W, 90, 3, 404)
    img -= np.clip(stain - 0.62, 0, 1)[..., None] * np.array([0.2, 0.24, 0.28])
    img *= (1 - near[..., None] ** 2.5 * 0.22)
    out = np.zeros((H, W, 4))
    out[..., :3] = img
    out[..., 3] = alpha
    save_rgba(out, name)


# 暫停選單的直式皮紙（九宮格的邊：PAUSE_BORDER px）
PAUSE_SIZE = (760, 1040)
PAUSE_BORDER = 150


def pause_parchment():
    W, H = PAUSE_SIZE
    pad = 30
    TW, TH = W + pad * 2, H + pad * 2
    inset = pad + 22
    # 裂口、小洞只放在四個角的範圍（九宮格中間會被拉長）
    alpha, near, thread = ragged_mask(TH, TW, inset, 501, rough=1.0, tears=0, holes=0, threads=40)
    yy, xx = np.mgrid[0:TH, 0:TW].astype(float)
    r = np.random.default_rng(502)
    for cx, cy in [(PAUSE_BORDER * 0.55, PAUSE_BORDER * 0.7), (TW - PAUSE_BORDER * 0.6, TH - PAUSE_BORDER * 0.5)]:
        rad = r.uniform(6, 10)
        dh = np.hypot(xx - cx, yy - cy) - rad + (fbm(TH, TW, 5, 2, int(r.integers(999))) - 0.5) * rad
        alpha = np.minimum(alpha, np.clip(dh / 1.5 + 0.5, 0, 1))
        near = np.maximum(near, np.clip(1 - dh / 7, 0, 1) * (dh > -1))
    base = np.array([0.79, 0.67, 0.48])
    mottle = fbm(TH, TW, 160, 6, 503)
    patch = fbm(TH, TW, 70, 4, 504)
    grain = fbm(TH, TW, 4, 2, 505)
    img = base[None, None] * (0.86 + 0.2 * mottle[..., None])
    img -= np.clip(patch - 0.6, 0, 1)[..., None] * np.array([0.22, 0.2, 0.16])
    img *= (0.965 + 0.07 * grain[..., None])
    img *= (1 - near[..., None] ** 1.6 * 0.55)
    sh = ndimage.shift(ndimage.gaussian_filter(alpha, 8), (9, 7), order=1) * 0.5
    out = np.zeros((TH, TW, 4))
    a = alpha + sh * (1 - alpha)
    out[..., :3] = img * (alpha / np.maximum(a, 1e-4))[..., None]
    out[..., 3] = a
    save_rgba(out, 'Pause_Parchment.png')


def move_icon(name, draw):
    """招式圖示：128x128 金色圓形（跟 Move_LeapCleave / Move_ShieldSlide 同一種）：暗金底、中間亮、外圈一圈光"""
    S = 128
    yy, xx = np.mgrid[0:S, 0:S].astype(float)
    cx = cy = (S - 1) / 2
    d = np.hypot(xx - cx, yy - cy) / (S / 2)
    glow = np.clip(1 - d, 0, 1)
    swirl = fbm(S, S, 18, 3, sum(map(ord, name)))
    col = np.array([0.42, 0.24, 0.07])[None, None] * (0.7 + 0.5 * swirl[..., None])         + np.array([1.0, 0.78, 0.35])[None, None] * (glow ** 1.6)[..., None] * 0.9
    col *= (1 - np.clip(d - 0.55, 0, 1)[..., None] * 1.3)                     # 四周暗下來
    light = draw(xx, yy, S)      # 0~1：亮金色的圖案
    edge = np.clip(ndimage.grey_dilation(light, size=(5, 5)) - light, 0, 1)
    col = col * (1 - edge[..., None] * 0.75)                                   # 深色描邊
    halo = np.clip(ndimage.gaussian_filter(light, 4) * 2.2, 0, 1)
    col = col + np.array([1.0, 0.75, 0.3])[None, None] * halo[..., None] * 0.45  # 光暈
    col = col * (1 - light[..., None]) + np.array([1.0, 0.95, 0.75])[None, None] * light[..., None]
    rim = np.clip(1 - np.abs(d - 0.94) / 0.05, 0, 1)
    col = col * (1 - rim[..., None] * 0.6) + np.array([0.95, 0.7, 0.3])[None, None] * rim[..., None] * 0.6
    a = np.clip((1 - d) * S / 2 + 0.5, 0, 1)
    out = np.zeros((S, S, 4))
    out[..., :3] = np.clip(col, 0, 1)
    out[..., 3] = a
    path = os.path.join(OUT, '..', 'UI', name)
    Image.fromarray(np.clip(out * 255, 0, 255).astype(np.uint8), 'RGBA').save(path, optimize=True)


def seg(xx, yy, x0, y0, x1, y1, w):
    dx, dy = x1 - x0, y1 - y0
    L2 = dx * dx + dy * dy
    t = np.clip(((xx - x0) * dx + (yy - y0) * dy) / L2, 0, 1)
    d = np.hypot(xx - x0 - t * dx, yy - y0 - t * dy)
    return np.clip(w - d + 0.5, 0, 1), t


def tri(xx, yy, pts):
    (x0, y0), (x1, y1), (x2, y2) = pts
    def side(ax, ay, bx, by):
        return (bx - ax) * (yy - ay) - (by - ay) * (xx - ax)
    s0, s1, s2 = side(x0, y0, x1, y1), side(x1, y1, x2, y2), side(x2, y2, x0, y0)
    inside = ((s0 >= 0) & (s1 >= 0) & (s2 >= 0)) | ((s0 <= 0) & (s1 <= 0) & (s2 <= 0))
    return ndimage.gaussian_filter(inside.astype(float), 0.6)


def icon_charge(xx, yy, S):
    # 破陣突刺：一支長槍往右上衝出去，後面拖著幾道速度線，槍尖前面炸開一圈
    shaft, _ = seg(xx, yy, 18, 98, 92, 40, 3.2)
    head = tri(xx, yy, [(88, 34), (108, 22), (98, 46)])
    tassel = np.clip(1 - np.hypot(xx - 86, yy - 46) / 6, 0, 1)
    lines = np.zeros_like(xx)
    for off, L in [(-14, 40), (12, 34), (-26, 26), (24, 22)]:
        px, py = 22 + 0.62 * off, 92 + 0.79 * off          # 往槍的兩側（垂直方向）錯開
        l, t = seg(xx, yy, px - 0.79 * 10, py + 0.62 * 10, px + 0.79 * L, py - 0.62 * L, 1.6)
        lines = np.maximum(lines, l * (0.3 + 0.7 * t))
    burst = np.zeros_like(xx)
    ang = np.arctan2(yy - 22, xx - 108)
    rr = np.hypot(xx - 108, yy - 22)
    burst = np.clip(1.15 - rr / 30, 0, 1) * (0.45 + 0.55 * np.cos(ang * 7)) ** 2
    core = np.clip(1 - rr / 9, 0, 1)
    return np.clip(np.maximum.reduce([shaft, head, tassel * 0.9, lines, burst, core]), 0, 1)


def icon_plunge(xx, yy, S):
    # 飛身貫地：長槍直直往下插進地裡，地面裂開、往上噴碎石和光
    shaft, _ = seg(xx, yy, 64, 10, 64, 80, 3.4)
    head = tri(xx, yy, [(56, 78), (72, 78), (64, 100)])
    ground = np.clip(1 - np.abs(yy - 100) / 2.2, 0, 1) * (np.abs(xx - 64) < 50)
    cracks = np.zeros_like(xx)
    for ex, ey in [(24, 112), (104, 114), (40, 122), (88, 122), (64, 124)]:
        c, _ = seg(xx, yy, 64, 100, ex, ey, 1.2)
        cracks = np.maximum(cracks, c)
    ang = np.arctan2(yy - 100, xx - 64)
    rr = np.hypot(xx - 64, yy - 100)
    burst = np.clip(1.15 - rr / 40, 0, 1) * (0.45 + 0.55 * np.cos(ang * 9)) ** 2 * (yy < 103)
    core = np.clip(1 - rr / 11, 0, 1)
    return np.clip(np.maximum.reduce([shaft, head, ground * 0.8, cracks, burst, core]), 0, 1)


def preview(path):
    """擺得跟 ControlsSettingsMenu.cs 差不多（不含字），看整體"""
    canvas = Image.open(os.path.join(OUT, 'Paper.jpg')).convert('RGBA')
    dim = Image.new('RGBA', canvas.size, (13, 9, 5, 140))
    canvas.alpha_composite(dim)
    p = Image.open(os.path.join(OUT, 'Settings_Parchment.png'))
    W, H = PARCHMENT_SIZE
    p = p.resize((W + 80, H + 80), Image.LANCZOS)
    canvas.alpha_composite(p, (960 - p.width // 2, 540 - 25 - p.height // 2))
    c = Image.open(os.path.join(OUT, 'Settings_Cloth.png'))
    l, b, r, t = CLOTH_BORDER

    def sliced(w, h, tint=(1, 1, 1)):
        # 九宮格（邊縮成一半顯示：pixelsPerUnit 200）
        s = 0.5
        src = np.asarray(c).astype(float) / 255
        out = np.zeros((h, w, 4))
        xs = [0, l, c.width - r, c.width]
        ys = [0, t, c.height - b, c.height]
        dx = [0, round(l * s), w - round(r * s), w]
        dy = [0, round(t * s), h - round(b * s), h]
        for i in range(3):
            for j in range(3):
                part = Image.fromarray((src[ys[j]:ys[j + 1], xs[i]:xs[i + 1]] * 255).astype(np.uint8), 'RGBA')
                tw, th = dx[i + 1] - dx[i], dy[j + 1] - dy[j]
                if tw > 0 and th > 0:
                    out[dy[j]:dy[j + 1], dx[i]:dx[i + 1]] = np.asarray(part.resize((tw, th), Image.LANCZOS)).astype(float) / 255
        out[..., :3] *= np.array(tint)
        return Image.fromarray((np.clip(out, 0, 1) * 255).astype(np.uint8), 'RGBA')

    for x, y, w, h, tint in [(960 - 250, 540 - 345, 340, 56, (0.93, 0.55, 0.45)), (960 + 80, 540 - 345, 280, 56, (1, 1, 1)),
                             (960 + 360, 540 - 345, 240, 56, (1, 1, 1))]:
        im = sliced(w, h, tint)
        canvas.alpha_composite(im, (int(x - w / 2), int(y - h / 2)))
    for row in range(10):
        for col in (-430, 430):
            w, h = 240, 38
            im = sliced(w, h)
            canvas.alpha_composite(im, (int(960 + col + 140 - w / 2), int(540 - 135 + row * 41.7 - h / 2)))
    canvas.convert('RGB').save(path, quality=90)


if __name__ == '__main__':
    os.makedirs(OUT, exist_ok=True)
    if '--preview-only' not in sys.argv:
        parchment()
        cloth()
        pause_parchment()
        cloth(1400, 72, 'Loading_Cloth.png', 601, 50)
        move_icon('Move_SpearCharge.png', icon_charge)
        move_icon('Move_SpearPlunge.png', icon_plunge)
    if '--preview' in sys.argv or '--preview-only' in sys.argv:
        preview(sys.argv[-1] if sys.argv[-1].endswith('.jpg') else os.path.join(OUT, '..', 'settings_preview.jpg'))
    print('ok')
