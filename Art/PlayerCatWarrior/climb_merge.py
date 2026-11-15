"""Tripo 攀爬 → CW_Climb（取代 PlayerCatWarrior_AirClimb.fbx 裡原本手擺的版本）
Tripo 沒有牆和平台：腳把平台當成膝蓋高（約 0.30），手撐在比較高的地方（約 0.5）。
這裡把平台上緣定在手撐的高度 E：先上去的那隻腳變成踩在牆上蹬，最後站起來那段整個人再往上抬 LIFT，站到平台上。
輸出的原點 = 爬完站好時兩腳中間、平台上緣的高度（CalligraphyPlatformClimb 把根節點放在這裡），平台邊緣在原點後方 EDGE_BEHIND。
用法：python3.11 climb_merge.py -- AirClimb.fbx tripo_climb.fbx out.fbx [F0 F1 E EDGE_Y LIFT_F0 LIFT_F1]"""
import bpy, sys
from mathutils import Matrix, Vector
a=sys.argv[sys.argv.index('--')+1:]; ac, src_path, out = a[:3]
F0=int(a[3]) if len(a)>3 else 1; F1=int(a[4]) if len(a)>4 else 80
E=float(a[5]) if len(a)>5 else 0.55           # 平台上緣在 Tripo 座標的高度（Tripo 的地面 = 0；手一開始勾在這裡、撐起來時壓在這裡）
EDGE_Y=float(a[6]) if len(a)>6 else -0.48      # 平台邊緣（Tripo 座標，角色往 -y 走）
L0=int(a[7]) if len(a)>7 else 30; L1=int(a[8]) if len(a)>8 else 66   # 這段時間把人往上抬
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src_path)
src=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
sc=bpy.context.scene
P=lambda n: src.matrix_world@src.pose.bones[n].head
sc.frame_set(F1); bpy.context.view_layer.update()
feet=(P('Left_Foot')+P('Right_Foot'))/2; sole=min(P('Left_Foot').z,P('Right_Foot').z)
sc.frame_set(F0); bpy.context.view_layer.update(); sole0=min(P('Left_Foot').z,P('Right_Foot').z)
LIFT=E+sole0-sole   # 站好時腳底要剛好在 E（一開始站在地面上，腳踝骨頭比腳底高 sole0）
print('FEET',tuple(round(x,3) for x in feet),'ANKLE0',round(sole0,3),'LIFT',round(LIFT,3),'EDGE_BEHIND',round(EDGE_Y-feet.y,3))
def smooth(t): t=max(0.0,min(1.0,t)); return t*t*(3-2*t)
frames=[]
for f in range(F0,F1+1):
    sc.frame_set(f); bpy.context.view_layer.update()
    off=LIFT*smooth((f-L0)/(L1-L0))
    T=Matrix.Translation(Vector((-feet.x,-feet.y,-E+off)))
    frames.append({b.name:Matrix.LocRotScale((T@src.matrix_world@b.matrix).translation,(src.matrix_world@b.matrix).to_quaternion(),None) for b in src.pose.bones})
# ---- 腳不要穿進牆：膝蓋跑進石頭（平台上緣以下、牆面裡面）的那幾格，把那隻腳慢慢拉直、垂在牆面外（撐起來時腳垂著的姿勢）----
EB=EDGE_Y-feet.y   # 輸出座標裡牆面的位置（y < EB 是石頭裡面，z < 0 是平台上緣以下）
def desc(name):
    out=[]; b=src.pose.bones[name]
    def w(x): out.append(x.name); [w(c) for c in x.children]
    for c in b.children: w(c)
    return out
def fix_leg(side):
    U,K,F=side+'_UpperLeg',side+'_LowerLeg',side+'_Foot'
    kids=desc(F)
    raw=[]
    for fr in frames:
        k=fr[K].translation
        raw.append(max(0.0,(EB-k.y))/0.06 if k.z<-0.04 else 0.0)
    n=len(raw); wmax=[max(raw[max(0,i-4):i+5]) for i in range(n)]
    wt=[min(1.0,sum(wmax[max(0,i-4):i+5])/len(wmax[max(0,i-4):i+5])) for i in range(n)]
    for i,fr in enumerate(frames):
        w=smooth(wt[i])
        if w<=1e-4: continue
        H=fr[U].translation.copy(); Kp=fr[K].translation.copy(); Fp=fr[F].translation.copy()
        a=(Kp-H).length; b=(Fp-Kp).length
        # 目標：腳在膝蓋正下方附近、貼著牆面外側，腿幾乎伸直
        tgt=Vector((Fp.x, max(EB+0.035, H.y), 0)); dz=(0.96*(a+b))**2-((tgt.x-H.x)**2+(tgt.y-H.y)**2)
        tgt.z=H.z-(dz**0.5 if dz>0 else 0.0)
        T2=Fp.lerp(tgt,w)
        d=min((T2-H).length,(a+b)*0.999); nrm=(T2-H).normalized()
        x=(a*a-b*b+d*d)/(2*d); h=max(0.0,a*a-x*x)**0.5
        pole=(Kp-H)-nrm*(Kp-H).dot(nrm)
        if pole.length<1e-5: pole=Vector((0,-1,0))-nrm*(-nrm.y)
        K2=H+nrm*x+pole.normalized()*h
        T2=H+nrm*d
        def setrot(bn,old_dir,new_dir,pos):
            m=fr[bn]; q=old_dir.rotation_difference(new_dir)@m.to_quaternion()
            fr[bn]=Matrix.LocRotScale(pos,q,None)
        setrot(U,Kp-H,K2-H,H)
        setrot(K,Fp-Kp,T2-K2,K2)
        q=fr[F].to_quaternion(); fr[F]=Matrix.LocRotScale(T2,q,None)
        for c in kids: fr[c]=Matrix.LocRotScale(fr[c].translation+(T2-Fp),fr[c].to_quaternion(),None)
    print('LEGFIX',side,'frames',sum(1 for x in wt if x>1e-4),'max',round(max(raw)*0.06,3))
