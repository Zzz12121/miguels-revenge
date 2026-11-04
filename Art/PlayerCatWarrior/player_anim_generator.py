"""
產生貓戰士的 CW_Jump / CW_Fall / CW_Land / CW_Climb 動畫（Blender 4.2）
用法：
  1. Blender 匯入 Models/PlayerCharacter/PlayerCatWarrior_Animated/tripo_convert_b6817ade-899b-4f50-9c7e-c61b36b6634c.fbx，存成 cat.blend
  2. blender -b -P player_anim_generator.py -- cat.blend new.blend   （或在 python 裝了 bpy 的環境：python player_anim_generator.py -- cat.blend new.blend）
  3. blender -b -P player_anim_export.py -- new.blend PlayerCatWarrior_AirClimb.fbx，覆蓋 Assets/_Project/Models/PlayerCharacter/ 裡的同名檔案
  4. Unity 執行選單 GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫
想改姿勢：改下面各 pose 函式裡的關節位置 / 角度；關鍵格的時間在最下面 build_all()。
攀爬的姿勢以「牆面在 y = D、平台上緣在 z = 0」來擺；身體、腳尖都留在牆外，跨過邊緣前先把腳抬高（中間格），才不會穿進石頭。
"""
import bpy, math, sys
from mathutils import Vector, Matrix, Quaternion

FPS = 24
IDLE_ANKLE_Z = 0.111
RIG = None
IDLE = {}      # standing_relax 第 1 格的姿勢（每根骨頭的 local 位置 / 旋轉）
REST_FOOT = {}

def upd():
    bpy.context.view_layer.update()

def pb(n): return RIG.pose.bones[n]

def capture(action, frame):
    RIG.animation_data.action = action
    bpy.context.scene.frame_set(frame); upd()
    return {b.name: (b.location.copy(), b.rotation_quaternion.copy()) for b in RIG.pose.bones}

def apply(pose):
    RIG.animation_data.action = None
    for b in RIG.pose.bones:
        if b.name in pose:
            b.location, b.rotation_quaternion = pose[b.name][0].copy(), pose[b.name][1].copy()
    upd()

def head(n):
    upd(); return pb(n).head.copy()

def tail(n):
    upd(); return pb(n).tail.copy()

def rot(n, axis, deg):
    """骨架空間繞軸旋轉（以骨頭根部為中心），子骨頭跟著轉"""
    upd()
    b = pb(n); h = b.head.copy()
    q = Quaternion(Vector(axis).normalized(), math.radians(deg))
    b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
    upd()

def swing(n, direction):
    upd()
    b = pb(n); cur = (b.tail - b.head).normalized()
    q = cur.rotation_difference(Vector(direction).normalized())
    h = b.head.copy()
    b.matrix = Matrix.Translation(h) @ q.to_matrix().to_4x4() @ Matrix.Translation(-h) @ b.matrix
    upd()

def move_hips(offset):
    """把整個身體（Hips）平移 offset（骨架空間）"""
    upd(); b = pb('Hips')
    b.matrix = Matrix.Translation(Vector(offset)) @ b.matrix
    upd()

def place_hips(pos):
    move_hips(Vector(pos) - head('Hips'))

def reach(upper, lower, end, target, pole):
    """兩節骨頭（upper→lower→end 的根部）伸到 target，手肘 / 膝蓋往 pole 的方向彎"""
    upd()
    a = head(upper); l1 = pb(upper).bone.length; l2 = (pb(end).bone.head_local - pb(lower).bone.head_local).length
    t = Vector(target); d = t - a; dist = d.length
    dist_c = max(min(dist, (l1 + l2) * 0.999), abs(l1 - l2) + 1e-4)
    dn = d.normalized()
    bend = Vector(pole) - a; bend = bend - dn * bend.dot(dn)
    if bend.length < 1e-5: bend = Vector((0, -1, 0))
    bend.normalize()
    x = (l1 * l1 - l2 * l2 + dist_c * dist_c) / (2 * dist_c)
    h = math.sqrt(max(l1 * l1 - x * x, 0))
    elbow = a + dn * x + bend * h
    swing(upper, elbow - a)
    swing(lower, t - head(lower))

def arm(side, wrist, pole):
    reach(f'{side}_UpperArm', f'{side}_LowerArm', f'{side}_Hand', wrist, pole)

