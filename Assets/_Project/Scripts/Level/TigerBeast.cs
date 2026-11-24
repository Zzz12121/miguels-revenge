using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 竹林裡的雙尾虎：
///   平常趴在地上（尾巴慢慢甩）。玩家走近（wakeRadius）或打到牠 → 站起來怒吼 → 衝過來決鬥。
///   招式：遠一點就伏低身子蓄力、飛撲過來（高度剛好，翻滾的無敵時間可以躲掉）；貼近了就揮爪，越近揮得越頻繁
///   （左右爪連揮最多三下，冷卻也越短）。撲空或揮完會停一下（破綻）。在撲的距離內冷卻時沿著圓弧繞著玩家走（身體朝走的方向、頭看著玩家）。
///   玩家死了：在原地趴下，不追、不走開；玩家再走近才又起來。
///   被打中 fleeHits 下以後再吼一聲，轉身往竹林深處（fleeTarget）跑掉，原地留下一張虎皮和一對虎牙（TigerLoot，拿去鐵匠鋪升級武器）。
/// 不會死。模型是 Tripo 的四足骨架（沒有動畫），所有動作都是在 LateUpdate 用程式擺骨頭：
/// 每幀先把骨頭放回原本的姿勢，再依狀態疊上走路 / 奔跑的步伐、趴下、吼叫、飛撲、揮爪。
/// </summary>
public class TigerBeast : MonoBehaviour, IDamageable
{
    [Header("模型")]
    [Tooltip("模型的根（骨頭都在底下）")]
    public Transform model;
    [Tooltip("身體的長度 (m)，步伐、飛撲的距離照這個比例")]
    public float bodyLength = 2.6f;
    [Tooltip("腿拉長多少倍（大腿到腳掌的每一節都拉長，身體跟著抬高，腳還是踩在地上）")]
    public float legLength = 1.22f;
    [Tooltip("身體（含腿的間距）往左右加寬多少倍")]
    public float bodyWidth = 1.2f;
    [Tooltip("四條腿往外張開幾度（站得比較開、比較穩）")]
    public float legSplay = 9f;
    [Tooltip("步伐節奏：越大一步跨越遠、腳動得越慢（每走 bodyLength × 這個倍數的距離踏一個循環）")]
    public float strideScale = 1.45f;
    [Tooltip("關節彎曲（0 = 模型原本直直的腿，1 = 貓科用腳趾站的姿勢：後腳 Z 字形、前腳手肘微彎）")]
    [Range(0f, 1.5f)] public float legBend = 1f;
    [Tooltip("抬腳往前跨時關節收多少（度）：前腳手肘 / 手腕、後腳膝蓋 / 腳踝")]
    public float swingFold = 38f;

    [Header("行為")]
    public float wakeRadius = 13f;
    public float runSpeed = 6f;
    public float prowlSpeed = 1.6f;
    [Tooltip("飛撲的距離範圍 (m)")]
    public Vector2 pounceRange = new Vector2(4.5f, 9f);
    public float pounceHeight = 2.4f;
    [Tooltip("伏低蓄力 / 飛在空中 / 揮爪預備 / 出招後的破綻 (秒)")]
    public float windupTime = 0.8f, flightTime = 0.85f, swipeWindup = 0.6f, recoverTime = 1.2f;
    public float swipeRange = 2.6f;
    public float pounceDamage = 18f, swipeDamage = 12f;
    [Tooltip("被打中幾下就逃走")]
    public int fleeHits = 8;
    [Tooltip("逃去哪裡（竹林深處）")]
    public Vector3 fleeTarget;
    [Tooltip("舊版逃走時留下的草藥：現在改成掉虎皮、虎牙（TigerLoot），這些一直藏著")]
    public GameObject[] herbs;

    public enum State { Lying, Waking, Chase, Windup, Pounce, Swipe, Recover, Fleeing, Gone }
    public State Current { get; private set; } = State.Lying;
    public int Hits { get; private set; }
    public bool IsDead => false;
    public int PounceHits { get; private set; }

    PlayerHealth player;
    Terrain terrain;
    float stateTime, gait, lie = 1f, attackCooldown, flinch, lastHit = float.NegativeInfinity;
    bool struck;
    // 揮爪連段：第幾下、用哪隻前腳（0 左 1 右）、這一下的預備時間、出招後的破綻
    int combo, paw = 1;
    float swipeWind, recoverDur;
    // 繞圈的方向（1 / -1），隔一陣子可能換方向
    float circleSign = 1f, circleTimer;
    // 實際的移動（步伐照這個走）：水平速度、轉身的角速度（度 / 秒）
    Vector3 lastPos, moveVel;
    float lastYaw, yawVel;
    Vector3 leapFrom, leapTo;
    GUIStyle nameStyle;

    // 骨頭
    readonly List<Transform> bones = new List<Transform>();
    readonly List<Quaternion> rest = new List<Quaternion>();
    Transform spine0, spine1, spine2, spine3, head0, head1;
    Transform[][] legs = new Transform[4][];   // 0 左前 1 右前 2 左後 3 右後
    readonly List<Transform> tail = new List<Transform>();
    readonly List<Transform> tail2 = new List<Transform>();
    Vector3 modelRestPos;

