"""把 PlayerCatWarrior_AirClimb.fbx 裡的動畫（攀爬除外）整段水平平移，讓第一格的髖部落在 TARGET：
跟待機動畫（Tripo 動作庫 standing_relax，Unity 以重心對齊）在遊戲裡的髖部位置一樣。
Unity 那邊這些動畫用「Based Upon Original」，所以切換跳躍 / 落地 / 翻滾 / 揮刀時身體不會前後左右偏。
TARGET 是用 JumpDriftProbe 量出來的：待機時髖部在角色的 右 1.5cm、後 0.19cm，模型 1 單位 ≈ 172cm（Blender：角色面向 -Y、右邊是 -X）。
用法：python3.11 recenter_clips.py -- in.fbx out.fbx"""
import bpy, sys
from mathutils import Vector
a=sys.argv[sys.argv.index('--')+1:]; src,out=a[0],a[1]
TARGET=Vector((-1.50/172.0, 0.19/172.0))
SKIP={'CW_Climb'}
bpy.ops.wm.read_factory_settings(use_empty=True); bpy.ops.import_scene.fbx(filepath=src)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for x in bpy.data.actions: x.name=x.name.split('|')[-1]; x.use_fake_user=True
rig.animation_data_create(); sc=bpy.context.scene
hb=rig.data.bones['Hips']
to_loc=(rig.matrix_world.to_3x3()@hb.matrix_local.to_3x3()).inverted()
for act in bpy.data.actions:
    if act.name in SKIP: continue
    rig.animation_data.action=act
    f0=int(act.frame_range[0]); sc.frame_set(f0); bpy.context.view_layer.update()
    h=rig.matrix_world@rig.pose.bones['Hips'].head
    d=Vector((TARGET.x-h.x, TARGET.y-h.y, 0.0))
    dl=to_loc@d
    curves=[c for c in act.fcurves if c.data_path=='pose.bones["Hips"].location']
    if len(curves)!=3: print('NOLOC',act.name,len(curves)); continue
    for c in curves:
        for k in c.keyframe_points:
            k.co.y+=dl[c.array_index]; k.handle_left.y+=dl[c.array_index]; k.handle_right.y+=dl[c.array_index]
        c.update()
    sc.frame_set(f0); bpy.context.view_layer.update()
    h2=rig.matrix_world@rig.pose.bones['Hips'].head
    print('SHIFT %-12s by (%.4f %.4f) -> hips (%.4f %.4f)'%(act.name,d.x,d.y,h2.x,h2.y))
rig.animation_data.action=None
for b in rig.pose.bones: b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects: o.select_set(True)
bpy.context.view_layer.objects.active=rig
sc.render.fps=24
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE','EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
