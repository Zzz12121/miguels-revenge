"""Tripo 翻滾 → CW_Roll：取一段、重新分配時間（身體前進的進度 ≈ 動畫進度）、拿掉整體位移（原地翻滾），加進 PlayerCatWarrior_AirClimb.fbx"""
import bpy, sys
from mathutils import Matrix, Vector
args=sys.argv[sys.argv.index('--')+1:]; ac, src_path, out = args[:3]
F0=int(args[3]) if len(args)>3 else 12; F1=int(args[4]) if len(args)>4 else 60; N=int(args[5]) if len(args)>5 else 49
TRAVEL_WEIGHT=0.65   # 動畫進度 = 0.65*前進比例 + 0.35*時間比例（Unity 端用同一個公式）
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src_path)
src=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
sc=bpy.context.scene
def hips_at(f, sub=0.0):
    sc.frame_set(f, subframe=sub); bpy.context.view_layer.update()
    return src.matrix_world @ src.pose.bones['Hips'].head
H={f:hips_at(f) for f in range(F0,F1+1)}
D=H[F1]-H[F0]; D.z=0
dirv=D.normalized(); L=D.length
s=[]; best=0
for f in range(F0,F1+1):
    d=H[f]-H[F0]; v=max(0.0,min(1.0,d.dot(dirv)/L)); best=max(best,v); s.append(best)
s=[x/s[-1] for x in s]
w=[TRAVEL_WEIGHT*s[i]+(1-TRAVEL_WEIGHT)*i/(F1-F0) for i in range(len(s))]
print('TRAVEL',round(L,3),'DIR',tuple(round(x,3) for x in dirv))
print('W', [round(x,2) for x in w])
def inv(target):
    for i in range(len(w)-1):
        if w[i+1]>=target:
            a=(target-w[i])/max(1e-6,w[i+1]-w[i]); return F0+i+a
    return float(F1)
frames=[]
for k in range(N):
    tw=k/(N-1); ff=inv(tw); fi=int(ff); sub=ff-fi
    if fi>=F1: fi,sub=F1,0.0
    sc.frame_set(fi, subframe=sub); bpy.context.view_layer.update()
    off=Vector((D.x*tw, D.y*tw, 0))
    T=Matrix.Translation(-off)
    fr={}
    for b in src.pose.bones:
        m=T @ src.matrix_world @ b.matrix
        fr[b.name]=Matrix.LocRotScale(m.translation, m.to_quaternion(), None)
    frames.append(fr)
    if k%6==0: h=fr['Hips'].translation; print('K',k,'src',round(ff,2),'hips',tuple(round(x,3) for x in h))
for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
for a in list(bpy.data.actions): bpy.data.actions.remove(a)
bpy.ops.import_scene.fbx(filepath=ac)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for a in bpy.data.actions: a.name=a.name.split('|')[-1]; a.use_fake_user=True
old=bpy.data.actions.get('CW_Roll')
if old: bpy.data.actions.remove(old)
print('ACTIONS',[a.name for a in bpy.data.actions])
for b in rig.pose.bones: b.rotation_mode='QUATERNION'
act=bpy.data.actions.new('CW_Roll'); act.use_fake_user=True
rig.animation_data_create()
inv_w=rig.matrix_world.inverted()
order=[]
def walk(b):
    order.append(b); [walk(c) for c in b.children]
for b in rig.pose.bones:
    if b.parent is None: walk(b)
prev={}
for k,fr in enumerate(frames):
    rig.animation_data.action=None
    for b in rig.pose.bones: b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
    bpy.context.view_layer.update()
    for b in order:
        if b.name not in fr: continue
        b.matrix = inv_w @ fr[b.name]
        bpy.context.view_layer.update()
    for b in rig.pose.bones:
        b.scale=(1,1,1)
        q=b.rotation_quaternion
        if b.name in prev and prev[b.name].dot(q)<0: b.rotation_quaternion=-q
        prev[b.name]=b.rotation_quaternion.copy()
    rig.animation_data.action=act
    for b in rig.pose.bones:
        b.keyframe_insert('rotation_quaternion',frame=k+1); b.keyframe_insert('location',frame=k+1)
rig.animation_data.action=act; err=0
for k in range(0,N,4):
    sc.frame_set(k+1); bpy.context.view_layer.update()
    for b in rig.pose.bones:
        if b.name in frames[k]: err=max(err,((rig.matrix_world@b.matrix).translation-frames[k][b.name].translation).length)
print('MAXERR',err,'FRAMES',N)
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
