"""幻影分身突進的三段動畫（Blender 手擺，Tripo 貓戰士骨架）：
  CW_PhantomDash    空中突進的姿勢（上身前壓、左膝往前頂、右腿往後伸、刀收在右後方）
  CW_PhantomSlash   突進斬：左腳踏出去煞車、轉腰由右往左橫砍、停住、收招站起
  CW_PhantomScissor 剪刀斬：壓低、右手從左腰拔刀由左往右砍過去、殘心收勢
姿勢用「角色座標」寫（公尺；x = 右、y = 上、z = 前），每個關鍵姿勢之間用 smoothstep 補間，手腳都用 IK。
遊戲裡刀的方向和（揮刀時的）左手是程式算的，這裡負責身體、腳、右手的位置。
用法：python3.11 phantom_pose.py -- tripo.fbx outdir [只做哪一段]"""
import bpy, sys, math, json, os
from mathutils import Vector, Matrix, Quaternion
a=sys.argv[sys.argv.index('--')+1:]; src,outdir=a[0],a[1]; only=a[2] if len(a)>2 else None
P=dict(curl=72)
U=1.72   # 1 Blender 單位 = 1.72 公尺
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
rig=[o for o in bpy.data.objects if o.type=='ARMATURE'][0]
sc=bpy.context.scene; srcact=rig.animation_data.action
print('FPS',sc.render.fps)
sc.frame_set(1); bpy.context.view_layer.update()
pb=rig.pose.bones; W=rig.matrix_world; inv=W.inverted()
base={b.name:b.matrix_basis.copy() for b in pb}
rig.animation_data.action=None
for b in pb: b.rotation_mode='QUATERNION'
def upd(): bpy.context.view_layer.update()
def wm(b): return W@b.matrix
def setw(b,M): b.matrix=inv@M; upd()
def rotw(b,axis,deg,pivot=None):
    M=wm(b); p=M.translation if pivot is None else pivot
    R=Matrix.Translation(p)@Matrix.Rotation(math.radians(deg),4,Vector(axis).normalized())@Matrix.Translation(-p)
    setw(b,R@M)
def rotq(b,q,pivot):
    M=wm(b); R=Matrix.Translation(pivot)@q.to_matrix().to_4x4()@Matrix.Translation(-pivot); setw(b,R@M)
def head(b): return wm(b).translation.copy()
def tailw(b): return W@b.tail
def setworldrot(b,q):
    M=wm(b); setw(b,Matrix.LocRotScale(M.translation,q,M.to_scale()))
def ik(upper,lower,end,target,pole):
    A=head(upper); L1=(head(lower)-A).length; L2=(head(end)-head(lower)).length
    T=Vector(target); d=T-A; dist=min(d.length,L1+L2-1e-4); dn=d.normalized()
    cosA=(L1*L1+dist*dist-L2*L2)/(2*L1*dist); ang=math.acos(max(-1,min(1,cosA)))
    pl=Vector(pole); pl=(pl-dn*pl.dot(dn)).normalized()
    knee=A+(dn*math.cos(ang)+pl*math.sin(ang))*L1
    cur=(head(lower)-A).normalized(); rotq(upper,cur.rotation_difference((knee-A).normalized()),A)
    B=head(lower); cur2=(head(end)-B).normalized(); want=(T-B).normalized()
    rotq(lower,cur2.rotation_difference(want),B)
FING=('Index','Middle','Ring','Pinky')
def hand_basis(side):
    hd=head(pb[side+'_Hand']); ip=head(pb[side+'_IndexProximal']); pp=head(pb[side+'_PinkyProximal']); mp=head(pb[side+'_MiddleProximal'])
    across=(pp-ip).normalized(); fdir=(mp-hd).normalized(); n=fdir.cross(across).normalized()
    if (head(pb[side+'_ThumbIntermediate'])-hd).dot(n)<0: n=-n
    return across,n,fdir
def basis(a,p):
    a=Vector(a).normalized(); p=(Vector(p)-a*Vector(p).dot(a)).normalized(); return Matrix((a,p,a.cross(p))).transposed()
def orient_hand(side,across_w,palm_w):
    ac,pn,_=hand_basis(side); R=basis(across_w,palm_w)@basis(ac,pn).transposed()
    rotq(pb[side+'_Hand'],R.to_quaternion(),head(pb[side+'_Hand']))
