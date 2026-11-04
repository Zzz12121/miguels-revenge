import bpy, sys
src, dst = sys.argv[sys.argv.index('--')+1:][:2]
bpy.ops.wm.open_mainfile(filepath=src)
for a in list(bpy.data.actions):
    if not a.name.startswith('CW_'): bpy.data.actions.remove(a)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for o in list(bpy.data.objects):
    if o.type=='MESH': bpy.data.objects.remove(o,do_unlink=True)
# 匯出時骨架停在 rest pose：FBX 裡每根骨頭的預設位置 = 原本模型的 rest（Unity 用它建立 Avatar）
rig.animation_data.action=None
for b in rig.pose.bones:
    b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
bpy.context.view_layer.update()
# 原本的模型最上層有兩個節點（Armature 和網格 tripo_node_b6817ade）。只有 Armature 一個的話，
# Unity 匯入時會把它併成模型本身，骨架階層就跟原本的 Avatar 對不上 → 放一個同名的空物件佔位
dummy=bpy.data.objects.new('tripo_node_b6817ade', None)
bpy.context.scene.collection.objects.link(dummy)
bpy.ops.object.select_all(action='DESELECT'); rig.select_set(True); dummy.select_set(True); bpy.context.view_layer.objects.active=rig
# 單位跟原本的模型一樣（FBX 記公尺、骨架不縮放）：不然骨架會被放大 100 倍，動畫套上去身體會飛出去
bpy.ops.export_scene.fbx(filepath=dst, use_selection=True, object_types={'ARMATURE','EMPTY'}, apply_scale_options='FBX_SCALE_UNITS',
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0, armature_nodetype='NULL')
