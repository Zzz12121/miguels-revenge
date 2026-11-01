"""
黑貓 NPC 的動畫：BC_Idle（待機）/ BC_Gesture（GIF 那段：搓手 → 甩開 → 大力拍手 → 托下巴 → 摀嘴歪頭偷笑 → 舉手 → 指人 → 揮開）
用法（Blender 4.2）：
  1. Blender 匯入 Tripo 匯出的 BlackCat_Rigged（tripo_convert_*.fbx），存成 cat.blend
  2. blender -b -P blackcat_anim_generator.py -- cat.blend new.blend
     （或在 python 裝了 bpy 的環境：python blackcat_anim_generator.py -- cat.blend new.blend）
  3. blender -b -P blackcat_export.py -- new.blend BlackCat.fbx，放到 Assets/_Project/Models/BlackCat/
  4. Unity 選單 GeminiGrowUp > 黑貓 > 建立 Prefab 並放進目前場景
座標：角色面向 -Y、上方 +Z、角色的左邊是 +X（Tripo 匯出的骨架就是這樣，身體中心 x ≈ 0.074，身高約 0.98）。
姿勢都用「手腕要到哪裡 + 手指朝哪 + 手掌朝哪」來擺：手臂用兩節骨頭的解析 IK，手掌再轉到指定方向。
時間參考 GIF（3.47 秒），拍手和伸手的部分放慢、加上預備和慣性的中間格，全長約 5 秒；30 fps，第 1 格 = 0 秒。
"""
import bpy, math, sys
from mathutils import Vector, Matrix, Quaternion

FPS = 30
P = 'mixamorig:'
RIG = None
REST = {}
CX = 0.074                      # 身體中心 x
MOUTH_REST = Vector((CX, -0.312, 0.745))   # 嘴巴（牙齒中間，表面）
CHIN_REST = Vector((CX, -0.300, 0.700))

def upd(): bpy.context.view_layer.update()
def pb(n): return RIG.pose.bones[P + n]
def head(n): upd(); return pb(n).head.copy()
def tail(n): upd(); return pb(n).tail.copy()

def rot(n, axis, deg):
    """骨架空間繞軸旋轉（以骨頭根部為中心），子骨頭跟著轉。
    axis：(1,0,0) 正 = 往前彎；(0,1,0) 正 = 往角色左邊倒；(0,0,1) 正 = 轉向角色左邊"""
    upd(); b = pb(n); h = b.head.copy()
    q = Quaternion(Vector(axis).normalized(), math.radians(deg))
    b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
    upd()

def rot_q(n, q):
    upd(); b = pb(n); h = b.head.copy()
    b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
    upd()

def swing(n, direction):
    upd(); b = pb(n); cur = (b.tail - b.head).normalized()
    rot_q(n, cur.rotation_difference(Vector(direction).normalized()))

def reach(upper, lower, end, target, pole):
    upd()
    a = head(upper); l1 = (head(lower) - a).length; l2 = (head(end) - head(lower)).length
    t = Vector(target); d = t - a; dist = d.length
    dist_c = max(min(dist, (l1 + l2) * 0.999), abs(l1 - l2) + 1e-4)
    dn = d.normalized()
    bend = Vector(pole) - a; bend = bend - dn * bend.dot(dn)
    if bend.length < 1e-5: bend = Vector((0, 1, 0))
    bend.normalize()
    x = (l1 * l1 - l2 * l2 + dist_c * dist_c) / (2 * dist_c)
    h = math.sqrt(max(l1 * l1 - x * x, 0))
    elbow = a + dn * x + bend * h
    swing(upper, elbow - a)
    swing(lower, t - head(lower))

def side_name(s): return 'Left' if s == 'L' else 'Right'

def arm(s, wrist, pole):
    n = side_name(s)
    reach(f'{n}Arm', f'{n}ForeArm', f'{n}Hand', wrist, pole)

