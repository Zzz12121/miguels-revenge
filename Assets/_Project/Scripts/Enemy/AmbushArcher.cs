using System.Collections;
using UnityEngine;

/// <summary>
/// 埋伏弓箭手（外觀用維京村莊敵艦的 BoatArcher：Erika Archer + 拉弓動畫）。
///   平常蹲在巨石 / 樹後（hidePoint，ArcherCoverPose 蹲姿）→ 收到 ArcherAmbushGroup 的命令後站起來、從掩體旁邊跨出去（FindPeek）
///   射完之後動態找掩體（FindCover）：躲在附近石頭 / 樹「玩家對面」那一側，越遠離玩家越好；玩家太近時優先往遠處撤，
///   找不到掩體就往遠離玩家的方向退。移動會繞過障礙物、貼著地面、不會跑下懸崖。
///   → 轉向玩家拉弓放箭 → 轉身跑回掩體後面蹲下 → 隔一下再出來。看不到玩家（被石頭擋住）就不放箭。
///   跑步用動畫包的 Run / Walk（Animator 沒有這兩個狀態時退回用滑的）。
/// 可以被玩家的刀和箭打（IDamageable）：被打 hitsToKill 下 → 布娃娃（HumanoidRagdoll）被打飛、四肢癱軟倒地。
/// 投降（Surrender）：其他弓箭手都倒下後，最後一名會走出來跪下、把弓遞給玩家，按互動鍵接過 → 獲得弓箭技能，然後逃走。
/// </summary>
[RequireComponent(typeof(BoatArcherVisual))]
public class AmbushArcher : MonoBehaviour, IDamageable
{
    [Header("站位（世界座標，執行時會貼齊地面）")]
    public Vector3 hidePoint;
    public Vector3 peekPoint;

    [Header("射擊")]
    public float arrowDamage = 10f;
    public float arrowSpeed = 28f;
    [Tooltip("超過這個距離不射")]
    public float range = 40f;
    [Tooltip("兩次射擊之間躲在掩體後面幾秒（會隨機 ±0.5）")]
    public float hideSeconds = 1.6f;
    [Header("移動")]
    [Tooltip("跑出 / 跑回掩體的速度 (m/s)")]
    public float runSpeed = 3.8f;
    [Tooltip("投降時走出來的速度 (m/s)")]
    public float walkSpeed = 1.5f;
    [Tooltip("轉身 90 度花幾秒")]
    public float turnSeconds = 0.22f;
    [Tooltip("站起來 / 蹲下花幾秒")]
    public float crouchSeconds = 0.3f;
    [Header("命中率")]
    [Tooltip("玩家很近或站著不動時，這一箭瞄準命中的機率")]
    [Range(0f, 1f)] public float hitChanceEasy = 0.9f;
    [Tooltip("玩家在遠處跑動時，這一箭瞄準命中的機率")]
    [Range(0f, 1f)] public float hitChanceMoving = 0.35f;
    [Tooltip("比這個距離近 = 很近 (m)")]
    public float closeRange = 12f;
    [Tooltip("水平速度低於這個值 = 站著不動 (m/s)")]
    public float stillSpeed = 0.6f;
    [Tooltip("故意射偏時，偏離玩家多遠 (m)：射在身邊擦過去，看起來是差一點")]
    public Vector2 missOffset = new Vector2(1.5f, 2.6f);

    [Header("血量 / 倒地")]
    public int hitsToKill = 2;
    [Tooltip("被打倒時往後飛的速度 (m/s)")]
    public float knockbackSpeed = 7f;
    [Tooltip("被打倒時往上彈的速度 (m/s)")]
    public float knockupSpeed = 3.5f;

    [Header("投降")]
    [Tooltip("離投降的弓箭手多近才能接過弓 (m)")]
    public float handOverRange = 3.2f;
    [Tooltip("說完話之後，在附近撒幾顆可以磨成石粉的石頭")]
    public int powderStones = 6;
    [Tooltip("石頭撒在投降位置的多遠範圍內 (m)")]
    public Vector2 powderStoneRadius = new Vector2(3f, 11f);
    public string speakerName = "投降的弓箭手";

    public bool IsDead { get; private set; }
    public bool Active { get; private set; }
    public bool Surrendered { get; private set; }
    public bool HandedOver { get; private set; }

    BoatArcherVisual visual;
    ArcherCoverPose pose;
    CapsuleCollider body;
    PlayerHealth player;
    Coroutine loop;
    int hits;
    bool exposed, offering;

    static readonly int RunHash = Animator.StringToHash("Run"), WalkHash = Animator.StringToHash("Walk"), IdleHash = Animator.StringToHash("Idle");

