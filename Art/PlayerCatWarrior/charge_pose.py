"""在 Tripo 貓戰士骨架上手擺「蓄力舉刀」姿勢 + 四個方向的小碎步（原地循環），輸出每段一個 FBX 和預覽圖
用法：python3.11 charge_pose.py -- tripo.fbx outdir [render_only_clip]
右手把刀拉到頭的右側（刀尖朝前，遊戲裡程式會讓刀尖對準目標），左手往前伸指著目標，像拉弓"""
import bpy, sys, math, json, os
from mathutils import Vector, Matrix, Quaternion
a=sys.argv[sys.argv.index('--')+1:]; src,outdir=a[0],a[1]; only=a[2] if len(a)>2 else None
P=dict(hipsDrop=-.022, lean=2, twist=-22, chestTwist=-8, headTurn=26, headPitch=-4,
       rHandFromHead=(-.10,.05,.015), rPole=(-1,.4,.5), lReach=(.02,-.23,-.07), lPole=(.5,.1,-1),
       stanceWide=.06, stagger=.025, curl=72, step=.28, lift=.045, bob=.009, frames=48)
pj=os.path.join(outdir,'params.json')
if os.path.exists(pj): P.update(json.load(open(pj)))
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
# 腳放在髖部正下方（基本姿勢本來是腳張很開、膝蓋彎的），stanceWide = 每隻腳離中線多遠
stanceL=Vector((hp0.x+P['stanceWide'],hp0.y-P['stagger'],ank0['Left_Foot'].z))
stanceR=Vector((hp0.x-P['stanceWide'],hp0.y+P['stagger'],ank0['Right_Foot'].z))
def make_upright(extra):
    """把上身轉成直的（髖部到頭的連線垂直），再往前傾 extra 度"""
    v=head(pb['Head'])-head(pb['Hips'])
    rotw(pb['Spine'],X,math.degrees(math.atan2(v.y,v.z))+extra)

def pose_frame(hipsOffset, footL, footR):
    for b in pb: b.matrix_basis=base[b.name]
    upd()
    h=pb['Hips']; M=wm(h); M.translation+=Vector((0,0,-P['hipsDrop']))+hipsOffset; setw(h,M)
    ik(pb['Left_UpperLeg'],pb['Left_LowerLeg'],pb['Left_Foot'],footL[0],(.12,-1,0))
    ik(pb['Right_UpperLeg'],pb['Right_LowerLeg'],pb['Right_Foot'],footR[0],(-.12,-1,0))
    setworldrot(pb['Left_Foot'],footL[1]); setworldrot(pb['Right_Foot'],footR[1])
    # 上身：往前傾一點、左肩轉向前方（像拉弓），頭轉回來看前面
    make_upright(P['lean']); rotw(pb['Spine'],Z,P['twist']); rotw(pb['Chest'],Z,P['chestTwist'])
    rotw(pb['Head'],Z,P['headTurn']); rotw(pb['Head'],X,P['headPitch'])
    # 右手：握刀拉到頭的右側（刀尖朝前）；左手往前伸指著目標
    hd=head(pb['Head'])
    ik(pb['Right_UpperArm'],pb['Right_LowerArm'],pb['Right_Hand'],hd+Vector(P['rHandFromHead']),P['rPole'])
    sh=head(pb['Left_UpperArm'])
    ik(pb['Left_UpperArm'],pb['Left_LowerArm'],pb['Left_Hand'],sh+Vector(P['lReach']),P['lPole'])
    orient_hand('Right',(0,1,-.2),(1,0,-.35)); curl('Right',P['curl'])
    orient_hand('Left',(1,0,0),(0,0,-1)); curl('Left',10,.3)

def keyall(f):
    for b in pb: b.keyframe_insert('rotation_quaternion',frame=f); b.keyframe_insert('location',frame=f)

def foot_at(stance, phase, d, footq):
    # phase 0..1：前 60% 踩地（往後滑），後 40% 抬起來往前跨
    s=P['step']*(.6 if abs(d.x)>.5 else 1.); st=.6
    if phase<st:
        u=phase/st; off=s*(.5-u); up=0
    else:
        u=(phase-st)/(1-st); off=s*(-.5+u); up=P['lift']*math.sin(math.pi*u)
    return (stance+d*off+Vector((0,0,up)), footq)