def hand_frame(s):
    """手的方向：f = 手指方向、t = 往大拇指那側、n = 掌心朝向"""
    n = side_name(s); upd()
    w = head(f'{n}Hand')
    f = (head(f'{n}HandMiddle1') + head(f'{n}HandIndex1')) * .5 - w
    if f.length < 1e-4: f = tail(f'{n}Hand') - w
    f.normalize()
    t = head(f'{n}HandIndex1') - head(f'{n}HandPinky1'); t = (t - f * t.dot(f)).normalized()
    palm = f.cross(t) if s == 'L' else t.cross(f)
    return f, t, palm.normalized()

def orient_hand(s, finger_dir, palm_dir):
    f, t, n = hand_frame(s)
    F = Vector(finger_dir).normalized(); N = Vector(palm_dir); N = (N - F * N.dot(F)).normalized()
    cur = Matrix((f, n, f.cross(n))).transposed()
    tgt = Matrix((F, N, F.cross(N))).transposed()
    rot_q(f'{side_name(s)}Hand', (tgt @ cur.inverted()).to_quaternion())

FINGERS = ('Index', 'Middle', 'Ring', 'Pinky')
def fingers(s, curl=0.0, spread=0.0, index=None, thumb=None):
    """curl 0 = 伸直、1 = 握拳；spread = 手指張開（度）；index / thumb 可以分開設定彎曲"""
    n = side_name(s)
    f, t, palm = hand_frame(s)
    for i, fing in enumerate(FINGERS):
        c = curl if not (fing == 'Index' and index is not None) else index
        # 張開：以中指為中心往兩邊散開
        sp = (1.5 - i) * spread
        if abs(sp) > .01: rot(f'{n}Hand{fing}1', palm, sp if s == 'L' else -sp)
        for seg, ang in (('1', 75), ('2', 90), ('3', 60)):
            b = f'{n}Hand{fing}{seg}'
            if P + b not in RIG.pose.bones or c == 0: continue
            d = (tail(b) - head(b)).normalized()
            axis = d.cross(palm)
            if axis.length > 1e-4: rot(b, axis, ang * c)
    tc = curl * .6 if thumb is None else thumb
    if tc:
        for seg, ang in (('1', 25), ('2', 35), ('3', 35)):
            b = f'{n}HandThumb{seg}'
            d = (tail(b) - head(b)).normalized(); axis = d.cross(palm)
            if axis.length > 1e-4: rot(b, axis, ang * tc)

# ---------------------------------------------------------------------------
def key(frame):
    for b in RIG.pose.bones:
        b.keyframe_insert('rotation_quaternion', frame=frame)
        b.keyframe_insert('location', frame=frame)

def rest():
    for b in RIG.pose.bones:
        b.location = (0, 0, 0); b.rotation_quaternion = (1, 0, 0, 0); b.scale = (1, 1, 1)
    upd()

def make(name, keys, snappy=()):
    fr = [k[0] for k in keys]; assert fr == sorted(fr) and len(set(fr)) == len(fr), fr
    """keys = [(frame, pose_fn)]；snappy 裡的格數：進入那一格之前用「加速」曲線（拍下去很猛、不會慢慢靠過去）"""
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    RIG.animation_data_create()
    prev = {}
    for frame, fn in keys:
        RIG.animation_data.action = None
        rest(); fn(); upd()
        for b in RIG.pose.bones:
            q = b.rotation_quaternion
            if b.name in prev and prev[b.name].dot(q) < 0: b.rotation_quaternion = -q
            prev[b.name] = b.rotation_quaternion.copy()
        RIG.animation_data.action = act
        key(frame)
    frames = [k[0] for k in keys]
    for fc in act.fcurves:
        for kp in fc.keyframe_points:
            kp.interpolation = 'BEZIER'; kp.handle_left_type = kp.handle_right_type = 'AUTO_CLAMPED'
        fc.update()
        for kp in fc.keyframe_points:
            i = frames.index(int(round(kp.co.x))) if int(round(kp.co.x)) in frames else -1
            if i > 0 and frames[i] in snappy:
                # 前一格到這一格：先慢後快（ease-in），撞到這一格就停
                pk = [k for k in fc.keyframe_points if int(round(k.co.x)) == frames[i - 1]]
                if pk: pk[0].interpolation = 'CUBIC'; pk[0].easing = 'EASE_IN'
        fc.update()
    RIG.animation_data.action = act
    return act