    void Awake()
    {
        visual = GetComponent<BoatArcherVisual>();
        pose = GetComponent<ArcherCoverPose>();
        if (pose == null) pose = gameObject.AddComponent<ArcherCoverPose>();
        body = GetComponent<CapsuleCollider>();
        if (body == null)
        {
            body = gameObject.AddComponent<CapsuleCollider>();
            body.center = new Vector3(0f, 0.9f, 0f);
            body.height = 1.8f;
            body.radius = 0.35f;
        }
        hidePoint = Grounded(hidePoint);
        peekPoint = Grounded(peekPoint);
        transform.position = hidePoint;
        transform.rotation = CoverFacing();
        pose.crouch = 1f;   // 一開始就蹲在掩體後面
    }

    static Vector3 Grounded(Vector3 p)
    {
        var hitsDown = Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 ground = p;
        foreach (var h in hitsDown)
        {
            if (h.collider.GetComponentInParent<AmbushArcher>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            float d = Mathf.Abs(h.point.y - p.y);
            if (d < best) { best = d; ground = h.point; }
        }
        return ground;
    }

    /// <summary>蹲在掩體後面時面向的方向：朝著玩家那一側（掩體在中間），隨時準備探出去</summary>
    Quaternion CoverFacing()
    {
        if (player != null) return FacePlayer(transform.position);
        Vector3 d = peekPoint - hidePoint; d.y = 0f;
        return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : transform.rotation;
    }

    /// <summary>開始埋伏攻擊（delay 秒後第一次跑出來）</summary>
    public void Engage(float delay)
    {
        if (IsDead || Active || Surrendered) return;
        Active = true;
        if (loop != null) StopCoroutine(loop);
        loop = StartCoroutine(Loop(delay));
    }

    /// <summary>玩家死掉 / 重生時：跑回掩體蹲下待命</summary>
    public void Disengage()
    {
        if (IsDead || Surrendered) return;
        Active = false;
        if (loop != null) StopCoroutine(loop);
        loop = StartCoroutine(Retreat());
    }

    // ---------------- 找掩體 ----------------

    [Header("找掩體（動態）")]
    [Tooltip("在身邊多遠內找可以躲的石頭 / 樹 (m)")]
    public float coverSearchRadius = 15f;
    [Tooltip("一次最多跑多遠去換掩體 (m)")]
    public float maxRelocate = 16f;
    [Tooltip("玩家比這個距離近就優先往遠離玩家的方向撤 (m)")]
    public float tooClose = 9f;
    [Tooltip("找不到掩體時，往遠離玩家的方向退多遠 (m)")]
    public float fleeStep = 7f;

    Vector3 coverPos;          // 現在躲的位置
    Vector3 coverObstacle;     // 擋在玩家和自己中間的那個東西（中心）
    float coverObstacleRadius; // 它的半徑（探頭時往旁邊跨多遠）
    bool hasCover;
    static readonly Collider[] overlap = new Collider[48];

    bool IgnoreForPhysics(Collider c) =>
        c == null || c.isTrigger || c.GetComponentInParent<AmbushArcher>() != null || c.GetComponentInParent<PlayerHealth>() != null
        || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic);

