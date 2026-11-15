"""在 Tripo 的貓戰士骨架上手擺「居合低伏預備」姿勢，存成動畫（一個定住的姿勢），並輸出預覽圖
用法：python3.11 pose_iai.py -- tripo.fbx out.fbx preview_prefix [params.json]"""
import bpy, sys, math, json
from mathutils import Vector, Matrix, Quaternion
a=sys.argv[sys.argv.index('--')+1:]; src,out,prev=a[0],a[1],a[2]
P=dict(hipsDrop=.27, hipsFwd=-.05, hipsPitch=20, twist=18, chest=13, upper=13, neck=4, head=10,
       rFoot=(-.17,-.43,.095), lFoot=(-.03,.25,.10), lKneeLow=True,
       rHand=(.10,-.10,.06), lHand=(.14,-.02,.02), rAcross=(-.3,-.85,.45), rPalm=(.5,0,-.8), lAcross=(.3,.85,-.45), lPalm=(-.6,0,-.6), curl=72, curlSign=1, rElbowPole=(-1,-.2,.6), lElbowPole=(1,.6,.2))
if len(a)>3: P.update(json.load(open(a[3])))
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
sc=bpy.context.scene; act=rig.animation_data.action
sc.frame_set(1); bpy.context.view_layer.update()
pb=rig.pose.bones; W=rig.matrix_world; inv=W.inverted()
base={b.name:b.matrix_basis.copy() for b in pb}
grip22={}
sc.frame_set(22); bpy.context.view_layer.update()
for b in pb:
    if any(k in b.name for k in ('Thumb','Index','Middle','Ring','Pinky')): grip22[b.name]=b.matrix_basis.copy()
rig.animation_data.action=None
for b in pb: b.rotation_mode='QUATERNION'; b.matrix_basis=base[b.name]
bpy.context.view_layer.update()
def wm(b): return W@b.matrix
def setw(b,M): b.matrix=inv@M; bpy.context.view_layer.update()
def rotw(b,axis,deg,pivot=None):
    M=wm(b); p=M.translation if pivot is None else pivot
    R=Matrix.Translation(p)@Matrix.Rotation(math.radians(deg),4,Vector(axis).normalized())@Matrix.Translation(-p)
    setw(b,R@M)
def rotq(b,q,pivot):
    M=wm(b); R=Matrix.Translation(pivot)@q.to_matrix().to_4x4()@Matrix.Translation(-pivot); setw(b,R@M)
def head(b): return wm(b).translation.copy()
def tailw(b): return W@b.tail
X,Y,Z=Vector((1,0,0)),Vector((0,1,0)),Vector((0,0,1))
foot0={n:wm(pb[n]).to_quaternion() for n in ('Left_Foot','Right_Foot','Left_Toes','Right_Toes')}
# 1) 臀部往下、往前，骨盆前傾
h=pb['Hips']; M=wm(h); M.translation+=Vector((0,P['hipsFwd'],-P['hipsDrop'])); setw(h,M)
rotw(h,X,P['hipsPitch'])
# 2) 上身：往左扭（右肩往前）、往前彎；頭稍微抬回來看前方地面
rotw(pb['Spine'],Z,P['twist']); rotw(pb['Chest'],X,P['chest']); rotw(pb['UpperChest'],X,P['upper'])
rotw(pb['Neck'],X,P['neck']); rotw(pb['Head'],X,P['head'])
# 兩段 IK：把 upper 轉到指向膝蓋/手肘，再把 lower 轉到指向目標
def ik(upper,lower,end,target,pole):
    A=head(upper); L1=(head(lower)-A).length; L2=(head(end)-head(lower)).length
    T=Vector(target); d=T-A; dist=min(d.length,L1+L2-1e-4); dn=d.normalized()
    cosA=(L1*L1+dist*dist-L2*L2)/(2*L1*dist); ang=math.acos(max(-1,min(1,cosA)))
    pl=Vector(pole); pl=(pl-dn*pl.dot(dn)).normalized()
    knee=A+(dn*math.cos(ang)+pl*math.sin(ang))*L1
    cur=(head(lower)-A).normalized(); rotq(upper,cur.rotation_difference((knee-A).normalized()),A)
    # 讓關節彎曲平面對準 pole：繞上段骨頭自己的軸轉，使下段的方向落在平面上
    B=head(lower); cur2=(head(end)-B).normalized(); want=(T-B).normalized()
    rotq(lower,cur2.rotation_difference(want),B)
def setworldrot(b,q):
    M=wm(b); setw(b,Matrix.LocRotScale(M.translation,q,M.to_scale()))