# ---------------------------------------------------------------------------
#  基本身體
# ---------------------------------------------------------------------------
ANKLE = {}
def setup(blend):
    global RIG
    bpy.ops.wm.open_mainfile(filepath=blend)
    RIG = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for b in RIG.pose.bones: b.rotation_mode = 'QUATERNION'
    RIG.animation_data_clear(); rest()
    for s in ('L', 'R'):
        n = side_name(s)
        m = pb(f'{n}Foot').matrix.copy(); m.translation = (0, 0, 0)
        ANKLE[s] = (head(f'{n}Foot'), m, pb(f'{n}ToeBase').matrix.copy())

def hips(offset=(0, 0, 0), pitch=0.0, roll=0.0, yaw=0.0):
    """移動 / 轉動骨盆，腳掌留在地上（腿重新 IK）"""
    upd(); b = pb('Hips')
    if pitch: rot('Hips', (1, 0, 0), pitch)
    if roll: rot('Hips', (0, 1, 0), roll)
    if yaw: rot('Hips', (0, 0, 1), yaw)
    b.matrix = Matrix.Translation(Vector(offset)) @ b.matrix; upd()
    for s in ('L', 'R'):
        n = side_name(s); ank, frot, _ = ANKLE[s]
        reach(f'{n}UpLeg', f'{n}Leg', f'{n}Foot', ank, ank + Vector((0, -0.5, 0.15)))
        fb = pb(f'{n}Foot'); h = fb.head.copy(); fb.matrix = Matrix.Translation(h) @ frot; upd()

def torso(lean=0.0, side=0.0, twist=0.0, chest_lean=None):
    """上半身：lean 正 = 往前彎、side 正 = 往左倒、twist 正 = 轉向左邊（分到三節脊椎）"""
    cl = lean if chest_lean is None else chest_lean
    for b, w in (('Spine', .3), ('Spine1', .35), ('Spine2', .35)):
        ll = lean if b != 'Spine2' else cl
        if ll: rot(b, (1, 0, 0), ll * w)
        if side: rot(b, (0, 1, 0), side * w)
        if twist: rot(b, (0, 0, 1), twist * w)

def head_pose(pitch=0.0, roll=0.0, yaw=0.0):
    """頭：pitch 正 = 低頭、roll 正 = 往左歪、yaw 正 = 看向左邊"""
    for b, w in (('Neck', .35), ('Head', .65)):
        if yaw: rot(b, (0, 0, 1), yaw * w)
        if pitch: rot(b, (1, 0, 0), pitch * w)
        if roll: rot(b, (0, 1, 0), roll * w)

def shrug(s, up=0.0, fwd=0.0):
    """聳肩：up 正 = 肩膀往上、fwd 正 = 肩膀往前（手才搆得到臉）"""
    n = side_name(s); sg = 1 if s == 'L' else -1
    if up: rot(f'{n}Shoulder', (0, 1, 0), -sg * up)
    if fwd: rot(f'{n}Shoulder', (0, 0, 1), sg * fwd)

def head_point(rest_point):
    """頭上某一點（rest 位置）現在在哪裡（跟著頭動）"""
    upd(); b = pb('Head')
    return b.matrix @ b.bone.matrix_local.inverted() @ Vector(rest_point)

def head_dir(rest_dir):
    upd(); b = pb('Head')
    return (b.matrix.to_3x3() @ b.bone.matrix_local.to_3x3().inverted() @ Vector(rest_dir)).normalized()

def put_palm(s, palm_center, finger_dir, palm_dir, pole, palm_off=0.035):
    """手掌中心放到 palm_center（手腕 = 手掌中心往手指反方向退 palm_off），再轉手掌方向。
    IK 要兩次：第一次轉好手掌後，手掌中心的位置會稍微不同"""
    F = Vector(finger_dir).normalized()
    for _ in range(2):
        arm(s, Vector(palm_center) - F * palm_off, pole)
        orient_hand(s, finger_dir, palm_dir)
        f, t, n = hand_frame(s)
        w = head(f'{side_name(s)}Hand')
        palm_off = ((head(f'{side_name(s)}HandMiddle1') - w).length) * .75

