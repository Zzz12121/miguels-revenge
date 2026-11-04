"""匯出黑貓（網格 + 骨架 + BC_Idle / BC_Gesture）給 Unity：python blackcat_export.py -- new.blend BlackCat.fbx"""
import bpy, sys
src, dst = sys.argv[sys.argv.index('--') + 1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
for a in list(bpy.data.actions):
    if not a.name.startswith('BC_'): bpy.data.actions.remove(a)
rig = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
rig.animation_data_create(); rig.animation_data.action = None
for b in rig.pose.bones:
    b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
bpy.context.view_layer.update()
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.data.materials.clear()          # 材質在 Unity 裡用選單建立（URP Lit）
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE', 'MESH'): o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.context.scene.render.fps = 30
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE', 'MESH'}, apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, armature_nodetype='NULL', mesh_smooth_type='FACE')
