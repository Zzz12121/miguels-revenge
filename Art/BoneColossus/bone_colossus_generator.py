"""
GeminiGrowUp — 骸骨巨像 Boss 產生器（Blender 4.x）
（骨架、動作跟方塊石像完全一樣，只換了外型：圓潤的低多邊形骨頭，風格接近小怪骷髏；第三版：更兇的臉、更粗的骨頭、裂痕）

用法：
  1. Blender → File → New → General（開一個新檔，避免動到其他東西）
  2. 切到 Scripting 分頁 → Open → 選這個檔案 → 按 ▶ Run Script
  3. 會產生：
       - GolemRig      骨架（Generic，不是 Humanoid）
       - Golem_Body    由一塊塊骨頭組成的身體（每一塊 100% 綁在一根骨頭上，不需要調權重）
       - 動作：Idle / Walk / Swipe / Slam / Roar / Beam / Jump / JumpLand / Death
         （名稱要維持 Golem_ 開頭，Unity 的安裝工具靠名稱對應到 Boss 的招式）
  4. 匯出到 Unity：選取 GolemRig 和 Golem_Body → File → Export → FBX
       Apply Scalings = FBX All、Forward = -Z、Up = Y、
       取消勾選 Add Leaf Bones、勾選 Bake Animation → All Actions

⚠ 重新執行這個腳本會「重建整隻 Boss 和所有 Golem_ 動作」。
  如果你已經在 Blender 裡手動調過動畫，就不要再執行腳本，改用 Edit Mode 直接改造型。

改造型的方法：
  - 想改某一塊骨頭的粗細 / 位置：改下面 PARTS_CENTER / PARTS_SIDE 清單的數字再重新執行（最簡單）
  - 想加減裂痕：改 CRACKS 清單
  - 或在 Edit Mode 直接拉頂點（每塊骨頭是一個獨立的區塊，選一個面按 L 可以選取整塊）
  - 物件、骨頭、動作、材質的名稱都沿用石像時期的（Golem_…、M_Golem…），Unity 的安裝工具靠這些名稱對應，不要改

角色面向 -Y（Blender 的前視圖看到的是正面），單位是公尺，肩高約 5.8m、頭頂約 7.8m、含角約 9.3m（骨架跟石像一樣大）。
"""

import math
import random

import bpy
import bmesh
from mathutils import Vector, Quaternion, Euler

RIG_NAME = "GolemRig"
BODY_NAME = "Golem_Body"
COLLECTION_NAME = "Golem"

# ---------------------------------------------------------------------------
#  骨架：(名稱, 頭, 尾, 父骨頭)
#  .L 在 +X（角色的左手邊），.R 在 -X
# ---------------------------------------------------------------------------
BONES_CENTER = [
    ("root",  (0, 0, 0.0), (0, 0, 0.5), None),
    ("hips",  (0, 0, 2.4), (0, 0, 3.1), "root"),
    ("spine", (0, 0, 3.1), (0, 0, 4.0), "hips"),
    ("chest", (0, 0, 4.0), (0, 0, 5.4), "spine"),
    ("head",  (0, -0.5, 5.4), (0, -0.9, 6.2), "chest"),
]
BONES_SIDE = [  # 只寫左邊，右邊自動鏡像
    ("shoulder",  (0.6, 0, 5.2),   (1.9, 0, 5.2),     "chest"),
    ("upper_arm", (2.2, 0, 5.0),   (2.5, 0, 3.4),     "shoulder"),
    ("forearm",   (2.5, 0, 3.4),   (2.6, -0.2, 1.8),  "upper_arm"),
    ("hand",      (2.6, -0.2, 1.8), (2.65, -0.35, 0.8), "forearm"),
    ("thigh",     (0.85, 0, 2.6),  (0.9, 0, 1.6),     "hips"),
    ("shin",      (0.9, 0, 1.6),   (0.9, 0, 0.6),     "thigh"),
    ("foot",      (0.9, 0, 0.6),   (0.9, -0.8, 0.2),  "shin"),
]

# ---------------------------------------------------------------------------
#  身體零件。四種形狀：
#    ("sphere", 骨頭, 中心, (x半徑, y半徑, z半徑), 材質, 旋轉(度), 細分)   圓潤的骨塊：頭骨、關節、椎骨
#    ("tube",   骨頭, [路徑上的點...], [每個點的半徑...], 材質, 邊數)      彎管：肋骨、角、手指、爪、牙
#    ("bone",   骨頭, 起點, 終點, 骨幹半徑, 關節半徑, 材質)                 一根長骨頭（兩端有膨大的關節）
#    ("box",    骨頭, 中心, 大小, 材質, 旋轉(度))                           小方塊（目前沒用到）
#  PARTS_SIDE 會自動鏡像到右邊；掛在中央骨頭（head / chest / hips…）上的零件只鏡像位置。
#  CRACKS 是貼在表面上的黑色裂痕（左右不對稱，所以直接寫骨頭全名）。
# ---------------------------------------------------------------------------

def _rib(z0, width, drop, end_angle, r0=0.14, r1=0.085, steps=9):
    """一根肋骨：從脊椎往外繞到胸骨的彎管（從上面看是半個橢圓，越往前越低）"""
    pts, radii = [], []
    start = 0.22
    for k in range(steps + 1):
        t = k / steps
        ang = start + (end_angle - start) * t
        pts.append((width * math.sin(ang), -0.15 + 0.75 * math.cos(ang), z0 - drop * (1 - math.cos(ang)) / 2))
        radii.append(r0 + (r1 - r0) * t)
    return ("tube", "chest", pts, radii, "bone", 6)


def _on(center, radii, direction, push=1.05):
    """橢球表面上、某個方向的點（裂痕的路徑用這個比較好寫）"""
    v = Vector(direction).normalized()
    return (center[0] + radii[0] * v.x * push, center[1] + radii[1] * v.y * push, center[2] + radii[2] * v.z * push)


