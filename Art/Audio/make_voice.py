# 對話英文配音：voice_lines.tsv → (prep) tts_jobs.tsv → PowerShell 用 Zira 念 → (post) 調音高、修頭尾、寫進 Resources/Audio/Voice + voice_index.txt
# 用法（在 Art/Audio 底下）：
#   python -P make_voice.py prep
#   powershell -ExecutionPolicy Bypass -File tts.ps1 -Dir <暫存資料夾>      （prep 會印出暫存資料夾的路徑）
#   python -P make_voice.py post
# 改了台詞：在 voice_lines.tsv 加 / 改一行（profile ⇥ 中文 ⇥ 英文；玩家說的話開頭是 @；會變的部分寫成 {...}）再跑一次
import sys, os, re, hashlib, html
import numpy as np
from scipy.io import wavfile
from scipy import signal
from fractions import Fraction

HERE = os.path.dirname(os.path.abspath(__file__))
import tempfile
WORK = os.path.join(tempfile.gettempdir(), 'miguels_voice')
RAW = os.path.join(WORK, 'tts_raw')
OUT = os.path.join(HERE, '..', '..', 'Assets', '_Project', 'Resources', 'Audio', 'Voice')

# SSML 音高、語速；factor = 之後再整段變調（< 1 更低沉、也更慢）
PROFILES = {
    'player':   dict(pitch='-8%',  rate=1.0,  factor=0.97),
    'woman':    dict(pitch='+20%', rate=1.0,  factor=1.03),
    'oldwoman': dict(pitch='-8%',  rate=0.92, factor=0.95),
    'child':    dict(pitch='+25%', rate=0.95, factor=1.12),
    'man':      dict(pitch='-30%', rate=1.12, factor=0.84),
    'oldman':   dict(pitch='-40%', rate=1.06, factor=0.80),
}

def key(zh):
    """和 DialogueVoice.Key 一樣：去掉開頭 @、空白，數字 → #；{...} → *"""
    zh = re.sub(r'\{[^}]*\}', '*', zh)
    zh = re.sub(r'\*+', '*', zh)
    out, last = [], False
    for i, c in enumerate(zh):
        if i == 0 and c == '@': continue
        if c.isspace(): continue
        d = '0' <= c <= '9'
        if d:
            if not last: out.append('#')
        else: out.append(c)
        last = d
    return ''.join(out)

def rows():
    seen = {}
    for line in open(os.path.join(HERE, 'voice_lines.tsv'), encoding='utf-8'):
        line = line.rstrip('\n')
        if not line or line.startswith('#'): continue
        prof, zh, en = line.split('\t')
        k = key(zh)
        if k in seen: continue
        seen[k] = 1
        clip = 'v_' + hashlib.md5(k.encode('utf-8')).hexdigest()[:10]
        yield prof, zh, en, k, clip

if sys.argv[1] == 'prep':
    os.makedirs(RAW, exist_ok=True)
    with open(os.path.join(WORK, 'tts_jobs.tsv'), 'w', encoding='utf-8') as f:
        n = 0
        for prof, zh, en, k, clip in rows():
            p = PROFILES[prof]
            ssml = ("<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='en-US'>"
                    f"<prosody pitch='{p['pitch']}' rate='{p['rate']}'>{html.escape(en, quote=False)}</prosody></speak>")
            f.write(f'{clip}\t{ssml}\n'); n += 1
    print('jobs', n, '→ 接著跑：powershell -ExecutionPolicy Bypass -File tts.ps1 -Dir', WORK)

elif sys.argv[1] == 'post':
    os.makedirs(OUT, exist_ok=True)
    idx = ['# 對話英文配音對照表（Art/Audio/make_voice.py 產生）：中文台詞 key ⇥ 語音檔 ⇥ 英文']
    total = 0
    for prof, zh, en, k, clip in rows():
        sr, x = wavfile.read(os.path.join(RAW, clip + '.wav'))
        x = x.astype(np.float32) / 32768
        # 去掉前後的靜音（留一點點）
        a = np.abs(x); thr = 0.01
        nz = np.where(a > thr)[0]
        if len(nz): x = x[max(0, nz[0] - int(0.03 * sr)): nz[-1] + int(0.12 * sr)]
        f = PROFILES[prof]['factor']
        if abs(f - 1) > 1e-3:
            fr = Fraction(1 / f).limit_denominator(40)
            x = signal.resample_poly(x, fr.numerator, fr.denominator)
        # 男聲稍微濾掉一點高頻，聽起來厚一點
        if prof in ('man', 'oldman'):
            sos = signal.butter(2, 5200, 'low', fs=sr, output='sos'); x = signal.sosfilt(sos, x)
        x = x / (np.max(np.abs(x)) + 1e-6) * 0.85
        n = int(0.008 * sr); x[:n] *= np.linspace(0, 1, n); x[-n:] *= np.linspace(1, 0, n)
        wavfile.write(os.path.join(OUT, clip + '.wav'), sr, (x * 32767).astype(np.int16))
        total += len(x) / sr
        idx.append(f'{k}\t{clip}\t{en}')
    open(os.path.join(OUT, 'voice_index.txt'), 'w', encoding='utf-8', newline='\n').write('\n'.join(idx) + '\n')
    print('clips', len(idx) - 1, f'total {total:.0f}s')
