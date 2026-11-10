"""
Tripo 生成的人物（老僧、村長）的貼圖：縮成 2048，再把 metallic + roughness 合成 URP Lit 用的 Metallic Map
（R = 金屬度，A = 光滑度 = 1 - 粗糙度）。

用法：
    python make_monk_textures.py <basecolor.PNG> <normal.PNG> <metallic.PNG> <roughness.PNG> [名字，預設 Monk]

輸出到 Assets/_Project/Models/JapaneseVillage/<名字>/T_JV_<名字>_Base / _Normal / _MetallicSmooth.png
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
MODELS = os.path.normpath(os.path.join(HERE, "..", "..", "Assets", "_Project", "Models", "JapaneseVillage"))
SIZE = 2048


def load(path, mode="RGB"):
    return Image.open(path).convert(mode).resize((SIZE, SIZE), Image.LANCZOS)


def main():
    if len(sys.argv) < 5:
        print(__doc__)
        sys.exit(1)
    base, normal, metallic, roughness = sys.argv[1:5]
    name = sys.argv[5] if len(sys.argv) > 5 else "Monk"
    OUT = os.path.join(MODELS, name)
    os.makedirs(OUT, exist_ok=True)
    load(base).save(os.path.join(OUT, f"T_JV_{name}_Base.png"), optimize=True)
    load(normal).save(os.path.join(OUT, f"T_JV_{name}_Normal.png"), optimize=True)

    m = np.asarray(load(metallic, "L"), dtype=np.uint8)
    r = np.asarray(load(roughness, "L"), dtype=np.uint8)
    packed = np.zeros((SIZE, SIZE, 4), dtype=np.uint8)
    packed[..., 0] = m
    packed[..., 1] = m
    packed[..., 2] = m
    packed[..., 3] = 255 - r
    Image.fromarray(packed, "RGBA").save(os.path.join(OUT, f"T_JV_{name}_MetallicSmooth.png"), optimize=True)
    print("saved to", OUT, "metallic mean", m.mean().round(1), "smoothness mean", (255 - r).mean().round(1))


if __name__ == "__main__":
    main()