PARTS_CENTER = [
    # ---- 骨盆 ----
    ("sphere", "hips", (0, 0.2, 2.78), (0.56, 0.44, 0.46), "bone_dark", (0, 0, 0), 2),
    ("tube",   "hips", [(-0.75, -0.3, 2.52), (-0.38, -0.52, 2.42), (0, -0.6, 2.4), (0.38, -0.52, 2.42), (0.75, -0.3, 2.52)],
               [0.19, 0.18, 0.19, 0.18, 0.19], "bone", 6),
    # ---- 腰椎 ----
    ("sphere", "spine", (0, 0.15, 3.2),  (0.44, 0.44, 0.19), "bone", (0, 0, 0), 2),
    ("sphere", "spine", (0, 0.15, 3.52), (0.42, 0.42, 0.19), "bone", (0, 0, 0), 2),
    ("sphere", "spine", (0, 0.15, 3.84), (0.42, 0.42, 0.19), "bone", (0, 0, 0), 2),
    ("tube",   "spine", [(0, 0.45, 3.2), (0, 0.95, 3.08)], [0.12, 0.03], "bone_dark", 5),
    ("tube",   "spine", [(0, 0.45, 3.52), (0, 0.95, 3.4)], [0.12, 0.03], "bone_dark", 5),
    ("tube",   "spine", [(0, 0.45, 3.84), (0, 0.95, 3.72)], [0.12, 0.03], "bone_dark", 5),
    # ---- 胸椎 + 背上的骨刺 ----
    ("sphere", "chest", (0, 0.52, 4.2),  (0.36, 0.36, 0.17), "bone", (0, 0, 0), 2),
    ("sphere", "chest", (0, 0.58, 4.48), (0.36, 0.36, 0.17), "bone", (0, 0, 0), 2),
    ("sphere", "chest", (0, 0.62, 4.76), (0.36, 0.36, 0.17), "bone", (0, 0, 0), 2),
    ("sphere", "chest", (0, 0.6, 5.04),  (0.36, 0.36, 0.17), "bone", (0, 0, 0), 2),
    ("sphere", "chest", (0, 0.52, 5.3),  (0.34, 0.34, 0.17), "bone", (0, 0, 0), 2),
    ("tube",   "chest", [(0, 0.8, 4.48), (0, 1.3, 4.7), (0, 1.6, 5.1)], [0.16, 0.1, 0.01], "bone_dark", 5),
    ("tube",   "chest", [(0, 0.85, 4.76), (0, 1.45, 5.05), (0, 1.8, 5.6)], [0.18, 0.11, 0.01], "bone_dark", 5),
    ("tube",   "chest", [(0, 0.82, 5.04), (0, 1.35, 5.4), (0, 1.6, 5.95)], [0.16, 0.1, 0.01], "bone_dark", 5),
    # ---- 胸骨 ----
    ("sphere", "chest", (0, -0.94, 4.72), (0.24, 0.12, 0.62), "bone", (0, 0, 0), 2),
    # ---- 肋骨籠裡燃燒的靈魂核心 ----
    ("sphere", "chest", (0, -0.12, 4.65), (0.5, 0.5, 0.5), "core", (0, 0, 0), 1),
    ("sphere", "chest", (0, -0.12, 4.75), (0.3, 0.3, 0.8), "core", (0, 0, 30), 1),
    # ---- 頸椎 ----
    ("sphere", "chest", (0, 0.32, 5.5),  (0.32, 0.32, 0.16), "bone", (0, 0, 0), 2),
    ("sphere", "chest", (0, 0.2, 5.8),   (0.3, 0.3, 0.15), "bone", (0, 0, 0), 2),
    ("sphere", "head",  (0, -0.05, 5.58), (0.25, 0.25, 0.13), "bone", (0, 0, 0), 2),
    # ---- 頭骨 ----
    ("sphere", "head", (0, -0.45, 6.15), (0.8, 0.92, 0.74), "bone", (0, 0, 0), 2),       # 顱骨
    ("sphere", "head", (0, -1.02, 5.74), (0.5, 0.42, 0.42), "bone", (0, 0, 0), 2),       # 臉 / 上顎
    ("sphere", "head", (0, -1.42, 5.8), (0.11, 0.07, 0.17), "void", (0, 0, 0), 1),       # 鼻孔
    ("tube",   "head", [(-0.58, -0.55, 5.62), (-0.56, -0.86, 5.16), (-0.34, -1.24, 4.92), (0, -1.38, 4.86),
                        (0.34, -1.24, 4.92), (0.56, -0.86, 5.16), (0.58, -0.55, 5.62)],
               [0.13, 0.16, 0.17, 0.19, 0.17, 0.16, 0.13], "bone", 6),                   # 下顎（張得更開）
    ("tube",   "head", [(0, -1.42, 4.8), (0, -1.5, 4.56)], [0.13, 0.01], "bone_dark", 5),  # 下巴的骨刺
]
PARTS_SIDE = [
    # ---- 臉：斜吊的眼窩 + 細長的發光眼 + 壓得很低的倒八字眉骨 ----
    ("sphere", "head", (0.35, -1.18, 6.0), (0.3, 0.14, 0.2), "void", (0, -22, 0), 2),
    ("sphere", "head", (0.34, -1.3, 5.97), (0.13, 0.045, 0.06), "eye", (0, -22, 0), 1),
    ("tube",   "head", [(0.07, -1.34, 5.98), (0.36, -1.4, 6.19), (0.74, -1.1, 6.38), (0.9, -0.82, 6.52)],
               [0.1, 0.14, 0.09, 0.01], "bone", 5),
    ("sphere", "head", (0.54, -0.98, 5.8), (0.2, 0.3, 0.13), "bone", (0, 0, 0), 2),      # 顴骨
    ("tube",   "head", [(0.66, -0.9, 5.8), (0.86, -0.6, 5.86), (0.96, -0.3, 5.98)], [0.1, 0.07, 0.01], "bone_dark", 5),  # 顴骨往後的骨刺
    # ---- 牙：尖的。上排三顆（中間那顆是獠牙），下排三顆 ----
    ("tube",   "head", [(0.09, -1.33, 5.52), (0.09, -1.42, 5.34), (0.09, -1.43, 5.14)], [0.08, 0.065, 0.008], "bone", 4),
    ("tube",   "head", [(0.26, -1.26, 5.52), (0.27, -1.37, 5.3), (0.28, -1.4, 5.06)], [0.1, 0.085, 0.008], "bone", 4),
    ("tube",   "head", [(0.4, -1.14, 5.56), (0.42, -1.24, 5.36), (0.43, -1.25, 5.18)], [0.075, 0.06, 0.008], "bone", 4),
    ("tube",   "head", [(0.16, -1.34, 4.9), (0.17, -1.36, 5.08), (0.17, -1.34, 5.24)], [0.07, 0.055, 0.008], "bone", 4),
    ("tube",   "head", [(0.36, -1.2, 4.92), (0.37, -1.24, 5.12), (0.37, -1.22, 5.36)], [0.09, 0.07, 0.008], "bone", 4),
    ("tube",   "head", [(0.5, -0.98, 5.02), (0.51, -1.0, 5.16), (0.51, -0.99, 5.28)], [0.06, 0.05, 0.008], "bone", 4),
    # ---- 角：一對大的，後面一對小的 ----
    ("tube",   "head", [(0.58, -0.3, 6.62), (0.95, -0.25, 6.95), (1.22, -0.38, 7.35), (1.28, -0.65, 7.75), (1.18, -0.92, 8.02)],
               [0.3, 0.25, 0.18, 0.1, 0.012], "bone_dark", 6),
    ("tube",   "head", [(0.52, 0.08, 6.42), (0.9, 0.3, 6.7), (1.02, 0.46, 7.02)], [0.17, 0.11, 0.012], "bone_dark", 5),
    # ---- 肋骨：6 對（最下面一對比較短） ----
    _rib(4.18, 1.2, 0.35, 2.3),
    _rib(4.42, 1.42, 0.4, 2.78),
    _rib(4.66, 1.5, 0.4, 2.9),
    _rib(4.9, 1.45, 0.38, 2.9),
    _rib(5.14, 1.3, 0.32, 2.9),
    _rib(5.34, 1.02, 0.25, 2.9),
    # ---- 鎖骨、肩胛骨、肩關節、肩上的骨刺 ----
    ("tube",   "shoulder", [(0.15, -0.9, 5.22), (0.9, -0.7, 5.34), (1.75, -0.25, 5.4)], [0.15, 0.15, 0.17], "bone", 6),
    ("sphere", "shoulder", (1.35, 0.8, 5.0), (0.6, 0.14, 0.7), "bone", (0, 0, -15), 2),
    ("sphere", "shoulder", (2.1, 0, 5.2), (0.64, 0.64, 0.58), "bone_dark", (0, 0, 0), 2),
    ("tube",   "shoulder", [(2.1, 0.1, 5.6), (2.36, 0.2, 6.15), (2.55, 0.25, 6.7)], [0.2, 0.12, 0.012], "bone_dark", 5),
    ("tube",   "shoulder", [(1.75, 0.35, 5.55), (1.88, 0.55, 6.0), (1.95, 0.7, 6.4)], [0.16, 0.1, 0.012], "bone_dark", 5),
    # ---- 手臂（粗） ----
    ("bone",   "upper_arm", (2.2, 0, 4.95), (2.48, 0, 3.48), 0.33, 0.5, "bone"),
    ("bone",   "forearm", (2.3, -0.03, 3.3), (2.36, -0.2, 1.95), 0.22, 0.32, "bone"),
    ("bone",   "forearm", (2.82, 0.03, 3.3), (2.92, -0.18, 1.95), 0.2, 0.3, "bone"),
    ("tube",   "forearm", [(2.56, 0, 3.1), (2.64, -0.19, 2.1)], [0.07, 0.07], "core", 5),      # 兩根骨頭之間流動的能量
    ("sphere", "forearm", (2.64, -0.2, 1.8), (0.62, 0.38, 0.26), "bone_dark", (0, 0, 0), 2),   # 手腕
    # ---- 手：手掌 + 四根帶爪的手指 + 拇指 ----
    ("sphere", "hand", (2.65, -0.27, 1.4), (0.74, 0.3, 0.46), "bone", (0, 0, 0), 2),
    ("sphere", "hand", (2.65, -0.55, 1.4), (0.2, 0.06, 0.2), "core", (0, 0, 0), 1),            # 掌心（光束從這裡射出）
    ("tube",   "hand", [(2.1, -0.3, 1.1), (2.07, -0.44, 0.68), (2.07, -0.66, 0.4), (2.1, -0.9, 0.24)], [0.17, 0.15, 0.12, 0.012], "bone", 5),
    ("tube",   "hand", [(2.47, -0.32, 1.06), (2.47, -0.46, 0.62), (2.47, -0.7, 0.34), (2.47, -0.96, 0.18)], [0.17, 0.15, 0.12, 0.012], "bone", 5),
    ("tube",   "hand", [(2.84, -0.32, 1.06), (2.84, -0.46, 0.62), (2.84, -0.7, 0.34), (2.84, -0.96, 0.18)], [0.17, 0.15, 0.12, 0.012], "bone", 5),
    ("tube",   "hand", [(3.2, -0.3, 1.1), (3.23, -0.44, 0.7), (3.23, -0.66, 0.44), (3.2, -0.88, 0.28)], [0.16, 0.14, 0.11, 0.012], "bone", 5),
    ("tube",   "hand", [(2.0, -0.35, 1.5), (1.72, -0.62, 1.34), (1.6, -0.9, 1.1)], [0.17, 0.13, 0.012], "bone", 5),
    # ---- 髖骨（往兩側張開的翼）、髖關節 ----
    ("sphere", "hips", (0.82, 0.15, 2.94), (0.6, 0.24, 0.46), "bone", (0, -25, 20), 2),
    ("sphere", "hips", (0.85, 0, 2.55), (0.4, 0.4, 0.4), "bone_dark", (0, 0, 0), 2),
    # ---- 腿 ----
    ("bone",   "thigh", (0.85, 0, 2.48), (0.9, -0.03, 1.62), 0.3, 0.42, "bone"),
    ("sphere", "thigh", (0.9, -0.42, 1.6), (0.27, 0.15, 0.29), "bone_dark", (0, 0, 0), 2),     # 膝蓋骨
    ("bone",   "shin", (0.85, -0.03, 1.52), (0.88, 0, 0.64), 0.24, 0.34, "bone"),
    ("bone",   "shin", (1.22, 0.08, 1.48), (1.2, 0.08, 0.68), 0.12, 0.17, "bone"),
    # ---- 腳掌、腳跟、三根帶爪的腳趾 ----
    ("sphere", "foot", (0.9, -0.25, 0.3), (0.46, 0.66, 0.24), "bone", (0, 0, 0), 2),
    ("sphere", "foot", (0.9, 0.25, 0.32), (0.3, 0.34, 0.28), "bone_dark", (0, 0, 0), 2),
    ("tube",   "foot", [(0.6, -0.74, 0.27), (0.56, -1.12, 0.22), (0.54, -1.4, 0.08)], [0.14, 0.11, 0.012], "bone", 5),
    ("tube",   "foot", [(0.9, -0.8, 0.27), (0.9, -1.2, 0.22), (0.9, -1.5, 0.08)], [0.15, 0.12, 0.012], "bone", 5),
    ("tube",   "foot", [(1.2, -0.74, 0.27), (1.24, -1.12, 0.22), (1.26, -1.4, 0.08)], [0.14, 0.11, 0.012], "bone", 5),
]

