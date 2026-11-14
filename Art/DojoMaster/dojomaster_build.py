"""
道場老師父（DojoMaster）：把 Tripo 匯出的動畫（說話 / 跪坐起身 / 走路 / 跪坐喝茶）合成一個 FBX，順便修好長袍的蒙皮。
用法（Blender 4.2 / bpy）：python dojomaster_build.py -- talk.fbx sitstand.fbx walk.fbx 輸出.fbx [sitdrink.fbx]
蒙皮問題：Tripo 自動綁骨架時，把長袍下襬（站姿時剛好在手旁邊）也綁到手指骨頭上，
手一動袍子就被拉成一大片。這裡把「離手臂骨頭太遠」的手臂權重拿掉，改用附近正常頂點的權重補上。
"""
import bpy, sys
from mathutils import Vector, kdtree
_args = sys.argv[sys.argv.index('--') + 1:]
talk, sitstand, walk, out = _args[:4]
sitdrink = _args[4] if len(_args) > 4 else None
ARM_KEYS = ('Shoulder', 'Arm', 'Hand', 'Thumb', 'Index', 'Middle', 'Ring', 'Pinky')
MAX_ARM_DIST = 0.15   # 手臂骨頭的影響範圍（模型身高約 0.9）：超過這個距離的頂點不該跟著手動

def is_arm(n): return any(k in n for k in ARM_KEYS)

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(1e-9, ab.length_squared)))
    return (p - (a + ab * t)).length

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=talk)
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
mesh = [o for o in bpy.data.objects if o.type == 'MESH'][0]
bpy.data.actions[0].name = 'DM_Talk'
for name, path in (('DM_SitStand', sitstand), ('DM_Walk', walk)) + ((('DM_SitDrinkRaw', sitdrink),) if sitdrink else ()):
    before = set(bpy.data.objects); before_act = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=path)
    act = [a for a in bpy.data.actions if a not in before_act][0]
    act.name = name
    for o in [o for o in bpy.data.objects if o not in before]:
        bpy.data.objects.remove(o, do_unlink=True)
for a in bpy.data.actions: a.use_fake_user = True

# ---- 走路改成原地走 ----
# Tripo 的走路是「真的往前走」（髖部 5 秒走了約 3.5 公尺），在 Unity 裡每循環一次就會瞬間彈回原點。
# 把髖部水平方向的直線位移扣掉，只留左右 / 上下的晃動；往前移動交給演出程式（DojoOpeningCutscene）。
walk_act = bpy.data.actions['DM_Walk']
for fc in walk_act.fcurves:
    if fc.data_path != 'pose.bones["Hips"].location' or len(fc.keyframe_points) < 2: continue
    k0, k1 = fc.keyframe_points[0], fc.keyframe_points[-1]
    f0, v0, f1, v1 = k0.co.x, k0.co.y, k1.co.x, k1.co.y
    if abs(v1 - v0) < 5.0: continue   # 單位是公分：沒有明顯漂移的軸不動
    for k in fc.keyframe_points:
        d = (v1 - v0) * (k.co.x - f0) / (f1 - f0)
        k.co.y -= d; k.handle_left.y -= d; k.handle_right.y -= d
    fc.update()
    print('WALK in place: Hips location[%d] drift %.1f removed' % (fc.array_index, v1 - v0))

# ---- 跪坐喝茶：剪成可以循環的一段 ----
# Tripo 的喝茶動畫是「一邊坐下一邊端起茶杯」（第 1～55 格在坐下），坐穩之後只剩「喝一口 → 放下」（第 81～117 格）。
# 重新排成：坐著端在腹前（停一下）→ 端到嘴邊（第 117 → 81 格倒著播）→ 喝（停一下）→ 放回腹前（81 → 117），頭尾同一個姿勢。
# 轉折的地方用 smoothstep 放慢，不會突然停住。
if sitdrink:
    raw = bpy.data.actions['DM_SitDrinkRaw']
    REST, SIP = 117.0, 81.0
    seq = []                                   # 每一格對應原本動畫的第幾格（可以是小數）
    seq += [REST] * 30                         # 端在腹前 1.25 秒
    for i in range(36):                        # 端起來 1.5 秒
        u = (i + 1) / 36; e = u * u * (3 - 2 * u); seq.append(REST + (SIP - REST) * e)
    seq += [SIP] * 14                          # 喝 0.6 秒
    for i in range(40):                        # 放下 1.7 秒
        u = (i + 1) / 40; e = u * u * (3 - 2 * u); seq.append(SIP + (REST - SIP) * e)
    drink = bpy.data.actions.new('DM_SitDrink'); drink.use_fake_user = True
    for fc in raw.fcurves:
        nf = drink.fcurves.new(fc.data_path, index=fc.array_index, action_group=fc.group.name if fc.group else '')
        nf.keyframe_points.add(len(seq))
        for i, src in enumerate(seq):
            k = nf.keyframe_points[i]; k.co = (i + 1, fc.evaluate(src)); k.interpolation = 'LINEAR'
        nf.update()
    bpy.data.actions.remove(raw)
    print('SITDRINK frames', len(seq))

