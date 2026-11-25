# 合成音效：武士刀揮砍（風切 + 刀身的細哨音「咻—」）、刀砍到石頭（金屬鏘聲 + 碎石）、砍到木頭（悶響 + 木頭共鳴）
# 用法：python -P synth_blade_sfx.py ../../Assets/_Project/Resources/Audio/SFX/Gen
# 輸出：katana_swing_1~3、blade_stone_1~2、blade_wood_1~2（.wav，44.1 kHz 單聲道）
# 遊戲裡（PlayerSfx）同一個揮砍聲用不同音高播：空揮比較高、砍進敵人身上當下把音高往下拉；長槍、小刀各自再高低一點。
import numpy as np, os, sys
from scipy.io import wavfile
from scipy import signal

SR = 44100
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)


def t_(dur): return np.arange(int(dur * SR)) / SR
def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype='band', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def lp(x, f, order=2):
    sos = signal.butter(order, f, btype='low', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def hp(x, f, order=2):
    sos = signal.butter(order, f, btype='high', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def pad(x, n): return np.pad(x, (0, max(0, n - len(x))))[:n]
def mix(*xs):
    n = max(len(x) for x in xs); return sum(pad(x, n) for x in xs)
def fade(x, a=0.002, b=0.02):
    na, nb = int(a * SR), int(b * SR); x = x.copy()
    if na: x[:na] *= np.linspace(0, 1, na)
    if nb: x[-nb:] *= np.linspace(1, 0, nb)
    return x
def save(name, x, peak=0.9):
    x = fade(x); m = np.max(np.abs(x)) or 1; x = x / m * peak
    wavfile.write(os.path.join(OUT, name + '.wav'), SR, (x * 32767).astype(np.int16)); print(name, f'{len(x)/SR:.2f}s')


def svf_band(x, freq, q):
    """時間變化的帶通（Chamberlin state-variable filter）：freq、q 是跟 x 一樣長的陣列"""
    y = np.zeros_like(x)
    low = band = 0.0
    f = 2 * np.sin(np.pi * np.clip(freq, 20, SR * 0.22) / SR)
    damp = 1.0 / np.maximum(q, 0.5)
    for i in range(len(x)):
        low += f[i] * band
        high = x[i] - low - damp[i] * band
        band += f[i] * high
        y[i] = band
    return y


# ---------- 武士刀揮砍：刀越揮越快再收住（速度曲線），風切的中心頻率和刀身的哨音跟著速度走 ----------
def katana_swing(seed, dur=0.46, peak_at=0.13, hiss=1.0, whistle=1.0):
    r = np.random.default_rng(900 + seed)
    t = t_(dur)
    # 速度曲線：很快衝上去、拖一點尾巴（偏斜的鐘形）
    a = np.clip(t / peak_at, 0, None)
    after = np.clip(t - peak_at, 0, None) / (0.085 + 0.02 * r.random())
    speed = np.where(t < peak_at, np.sin(0.5 * np.pi * np.clip(a, 0, 1)) ** 2, np.exp(-after ** 1.6))
    n = r.standard_normal(len(t))
    # 風切：寬一點的帶通，中心 450 → 2300 Hz
    wind = svf_band(n, 450 + 1850 * speed, 1.1 + 1.3 * speed) * speed ** 1.4
    # 刀身的哨音：很窄的帶通（像薄刀片劃過空氣的「咻—」），頻率跟著速度上下（都卜勒）
    f_wh = (1900 + 250 * r.random()) + 1500 * speed
    wh = svf_band(r.standard_normal(len(t)), f_wh, 26 + 14 * speed) * speed ** 2.2
    # 低頻的推風（很淡）
    body = lp(r.standard_normal(len(t)), 320) * speed ** 2 * 0.6
    x = hiss * hp(wind, 250) + whistle * 1.4 * wh + body
    # 起頭一點點「刷」：刀出鞘般的高頻擦聲
    x += hp(r.standard_normal(len(t)), 4000) * np.exp(-t / 0.015) * 0.06
    return x


for i, (pk, hs, wl) in enumerate([(0.12, 1.0, 1.0), (0.14, 0.9, 1.2), (0.11, 1.1, 0.85)]):
    save(f'katana_swing_{i + 1}', katana_swing(i, peak_at=pk, hiss=hs, whistle=wl), peak=0.85)


# ---------- 刀砍到石頭：清脆的金屬鏘聲（不諧和的泛音、各自衰減）+ 一下尖的撞擊 + 碎石沙沙 ----------
def blade_stone(seed):
    r = np.random.default_rng(1200 + seed)
    t = t_(0.75)
    partials = [(1180, 1.0, 0.32), (2930, 0.7, 0.24), (4370, 0.5, 0.18), (6080, 0.35, 0.12), (7710, 0.2, 0.08)]
    ring = np.zeros_like(t)
    for f, amp, dec in partials:
        f *= 1 + 0.06 * (r.random() - 0.5)
        beat = 1 + 0.004 * np.sin(2 * np.pi * (3 + 4 * r.random()) * t)   # 一點點拍頻，聽起來比較像金屬
        ring += amp * np.sin(2 * np.pi * f * beat * t + r.random() * 6) * np.exp(-t / (dec * (0.8 + 0.4 * r.random())))
    click = hp(r.standard_normal(len(t)), 2500) * np.exp(-t / 0.004) * 1.6
    grit = bp(r.standard_normal(len(t)), 700, 3200) * np.exp(-t / 0.05) * 0.9
    thud = lp(r.standard_normal(len(t)), 260) * np.exp(-t / 0.03) * 1.2
    return mix(ring * 0.8, click, grit, thud)


for i in range(2):
    save(f'blade_stone_{i + 1}', blade_stone(i), peak=0.9)


# ---------- 刀砍到木頭：「咚」的木頭共鳴（低頻泛音很快衰減）+ 劈進木纖維的沙聲 + 一點點刀身的鈴聲 ----------
def blade_wood(seed):
    r = np.random.default_rng(1500 + seed)
    t = t_(0.5)
    body = np.zeros_like(t)
    for f, amp, dec in [(210, 1.0, 0.09), (395, 0.7, 0.07), (640, 0.45, 0.05), (1040, 0.3, 0.035)]:
        f *= 1 + 0.08 * (r.random() - 0.5)
        body += amp * np.sin(2 * np.pi * f * t) * np.exp(-t / dec)
    click = bp(r.standard_normal(len(t)), 1500, 6000) * np.exp(-t / 0.005) * 1.1
    fiber = bp(r.standard_normal(len(t)), 450, 1600) * np.exp(-t / 0.035) * 0.8
    ting = np.sin(2 * np.pi * (2450 + 200 * r.random()) * t) * np.exp(-t / 0.12) * 0.16
    return mix(body * 1.1, click, fiber, ting)


for i in range(2):
    save(f'blade_wood_{i + 1}', blade_wood(i), peak=0.9)
