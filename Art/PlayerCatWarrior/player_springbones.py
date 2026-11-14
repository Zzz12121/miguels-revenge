"""
貓戰士（玩家）的頭髮、裙子彈簧骨資料產生器。
用法：python player_springbones.py -- <PlayerCatWarrior_TailRigged_GarmentFixed.asset> <輸出 PlayerCatWarrior_SpringBones.json>
（需要 numpy；不用 Blender）
從 Unity 的網格檔（文字格式的 .asset）讀頂點、權重、bindpose，選出頭髮（頭部骨頭帶動、在頭的後面 / 兩側、低於頭骨）
和裙子（腰到大腿中段、離大腿骨夠遠的頂點），沿著新加的骨頭串分配權重（根部保留原本的骨頭、越往末端越跟新骨頭）。
Unity 選單 GeminiGrowUp > 玩家 > 加上頭髮與裙子的彈簧骨 讀這個 JSON。
網格座標：y 朝上、+z 是角色正面。
"""
import re, math, json, numpy as np, zlib
import sys
A, OUT = sys.argv[sys.argv.index('--') + 1:][:2]
s=open(A).read()
vc=int(re.search(r'm_VertexCount: (\d+)',s).group(1))
data=bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)',s).group(1))
def al(x): return (x+15)//16*16
s0=0; s1=al(40*vc); s2=al(s1+8*vc)
a0=np.frombuffer(data[s0:s0+40*vc],np.uint8).reshape(vc,40)
pos=a0[:,0:12].copy().view(np.float32).reshape(vc,3)
a2=np.frombuffer(data[s2:s2+32*vc],np.uint8).reshape(vc,32)
bw=a2[:,0:16].copy().view(np.float32).reshape(vc,4); bi=a2[:,16:32].copy().view(np.uint32).reshape(vc,4)
idx=np.frombuffer(bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)',s).group(1)),np.uint16).reshape(-1,3)
# bindposes
bp_block=s[s.index('m_BindPose:'):s.index('m_IsReadable')] if 'm_BoneNameHashes' not in s[s.index('m_BindPose:'):s.index('m_IsReadable')] else None
blk=s[s.index('m_BindPose:'):s.index('m_BoneNameHashes')]
vals=re.findall(r'e(\d)(\d): ([-\d.eE+]+)',blk)
mats=[]; cur=np.zeros((4,4))
for k,(r,c,v) in enumerate(vals):
    cur[int(r),int(c)]=float(v)
    if r=='3' and c=='3': mats.append(cur.copy()); cur=np.zeros((4,4))