def sh(s): return head(f'{side_name(s)}Arm')

# ---------------------------------------------------------------------------
#  姿勢（全部從 rest 開始擺）
# ---------------------------------------------------------------------------
def together(breath=0.0, hunch=0.0, y=-0.30, z=0.455, lean=4.0):
    """起始 / 待機：雙手在胸前輕輕合著（掌心相對、指尖朝上前方）"""
    hips((0, 0, -0.004 * hunch))
    torso(lean=lean + 6 * hunch - 1.5 * breath, chest_lean=lean + 6 * hunch - 2.5 * breath)
    shrug('L', up=6 * hunch + 2 * breath); shrug('R', up=6 * hunch + 2 * breath)
    head_pose(pitch=-6 + 4 * hunch + 1.5 * breath)
    c = Vector((CX, y, z + 0.006 * breath))
    put_palm('L', c + Vector((0.016, 0, 0)), (-0.15, -0.55, 0.82), (-1, 0, 0), sh('L') + Vector((0.25, 0.25, -0.2)))
    put_palm('R', c + Vector((-0.016, 0, 0)), (0.15, -0.55, 0.82), (1, 0, 0), sh('R') + Vector((-0.25, 0.25, -0.2)))
    fingers('L', 0.15); fingers('R', 0.15)

def rub(phase, hunch=1.0):
    """搓手：兩隻手掌貼在一起，上下交錯滑動；肩膀縮起來、低頭往前看（打壞主意）"""
    hips((0, 0, -0.008))
    torso(lean=10 * hunch, chest_lean=12 * hunch, twist=3 * phase)
    shrug('L', up=10 * hunch, fwd=4); shrug('R', up=10 * hunch, fwd=4)
    head_pose(pitch=4 * hunch, yaw=-2 * phase)
    c = Vector((CX, -0.30, 0.45))
    dz = 0.022 * phase
    put_palm('L', c + Vector((0.014, -0.004, dz)), (-0.35, -0.6, 0.72), (-1, 0, -0.1), sh('L') + Vector((0.25, 0.25, -0.2)))
    put_palm('R', c + Vector((-0.014, 0.004, -dz)), (0.35, -0.55, 0.76), (1, 0, -0.1), sh('R') + Vector((-0.25, 0.25, -0.2)))
    fingers('L', 0.25); fingers('R', 0.25)

def fling(amount=1.0, over=0.0):
    """甩開：雙臂猛地往兩側上方張開、手掌朝前、手指張開，上半身往後仰"""
    k = amount
    hips((0, 0.006 * k, 0.006 * k))
    torso(lean=-9 * k - 3 * over, chest_lean=-11 * k - 4 * over)
    shrug('L', up=7 * k); shrug('R', up=7 * k)
    head_pose(pitch=-10 * k - 3 * over, roll=-3 * k)
    for s, sg in (('L', 1), ('R', -1)):
        a = sh(s)
        w = a + Vector((sg * (0.21 + 0.03 * over) * k, -0.10 + 0.02 * k, 0.10 * k + 0.03 * over))
        put_palm(s, w, (sg * 0.55, -0.05, 0.84), (0, -1, 0.1), a + Vector((sg * 0.15, 0.25, -0.25)))
        fingers(s, 0.0, spread=12 * k)

def fling_mid():
    """甩開的途中：手肘先往外上方帶，手掌還在後面跟著（動作有先後，不會整隻手臂直直地瞬間打開）"""
    hips((0, 0.003, 0.004))
    torso(lean=-3, chest_lean=-4)
    shrug('L', up=5); shrug('R', up=5)
    head_pose(pitch=-5, roll=-1)
    for s, sg in (('L', 1), ('R', -1)):
        a = sh(s)
        put_palm(s, a + Vector((sg * 0.12, -0.16, 0.04)), (sg * 0.3, -0.5, 0.8), (sg * -0.3, -0.9, 0.1), a + Vector((sg * 0.3, 0.1, 0.05)))
        fingers(s, 0.1, spread=6)

