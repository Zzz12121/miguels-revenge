"""
村民模型（Tripo 生成的農民）的貼圖變化版本。

用法：
    python make_villager_textures.py [Tripo 匯出的 BaseColor.png] [Normal_Bake.png]

- 有給 Tripo 的原始貼圖：縮成 2048 存成 T_JV_Villager_Base_Grey.png / T_JV_Villager_Normal.png
- 沒給：直接拿專案裡已經有的 Grey 版本重做其他顏色
- 其他顏色：只改「衣服」（灰色、飽和度低的像素），皮膚和頭髮保持原樣
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Project", "Models", "JapaneseVillage", "Villager"))
SIZE = 2048

# 衣服要換成的顏色（sRGB 0~1）：亮度會照原本灰布的明暗變化
VARIANTS = {
    "Indigo": (0.30, 0.35, 0.48),
    "Brown": (0.47, 0.38, 0.29),
    "Olive": (0.40, 0.42, 0.30),
}
DYE = 0.8  # 染色的強度：1 = 完全換成目標色，越小越保留原本灰布的顏色


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def main():
    os.makedirs(OUT, exist_ok=True)
    grey_path = os.path.join(OUT, "T_JV_Villager_Base_Grey.png")
    if len(sys.argv) > 1:
        Image.open(sys.argv[1]).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS).save(grey_path, optimize=True)
        print("saved", grey_path)
    if len(sys.argv) > 2:
        normal_path = os.path.join(OUT, "T_JV_Villager_Normal.png")
        Image.open(sys.argv[2]).convert("RGB").resize((SIZE, SIZE), Image.LANCZOS).save(normal_path, optimize=True)
        print("saved", normal_path)

    base = np.asarray(Image.open(grey_path).convert("RGB")).astype(np.float32) / 255.0
    mx = base.max(axis=2)
    mn = base.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1e-4)
    lum = base @ np.array([0.299, 0.587, 0.114], dtype=np.float32)

    # 衣服：灰灰的（飽和度低）而且不是全黑的空白區；皮膚偏粉橘、頭髮偏棕，飽和度都比較高
    warm = (base[..., 0] - base[..., 2]) / np.maximum(mx, 1e-4)   # 皮膚偏暖（紅比藍多很多）
    cloth = (1.0 - smoothstep(0.12, 0.24, sat)) * (1.0 - smoothstep(0.10, 0.18, warm)) * smoothstep(0.03, 0.08, lum)
    mean = float((lum * cloth).sum() / max(cloth.sum(), 1.0))
    print(f"cloth pixels {cloth.mean() * 100:.1f}%  mean luminance {mean:.3f}")

    for name, color in VARIANTS.items():
        target = np.array(color, dtype=np.float32)
        tint = np.clip(target[None, None, :] * (lum / mean)[..., None] * (mean / target.mean()) * 1.05, 0.0, 1.0)
        tint = base + (tint - base) * DYE
        out = base * (1.0 - cloth[..., None]) + tint * cloth[..., None]
        path = os.path.join(OUT, f"T_JV_Villager_Base_{name}.png")
        Image.fromarray((out * 255.0 + 0.5).astype(np.uint8)).save(path, optimize=True)
        print("saved", path)


if __name__ == "__main__":
    main()