def leg(side, ankle, pole):
    reach(f'{side}_UpperLeg', f'{side}_LowerLeg', f'{side}_Foot', ankle, pole)

def aim(n, direction):
    swing(n, direction)

IDLE_FOOT = {}
def plant(side):
    """腳掌平貼地面：腳的方向跟待機時一樣（不然腳尖會插進地板）"""
    upd(); b = pb(f'{side}_Foot'); h = b.head.copy()
    rot_only = IDLE_FOOT[side]
    b.matrix = Matrix.Translation(h) @ rot_only
    upd()

FINGERS = ['Index', 'Middle', 'Ring', 'Pinky']
def curl(side, amount, sign=None):
    """手指彎曲（0 = 不動，1 = 握緊）：每一節繞「手掌橫向」的軸往掌心彎"""
    upd()
    across = (head(f'{side}_IndexProximal') - head(f'{side}_PinkyProximal')).normalized()
    sgn = CURL_SIGN if side == 'Left' else -CURL_SIGN
    for f in FINGERS:
        for seg, ang in (('Proximal', 55), ('Intermediate', 70), ('Distal', 45)):
            n = f'{side}_{f}{seg}'
            if n in RIG.pose.bones: rot(n, across, sgn * ang * amount)
CURL_SIGN = 1

def key(frame):
    for b in RIG.pose.bones:
        b.keyframe_insert('rotation_quaternion', frame=frame)
        b.keyframe_insert('location', frame=frame)

def make(name, keys):
    old = bpy.data.actions.get(name)
    if old: bpy.data.actions.remove(old)
    act = bpy.data.actions.new(name); act.use_fake_user = True
    RIG.animation_data.action = act
    prev = {}
    for frame, fn in keys:
        RIG.animation_data.action = None
        fn(); upd()
        for b in RIG.pose.bones:
            q = b.rotation_quaternion
            if b.name in prev and prev[b.name].dot(q) < 0: b.rotation_quaternion = -q
            prev[b.name] = b.rotation_quaternion.copy()
        RIG.animation_data.action = act
        key(frame)
    RIG.animation_data.action = act
    return act

# ---------------------------------------------------------------------------
def setup(blend):
    global RIG, IDLE
    bpy.ops.wm.open_mainfile(filepath=blend)
    RIG = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    for b in RIG.pose.bones: b.rotation_mode = 'QUATERNION'
    idle = [a for a in bpy.data.actions if 'standing_relax' in a.name][0]
    IDLE.update(capture(idle, 1))
    apply(IDLE)
    for side in ('Left', 'Right'):
        m = pb(f'{side}_Foot').matrix.copy(); m.translation = (0, 0, 0)
        IDLE_FOOT[side] = m
    global IDLE_ANKLE_Z
    IDLE_ANKLE_Z = head('Right_Foot').z

def idle():
    apply(IDLE)

X0 = None
def center():
    return head('Hips').x

# ---------------- 跳躍 / 下落 / 落地 ----------------

def feet_now():
    return head('Left_Foot'), head('Right_Foot')

def jump_takeoff():
    idle()
    lf, rf = feet_now(); x = center(); hz = head('Hips').z
    rot('Spine', (1, 0, 0), 6); rot('UpperChest', (1, 0, 0), 4)
    # 腳剛離地：膝蓋幾乎打直、腳尖往下
    leg('Left', Vector((lf.x, lf.y + 0.02, lf.z + 0.03)), Vector((lf.x, -0.6, 0.35)))
    leg('Right', Vector((rf.x, rf.y + 0.05, rf.z + 0.05)), Vector((rf.x, -0.6, 0.35)))
    aim('Left_Foot', (0, -0.35, -1)); aim('Right_Foot', (0, -0.3, -1))
    # 手往前上方甩起
    sh = head('Left_UpperArm').z
    arm('Left', Vector((x + 0.15, -0.13, sh - 0.03)), Vector((x + 0.3, 0.2, sh - 0.35)))
    arm('Right', Vector((x - 0.15, -0.12, sh - 0.05)), Vector((x - 0.3, 0.2, sh - 0.35)))