def curl(side,deg,thumb=.6):
    ac,pn,_=hand_basis(side)
    for f in FING:
        b0=pb[side+'_'+f+'Proximal']; d0=(tailw(b0)-head(b0)).normalized(); ax=d0.cross(pn).normalized()
        for seg,k in (('Proximal',1),('Intermediate',1.1),('Distal',.8)): rotw(pb[side+'_'+f+seg],ax,deg*k,head(pb[side+'_'+f+seg]))
    t0=pb[side+'_ThumbProximal']; d0=(tailw(t0)-head(t0)).normalized(); ax=d0.cross(pn).normalized()
    for seg,k in (('Proximal',.35),('Intermediate',.45),('Distal',.45)): rotw(pb[side+'_Thumb'+seg],ax,deg*k*thumb,head(pb[side+'_Thumb'+seg]))
X,Z=Vector((1,0,0)),Vector((0,0,1))
for b in pb: b.matrix_basis=base[b.name]
upd()
foot0={n:wm(pb[n]).to_quaternion() for n in ('Left_Foot','Right_Foot')}
ank0={n:head(pb[n]) for n in ('Left_Foot','Right_Foot')}
# 站姿：兩腳稍微張開、左腳稍微在前（左肩對著目標）

hp0=head(pb['Hips'])
O=Vector((hp0.x,hp0.y,0))
def C(x,y,z): return Vector((-x/U,-z/U,y/U))          # 角色座標（公尺）→ Blender 向量
def Rz(deg): return Matrix.Rotation(math.radians(deg),3,'Z')
def make_upright():
    v=head(pb['Head'])-head(pb['Hips'])
    # 只修前後方向的歪斜（基本姿勢骨盆往前推）
    rotw(pb['Spine'],X,math.degrees(math.atan2(v.y,v.z)))
def pose(k):
    """k：hips=(x,y,z) 公尺、yaw 髖部轉向（正 = 往左轉）、twist 上身再多轉、lean 前傾、
    lf/rf=(x,y,z,腳尖外轉,腳跟抬起) 腳踝位置（y = 離地）、rh/lh 手相對胸口（跟著上身轉）、head 頭多轉回來的比例"""
    for b in pb: b.matrix_basis=base[b.name]
    upd()
    make_upright()
    h=pb['Hips']; M=wm(h); M.translation=O+C(*k['hips']); setw(h,M)
    rotw(h,Z,k['yaw'])
    for side,key,sx in (('Left','lf',-1),('Right','rf',1)):
        x,y,z,toe,heel=k[key]
        tgt=O+C(x,y,z)+Vector((0,0,ank0[side+'_Foot'].z))
        fwd=Rz(toe*(-sx))@Vector((0,-1,0))
        ik(pb[side+'_UpperLeg'],pb[side+'_LowerLeg'],pb[side+'_Foot'],tgt,fwd+Vector((0,0,.25))+C(sx*.25,0,0)*U)
        q=Quaternion((0,0,1),math.radians(toe*(-sx)))@foot0[side+'_Foot']
        setworldrot(pb[side+'_Foot'],q)
        if heel: rotw(pb[side+'_Foot'],Rz(toe*(-sx))@Vector((1,0,0)),heel)   # 腳跟抬起（腳尖朝下）
    tyaw=k['yaw']+k['twist']
    lat=Rz(k['yaw'])@Vector((1,0,0))
    # 先扭腰、再往「前進方向」前傾（順序反過來的話，扭完之後前傾會歪到旁邊去）
    rotw(pb['Spine'],Z,k['twist']*.45); rotw(pb['Chest'],Z,k['twist']*.3); rotw(pb['UpperChest'],Z,k['twist']*.25)
    rotw(pb['Spine'],lat,k['lean']*.6,head(pb['Spine'])); rotw(pb['Chest'],lat,k['lean']*.4,head(pb['Chest']))
    # 頭：看前方（把身體轉的、前傾的轉回來大部分）
    hl=k.get('head',.85)
    rotw(pb['Neck'],Z,-tyaw*hl*.4); rotw(pb['Head'],Z,-tyaw*hl*.6)
    rotw(pb['Neck'],X,-k['lean']*.35); rotw(pb['Head'],X,-k['lean']*.4)
    ch=head(pb['UpperChest']); T=Rz(tyaw)
    def torso(v): return ch+T@C(*v)
    ik(pb['Right_UpperArm'],pb['Right_LowerArm'],pb['Right_Hand'],torso(k['rh']),T@C(*k.get('rpole',(1,-.6,-.4))))
    ik(pb['Left_UpperArm'],pb['Left_LowerArm'],pb['Left_Hand'],torso(k['lh']),T@C(*k.get('lpole',(-1,-.6,-.4))))
    bl=T@C(*k.get('blade',(0,0,1)))
    orient_hand('Right',-bl,T@C(*k.get('rpalm',(0,-1,0)))); curl('Right',P['curl'])
    orient_hand('Left',T@C(*k.get('lacross',(0,0,-1))),T@C(*k.get('lpalm',(1,0,0)))); curl('Left',k.get('lcurl',30),.4)
