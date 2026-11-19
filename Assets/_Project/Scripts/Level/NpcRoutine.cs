using UnityEngine;

/// <summary>
/// 村民沒在跟玩家說話時的日常：照 stops 的順序走過去、在每一站做一件事（站著東張西望、坐下、打鐵、顧爐火、擦長椅、進屋、看書），做完換下一站，繞一圈再從頭。
/// 玩家走近（noticeRadius 以內）或正在對話時停下來：坐著的先站起來、看著玩家；玩家走開（resumeRadius 以外）才接著做。
/// 坐、打鐵這些動作專案裡沒有適合人形骨架的動畫，用程式在 LateUpdate 擺骨頭（蓋在待機動畫上）。
/// 走路用 Animator 的 Speed / WalkRate（AC_Villager）。
/// </summary>
public class NpcRoutine : MonoBehaviour
{
    public enum Act
    {
        /// <summary>站著（東張西望）</summary>
        Stand,
        /// <summary>坐下（seatY = 坐面的高度）</summary>
        Sit,
        /// <summary>舉鎚往前面敲（鐵砧在正前方）</summary>
        Hammer,
        /// <summary>彎腰顧爐火</summary>
        Tend,
        /// <summary>彎腰擦東西（長椅、桌面）</summary>
        Wipe,
        /// <summary>走進屋裡（人消失一陣子再出來）</summary>
        Inside,
        /// <summary>只是路過的點，不停</summary>
        Pass,
        /// <summary>蹲下來看地上的東西（石頭），一隻手伸過去摸</summary>
        Crouch,
        /// <summary>原地蹦蹦跳跳</summary>
        Hop,
        /// <summary>雙手捧著書低頭看（偶爾翻頁）；seatY &gt; 0 就坐著看。書是 book（只有看書時才出現）</summary>
        Read,
        /// <summary>在桌上的藥缽裡磨藥：左手扶著藥缽、右手拿藥杵畫圈；seatY &gt; 0 就坐著磨。藥杵是 tool（只有磨藥時才出現）</summary>
        Grind,
    }

    [System.Serializable]
    public struct Stop
    {
        public Vector3 position;
        public float yaw;
        public float duration;
        public Act act;
        public float seatY;
        [Tooltip("走到這一站的速度 (m/s)；0 = 用 walkSpeed。超過 runFrom 就用跑的")]
        public float speed;
        [Tooltip("Inside 用：這間房子的正門。有的話先拉開門、踏上台階走到門檻才進屋（門隨後關上），出來時也是開門、走下台階、關門")]
        public HouseEntryDoor door;
    }

    public Stop[] stops;
    public Animator animator;
    public float walkSpeed = 1.1f;
    [Tooltip("速度超過這個就播跑步（Animator 的 Speed = 2）")]
    public float runFrom = 2f;
    [Tooltip("跑步動畫原本的前進速度（m/s，這個人縮放 1 時）")]
    public float animationRunSpeed = 3.4f;
    [Tooltip("走路動畫原本的前進速度（m/s，這個人縮放 1 時）")]
    public float animationWalkSpeed = 1.72f;
    public float noticeRadius = 4.5f;
    [Tooltip("開：玩家走近也繼續做自己的事（老漁夫坐著釣魚，不站起來）")]
    public bool ignorePlayer;
    public float resumeRadius = 6.5f;
    [Tooltip("Read 的時候捧在手上的書（其他時候藏起來）；書的 +Y 是書頁那面、+Z 是書的上緣")]
    public Transform book;
    [Tooltip("Grind 的時候拿在右手的藥杵（其他時候藏起來）；+Y 是杵頭朝下的那一頭")]
    public Transform tool;
    [Tooltip("沒在看書的時候拿在右手輕輕搧的扇子（扇軸在原點、+Y 是扇面往上、+Z 是扇面朝前）；看書時收起來")]
    public Transform fan;

    [Header("左手受傷")]
    [Tooltip("左手受傷不能動：一直垂在身體旁邊、不跟著走路擺（做事也不動），事情都用右手做")]
    public bool injuredLeftArm;

    /// <summary>現在在做什麼（測試用）</summary>
    public Act CurrentAct => walking ? Act.Pass : stops != null && stops.Length > 0 ? stops[index].act : Act.Stand;
    public bool Walking => walking;

    /// <summary>
    /// 劇情接手（藥師倒藥給玩家，ApothecaryPotionGift）：日常暫停（不走、不坐、不看玩家），
    /// 走路的動畫用 ScriptedSpeed（m/s）；位置、方向、手臂由接手的腳本擺。做完呼叫 ResumeRoutine 走回這一站
    /// </summary>
    [System.NonSerialized] public bool Scripted;
    [System.NonSerialized] public float ScriptedSpeed;
    /// <summary>劇情中坐下（坐面的高度，0 = 站著）：坐在板凳上做事</summary>
    [System.NonSerialized] public float ScriptedSeatY;

    /// <summary>劇情結束：從現在的位置走回目前這一站，接著照常做事</summary>
    public void ResumeRoutine()
    {
        Scripted = false;
        ScriptedSpeed = 0f;
        ScriptedSeatY = 0f;
        if (stops == null || stops.Length == 0) return;
        from = transform.position;
        to = stops[index].position;
        Vector3 d = to - from;
        d.y = 0f;
        walkLen = d.magnitude;
        walkDone = 0f;
        legSpeed = stops[index].speed > 0f ? stops[index].speed : walkSpeed;
        walking = walkLen > 0.05f;
        timer = stops[index].duration;
    }
    public bool Interrupted => interrupted;
    public int StopIndex => index;
    public float SitWeight => sitW;

    int index;
    float lastSeatY; // 最後一次坐的坐面高度（站起來的過程用）
    bool doorMutedTalk; // 開門的動畫進行中把對話關掉了（門的動作做完再打開）
    float timer, walkDone, walkLen, sitW, actW, insideT, crouchW, hopW, readW, fanW, legSpeed;
    Vector3 modelRest;
    bool modelRestSet;
    bool walking, interrupted, hidden;
    Vector3 from, to;
    MerchantNPC npc;
    Transform player;
    Renderer[] renderers;
    float seed;