def jump_tuck(extra=0.0):
    idle()
    lf, rf = feet_now(); x = center(); hp = head('Hips')
    rot('Spine', (1, 0, 0), -2); rot('UpperChest', (1, 0, 0), -2); rot('Head', (1, 0, 0), -4)
    # 前腳膝蓋抬高、後腳微彎（像跨過東西）
    leg('Left', Vector((lf.x - 0.005, hp.y - 0.06 + extra * 0.05, 0.30 - extra * 0.08)), Vector((lf.x, -0.7, 0.6)))
    leg('Right', Vector((rf.x + 0.005, hp.y + 0.10, 0.20 - extra * 0.05)), Vector((rf.x, -0.7, 0.4)))
    aim('Left_Foot', (0, -0.45, -1)); aim('Right_Foot', (0, -0.2, -1))
    sh = head('Left_UpperArm').z
    # 手往兩側上方張開保持平衡，手肘微彎
    arm('Left', Vector((x + 0.25, -0.09, sh - 0.08 - extra * 0.03)), Vector((x + 0.25, 0.25, sh - 0.35)))
    arm('Right', Vector((x - 0.25, -0.07, sh - 0.10 - extra * 0.03)), Vector((x - 0.25, 0.25, sh - 0.35)))

def fall_pose(phase):
    """下落：腳往下伸準備著地（膝蓋微彎、一前一後），手往兩側張開、微微上下擺"""
    idle()
    lf, rf = feet_now(); x = center(); hp = head('Hips')
    s = math.sin(phase * 2 * math.pi)
    rot('Spine', (1, 0, 0), -4); rot('UpperChest', (1, 0, 0), -3); rot('Head', (1, 0, 0), 7)
    leg('Left', Vector((lf.x + 0.01, hp.y - 0.08, 0.15 + 0.025 * s)), Vector((lf.x + 0.05, -0.7, 0.45)))
    leg('Right', Vector((rf.x - 0.01, hp.y + 0.06, 0.20 - 0.025 * s)), Vector((rf.x - 0.05, -0.7, 0.45)))
    aim('Left_Foot', (0, -0.25, -1)); aim('Right_Foot', (0, -0.15, -1))
    sh = head('Left_UpperArm').z
    arm('Left', Vector((x + 0.26, -0.06, sh - 0.06 + 0.025 * s)), Vector((x + 0.25, 0.25, sh - 0.35)))
    arm('Right', Vector((x - 0.26, -0.04, sh - 0.07 - 0.025 * s)), Vector((x - 0.25, 0.25, sh - 0.35)))

def land_pose(depth, lean, arms_fwd):
    """落地緩衝：腳踩在原本的位置，膝蓋彎下去吸收衝擊，上身前傾、手往前下方平衡"""
    idle()
    lf, rf = feet_now(); x = center()
    move_hips((0, -0.015 * depth / 0.16, -depth))
    rot('Spine', (1, 0, 0), lean * 0.55); rot('UpperChest', (1, 0, 0), lean * 0.45)
    rot('Head', (1, 0, 0), -lean * 0.6)
    leg('Left', lf, Vector((lf.x + 0.12, -0.8, 0.35)))
    leg('Right', rf, Vector((rf.x - 0.12, -0.8, 0.35)))
    plant('Left'); plant('Right')
    if arms_fwd > 0:
        sh = head('Left_UpperArm').z
        k = arms_fwd
        arm('Left', Vector((x + 0.16, -0.14 * k, sh - 0.27 + 0.05 * k)), Vector((x + 0.3, 0.3, sh)))
        arm('Right', Vector((x - 0.16, -0.12 * k, sh - 0.28 + 0.05 * k)), Vector((x - 0.3, 0.3, sh)))

# ---------------- 攀爬 ----------------
# 平台上緣（站上去的那一面）在 z = 0，邊緣在 y = +D（角色背後），牆面從邊緣往下。
# 角色原點 = 爬完站好的位置（面向 -Y，也就是面向平台裡面）。
D = 0.30

def grip(side):
    x = center() + (0.11 if side == 'Left' else -0.11)
    return x

