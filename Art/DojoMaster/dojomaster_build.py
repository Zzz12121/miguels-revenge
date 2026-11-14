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