    Transform hips, spine, head, lUp, lLow, lFoot, rUp, rLow, rFoot, lArm, lFore, lHand, rArm, rFore, rHand;
    Transform lMiddle, rMiddle, lThumb, rThumb; // 手指根部：算手掌中心、手掌朝哪
    bool bonesFound;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WalkRateId = Animator.StringToHash("WalkRate");

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        var hp = PlayerHealth.Find();
        if (hp != null) player = hp.transform;
        renderers = GetComponentsInChildren<Renderer>(true);
        seed = Random.value * 10f;
        // 坐下、打鐵是在 LateUpdate 擺骨頭：Animator 被剔除時（主鏡頭沒看到）會在別的鏡頭（對話的半身像）拍的時候蓋掉，所以一直更新
        if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (stops == null || stops.Length == 0) { enabled = false; return; }
        index = 0;
        transform.SetPositionAndRotation(stops[0].position, Quaternion.Euler(0f, stops[0].yaw, 0f));
        timer = stops[0].duration * Random.Range(0.3f, 1f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool talking = npc != null && npc.InConversation;
        bool near = false;
        bool atDoor = doorPhase != DoorPhase.None; // 正在開門、上下台階：不停下來看玩家
        if (player != null && !hidden && !atDoor && !Scripted)
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            near = !ignorePlayer && d.magnitude < (interrupted ? resumeRadius : noticeRadius);
            if (near && !talking && d.sqrMagnitude > 0.01f) Face(d, 220f);
        }
        interrupted = (near || (talking && !ignorePlayer)) && !hidden && !atDoor && !Scripted;

        var stop = stops[index];
        float speed = 0f;
        float wantSit = 0f, wantAct = 0f, wantCrouch = 0f, wantHop = 0f, wantRead = 0f;

        if (Scripted)
        {
            speed = ScriptedSpeed; // 做事的姿勢都慢慢收掉（wantXxx = 0）；要坐就坐
            if (ScriptedSeatY > 0f) wantSit = 1f;
        }
        else if (atDoor)
        {
            speed = UpdateDoor(stop, dt);
        }
        else if (hidden)
        {
            // 在屋裡：時間到了才出來（有門的先拉開門，門開了才走出來）
            insideT -= dt;
            if (insideT <= 0f)
            {
                if (stop.act == Act.Inside && stop.door != null && stop.door.inside != null)
                {
                    // 有門：人出現在門檻後面（屋裡）、朝外，伸手拉開門才走出來
                    Vector3 o = stop.door.outside != null ? stop.door.outside.position : stop.position;
                    Vector3 inP = stop.door.inside.position;
                    transform.SetPositionAndRotation(Inner(inP, o), Quaternion.LookRotation(Flat(o - inP).normalized));
                    SetHidden(false, false);
                    BeginDoor(DoorPhase.OpenOut);
                }
                else SetHidden(false);
            }
        }
        else if (interrupted)
        {
            // 停下來看著玩家（坐著的先站起來）
        }
        else if (walking)
        {
            walkDone += legSpeed * dt;
            float t = walkLen > 0.001f ? Mathf.Clamp01(walkDone / walkLen) : 1f;
            transform.position = Vector3.Lerp(from, to, t);
            Vector3 dir = to - from;
            dir.y = 0f;
            Face(dir, 300f);
            speed = legSpeed;
            if (t >= 1f)
            {
                walking = false;
                timer = stop.duration;
                if (stop.act == Act.Inside)
                {
                    // 有門：先停在門口把門拉開（UpdateDoor 接著踏上台階進去）；沒有門就直接進屋
                    if (stop.door != null) BeginDoor(DoorPhase.UpIn);
                    else { SetHidden(true); insideT = stop.duration; }
                }
            }
        }
        else
        {
            // 在這一站：轉到該面向的方向，做這站的事
            if (stop.act != Act.Pass) Face(Quaternion.Euler(0f, stop.yaw, 0f) * Vector3.forward, 160f);
            if (stop.act == Act.Sit) wantSit = 1f;
            else if (stop.act == Act.Hammer || stop.act == Act.Tend || stop.act == Act.Wipe) wantAct = 1f;
            else if (stop.act == Act.Grind) { wantAct = 1f; if (stop.seatY > 0f) wantSit = 1f; }
            else if (stop.act == Act.Crouch) wantCrouch = 1f;
            else if (stop.act == Act.Hop) wantHop = 1f;
            else if (stop.act == Act.Read) { wantRead = 1f; if (stop.seatY > 0f) wantSit = 1f; }
            timer -= dt;
            // 坐著的要先站起來才走（看書的先把書收起來）
            if (timer <= 0f && sitW <= 0.02f && actW <= 0.05f && crouchW <= 0.05f && hopW <= 0.05f && readW <= 0.05f) StartWalk();
            else if (timer <= 0f)
            {
                wantAct = 0f; wantCrouch = 0f; wantHop = 0f; wantRead = 0f;
                if (readW <= 0.3f && actW <= 0.3f) wantSit = 0f;
            }
        }