# ---- 裂痕：(骨頭全名, [大概的路徑點...], 半寬, 亂數種子) ----
#  路徑點只要「大概在表面外面一點」就好，程式會自己貼到最近的表面上，並加上鋸齒和分岔。
_SKULL = ((0, -0.45, 6.15), (0.8, 0.92, 0.74))
_SHOULDER = ((2.1, 0, 5.2), (0.64, 0.64, 0.58))
CRACKS = [
    ("head", [_on(*_SKULL, (0.3, -0.8, 0.5)), _on(*_SKULL, (0.12, -0.5, 0.85)), _on(*_SKULL, (0.2, -0.1, 1.0)), _on(*_SKULL, (0.05, 0.35, 0.9))], 0.06, 11),
    ("head", [_on(*_SKULL, (-0.9, -0.4, 0.15)), _on(*_SKULL, (-0.8, -0.15, 0.55)), _on(*_SKULL, (-0.5, 0.2, 0.85))], 0.05, 12),
    ("head", [(0.3, -1.46, 5.8), (0.37, -1.42, 5.62), (0.3, -1.38, 5.5)], 0.03, 13),             # 左眼下方的疤
    ("shoulder.L", [_on(*_SHOULDER, (-0.3, -0.8, 0.5)), _on(*_SHOULDER, (0.2, -0.95, 0.15)), _on(*_SHOULDER, (0.55, -0.8, -0.15))], 0.05, 14),
    ("upper_arm.R", [(-2.28, -0.38, 4.5), (-2.36, -0.38, 4.1), (-2.43, -0.38, 3.75)], 0.05, 15),
    ("forearm.L", [(2.31, -0.3, 3.0), (2.33, -0.33, 2.65), (2.35, -0.38, 2.3)], 0.04, 16),
    ("chest", [(0.06, -1.1, 5.05), (-0.04, -1.1, 4.75), (0.05, -1.1, 4.45)], 0.03, 17),          # 胸骨
    ("hips", [(-0.5, -0.15, 3.2), (-0.8, -0.15, 3.0), (-1.05, -0.1, 2.8)], 0.045, 18),
    ("thigh.L", [(0.86, -0.36, 2.3), (0.89, -0.36, 1.85)], 0.045, 19),
]

