using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 竹林裡的雙尾虎：
///   平常趴在地上（尾巴慢慢甩）。玩家走近（wakeRadius）或打到牠 → 站起來怒吼 → 衝過來決鬥。
///   招式：遠一點就伏低身子蓄力、飛撲過來（高度剛好，翻滾的無敵時間可以躲掉）；貼近了就揮爪。撲空或揮完會停一下（破綻）。
///   被打中 fleeHits 下以後再吼一聲，轉身往竹林深處（fleeTarget）跑掉，原地留下幾株草藥（herbs，一開始藏著）。
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
    [Tooltip("逃走時留下的草藥（一開始藏著）")]
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
        SnapToGround(1f);
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
        // 原地留下草藥
        if (herbs != null)
            for (int i = 0; i < herbs.Length; i++)
            {
                if (herbs[i] == null) continue;
                float a = i * Mathf.PI * 2f / herbs.Length + 0.4f;
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.3f;
                p.y = Ground(p);
                herbs[i].transform.position = p;
                herbs[i].SetActive(true);
            }
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
                if (!playerOk) { speed = Step(-toPlayer, prowlSpeed * 0.5f); break; }
                Face(toPlayer, 130f);
                if (attackCooldown <= 0f && dist < swipeRange) { SetState(State.Swipe); break; }
                if (attackCooldown <= 0f && dist > pounceRange.x && dist < pounceRange.y) { SetState(State.Windup); GameAudio.Play("SFX/RPG/creak3", transform.position, 0.8f, 0.35f, 0.05f, 1f, 30f); break; }
                // 太遠就衝過去；在撲的距離內但還在冷卻就繞著玩家走
                if (dist > pounceRange.y * 0.8f) speed = Step(toPlayer, runSpeed);
                else if (dist > swipeRange * 0.8f)
                {
                    Vector3 around = Vector3.Cross(Vector3.up, toPlayer.normalized) + toPlayer.normalized * 0.25f;
                    speed = Step(around, prowlSpeed);
                    Face(toPlayer, 130f);
                }
                break;
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
                Face(toPlayer, 110f);
                if (!struck && stateTime > swipeWindup)
                {
                    struck = true;
                    GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.9f, 0.6f, 0.05f, 1f, 30f);
                    if (playerOk && dist < swipeRange + 0.3f && Vector3.Dot(transform.forward, toPlayer.normalized) > 0.4f)
                        player.TakeDamage(swipeDamage, toPlayer.normalized * 8f + Vector3.up * 2f, false, "被雙尾虎抓傷了");
                }
                if (stateTime > swipeWindup + 0.45f) { SetState(State.Recover); attackCooldown = 2f; }
                break;
            case State.Recover:
                // 喘口氣：玩家趁這時候打牠
                if (stateTime > recoverTime) SetState(State.Chase);
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
        gait += dt * (currentSpeed > 0.1f ? currentSpeed / (bodyLength * 0.95f) : 0f) * Mathf.PI * 2f;
        currentSpeed = Mathf.MoveTowards(currentSpeed, speed, dt * 9f); // 加速、減速都柔和一點

        // 各個動作的姿勢慢慢混合過去（不要一下子跳過去）
        float blend = dt * 3.2f;
        roarW = Mathf.MoveTowards(roarW, Current == State.Waking || (Current == State.Fleeing && stateTime < 1.8f) ? 1f : 0f, blend);
        windW = Mathf.MoveTowards(windW, Current == State.Windup ? 1f : 0f, blend * 1.4f);
        pounceW = Mathf.MoveTowards(pounceW, Current == State.Pounce ? 1f : 0f, blend * 2.2f);
        swipeW = Mathf.MoveTowards(swipeW, Current == State.Swipe ? 1f : 0f, blend * 1.6f);
    }

    float currentSpeed, roarW, windW, pounceW, swipeW;

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

        // 步伐：前後腳交錯（跑步時接近跳躍的節奏），一條腿 = 大腿擺 + 小腿收
        float swingAmp = Mathf.Lerp(24f, 42f, run) * walk;
        for (int i = 0; i < 4; i++)
        {
            var leg = legs[i];
            if (leg == null || leg[0] == null) continue;
            bool front = i < 2;
            float ph = gait + (i % 2 == 0 ? 0f : Mathf.Lerp(Mathf.PI, Mathf.PI * 0.35f, run)) + (front ? 0f : Mathf.Lerp(Mathf.PI * 0.5f, Mathf.PI * 1.1f, run));
            float s = Mathf.Sin(ph);
            Rot(leg[0], side, s * swingAmp);
            if (leg[1] != null) Rot(leg[1], side, (front ? 1f : -1f) * Mathf.Max(0f, Mathf.Cos(ph)) * 42f * walk);
        }
        // 身體上下起伏、跑步時前後擺
        float bob = Mathf.Abs(Mathf.Sin(gait)) * 0.065f * L * walk;
        model.position += up * (bob + Mathf.Sin(t * 1.3f) * 0.01f * L); // 呼吸起伏
        if (spine0 != null) Rot(spine0, side, Mathf.Sin(gait * 2f) * 7f * run);
        if (spine2 != null) Rot(spine2, up, Mathf.Sin(gait) * 6f * walk); // 走路時肩膀左右扭
        if (head0 != null) Rot(head0, side, Mathf.Sin(gait * 2f + 0.8f) * 5f * walk); // 頭跟著步伐點

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
            model.position -= up * 0.28f * L * 0.5f * w;
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
        // 揮爪：右前腳慢慢舉高、往前下拍
        if (swipeW > 0.001f)
        {
            float a = Current == State.Swipe && stateTime > swipeWindup ? 1f - Mathf.SmoothStep(0f, 1f, (stateTime - swipeWindup) / 0.3f) : 1f;
            float w = Mathf.SmoothStep(0f, 1f, swipeW);
            if (legs[1]?[0] != null) Rot(legs[1][0], side, (-125f * a - 35f * (1f - a)) * w);
            if (spine2 != null) { Rot(spine2, side, -18f * a * w); Rot(spine2, up, 12f * a * w); }
            if (head1 != null) Rot(head1, side, 24f * a * w);
        }
        if (flinch > 0.001f && spine2 != null) Rot(spine2, up, Mathf.Sin(t * 30f) * 6f * flinch);
    }

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
