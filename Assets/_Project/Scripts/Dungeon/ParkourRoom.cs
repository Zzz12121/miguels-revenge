using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 跑酷房（由 DungeonGenerator 自動產生，不用手動擺）。
///
///   - 比一般房間大，房間裡沒有地板，只有兩端門口前面各一條窄窄的平台
///   - 整片虛空均勻地鋪滿浮空的石板（跟 Boss 房地板一樣），沒有特定的「路」；
///     每顆的高度都不一樣（平台往下 2 個跳躍高度 ～ 往上 1 個跳躍高度之間），一定找得到跳得過去的路線到對面的門
///   - 石頭會輕輕地上下浮動、左右擺動；站在上面會跟著石頭一起動
///   - 門不會關、沒有小怪
///   - 掉下去：不會死，傳送回最後站過的那一端平台，扣一點血（判定線在最低的石頭底下，站在最低的石頭上不會被判定成掉下去）
///
/// 石頭的位置和高度每次產生地城都不一樣（跟著地城的亂數種子）。
/// 「跳不跳得過去」用玩家實際的跳躍高度、重力、走路速度計算（衝刺跳會更遠，比較輕鬆）。
/// </summary>
[DefaultExecutionOrder(-50)] // 石頭要比玩家的移動先動，站在上面的玩家才會被平順地帶著走
public class ParkourRoom : MonoBehaviour
{
    [Header("石頭的高低（單位：玩家的跳躍高度）")]
    [Tooltip("最低的石頭在平台下方幾個跳躍高度")]
    public float lowestJumps = 2f;
    [Tooltip("最高的石頭在平台上方幾個跳躍高度")]
    public float highestJumps = 1f;

    [Header("石頭的動畫")]
    [Tooltip("上下浮動的幅度（幾個跳躍高度）")]
    public float bobJumps = 0.2f;
    [Tooltip("左右擺動的距離 (m)")]
    public float swayDistance = 0.15f;
    [Tooltip("輕微傾斜的角度（度）")]
    public float tiltDegrees = 1.5f;
    [Tooltip("一次浮動 / 擺動要幾秒（隨機範圍）")]
    public Vector2 period = new Vector2(3f, 5.5f);

    [Header("掉下去")]
    [Tooltip("掉到平台以下多深就算掉下去 (m)。產生石頭時會自動設成「最低的石頭再往下 2.5m」")]
    public float fallDepth = 6f;
    [Tooltip("掉下去扣多少血（只會扣到剩 1 滴，不會因此死掉）")]
    public float fallDamage = 10f;

    [Header("提示文字")]
    public string enterTitle = "PARKOUR";
    public string enterSubtitle = "跳過浮空的石頭，前往對面的門";
    public string clearTitle = "COMPLETE";
    public string clearSubtitle = "";

    [Header("由 DungeonGenerator 設定")]
    [Tooltip("從一個門到對面的門的方向（房間的本地座標）")]
    public Vector3 axisLocal = Vector3.forward;
    public float halfSize = 18f;
    [Tooltip("兩端門口前平台的深度 (m)")]
    public float ledgeDepth = 4f;
    [Tooltip("玩家的跳躍高度 (m)，產生時從玩家身上讀")]
    public float jumpHeight = 1.6f;

    class Motion
    {
        public Transform t;
        public Collider col;
        public Vector3 basePos, swayDir, lastWorldDelta;
        public Quaternion baseRot;
        public float bobSpeed, bobPhase, swaySpeed, swayPhase, tiltPhase;
    }
    readonly List<Motion> motions = new List<Motion>();
    readonly Dictionary<Collider, Motion> byCollider = new Dictionary<Collider, Motion>();
    float savedKillY = float.NaN;
    bool loweredKillY;

    Transform player;
    PlayerHealth health;
    ThirdPersonController move;
    CharacterController cc;
    int lastLedge;      // 最後站過哪一端（+1 / -1，0 = 還沒進來過）
    int firstLedge;
    bool completed;
    float lastRespawnTime = float.NegativeInfinity;