HEAD_SCALE = 1.3        # 頭骨放大倍率（想要更大的頭就改這個）
HEAD_LIFT = 0.5         # 頭骨往上抬多少，放大後下巴才不會卡到鎖骨
HEAD_PIVOT = (0.0, -0.5, 5.4)
RANDOM_SEED = 7


def _head_point(p):
    q = tuple(HEAD_PIVOT[i] + (p[i] - HEAD_PIVOT[i]) * HEAD_SCALE for i in range(3))
    return (q[0], q[1], q[2] + HEAD_LIFT)


def _scale_head(parts):
    """把掛在 head 骨頭上的零件，以脖子為中心放大"""
    out = []
    for p in parts:
        if p[1] != "head":
            out.append(p)
        elif p[0] == "sphere":
            out.append((p[0], p[1], _head_point(p[2]), tuple(v * HEAD_SCALE for v in p[3])) + p[4:])
        elif p[0] == "box":
            out.append((p[0], p[1], _head_point(p[2]), tuple(v * HEAD_SCALE for v in p[3])) + p[4:])
        elif p[0] == "tube":
            out.append((p[0], p[1], [_head_point(q) for q in p[2]], [r * HEAD_SCALE for r in p[3]]) + p[4:])
        else:
            out.append(p)
    return out


PARTS_CENTER = _scale_head(PARTS_CENTER)
PARTS_SIDE = _scale_head(PARTS_SIDE)
CRACKS = [(b, [_head_point(q) for q in pts] if b == "head" else pts, w, seed) for b, pts, w, seed in CRACKS]


# ===========================================================================

def mirror_name(name, side):
    return f"{name}.{side}"


def mirror_vec(v, side):
    return (v[0] if side == "L" else -v[0], v[1], v[2])