hx=re.search(r'm_BoneNameHashes: ([0-9a-f]+)',s).group(1)
hashes=[int.from_bytes(bytes.fromhex(hx[i:i+8]),'little') for i in range(0,len(hx),8)]
# 骨頭名稱：Unity 存的是「骨頭路徑」的 CRC32（例如 Hips/Spine/Chest），用 Tripo 骨架的路徑對回名字；尾巴骨是後來加的（Tail_01～10）
PATHS = ['Hips', 'Hips/Spine', 'Hips/Spine/Chest', 'Hips/Spine/Chest/UpperChest', 'Hips/Spine/Chest/UpperChest/Neck', 'Hips/Spine/Chest/UpperChest/Neck/Neck_Twist_A', 'Hips/Spine/Chest/UpperChest/Neck/Head', 'Hips/Spine/Chest/UpperChest/Neck/Head/Left_Eye', 'Hips/Spine/Chest/UpperChest/Neck/Head/Right_Eye', 'Hips/Spine/Chest/UpperChest/Left_Shoulder', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_ThumbProximal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_ThumbProximal/Left_ThumbIntermediate', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_ThumbProximal/Left_ThumbIntermediate/Left_ThumbDistal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_ThumbProximal/Left_ThumbIntermediate/Left_ThumbDistal/Left_ThumbDistalEnd', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_IndexProximal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_IndexProximal/Left_IndexIntermediate', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_IndexProximal/Left_IndexIntermediate/Left_IndexDistal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_IndexProximal/Left_IndexIntermediate/Left_IndexDistal/Left_IndexDistalEnd', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_MiddleProximal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_MiddleProximal/Left_MiddleIntermediate', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_MiddleProximal/Left_MiddleIntermediate/Left_MiddleDistal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_MiddleProximal/Left_MiddleIntermediate/Left_MiddleDistal/Left_MiddleDistalEnd', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_RingProximal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_RingProximal/Left_RingIntermediate', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_RingProximal/Left_RingIntermediate/Left_RingDistal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_RingProximal/Left_RingIntermediate/Left_RingDistal/Left_RingDistalEnd', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_PinkyProximal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_PinkyProximal/Left_PinkyIntermediate', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_PinkyProximal/Left_PinkyIntermediate/Left_PinkyDistal', 'Hips/Spine/Chest/UpperChest/Left_Shoulder/Left_UpperArm/Left_LowerArm/Left_Hand/Left_PinkyProximal/Left_PinkyIntermediate/Left_PinkyDistal/Left_PinkyDistalEnd', 'Hips/Spine/Chest/UpperChest/Right_Shoulder', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_ThumbProximal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_ThumbProximal/Right_ThumbIntermediate', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_ThumbProximal/Right_ThumbIntermediate/Right_ThumbDistal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_ThumbProximal/Right_ThumbIntermediate/Right_ThumbDistal/Right_ThumbDistalEnd', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_IndexProximal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_IndexProximal/Right_IndexIntermediate', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_IndexProximal/Right_IndexIntermediate/Right_IndexDistal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_IndexProximal/Right_IndexIntermediate/Right_IndexDistal/Right_IndexDistalEnd', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_MiddleProximal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_MiddleProximal/Right_MiddleIntermediate', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_MiddleProximal/Right_MiddleIntermediate/Right_MiddleDistal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_MiddleProximal/Right_MiddleIntermediate/Right_MiddleDistal/Right_MiddleDistalEnd', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_RingProximal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_RingProximal/Right_RingIntermediate', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_RingProximal/Right_RingIntermediate/Right_RingDistal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_RingProximal/Right_RingIntermediate/Right_RingDistal/Right_RingDistalEnd', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_PinkyProximal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_PinkyProximal/Right_PinkyIntermediate', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_PinkyProximal/Right_PinkyIntermediate/Right_PinkyDistal', 'Hips/Spine/Chest/UpperChest/Right_Shoulder/Right_UpperArm/Right_LowerArm/Right_Hand/Right_PinkyProximal/Right_PinkyIntermediate/Right_PinkyDistal/Right_PinkyDistalEnd', 'Hips/Left_UpperLeg', 'Hips/Left_UpperLeg/Left_LowerLeg', 'Hips/Left_UpperLeg/Left_LowerLeg/Left_Foot', 'Hips/Left_UpperLeg/Left_LowerLeg/Left_Foot/Left_Toes', 'Hips/Left_UpperLeg/Left_LowerLeg/Left_Foot/Left_Toes/Left_ToesEnd', 'Hips/Right_UpperLeg', 'Hips/Right_UpperLeg/Right_LowerLeg', 'Hips/Right_UpperLeg/Right_LowerLeg/Right_Foot', 'Hips/Right_UpperLeg/Right_LowerLeg/Right_Foot/Right_Toes', 'Hips/Right_UpperLeg/Right_LowerLeg/Right_Foot/Right_Toes/Right_ToesEnd']
cand = {}
for pth in PATHS:
    for pre in ('', 'Armature/'): cand[zlib.crc32((pre + pth).encode())] = pth.split('/')[-1]
bn = [cand.get(h, '?') for h in hashes] + ['Tail_%02d' % i for i in range(1, len(mats) - len(hashes) + 1)]
d=dict(pos=pos,idx=idx,bw=bw,bi=bi,mats=mats,bn=bn); BW=bw; BI=bi

P=d['pos']; bn=d['bn']; bi=d['bi']; bw=d['bw']
bp={n:np.linalg.inv(m)[:3,3] for n,m in zip(bn,d['mats'])}
dom=np.array([bn[bi[i][np.argmax(bw[i])]] for i in range(len(P))])
def segd(p,a,b):
    ab=b-a; t=np.clip(((p-a)@ab)/(ab@ab),0,1); return np.linalg.norm(p-(a+np.outer(t,ab)),axis=1), t