    /// <summary>p 附近的地面（和 refY 高低差超過 maxDy 或太陡 = 沒有地面）</summary>
    bool TryGround(Vector3 p, float refY, float maxDy, out Vector3 ground)
    {
        ground = p;
        float best = float.MaxValue; bool found = false;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, refY + 6f, p.z), Vector3.down, 14f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IgnoreForPhysics(h.collider) || h.normal.y < 0.6f) continue;
            float d = Mathf.Abs(h.point.y - refY);
            if (d < best) { best = d; ground = h.point; found = true; }
        }
        return found && best <= maxDy;
    }

    /// <summary>站在 g 不會卡進石頭、樹、牆裡</summary>
    bool BodyFits(Vector3 g)
    {
        int n = Physics.OverlapCapsuleNonAlloc(g + Vector3.up * 0.55f, g + Vector3.up * 1.5f, 0.3f, overlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!IgnoreForPhysics(overlap[i]) && !(overlap[i] is TerrainCollider)) return false;
        return true;
    }

    /// <summary>a → b 之間沒有障礙物擋路、地面連續（不會跑下懸崖）</summary>
    bool Walkable(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.05f) return true;
        Vector3 dir = d / dist;
        foreach (var h in Physics.CapsuleCastAll(a + Vector3.up * 0.7f, a + Vector3.up * 1.5f, 0.28f, dir, dist, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreForPhysics(h.collider) && !(h.collider is TerrainCollider)) return false;
        float y = a.y;
        for (float s = 1.2f; s < dist; s += 1.2f)
        {
            if (!TryGround(a + dir * s, y, 1.4f, out Vector3 g)) return false;
            y = g.y;
        }
        return Mathf.Abs(b.y - y) <= 1.6f;
    }

    /// <summary>從玩家那邊看不到 g（蹲著的頭被東西擋住）</summary>
    bool HiddenFromPlayer(Vector3 g) => Blocked(PlayerChest, g + Vector3.up * 0.95f);

    /// <summary>站在 g 拉弓看得到玩家</summary>
    bool CanShootFrom(Vector3 g) => !Blocked(g + Vector3.up * 1.45f, PlayerChest);

    bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; float dist = d.magnitude;
        if (dist < 0.01f) return false;
        foreach (var h in Physics.RaycastAll(from, d / dist, dist, ~0, QueryTriggerInteraction.Ignore))
            if (!IgnoreForPhysics(h.collider)) return true;
        return false;
    }

    /// <summary>
    /// 找下一個躲的位置：附近的石頭 / 樹（夠高、夠粗），躲在它「玩家對面」那一側。
    /// 分數：被擋住（玩家看不到）最重要，其次離玩家越遠越好、離自己太遠扣分；跑過去的路要通。
    /// 都不行就往遠離玩家的方向退 fleeStep。
    /// </summary>
    bool FindCover(bool flee, out Vector3 spot, out Vector3 obstacle, out float obstacleRadius)
    {
        spot = transform.position; obstacle = transform.position; obstacleRadius = 0.6f;
        if (player == null) return false;
        if (body != null) body.enabled = false;
        Vector3 me = transform.position, pp = player.transform.position;
        float myDist = Flat(me - pp).magnitude;
        var cands = new System.Collections.Generic.List<(float score, Vector3 g, Vector3 obs, float r)>();

        // 先用便宜的檢查（地面、站得下、玩家看不看得到）打分數，最後只對前幾名檢查路徑通不通
        void Consider(Vector3 cand, Vector3 obs, float r)
        {
            if (Flat(cand - me).magnitude > maxRelocate) return;
            if (!TryGround(cand, me.y, 3f, out Vector3 g) || !BodyFits(g)) return;
            float dp = Flat(g - pp).magnitude;
            if (dp < 4f) return;   // 不要躲到玩家身邊
            bool hidden = HiddenFromPlayer(g);
            float score = (hidden ? 0f : 60f)
                          - dp * (flee ? 2.2f : 1f)
                          + Flat(g - me).magnitude * 0.35f
                          + (dp < myDist - 0.5f ? 25f : 0f);   // 往玩家那邊靠近的位置扣很多分
            cands.Add((score, g, obs, r));
        }

        // 1. 身邊的障礙物：躲在它背對玩家的那一側（正後方，和左右偏 35 度）
        int n = Physics.OverlapSphereNonAlloc(me, coverSearchRadius, overlap, ~0, QueryTriggerInteraction.Ignore);
        var obstacles = new System.Collections.Generic.List<(Vector3 c, float r)>();
        for (int i = 0; i < n; i++)
        {
            var c = overlap[i];
            if (IgnoreForPhysics(c) || c is TerrainCollider) continue;
            var bnd = c.bounds;
            float r = Mathf.Max(bnd.extents.x, bnd.extents.z);
            if (bnd.size.y < 1.2f || r < 0.35f || r > 7f) continue;   // 太矮、太細、太大（牆、整片山壁）都不要
            obstacles.Add((new Vector3(bnd.center.x, me.y, bnd.center.z), r));
        }
        foreach (var (c, r) in obstacles)
        {
            Vector3 away = Flat(c - pp);
            if (away.sqrMagnitude < 0.01f) continue;
            away.Normalize();
            foreach (float ang in new[] { 0f, 35f, -35f })
                Consider(c + Quaternion.Euler(0f, ang, 0f) * away * (r + 0.75f), c, r);
        }
        // 2. 原本的埋伏位置
        Consider(hidePoint, hidePoint + Flat(pp - hidePoint).normalized * 1.5f, 0.8f);
        // 3. 找不到掩體：往遠離玩家的方向退
        Vector3 back = Flat(me - pp); if (back.sqrMagnitude < 0.01f) back = -transform.forward; back.Normalize();
        foreach (float ang in new[] { 0f, 40f, -40f, 75f, -75f })
        {
            Vector3 cand = me + Quaternion.Euler(0f, ang, 0f) * back * fleeStep;
            Consider(cand, cand - back * 1.5f, 0.6f);
        }

        cands.Sort((x, y) => x.score.CompareTo(y.score));
        bool found = false;
        for (int i = 0; i < cands.Count && i < 8; i++)
        {
            var c = cands[i];
            Vector3 via = Detour(me, c.g, c.obs, c.r);
            if (!Walkable(me, c.g) && !(Walkable(me, via) && Walkable(via, c.g))) continue;
            spot = c.g; obstacle = c.obs; obstacleRadius = c.r; found = true;
            break;
        }
        if (body != null) body.enabled = !IsDead;
        return found;
    }

    /// <summary>直線過不去時繞過障礙物的中途點（往障礙物較近的那一側繞）</summary>
    static Vector3 Detour(Vector3 a, Vector3 b, Vector3 obs, float r)
    {
        Vector3 ab = Flat(b - a);
        if (ab.sqrMagnitude < 0.01f) return b;
        Vector3 side = Vector3.Cross(Vector3.up, ab.normalized);
        float sign = Vector3.Dot(Flat(obs - a), side) > 0f ? -1f : 1f;
        return new Vector3(obs.x, a.y, obs.z) + side * sign * (r + 1.2f);
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>探頭射擊的位置：從掩體往左右（垂直玩家方向）跨出去，選第一個看得到玩家的；都不行就原地站起來試試</summary>
    bool FindPeek(out Vector3 peek)
    {
        peek = transform.position;
        if (player == null) return false;
        if (body != null) body.enabled = false;
        Vector3 me = transform.position;
        Vector3 to = Flat(player.transform.position - me);
        Vector3 side = Vector3.Cross(Vector3.up, to.normalized);
        float sign0 = Random.value < 0.5f ? 1f : -1f;
        float baseStep = Mathf.Max(1.2f, coverObstacleRadius + 0.6f);
        bool ok = false;
        if (CanShootFrom(me)) { peek = me; ok = true; }
        else
            foreach (float k in new[] { 1f, 1.6f, 2.3f })
            {
                foreach (float sgn in new[] { sign0, -sign0 })
                {
                    Vector3 cand = me + side * (sgn * baseStep * k) + to.normalized * 0.4f;
                    if (!TryGround(cand, me.y, 1.5f, out Vector3 g) || !BodyFits(g) || !Walkable(me, g)) continue;
                    if (!CanShootFrom(g)) continue;
                    peek = g; ok = true; break;
                }
                if (ok) break;
            }
        if (body != null) body.enabled = !IsDead;
        return ok;
    }

    // ---------------- 移動 ----------------

    bool HasState(int hash) => visual.HasAnimator && visual.animator.HasState(0, hash);

    void PlayLoco(bool run)
    {
        if (!visual.HasAnimator) return;
        visual.CancelDraw();
        int h = run ? RunHash : WalkHash;
        if (!HasState(h)) h = run ? WalkHash : RunHash;
        if (HasState(h)) visual.animator.CrossFadeInFixedTime(h, 0.15f, 0);
    }

    void PlayIdle()
    {
        if (visual.HasAnimator && HasState(IdleHash)) visual.animator.CrossFadeInFixedTime(IdleHash, 0.2f, 0);
    }

    /// <summary>
    /// 原地轉身：轉的角度大（超過 50 度）時腳要踏步（播 Walk），不然整個人像在地上滑著轉。
    /// keepStepping = 轉完緊接著要跑，不切回待機。
    /// </summary>
    IEnumerator TurnTo(Quaternion target, bool keepStepping = false)
    {
        Quaternion from = transform.rotation;
        float ang = Quaternion.Angle(from, target);
        if (ang < 3f) { transform.rotation = target; yield break; }
        bool step = ang > 50f && HasState(WalkHash);
        if (step) { visual.CancelDraw(); visual.animator.CrossFadeInFixedTime(WalkHash, 0.1f, 0); }
        float dur = turnSeconds * Mathf.Clamp(ang / 90f, 0.5f, 1.8f);
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            transform.rotation = Quaternion.Slerp(from, target, SmoothTween.Evaluate(SmoothTween.Ease.InOutSine, t / dur));
            yield return null;
        }
        transform.rotation = target;
        if (step && !keepStepping) PlayIdle();
    }

    /// <summary>移動到 target：直線被擋住就先繞到障礙物旁邊的中途點</summary>
    IEnumerator MoveTo(Vector3 target, float speed, bool run, bool detour = true)
    {
        if (detour && body != null)
        {
            body.enabled = false;
            bool direct = Walkable(transform.position, target);
            Vector3 via = Detour(transform.position, target, hasCover ? coverObstacle : (transform.position + target) * 0.5f, hasCover ? coverObstacleRadius : 1f);
            bool viaOk = !direct && Walkable(transform.position, via) && Walkable(via, target);
            body.enabled = !IsDead;
            if (viaOk)
            {
                TryGround(via, transform.position.y, 2f, out via);
                yield return MoveLeg(via, speed, run, false);
            }
        }
        yield return MoveLeg(target, speed, run, true);
    }

    /// <summary>先轉身面向目的地，再用跑步（或走路）動畫移過去，腳一路貼著地面；last = 到了切回待機</summary>
    IEnumerator MoveLeg(Vector3 target, float speed, bool run, bool last)
    {
        Vector3 start = transform.position;
        Vector3 flat = Flat(target - start);
        float dist = flat.magnitude;
        if (dist < 0.05f) { transform.position = target; if (last) PlayIdle(); yield break; }
        yield return TurnTo(Quaternion.LookRotation(flat), true);
        PlayLoco(run);
        float dur = dist / Mathf.Max(0.1f, speed);
        float y = start.y;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float u = t / dur;
            float e = last ? Mathf.Lerp(u, u * u * (3f - 2f * u), 0.4f) : Mathf.Lerp(u, 1f - (1f - u) * (1f - u), 0.4f);
            Vector3 p = Vector3.Lerp(start, target, e);
            // 貼地：每一幀往下找地面（坡地上不會浮在空中 / 陷進地裡）
            if (body != null) body.enabled = false;
            if (TryGround(p, y, 1.2f, out Vector3 g)) y = g.y; else y = Mathf.Lerp(start.y, target.y, e);
            if (body != null) body.enabled = !IsDead;
            p.y = Mathf.Lerp(transform.position.y, y, 0.6f);
            transform.position = p;
            yield return null;
        }
        transform.position = target;
        if (last) PlayIdle();
    }

    /// <summary>站起來 → 0 / 蹲下 → 1，等姿勢差不多到位</summary>
    IEnumerator Crouch(bool down)
    {
        pose.crouch = down ? 1f : 0f;
        yield return new WaitForSeconds(crouchSeconds);
    }

    /// <summary>
    /// 躲起來：找一個玩家看不到、離玩家比較遠的掩體（玩家太近時更積極往遠處撤），跑過去，面向玩家那側蹲下。
    /// </summary>
    IEnumerator Retreat()
    {
        exposed = false;
        visual.CancelDraw();
        if (player == null) player = PlayerHealth.Find();
        Vector3 target = hidePoint;
        if (player != null)
        {
            bool flee = Flat(player.transform.position - transform.position).magnitude < tooClose;
            if (FindCover(flee, out Vector3 spot, out Vector3 obs, out float r))
            {
                target = spot; coverObstacle = obs; coverObstacleRadius = r; hasCover = true;
            }
        }
        coverPos = target;
        yield return MoveTo(target, runSpeed, true);
        yield return TurnTo(CoverFacing());
        yield return Crouch(true);
    }

    IEnumerator RetreatThenLoop(float extraWait)
    {
        yield return Retreat();
        yield return Loop(extraWait);
    }

    IEnumerator Loop(float delay)
    {
        yield return new WaitForSeconds(delay);
        while (Active && !IsDead && !Surrendered)
        {
            if (player == null) player = PlayerHealth.Find();
            if (player == null || player.IsDead) { yield return null; continue; }

            // 玩家衝到身邊了：不探頭，先往遠處撤
            if (Flat(player.transform.position - transform.position).magnitude < tooClose * 0.6f)
            {
                yield return Crouch(false);
                yield return Retreat();
                yield return new WaitForSeconds(0.3f);
                continue;
            }

            // 站起來、從掩體旁邊跨出去（找看得到玩家的位置），轉向玩家
            yield return Crouch(false);
            if (!FindPeek(out Vector3 peek))
            {
                // 這個掩體射不到玩家（玩家換了位置）：換一個掩體再說
                yield return Retreat();
                yield return new WaitForSeconds(0.4f);
                continue;
            }
            exposed = true;
            yield return MoveTo(peek, runSpeed * 0.8f, true, false);
            yield return TurnTo(FacePlayer(transform.position));

            // 拉弓、放箭（拉弓期間一直盯著玩家）
            if (InRange())
            {
                float release = visual.PlayDraw();
                for (float t = 0f; t < release && !IsDead; t += Time.deltaTime)
                {
                    transform.rotation = Quaternion.Slerp(transform.rotation, FacePlayer(transform.position), 10f * Time.deltaTime);
                    yield return null;
                }
                if (IsDead) yield break;
                if (Active && player != null && !player.IsDead && HasLineOfSight()) Shoot();
                yield return new WaitForSeconds(0.35f);
            }
            else yield return new WaitForSeconds(0.6f);

            // 轉身找掩體躲起來（可能是同一個，也可能換一個離玩家更遠的）
            yield return Retreat();
            yield return new WaitForSeconds(Mathf.Max(0.3f, hideSeconds + Random.Range(-0.5f, 0.5f)));
        }
    }

    Quaternion FacePlayer(Vector3 from)
    {
        if (player == null) return transform.rotation;
        Vector3 d = player.transform.position - from; d.y = 0f;
        return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d) : transform.rotation;
    }

    bool InRange() => player != null && Vector3.Distance(transform.position, player.transform.position) <= range;

    Vector3 PlayerChest => player.transform.position + Vector3.up * 1.1f;

    bool HasLineOfSight()
    {
        Vector3 from = visual.ArrowWorldOrigin, to = PlayerChest;
        Vector3 dir = to - from; float dist = dir.magnitude;
        foreach (var h in Physics.RaycastAll(from, dir / dist, dist, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.GetComponentInParent<AmbushArcher>() != null) continue;
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            return false;   // 被石頭 / 樹 / 地形擋住
        }
        return true;
    }

    /// <summary>這一箭會不會瞄準命中：玩家很近或站著不動 = hitChanceEasy（預設 90%），遠處跑動 = hitChanceMoving</summary>
    public float CurrentHitChance(out Vector3 playerVelocity)
    {
        playerVelocity = Vector3.zero;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) { playerVelocity = cc.velocity; playerVelocity.y = 0f; }
        bool still = playerVelocity.magnitude < stillSpeed;
        bool close = Vector3.Distance(transform.position, player.transform.position) <= closeRange;
        return still || close ? hitChanceEasy : hitChanceMoving;
    }

    /// <summary>
    /// 算出射中 aim 的發射方向：BoatProjectile 會受 gravity 往下掉，所以往上抬「飛行時間內會掉的高度」。
    /// 飛行時間和抬高互相影響，疊代幾次就收斂。
    /// </summary>
    public static Vector3 BallisticDirection(Vector3 origin, Vector3 aim, float speed, float gravity)
    {
        speed = Mathf.Max(speed, 1f);
        float t = Vector3.Distance(origin, aim) / speed;
        Vector3 d = aim - origin;
        for (int i = 0; i < 4; i++)
        {
            d = aim + Vector3.up * (0.5f * gravity * t * t) - origin;
            t = d.magnitude / speed;
        }
        return d.normalized;
    }

    void Shoot()
    {
        Vector3 origin = visual.ArrowWorldOrigin;
        Vector3 target = player.transform.position + Vector3.up * 1.0f;   // BoatProjectile 判定命中的那一點（胸口）
        bool hit = Random.value < CurrentHitChance(out Vector3 vel);
        float flyTime = Vector3.Distance(origin, target) / Mathf.Max(arrowSpeed, 1f);

        Vector3 aim;
        if (hit) aim = target + vel * flyTime;   // 完整提前量：玩家照原方向繼續跑也會中
        else
        {
            // 故意射偏：從玩家身邊擦過去（左右偏、有時偏高），看起來是差一點
            Vector3 toPlayer = target - origin; toPlayer.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, toPlayer.normalized);
            float sign = Random.value < 0.5f ? -1f : 1f;
            aim = target + vel * flyTime * 0.3f + side * (sign * Random.Range(missOffset.x, missOffset.y)) + Vector3.up * Random.Range(0f, 0.6f);
        }

        var arrow = EnemyBoat.CreateArrow(origin, Vector3.forward);
        var proj = arrow.AddComponent<BoatProjectile>();
        Vector3 dir = BallisticDirection(origin, aim, arrowSpeed, proj.gravity);
        arrow.transform.rotation = Quaternion.LookRotation(dir);
        proj.isArrow = true;
        proj.damage = arrowDamage;
        proj.speed = arrowSpeed;
        proj.velocity = dir * arrowSpeed;
        GameAudio.Play("SFX/RPG/drawKnife3", origin, 0.75f, 1.5f, 0.08f, 1f, 35f);
        CombatVFX.Puff(origin, dir, new Color(0.95f, 0.95f, 0.9f, 0.6f));
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || Surrendered || amount <= 0f) return;   // 投降了就不能再打
        hits++;
        if (player == null) player = PlayerHealth.Find();
        bool lethal = hits >= Mathf.Max(1, hitsToKill);
        // 噴血：往「離開攻擊者」的方向噴，致命一擊噴得更多
        Vector3 from = player != null ? player.transform.position + Vector3.up * 1.1f : hitPoint - transform.forward;
        Vector3 bleedAt = hitPoint;
        if ((bleedAt - transform.position).sqrMagnitude > 9f || bleedAt.y < transform.position.y + 0.3f)
            bleedAt = transform.position + Vector3.up * 1.2f;   // 打中的點太離譜（例如在腳底）就噴在胸口
        BloodSprayVFX.Spawn(bleedAt, bleedAt - from, lethal ? 2.4f : 1f);
        CombatVFX.Impact(hitPoint, new Color(1f, 0.85f, 0.7f));
        GameAudio.Play("SFX/RPG/knifeSlice", hitPoint, 0.8f, 1.1f, 0.1f, 1f, 30f);
        if (lethal) { Die(hitPoint); return; }
        SmoothTween.PunchScale(visual.animator != null ? visual.animator.transform : transform, 0.1f, 0.2f);
        // 被打到：嚇一跳，轉身跑回掩體，下一輪重新出來
        if (exposed && Active)
        {
            if (loop != null) StopCoroutine(loop);
            loop = StartCoroutine(RetreatThenLoop(hideSeconds));
        }
    }

    void Die(Vector3 hitPoint)
    {
        IsDead = true;
        Active = false;
        offering = false;
        StopAllCoroutines();
        loop = null;
        SmoothTween.Kill(transform);
        if (body != null) body.enabled = false;
        visual.CancelDraw();
        visual.enabled = false;
        pose.enabled = false;
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.9f, 0.8f, 0.08f, 1f, 30f);

        // 被打飛的方向：從玩家往弓箭手（沒有玩家就從打中的那一點往外）
        Vector3 from = player != null ? player.transform.position : hitPoint;
        Vector3 dir = transform.position - from; dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f) dir = -transform.forward;
        dir.Normalize();

        var cc = player != null ? player.GetComponent<CharacterController>() : null;
        var rd = visual.animator != null ? HumanoidRagdoll.Create(visual.animator, 60f, cc) : null;
        if (rd != null)
        {
            // 往後飛 + 往上彈，身體往後翻（繞著「往側邊」的軸轉）
            rd.Launch(dir * knockbackSpeed + Vector3.up * knockupSpeed, Vector3.Cross(Vector3.up, dir) * -5f);
            ThirdPersonCamera.ShakeMain(0.12f, 0.18f);
            StartCoroutine(RagdollRoutine(rd));
        }
        else StartCoroutine(FallbackDeath());
    }

    /// <summary>布娃娃倒地 → 停下來之後躺一下 → 沉進地面 → 消失</summary>
    IEnumerator RagdollRoutine(HumanoidRagdoll rd)
    {
        yield return new WaitForSeconds(0.6f);
        for (float t = 0f; t < 4f && !rd.Settled; t += 0.2f) yield return new WaitForSeconds(0.2f);
        yield return new WaitForSeconds(2.5f);
        rd.Freeze();
        Vector3 start = transform.position, end = start + Vector3.down * 1.4f;
        for (float t = 0f; t < 1.6f; t += Time.deltaTime)
        {
            transform.position = Vector3.Lerp(start, end, SmoothTween.Evaluate(SmoothTween.Ease.InCubic, t / 1.6f));
            yield return null;
        }
        Destroy(gameObject);
    }

    /// <summary>沒有 Humanoid 骨架時：照舊往後倒</summary>
    IEnumerator FallbackDeath()
    {
        Vector3 back = -transform.forward; back.y = 0f;
        Quaternion fallen = Quaternion.AngleAxis(-85f, transform.right) * transform.rotation;
        SmoothTween.Move(transform, transform.position + back.normalized * 0.4f, 0.55f, SmoothTween.Ease.OutCubic);
        SmoothTween.Rotate(transform, fallen, 0.55f, SmoothTween.Ease.InCubic);
        yield return new WaitForSeconds(1.6f);
        SmoothTween.Move(transform, transform.position + Vector3.down * 1.2f, 1.4f, SmoothTween.Ease.InCubic);
        yield return new WaitForSeconds(1.5f);
        Destroy(gameObject);
    }

    // ---------------- 投降、交出弓 ----------------

    /// <summary>不打了：走出掩體、跪下、把弓遞給玩家（ArcherAmbushGroup 在只剩這一名時呼叫）</summary>
    public void Surrender()
    {
        if (IsDead || Surrendered) return;
        Surrendered = true;
        Active = false;
        if (loop != null) StopCoroutine(loop);
        loop = StartCoroutine(SurrenderRoutine());
    }

    IEnumerator SurrenderRoutine()
    {
        visual.CancelDraw();
        PlayIdle();
        yield return new WaitForSeconds(0.4f);
        yield return Crouch(false);
        if (player == null) player = PlayerHealth.Find();
        // 走出掩體（慢慢走、表示沒有敵意），面向玩家跪下
        Vector3 toward = player != null ? Flat(player.transform.position - transform.position) : transform.forward;
        float step = Mathf.Clamp(toward.magnitude - 3.5f, 0f, 4f);
        if (step > 0.3f && TryGround(transform.position + toward.normalized * step, transform.position.y, 1.5f, out Vector3 walkTo))
            yield return MoveTo(walkTo, walkSpeed, false);
        yield return TurnTo(FacePlayer(transform.position));
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.7f, 1.2f, 0.05f, 1f, 25f);
        pose.surrender = 1f;
        offering = true;
    }

    void Update()
    {
        if (!offering || HandedOver || IsDead) return;
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }
        Vector3 to = player.transform.position - transform.position; to.y = 0f;
        if (to.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to), 3f * Time.deltaTime);
            pose.offerDirection = to;
        }
        if (player.IsDead) return;
        float dist = to.magnitude;
        if (dist > handOverRange) return;
        InteractPrompt.Offer(this, dist, "接過弓（獲得弓箭技能）");
        if (InteractPrompt.Pressed(this)) HandOver();
    }

    void HandOver()
    {
        HandedOver = true;
        offering = false;
        // 弓、箭從弓箭手手上消失（玩家手上的弓在對話結束後由 PlayerLoadout 顯示）
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r.name.Contains("Bow_Mesh") || r.name.Contains("Arrow_Mesh")) r.enabled = false;
        GameAudio.Play("SFX/RPG/clothBelt2", player.transform.position, 0.9f, 1.1f, 0.05f, 1f, 20f);
        CombatVFX.Puff(transform.position + Vector3.up * 1.1f + transform.forward * 0.5f, Vector3.up, new Color(1f, 0.9f, 0.6f, 0.6f));

        // 交出弓的時候順便告訴玩家：前面的百骨駭足會叫蝙蝠，附近的石頭磨成粉可以擾亂牠們
        var color = new Color(0.95f, 0.75f, 0.45f);
        string key = InputBindings.DisplayName(GameAction.BatPowder);
        StoryDialogue.Line Me(string t) => StoryDialogue.L("米格爾", player.transform, false, t, new Color(0.6f, 0.85f, 1f));
        StoryDialogue.Line Him(string t) => StoryDialogue.L(speakerName, transform, true, t, color);
        StoryDialogue.Play(new[]
        {
            Him("別、別殺我……這把弓給你，就當是賠罪。"),
            Me("前面還有什麼在等著我？"),
            Him("過了吊橋，那頭百骨駭足會叫出一整群骷髏蝙蝠，被圍住就完了。"),
            Him("不過蝙蝠受不了石粉。這附近有些發著微光的石頭，撿起來磨成粉帶在身上。"),
            Him($"被蝙蝠圍攻的時候按【{key}】撒出去，粉塵裡牠們找不到你，咬不到你。"),
        }, () =>
        {
            PlayerLoadout.GrantBow();
            int n = PowderStone.Scatter(transform.position, powderStones, powderStoneRadius.x, powderStoneRadius.y);
            if (n < powderStones) PowderStone.Scatter(hidePoint, powderStones - n, 1.5f, powderStoneRadius.y, 917);
            StartCoroutine(FleeRoutine());
        });
    }

    /// <summary>交出弓之後站起來、轉身逃走，跑遠了消失</summary>
    IEnumerator FleeRoutine()
    {
        yield return new WaitForSeconds(0.5f);
        pose.surrender = 0f;
        yield return new WaitForSeconds(crouchSeconds + 0.2f);
        Vector3 away = transform.position - (player != null ? player.transform.position : transform.position - transform.forward);
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = -transform.forward;
        Vector3 target = Grounded(transform.position + away.normalized * 14f);
        if (body != null) body.enabled = false;
        yield return MoveTo(target, runSpeed * 1.1f, true);
        CombatVFX.Puff(transform.position + Vector3.up, Vector3.up, new Color(0.9f, 0.9f, 0.85f, 0.5f));
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
        Gizmos.DrawWireSphere(hidePoint, 0.35f);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.9f);
        Gizmos.DrawWireSphere(peekPoint, 0.35f);
        Gizmos.DrawLine(hidePoint, peekPoint);
    }
}