def get_collection():
    col = bpy.data.collections.get(COLLECTION_NAME)
    if col is None:
        col = bpy.data.collections.new(COLLECTION_NAME)
        bpy.context.scene.collection.children.link(col)
    return col


def make_material(name, color, emission=None, strength=0.0, roughness=0.9):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*color, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1.0)
        bsdf.inputs["Emission Strength"].default_value = strength
    mat.diffuse_color = (*color, 1.0)
    return mat


def build_materials():
    return {
        "bone":      make_material("M_GolemStone", (0.62, 0.57, 0.45), roughness=0.85),     # 骨頭（材質名稱沿用，Unity 的安裝工具靠名稱對應）
        "bone_dark": make_material("M_GolemStoneDark", (0.36, 0.32, 0.26), roughness=0.9),  # 關節、角、爪：比較深
        "eye":       make_material("M_GolemEye", (0.4, 1.0, 0.55), (0.15, 1.0, 0.35), 5.0, 0.4),
        "core":      make_material("M_GolemCore", (0.3, 1.0, 0.6), (0.1, 1.0, 0.4), 3.5, 0.3),
        "void":      make_material("M_GolemVoid", (0.01, 0.01, 0.012), roughness=1.0),       # 眼窩、鼻孔的黑洞
    }


def build_armature(col):
    arm_data = bpy.data.armatures.new(RIG_NAME)
    rig = bpy.data.objects.new(RIG_NAME, arm_data)
    col.objects.link(rig)
    rig.show_in_front = True

    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm_data.edit_bones

    def add(name, head, tail, parent):
        b = eb.new(name)
        b.head = Vector(head)
        b.tail = Vector(tail)
        if parent:
            b.parent = eb[parent]
            b.use_connect = False

    for name, h, t, p in BONES_CENTER:
        add(name, h, t, p)
    for side in ("L", "R"):
        for name, h, t, p in BONES_SIDE:
            parent = p if p in ("chest", "hips") else mirror_name(p, side)
            add(mirror_name(name, side), mirror_vec(h, side), mirror_vec(t, side), parent)

    bpy.ops.object.mode_set(mode="OBJECT")
    for pb in rig.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return rig


import random
from mathutils.bvhtree import BVHTree

CRACK_STEP = 0.07    # 裂痕每隔多遠取一個點（越小越貼合表面）
CRACK_KINK = 0.22    # 每隔多遠轉折一次
CRACK_LIFT = 0.02    # 裂痕浮在表面上方多高（避免跟表面重疊閃爍）


def _merge(bm, deform, part_bm, group_index, mat_index, geo):
    """把一個零件（自己的小 bmesh）併進身體：頂點 100% 綁到一根骨頭，並設定材質。
    同時把這根骨頭的表面記在 geo 裡，之後貼裂痕用。"""
    verts, polys = geo.setdefault(group_index, ([], []))
    base = len(verts)
    new = {}
    for v in part_bm.verts:
        nv = bm.verts.new(v.co)
        nv[deform][group_index] = 1.0
        new[v.index] = nv
        verts.append(v.co.copy())
    for f in part_bm.faces:
        nf = bm.faces.new([new[v.index] for v in f.verts])
        nf.material_index = mat_index
        polys.append([base + v.index for v in f.verts])


def add_crack(bm, deform, geo, group_index, mat_index, rough, width, seed):
    """在某根骨頭的表面貼一條黑色裂痕：鋸齒狀、中間寬兩端尖、帶一兩條分岔"""
    verts, polys = geo[group_index]
    tree = BVHTree.FromPolygons(verts, polys)
    rng = random.Random(seed)

    def snap(p):
        loc, nrm, _, _ = tree.find_nearest(p)
        return loc, nrm

    def resample(points, step):
        out = []
        for p, q in zip(points, points[1:]):
            n = max(1, round((q - p).length / step))
            out += [p.lerp(q, k / n) for k in range(n)]
        out.append(points[-1])
        return out

    def strip(points, w0):
        # 1) 先定出轉折點（左右亂偏），再細分成貼合表面的小段
        ctrl = resample([Vector(p) for p in points], CRACK_KINK)
        for i in range(1, len(ctrl) - 1):
            loc, nrm = snap(ctrl[i])
            side = nrm.cross(ctrl[i + 1] - ctrl[i - 1])
            if side.length > 1e-6:
                ctrl[i] = loc + nrm * 0.03 + side.normalized() * rng.uniform(-1, 1) * w0 * 2.2
        centre = [snap(p) for p in resample(ctrl, CRACK_STEP)]
        n = len(centre)
        if n < 3:
            return centre
        # 2) 沿著中心線往兩邊展開成一條帶子
        prev = None
        for i, (c, nn) in enumerate(centre):
            t = centre[min(i + 1, n - 1)][0] - centre[max(i - 1, 0)][0]
            side = nn.cross(t)
            if side.length < 1e-6:
                continue
            side.normalize()
            w = w0 * math.sin(math.pi * i / (n - 1)) ** 0.6 * rng.uniform(0.65, 1.25)
            row_pts = [c] if i in (0, n - 1) else [c + nn * 0.03 + side * w, c + nn * 0.03 - side * w]
            row = []
            for q in row_pts:
                loc, nrm = snap(q)
                v = bm.verts.new(loc + nrm * CRACK_LIFT)
                v[deform][group_index] = 1.0
                row.append(v)
            if prev is not None and len(prev) + len(row) >= 3:
                loop = prev + list(reversed(row))
                face_n = (loop[1].co - loop[0].co).cross(loop[-1].co - loop[0].co)
                if face_n.dot(nn) < 0:
                    loop.reverse()  # 面要朝外，Unity 才看得到
                bm.faces.new(loop).material_index = mat_index
            prev = row
        return centre

    main = strip(rough, width)
    # 3) 分岔：從主裂痕中段往旁邊長出一兩條短的
    length = sum((Vector(q) - Vector(p)).length for p, q in zip(rough, rough[1:]))
    n = len(main)
    for k in range(2 if length > 0.8 else 1):
        if n < 6:
            break
        i = rng.randrange(n // 4, 3 * n // 4)
        c, nn = main[i]
        t = (main[i + 1][0] - main[i - 1][0]).normalized()
        ang = math.radians(rng.uniform(35, 65)) * (1 if k % 2 == 0 else -1)
        d = Quaternion(nn, ang) @ t
        strip([c + nn * 0.03, c + nn * 0.03 + d * length * rng.uniform(0.3, 0.45)], width * 0.65)


def add_sphere(bm, center, radii, rot_deg, subdiv):
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0)
    rot = Euler([math.radians(a) for a in rot_deg]).to_matrix()
    for v in res["verts"]:
        p = Vector((v.co.x * radii[0], v.co.y * radii[1], v.co.z * radii[2]))
        v.co = rot @ p + Vector(center)