        sitW = Mathf.MoveTowards(sitW, wantSit, dt * 2.2f);
        // 開門、關門、進出屋子的動畫進行中不能對話（對話提示也不出現）；在屋裡另外由 SetHidden 關掉；劇情（Scripted）由劇情自己管
        // （只還原自己關掉的，其他系統關掉的對話不會被打開）
        if (npc != null && !hidden && !Scripted)
        {
            if (doorPhase != DoorPhase.None && npc.enabled && !npc.InConversation) { npc.enabled = false; doorMutedTalk = true; }
            else if (doorPhase == DoorPhase.None && doorMutedTalk) { npc.enabled = true; doorMutedTalk = false; }
        }
        readW = Mathf.MoveTowards(readW, wantRead, dt * 2f);
        if (fan != null)
        {
            fanW = Mathf.MoveTowards(fanW, readW < 0.05f && actW < 0.05f && !hidden && doorPhase == DoorPhase.None && !Scripted ? 1f : 0f, dt * 4f); // 拉門時先把扇子收起來
            bool showFan = fanW > 0.35f && !hidden;
            if (fan.gameObject.activeSelf != showFan) fan.gameObject.SetActive(showFan);
        }
        if (tool != null)
        {
            bool show = actW > 0.25f && !hidden && !walking && !Scripted && stops[index].act == Act.Grind;
            if (tool.gameObject.activeSelf != show) tool.gameObject.SetActive(show);
        }
        if (book != null)
        {
            bool show = readW > 0.25f && !hidden;
            if (book.gameObject.activeSelf != show) book.gameObject.SetActive(show);
        }
        actW = Mathf.MoveTowards(actW, wantAct, dt * 3f);
        crouchW = Mathf.MoveTowards(crouchW, wantCrouch, dt * 3f);
        hopW = Mathf.MoveTowards(hopW, wantHop, dt * 4f);

