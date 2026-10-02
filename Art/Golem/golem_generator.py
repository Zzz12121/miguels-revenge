"""
GeminiGrowUp — 方塊石像 Boss 產生器（Blender 4.x）

用法：
  1. Blender → File → New → General（開一個新檔，避免動到其他東西）
  2. 切到 Scripting 分頁 → Open → 選這個檔案 → 按 ▶ Run Script
  3. 會產生：
       - GolemRig      骨架（Generic，不是 Humanoid）
       - Golem_Body    由石塊組成的身體（每一塊 100% 綁在一根骨頭上，不需要調權重）
       - 動作：Idle / Walk / Swipe / Slam / Roar / Beam / Jump / JumpLand / Death
         （名稱要維持 Golem_ 開頭，Unity 的安裝工具靠名稱對應到 Boss 的招式）
  4. 匯出到 Unity：選取 GolemRig 和 Golem_Body → File → Export → FBX
       Apply Scalings = FBX All、Forward = -Z、Up = Y、
       取消勾選 Add Leaf Bones、勾選 Bake Animation → All Actions

⚠ 重新執行這個腳本會「重建整隻石像和所有 Golem_ 動作」。
  如果你已經在 Blender 裡手動調過動畫，就不要再執行腳本，改用 Edit Mode 直接改造型。

改造型的方法：
  - 想改某一塊石頭的大小 / 位置：改下面 BLOCKS 清單的數字再重新執行（最簡單）
  - 或在 Edit Mode 直接拉頂點（每塊石頭是一個獨立的區塊，選一個面按 L 可以選取整塊）

角色面向 -Y（Blender 的前視圖看到的是正面），單位是公尺，總高約 6.5m，跟遊戲裡的 Boss 一樣大。
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
#  石塊：(骨頭, 中心位置, 大小, 材質, 額外旋轉(度))
#  side=True 的會自動鏡像到右邊（骨頭名稱加 .L / .R）
# ---------------------------------------------------------------------------
BLOCKS_CENTER = [
    # 骨盆、腰、胸（胸口很寬，像大猩猩一樣前傾）
    ("hips",  (0, 0.05, 2.75),  (2.4, 1.4, 0.8),  "stone", (0, 0, 0)),
    ("spine", (0, 0.1, 3.6),    (2.1, 1.5, 1.0),  "stone_dark", (0, 0, 0)),
    ("chest", (0, -0.05, 4.75), (3.4, 2.0, 1.6),  "stone", (0, 0, 0)),
    ("chest", (0, 0.75, 5.45),  (2.2, 1.0, 0.7),  "stone_dark", (12, 0, 0)),   # 背上的隆起
    ("chest", (0.55, 1.05, 4.6), (0.9, 0.6, 1.0), "stone_dark", (0, 0, 8)),    # 背後碎石
    ("chest", (-0.6, 1.0, 4.2), (0.8, 0.5, 0.8),  "stone", (0, 0, -6)),
    # 胸口的核心水晶（光束從這裡射出）＋往外裂開的發光裂紋
    ("chest", (0, -1.07, 4.7),  (0.55, 0.18, 0.55), "core", (0, 45, 0)),
    ("chest", (0.62, -1.06, 4.98), (0.75, 0.05, 0.06), "core", (0, -28, 0)),
    ("chest", (-0.66, -1.06, 4.45), (0.85, 0.05, 0.06), "core", (0, -22, 0)),
    ("chest", (-0.35, -1.06, 5.15), (0.45, 0.05, 0.05), "core", (0, 55, 0)),
    # 頭：小小的、往前低垂，一條發光的眼縫
    ("head",  (0, -0.75, 5.75), (1.0, 1.0, 0.85), "stone_dark", (0, 0, 0)),
    ("head",  (0, -1.27, 5.8),  (0.72, 0.06, 0.14), "eye", (0, 0, 0)),
    ("head",  (0, -0.55, 6.25), (0.6, 0.6, 0.25), "stone", (0, 0, 4)),        # 頭頂碎石
]
BLOCKS_SIDE = [
    ("shoulder",  (1.95, 0, 5.3),    (1.3, 1.5, 1.0),  "stone_dark", (0, -10, 0)),  # 肩甲
    ("shoulder",  (2.05, 0.15, 5.9), (0.7, 0.8, 0.4),  "stone", (0, -18, 0)),       # 肩上的小石
    ("upper_arm", (2.35, 0, 4.25),   (1.0, 1.0, 1.6),  "stone", (0, 0, 0)),
    ("forearm",   (2.55, -0.1, 2.65), (1.2, 1.2, 1.5), "stone_dark", (0, 0, 0)),
    ("forearm",   (2.5, -0.72, 2.75), (0.1, 0.05, 0.9), "core", (0, 12, 0)),        # 前臂的發光紋路
    ("thigh",     (0.85, -0.52, 2.1), (0.08, 0.05, 0.6), "core", (0, -10, 0)),       # 大腿的發光紋路
    ("hand",      (2.65, -0.3, 1.25), (1.5, 1.6, 0.9), "stone", (0, 0, 0)),         # 拍地用的大手掌
    ("hand",      (2.65, -1.0, 0.95), (1.3, 0.5, 0.4), "stone_dark", (0, 0, 0)),    # 指節
    ("thigh",     (0.85, 0, 2.05),   (1.0, 1.0, 1.0),  "stone_dark", (0, 0, 0)),
    ("shin",      (0.9, 0, 1.15),    (0.85, 0.9, 1.1), "stone", (0, 0, 0)),
    ("foot",      (0.9, -0.2, 0.3),  (1.0, 1.4, 0.6),  "stone_dark", (0, 0, 0)),
]

BEVEL_WIDTH = 0.07      # 石塊邊緣的倒角，越大越圓潤
JITTER_DEGREES = 5.0    # 每塊石頭隨機歪一點，看起來像堆出來的
SIZE_JITTER = 0.06      # 每塊石頭大小隨機 ±6%
RANDOM_SEED = 7


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
        "stone":      make_material("M_GolemStone", (0.25, 0.235, 0.22)),
        "stone_dark": make_material("M_GolemStoneDark", (0.14, 0.13, 0.13)),
        "eye":        make_material("M_GolemEye", (1.0, 0.8, 0.25), (1.0, 0.75, 0.2), 8.0, 0.4),
        "core":       make_material("M_GolemCore", (1.0, 0.35, 0.1), (1.0, 0.3, 0.05), 10.0, 0.3),
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


def add_block(bm, groups, deform, bone, center, size, mat_index, extra_rot, rng):
    """在 bmesh 裡加一塊有倒角的石頭，並 100% 綁到 bone"""
    before = set(bm.faces)
    res = bmesh.ops.create_cube(bm, size=1.0)
    verts = res["verts"]

    glow = mat_index >= 2  # 眼睛、核心、紋路不要歪，保持俐落
    j = 0.0 if glow else JITTER_DEGREES
    jitter = Euler([math.radians(rng.uniform(-j, j)) for _ in range(3)])
    if not glow:
        k = 1.0 + rng.uniform(-SIZE_JITTER, SIZE_JITTER)
        size = (size[0] * k, size[1] * (1.0 + rng.uniform(-SIZE_JITTER, SIZE_JITTER)), size[2] * k)
    rot = Euler([math.radians(a) for a in extra_rot]).to_matrix() @ jitter.to_matrix()
    for v in verts:
        p = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
        v.co = rot @ p + Vector(center)

    faces = list({f for v in verts for f in v.link_faces})
    edges = list({e for v in verts for e in v.link_edges})
    bmesh.ops.bevel(bm, geom=verts + edges + faces, offset=BEVEL_WIDTH,
                    segments=1, affect="EDGES", profile=0.5)

    all_faces = set(bm.faces) - before   # 這一塊石頭（含倒角）的所有面
    all_verts = {v for f in all_faces for v in f.verts}
    for f in all_faces:
        f.material_index = mat_index
    gi = groups[bone]
    for v in all_verts:
        v[deform][gi] = 1.0


def build_body(col, rig, mats):
    rng = random.Random(RANDOM_SEED)
    mesh = bpy.data.meshes.new(BODY_NAME)
    body = bpy.data.objects.new(BODY_NAME, mesh)
    col.objects.link(body)

    mat_order = ["stone", "stone_dark", "eye", "core"]
    for key in mat_order:
        mesh.materials.append(mats[key])

    # 每根骨頭一個 vertex group
    groups = {}
    for b in rig.data.bones:
        groups[b.name] = body.vertex_groups.new(name=b.name).index

    bm = bmesh.new()
    deform = bm.verts.layers.deform.verify()

    for bone, c, s, m, r in BLOCKS_CENTER:
        add_block(bm, groups, deform, bone, c, s, mat_order.index(m), r, rng)
    for side in ("L", "R"):
        for bone, c, s, m, r in BLOCKS_SIDE:
            rr = r if side == "L" else (r[0], -r[1], -r[2])
            add_block(bm, groups, deform, mirror_name(bone, side), mirror_vec(c, side), s,
                      mat_order.index(m), rr, rng)

    bm.to_mesh(mesh)
    bm.free()
    for poly in mesh.polygons:
        poly.use_smooth = False  # 石頭要平面著色，才有一塊一塊的稜角感

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