def lerpv(a,b,t): return Vector(a).lerp(Vector(b),t)
def smooth(t): t=max(0.,min(1.,t)); return t*t*(3-2*t)
def pose_summon(r, breath):
    """轉刀：r=0 右手在胸前（握著刀）→ r=1 整隻手往前上方伸向刀（刀停在頭的前方 0.95m、上方 0.8m、右邊 0.4m），掌心對著刀；
    breath = 舉著的時候輕微呼吸（-1～1）"""
    for b in pb: b.matrix_basis=base[b.name]
    upd()
    h=pb['Hips']; M=wm(h); M.translation+=Vector((0,0,-P['hipsDrop'])); setw(h,M)
    ik(pb['Left_UpperLeg'],pb['Left_LowerLeg'],pb['Left_Foot'],stanceL,(.12,-1,0))
    ik(pb['Right_UpperLeg'],pb['Right_LowerLeg'],pb['Right_Foot'],stanceR,(-.12,-1,0))
    setworldrot(pb['Left_Foot'],foot0['Left_Foot']); setworldrot(pb['Right_Foot'],foot0['Right_Foot'])
    # 身體挺直、胸口稍微往後仰（往上看刀），舉手時右肩稍微往前
    make_upright(0); rotw(pb['Chest'],X,-4*r-1.2*breath); rotw(pb['UpperChest'],X,-3*r)
    rotw(pb['Spine'],Z,6*r)
    hd=head(pb['Head'])
    home=hd+Vector((-.23,-.55,.47))          # 刀停的位置（遊戲裡是公尺 /1.72）
    sh=head(pb['Right_UpperArm']); L=(head(pb['Right_LowerArm'])-sh).length+(head(pb['Right_Hand'])-head(pb['Right_LowerArm'])).length
    d=(home-sh+Vector((-.22,.02,.04))).normalized()   # 手往右外側舉一點，不要擋到臉（特寫鏡頭在右前方）
    raised=sh+d*L*.9+Vector((0,0,.004*breath))
    chest=head(pb['Chest'])+Vector((-.03,-.13,.05))
    tgt=lerpv(chest,raised,r)+Vector((0,-.05,.04))*math.sin(math.pi*r)   # 往前上方畫弧舉起來
    ik(pb['Right_UpperArm'],pb['Right_LowerArm'],pb['Right_Hand'],tgt,lerpv((-.6,.3,-1),(-1,.2,-.6),r))
    # 左手：自然放在身體旁邊，稍微張開
    lsh=head(pb['Left_UpperArm'])
    ik(pb['Left_UpperArm'],pb['Left_LowerArm'],pb['Left_Hand'],lsh+Vector((.06,.01,-.235)),(0,1,-.2))
    orient_hand('Left',(0,1,0),(-1,0,0)); curl('Left',28,.4)
    # 右手：一開始握拳（握刀）→ 舉起來時張開、掌心朝著刀、手指朝上
    up=(Vector((0,0,1))-d*d.z).normalized()
    orient_hand('Right',lerpv((0,1,-.2),d.cross(up),r),lerpv((1,0,-.35),d,r))
    curl('Right',P['curl']*(1-r)+10*r,.6*(1-r)+.2*r)

clips={'CW_VRSummon':'summon','CW_SwordAim':None,'CW_SwordStepF':Vector((0,-1,0)),'CW_SwordStepB':Vector((0,1,0)),
       'CW_SwordStepL':Vector((1,0,0)),'CW_SwordStepR':Vector((-1,0,0))}
cam=bpy.data.cameras.new('c'); cam.type='ORTHO'; co=bpy.data.objects.new('c',cam); sc.collection.objects.link(co); sc.camera=co
sc.render.engine='BLENDER_WORKBENCH'; sc.display.shading.light='STUDIO'; sc.render.resolution_x=360; sc.render.resolution_y=360
bpy.ops.mesh.primitive_plane_add(size=4,location=(0,0,0)); plane=bpy.context.active_object
objs=[o for o in bpy.data.objects if o.type in ('ARMATURE','MESH') and o is not plane]
def render(name,views,frame):
    sc.frame_set(frame); c=Vector((-.1,-.05,.45))
    for k,v in views.items():
        cam.ortho_scale=1.05; co.location=c+v; co.rotation_euler=(c-co.location).to_track_quat('-Z','Y').to_euler()
        sc.render.filepath=os.path.join(outdir,f'{name}_{k}_{frame}.png'); bpy.ops.render.render(write_still=True)
for name,d in clips.items():
    if only and name!=only: continue
    act=bpy.data.actions.new(name); rig.animation_data.action=act
    if d=='summon':
        N=32
        for f in range(N+1):
            r=smooth((f-4)/9)        # 第 4 → 13 格舉起來（技能裡是 0.13 → 0.4 那段）
            br=math.sin(2*math.pi*max(0,f-13)/19)
            pose_summon(r,br); keyall(f+1)
        sc.frame_start,sc.frame_end=1,N+1
        render(name,{'front34':Vector((-1.4,-2.2,.7)),'side':Vector((-3,0,.3))},2); render(name,{'front34':Vector((-1.4,-2.2,.7)),'side':Vector((-3,0,.3)),'back34':Vector((1.6,2.0,.9)),'cam2':Vector((-1.6,-2.3,.5)),'front':Vector((0,-3,.4))},20)
    elif d is None:
        pose_frame(Vector(),(stanceL,foot0['Left_Foot']),(stanceR,foot0['Right_Foot'])); keyall(1); keyall(2)
        sc.frame_start,sc.frame_end=1,2
    else:
        N=P['frames']
        for f in range(N+1):
            ph=f/N
            bob=Vector((0,0,-P['bob']*(1-math.cos(4*math.pi*ph))*.5))
            pose_frame(bob, foot_at(stanceL,ph%1,d,foot0['Left_Foot']), foot_at(stanceR,(ph+.5)%1,d,foot0['Right_Foot']))
            keyall(f+1)
        sc.frame_start,sc.frame_end=1,N+1
    if d is None: render(name,{'front34':Vector((-1.4,-2.2,.7)),'side':Vector((-3,0,.3)),'top':Vector((-.6,-1.2,2.4)),'back34':Vector((1.6,2.0,.9))},1)
    elif d!='summon': render(name,{'side':Vector((-3,0,.3)) if d.x==0 else Vector((0,-3,.3))},1); render(name,{'side':Vector((-3,0,.3)) if d.x==0 else Vector((0,-3,.3))},4)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.fbx(filepath=os.path.join(outdir,name+'.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,
        bake_anim=True,bake_anim_use_all_actions=False,bake_anim_use_nla_strips=False,bake_anim_force_startend_keying=True)
    print('DONE',name)