def lerp(a,b,t):
    if isinstance(a,(tuple,list)): return tuple(x+(y-x)*t for x,y in zip(a,b))
    return a+(b-a)*t
def smooth(t): t=max(0.,min(1.,t)); return t*t*(3-2*t)
def keyall(f):
    for b in pb: b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('location',frame=f)
def blend(keys,f):
    """keys = [(格, 姿勢, ease)]；ease：'s' smoothstep、'in' 慢進快出（揮刀加速）、'out' 快進慢出"""
    if f<=keys[0][0]: return keys[0][1]
    for (f0,k0,_),(f1,k1,e) in zip(keys,keys[1:]):
        if f<=f1:
            t=(f-f0)/(f1-f0); t={'s':smooth(t),'in':t*t,'out':1-(1-t)*(1-t),'l':t}[e]
            out={}
            for n in k1:
                a_=k0.get(n,k1[n]); out[n]=lerp(a_,k1[n],t)
            return out
    return keys[-1][1]

# ---------------- 姿勢 ----------------
DASH=dict(hips=(0,.86,0),yaw=-22,twist=-18,lean=58,
    lf=(-.10,.42,.30,0,25), rf=(.11,.38,-.60,8,62),
    rh=(.30,-.34,-.22), lh=(-.20,-.22,.30), blade=(.3,-.2,-.9), rpalm=(-1,0,0), lcurl=20, lpalm=(0,-1,0), lacross=(-1,0,0))
SLASH=[
 (0, dict(hips=(0,.80,0),yaw=-28,twist=-24,lean=46, lf=(-.12,.16,.44,0,10), rf=(.12,.02,-.46,12,48),
          rh=(.30,.04,-.10), lh=(-.18,-.12,.30), blade=(1,0,-.2), rpalm=(0,-1,0), lcurl=20, lpalm=(0,-1,0), lacross=(-1,0,0)),'s'),
 (3, dict(hips=(0,.76,.03),yaw=-34,twist=-30,lean=44, lf=(-.13,.07,.50,0,0), rf=(.12,0,-.48,14,44),
          rh=(.32,.07,-.15), lh=(-.16,-.10,.32), blade=(.9,0,-.5), rpalm=(0,-1,0), lcurl=20, lpalm=(0,-1,0), lacross=(-1,0,0)),'s'),
 (9, dict(hips=(-.02,.66,.12),yaw=24,twist=40,lean=38, lf=(-.14,0,.52,-6,0), rf=(.14,0,-.56,20,40),
          rh=(.05,.02,.48), lh=(-.40,-.10,-.24), blade=(-.9,.05,.4), rpalm=(0,-1,0), lcurl=25, lpalm=(0,-1,0), lacross=(-1,0,0)),'in'),
 (11,dict(hips=(-.02,.64,.13),yaw=27,twist=46,lean=40, lf=(-.14,0,.52,-6,0), rf=(.14,0,-.56,20,42),
          rh=(-.01,0,.48), lh=(-.42,-.12,-.26), blade=(-1,.05,.25), rpalm=(0,-1,0), lcurl=25, lpalm=(0,-1,0), lacross=(-1,0,0)),'out'),
 (24,dict(hips=(0,.84,.14),yaw=8,twist=10,lean=10, lf=(-.13,0,.50,-6,0), rf=(.15,0,-.12,12,0),
          rh=(.26,-.44,.10), lh=(-.24,-.46,.02), blade=(.15,-.6,.8), rpalm=(-1,0,0), lcurl=30, lpalm=(1,0,0), lacross=(0,0,-1)),'s'),
]
SCISSOR=[
 (0, dict(hips=(0,.72,0),yaw=30,twist=24,lean=52, lf=(-.12,0,-.50,-14,46), rf=(.12,0,.46,0,0),
          rh=(-.17,-.36,.17), lh=(-.23,-.42,.04), blade=(-.35,-.25,-.9), rpalm=(0,-1,0), lcurl=60, lpalm=(1,0,0), lacross=(0,0,-1),
          rpole=(1,-.3,-.6), lpole=(-1,-.2,-.8)),'s'),
 (3, dict(hips=(0,.68,.02),yaw=34,twist=30,lean=55, lf=(-.12,0,-.52,-14,48), rf=(.12,0,.48,0,0),
          rh=(-.18,-.36,.15), lh=(-.23,-.42,.03), blade=(-.35,-.25,-.9), rpalm=(0,-1,0), lcurl=60, lpalm=(1,0,0), lacross=(0,0,-1),
          rpole=(1,-.3,-.6), lpole=(-1,-.2,-.8)),'s'),
 (9, dict(hips=(0,.60,.14),yaw=-20,twist=-44,lean=50, lf=(-.14,0,-.62,-18,52), rf=(.14,0,.58,6,0),
          rh=(.12,.04,.50), lh=(-.26,-.40,-.12), blade=(.9,.05,.3), rpalm=(0,-1,0), lcurl=60, lpalm=(1,0,0), lacross=(0,0,-1),
          rpole=(1,-.5,-.3), lpole=(-1,-.2,-.8)),'in'),
 (11,dict(hips=(0,.58,.15),yaw=-23,twist=-50,lean=52, lf=(-.14,0,-.62,-18,52), rf=(.14,0,.58,6,0),
          rh=(.20,.02,.47), lh=(-.26,-.40,-.14), blade=(1,.05,.1), rpalm=(0,-1,0), lcurl=60, lpalm=(1,0,0), lacross=(0,0,-1),
          rpole=(1,-.5,-.3), lpole=(-1,-.2,-.8)),'out'),
 (24,dict(hips=(0,.66,.12),yaw=-14,twist=-32,lean=36, lf=(-.14,0,-.58,-18,46), rf=(.14,0,.56,6,0),
          rh=(.36,-.26,.10), lh=(-.25,-.42,-.08), blade=(.8,-.5,-.2), rpalm=(0,-1,0), lcurl=60, lpalm=(1,0,0), lacross=(0,0,-1),
          rpole=(1,-.5,-.3), lpole=(-1,-.2,-.8)),'s'),
]
# 空中側閃（跳 → 翻滾鍵）：大跨步飛躍——左腿往前伸、右腿往後蹬直、上身前壓，手臂跟腿反方向前後擺開（右手在前、左手在後）
def _air(h,lean,lf,rf,rh,lh,tw):
    return dict(hips=(0,h,0),yaw=0,twist=tw,lean=lean, lf=lf, rf=rf, rh=rh, lh=lh, head=.9,
                blade=(0,0,1), rpalm=(-1,0,0), lcurl=72, lpalm=(1,0,0), lacross=(0,0,-1), rpole=(1,-.8,-.2), lpole=(-1,-.3,-.8))