def add_box(bm, center, size, rot_deg):
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]
    rot = Euler([math.radians(a) for a in rot_deg]).to_matrix()
    for v in verts:
        p = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        v.co = rot @ p + Vector(center)
    edges = list({e for v in verts for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=verts + edges, offset=min(size) * 0.25, segments=1, affect="EDGES", profile=0.5)


def add_tube(bm, points, radii, sides):
    """沿著一串點掃出一根管子（每個點可以有不同的半徑），兩端封口"""
    pts = [Vector(p) for p in points]
    n = len(pts)
    rings = []
    normal = None
    for i in range(n):
        if i == 0:
            tangent = pts[1] - pts[0]
        elif i == n - 1:
            tangent = pts[-1] - pts[-2]
        else:
            tangent = (pts[i + 1] - pts[i]).normalized() + (pts[i] - pts[i - 1]).normalized()
        tangent.normalize()
        if normal is None:
            helper = Vector((0, 0, 1)) if abs(tangent.z) < 0.9 else Vector((1, 0, 0))
            normal = tangent.cross(helper).normalized()
        else:
            normal = (normal - tangent * normal.dot(tangent)).normalized()  # 沿著路徑平移，管子才不會扭轉
        binormal = tangent.cross(normal).normalized()
        ring = []
        for j in range(sides):
            ang = 2 * math.pi * j / sides
            ring.append(bm.verts.new(pts[i] + (normal * math.cos(ang) + binormal * math.sin(ang)) * radii[i]))
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            k = (j + 1) % sides
            bm.faces.new((rings[i][j], rings[i][k], rings[i + 1][k], rings[i + 1][j]))
    bm.faces.new(list(reversed(rings[0])))
    bm.faces.new(rings[-1])


def add_long_bone(bm, start, end, shaft, knob, sides=7):
    """一根長骨頭：中間細、兩端粗，兩端各有一顆圓圓的關節"""
    a, b = Vector(start), Vector(end)
    ts = [0.0, 0.14, 0.32, 0.68, 0.86, 1.0]
    rs = [knob * 0.8, knob * 0.72, shaft, shaft, knob * 0.72, knob * 0.8]
    add_tube(bm, [a.lerp(b, t) for t in ts], rs, sides)
    for p in (a, b):
        add_sphere(bm, p, (knob, knob, knob * 0.85), (0, 0, 0), 1)


def add_part(bm, deform, groups, mat_order, part, geo, side=None):
    kind, bone = part[0], part[1]
    center_bones = {b[0] for b in BONES_CENTER}
    mirror = side == "R"

    def mp(p):
        return (-p[0], p[1], p[2]) if mirror else tuple(p)

    def mr(r):
        return (r[0], -r[1], -r[2]) if mirror else r

    bone_name = bone if (side is None or bone in center_bones) else mirror_name(bone, side)
    pb = bmesh.new()  # 每個零件先在自己的 bmesh 裡做好，再併進身體（這樣才分得清楚哪些頂點屬於哪根骨頭）

    if kind == "sphere":
        _, _, c, radii, mat, rot, subdiv = part
        add_sphere(pb, mp(c), radii, mr(rot), subdiv)
    elif kind == "box":
        _, _, c, size, mat, rot = part
        add_box(pb, mp(c), size, mr(rot))
    elif kind == "tube":
        _, _, pts, radii, mat, sides = part
        add_tube(pb, [mp(p) for p in pts], radii, sides)
    elif kind == "bone":
        _, _, s, e, shaft, knob, mat = part
        add_long_bone(pb, mp(s), mp(e), shaft, knob)
    else:
        raise ValueError(kind)

    bmesh.ops.recalc_face_normals(pb, faces=pb.faces[:])  # 面一律朝外（鏡像過的零件會翻面）
    pb.verts.index_update()
    _merge(bm, deform, pb, groups[bone_name], mat_order.index(mat), geo)
    pb.free()


def build_body(col, rig, mats):
    mesh = bpy.data.meshes.new(BODY_NAME)
    body = bpy.data.objects.new(BODY_NAME, mesh)
    col.objects.link(body)

    mat_order = ["bone", "bone_dark", "eye", "core", "void"]
    for key in mat_order:
        mesh.materials.append(mats[key])

    # 每根骨頭一個 vertex group
    groups = {}
    for b in rig.data.bones:
        groups[b.name] = body.vertex_groups.new(name=b.name).index

    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()

    geo = {}
    for part in PARTS_CENTER:
        add_part(bm, deform, groups, mat_order, part, geo)
    for side in ("L", "R"):
        for part in PARTS_SIDE:
            add_part(bm, deform, groups, mat_order, part, geo, side)

    for bone_name, pts, width, seed in CRACKS:
        add_crack(bm, deform, geo, groups[bone_name], mat_order.index("void"), pts, width, seed)

    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = False  # 平面著色：低多邊形的稜角感，跟小怪骷髏一樣

    body.parent = rig
    mod = body.modifiers.new("Armature", "ARMATURE")
    mod.object = rig
    return body


# ---------------------------------------------------------------------------
#  擺姿勢的小工具：以「骨架空間的軸」旋轉骨頭（比直接填骨頭本地軸直覺）
#    X 軸：前後擺（負 = 往前 / 抬起手臂往前上方）
#    Y 軸：左右擺
# ---------------------------------------------------------------------------

def set_rot(rig, bone, axis, degrees):
    pb = rig.pose.bones[bone]
    rest = pb.bone.matrix_local.to_quaternion()
    q = Quaternion(Vector(axis), math.radians(degrees))
    pb.rotation_quaternion = rest.inverted() @ q @ rest


def set_rot_multi(rig, bone, rotations):
    """依序套用多個旋轉，例如 [((1,0,0), -80), ((0,1,0), 20)]"""
    pb = rig.pose.bones[bone]
    rest = pb.bone.matrix_local.to_quaternion()
    q = Quaternion()
    for axis, deg in rotations:
        q = Quaternion(Vector(axis), math.radians(deg)) @ q
    pb.rotation_quaternion = rest.inverted() @ q @ rest


def reset_pose(rig):
    for pb in rig.pose.bones:
        pb.rotation_quaternion = Quaternion()
        pb.location = Vector()


def pose_slam_windup(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), -18)          # 後仰
    set_rot(rig, "head", (1, 0, 0), -15)
    for s in ("L", "R"):
        set_rot(rig, f"upper_arm.{s}", (1, 0, 0), -165)  # 雙手舉過頭
        set_rot(rig, f"forearm.{s}", (1, 0, 0), -25)
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -15)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 25)
    rig.pose.bones["hips"].location = Vector((0, -0.25, 0))  # 微蹲（骨頭本地 Y = 往上）