fix_leg('Left'); fix_leg('Right')

# ---- 重新分配時間：讓身體高度上升得比較平均（Tripo 原本是先上升、停在邊緣 20 格、最後一口氣站上去，看起來卡卡的）----
# 動畫進度 = TW×高度進度 + (1-TW)×時間進度，等間隔重新取樣（跟翻滾的做法一樣）
TW=float(a[9]) if len(a)>9 else 0.7
hz=[fr['Hips'].translation.z for fr in frames]
mono=[]; best=-1e9
for z in hz: best=max(best,z); mono.append(best)
hn=[(x-mono[0])/max(1e-6,mono[-1]-mono[0]) for x in mono]
n=len(frames); wv=[TW*hn[i]+(1-TW)*i/(n-1) for i in range(n)]
def inv(t):
    for i in range(n-1):
        if wv[i+1]>=t: return i+(t-wv[i])/max(1e-6,wv[i+1]-wv[i])
    return n-1.0
new=[]
for k in range(n):
    fi=inv(k/(n-1)); i=min(int(fi),n-2); al=fi-i
    A,B=frames[i],frames[i+1]
    new.append({b:Matrix.LocRotScale(A[b].translation.lerp(B[b].translation,al),A[b].to_quaternion().slerp(B[b].to_quaternion(),al),None) for b in A})
frames=new
z2=[fr['Hips'].translation.z for fr in frames]; v=[z2[i+1]-z2[i] for i in range(n-1)]
print('RISE max %.4f mean %.4f'%(max(v),(z2[-1]-z2[0])/(n-1)))
print('TIMEMAP',' '.join('%d:%.3f'%(f,wv[f-F0]) for f in (10,24,44,46,54,56,57,64,67)))

for o in list(bpy.data.objects): bpy.data.objects.remove(o, do_unlink=True)
for x in list(bpy.data.actions): bpy.data.actions.remove(x)
bpy.ops.import_scene.fbx(filepath=ac)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
for x in bpy.data.actions: x.name=x.name.split('|')[-1]; x.use_fake_user=True
old=bpy.data.actions.get('CW_Climb')
if old: bpy.data.actions.remove(old)
for b in rig.pose.bones: b.rotation_mode='QUATERNION'
act=bpy.data.actions.new('CW_Climb'); act.use_fake_user=True
rig.animation_data_create(); inv_w=rig.matrix_world.inverted()
order=[]
def walk(b): order.append(b); [walk(c) for c in b.children]
for b in rig.pose.bones:
    if b.parent is None: walk(b)
prev={}
for k,fr in enumerate(frames):
    rig.animation_data.action=None
    for b in rig.pose.bones: b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
    bpy.context.view_layer.update()
    for b in order:
        if b.name in fr: b.matrix=inv_w@fr[b.name]; bpy.context.view_layer.update()
    for b in rig.pose.bones:
        b.scale=(1,1,1); q=b.rotation_quaternion
        if b.name in prev and prev[b.name].dot(q)<0: b.rotation_quaternion=-q
        prev[b.name]=b.rotation_quaternion.copy()
    rig.animation_data.action=act
    for b in rig.pose.bones: b.keyframe_insert('rotation_quaternion',frame=k+1); b.keyframe_insert('location',frame=k+1)
rig.animation_data.action=act; err=0
for k in range(0,len(frames),5):
    sc.frame_set(k+1); bpy.context.view_layer.update()
    for b in rig.pose.bones:
        if b.name in frames[k]: err=max(err,((rig.matrix_world@b.matrix).translation-frames[k][b.name].translation).length)
sc.frame_set(1); bpy.context.view_layer.update()
W=lambda n: rig.matrix_world@rig.pose.bones[n].head
leg=(W('Left_UpperLeg')-W('Left_LowerLeg')).length+(W('Left_LowerLeg')-W('Left_Foot')).length
print('MAXERR',err,'FRAMES',len(frames),'LEGLEN',round(leg,4))
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
