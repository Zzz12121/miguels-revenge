# 合成音效：玩家打擊、揮砍、跳躍、落地；鳥叫；喝酒（咕嚕、吞嚥、哈氣）
# 用法：python -P synth_sfx.py ../../Assets/_Project/Resources/Audio/SFX/Gen
import numpy as np, os, sys
from scipy.io import wavfile
from scipy import signal

SR = 44100
OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(7)

def t_(dur): return np.arange(int(dur * SR)) / SR
def noise(n): return rng.standard_normal(n)
def bp(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype='band', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def lp(x, f, order=2):
    sos = signal.butter(order, f, btype='low', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def hp(x, f, order=2):
    sos = signal.butter(order, f, btype='high', fs=SR, output='sos'); return signal.sosfilt(sos, x)
def pad(x, n): return np.pad(x, (0, max(0, n - len(x))))[:n]
def mix(*xs):
    n = max(len(x) for x in xs); return sum(pad(x, n) for x in xs)
def fade(x, a=0.002, b=0.01):
    na, nb = int(a * SR), int(b * SR); x = x.copy()
    if na: x[:na] *= np.linspace(0, 1, na)
    if nb: x[-nb:] *= np.linspace(1, 0, nb)
    return x
def save(name, x, peak=0.9):
    x = fade(x); m = np.max(np.abs(x)) or 1; x = x / m * peak
    wavfile.write(os.path.join(OUT, name + '.wav'), SR, (x * 32767).astype(np.int16)); print(name, f'{len(x)/SR:.2f}s')
def sweep(f, dur):
    """f: array of instantaneous freq -> sine"""
    return np.sin(2 * np.pi * np.cumsum(f) / SR)

# ---------- 打中：拳肉的悶響 + 一點脆聲 ----------
def impact(heavy=False, seed=0):
    r = np.random.default_rng(100 + seed)
    d = 0.5 if heavy else 0.32
    t = t_(d)
    f0 = r.uniform(140, 180) * (0.75 if heavy else 1)
    f = f0 * np.exp(-t / 0.05) + (48 if heavy else 62)
    thump = sweep(f, d) * np.exp(-t / (0.16 if heavy else 0.09))
    body = lp(noise(len(t)), r.uniform(900, 1500)) * np.exp(-t / (0.05 if heavy else 0.035)) * 1.6
    snap = hp(noise(len(t)), 2500) * np.exp(-t / 0.006) * 0.8
    crunch = bp(noise(len(t)), 1800, 4200) * np.exp(-t / 0.02) * r.uniform(0.3, 0.6)
    x = thump * 1.2 + body + snap + crunch
    if heavy:
        rumble = lp(noise(len(t)), 180) * np.exp(-t / 0.18) * 3
        x = x + rumble
    return np.tanh(x * 1.6)

for i in range(4): save(f'hit_impact_{i+1}', impact(False, i))
save('hit_heavy_1', impact(True, 10)); save('hit_heavy_2', impact(True, 11))

# ---------- 揮砍的風聲 ----------
def whoosh(dur, f_lo, f_hi, seed, q=0.35):
    r = np.random.default_rng(200 + seed)
    t = t_(dur); n = len(t)
    env = np.sin(np.pi * np.clip(t / dur, 0, 1)) ** 2
    peak = r.uniform(0.35, 0.5)
    env = np.where(t / dur < peak, np.sin(0.5 * np.pi * t / dur / peak) ** 2, np.cos(0.5 * np.pi * (t / dur - peak) / (1 - peak)) ** 1.5)
    centre = f_lo + (f_hi - f_lo) * np.sin(np.pi * np.clip(t / dur, 0, 1))
    x = noise(n); y = np.zeros(n)
    # 一段一段變中心頻率的帶通
    hop = 256
    zi = None
    for s in range(0, n, hop):
        c = centre[s]; lo, hi = c * (1 - q), c * (1 + q)
        sos = signal.butter(2, [lo, hi], btype='band', fs=SR, output='sos')
        if zi is None: zi = signal.sosfilt_zi(sos) * 0
        y[s:s + hop], zi = signal.sosfilt(sos, x[s:s + hop], zi=zi)
    return y * env

for i in range(3): save(f'swing_whoosh_{i+1}', whoosh(0.26 + 0.04 * i, 350 + 80 * i, 1700 + 200 * i, i))

# ---------- 跳躍：短的上揚風聲 + 衣服 ----------
def cloth(dur, seed):
    r = np.random.default_rng(300 + seed); t = t_(dur); n = len(t)
    am = lp(np.abs(noise(n)), 30) ; am = am / am.max()
    return bp(noise(n), 900, 5000) * am * np.exp(-t / (dur * 0.5))
for i in range(2):
    j = mix(whoosh(0.22, 500 + 100 * i, 1400 + 150 * i, 10 + i, 0.4), cloth(0.2, i) * 0.5)
    save(f'jump_whoosh_{i+1}', j, 0.7)

# ---------- 落地：低沉的一聲 + 沙土 ----------
def land(seed, heavy=False):
    r = np.random.default_rng(400 + seed); d = 0.45; t = t_(d); n = len(t)
    f = 95 * np.exp(-t / 0.04) + 45
    thump = sweep(f, d) * np.exp(-t / (0.12 if heavy else 0.07)) * 1.4
    grit = np.zeros(n)
    for _ in range(40 if heavy else 25):
        s = int(r.uniform(0, 0.12) * SR); L = int(r.uniform(0.002, 0.008) * SR)
        g = noise(L) * np.exp(-np.arange(L) / (L / 3)) * r.uniform(0.2, 1) * np.exp(-s / SR / 0.05)
        grit[s:s + L] += g[:max(0, min(L, n - s))]
    grit = bp(grit, 1500, 7000) * 0.8
    dust = lp(noise(n), 700) * np.exp(-t / 0.05)
    return np.tanh((thump + grit + dust) * 1.3)
save('jump_land_1', land(0)); save('jump_land_2', land(1)); save('jump_land_heavy', land(2, True))

# ---------- 鳥叫 ----------
BSR = SR
def chirp_note(dur, f_start, f_end, curve=1.0, vib=0.0, vib_rate=40, harm=0.15, att=0.004, rel=None):
    t = t_(dur); u = t / dur
    f = f_start + (f_end - f_start) * u ** curve
    if vib: f = f * (1 + vib * np.sin(2 * np.pi * vib_rate * t))
    ph = 2 * np.pi * np.cumsum(f) / SR
    x = np.sin(ph) + harm * np.sin(2 * ph) + harm * 0.3 * np.sin(3 * ph)
    rel = rel or dur * 0.5
    env = np.minimum(1, t / att) * np.minimum(1, (dur - t) / rel) ** 1.5
    env = np.clip(env, 0, 1)
    return x * env

def seq(notes, gaps):
    out = []
    for nte, g in zip(notes, gaps): out.append(nte); out.append(np.zeros(int(g * SR)))
    return np.concatenate(out)

def birdify(x):
    # 一點空間感：短的殘響
    ir_t = t_(0.25); ir = noise(len(ir_t)) * np.exp(-ir_t / 0.05) * 0.05; ir[0] = 1
    return signal.fftconvolve(x, ir)[:len(x) + len(ir) // 2]

r = np.random.default_rng(500)
# 麻雀：幾聲短促的啾啾
for k in range(3):
    n = r.integers(2, 5)
    notes = [chirp_note(r.uniform(0.04, 0.08), r.uniform(5200, 6500), r.uniform(3400, 4200), 0.7, harm=0.25) for _ in range(n)]
    save(f'bird_sparrow_{k+1}', birdify(seq(notes, r.uniform(0.06, 0.14, n))), 0.6)
# 顫音（連續快速的音）
for k in range(2):
    base = r.uniform(3800, 4800); n = r.integers(10, 16)
    notes = [chirp_note(0.035, base * 1.15, base * 0.9, 1.0, harm=0.1) * (0.6 + 0.4 * np.sin(np.pi * i / n)) for i in range(n)]
    save(f'bird_trill_{k+1}', birdify(seq(notes, [0.03] * n)), 0.5)
# 日本樹鶯：長長的「ホー」再接「ホケキョ」
def uguisu(seed):
    rr = np.random.default_rng(600 + seed)
    hoo = chirp_note(1.1, 1050, 1180, 2.0, vib=0.004, vib_rate=6, harm=0.08, att=0.12, rel=0.25)
    ho = chirp_note(0.14, 1700, 2300, 1, harm=0.1, att=0.01, rel=0.05)
    ke = chirp_note(0.09, 2900, 2500, 1, harm=0.1, att=0.005, rel=0.03)
    kyo = chirp_note(0.32, 2200, 3300, 0.5, vib=0.01, vib_rate=30, harm=0.1, att=0.01, rel=0.12)
    return birdify(seq([hoo, ho, ke, kyo], [0.12, 0.05, 0.04, 0.2]))
save('bird_uguisu_1', uguisu(0), 0.55)
# 綠繡眼一類：兩個音來回的口哨
for k in range(2):
    a, b = r.uniform(2600, 3200), r.uniform(3600, 4200)
    notes = []
    for i in range(r.integers(3, 6)):
        notes.append(chirp_note(0.09, a, b, 0.6, harm=0.05, att=0.01))
        notes.append(chirp_note(0.07, b, a * 0.95, 1.2, harm=0.05, att=0.005))
    save(f'bird_whistle_{k+1}', birdify(seq(notes, [0.02, 0.06] * (len(notes) // 2))), 0.5)
# 遠處的斑鳩：低沉的「咕—咕咕」
def dove():
    n1 = chirp_note(0.35, 520, 470, 1, harm=0.35, att=0.05, rel=0.15)
    n2 = chirp_note(0.22, 540, 500, 1, harm=0.35, att=0.03, rel=0.1)
    return birdify(lp(seq([n1, n2, n2], [0.15, 0.08, 0.3]), 1800))
save('bird_dove_1', dove(), 0.4)

# ---------- 喝酒 ----------
def bubbles(dur, rate, f_lo, f_hi, seed):
    rr = np.random.default_rng(700 + seed); n = int(dur * SR); x = np.zeros(n)
    for s in np.sort(rr.uniform(0, dur, int(dur * rate))):
        f0 = rr.uniform(f_lo, f_hi); tau = rr.uniform(0.006, 0.02); L = int(tau * 6 * SR)
        tt = np.arange(L) / SR
        f = f0 * (1 + 0.1 * tt / tau)             # 氣泡破掉時音高往上
        b = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-tt / tau) * rr.uniform(0.3, 1)
        i = int(s * SR); m = min(L, n - i); x[i:i + m] += b[:m]
    return x
def gulp(seed):
    rr = np.random.default_rng(800 + seed); d = 0.28; t = t_(d)
    f = 160 + 380 * (t / d) ** 0.6
    throat = sweep(f, d) * np.exp(-((t - 0.07) / 0.05) ** 2) * 0.9
    click = hp(noise(len(t)), 1200) * np.exp(-t / 0.004) * 0.3
    body = bp(noise(len(t)), 250, 900) * np.exp(-((t - 0.06) / 0.04) ** 2) * 0.8
    glug = bubbles(d, 60, 350, 900, seed) * 0.5
    return lp(throat + click + body + glug, 3500)
for i in range(3): save(f'drink_gulp_{i+1}', gulp(i), 0.8)
# 從酒壺倒進嘴裡：連續的咕嚕聲
pour = mix(bubbles(0.9, 70, 500, 1500, 20) * np.minimum(1, t_(0.9) / 0.1) * np.minimum(1, (0.9 - t_(0.9)) / 0.3), lp(noise(int(0.9 * SR)), 900) * 0.15)
save('drink_pour', pour, 0.6)
# 喝完：「哈——」的一口氣
d = 0.7; t = t_(d); breath = noise(len(t))
ah = bp(breath, 600, 1300) * 0.7 + bp(breath, 1800, 2800) * 0.35 + hp(breath, 4000) * 0.15
env = np.minimum(1, t / 0.06) * np.exp(-np.maximum(0, t - 0.12) / 0.22)
save('drink_ahh', ah * env, 0.5)