# ---- 修蒙皮 ----
mw = rig.matrix_world
bones = {b.name: (mw @ b.head_local, mw @ b.tail_local) for b in rig.data.bones}
groups = {g.index: g.name for g in mesh.vertex_groups}
world = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
fixed, broken = 0, []
for v in mesh.data.vertices:
    p = world[v.index]
    bad = [g.group for g in v.groups if is_arm(groups[g.group]) and g.weight > 0
           and groups[g.group] in bones and seg_dist(p, *bones[groups[g.group]]) > MAX_ARM_DIST]
    if not bad: continue
    for gi in bad: mesh.vertex_groups[gi].remove([v.index])
    fixed += 1
    broken.append(v.index)
broken_set = set(broken)
clean = [v.index for v in mesh.data.vertices if v.index not in broken_set and not any(is_arm(groups[g.group]) for g in v.groups if g.weight > 0)]
tree = kdtree.KDTree(len(clean))
for i, vi in enumerate(clean): tree.insert(world[vi], i)
tree.balance()
for vi in broken:
    acc = {}
    near = tree.find_n(world[vi], 8)
    tot = 0.0
    for co, i, d in near:
        w = 1.0 / max(d, 1e-4); tot += w
        for g in mesh.data.vertices[clean[i]].groups:
            acc[g.group] = acc.get(g.group, 0.0) + g.weight * w
    for g in list(mesh.data.vertices[vi].groups): mesh.vertex_groups[g.group].remove([vi])
    s = sum(acc.values())
    for gi, w in acc.items():
        if w / s > 0.01: mesh.vertex_groups[gi].add([vi], w / s, 'REPLACE')
print('WEIGHTS fixed vertices', fixed)

# ---- 頭髮、衣服加骨架（彈簧骨，Unity 裡由 SpringBoneSystem 讓它們晃動）----
# Tripo 的模型是一整塊網格（頭髮、長袍跟身體黏在一起），所以依「位置 / 網格的島」選區域，
# 把那一區的權重沿著新加的骨頭串分配（根部還是跟原本的骨頭，越往末端越跟新骨頭）。
# 座標是模型的 rest pose（面向 -Y、身高約 1.0）。
import math, numpy as np
SB_ORIG = -1
me = mesh.data
V = np.array([tuple(world[i]) for i in range(len(me.vertices))])

# 網格的島（相連的一塊一塊）
_par = list(range(len(me.vertices)))
def _f(a):
    while _par[a] != a: _par[a] = _par[_par[a]]; a = _par[a]
    return a
for e in me.edges:
    a, b2 = _f(e.vertices[0]), _f(e.vertices[1])
    if a != b2: _par[a] = b2
ISL = np.array([_f(i) for i in range(len(me.vertices))])
_u, _c = np.unique(ISL, return_counts=True)
def island_at(cond):
    """回傳中心符合條件的島（用 bbox 判斷）"""
    out = []
    for r in _u:
        pts = V[ISL == r]
        if cond(pts.min(0), pts.max(0), len(pts)): out.append(r)
    return out
MAIN = _u[np.argmax(_c)]

chains = []   # (名稱前綴, 父骨頭, [關節世界座標...（最後一個是末端）])
def chain(name, parent, joints): chains.append((name, parent, [Vector(j) for j in joints]))

# 1. 後面的長髮：從後腦往下到背
chain('SB_HairBack', 'Head', [(0.002, 0.035, 0.90), (0.002, 0.062, 0.845), (0.002, 0.072, 0.79), (0.002, 0.07, 0.725)])
# 2. 臉旁兩束頭髮（兩個小島）
SIDE_L = island_at(lambda mn, mx, n: 0.015 < (mn[0] + mx[0]) / 2 < 0.07 and mx[2] > 0.93 and mn[2] < 0.84 and n > 50)
SIDE_R = island_at(lambda mn, mx, n: -0.07 < (mn[0] + mx[0]) / 2 < -0.015 and mx[2] > 0.93 and mn[2] < 0.84 and n > 50)
def side_chain(name, isl):
    if not isl: return
    pts = np.concatenate([V[ISL == r] for r in isl]); cx, cy = pts[:, 0].mean(), pts[:, 1].mean()
    top, bot = pts[:, 2].max(), pts[:, 2].min()
    chain(name, 'Head', [(cx, cy, top - 0.035), (cx, cy, (top + bot) / 2 - 0.01), (cx, cy, bot + 0.012), (cx, cy, bot - 0.02)])