def clap_approach():
    """拍下去之前：兩手已經往中間收、還沒碰到（手肘先收，手掌跟上）"""
    hips((0, -0.002, -0.004))
    torso(lean=3, chest_lean=4)
    shrug('L', up=6, fwd=4); shrug('R', up=6, fwd=4)
    head_pose(pitch=-2)
    c = Vector((CX, -0.33, 0.51))
    put_palm('L', c + Vector((0.075, 0.01, 0.01)), (0.15, -0.45, 0.88), (-1, -0.2, 0), sh('L') + Vector((0.3, 0.2, -0.15)))
    put_palm('R', c + Vector((-0.075, 0.01, 0.01)), (-0.15, -0.45, 0.88), (1, -0.2, 0), sh('R') + Vector((-0.3, 0.2, -0.15)))
    fingers('L', 0.05, spread=4); fingers('R', 0.05, spread=4)

def clap_open(a=1.0, lean_back=0.0):
    """連拍之間：兩手往兩邊打開（比第一次甩開小），準備再拍下一下"""
    hips((0, 0.002 * a, 0.002 * a))
    torso(lean=2 - 4 * lean_back, chest_lean=2 - 6 * lean_back)
    shrug('L', up=5 + 3 * a); shrug('R', up=5 + 3 * a)
    head_pose(pitch=-3 - 3 * lean_back)
    c = Vector((CX, -0.32, 0.52 + 0.02 * a))
    d = 0.075 + 0.10 * a
    put_palm('L', c + Vector((d, 0.015, 0.0)), (0.3, -0.4, 0.86), (-1, -0.3, 0), sh('L') + Vector((0.3, 0.2, -0.1)))
    put_palm('R', c + Vector((-d, 0.015, 0.0)), (-0.3, -0.4, 0.86), (1, -0.3, 0), sh('R') + Vector((-0.3, 0.2, -0.1)))
    fingers('L', 0.05, spread=6 * a); fingers('R', 0.05, spread=6 * a)

# 4 下拍手的時間：照 IG 短片裡 4 聲拍手的間隔（0.78 / 0.73 / 0.63 秒，越拍越快）
CLAPS = (1.09, 1.87, 2.60, 3.23)
SHIFT = CLAPS[-1] - CLAPS[0]   # 後面的動作整段往後移

def point_prep():
    """伸手指人之前：右手先收回到胸前、手肘往後，食指慢慢伸出來（有一個「準備伸出去」的過程）"""
    hips((0, 0, -0.004))
    torso(lean=4, twist=-2, chest_lean=5)
    head_pose(pitch=-1, yaw=-4)
    shrug('R', up=8, fwd=4)
    a = sh('R')
    put_palm('R', a + Vector((0.03, -0.13, 0.06)), (0.2, -0.85, 0.45), (0.3, 0.1, -1), a + Vector((-0.3, 0.25, -0.15)))
    fingers('R', 0.85, index=0.35, thumb=0.7)
    put_palm('L', Vector((CX + 0.04, -0.29, 0.43)), (-0.7, -0.5, 0.5), (-0.5, 0.3, 0.8), sh('L') + Vector((0.3, 0.15, -0.3)))
    fingers('L', 0.8)

def clap(impact=True, bounce=0.0):
    """大力拍手：雙手在胸前用力合起來（掌心相對、手指朝上），身體往前震"""
    lean = 12 if impact else 6
    hips((0, -0.006, -0.012 if impact else -0.006))
    torso(lean=lean - 5 * bounce, chest_lean=lean + 3 - 6 * bounce)
    shrug('L', up=4, fwd=6); shrug('R', up=4, fwd=6)
    head_pose(pitch=6 if impact else 0, roll=0)
    c = Vector((CX, -0.33, 0.50 + 0.01 * bounce))
    put_palm('L', c + Vector((0.013, 0, 0)), (-0.05, -0.35, 0.94), (-1, 0, 0), sh('L') + Vector((0.3, 0.2, -0.2)))
    put_palm('R', c + Vector((-0.013, 0, 0)), (0.05, -0.35, 0.94), (1, 0, 0), sh('R') + Vector((-0.3, 0.2, -0.2)))
    fingers('L', 0.05); fingers('R', 0.05)