        if (animator != null)
        {
            bool run = speed >= runFrom;
            float natural = (run ? animationRunSpeed : animationWalkSpeed) * transform.lossyScale.y;
            animator.SetFloat(SpeedId, speed > 0f ? (run ? 2f : 1f) : 0f, 0.12f, dt);
            animator.SetFloat(WalkRateId, speed > 0f ? speed / Mathf.Max(0.1f, natural) : 1f);
        }
    }

    void StartWalk()
    {
        index = (index + 1) % stops.Length;
        from = transform.position;
        to = stops[index].position;
        Vector3 d = to - from;
        d.y = 0f;
        walkLen = d.magnitude;
        walkDone = 0f;
        legSpeed = stops[index].speed > 0f ? stops[index].speed : walkSpeed;
        walking = true;
        if (walkLen < 0.05f) { walking = false; timer = stops[index].duration; }
    }

    void SetHidden(bool hide, bool sound = true)
    {
        hidden = hide;
        foreach (var r in renderers) if (r != null) r.enabled = !hide;
        if (npc != null) npc.enabled = !hide; // 在屋裡的時候不能對話
        foreach (var c in GetComponents<Collider>()) c.enabled = !hide;
        if (!hide) timer = 0f;
        if (!sound) return;
        if (!hide) GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.5f, 1.1f, 0.05f, 1f, 20f);
        else GameAudio.Play("SFX/RPG/doorClose_2", transform.position, 0.5f, 1.1f, 0.05f, 1f, 20f);
    }

    // ---------------- 開門進屋 / 出屋 ----------------

    // 進屋：UpIn 踏上台階到門前 → OpenIn 伸手拉開門 → StepIn 跨過門檻 → CloseIn 轉身、伸手把門拉上 → 人進屋（消失）
    // 出屋：OpenOut 在門檻後面伸手拉開門 → StepOut 跨出門檻到門前 → CloseOut 轉身、伸手把門拉上 → DownOut 走下台階
    enum DoorPhase { None, UpIn, OpenIn, StepIn, CloseIn, OpenOut, StepOut, CloseOut, DownOut }
    DoorPhase doorPhase;
    float doorT, handT;
    Vector3 doorFrom;
    // 拉門的手：0 = 沒拉，1 = 手放在門板的引手上；doorHandDoor 是現在拉的那扇門
    float doorHandW;
    HouseEntryDoor doorHandDoor;
    bool doorHandStarted, doorHandSidesSet;
    float doorSideSpeed; // 拉門前橫著挪步的速度（Animator 用）
    readonly bool[] doorHandLeft = new bool[2];

    /// <summary>現在在門口開門、上下台階（測試用）</summary>
    public bool AtDoor => doorPhase != DoorPhase.None;
    public bool Hidden => hidden;
    /// <summary>拉門的手放在門上多少（測試用）</summary>
    public float DoorHandWeight => doorHandW;
    public string DoorPhaseName => doorPhase.ToString();

    void BeginDoor(DoorPhase phase)
    {
        doorPhase = phase;
        doorT = 0f;
        handT = 0f;
        doorHandStarted = false;
        doorHandSidesSet = false;
        doorFrom = transform.position;
    }

    /// <summary>屋裡、門檻後面 0.3 m（地板的高度）：在這裡轉身關門，門板滑回來不會碰到身體</summary>
    static Vector3 Inner(Vector3 inP, Vector3 outP) => inP - Flat(outP - inP).normalized * 0.3f;

    /// <summary>門前（門檻外約 0.4 m）：有緣側就站在緣側上，沒有就站在地上</summary>
    Vector3 Porch(HouseEntryDoor door, Vector3 inP, Vector3 outP)
    {
        Vector3 outward = Flat(outP - inP).normalized;
        Vector3 p = inP + outward * 0.55f;
        float y = outP.y;
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, inP.y + 0.4f, p.z), Vector3.down, sitHits, inP.y + 0.4f - outP.y + 0.3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var h = sitHits[i];
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (best > float.NegativeInfinity) y = Mathf.Clamp(best, outP.y, inP.y);
        p.y = y;
        return p;
    }

    /// <summary>
    /// 伸手拉門：先轉向門（關門時要轉身）→ 兩手伸到兩片門板的引手上 → 門開始滑、手跟著門板推開 / 拉上 →
    /// 門滑到手搆不到（開門）或關好了（關門）手放下來。回傳 true = 做完了。
    /// </summary>
    bool PullDoor(HouseEntryDoor door, Vector3 toDoor, bool open, float dt)
    {
        doorHandDoor = door;
        doorSideSpeed = 0f;
        Face(toDoor, 330f);
        Vector3 f = transform.forward;
        if (!doorHandStarted && Vector3.Angle(Flat(f), Flat(toDoor)) > 12f) { doorHandW = Mathf.MoveTowards(doorHandW, 0f, dt * 4f); return false; }
        bool single = door.panels != null && door.panels.Length == 1;
        if (single && !doorHandStarted && handT <= 0f)
        {
            // 只有一扇：先橫著挪到引手前面（引手在要用的那隻手前方約 0.2 m），手才搆得到、拉得動
            Vector3 right = Vector3.Cross(Vector3.up, Flat(toDoor).normalized);
            float s = transform.lossyScale.y;
            Vector3 grip = door.Grip(0, transform.position.y + 1.02f * s, transform.position);
            float lat = Vector3.Dot(grip - transform.position, right);
            bool useLeft = lat < 0f && !injuredLeftArm;
            doorHandLeft[0] = useLeft;
            doorHandSidesSet = true;
            float move = lat - (useLeft ? -0.2f : 0.2f) * s;
            if (Mathf.Abs(move) > 0.03f)
            {
                float step = Mathf.Clamp(move, -0.9f * dt, 0.9f * dt);
                transform.position += right * step;
                doorSideSpeed = 0.9f;
                return false;
            }
        }
        handT += dt;
        const float reach = 0.35f;
        if (!doorHandStarted)
        {
            doorHandW = Mathf.SmoothStep(0f, 1f, handT / reach);
            if (handT >= reach)
            {
                doorHandStarted = true;
                if (open) door.Open(); else door.Close();
            }
            return false;
        }
        if (open)
        {
            // 推到一半多手就搆不到了，手放下來、門自己滑到底
            doorHandW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.95f, door.Progress));
            return door.IsOpen && doorHandW <= 0.001f;
        }
        // 關門：門板滑回來碰到手、一起拉到關好；關好了手才放下（只有一扇的門板要滑很遠，拉過一半手就放開）
        if (single)
        {
            doorHandW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 0.1f, door.Progress));
            return door.IsClosed && doorHandW <= 0.001f;
        }
        if (!door.IsClosed) { doorHandW = 1f; return false; }
        doorHandW = Mathf.MoveTowards(doorHandW, 0f, dt * 4f);
        return doorHandW <= 0.001f;
    }

    /// <summary>
    /// 進屋：踏上台階到門前 → 伸手拉開門 → 跨過門檻 → 轉身把門拉上 → 人進屋（消失）。
    /// 出屋：人出現在門檻後面、伸手拉開門 → 跨出來 → 轉身把門拉上 → 走下台階。回傳這一幀走路的速度（Animator 用）。
    /// </summary>
    float UpdateDoor(Stop stop, float dt)
    {
        var door = stop.door;
        if (door == null || door.inside == null) { doorPhase = DoorPhase.None; doorHandW = 0f; if (hidden) SetHidden(false); return 0f; }
        Vector3 outP = door.outside != null ? door.outside.position : stop.position;
        Vector3 inP = door.inside.position;
        Vector3 inward = inP - outP;
        inward.y = 0f;
        Vector3 porch = Porch(door, inP, outP);
        float stepSpeed = walkSpeed * 0.75f;
        switch (doorPhase)
        {
            case DoorPhase.UpIn:
            {
                doorHandW = 0f;
                doorT += stepSpeed * dt / Mathf.Max(0.25f, Flat(porch - doorFrom).magnitude);
                transform.position = StepPath(doorFrom, porch, doorT);
                Face(inward, 300f);
                if (doorT >= 1f) BeginDoor(DoorPhase.OpenIn);
                return doorT < 1f ? stepSpeed : 0f;
            }
            case DoorPhase.OpenIn:
                if (PullDoor(door, inward, true, dt)) BeginDoor(DoorPhase.StepIn);
                return doorSideSpeed;
            case DoorPhase.StepIn:
            {
                Vector3 inner = Inner(inP, outP);
                doorT += stepSpeed * dt / Mathf.Max(0.3f, Flat(inner - doorFrom).magnitude);
                transform.position = StepPath(doorFrom, inner, doorT);
                Face(inward, 300f);
                if (doorT >= 1f) BeginDoor(DoorPhase.CloseIn);
                return stepSpeed;
            }
            case DoorPhase.CloseIn:
                if (PullDoor(door, -inward, false, dt))
                {
                    SetHidden(true, false);
                    insideT = stop.duration;
                    doorPhase = DoorPhase.None;
                }
                return doorSideSpeed;
            case DoorPhase.OpenOut:
                if (PullDoor(door, -inward, true, dt)) BeginDoor(DoorPhase.StepOut);
                return doorSideSpeed;
            case DoorPhase.StepOut:
            {
                doorT += stepSpeed * dt / Mathf.Max(0.3f, Flat(porch - doorFrom).magnitude);
                transform.position = StepPath(doorFrom, porch, doorT);
                Face(-inward, 300f);
                if (doorT >= 1f) BeginDoor(DoorPhase.CloseOut);
                return stepSpeed;
            }
            case DoorPhase.CloseOut:
                if (PullDoor(door, inward, false, dt)) BeginDoor(DoorPhase.DownOut);
                return doorSideSpeed;
            case DoorPhase.DownOut:
            {
                doorHandW = 0f;
                doorT += stepSpeed * dt / Mathf.Max(0.25f, Flat(outP - doorFrom).magnitude);
                transform.position = StepPath(doorFrom, outP, doorT);
                Face(-inward, 300f);
                if (doorT >= 1f)
                {
                    doorPhase = DoorPhase.None;
                    timer = 0f; // 接著走去下一站
                }
                return stepSpeed;
            }
        }
        return 0f;
    }

    /// <summary>
    /// 拉門的手（LateUpdate）：兩手各放在一片門板的引手上（左邊那片用左手），身體微微前傾；手肘往外下方彎。
    /// 手掌朝著門板。門板滑遠了手搆不到就伸直（IK 只伸到手臂的長度）。
    /// </summary>
    void PoseDoorHands(float w, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        var door = doorHandDoor;
        if (door == null || door.panels == null) return;
        float e = Mathf.SmoothStep(0f, 1f, w);
        if (spine != null) spine.rotation = Quaternion.AngleAxis(7f * e, side) * spine.rotation;
        float handY = transform.position.y + 1.02f * s;
        for (int i = 0; i < door.panels.Length && i < 2; i++)
        {
            Vector3 grip = door.Grip(i, handY, transform.position);
            // 用哪隻手：開始拉的時候決定，之後不換（只開一扇的門板會從身體一邊滑到另一邊）
            if (!doorHandSidesSet)
            {
                for (int k = 0; k < door.panels.Length && k < 2; k++)
                    doorHandLeft[k] = Vector3.Dot(door.Grip(k, handY, transform.position) - transform.position, side) < 0f;
                doorHandSidesSet = true;
            }
            bool left = doorHandLeft[i];
            if (left && injuredLeftArm)
            {
                if (door.panels.Length > 1) continue; // 左手受傷：兩扇的門只用右手拉右邊那扇
                left = false;                          // 只有一扇：右手伸過去拉
            }
            Transform a = left ? lArm : rArm, b = left ? lFore : rFore, c = left ? lHand : rHand;
            if (a == null || b == null || c == null) continue;
            Vector3 pole = a.position - up * 0.45f * s + side * (left ? -0.4f : 0.4f) * s - fwd * 0.15f * s;
            // 手掌中心放在引手上：手腕的目標往回退「手腕到手掌中心」那一段
            Vector3 palmOff = Palm(c, left ? lMiddle : rMiddle, b, s) - c.position;
            ReachIK(a, b, c, grip - palmOff, pole, e);
            TurnPalm(c, left ? lMiddle : rMiddle, left ? lThumb : rThumb, left, (grip - a.position).normalized, e);
        }
    }

    /// <summary>兩節骨頭的 IK（肩 → 手肘 → 手腕）：手伸到 target（搆不到就伸直指著它），手肘往 pole 那邊彎</summary>
    internal static void ReachIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole, float w)
    {
        if (upper == null || lower == null || end == null || w <= 0.001f) return;
        float l1 = Vector3.Distance(upper.position, lower.position), l2 = Vector3.Distance(lower.position, end.position);
        if (l1 < 1e-4f || l2 < 1e-4f) return;
        Vector3 root = upper.position, to = target - root;
        if (to.sqrMagnitude < 1e-6f) return;
        float d = Mathf.Clamp(to.magnitude, Mathf.Abs(l1 - l2) + 0.01f, (l1 + l2) * 0.999f);
        Vector3 dir = to.normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole - root, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(lower.position - root, dir);
        bend.Normalize();
        float cos = Mathf.Clamp((l1 * l1 + d * d - l2 * l2) / (2f * l1 * d), -1f, 1f);
        Vector3 elbow = root + dir * (l1 * cos) + bend * (l1 * Mathf.Sqrt(1f - cos * cos));
        Aim(upper, lower, elbow - root, w);
        Aim(lower, end, root + dir * d - lower.position, w);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>上下台階的路線：水平等速走過去，高度分兩段（先踩上 / 下第一階到一半高，再踩上 / 下第二階）</summary>
    static Vector3 StepPath(Vector3 a, Vector3 b, float t)
    {
        t = Mathf.Clamp01(t);
        Vector3 p = Vector3.Lerp(a, b, t);
        float k = t < 0.45f ? 0.5f * Mathf.SmoothStep(0f, 1f, t / 0.45f) : 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, (t - 0.45f) / 0.55f);
        p.y = a.y + (b.y - a.y) * k;
        return p;
    }

    void Face(Vector3 dir, float degPerSec)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
    }

    // ---------------- 姿勢 ----------------

    void FindBones()
    {
        bonesFound = true;
        if (animator == null || !animator.isHuman) return;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        hips = B(HumanBodyBones.Hips);
        spine = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
        head = B(HumanBodyBones.Head);
        lUp = B(HumanBodyBones.LeftUpperLeg); lLow = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
        rUp = B(HumanBodyBones.RightUpperLeg); rLow = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
        lArm = B(HumanBodyBones.LeftUpperArm); lFore = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
        rArm = B(HumanBodyBones.RightUpperArm); rFore = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
        lMiddle = B(HumanBodyBones.LeftMiddleProximal); rMiddle = B(HumanBodyBones.RightMiddleProximal);
        lThumb = B(HumanBodyBones.LeftThumbProximal); rThumb = B(HumanBodyBones.RightThumbProximal);
    }

    void LateUpdate()
    {
        if (hidden || animator == null) return;
        if (!bonesFound) FindBones();
        if (hips == null) return;
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        float s = transform.lossyScale.y;
        float time = Time.time + seed;

        // 站著的時候偶爾轉頭看看四周
        if (!walking && !interrupted && head != null && stops[index].act == Act.Stand)
            head.rotation = Quaternion.AngleAxis(Mathf.Sin(time * 0.6f) * 30f * Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.Sin(time * 0.23f))), up) * head.rotation;

        if (sitW > 0.001f)
        {
            // 坐面高度是世界座標：站起來的那一段（sitW 慢慢變 0）這一站可能沒有坐面（劇情裡站起來、seatY = 0），
            // 用 0 的話屁股會被拉到 y = 0（整個人沉進地底下再冒出來），所以沿用最後一次真的坐的高度
            float seatY = Scripted && ScriptedSeatY > 0f ? ScriptedSeatY : stops[index].seatY;
            if (seatY > 0f) lastSeatY = seatY; else seatY = lastSeatY;
            if (seatY > 0f) PoseSit(seatY, sitW, up, fwd, side, s);
        }
        if (actW > 0.001f) PoseWork(stops[index].act, actW, time, up, fwd, side, s);
        if (crouchW > 0.001f) PoseCrouch(crouchW, time, up, fwd, side, s);
        if (readW > 0.001f) PoseRead(readW, time, up, fwd, side, s);
        PoseHop(hopW, time, up, fwd, side, s);
        if (fan != null && fanW > 0.001f) PoseFan(fanW, time, up, fwd, side, s);
        if (doorHandW > 0.001f) PoseDoorHands(doorHandW, up, fwd, side, s);
        if (injuredLeftArm) PoseInjuredArm(up, fwd, side, s);
    }

    /// <summary>
    /// 受傷的左手：整隻手臂無力地垂在身體旁邊（稍微離開身體、手肘微彎），走路、做事都不會擺；最後才擺，蓋掉所有動作。
    /// 跟著胸口的方向（身體前傾、扭腰時手跟著）。垂著跟模型原本的姿勢很接近，衣服不會被拉走。
    /// </summary>
    void PoseInjuredArm(Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        if (lArm == null || lFore == null || lHand == null) return;
        Vector3 bodyUp = spine != null ? spine.TransformDirection(Vector3.up) : up;
        if (Vector3.Dot(bodyUp, up) < 0.3f) bodyUp = up;
        Vector3 bodyFwd = Vector3.ProjectOnPlane(fwd, bodyUp).normalized;
        Vector3 bodySide = Vector3.Cross(bodyUp, bodyFwd); // 身體的右邊
        Aim(lArm, lFore, -bodyUp * 0.97f + bodyFwd * 0.06f - bodySide * 0.16f, 1f);
        Aim(lFore, lHand, -bodyUp * 0.93f + bodyFwd * 0.22f - bodySide * 0.06f, 1f);
    }

    /// <summary>手掌中心：手腕往中指根部 60%（沒有手指骨頭就沿前臂方向往前 5 cm）</summary>
    internal static Vector3 Palm(Transform hand, Transform middle, Transform fore, float s)
    {
        if (middle != null) return Vector3.Lerp(hand.position, middle.position, 0.6f);
        Vector3 d = fore != null ? (hand.position - fore.position).normalized : Vector3.zero;
        return hand.position + d * 0.05f * s;
    }

    /// <summary>
    /// 手掌轉向 want（繞手指的方向轉）：手掌法線 = 中指方向 × 拇指方向（左手反過來），只轉手腕不動手臂。
    /// </summary>
    internal static void TurnPalm(Transform hand, Transform middle, Transform thumb, bool left, Vector3 want, float w)
    {
        if (hand == null || middle == null || thumb == null || w <= 0.001f) return;
        Vector3 along = (middle.position - hand.position).normalized;
        Vector3 toThumb = thumb.position - hand.position;
        Vector3 normal = Vector3.Cross(along, toThumb).normalized * (left ? -1f : 1f);
        Vector3 a = Vector3.ProjectOnPlane(normal, along), b = Vector3.ProjectOnPlane(want, along);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return;
        float angle = Vector3.SignedAngle(a, b, along) * w;
        hand.rotation = Quaternion.AngleAxis(angle, along) * hand.rotation;
    }

    /// <summary>拿扇子：右手前臂抬到胸前，扇子立在手上、扇面朝著自己，手腕慢慢左右搧</summary>
    void PoseFan(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        float e = Mathf.SmoothStep(0f, 1f, w);
        Aim(rArm, rFore, -up * 0.85f + fwd * 0.3f + side * 0.2f, e);
        Aim(rFore, rHand, up * 0.55f + fwd * 0.6f - side * 0.25f, e);
        if (rHand == null || !fan.gameObject.activeSelf) return;
        float wave = Mathf.Sin(time * 2.4f) * 22f;
        Vector3 face = Quaternion.AngleAxis(wave, up) * (-fwd * 0.75f - side * 0.35f).normalized; // 扇面朝自己（往臉搧）
        Vector3 fanUp = (up + side * 0.25f + fwd * 0.1f).normalized;
        // 扇骨握在手掌裡：扇軸（扇子的原點）在手掌中心下面一點，扇子從虎口往上伸出去
        Vector3 grip = Palm(rHand, rMiddle, rFore, s);
        fan.SetPositionAndRotation(grip - fanUp * 0.03f * s, Quaternion.LookRotation(-face, fanUp));
    }

    /// <summary>看書：上臂垂在身側、前臂往前往內收到胸前，雙手捧著書；低頭看，大約每 7 秒右手翻一頁</summary>
    void PoseRead(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        float e = Mathf.SmoothStep(0f, 1f, w);
        float page = Mathf.Repeat(time / 7f, 1f);
        float flip = page > 0.9f ? Mathf.Sin((page - 0.9f) / 0.1f * Mathf.PI) : 0f; // 翻頁：右手往左上掀一下
        if (spine != null) spine.rotation = Quaternion.AngleAxis(6f * e, side) * spine.rotation;
        // 兩手分開托著書的兩邊（書約 28 cm 寬，兩個手掌相距約 20 cm）
        Aim(lArm, lFore, -up * 0.9f + fwd * 0.32f - side * 0.16f, e);
        Aim(rArm, rFore, -up * 0.9f + fwd * 0.32f + side * 0.16f, e);
        Aim(lFore, lHand, fwd * 0.85f + up * 0.32f + side * 0.2f, e);
        Aim(rFore, rHand, fwd * 0.85f + up * (0.32f + 0.25f * flip) - side * (0.2f + 0.4f * flip), e);
        if (head != null) head.rotation = Quaternion.AngleAxis((24f + Mathf.Sin(time * 0.5f) * 3f) * e, side) * head.rotation;

        if (book == null || lHand == null || rHand == null || !book.gameObject.activeSelf) return;
        // 書放在兩隻手掌上：書的下面貼著兩個手掌中心，書頁朝著眼睛；兩隻手掌朝上托著書
        Vector3 lp = Palm(lHand, lMiddle, lFore, s), rp = Palm(rHand, rMiddle, rFore, s);
        Vector3 mid = (lp + rp) * 0.5f;
        Vector3 eye = head != null ? head.position + fwd * 0.08f * s : mid + up * 0.4f * s - fwd * 0.3f * s;
        Vector3 toEye = (eye - mid).normalized;
        TurnPalm(lHand, lMiddle, lThumb, true, toEye, e);
        TurnPalm(rHand, rMiddle, rThumb, false, toEye, e * (1f - flip));
        lp = Palm(lHand, lMiddle, lFore, s);
        if (flip < 0.01f) rp = Palm(rHand, rMiddle, rFore, s);
        mid = (lp + rp) * 0.5f;
        Vector3 top = Vector3.ProjectOnPlane(fwd, toEye);
        if (top.sqrMagnitude < 1e-4f) top = up;
        book.SetPositionAndRotation(mid + toEye * 0.03f, Quaternion.LookRotation(top.normalized, toEye));
    }

    // 腳的骨頭（腳踝）離腳底多高（× 縮放）：坐下時腳放回地上用。站著時量出來的不準（待機動畫會抬腳、站在台階邊），用固定值
    const float AnkleHeight = 0.08f;
    static readonly RaycastHit[] sitHits = new RaycastHit[8];

    void PoseSit(float seatY, float w, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        // 屁股坐在坐面正中間（這一站的位置就是坐面中心），稍微往前一點點；以前只往下降，骨盆在身體前面的人會坐到椅子外面
        Vector3 seat = new Vector3(transform.position.x, seatY + 0.1f * s, transform.position.z) + fwd * 0.04f * s;
        hips.position = Vector3.Lerp(hips.position, seat, w);
        // 大腿往前平放
        Aim(lUp, lLow, fwd - side * 0.12f - up * 0.05f, w);
        Aim(rUp, rLow, fwd + side * 0.12f - up * 0.05f, w);
        // 小腿往下、腳平踩在地上（膝蓋正下方稍微往前）；搆不到地面（坐在棧橋邊）就自然垂著
        PlantFoot(lUp, lLow, lFoot, w, up, fwd, side, s);
        PlantFoot(rUp, rLow, rFoot, w, up, fwd, side, s);
        Aim(lArm, lFore, -up * 0.85f + fwd * 0.35f - side * 0.08f, w);
        Aim(rArm, rFore, -up * 0.85f + fwd * 0.35f + side * 0.08f, w);
        Aim(lFore, lHand, fwd - up * 0.35f, w);
        Aim(rFore, rHand, fwd - up * 0.35f, w);
    }

    /// <summary>蹲下：屁股降到膝蓋高度附近，膝蓋往前、小腿往後收，低頭看地上，右手往前下方伸</summary>
    /// <summary>
    /// 坐著時把腳放到地上：小腿長度固定，腳落在膝蓋前下方剛好踩到地面的地方。
    /// 椅子比腿高（小腿伸直還踩不到地）：大腿往下斜一點讓膝蓋降下來；地面遠到怎樣都踩不到（坐在棧橋邊）就自然垂著。
    /// </summary>
    void PlantFoot(Transform upLeg, Transform low, Transform foot, float w, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        if (upLeg == null || low == null || foot == null) return;
        float shin = Vector3.Distance(low.position, foot.position);
        float thigh = Vector3.Distance(upLeg.position, low.position);
        Quaternion footRot = foot.rotation;
        float ground = GroundBelow(low.position, shin * 1.8f);
        float ankle = AnkleHeight * s;
        float dh = low.position.y - (ground + ankle);
        bool reachable = !float.IsNegativeInfinity(ground) && dh < shin + thigh * 0.5f;
        if (reachable && dh > shin * 0.97f)
        {
            // 膝蓋要降多少才踩得到：大腿繞左右軸往下轉
            float need = dh - shin * 0.97f;
            float angle = Mathf.Asin(Mathf.Clamp(need / Mathf.Max(0.05f, thigh), 0f, 0.6f)) * Mathf.Rad2Deg;
            upLeg.rotation = Quaternion.AngleAxis(angle * w, side) * upLeg.rotation;
            ground = GroundBelow(low.position, shin * 1.8f);
            if (float.IsNegativeInfinity(ground)) reachable = false;
            else dh = low.position.y - (ground + ankle);
        }
        Vector3 dir;
        if (!reachable) dir = -up + fwd * 0.12f; // 搆不到：垂著
        else
        {
            float dh2 = Mathf.Clamp(dh, 0.05f * s, shin);
            float forward = Mathf.Sqrt(Mathf.Max(0f, shin * shin - dh2 * dh2));
            dir = -up * dh2 + fwd * forward;
        }
        Aim(low, foot, dir, w);
        foot.rotation = Quaternion.Slerp(foot.rotation, footRot, 0.5f); // 腳掌大致維持原本的角度
    }

    float GroundBelow(Vector3 from, float range)
    {
        float ground = float.NegativeInfinity;
        int n = Physics.RaycastNonAlloc(from, Vector3.down, sitHits, range, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
            if (!sitHits[i].collider.transform.IsChildOf(transform)) ground = Mathf.Max(ground, sitHits[i].point.y);
        return ground;
    }

    void PoseCrouch(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        float rest = hips.position.y - transform.position.y;
        hips.position -= up * rest * 0.5f * w;
        Aim(lUp, lLow, fwd * 0.9f - up * 0.25f - side * 0.25f, w);
        Aim(rUp, rLow, fwd * 0.9f - up * 0.25f + side * 0.25f, w);
        Aim(lLow, lFoot, -up - fwd * 0.55f, w);
        Aim(rLow, rFoot, -up - fwd * 0.55f, w);
        if (spine != null) spine.rotation = Quaternion.AngleAxis(32f * w, side) * spine.rotation;
        if (head != null) head.rotation = Quaternion.AngleAxis((22f + Mathf.Sin(time * 1.3f) * 6f) * w, side) * head.rotation;
        float poke = Mathf.Sin(time * 2.4f) * 0.12f;
        Aim(rArm, rFore, fwd * 0.6f - up * 0.75f + side * 0.15f, w);
        Aim(rFore, rHand, fwd * 0.7f - up * (0.6f + poke), w);
        Aim(lArm, lFore, -up * 0.8f + fwd * 0.4f - side * 0.2f, w);
        Aim(lFore, lHand, fwd - up * 0.3f, w);
    }

    /// <summary>蹦跳：整個人往上彈（模型往上移），跳到空中時膝蓋往上收</summary>
    void PoseHop(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        var model = animator.transform;
        if (!modelRestSet) { modelRest = model.localPosition; modelRestSet = true; }
        if (w <= 0.001f) { model.localPosition = modelRest; return; }
        float h = Mathf.Abs(Mathf.Sin(time * 4.2f));
        model.localPosition = modelRest;
        model.position += up * h * 0.32f * s * w;
        float tuck = h * w;
        Aim(lUp, lLow, fwd * 0.6f - up * 0.8f - side * 0.1f, tuck);
        Aim(rUp, rLow, fwd * 0.6f - up * 0.8f + side * 0.1f, tuck);
        Aim(lLow, lFoot, -up - fwd * 0.3f, tuck);
        Aim(rLow, rFoot, -up - fwd * 0.3f, tuck);
        Aim(lArm, lFore, up * 0.6f - side * 0.6f + fwd * 0.2f, w * 0.8f);
        Aim(rArm, rFore, up * 0.6f + side * 0.6f + fwd * 0.2f, w * 0.8f);
    }

    float lastStrike = -1f;

    void PoseWork(Act act, float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        switch (act)
        {
            case Act.Hammer:
            {
                // 一下約 1.2 秒：慢慢舉高到頭後面 → 快速往前下敲 → 停一下
                float p = Mathf.Repeat(time * 0.83f, 1f);
                float k = p < 0.55f ? Mathf.SmoothStep(0f, 1f, p / 0.55f) : p < 0.68f ? 1f - Mathf.SmoothStep(0f, 1f, (p - 0.55f) / 0.13f) : 0f;
                if (spine != null) spine.rotation = Quaternion.AngleAxis(12f * w, side) * spine.rotation;
                Vector3 armUp = up * 0.85f + fwd * 0.2f + side * 0.25f, armDown = fwd * 0.75f - up * 0.45f + side * 0.1f;
                Vector3 foreUp = up * 0.3f - fwd * 0.6f, foreDown = fwd * 0.5f - up * 0.85f;
                Aim(rArm, rFore, Vector3.Slerp(armDown.normalized, armUp.normalized, k), w);
                Aim(rFore, rHand, Vector3.Slerp(foreDown.normalized, foreUp.normalized, k), w);
                // 左手扶著鐵砧上的東西
                Aim(lArm, lFore, fwd * 0.6f - up * 0.7f - side * 0.2f, w);
                Aim(lFore, lHand, fwd - up * 0.2f, w);
                // 敲下去的瞬間：叮一聲、冒火花
                if (w > 0.9f && p >= 0.68f && p < 0.72f && time - lastStrike > 0.5f)
                {
                    lastStrike = time;
                    Vector3 at = transform.position + fwd * 0.75f * s + up * 0.75f;
                    GameAudio.Play("SFX/RPG/metalPot1", at, 0.55f, Random.Range(1.3f, 1.5f), 0.05f, 1f, 25f);
                    CombatVFX.Impact(at, new Color(1f, 0.7f, 0.3f));
                }
                break;
            }
            case Act.Tend:
            {
                // 彎腰顧爐火：右手往前下方慢慢撥動
                if (spine != null) spine.rotation = Quaternion.AngleAxis(18f * w, side) * spine.rotation;
                float o = Mathf.Sin(time * 2f) * 0.18f;
                Aim(rArm, rFore, fwd * 0.7f - up * 0.6f + side * 0.2f + up * o, w);
                Aim(rFore, rHand, fwd - up * 0.2f + up * o, w);
                break;
            }
            case Act.Grind:
            {
                // 磨藥：身體往前傾、低頭看藥缽；左手扶著藥缽，右手拿藥杵在缽裡畫圈
                if (spine != null) spine.rotation = Quaternion.AngleAxis(16f * w, side) * spine.rotation;
                if (head != null) head.rotation = Quaternion.AngleAxis(22f * w, side) * head.rotation;
                float a = time * 3.4f;
                Vector3 circle = (side * Mathf.Cos(a) + fwd * Mathf.Sin(a)) * 0.1f;
                Aim(lArm, lFore, -up * 0.75f + fwd * 0.5f - side * 0.08f, w);
                Aim(lFore, lHand, fwd * 0.75f - up * 0.35f + side * 0.4f, w);
                Aim(rArm, rFore, -up * 0.7f + fwd * 0.55f + side * 0.1f + circle * 0.6f, w);
                Aim(rFore, rHand, fwd * 0.7f - up * 0.55f - side * 0.3f + circle, w);
                if (tool != null && rHand != null && tool.gameObject.activeSelf)
                    tool.SetPositionAndRotation(rHand.position + fwd * 0.03f * s, Quaternion.FromToRotation(Vector3.up, (-up + fwd * 0.15f).normalized));
                break;
            }
            case Act.Wipe:
            {
                // 彎腰擦長椅：右手在前下方左右來回
                if (spine != null) spine.rotation = Quaternion.AngleAxis(28f * w, side) * spine.rotation;
                float o = Mathf.Sin(time * 3.2f) * 0.6f;
                Aim(rArm, rFore, fwd * 0.7f - up * 0.7f + side * 0.15f, w);
                Aim(rFore, rHand, fwd * 0.6f - up * 0.5f + side * o, w);
                Aim(lArm, lFore, fwd * 0.4f - up * 0.8f - side * 0.2f, w);
                break;
            }
        }
    }

    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir.normalized), w) * bone.rotation;
    }

    void OnDrawGizmosSelected()
    {
        if (stops == null) return;
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        for (int i = 0; i < stops.Length; i++)
        {
            Gizmos.DrawWireSphere(stops[i].position, 0.25f);
            Gizmos.DrawLine(stops[i].position, stops[(i + 1) % stops.Length].position);
        }
    }
}