def climb_hands(stage):
    """stage 0 = 掛著（手腕在邊緣外側、手指勾住上緣），1 = 撐起（手掌平貼在平台上、手腕在肩膀正下方）"""
    for side in ('Left', 'Right'):
        x = grip(side) if stage < 1 else center() + (0.13 if side == 'Left' else -0.13)
        sgn = 1 if side == 'Left' else -1
        if stage == 0:
            w = Vector((x, D + 0.035, -0.025)); pole = Vector((x + sgn * 0.2, D + 0.4, -0.6)); hand_dir = (0, -0.75, 0.66)
        elif stage == 0.5:
            w = Vector((x, D + 0.03, -0.01)); pole = Vector((x + sgn * 0.25, D + 0.4, -0.5)); hand_dir = (0, -0.9, 0.42)
        else:
            w = Vector((x, D - 0.05, 0.035)); pole = Vector((x + sgn * 0.3, D + 0.5, 0.5)); hand_dir = (sgn * 0.15, -1, -0.05)
        arm(side, w, pole)
        aim(f'{side}_Hand', hand_dir)
        curl(side, 0.85 if stage < 1 else 0.15)

def climb_hang():
    idle(); x = center()
    place_hips((x, D + 0.24, -0.47))
    rot('Spine', (1, 0, 0), -3); rot('UpperChest', (1, 0, 0), 2); rot('Head', (1, 0, 0), -12)
    climb_hands(0)
    # 腳懸空、膝蓋微彎，腳尖碰到牆
    leg('Left', Vector((x + 0.06, D + 0.13, -0.93)), Vector((x + 0.06, D - 0.2, -0.7)))
    leg('Right', Vector((x - 0.06, D + 0.16, -0.96)), Vector((x - 0.06, D - 0.2, -0.75)))
    aim('Left_Foot', (0, -0.6, -0.8)); aim('Right_Foot', (0, -0.5, -0.86))

def climb_pull1():
    idle(); x = center()
    place_hips((x, D + 0.22, -0.31))
    rot('Spine', (1, 0, 0), 2); rot('UpperChest', (1, 0, 0), 3); rot('Head', (1, 0, 0), -14)
    climb_hands(0)
    # 左腳踩牆往上蹬（膝蓋往外上方抬，腳掌貼著牆面、腳尖不會插進牆裡），右腳還懸著
    leg('Left', Vector((x + 0.07, D + 0.12, -0.46)), Vector((x + 0.15, D + 0.5, -0.15)))
    leg('Right', Vector((x - 0.07, D + 0.16, -0.80)), Vector((x - 0.06, D - 0.1, -0.6)))
    aim('Left_Foot', (0, -0.6, -0.8)); aim('Right_Foot', (0, -0.5, -0.86))

def climb_pull2():
    idle(); x = center()
    place_hips((x, D + 0.18, -0.13))
    # 胸口拉到邊緣：上半身只微微前傾（胸口還在邊緣下面，傾太多會撞進牆裡）
    rot('Spine', (1, 0, 0), 6); rot('UpperChest', (1, 0, 0), 5); rot('Head', (1, 0, 0), -16)
    climb_hands(0.5)
    leg('Left', Vector((x + 0.07, D + 0.12, -0.30)), Vector((x + 0.15, D + 0.5, -0.05)))
    leg('Right', Vector((x - 0.07, D + 0.15, -0.55)), Vector((x - 0.1, D + 0.4, -0.3)))
    aim('Left_Foot', (0, -0.6, -0.8)); aim('Right_Foot', (0, -0.5, -0.86))

def climb_knee_up():
    """撐起之前：右膝先抬高到邊緣上方（腳還在牆外），下一格才跨上去，腳不會穿過邊角"""
    idle(); x = center()
    place_hips((x, D + 0.12, -0.05))
    rot('Spine', (1, 0, 0), 12); rot('UpperChest', (1, 0, 0), 10); rot('Head', (1, 0, 0), -20)
    climb_hands(0.5)
    leg('Right', Vector((x - 0.08, D + 0.19, -0.02)), Vector((x - 0.1, D + 0.08, 0.8)))
    leg('Left', Vector((x + 0.07, D + 0.12, -0.24)), Vector((x + 0.15, D + 0.5, 0.0)))
    aim('Right_Foot', (0, -0.3, -0.95)); aim('Left_Foot', (0, -0.6, -0.8))

def climb_press():
    idle(); x = center()
    place_hips((x, D + 0.03, 0.12))
    rot('Spine', (1, 0, 0), 22); rot('UpperChest', (1, 0, 0), 20); rot('Head', (1, 0, 0), -24)
    climb_hands(1)
    # 右膝跨上平台（腳踝在邊緣上方），左腳還踩著牆
    leg('Right', Vector((x - 0.08, D - 0.03, 0.17)), Vector((x - 0.1, D - 0.5, 0.55)))
    leg('Left', Vector((x + 0.07, D + 0.14, -0.17)), Vector((x + 0.15, D + 0.5, 0.1)))
    aim('Right_Foot', (0, -0.3, -1)); aim('Left_Foot', (0, -0.2, -0.98))