hp=head(h)
# 3) 腳：右腳往前深蹲、左腳往後拉長，膝蓋貼近地面
ik(pb['Right_UpperLeg'],pb['Right_LowerLeg'],pb['Right_Foot'],P['rFoot'],(0,-1,.3))
ik(pb['Left_UpperLeg'],pb['Left_LowerLeg'],pb['Left_Foot'],P['lFoot'],(0,-.4,-1))
setworldrot(pb['Right_Foot'],foot0['Right_Foot']); setworldrot(pb['Right_Toes'],foot0['Right_Toes'])
# 後腳：腳尖踩地、腳跟抬起（腳掌往下）
lf=pb['Left_Foot']; cur=(tailw(lf)-head(lf)).normalized(); rotq(lf,cur.rotation_difference(Vector((0,-.25,-1)).normalized()),head(lf))
lt=pb['Left_Toes']; cur=(tailw(lt)-head(lt)).normalized(); rotq(lt,cur.rotation_difference(Vector((0,-1,-.05)).normalized()),head(lt))
# 4) 手：都在左腰（刀柄 / 刀鞘口），相對於骨盆的位置
hp=head(h)
rT=hp+Vector(P['rHand']); lT=hp+Vector(P['lHand'])
ik(pb['Right_UpperArm'],pb['Right_LowerArm'],pb['Right_Hand'],rT,P['rElbowPole'])
ik(pb['Left_UpperArm'],pb['Left_LowerArm'],pb['Left_Hand'],lT,P['lElbowPole'])
# 手掌方向：右手握刀柄（拳頭朝前下）、左手握刀鞘
def aimhand(b,dirv):
    cur=(tailw(b)-head(b)).normalized(); rotq(b,cur.rotation_difference(Vector(dirv).normalized()),head(b))
FING=('Index','Middle','Ring','Pinky')
def hand_basis(side):
    hd=head(pb[side+'_Hand']); ip=head(pb[side+'_IndexProximal']); pp=head(pb[side+'_PinkyProximal']); mp=head(pb[side+'_MiddleProximal'])
    across=(pp-ip).normalized(); fdir=(mp-hd).normalized()
    n=fdir.cross(across).normalized()
    th=head(pb[side+'_ThumbIntermediate'])-hd
    if th.dot(n)<0: n=-n          # 掌心那一側 = 大拇指那一側
    n=n*P['curlSign']
    return across,n
def basis(a,p):
    a=Vector(a).normalized(); p=(Vector(p)-a*Vector(p).dot(a)).normalized(); return Matrix((a,p,a.cross(p))).transposed()
def orient_hand(side,across_w,palm_w):
    ac,pn=hand_basis(side); R=basis(across_w,palm_w)@basis(ac,pn).transposed()
    rotq(pb[side+'_Hand'],R.to_quaternion(),head(pb[side+'_Hand']))
def fist(side,deg):
    ac,pn=hand_basis(side)
    for f in FING:
        b0=pb[side+'_'+f+'Proximal']; d0=(tailw(b0)-head(b0)).normalized()
        ax=d0.cross(pn).normalized()      # 每根手指固定一個彎曲軸（往掌心彎）
        for seg,k in (('Proximal',1),('Intermediate',1.1),('Distal',.8)):
            b=pb[side+'_'+f+seg]; rotw(b,ax,deg*k,head(b))
    t0=pb[side+'_ThumbProximal']; d0=(tailw(t0)-head(t0)).normalized(); ax=d0.cross(pn).normalized()
    for seg,k in (('Proximal',.35),('Intermediate',.45),('Distal',.45)):
        b=pb[side+'_Thumb'+seg]; rotw(b,ax,deg*k,head(b))
orient_hand('Right',P['rAcross'],P['rPalm']); orient_hand('Left',P['lAcross'],P['lPalm'])
fist('Right',P['curl']); fist('Left',P['curl'])
# 報告：腳離地多高
for n in ('Left_Foot','Right_Foot','Left_ToesEnd','Right_ToesEnd','Left_LowerLeg','Right_LowerLeg','Right_Hand','Left_Hand','Head'):
    print('POS',n,tuple(round(x,3) for x in head(pb[n])))
# 存成動畫：兩格一樣的姿勢
for b in pb: b.rotation_mode='QUATERNION'
newact=bpy.data.actions.new('Iai'); rig.animation_data.action=newact
for f in (1,30):
    for b in pb: b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('location',frame=f)
if act: bpy.data.actions.remove(act)
# 預覽
cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; cam.ortho_scale=.95; co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'
sc.render.resolution_x=420; sc.render.resolution_y=420
bpy.ops.mesh.primitive_plane_add(size=4,location=(0,0,0))
c=Vector((-.1,-.08,.3))
views={'hands':Vector((1.4,-.7,.15)),'handsF':Vector((-.3,-1.6,.1)),'front34':Vector((-1.6,-2.4,.9)),'side':Vector((-3,0,.25)),'ref':Vector((1.2,-1.8,2.0)),'back34':Vector((1.8,2.2,.8))}
sc.frame_set(1)
for k,v in views.items():
    cam.ortho_scale=.4 if k.startswith('hands') else .95
    cc=(head(pb['Right_Hand'])+head(pb['Left_Hand']))/2 if k.startswith('hands') else c
    co.location=cc+v; co.rotation_euler=(cc-co.location).to_track_quat('-Z','Y').to_euler()
    sc.render.filepath=f'{prev}_{k}.png'; bpy.ops.render.render(write_still=True)
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type in ('ARMATURE','MESH') and o.name!='Plane': o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.fbx(filepath=out,use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,
    bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True)