    void Start()
    {
        // 距離照體型縮放（數值是以 2.6 m 的老虎調的）
        float k = bodyLength / 2.6f;
        pounceRange *= k;
        swipeRange *= k;
        player = PlayerHealth.Find();
        terrain = Terrain.activeTerrain;
        if (herbs != null) foreach (var h in herbs) if (h != null) h.SetActive(false);
        FindBones();
        Widen();
        StretchLegs();
        MeasureBendDrop();
        SnapToGround(1f);
        swipeWind = swipeWindup;
        recoverDur = recoverTime;
        lastPos = transform.position;
        lastYaw = transform.eulerAngles.y;
    }

    void FindBones()
    {
        if (model == null) return;
        modelRestPos = model.localPosition;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t == model) continue;
            bones.Add(t);
            rest.Add(t.localRotation);
        }
        Transform B(string key) => bones.Find(b => b.name.EndsWith(key));
        spine0 = B("Spine_0"); spine1 = B("Spine_1"); spine2 = B("Spine_2"); spine3 = B("Spine_3");
        head0 = B("Head_0"); head1 = B("Head_1");
        string[] chains = { "0_Left_Limb_", "0_Right_Limb_", "1_Left_Limb_", "1_Right_Limb_" };
        var found = new Transform[4][];
        for (int i = 0; i < 4; i++)
        {
            found[i] = new Transform[4];
            for (int k = 0; k < 4; k++) found[i][k] = B(chains[i] + k);
        }
        // 0_ / 1_ 哪一組是前腳：離頭近的那組
        bool zeroIsFront = true;
        if (head0 != null && found[0][0] != null && found[2][0] != null)
            zeroIsFront = (found[0][0].position - head0.position).sqrMagnitude < (found[2][0].position - head0.position).sqrMagnitude;
        legs = zeroIsFront ? found : new[] { found[2], found[3], found[0], found[1] };
        for (int k = 0; k < 5; k++) { var t = B("Tail_" + k); if (t != null) tail.Add(t); }
        // 第二條尾巴：tripo 沒命名的骨頭裡，掛在尾巴根附近、往後延伸的那串
        foreach (var b in bones)
            if (b.name.StartsWith("bone_") && b.parent != null && (b.parent == spine0 || b.parent == B("Root")) && !tail.Contains(b))
            {
                var c = b;
                while (c != null) { tail2.Add(c); c = c.childCount > 0 ? c.GetChild(0) : null; }
                break;
            }
    }

    float legExtra; // 腿拉長以後身體抬高了多少 (m)：趴下時多壓低這麼多

    /// <summary>
    /// 模型原本四條腿不一樣長（Tripo 綁骨，右後腳比較短 / 擺得比較高）：腳比最低的那隻高的那條腿，
    /// 單獨把膝蓋、腳踝的骨頭再往外移，讓髖到腳踝的高度補上差距，站著的時候四隻腳都碰得到地
    /// </summary>
    void EqualizeLegs(float lowest)
    {
        for (int i = 0; i < 4; i++)
        {
            var leg = legs[i];
            if (leg == null || leg[0] == null || leg[1] == null || leg[2] == null) continue;
            Transform foot = leg.Length > 3 && leg[3] != null ? leg[3] : leg[2];
            float gap = foot.position.y - lowest;
            float span = leg[0].position.y - leg[2].position.y; // 髖到腳踝的高度
            if (gap < 0.01f || span < 0.05f) continue;
            float f = Mathf.Clamp((span + gap) / span, 1f, 1.5f);
            leg[1].localPosition *= f;
            leg[2].localPosition *= f;
            Debug.Log($"[TigerBeast] 第 {i} 條腿（0 左前 1 右前 2 左後 3 右後）比較短：腳高了 {gap:F2} m，再拉長 {f:F2} 倍");
        }
    }

    /// <summary>身體加寬：模型往老虎左右方向（模型自己的哪個軸都有可能）放大 bodyWidth 倍，腿的間距也跟著變寬</summary>
    void Widen()
    {
        if (model == null || Mathf.Approximately(bodyWidth, 1f)) return;
        Vector3 lr = model.InverseTransformDirection(transform.right);
        lr = new Vector3(Mathf.Abs(lr.x), Mathf.Abs(lr.y), Mathf.Abs(lr.z));
        Vector3 s = model.localScale;
        if (lr.x >= lr.y && lr.x >= lr.z) s.x *= bodyWidth;
        else if (lr.y >= lr.z) s.y *= bodyWidth;
        else s.z *= bodyWidth;
        model.localScale = s;
    }

    /// <summary>
    /// 腿拉長：每條腿的小腿、腳掌的骨頭沿著原本的方向往外移（第一節是大腿根，不動），
    /// 綁在上面的皮膚跟著拉長；腳變低了多少就把整個模型抬高多少，腳還是踩在地上。
    /// </summary>
    void StretchLegs()
    {
        if (model == null || Mathf.Approximately(legLength, 1f)) return;
        float LowestFoot()
        {
            float y = float.PositiveInfinity;
            foreach (var leg in legs)
            {
                if (leg == null) continue;
                for (int k = leg.Length - 1; k >= 0; k--)
                    if (leg[k] != null) { y = Mathf.Min(y, leg[k].position.y); break; }
            }
            return y;
        }
        float before = LowestFoot();
        foreach (var leg in legs)
        {
            if (leg == null) continue;
            for (int k = 1; k < leg.Length && k <= 2; k++) // 膝蓋、腳踝往外移；腳趾（最後一節）長度不變
                if (leg[k] != null) leg[k].localPosition *= legLength;
        }
        float after = LowestFoot();
        if (float.IsInfinity(before) || float.IsInfinity(after)) return;
        EqualizeLegs(after);
        legExtra = Mathf.Max(0f, before - after);
        model.position += Vector3.up * legExtra;
        modelRestPos = model.localPosition;
    }

    // 站著的關節角度（度，繞老虎的右方向；正 = 往後，負 = 往前），每條腿 [肩 / 髖, 肘 / 膝, 腕 / 踝, 腳趾]：
    //   前腳：上臂從肩膀往後下到手肘、前臂往前一點、掌骨往前斜，腳趾轉回來平貼地面
    //   後腳：大腿往前下、小腿往後下（膝蓋在前面）、蹠骨往前下（腳跟高高的在後面），腳趾轉回來平貼地面
    static readonly float[] FrontBend = { 12f, -16f, -14f, 18f };
    static readonly float[] HindBend = { -18f, 40f, -32f, 10f };
    float bendDrop; // 腿彎了以後腳比原本高多少 (m)：身體壓低這麼多，腳才踩在地上
    float sole;     // 腳的骨頭（最後一節）站著時離地面多高 (m)：腳掌的厚度
    readonly float[] swingAmt = new float[4]; // 每條腿這一幀抬腳往前跨了多少（0 = 踩在地上）

    void Bend(int i, float w, Vector3 side)
    {
        var leg = legs[i];
        if (leg == null || w <= 0.001f) return;
        var a = i < 2 ? FrontBend : HindBend;
        for (int k = 0; k < leg.Length && k < a.Length; k++) Rot(leg[k], side, a[k] * w);
    }

    void MeasureBendDrop()
    {
        if (model == null || legBend <= 0.001f) return;
        float Foot(int i)
        {
            var leg = legs[i];
            if (leg == null) return float.NaN;
            for (int k = leg.Length - 1; k >= 0; k--) if (leg[k] != null) return leg[k].position.y;
            return float.NaN;
        }
        var before = new float[4];
        for (int i = 0; i < 4; i++) before[i] = Foot(i);
        // 腳掌厚度：原本的姿勢裡最低的那隻腳離根物件多高（根物件之後會貼著地面）
        sole = float.PositiveInfinity;
        for (int i = 0; i < 4; i++) if (!float.IsNaN(before[i])) sole = Mathf.Min(sole, before[i] - transform.position.y);
        if (float.IsInfinity(sole)) sole = 0f;
        for (int i = 0; i < 4; i++) Bend(i, legBend, transform.right);
        // 四隻腳平均升了多少：身體壓低這麼多（每隻腳剩下的差，由 PlantFeet 用 IK 讓腳剛好踩在地上）
        float rise = 0f;
        int n = 0;
        for (int i = 0; i < 4; i++)
        {
            float d = Foot(i) - before[i];
            if (!float.IsNaN(d)) { rise += d; n++; }
        }
        for (int i = 0; i < bones.Count; i++) bones[i].localRotation = rest[i];
        bendDrop = n > 0 ? Mathf.Max(0f, rise / n) : 0f;
    }

    /// <summary>
    /// 腳踩在地上：每隻腳（最後一節骨頭）和底下地面的高度差，用兩節骨頭的 IK（髖 / 肩 → 膝 / 肘 → 踝 / 腕）把腳放到地面上，
    /// 腳掌的方向不變（踝 / 腕的世界旋轉還原）。踩地的那半步懸空就往下伸、插進地裡就收起來；抬腳往前跨的那半步只防止插進地裡。
    /// weights[i] = 這隻腳要不要踩地（飛撲在空中、趴下、揮爪舉起來的那隻是 0）
    /// </summary>
    void PlantFeet(float[] weights, Vector3 up, Vector3 fwd)
    {
        if (terrain == null) return; // 沒有地形就不知道地面在哪
        // 後腳伸直了還踩不到地：整個身體壓低差的那一段（平滑跟上，不會一抖一抖）
        float need = 0f;
        for (int i = 2; i < 4; i++)
        {
            var leg = legs[i];
            float w = weights[i] * (1f - swingAmt[i]);
            if (leg == null || w <= 0.001f || leg[0] == null || leg[1] == null || leg[2] == null) continue;
            Transform footT = leg.Length > 3 && leg[3] != null ? leg[3] : leg[2];
            float dy = FootGround(footT.position) + sole - footT.position.y;
            if (dy >= 0f) continue;
            Vector3 hip = leg[0].position, ankle = leg[2].position + up * dy;
            float reach = (Vector3.Distance(hip, leg[1].position) + Vector3.Distance(leg[1].position, leg[2].position)) * 0.995f;
            float dist = Vector3.Distance(hip, ankle);
            if (dist <= reach) continue;
            float vertical = Mathf.Max(0.5f, Vector3.Dot((hip - ankle) / dist, up));
            need = Mathf.Max(need, (dist - reach) / vertical * w);
        }
        hipDrop = Mathf.Lerp(hipDrop, Mathf.Min(need, 0.55f), 1f - Mathf.Exp(-14f * Time.deltaTime));
        if (hipDrop > 0.001f) model.position -= up * hipDrop;
        for (int i = 0; i < 4; i++)
        {
            var leg = legs[i];
            float w = weights[i];
            if (leg == null || w <= 0.001f || leg[0] == null || leg[1] == null || leg[2] == null) continue;
            Transform footT = leg.Length > 3 && leg[3] != null ? leg[3] : leg[2];
            // 模型被放大得不均勻（身體加寬）：骨頭轉過去的方向會差一點點，量一次、修一次，最多三次
            for (int iter = 0; iter < 3; iter++)
            {
                Vector3 foot = footT.position;
                float dy = FootGround(foot) + sole - foot.y; // > 0 = 插進地裡（要抬）、< 0 = 懸空（要放下）
                if (dy < 0f) dy *= 1f - swingAmt[i];    // 抬腳往前跨：本來就該離地
                dy = Mathf.Clamp(dy, -0.45f, 0.45f) * w;
                if (Mathf.Abs(dy) < 0.005f) break;
                TwoBoneIK(leg[0], leg[1], leg[2], leg[2].position + up * dy, fwd);
            }
        }
    }

    static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 fallbackBend)
    {
        Quaternion keep = c.rotation;
        Vector3 root = a.position;
        float l1 = Vector3.Distance(root, b.position), l2 = Vector3.Distance(b.position, c.position);
        if (l1 < 1e-4f || l2 < 1e-4f) return;
        Vector3 to = target - root;
        if (to.sqrMagnitude < 1e-8f) return;
        float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, (l1 + l2) * 0.999f);
        Vector3 dir = to.normalized;
        // 膝蓋 / 手肘往原本彎的那一邊彎（保持關節的方向，不會反折）
        Vector3 bend = Vector3.ProjectOnPlane(b.position - root, dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(fallbackBend, dir);
        bend.Normalize();
        float cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        Vector3 knee = root + dir * (l1 * cos) + bend * (l1 * Mathf.Sqrt(1f - cos * cos));
        a.rotation = Quaternion.FromToRotation(b.position - root, knee - root) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, root + dir * d - b.position) * b.rotation;
        c.rotation = keep;
    }

    // ---------------- 被打 ----------------

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (amount <= 0f || Current == State.Fleeing || Current == State.Gone || Time.time - lastHit < 0.12f) return;
        lastHit = Time.time;
        Hits++;
        flinch = 1f;
        CombatVFX.Impact(hitPoint, new Color(1f, 0.75f, 0.35f));
        if (Current == State.Lying) { Wake(); return; }
        if (Hits >= fleeHits) StartFlee();
    }

    void Wake()
    {
        SetState(State.Waking);
        GameAudio.Play("SFX/dragon_roar", transform.position, 1f, 0.78f, 0f, 1f, 80f);
        ThirdPersonCamera.ShakeMain(0.25f, 0.9f);
    }

    void StartFlee()
    {
        SetState(State.Fleeing);
        GameAudio.Play("SFX/dragon_roar", transform.position, 1f, 0.72f, 0f, 1f, 80f);
        ThirdPersonCamera.ShakeMain(0.2f, 0.8f);
        // 原地留下虎皮、虎牙（舊的草藥不再出現）
        TigerLoot.Drop(transform.position, transform.forward, Ground);
    }

    void SetState(State s)
    {
        Current = s;
        stateTime = 0f;
        struck = false;
    }

    // ---------------- 行為 ----------------

    void Update()
    {
        if (Current == State.Gone) return;
        float dt = Time.deltaTime;
        stateTime += dt;
        flinch = Mathf.MoveTowards(flinch, 0f, dt * 3f);
        attackCooldown -= dt;
        lie = Mathf.MoveTowards(lie, Current == State.Lying ? 1f : 0f, dt * (Current == State.Lying ? 0.8f : 2.2f));

        Vector3 toPlayer = player != null ? player.transform.position - transform.position : Vector3.forward * 100f;
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;
        bool playerOk = player != null && !player.IsDead;
        float speed = 0f;
        // 玩家死了：在原地趴下（不追、不走開），玩家重生後再走近才起來
        if (!playerOk && (Current == State.Waking || Current == State.Chase || Current == State.Windup || Current == State.Swipe || Current == State.Recover))
        {
            SetState(State.Lying);
            combo = 0;
            attackCooldown = 0f;
        }

        switch (Current)
        {
            case State.Lying:
                if (playerOk && dist < wakeRadius) Wake();
                break;
            case State.Waking:
                Face(toPlayer, 70f);
                if (stateTime > 2.2f) SetState(State.Chase);
                break;
            case State.Chase:
            {
                if (attackCooldown <= 0f && dist < swipeRange) { combo = 0; StartSwipe(); break; }
                if (attackCooldown <= 0f && dist > pounceRange.x && dist < pounceRange.y) { SetState(State.Windup); GameAudio.Play("SFX/RPG/creak3", transform.position, 0.8f, 0.35f, 0.05f, 1f, 30f); break; }
                circleTimer -= dt;
                if (circleTimer <= 0f) { circleTimer = Random.Range(2.5f, 5f); if (Random.value < 0.4f) circleSign = -circleSign; }
                Vector3 to = toPlayer.normalized;
                Vector3 tangent = Vector3.Cross(Vector3.up, to) * circleSign;
                if (dist > pounceRange.y * 0.8f) speed = Step(toPlayer, runSpeed); // 太遠就衝過去
                else if (dist > swipeRange * 0.8f)
                {
                    // 在撲的距離內、還在冷卻：沿著圓弧繞著玩家走（稍微往內靠），身體朝走的方向、頭轉過去看玩家（LateUpdate）
                    Vector3 move = (tangent + to * 0.3f).normalized;
                    transform.position += move * prowlSpeed * dt;
                    Face(Vector3.Slerp(to, move, 0.6f), 140f);
                    speed = prowlSpeed;
                }
                else
                {
                    // 貼得很近、還在冷卻：面向玩家、橫著小步挪（四隻腳真的踏步）
                    transform.position += tangent * prowlSpeed * 0.45f * dt;
                    Face(toPlayer, 130f);
                    speed = prowlSpeed * 0.45f;
                }
                break;
            }
            case State.Windup:
                // 伏低、屁股翹起來：0.55 秒後撲出去
                Face(toPlayer, 150f);
                if (stateTime > windupTime)
                {
                    leapFrom = transform.position;
                    Vector3 target = player.transform.position;
                    Vector3 dir = toPlayer.normalized;
                    leapTo = target + dir * 1.2f; // 撲過頭一點：沒閃開的話剛好撲在身上
                    leapTo.y = Ground(leapTo);
                    SetState(State.Pounce);
                    GameAudio.Play("SFX/dragon_roar", transform.position, 0.7f, 1.05f, 0.05f, 1f, 50f);
                }
                break;
            case State.Pounce:
            {
                float t = Mathf.Clamp01(stateTime / flightTime);
                Vector3 p = Vector3.Lerp(leapFrom, leapTo, t);
                p.y = Mathf.Lerp(leapFrom.y, leapTo.y, t) + Mathf.Sin(t * Mathf.PI) * pounceHeight;
                transform.position = p;
                // 撲到玩家身上（翻滾中無敵就沒事）
                if (!struck && t > 0.35f && t < 0.95f && player != null)
                {
                    Vector3 d = player.transform.position - transform.position;
                    if (new Vector2(d.x, d.z).magnitude < 0.6f * bodyLength && Mathf.Abs(d.y) < pounceHeight + 1f)
                    {
                        struck = true;
                        if (player.TakeDamage(pounceDamage, transform.forward * 11f + Vector3.up * 4f, false, "被雙尾虎撲倒了")) PounceHits++;
                    }
                }
                if (t >= 1f)
                {
                    GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.5f, 0.05f, 1f, 40f);
                    CombatVFX.Impact(transform.position + Vector3.up * 0.1f, new Color(0.6f, 0.55f, 0.4f));
                    SetState(State.Recover);
                    attackCooldown = 2.6f;
                }
                break;
            }
            case State.Swipe:
            {
                Face(toPlayer, 140f);
                if (!struck && stateTime > swipeWind)
                {
                    struck = true;
                    GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.9f, 0.6f + 0.08f * combo, 0.05f, 1f, 30f);
                    if (playerOk && dist < swipeRange + 0.3f && Vector3.Dot(transform.forward, toPlayer.normalized) > 0.4f)
                        player.TakeDamage(swipeDamage, toPlayer.normalized * 8f + Vector3.up * 2f, false, "被雙尾虎抓傷了");
                }
                if (stateTime > swipeWind + 0.4f)
                {
                    // 越近越兇：玩家還貼在面前就換另一隻爪子接著揮（最多三下），揮完的破綻和冷卻也越短
                    float near = 1f - Mathf.Clamp01((dist - swipeRange * 0.4f) / (swipeRange * 1.2f));
                    if (playerOk && dist < swipeRange * 1.1f && combo < 2 && Random.value < 0.45f + 0.5f * near)
                    {
                        combo++;
                        paw = 1 - paw;
                        StartSwipe();
                    }
                    else
                    {
                        recoverDur = Mathf.Lerp(recoverTime, recoverTime * 0.4f, near);
                        attackCooldown = Mathf.Lerp(1.8f, 0.35f, near);
                        combo = 0;
                        SetState(State.Recover);
                    }
                }
                break;
            }
            case State.Recover:
                // 喘口氣：玩家趁這時候打牠
                if (stateTime > recoverDur) SetState(State.Chase);
                break;
            case State.Fleeing:
            {
                if (stateTime < 1.8f) { Face(-toPlayer, 110f); break; } // 先吼一聲、慢慢轉身
                Vector3 away = fleeTarget - transform.position;
                away.y = 0f;
                speed = Step(away, runSpeed * 1.25f);
                if (away.magnitude < 1.5f || stateTime > 9f)
                {
                    SetState(State.Gone);
                    gameObject.SetActive(false);
                    return;
                }
                break;
            }
        }

        if (Current != State.Pounce) SnapToGround(dt * 12f);
        // 步伐照實際的移動走：往前、橫著走、原地轉身都會踏步（轉身時前腳和後腳往相反的方向挪）
        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        lastPos = transform.position;
        float yaw = transform.eulerAngles.y;
        float yawRate = dt > 1e-5f ? Mathf.DeltaAngle(lastYaw, yaw) / dt : 0f;
        lastYaw = yaw;
        bool grounded = Current != State.Pounce;
        moveVel = Vector3.Lerp(moveVel, grounded && dt > 1e-5f ? delta / dt : Vector3.zero, 1f - Mathf.Exp(-dt * 10f));
        yawVel = Mathf.Lerp(yawVel, grounded ? yawRate : 0f, 1f - Mathf.Exp(-dt * 10f));
        float stepSpeed = Mathf.Max(moveVel.magnitude, Mathf.Abs(yawVel) * Mathf.Deg2Rad * 0.42f * bodyLength);
        gait += dt * (stepSpeed > 0.08f ? stepSpeed / (bodyLength * strideScale) : 0f) * Mathf.PI * 2f; // 步子跨大、腳動得慢一點
        currentSpeed = Mathf.MoveTowards(currentSpeed, Mathf.Max(speed, moveVel.magnitude), dt * 9f); // 加速、減速都柔和一點

        // 各個動作的姿勢慢慢混合過去（不要一下子跳過去）
        float blend = dt * 3.2f;
        roarW = Mathf.MoveTowards(roarW, Current == State.Waking || (Current == State.Fleeing && stateTime < 1.8f) ? 1f : 0f, blend);
        windW = Mathf.MoveTowards(windW, Current == State.Windup ? 1f : 0f, blend * 1.4f);
        pounceW = Mathf.MoveTowards(pounceW, Current == State.Pounce ? 1f : 0f, blend * 2.2f);
        swipeW = Mathf.MoveTowards(swipeW, Current == State.Swipe ? 1f : 0f, blend * 1.6f);
    }

    float currentSpeed, roarW, windW, pounceW, swipeW;

    void StartSwipe()
    {
        SetState(State.Swipe);
        swipeWind = combo == 0 ? swipeWindup : swipeWindup * 0.5f; // 連段的第二、三下預備比較短
    }

    /// <summary>移動中（測試用）：實際的水平速度、轉身角速度</summary>
    public Vector3 MoveVelocity => moveVel;
    public float YawVelocity => yawVel;
    public int Combo => combo;

    float Step(Vector3 dir, float speed)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return 0f;
        Face(dir, 150f);
        transform.position += dir.normalized * speed * Time.deltaTime;
        return speed;
    }

    void Face(Vector3 dir, float degPerSec)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
    }

    float Ground(Vector3 p) => terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y;

    void SnapToGround(float k)
    {
        Vector3 p = transform.position;
        p.y = Mathf.Lerp(p.y, Ground(p), Mathf.Clamp01(k));
        transform.position = p;
    }

    // ---------------- 姿勢（程式擺骨頭） ----------------

    void LateUpdate()
    {
        if (model == null || bones.Count == 0) return;
        for (int i = 0; i < bones.Count; i++) bones[i].localRotation = rest[i];
        model.localPosition = modelRestPos;

        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        float L = bodyLength, t = Time.time;
        float run = Mathf.Clamp01(currentSpeed / runSpeed), walk = Mathf.Clamp01(currentSpeed / 1.5f);

        // 步伐：前後腳交錯（跑步時接近跳躍的節奏），一條腿 = 大腿擺 + 小腿收。
        // 每條腿往「那隻腳實際移動的方向」擺：身體的速度 + 轉身造成的速度（前腳、後腳方向相反），
        // 所以橫著走、原地轉身也是一步一步踏出去，不是整隻滑過去 / 轉過去
        float yawRad = yawVel * Mathf.Deg2Rad;
        float stepWalk = 0f;
        for (int i = 0; i < 4; i++) swingAmt[i] = 0f;
        // 站姿：關節微彎（趴下時慢慢放掉，趴下的姿勢自己擺）
        float bendW = legBend * (1f - Mathf.SmoothStep(0f, 1f, lie));
        for (int i = 0; i < 4; i++) Bend(i, bendW, side);
        model.position -= up * bendDrop * bendW;
        for (int i = 0; i < 4; i++)
        {
            var leg = legs[i];
            if (leg == null || leg[0] == null) continue;
            bool front = i < 2, left = i % 2 == 0;
            Vector3 offset = fwd * (front ? 0.42f : -0.42f) * L + side * (left ? -0.13f : 0.13f) * L;
            Vector3 legVel = moveVel + Vector3.Cross(up, offset) * yawRad;
            legVel.y = 0f;
            float legSpeed = legVel.magnitude;
            float legWalk = Mathf.Clamp01(legSpeed / 1.5f);
            stepWalk = Mathf.Max(stepWalk, legWalk);
            Vector3 axis = legSpeed > 0.05f ? Vector3.Cross(up, legVel / legSpeed) : side;
            float swingAmp = Mathf.Lerp(30f, 48f, run) * legWalk; // 節奏慢了，每一步擺大一點才不會滑
            float ph = gait + (i % 2 == 0 ? 0f : Mathf.Lerp(Mathf.PI, Mathf.PI * 0.35f, run)) + (front ? 0f : Mathf.Lerp(Mathf.PI * 0.5f, Mathf.PI * 1.1f, run));
            float s = Mathf.Sin(ph);
            Rot(leg[0], axis, s * swingAmp);
            // 大腿往後擺（cos > 0）= 腳踩在地上、身體往前走過去；往前擺（cos < 0）= 腳抬起來往前跨。
            // 抬腳那半圈關節收起來（前腳：手肘往後收、手腕往後折，腳掌翻起來；後腳：膝蓋、腳踝一起折，腳跟往上），
            // 踩地那半圈腿撐直、膝蓋只吃一點重量（腳剛落地時微微一沉）
            float c = Mathf.Cos(ph);
            float fold = Mathf.Max(0f, -c);
            swingAmt[i] = fold * legWalk;
            fold = fold * fold * (3f - 2f * fold) * swingFold * legWalk;
            float load = Mathf.Max(0f, c) * Mathf.Max(0f, s) * 10f * legWalk * legBend; // 落地後膝蓋多彎一點點
            Vector3 jointAxis = side; // 關節只能往解剖上的方向彎（跟走的方向無關），橫著走、倒退也一樣
            if (front)
            {
                Rot(leg[1], jointAxis, fold * 0.7f + load * 0.5f);
                Rot(leg[2], jointAxis, fold * 1.3f);
                Rot(leg[3], jointAxis, -fold * 0.4f);
            }
            else
            {
                Rot(leg[1], jointAxis, fold * 0.8f + load);
                Rot(leg[2], jointAxis, -fold * 1.1f - load);
                Rot(leg[3], jointAxis, fold * 0.5f);
            }
        }
        walk = Mathf.Max(walk, stepWalk);
        // 四條腿往外張開一點（左腿往左、右腿往右）
        if (Mathf.Abs(legSplay) > 0.01f)
            for (int i = 0; i < 4; i++)
                if (legs[i]?[0] != null) Rot(legs[i][0], fwd, (i % 2 == 0 ? -1f : 1f) * legSplay);
        // 身體上下起伏、跑步時前後擺
        float bob = Mathf.Abs(Mathf.Sin(gait)) * 0.065f * L * walk;
        model.position += up * (bob + Mathf.Sin(t * 1.3f) * 0.01f * L); // 呼吸起伏
        if (spine0 != null) Rot(spine0, side, Mathf.Sin(gait * 2f) * 7f * run);
        if (spine2 != null) Rot(spine2, up, Mathf.Sin(gait) * 6f * walk); // 走路時肩膀左右扭
        if (head0 != null) Rot(head0, side, Mathf.Sin(gait * 2f + 0.8f) * 5f * walk); // 頭跟著步伐點

        // 繞著玩家走、橫著挪的時候：頭和肩膀轉過去盯著玩家
        if (Current == State.Chase && player != null && !player.IsDead)
        {
            Vector3 tp = player.transform.position - transform.position;
            tp.y = 0f;
            float look = Mathf.Clamp(Vector3.SignedAngle(fwd, tp, up), -65f, 65f);
            if (spine3 != null) Rot(spine3, up, look * 0.3f);
            if (head0 != null) Rot(head0, up, look * 0.55f);
        }

        // 尾巴：兩條各自慢慢甩，跑的時候往後拉直、生氣時翹高
        float angry = Current == State.Lying ? 0f : 1f;
        float k = 0f;
        foreach (var tb in tail) { Rot(tb, up, Mathf.Sin(t * 0.9f - k) * 24f); Rot(tb, side, -6f * angry); k += 0.5f; }
        k = 0f;
        foreach (var tb in tail2) { Rot(tb, up, Mathf.Sin(t * 0.75f + 1.7f - k) * 26f); Rot(tb, side, -5f * angry); k += 0.5f; }

        // 趴著：身體壓低、前腳往前伸、後腳收在身下、頭擱低
        if (lie > 0.001f)
        {
            float w = Mathf.SmoothStep(0f, 1f, lie);
            model.position -= up * (0.28f * L * 0.5f + legExtra) * w; // 腿拉長了，趴下時多壓低一點
            for (int i = 0; i < 4; i++)
            {
                var leg = legs[i];
                if (leg == null || leg[0] == null) continue;
                if (i < 2) { Rot(leg[0], side, -70f * w); if (leg[1] != null) Rot(leg[1], side, 15f * w); }
                else { Rot(leg[0], side, -55f * w); if (leg[1] != null) Rot(leg[1], side, 110f * w); }
            }
            if (head0 != null) Rot(head0, side, (14f + Mathf.Sin(t * 0.5f) * 3f) * w);
        }

        // 怒吼：前半身抬起、頭往上仰、張嘴、身體微微抖
        if (roarW > 0.001f)
        {
            float r = Mathf.SmoothStep(0f, 1f, roarW) * (0.75f + 0.25f * Mathf.Sin(t * 2.2f));
            if (spine2 != null) Rot(spine2, side, -26f * r);
            if (head0 != null) Rot(head0, side, -40f * r);
            if (head1 != null) Rot(head1, side, 30f * r); // 下巴張大
            for (int i = 0; i < 2; i++) if (legs[i]?[0] != null) Rot(legs[i][0], side, -18f * r); // 前腳撐起來
            model.position += side * Mathf.Sin(t * 22f) * 0.012f * L * r + up * 0.06f * L * r;
        }
        // 伏低蓄力：前半身壓低、後腳蹬直、頭貼地盯著玩家
        if (windW > 0.001f)
        {
            float w = Mathf.SmoothStep(0f, 1f, windW);
            model.position -= up * 0.2f * L * w;
            if (spine0 != null) Rot(spine0, side, 14f * w);
            if (head0 != null) Rot(head0, side, -6f * w);
            for (int i = 0; i < 2; i++) if (legs[i]?[1] != null) Rot(legs[i][1], side, 55f * w);
            for (int i = 2; i < 4; i++) if (legs[i]?[0] != null) Rot(legs[i][0], side, 32f * w);
        }
        // 飛撲：身體拉長、前腳往前伸、後腳往後蹬
        if (pounceW > 0.001f)
        {
            float pw = Mathf.SmoothStep(0f, 1f, pounceW);
            for (int i = 0; i < 4; i++)
                if (legs[i]?[0] != null) Rot(legs[i][0], side, (i < 2 ? -68f : 80f) * pw); // 前腳往前伸（略低於水平，不要往上舉）
            if (head0 != null) Rot(head0, side, -20f * pw);
            if (head1 != null) Rot(head1, side, 32f * pw);
            if (spine2 != null) Rot(spine2, side, -12f * pw); // 身體拉長
        }
        // 揮爪：這一下用的前腳（連段時左右交替）舉高、往前下拍；身體往那一邊扭
        if (swipeW > 0.001f)
        {
            float raise = Current == State.Swipe && stateTime < swipeWind ? Mathf.SmoothStep(0f, 1f, stateTime / Mathf.Max(0.05f, swipeWind)) : 1f;
            float a = Current == State.Swipe && stateTime > swipeWind ? 1f - Mathf.SmoothStep(0f, 1f, (stateTime - swipeWind) / 0.25f) : raise;
            float w = Mathf.SmoothStep(0f, 1f, swipeW);
            float twist = paw == 1 ? 1f : -1f;
            if (legs[paw]?[0] != null)
            {
                Rot(legs[paw][0], side, (-125f * a - 35f * (1f - a)) * w);
                Rot(legs[paw][0], fwd, -twist * 14f * a * w); // 爪子往外張一點
            }
            if (spine2 != null) { Rot(spine2, side, -18f * a * w); Rot(spine2, up, 12f * twist * a * w); }
            if (head1 != null) Rot(head1, side, 24f * a * w);
        }
        if (flinch > 0.001f && spine2 != null) Rot(spine2, up, Mathf.Sin(t * 30f) * 6f * flinch);

        // 腳踩地：在地上的時候四隻腳都貼著地面（後腳一定踩著；前腳在怒吼撐起來、揮爪舉起來時不管）
        {
            float ground = (1f - Mathf.SmoothStep(0f, 1f, pounceW)) * (1f - Mathf.SmoothStep(0f, 1f, lie));
            plantW[2] = plantW[3] = ground;
            float front = ground * (1f - Mathf.SmoothStep(0f, 1f, roarW));
            plantW[0] = plantW[1] = front;
            if (swipeW > 0.001f && paw >= 0 && paw < 2) plantW[paw] *= 1f - Mathf.SmoothStep(0f, 1f, swipeW);
            PlantFeet(plantW, up, fwd);
        }
    }

    readonly float[] plantW = new float[4];
    static readonly RaycastHit[] footHits = new RaycastHit[8];

    /// <summary>腳底下的地面：地形，或比地形高的石階、石頭（往下打射線，老虎自己、玩家不算）</summary>
    float FootGround(Vector3 foot)
    {
        float g = Ground(foot);
        int n = Physics.RaycastNonAlloc(foot + Vector3.up * 1.2f, Vector3.down, footHits, 3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = footHits[i].collider;
            if (c == null || c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            if (footHits[i].point.y > g) g = footHits[i].point.y;
        }
        return g;
    }
    float hipDrop; // 後腳踩不到地時身體壓低多少（平滑過）

    static void Rot(Transform b, Vector3 axis, float degrees)
    {
        if (b == null || Mathf.Abs(degrees) < 0.01f) return;
        b.rotation = Quaternion.AngleAxis(degrees, axis) * b.rotation;
    }

    // ---------------- 畫面上方的血條（決鬥中） ----------------

    void OnGUI()
    {
        if (player == null || GameUI.OverlayOpen) return;
        if (Current == State.Lying || Current == State.Gone || Current == State.Fleeing) return;
        if ((player.transform.position - transform.position).sqrMagnitude > 45f * 45f) return;
        nameStyle ??= new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold, fontSize = 16, alignment = TextAnchor.MiddleCenter };
        float w = Mathf.Min(460f, Screen.width * 0.5f);
        var box = new Rect((Screen.width - w) * 0.5f, 18f, w, 44f);
        GameUI.Panel(box);
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(box.x, box.y + 2f, box.width, 20f), "雙尾虎", nameStyle);
        float k = 1f - Mathf.Clamp01((float)Hits / Mathf.Max(1, fleeHits));
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(box.x + 14f, box.y + 26f, box.width - 28f, 9f), Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.35f, 0.15f);
        GUI.DrawTexture(new Rect(box.x + 14f, box.y + 26f, (box.width - 28f) * k, 9f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