def chin():
    """托下巴（思考）：右手托著下巴，左手橫在胸前托住右手肘"""
    hips((0, 0, -0.004))
    torso(lean=6, side=2, chest_lean=7)
    head_pose(pitch=6, roll=4, yaw=-4)
    shrug('R', up=14, fwd=10)
    ch = head_point(CHIN_REST)
    put_palm('R', ch + Vector((0.0, -0.035, -0.035)), (0.25, -0.15, 0.95), (0, 0.6, 0.8), sh('R') + Vector((-0.3, 0.15, -0.35)))
    fingers('R', 0.45, thumb=0.2)
    el = head('RightForeArm')
    put_palm('L', el + Vector((0.035, -0.03, -0.02)), (-1, -0.1, 0.05), (0, 0.1, 1), sh('L') + Vector((0.25, 0.2, -0.3)))
    fingers('L', 0.35)

def cover(tilt=1.0, glance=0.0):
    """摀嘴偷笑：右手摀住嘴巴，頭用力往一邊歪、上半身也往同一邊倒（tilt 0 = 頭擺正）"""
    k = tilt
    hips((0.008 * k, 0, -0.006))
    torso(lean=8, side=-9 * k, chest_lean=9, twist=-2 * k)
    head_pose(pitch=10 + 4 * k, roll=-26 * k, yaw=-6 * k + 14 * glance)
    shrug('R', up=18, fwd=14); shrug('L', up=6)
    m = head_point(MOUTH_REST); fwd = head_dir((0, -1, 0)); up = head_dir((0, 0, 1))
    put_palm('R', m + fwd * 0.03 - up * 0.012, up * 0.85 + head_dir((1, 0, 0)) * 0.5, -fwd, sh('R') + Vector((-0.35, 0.1, -0.3)))
    fingers('R', 0.2, thumb=0.3)
    # 左手放鬆在肚子前面
    put_palm('L', Vector((CX + 0.05, -0.29, 0.40)), (-0.6, -0.5, 0.6), (-0.6, 0.3, 0.6), sh('L') + Vector((0.3, 0.15, -0.3)))
    fingers('L', 0.45)

def wait_hand(amount=1.0):
    """舉手（等一下）：右手從嘴巴拿開、舉在臉旁邊，掌心朝前、手指朝上"""
    hips((0, 0, -0.004))
    torso(lean=3, side=2, chest_lean=3)
    head_pose(pitch=0, roll=4, yaw=-6)
    shrug('R', up=16 * amount, fwd=4)
    a = sh('R')
    put_palm('R', Vector((CX - 0.155, -0.31, 0.66 + 0.02 * amount)), (-0.15, -0.1, 0.98), (0, -1, 0), a + Vector((-0.3, 0.2, -0.3)))
    fingers('R', 0.0, spread=8)
    put_palm('L', Vector((CX + 0.05, -0.29, 0.41)), (-0.6, -0.5, 0.6), (-0.6, 0.3, 0.6), sh('L') + Vector((0.3, 0.15, -0.3)))
    fingers('L', 0.5)

def point(amount=1.0):
    """指人：右手往前伸、食指直直指向前方，其他手指握起來；左手在胸前握拳"""
    hips((0, -0.004, -0.004))
    torso(lean=8, twist=-6, chest_lean=9)
    head_pose(pitch=-2, yaw=-3)
    shrug('R', up=6, fwd=14)
    a = sh('R')
    put_palm('R', a + Vector((0.03, -0.26 * amount - 0.02, 0.13)), (0.08, -1, 0.12), (0.3, 0, -1), a + Vector((-0.3, 0.1, -0.2)))
    fingers('R', 1.0, index=0.0, thumb=0.9)
    put_palm('L', Vector((CX + 0.02, -0.30, 0.45)), (-0.7, -0.5, 0.5), (-0.5, 0.3, 0.8), sh('L') + Vector((0.3, 0.15, -0.3)))
    fingers('L', 0.9)

