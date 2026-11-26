# 整理刀的錄音（從網路下載的兩個 mp3，原檔不放進專案）：
#   sharp-katana-swing-whoosh.mp3：裡面是連續三刀 → 切成 katana_swing_1~3（揮刀聲，PlayerSfx 輪流用）
#   wet-sword-stab-into-flesh.mp3：刀刺進肉裡 → 剪掉開頭 0.09 秒的空白 → flesh_stab（砍中敵人的聲音，玩家的 MeleeCombat.hitSound）
# 步驟：先用 Windows 內建的轉檔把兩個 mp3 轉成 swing.wav、stab.wav：
#   powershell -File mp3_to_wav.ps1 -InFile <mp3 完整路徑> -OutDir <資料夾> -OutName swing.wav
# 再跑這支：
#   python -P prepare_blade_recordings.py <放 swing.wav / stab.wav 的資料夾> ../../Assets/_Project/Resources/Audio/SFX/Blade
# 輸出 16-bit 單聲道 wav（3D 音效要單聲道），每段淡入 15 ms、結尾淡出 60 ms，峰值調到 0.92。
import numpy as np, os, sys
from scipy.io import wavfile

SRC, OUT = sys.argv[1], sys.argv[2]
os.makedirs(OUT, exist_ok=True)


def load(name):
    sr, x = wavfile.read(os.path.join(SRC, name))
    x = x.astype(np.float64)
    if x.ndim > 1: x = x.mean(axis=1)
    return sr, x / (np.abs(x).max() or 1)


def save(name, sr, x, start, end):
    a, b = int(start * sr), min(len(x), int(end * sr))
    y = x[a:b].copy()
    fi, fo = int(0.015 * sr), int(0.06 * sr)
    y[:fi] *= np.linspace(0, 1, fi)
    y[-fo:] *= np.linspace(1, 0, fo)
    y = y / (np.abs(y).max() or 1) * 0.92
    wavfile.write(os.path.join(OUT, name + '.wav'), sr, (y * 32767).astype(np.int16))
    print(f'{name}: {start:.3f} ~ {b / sr:.3f} s ({len(y) / sr:.2f} s)')


# 揮刀錄音（2.1 秒）的音量（20 ms 視窗，相對最大值）：
#   第一刀 0.10~0.65 s，0.675 s 最安靜（-31 dB）
#   0.72~0.82 s 有一小段預揮，0.85~0.90 s 稍微安靜（-23 dB），第二刀 0.95~1.07 s，1.13~1.25 s 安靜（-25~-28 dB）
#   第三刀 1.30~1.60 s，尾巴拖到 2.0 s
# 第二刀從預揮之後切，聽起來才是一刀；三段都從起音前一點開始，揮刀聲一播就聽得到風切。
sr, x = load('swing.wav')
for k, (a, b) in enumerate([(0.075, 0.69), (0.87, 1.20), (1.26, 2.05)]):
    save(f'katana_swing_{k + 1}', sr, x, a, b)

# 刺進肉裡（0.8 秒）：0.096 s 才出聲、0.19 s 最大、0.52 s 後只剩很小的尾音
sr, x = load('stab.wav')
save('flesh_stab', sr, x, 0.085, 0.725)