# head-region verts
H=bp['Head']; N=bp['Neck']
hairmask=np.isin(dom,['Head','Neck','Neck_Twist_A'])
# skirt: distance to thigh axes
dl,_=segd(P,bp['Left_UpperLeg'],bp['Left_LowerLeg']); dr,_=segd(P,bp['Right_UpperLeg'],bp['Right_LowerLeg'])
dthigh=np.minimum(dl,dr)
band=(P[:,1]>0.38)&(P[:,1]<0.62)&~np.isin(dom,[n for n in bn if n.startswith('Tail') or 'Arm' in n or 'Hand' in n or any(k in n for k in ('Thumb','Index','Middle','Ring','Pinky'))])

def sm(a,b,x): t=np.clip((x-a)/(b-a),0,1); return t*t*(3-2*t)
headdom=np.isin(dom,['Head','Neck','Neck_Twist_A'])
rel=P-H
hair=headdom&(P[:,1]<0.885)&((rel[:,2]<0.02)|(np.abs(rel[:,0])>0.05))&(np.linalg.norm((P-N)[:,[0,2]],axis=1)>0.045)
excl=np.array([n.startswith('Tail') or 'Arm' in n or 'Hand' in n or any(k in n for k in ('Thumb','Index','Middle','Ring','Pinky')) for n in dom])
skirt=sm(0.06,0.085,dthigh)*((P[:,1]>0.38)&(P[:,1]<0.63)&~excl)

H=bp['Head']; HIP=bp['Hips']
chains=[]   # (name, parent, joints[...])
# ---- 裙子：繞腰 8 串，每串 2 節 ----
SY=[0.60,0.51,0.42,0.365]
skirt=skirt*sm(0.605,0.575,P[:,1])
ANG=list(range(-180,180,45))
cx,cz=HIP[0],HIP[2]
ang_v=np.degrees(np.arctan2(P[:,2]-cz,P[:,0]-cx)); rad_v=np.hypot(P[:,0]-cx,P[:,2]-cz)
def level_med(y):
    sel=(skirt>0.5)&(np.abs(P[:,1]-y)<0.05)&(rad_v<0.2)
    return float(np.median(rad_v[sel])) if sel.sum()>5 else 0.1
def rad_at(a,y):
    sel=(skirt>0.5)&(np.abs(((ang_v-a+180)%360)-180)<30)&(np.abs(P[:,1]-y)<0.05)&(rad_v<0.2)
    return float(np.median(rad_v[sel])) if sel.sum()>=6 else level_med(y)
for a in ANG:
    r=math.radians(a)
    rs=[min(max(rad_at(a,0.575),0.06),0.1)-0.008]          # 腰：貼著身體
    for y in SY[1:]: rs.append(max(rs[-1]+0.008, rad_at(a,y)-0.008))   # 往下只會越來越開
    js=[(cx+math.cos(r)*rr, y, cz+math.sin(r)*rr) for rr,y in zip(rs,SY)]
    chains.append(('SB_Skirt%+04d'%a,'Hips',js))
# ---- 頭髮：後面一串、左右各一串，每串 2 節 ----
rel=P-H
groups={'SB_HairBack':hair&(rel[:,2]<0.0)&(np.abs(rel[:,0])<0.05),
        'SB_HairL':hair&(rel[:,0]<-0.05), 'SB_HairR':hair&(rel[:,0]>0.05)}
for name,g in groups.items():
    pts=P[g]; top=min(0.885,pts[:,1].max()); bot=pts[:,1].min()
    c=pts.mean(0); x,z=c[0],c[2]
    if name=='SB_HairBack': z=pts[:,2].mean()+0.01
    chains.append((name,'Head',[(x,top,z),(x,(top+bot)/2,z),(x,bot+0.008,z),(x,bot-0.02,z)]))
    print(name,'verts',g.sum(),'top',round(top,3),'bot',round(bot,3),'x',round(x,3),'z',round(z,3))