def sweep(amount=1.0):
    """揮開：指人的那隻手往身體另一側掃過去（不屑地揮一下）"""
    hips((0, 0, -0.004))
    torso(lean=6, twist=10 * amount, side=3 * amount, chest_lean=7)
    head_pose(pitch=0, yaw=8 * amount, roll=4 * amount)
    shrug('R', up=8, fwd=16)
    put_palm('R', Vector((CX + 0.12 + 0.12 * amount, -0.33, 0.46 + 0.02 * amount)), (1, -0.2, 0.1), (0, -0.3, -1), sh('R') + Vector((-0.1, 0.2, -0.35)))
    fingers('R', 0.15, spread=6)
    put_palm('L', Vector((CX + 0.04, -0.29, 0.43)), (-0.7, -0.5, 0.5), (-0.5, 0.3, 0.8), sh('L') + Vector((0.3, 0.15, -0.3)))
    fingers('L', 0.6)

def T(t): return int(round(t * FPS)) + 1

def build_all():
    make('BC_Idle', [(1, lambda: together(0)), (46, lambda: together(1)), (91, lambda: together(0))])
    # 時間：拍手前後放慢、加上「預備 → 動作 → 慣性」的中間格，伸手也有一段伸出去的時間
    g = [
        (T(0.00), lambda: together(0)),
        (T(0.15), lambda: rub(1, .7)),
        (T(0.30), lambda: rub(-1, 1)),
        (T(0.44), lambda: rub(.6, 1.15)),         # 蓄力：肩膀縮最緊
        (T(0.56), fling_mid),                     # 手肘先帶出去
        (T(0.68), lambda: fling(1.0)),            # 張開
        (T(0.80), lambda: fling(1.0, 1.0)),       # 慣性：再往外飄一點
        (T(0.92), lambda: fling(0.9, 0.5)),       # 開始往回收
        (T(1.02), clap_approach),                 # 兩手往中間靠
        (T(1.09), lambda: clap(True)),            # 啪！
    ]
    for j in range(1, len(CLAPS)):
        prev, cur = CLAPS[j - 1], CLAPS[j]; gap = cur - prev
        amt = 0.75 + 0.25 * (j == len(CLAPS) - 1)      # 最後一下打得最開
        g += [
            (T(prev + 0.08), lambda: clap(False, 1.0)),                 # 反彈
            (T(prev + gap * 0.5), lambda a=amt: clap_open(a, 0.5)),    # 打開
            (T(cur - 0.07), clap_approach),                            # 往中間靠
            (T(cur), lambda: clap(True)),                              # 啪！
        ]
    g += [
        (T(CLAPS[-1] + 0.08), lambda: clap(False, 1.0)),
        (T(CLAPS[-1] + 0.21), lambda: clap(False, 0.3)),
    ]
    later = [
        (T(1.50), chin),                          # 托下巴
        (T(1.68), lambda: cover(0.35)),           # 手移到嘴巴，頭開始歪
        (T(1.86), lambda: cover(1.0)),            # 頭歪最多（偷笑）
        (T(2.12), lambda: cover(0.9)),
        (T(2.34), lambda: cover(0.0, 0.0)),       # 頭擺正，手還摀著
        (T(2.60), lambda: cover(0.0, -0.6)),      # 眼睛往旁邊瞄
        (T(2.80), lambda: cover(0.0, -0.4)),
        (T(3.08), lambda: wait_hand(1.0)),        # 舉手：等一下
        (T(3.28), lambda: wait_hand(0.9)),
        (T(3.46), point_prep),                    # 手收回來、準備伸出去
        (T(3.78), lambda: point(1.0)),            # 慢慢伸出去指人
        (T(4.02), lambda: point(0.95)),
        (T(4.16), lambda: sweep(0.3)),
        (T(4.42), lambda: sweep(1.0)),            # 揮開
        (T(4.95), lambda: together(0)),           # 回到起始姿勢（循環接得起來）
    ]
    # 托下巴之後的動作：時間是照「只拍一下」的版本寫的，整段往後移到第 4 下拍完之後
    g += [(f + round(SHIFT * FPS), fn) for f, fn in later]
    make('BC_Gesture', g, snappy=tuple(T(c) for c in CLAPS))

if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:]
    setup(args[0])
    build_all()
    bpy.context.scene.render.fps = FPS
    bpy.ops.wm.save_as_mainfile(filepath=args[1])
    print('ACTIONS', [a.name for a in bpy.data.actions if a.name.startswith('BC_')])