    Vector3 LateralLocal => Vector3.Cross(Vector3.up, axisLocal).normalized;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        player = p.transform;
        health = p.GetComponent<PlayerHealth>();
        move = p.GetComponent<ThirdPersonController>();
        cc = p.GetComponent<CharacterController>();
        if (health != null) savedKillY = health.killY;
    }

    void Update()
    {
        // 先看玩家踩在哪一顆石頭上（用上一幀的位置），石頭動完再把玩家帶著走
        Motion standing = player != null && health != null && !health.IsDead ? StandingOn() : null;
        AnimateStones();
        if (standing != null && standing.lastWorldDelta.sqrMagnitude > 0f)
        {
            // 直接搬動玩家，不要用 CharacterController.Move：
            // Move 一次只往上 / 往旁邊動一點點，腳底下沒有碰到東西，CharacterController 會把「站在地上」改成 false，
            // 移動腳本和動畫就以為玩家在空中 → 一直切到「往下掉」的動作（看起來像一直倒下），也跳不起來
            player.position += standing.lastWorldDelta;
            Physics.SyncTransforms();
        }

        if (player == null || health == null || health.IsDead) return;
        Vector3 l = transform.InverseTransformPoint(player.position);
        float a = Vector3.Dot(l, axisLocal);
        float b = Vector3.Dot(l, LateralLocal);
        bool inside = Mathf.Abs(a) <= halfSize && Mathf.Abs(b) <= halfSize;
        KeepKillYBelow(inside);
        if (!inside) return; // 不在這間房間裡

        if (l.y < -fallDepth)
        {
            if (Time.time - lastRespawnTime > 0.5f) Respawn();
            return;
        }

        bool grounded = move == null || move.IsGrounded;
        bool onLedge = Mathf.Abs(a) >= halfSize - ledgeDepth && l.y > -0.5f && l.y < 1.5f && grounded;
        if (!onLedge) return;

        int side = a > 0f ? 1 : -1;
        if (firstLedge == 0)
        {
            firstLedge = side;
            if (!string.IsNullOrEmpty(enterTitle)) VictoryBanner.ShowSmall(enterTitle, enterSubtitle);
        }
        else if (!completed && side != firstLedge)
        {
            completed = true;
            if (!string.IsNullOrEmpty(clearTitle)) VictoryBanner.ShowSmall(clearTitle, clearSubtitle);
        }
        lastLedge = side;
    }

    /// <summary>
    /// 玩家原本「掉到 killY 以下就死掉」：在這間房間裡把它暫時壓到判定線底下，
    /// 讓這裡的「傳送回平台」先發生（站在很低的石頭上也不會被誤判死亡）。離開房間就恢復。
    /// </summary>
    void KeepKillYBelow(bool inside)
    {
        if (float.IsNaN(savedKillY)) return;
        if (inside)
        {
            float want = Mathf.Min(savedKillY, transform.position.y - fallDepth - 4f);
            if (health.killY != want) health.killY = want;
            loweredKillY = true;
        }
        else if (loweredKillY)
        {
            health.killY = savedKillY;
            loweredKillY = false;
        }
    }

    void OnDisable()
    {
        if (loweredKillY && health != null) health.killY = savedKillY;
        loweredKillY = false;
    }

    // ---------------- 石頭的動畫 ----------------

    void AddMotion(Transform t, System.Random rng)
    {
        float ang = Range(rng, 0f, Mathf.PI * 2f);
        var m = new Motion
        {
            t = t,
            col = t.GetComponent<Collider>(),
            basePos = t.localPosition,
            baseRot = t.localRotation,
            swayDir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)),
            bobSpeed = Mathf.PI * 2f / Range(rng, period.x, period.y),
            swaySpeed = Mathf.PI * 2f / Range(rng, period.x, period.y),
            bobPhase = Range(rng, 0f, 6.283f),
            swayPhase = Range(rng, 0f, 6.283f),
            tiltPhase = Range(rng, 0f, 6.283f),
        };
        motions.Add(m);
        if (m.col != null) byCollider[m.col] = m;
    }

    void AnimateStones()
    {
        if (motions.Count == 0) return;
        float time = Time.time;
        float bob = bobJumps * jumpHeight;
        foreach (var m in motions)
        {
            if (m.t == null) continue;
            Vector3 p = m.basePos
                      + Vector3.up * (Mathf.Sin(time * m.bobSpeed + m.bobPhase) * bob)
                      + m.swayDir * (Mathf.Sin(time * m.swaySpeed + m.swayPhase) * swayDistance);
            Quaternion r = m.baseRot * Quaternion.Euler(Mathf.Sin(time * m.swaySpeed + m.tiltPhase) * tiltDegrees, 0f,
                                                        Mathf.Cos(time * m.bobSpeed + m.tiltPhase) * tiltDegrees);
            Vector3 before = m.t.position;
            m.t.SetLocalPositionAndRotation(p, r);
            m.lastWorldDelta = m.t.position - before;
        }
        Physics.SyncTransforms(); // 碰撞的位置馬上更新（玩家這一幀的移動要用新的位置）
    }

    /// <summary>玩家腳底下是哪一顆會動的石頭（沒有 = null）</summary>
    Motion StandingOn()
    {
        if (motions.Count == 0 || move == null || !move.IsGrounded) return null;
        Vector3 feet = new Vector3(player.position.x, health.FeetY, player.position.z);
        if (!Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, out var hit, 0.8f, ~0, QueryTriggerInteraction.Ignore))
            return null;
        return byCollider.TryGetValue(hit.collider, out var m) ? m : null;
    }

    /// <summary>掉下去了：傳送回最後站過的那一端平台（面向房間），扣一點血</summary>
    void Respawn()
    {
        lastRespawnTime = Time.time;
        int side = lastLedge != 0 ? lastLedge : -1;
        Vector3 local = axisLocal * (side * (halfSize - ledgeDepth * 0.5f)) + Vector3.up * 1f;
        Vector3 pos = transform.TransformPoint(local);
        Quaternion rot = Quaternion.LookRotation(transform.TransformDirection(-axisLocal * side), Vector3.up);

        if (cc != null) cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        player.SetPositionAndRotation(pos, rot);
        if (cc != null) cc.enabled = true;
        if (move != null) move.ResetMotion();

        float dmg = Mathf.Min(fallDamage, health.Health - 1f);
        if (dmg > 0f) health.TakeDamage(dmg, Vector3.zero, true, "掉下去了");
        GameAudio.Play("SFX/RPG/dropLeather", pos, 0.8f, 0.7f, 0.05f, 0.3f);
        if (Camera.main != null && Camera.main.TryGetComponent(out ThirdPersonCamera cam)) cam.SnapToTarget();
    }

    // =====================================================================
    //  產生浮空的石頭
    // =====================================================================

    struct Stone { public Vector2 ab; public float y, r; }

    /// <summary>
    /// 在 room 裡面鋪滿浮空的石頭。axisLocal = 從一個門到對面的門的方向（房間本地座標）。
    /// stone = 石板的材質（用 Boss 競技場地板的，沒有就用灰色）；rock 目前沒用到（保留給之後想加岩塊時用）。
    /// </summary>
    public static ParkourRoom Build(GameObject room, Vector3 axisLocal, float halfSize, float ledgeDepth,
                                    System.Random rng, Material stone, Material rock)
    {
        var pr = room.AddComponent<ParkourRoom>();
        pr.axisLocal = axisLocal.normalized;
        pr.halfSize = halfSize;
        pr.ledgeDepth = ledgeDepth;

        // 用玩家實際的跳躍能力來判斷「跳不跳得過去」
        var jump = JumpModel.FromPlayer();
        pr.jumpHeight = jump.height;

        var parent = new GameObject("FloatingStones").transform;
        parent.SetParent(room.transform, false);
        Vector3 lateral = pr.LateralLocal;
        Vector3 ToLocal(Vector2 ab, float y) => pr.axisLocal * ab.x + lateral * ab.y + Vector3.up * y;

        var field = PlanField(rng, halfSize, ledgeDepth, jump, pr.lowestJumps, pr.highestJumps);
        float lowest = 0f;
        foreach (var st in field)
        {
            var go = MakeStone(parent, ToLocal(st.ab, st.y), st.r, rng, stone, true);
            pr.AddMotion(go.transform, rng);
            lowest = Mathf.Min(lowest, st.y);
        }
        // 掉到「最低的石頭再往下 bob + 2.5m」才算掉下去：站在最低的石頭上不會被判定成掉下去
        pr.fallDepth = -lowest + pr.bobJumps * pr.jumpHeight + 2.5f;

        // 裝飾：掉下去的判定線再往下幾顆比較小的浮石（沒有碰撞，踩不到），讓整個空間看起來是浮空的
        int decor = 5 + rng.Next(4);
        for (int i = 0; i < decor; i++)
        {
            var ab = new Vector2(Range(rng, -halfSize + 3f, halfSize - 3f), Range(rng, -halfSize + 3f, halfSize - 3f));
            float y = -pr.fallDepth - Range(rng, 1.5f, 4f);
            var go = MakeStone(parent, ToLocal(ab, y), Range(rng, 0.6f, 1.3f), rng, stone, false);
            pr.AddMotion(go.transform, rng);
        }
        return pr;
    }

    /// <summary>玩家跳得多高、多遠（用來判斷兩顆石頭之間跳不跳得過去）</summary>
    struct JumpModel
    {
        public float height, gravity, speed;

        public static JumpModel FromPlayer()
        {
            var m = new JumpModel { height = 1.6f, gravity = 25f, speed = 4.5f };
            var p = GameObject.FindWithTag("Player");
            var c = p != null ? p.GetComponent<ThirdPersonController>() : null;
            if (c != null)
            {
                m.height = Mathf.Max(0.5f, c.jumpHeight);
                m.gravity = Mathf.Max(1f, Mathf.Abs(c.gravity));
                m.speed = Mathf.Max(1f, c.walkSpeed);
            }
            return m;
        }

        /// <summary>
        /// 用「走路速度」起跳，落在高 dy 公尺的地方，水平最多跳得過多寬的空隙（留 15% 餘裕，衝刺跳會更遠）。
        /// 太高跳不上去回傳 -1。
        /// </summary>
        public float MaxGap(float dy)
        {
            if (dy > height * 0.8f) return -1f;
            float vy = Mathf.Sqrt(2f * gravity * height);
            float t = (vy + Mathf.Sqrt(Mathf.Max(0f, vy * vy - 2f * gravity * dy))) / gravity;
            return speed * t * 0.85f;
        }
    }

    /// <summary>
    /// 整片虛空均勻地鋪滿石頭（約 4.5m 一格，位置、大小隨機偏移），高度各自隨機
    /// （從平台往下 lowJumps 個跳躍高度、往上 highJumps 個跳躍高度之間）。
    /// 沒有特定的「路」，但一定檢查過：從一端的平台一路跳得到對面的平台（往上跳不能超過跳躍高度、空隙不能太寬）。
    /// 排不出來就把高低差縮小一點再試。ab.x = 沿著門到門的方向、ab.y = 左右；y = 高度（平台 = 0）。
    /// </summary>
    static List<Stone> PlanField(System.Random rng, float half, float ledge, JumpModel jump, float lowJumps, float highJumps)
    {
        float aS = -(half - ledge), aE = half - ledge;
        float bMax = half - 1.5f;
        const float Cell = 4.5f;
        float za0 = aS + 1.6f, za1 = aE - 1.6f;
        int na = Mathf.Max(1, Mathf.FloorToInt((za1 - za0) / Cell));
        int nb = Mathf.Max(1, Mathf.FloorToInt(2f * bMax / Cell));
        float sa = (za1 - za0) / na, sb = 2f * bMax / nb;

        var list = new List<Stone>();
        for (int i = 0; i < na; i++)
            for (int j = 0; j < nb; j++)
            {
                var ab = new Vector2(za0 + sa * (i + 0.5f) + Range(rng, -0.9f, 0.9f), -bMax + sb * (j + 0.5f) + Range(rng, -0.9f, 0.9f));
                float r = Range(rng, 1.3f, 1.8f);
                // 不要碰到兩端的平台和牆
                r = Mathf.Min(r, ab.x - aS - 0.8f, aE - ab.x - 0.8f, bMax - Mathf.Abs(ab.y));
                list.Add(new Stone { ab = ab, r = r });
            }
        // 石頭之間至少留 0.8m 的空隙
        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                float d = Vector2.Distance(list[i].ab, list[j].ab);
                float need = list[i].r + list[j].r + 0.8f;
                if (d >= need) continue;
                float k = Mathf.Max(0f, d - 0.8f) / (list[i].r + list[j].r);
                var a = list[i]; a.r *= k; list[i] = a;
                var b = list[j]; b.r *= k; list[j] = b;
            }
        list.RemoveAll(st => st.r < 0.9f);

        float lo = -lowJumps * jump.height, hi = highJumps * jump.height;
        for (int attempt = 0; attempt < 120; attempt++)
        {
            float scale = Mathf.Pow(0.92f, attempt / 4); // 一直排不出來就慢慢縮小高低差
            for (int i = 0; i < list.Count; i++)
            {
                var st = list[i];
                st.y = Range(rng, lo, hi) * scale;
                list[i] = st;
            }
            if (Reachable(list, aS, aE, jump)) return list;
        }
        // 保險：全部跟平台一樣高（格子夠密，一定跳得過）
        for (int i = 0; i < list.Count; i++) { var st = list[i]; st.y = 0f; list[i] = st; }
        return list;
    }

    /// <summary>從 a = aS 的平台出發，能不能一路跳到 a = aE 的平台</summary>
    static bool Reachable(List<Stone> list, float aS, float aE, JumpModel jump)
    {
        var seen = new bool[list.Count];
        var stack = new Stack<int>();
        for (int i = 0; i < list.Count; i++)
            if (list[i].ab.x - list[i].r - aS <= jump.MaxGap(list[i].y)) { seen[i] = true; stack.Push(i); }
        while (stack.Count > 0)
        {
            var s = list[stack.Pop()];
            if (aE - (s.ab.x + s.r) <= jump.MaxGap(-s.y)) return true;
            for (int j = 0; j < list.Count; j++)
            {
                if (seen[j]) continue;
                var t = list[j];
                float gap = Vector2.Distance(s.ab, t.ab) - s.r - t.r;
                if (gap <= jump.MaxGap(t.y - s.y)) { seen[j] = true; stack.Push(j); }
            }
        }
        return false;
    }

    static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

    /// <summary>一顆浮石：只有一塊不規則的石板（跟 Boss 房的地板一樣），站得上去</summary>
    static GameObject MakeStone(Transform parent, Vector3 localTop, float radius, System.Random rng,
                                Material stone, bool solid)
    {
        var go = new GameObject(solid ? "FloatingStone" : "FloatingStone_Decor");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localTop;
        go.transform.localRotation = Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f);

        var outline = RandomOutline(rng, radius);
        float thickness = solid ? 0.9f : 0.6f;
        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = ArenaMeshes.BuildSlab(outline, thickness, 0.12f, new Vector2(localTop.x, localTop.z), 1.5f);
        var mr = go.AddComponent<MeshRenderer>();
        if (stone != null) mr.sharedMaterial = stone;
        if (solid)
        {
            go.AddComponent<MeshCollider>().sharedMesh = ArenaMeshes.BuildSlabCollider(outline, thickness);
            // 會動的碰撞體要有 Kinematic Rigidbody（位置由程式控制）
            var rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        return go;
    }

    /// <summary>隨機的凸多邊形輪廓（逆時針），大約是半徑 radius 的圓</summary>
    static Vector2[] RandomOutline(System.Random rng, float radius)
    {
        int n = 7 + rng.Next(3);
        var pts = new List<Vector2>();
        for (int i = 0; i < n; i++)
        {
            float a = (i + Range(rng, -0.25f, 0.25f)) / n * Mathf.PI * 2f;
            float r = radius * Range(rng, 0.85f, 1.05f);
            pts.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
        }
        return ConvexHull(pts);
    }

    /// <summary>凸包（Andrew's monotone chain），回傳逆時針的頂點</summary>
    static Vector2[] ConvexHull(List<Vector2> pts)
    {
        pts.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));
        var hull = new List<Vector2>();
        float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
        foreach (var p in pts)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        int lower = hull.Count + 1;
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            var p = pts[i];
            while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        hull.RemoveAt(hull.Count - 1);
        return hull.ToArray();
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(Vector3.down * fallDepth, new Vector3(halfSize * 2f, 0.05f, halfSize * 2f));
    }
}