AIRDASH=[
 (0, _air(.86,26,(-.10,.36,.22,0,20),(.11,.34,-.30,6,50),(.20,-.20,.24),(-.26,-.28,-.14),8),'s'),
 (3, _air(.88,46,(-.10,.46,.58,0,12),(.11,.46,-.70,6,66),(.20,-.02,.40),(-.32,-.16,-.36),16),'out'),
 (10,_air(.88,48,(-.10,.44,.56,0,14),(.11,.48,-.72,6,68),(.20,-.04,.40),(-.33,-.15,-.38),16),'s'),
]
clips={'CW_AirDash':AIRDASH,'CW_PhantomDash':[(0,DASH,'s'),(1,DASH,'s')],'CW_PhantomSlash':SLASH,'CW_PhantomScissor':SCISSOR}
cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.render.resolution_x=300; sc.render.resolution_y=300
bpy.ops.mesh.primitive_plane_add(size=5,location=(O.x,O.y,0)); plane=bpy.context.active_object
objs=[o for o in bpy.data.objects if o.type in ('ARMATURE','MESH') and o is not plane]
VIEWS={'f34':Vector((1.5,-2.2,.8)),'side':Vector((-3,0,.3)),'top':Vector((0,-.6,3))}
def render(name,frames):
    c=O+Vector((0,0,.42)); cam.ortho_scale=1.35
    for f in frames:
        sc.frame_set(f+1)
        for k,v in VIEWS.items():
            co.location=c+v; co.rotation_euler=(c-co.location).to_track_quat('-Z','Y').to_euler()
            sc.render.filepath=os.path.join(outdir,f'{name}_{k}_{f}.png'); bpy.ops.render.render(write_still=True)
for name,keys in clips.items():
    if only and name!=only: continue
    act=bpy.data.actions.new(name); rig.animation_data.action=act
    N=keys[-1][0]
    for f in range(N+1):
        pose(blend(keys,f)); keyall(f+1)
    sc.frame_start,sc.frame_end=1,N+1
    render(name,[k[0] for k in keys] if N>1 else [0])
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir,name+'.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
        bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True)
    print('DONE',name)