# ---- 權重 ----
def along(p,js):
    best=(1e9,0.0); n=len(js)
    for i in range(n-1):
        a,b=np.array(js[i]),np.array(js[i+1]); ab=b-a; t=float((p-a)@ab/(ab@ab))
        tc=min(1,t) if i==0 else (max(0,t) if i==n-2 else min(1,max(0,t)))
        dd=np.linalg.norm(p-(a+ab*min(1,max(0,t))))
        if dd<best[0]: best=(dd,i+tc)
    return best[1]
def kernel(t,n):
    if t<=-0.5: return [1.0]+[0.0]*n
    if t>=n-0.5: return [0.0]*n+[1.0]
    cs=[-0.5]+[i+0.5 for i in range(n)]; w=[max(0,1-abs(t-c)) for c in cs]; s=sum(w); return [x/s for x in w]
names={c[0]:[f'{c[0]}_{i+1}' for i in range(len(c[2])-2)] for c in chains}  # 末端點：最後一個關節是 _End
# 注意：每串 joints 有 4 個點 → 3 節骨頭？裙子 SY 4 點 = 2 節 + 末端；頭髮 4 點 = 2 節 + 末端
add={}
jt={c[0]:c[2] for c in chains}
def assign(vi,ch,f,t):
    nm=names[ch]; k=kernel(t,len(nm))
    e=add.setdefault(vi,{'_f':0.0,'ORIG':0.0}); e['_f']+=f; e['ORIG']+=k[0]*f
    for b,w in zip(nm,k[1:]):
        if w>0: e[b]=e.get(b,0.0)+w*f
for vi in np.where(hair)[0]:
    ch='SB_HairBack' if groups['SB_HairBack'][vi] else ('SB_HairL' if groups['SB_HairL'][vi] else ('SB_HairR' if groups['SB_HairR'][vi] else None))
    if ch: assign(int(vi),ch,1.0,along(P[vi],jt[ch][:-1]))
for vi in np.where(skirt>0.01)[0]:
    a=ang_v[vi]; f=float(skirt[vi]); t=(SY[0]-P[vi,1])/(SY[0]-SY[1])
    for A in ANG:
        da=abs(((a-A+180)%360)-180)
        if da<45: assign(int(vi),'SB_Skirt%+04d'%A,f*(1-da/45),t)
out_w=[]
for vi,e in add.items():
    f=e.pop('_f'); keep=e.pop('ORIG')
    # 混合比例：f 是這個頂點總共有多少要交給彈簧骨（裙子邊緣 < 1）
    chainshare=min(1.0,f)
    origscale=(keep/max(f,1e-9))*chainshare+(1-chainshare)
    nw={}
    for k in range(4):
        if BW[vi][k]>0: nw[bn[BI[vi][k]]]=nw.get(bn[BI[vi][k]],0)+float(BW[vi][k])*origscale
    for b,w in e.items(): nw[b]=nw.get(b,0)+w/max(f,1e-9)*chainshare
    top=sorted(nw.items(),key=lambda kv:-kv[1])[:4]; s=sum(w for _,w in top)
    out_w.append([vi,[[b,round(w/s,5)] for b,w in top if w/s>0.004]])
bones=[]
for name,parent,js in chains:
    prev=parent
    for i in range(len(js)-2):
        bones.append({'name':f'{name}_{i+1}','parent':prev,'pos':[round(float(x),5) for x in js[i]]}); prev=f'{name}_{i+1}'
    bones.append({'name':f'{name}_{len(js)-2}_End' if False else f'{name}_End','parent':prev,'pos':[round(float(x),5) for x in js[-2]]})
# Unity 的 JsonUtility 能讀的格式
jb=[{'name':b['name'],'parent':b['parent'],'pos':{'x':b['pos'][0],'y':b['pos'][1],'z':b['pos'][2]}} for b in bones]
jw=[{'v':int(vi),'b':[x[0] for x in ws],'w':[x[1] for x in ws]} for vi,ws in out_w]
json.dump({'source':'PlayerCatWarrior_TailRigged_GarmentFixed','vertexCount':int(len(P)),'hipsToHead':round(float(np.linalg.norm(H-HIP)),4),'bones':jb,'weights':jw},open(OUT,'w'),separators=(',',':'))
print('chains',len(chains),'bones',len(bones),'weighted verts',len(out_w))