side_chain('SB_HairL', SIDE_L); side_chain('SB_HairR', SIDE_R)
# 3. 外袍的寬袖子：從前臂往下垂的那片布
SB_SLEEVE = {}
for side, bn in (('L', 'Left_LowerArm'), ('R', 'Right_LowerArm')):
    h, t = bones[bn]
    mid = h.lerp(t, 0.35)
    chain('SB_Sleeve' + side, bn, [tuple(mid + Vector((0, 0.02, -0.01))), tuple(mid + Vector((0.01 if side == 'L' else -0.01, 0.055, -0.065))), tuple(mid + Vector((0.02 if side == 'L' else -0.02, 0.075, -0.13)))])
    SB_SLEEVE[side] = (h, t)
# 4. 長袍下襬：繞腰一圈 8 串，每串 3 節（腰 → 膝 → 小腿 → 下襬）
HIP = Vector((0.002, -0.025, 0.0))
SKIRT_Z = [0.47, 0.34, 0.21, 0.08]
SKIRT_ANG = [-180, -135, -90, -45, 0, 45, 90, 135]
skirt_pts = V[(ISL == MAIN) & (V[:, 2] < 0.5)]
def skirt_radius(ang, z):
    d = skirt_pts[:, :2] - np.array([HIP.x, HIP.y]); a = np.degrees(np.arctan2(d[:, 1], d[:, 0])); rr = np.linalg.norm(d, axis=1)
    sel = (np.abs(((a - ang + 180) % 360) - 180) < 22) & (np.abs(skirt_pts[:, 2] - z) < 0.05) & (rr < 0.3)
    return float(np.percentile(rr[sel], 80)) - 0.015 if sel.sum() > 3 else 0.12
for ang in SKIRT_ANG:
    rad = math.radians(ang)
    joints = [(HIP.x + math.cos(rad) * skirt_radius(ang, z), HIP.y + math.sin(rad) * skirt_radius(ang, z), z) for z in SKIRT_Z]
    chain('SB_Skirt%+04d' % ang, 'Hips', joints)

# ---- 建立骨頭 ----
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
inv = rig.matrix_world.inverted()
eb = rig.data.edit_bones
made = {}
for name, parent, joints in chains:
    prev = eb[parent]
    for i in range(len(joints) - 1):
        bname = f'{name}_{i + 1}'
        b2 = eb.new(bname); b2.head = inv @ joints[i]; b2.tail = inv @ joints[i + 1]
        b2.parent = prev; b2.use_connect = i > 0; b2.use_deform = True
        prev = b2
    # 末端骨（不影響網格，只是讓 Unity 知道最後一節有多長）
    e = eb.new(f'{name}_End'); e.head = inv @ joints[-1]; e.tail = inv @ (joints[-1] + (joints[-1] - joints[-2]) * 0.3)
    e.parent = prev; e.use_connect = True; e.use_deform = False
    made[name] = [f'{name}_{i + 1}' for i in range(len(joints) - 1)]
bpy.ops.object.mode_set(mode='OBJECT')
for b2 in rig.pose.bones: b2.rotation_mode = 'QUATERNION'

# ---- 權重 ----
def along(p, joints):
    """點投影在骨頭串上的位置：0 = 第一個關節、i = 第 i 個關節（可以 < 0 或 > 節數）、以及離串的距離"""
    best = (1e9, 0.0)
    for i in range(len(joints) - 1):
        a, b2 = joints[i], joints[i + 1]; ab = b2 - a
        t = (p - a).dot(ab) / max(1e-9, ab.length_squared)
        tc = max(0.0, min(1.0, t)) if 0 < i < len(joints) - 2 else (min(1.0, t) if i == 0 and len(joints) > 2 else (max(0.0, t) if i == len(joints) - 2 and len(joints) > 2 else t))
        d = (p - (a + ab * max(0.0, min(1.0, t)))).length
        if d < best[0]: best = (d, i + tc)
    return best
def kernel(t, n):
    """t → 原本的骨頭 + 每一節的權重（三角形分配，相鄰兩節之間平滑過渡）"""
    centers = [-0.5] + [i + 0.5 for i in range(n)]
    w = [max(0.0, 1.0 - abs(t - c)) for c in centers]
    if t <= -0.5: w = [1.0] + [0.0] * n
    if t >= n - 0.5: w = [0.0] * n + [1.0]
    s = sum(w); return [x / s for x in w]
