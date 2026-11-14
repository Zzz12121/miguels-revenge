# 用法：python player_anim_merge_tripo.py -- PlayerCatWarrior_AirClimb.fbx <Tripo 匯出的動畫 FBX> 輸出.fbx [動畫名稱，預設 CW_AirDash]
# Tripo 動畫必須是同一套骨架（貓戰士的 Tripo 骨架）；會轉成指定名稱的動畫加進 PlayerCatWarrior_AirClimb.fbx，原本的動畫不變。
"""把 Tripo 做的 AirDash 動畫（同一套骨架）轉成 CW_AirDash，加進 PlayerCatWarrior_AirClimb.fbx 裡（世界空間逐格對齊，跟骨架縮放無關）"""
import bpy, sys
from mathutils import Matrix
args=sys.argv[sys.argv.index('--')+1:]; ac, ad, out = args[:3]; NEWNAME = args[3] if len(args) > 3 else 'CW_AirDash'
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=ad)
src=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]; src.name='SRC'
sact=bpy.data.actions[0]; f0,f1=[int(x) for x in sact.frame_range]
sc=bpy.context.scene
names=[b.name for b in src.data.bones]
frames={}
src.animation_data.action=sact
for f in range(f0,f1+1):
    sc.frame_set(f); bpy.context.view_layer.update()
    frames[f]={}
    for b in src.pose.bones:
        m=src.matrix_world @ b.matrix
        frames[f][b.name]=Matrix.LocRotScale(m.translation, m.to_quaternion(), None)  # 去掉骨架的 0.01 縮放
# 清掉來源，匯入目標
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
bpy.ops.import_scene.fbx(filepath=ac)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for a in bpy.data.actions: a.name=a.name.split('|')[-1]; a.use_fake_user=True
print('OBJECTS',[(o.name,o.type) for o in bpy.data.objects]); print('ACTIONS',[a.name for a in bpy.data.actions])
for b in rig.pose.bones: b.rotation_mode='QUATERNION'
act=bpy.data.actions.new(NEWNAME); act.use_fake_user=True
rig.animation_data_create()
inv=rig.matrix_world.inverted()
order=[]
def walk(b):
    order.append(b); [walk(c) for c in b.children]
for b in rig.pose.bones:
    if b.parent is None: walk(b)
prev={}
for f in range(f0,f1+1):
    rig.animation_data.action=None
    for b in rig.pose.bones: b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
    bpy.context.view_layer.update()
    for b in order:
        if b.name not in frames[f]: continue
        b.matrix = inv @ frames[f][b.name]
        bpy.context.view_layer.update()
    for b in rig.pose.bones:
        b.scale=(1,1,1)
        q=b.rotation_quaternion
        if b.name in prev and prev[b.name].dot(q)<0: b.rotation_quaternion=-q
        prev[b.name]=b.rotation_quaternion.copy()
    rig.animation_data.action=act
    for b in rig.pose.bones:
        b.keyframe_insert('rotation_quaternion',frame=f-f0+1); b.keyframe_insert('location',frame=f-f0+1)
# 檢查：每一格骨頭的世界位置誤差
rig.animation_data.action=act; err=0
for f in range(f0,f1+1,4):
    sc.frame_set(f-f0+1); bpy.context.view_layer.update()
    for b in rig.pose.bones:
        if b.name in frames[f]: err=max(err,((rig.matrix_world@b.matrix).translation-frames[f][b.name].translation).length)
print('MAXERR',err,'FRAMES',f1-f0+1)
rig.animation_data.action=None
for b in rig.pose.bones: b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
bpy.context.view_layer.update()
for o in list(bpy.data.objects):
    if o.type=='MESH': bpy.data.objects.remove(o,do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects: o.select_set(True)
bpy.context.view_layer.objects.active=rig
sc.render.fps=24
bpy.ops.export_scene.fbx(filepath=out, use_selection=True, object_types={'ARMATURE','EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