def pose_slam_impact(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), 30)           # 整個上半身往前砸
    set_rot(rig, "spine", (1, 0, 0), 10)
    set_rot(rig, "head", (1, 0, 0), 10)
    for s in ("L", "R"):
        set_rot(rig, f"upper_arm.{s}", (1, 0, 0), -55)
        set_rot(rig, f"forearm.{s}", (1, 0, 0), -15)
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -28)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 40)
        set_rot(rig, f"foot.{s}", (1, 0, 0), -12)
    rig.pose.bones["hips"].location = Vector((0, -0.38, 0))


def pose_roar(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), -25)          # 朝天
    set_rot(rig, "head", (1, 0, 0), -35)
    set_rot(rig, "upper_arm.L", (0, 1, 0), -75)    # 雙手往兩側張開
    set_rot(rig, "upper_arm.R", (0, 1, 0), 75)
    set_rot(rig, "forearm.L", (0, 1, 0), -25)
    set_rot(rig, "forearm.R", (0, 1, 0), 25)


def pose_idle_breath(rig, k):
    """k = 0~1，呼吸起伏"""
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), 4 + 3 * k)
    set_rot(rig, "head", (1, 0, 0), 3 * k)
    for s in ("L", "R"):
        set_rot(rig, f"upper_arm.{s}", (1, 0, 0), -4 * k)
    rig.pose.bones["hips"].location = Vector((0, -0.06 * k, 0))


def pose_walk(rig, phase):
    """phase = -1 ~ 1：左腳在前(1) / 右腳在前(-1)，0 = 兩腳交錯經過"""
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), 6)
    set_rot_multi(rig, "spine", [((0, 0, 1), 6 * phase)])
    set_rot(rig, "thigh.L", (1, 0, 0), -24 * phase)
    set_rot(rig, "thigh.R", (1, 0, 0), 24 * phase)
    set_rot(rig, "shin.L", (1, 0, 0), 18 if phase < 0 else 6)
    set_rot(rig, "shin.R", (1, 0, 0), 18 if phase > 0 else 6)
    set_rot(rig, "upper_arm.L", (1, 0, 0), 16 * phase)     # 手跟腳反方向擺
    set_rot(rig, "upper_arm.R", (1, 0, 0), -16 * phase)
    rig.pose.bones["hips"].location = Vector((0, -0.12 * abs(phase), 0))


def pose_swipe_windup(rig):
    reset_pose(rig)
    set_rot_multi(rig, "chest", [((0, 0, 1), -28), ((1, 0, 0), -8)])   # 身體往右扭、後仰
    set_rot_multi(rig, "upper_arm.R", [((0, 1, 0), 95), ((1, 0, 0), 25)])  # 右手往右後方舉起
    set_rot(rig, "forearm.R", (0, 1, 0), 30)
    set_rot(rig, "upper_arm.L", (1, 0, 0), -20)
    rig.pose.bones["hips"].location = Vector((0, -0.15, 0))


def pose_swipe_strike(rig):
    reset_pose(rig)
    set_rot_multi(rig, "chest", [((0, 0, 1), 32), ((1, 0, 0), 14)])    # 往左扭、前傾
    set_rot_multi(rig, "upper_arm.R", [((1, 0, 0), -80), ((0, 0, 1), 40)])  # 右手橫掃到左前方
    set_rot(rig, "forearm.R", (1, 0, 0), -10)
    set_rot(rig, "upper_arm.L", (1, 0, 0), 25)
    rig.pose.bones["hips"].location = Vector((0, -0.2, 0))


def pose_beam(rig):
    """舉起右手指向前下方：光束從右手掌射出、斜斜打到前方地面，再貼著地面往前延伸"""
    reset_pose(rig)
    set_rot_multi(rig, "chest", [((0, 0, 1), -12), ((1, 0, 0), -4)])   # 身體微微往右轉、挺胸
    set_rot(rig, "head", (1, 0, 0), 8)                                  # 低頭看著目標
    set_rot_multi(rig, "upper_arm.R", [((1, 0, 0), -62), ((0, 0, 1), 10)])  # 右手往前下方舉、稍微往內指向目標
    set_rot(rig, "forearm.R", (1, 0, 0), -8)
    set_rot(rig, "hand.R", (1, 0, 0), -10)
    set_rot(rig, "upper_arm.L", (1, 0, 0), 12)                          # 左手自然垂下、稍微往後
    set_rot(rig, "thigh.L", (1, 0, 0), -12)                             # 左腳往前踏穩
    set_rot(rig, "shin.L", (1, 0, 0), 14)
    set_rot(rig, "thigh.R", (1, 0, 0), 8)