add = {}   # 頂點 → {骨頭: 權重}，加上 'ORIG' 的保留比例
def assign(vi, chain_name, factor, t):
    names = made[chain_name]; k = kernel(t, len(names))
    d = add.setdefault(vi, {'ORIG': 0.0}); d['_f'] = d.get('_f', 0.0) + factor
    d['ORIG'] += k[0] * factor
    for nm, w in zip(names, k[1:]):
        if w > 0: d[nm] = d.get(nm, 0.0) + w * factor
jt = {name: joints for name, parent, joints in chains}
HEAD_ISL = island_at(lambda mn, mx, n: n > 300 and mn[2] > 0.7 and mx[2] < 0.97 and mx[2] > 0.95)
SASH = island_at(lambda mn, mx, n: mx[2] < 0.4 and mn[2] < 0.1 and mx[1] < -0.06 and abs((mn[0] + mx[0]) / 2) < 0.04)
FLAPS = island_at(lambda mn, mx, n: n < 40 and mx[2] < 0.4 and mn[2] > 0.12 and 0.12 < abs((mn[0] + mx[0]) / 2) < 0.2)
for vi in range(len(V)):
    p = Vector(V[vi]); isl = ISL[vi]
    # 後面長髮：頭髮島裡後腦以下、在頭後面的部分 + 背後那一束小島
    if (isl in HEAD_ISL and p.z < 0.905 and p.y > 0.0) or (isl not in HEAD_ISL and isl != MAIN and p.y > 0.0 and 0.75 < p.z < 0.96 and abs(p.x) < 0.03 and len(V[ISL == isl]) < 60 and V[ISL == isl][:, 1].max() > 0.06):
        d, t = along(p, jt['SB_HairBack']); assign(vi, 'SB_HairBack', 1.0, t); continue
    if isl in SIDE_L: d, t = along(p, jt['SB_HairL']); assign(vi, 'SB_HairL', 1.0, t); continue
    if isl in SIDE_R: d, t = along(p, jt['SB_HairR']); assign(vi, 'SB_HairR', 1.0, t); continue
    if isl == MAIN and 0.46 < p.z < 0.72 and abs(p.x) > 0.14:
        side = 'L' if p.x > 0 else 'R'; h, tl = SB_SLEEVE[side]
        dist = seg_dist(p, h, tl)
        f = max(0.0, min(1.0, (dist - 0.03) / 0.04))          # 離前臂越遠（垂下來的布）越跟袖子的骨頭
        if f > 0: d, t = along(p, jt['SB_Sleeve' + side]); assign(vi, 'SB_Sleeve' + side, f, max(t, 0.0))
        continue
    skirt = (isl == MAIN and (p.z < 0.47 or (p.z < 0.54 and (Vector((p.x, p.y, 0)) - HIP).length < 0.15))) or isl in SASH or isl in FLAPS
    if skirt:
        a = math.degrees(math.atan2(p.y - HIP.y, p.x - HIP.x))
        t = (SKIRT_Z[0] - p.z) / (SKIRT_Z[0] - SKIRT_Z[1])
        for ang in SKIRT_ANG:
            da = abs(((a - ang + 180) % 360) - 180)
            if da < 45: assign(vi, 'SB_Skirt%+04d' % ang, 1.0 - da / 45.0, t)
for nm_list in made.values():
    for nm in nm_list:
        if nm not in mesh.vertex_groups: mesh.vertex_groups.new(name=nm)
gidx = {g.name: g.index for g in mesh.vertex_groups}
changed = 0
for vi, d in add.items():
    f = d.pop('_f'); keep = d.pop('ORIG') / max(f, 1e-9)       # 原本骨頭保留的比例
    new = {}
    for g in me.vertices[vi].groups:
        if g.weight > 0: new[g.group] = g.weight * keep
    for nm, w in d.items(): new[gidx[nm]] = new.get(gidx[nm], 0.0) + w / max(f, 1e-9)
    top = sorted(new.items(), key=lambda kv: -kv[1])[:4]           # Unity 一個頂點最多 4 根骨頭
    s = sum(w for _, w in top)
    for g in list(me.vertices[vi].groups): mesh.vertex_groups[g.group].remove([vi])
    for gi, w in top:
        if w / s > 0.005: mesh.vertex_groups[gi].add([vi], w / s, 'REPLACE')
    changed += 1
print('SPRINGBONES chains', len(chains), 'bones', sum(len(v) for v in made.values()), 'weighted vertices', changed)

# ---- 匯出 ----
rig.animation_data_create(); rig.animation_data.action = None
for b in rig.pose.bones: b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True); mesh.select_set(True); bpy.context.view_layer.objects.active = rig
bpy.context.scene.render.fps = 24
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, mesh_smooth_type='FACE', path_mode='STRIP')
print('ACTIONS', [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