def climb_step(left_mid=False):
    idle(); x = center()
    place_hips((x, D - 0.13, 0.27))
    rot('Spine', (1, 0, 0), 20); rot('UpperChest', (1, 0, 0), 14); rot('Head', (1, 0, 0), -18)
    # 右腳踩上平台；左手還撐在平台上，右手撐在右膝上
    leg('Right', Vector((x - 0.09, D - 0.17, IDLE_ANKLE_Z)), Vector((x - 0.12, -0.6, 0.5)))
    lw = Vector((center() + 0.13, D - 0.07, 0.035))
    arm('Left', lw, Vector((x + 0.4, D + 0.3, 0.6))); aim('Left_Hand', (0.15, -1, -0.05)); curl('Left', 0.15)
    rk = head('Right_LowerLeg')
    arm('Right', Vector((rk.x - 0.02, rk.y - 0.03, rk.z + 0.04)), Vector((x - 0.4, D + 0.3, 0.5)))
    aim('Right_Hand', (0, -0.6, -0.8)); curl('Right', 0.4)
    # 左膝靠在邊緣上，腳掌還在外面
    if left_mid:
        # 中間格：左腳先往外、往上提，腳尖朝下，不會刮進牆面
        leg('Left', Vector((x + 0.08, D + 0.17, -0.04)), Vector((x + 0.08, D + 0.1, 0.4)))
    else:
        leg('Left', Vector((x + 0.08, D + 0.12, 0.11)), Vector((x + 0.08, D - 0.4, 0.2)))
    plant('Right'); aim('Left_Foot', (0, 0.3, -1))

def climb_rise(mid=False):
    idle(); x = center()
    lf, rf = feet_now()
    move_hips((0, 0.06, -0.09))
    rot('Spine', (1, 0, 0), 12); rot('UpperChest', (1, 0, 0), 6); rot('Head', (1, 0, 0), -10)
    leg('Right', Vector((rf.x, D - 0.17, rf.z)), Vector((rf.x, -0.6, 0.5)))
    plant('Right')
    # 左腳往前跨，腳還在空中
    if mid:
        # 中間格：先把左腳抬過邊緣（腳尖朝下），再往前跨，避免腳尖刮進石頭上緣
        leg('Left', Vector((lf.x, lf.y + 0.05, 0.22)), Vector((lf.x, -0.6, 0.5)))
        aim('Left_Foot', (0, 0.15, -1))
    else:
        leg('Left', Vector((lf.x, lf.y + 0.06, 0.21)), Vector((lf.x, -0.6, 0.5)))
        aim('Left_Foot', (0, -0.6, -0.8))
    sh = head('Left_UpperArm').z
    arm('Left', Vector((x + 0.17, -0.05, sh - 0.25)), Vector((x + 0.3, 0.3, sh)))
    arm('Right', Vector((x - 0.17, -0.02, sh - 0.26)), Vector((x - 0.3, 0.3, sh)))

def build_all():
    make('CW_Jump', [(1, jump_takeoff), (5, lambda: jump_tuck(0)), (10, lambda: jump_tuck(0.4))])
    make('CW_Fall', [(1, lambda: fall_pose(0)), (7, lambda: fall_pose(0.25)), (13, lambda: fall_pose(0.5)), (19, lambda: fall_pose(0.75)), (25, lambda: fall_pose(0))])
    make('CW_Land', [(1, lambda: land_pose(0.07, 10, 0.5)), (4, lambda: land_pose(0.20, 28, 1)), (8, lambda: land_pose(0.09, 14, 0.6)), (14, idle)])
    make('CW_Climb', [(1, climb_hang), (7, climb_pull1), (12, climb_pull2), (14, climb_knee_up), (17, climb_press), (19, lambda: climb_step(True)), (21, climb_step), (23, lambda: climb_rise(True)), (26, climb_rise), (30, idle)])

if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:]
    setup(args[0])
    build_all()
    bpy.ops.wm.save_as_mainfile(filepath=args[1])
    print('ACTIONS', [a.name for a in bpy.data.actions if a.name.startswith('CW_')])