def pose_jump_crouch(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), 22)
    for s in ("L", "R"):
        set_rot(rig, f"upper_arm.{s}", (1, 0, 0), 40)      # 手往後甩，準備起跳
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -42)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 70)
        set_rot(rig, f"foot.{s}", (1, 0, 0), -25)
    rig.pose.bones["hips"].location = Vector((0, -0.85, 0))


def pose_jump_air(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), -10)
    set_rot(rig, "head", (1, 0, 0), 15)
    for s in ("L", "R"):
        set_rot(rig, f"upper_arm.{s}", (1, 0, 0), -155)    # 雙手舉高，準備往下砸
        set_rot(rig, f"forearm.{s}", (1, 0, 0), -20)
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -20)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 30)


def pose_land(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), 25)
    for s, out in (("L", -35), ("R", 35)):
        set_rot_multi(rig, f"upper_arm.{s}", [((1, 0, 0), -40), ((0, 1, 0), out)])
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -38)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 60)
        set_rot(rig, f"foot.{s}", (1, 0, 0), -20)
    rig.pose.bones["hips"].location = Vector((0, -0.75, 0))


def pose_death_stagger(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), -20)
    set_rot(rig, "head", (1, 0, 0), -35)
    set_rot(rig, "upper_arm.L", (0, 1, 0), -40)
    set_rot(rig, "upper_arm.R", (0, 1, 0), 40)


def pose_death_slump(rig):
    reset_pose(rig)
    set_rot(rig, "chest", (1, 0, 0), -30)
    set_rot(rig, "head", (1, 0, 0), -45)
    for s, out in (("L", -70), ("R", 70)):
        set_rot_multi(rig, f"upper_arm.{s}", [((0, 1, 0), out), ((1, 0, 0), 20)])
        set_rot(rig, f"thigh.{s}", (1, 0, 0), -55)
        set_rot(rig, f"shin.{s}", (1, 0, 0), 85)
    rig.pose.bones["hips"].location = Vector((0, -1.2, 0))


def key_pose(rig, frame):
    for pb in rig.pose.bones:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)


def make_action(rig, name, keys):
    """keys = [(frame, pose_function), ...]"""
    if rig.animation_data is None:
        rig.animation_data_create()
    old = bpy.data.actions.get(name)   # 重新執行時先刪掉舊的，避免出現 Golem_Slam.001
    if old is not None:
        bpy.data.actions.remove(old)
    action = bpy.data.actions.new(name)
    action.use_fake_user = True  # 沒被使用也會存進 .blend
    rig.animation_data.action = action
    for frame, fn in keys:
        fn(rig)
        key_pose(rig, frame)
    return action


def build_actions(rig):
    # 24 fps：拍地的蓄力 ≈ 1.3 秒、怒吼 ≈ 1.5 秒，跟遊戲程式的時間差不多
    make_action(rig, "Golem_Idle", [
        (1, lambda r: pose_idle_breath(r, 0)),
        (24, lambda r: pose_idle_breath(r, 1)),
        (48, lambda r: pose_idle_breath(r, 0)),
    ])
    make_action(rig, "Golem_Slam", [
        (1, reset_pose),
        (32, pose_slam_windup),
        (35, pose_slam_impact),     # 砸下去的瞬間（Unity 裡在這裡放 Animation Event 判定傷害）
        (50, pose_slam_impact),
        (72, reset_pose),
    ])
    make_action(rig, "Golem_Roar", [
        (1, reset_pose),
        (10, pose_roar),
        (36, pose_roar),
        (48, reset_pose),
    ])
    make_action(rig, "Golem_Walk", [        # 循環：32 格 = 1.33 秒走一個完整步伐
        (1, lambda r: pose_walk(r, 1)),
        (9, lambda r: pose_walk(r, 0)),
        (17, lambda r: pose_walk(r, -1)),
        (25, lambda r: pose_walk(r, 0)),
        (33, lambda r: pose_walk(r, 1)),
    ])
    make_action(rig, "Golem_Swipe", [       # 第 18 格揮中
        (1, reset_pose),
        (15, pose_swipe_windup),
        (18, pose_swipe_strike),
        (26, pose_swipe_strike),
        (36, reset_pose),
    ])
    make_action(rig, "Golem_Beam", [        # 第 12 格舉好右手，之後維持姿勢（遊戲裡會停在最後一格直到光束結束）
        (1, reset_pose),
        (12, pose_beam),
        (24, pose_beam),
    ])
    make_action(rig, "Golem_Jump", [        # 第 17 格起跳，之後維持空中姿勢
        (1, reset_pose),
        (17, pose_jump_crouch),
        (21, pose_jump_air),
        (30, pose_jump_air),
    ])
    make_action(rig, "Golem_JumpLand", [    # 第 1 格著地
        (1, pose_land),
        (12, pose_land),
        (32, reset_pose),
    ])
    make_action(rig, "Golem_Death", [
        (1, reset_pose),
        (10, pose_death_stagger),
        (40, pose_death_slump),
        (48, pose_death_slump),
    ])
    rig.animation_data.action = bpy.data.actions["Golem_Idle"]
    reset_pose(rig)


def build_golem():
    for name in (RIG_NAME, BODY_NAME):
        old = bpy.data.objects.get(name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)

    col = get_collection()
    mats = build_materials()
    rig = build_armature(col)
    body = build_body(col, rig, mats)
    build_actions(rig)
    print(f"[Golem] 完成：{len(body.data.polygons)} 面、{len(rig.data.bones)} 根骨頭")
    return rig, body


if __name__ == "__main__":
    build_golem()
