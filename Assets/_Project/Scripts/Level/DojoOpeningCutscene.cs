using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 道場開場演出：玩家第一次按 E 推開大門（DojoCourtyardGate）時開始。
///   1. 特寫玩家推門：玩家移到門板前，門跟著推門動畫裡「手往前推的位置」打開；手推到底就收手，門自己盪開
///   2. 茶室：老師父坐在離大門最近的茶几旁，端起茶杯喝一口（手臂用 IK 做的）
///   3. 好幾個固定鏡頭（每個停一樣久，時間到就切，鏡頭慢慢往老師父推近）：老師父走出茶室、走下台階、走過庭院；
///      老師父跟不上就直接移到下一個鏡頭該在的位置。門開了之後玩家也慢慢往庭院中間走。
///   4. 高空俯視：兩人慢慢走近，在庭院中間（MeetPoint）隔一段距離停下 → 劇情對話（StoryDialogue，跟沙漠商人同一套）
///   5. 對話結束：鏡頭從高空飛回玩家背後、可以操作 → 老師父開始 Boss 戰（DojoMasterBoss，血條 / 音樂跟森林島一樣）
/// 演出期間玩家不能操作；對話前按 Space 可以跳過鏡頭直接到對話。
/// 選單 GeminiGrowUp > 道場 > 建立開場演出 會把這個物件、路線、鏡頭都擺好，之後可以在場景裡直接拖動調整。
/// </summary>
[DisallowMultipleComponent]
public sealed class DojoOpeningCutscene : MonoBehaviour
{
    [System.Serializable]
    public class DialogueLine
    {
        [Tooltip("打勾 = 玩家說的話；不打勾 = 老師父說的話")]
        public bool playerSpeaks;
        [TextArea(2, 4)] public string text;
        public DialogueLine() { }
        public DialogueLine(bool player, string text) { playerSpeaks = player; this.text = text; }
    }

    [Header("場景物件（選單會自動填好）")]
    public DojoCourtyardGate gate;
    [Tooltip("PF_DojoMaster（老師父）的根物件")]
    public Transform master;
    [Tooltip("坐墊表面：位置 = 老師父坐的地方，藍色箭頭（Z）= 老師父面向")]
    public Transform sitPoint;
    [Tooltip("老師父走出來的路線（依序）。最後一點之後會接到會面點")]
    public List<Transform> path = new List<Transform>();
    [Tooltip("兩人會面的庭院中心。藍色箭頭（Z）指向主屋：老師父停在箭頭那一側，玩家停在另一側")]
    public Transform meetPoint;
    [Tooltip("鏡頭（依序播放）")]
    public List<DojoCutsceneShot> shots = new List<DojoCutsceneShot>();

    [Header("鏡頭")]
    [Tooltip("每個鏡頭停幾秒（最後的會面鏡頭除外：等兩人走到定位）")]
    [Min(0.5f)] public float shotSeconds = 3f;

    [Header("會面（高空俯視）")]
    [Tooltip("兩人停下時相隔幾公尺")]
    [Min(1f)] public float meetingGap = 4.5f;
    [Tooltip("俯視鏡頭裡兩人走幾秒才到定位")]
    [Min(1f)] public float meetingSeconds = 5f;
    [Tooltip("俯視鏡頭的高度：一開始 → 兩人停下時（慢慢降下來）")]
    public float meetHeightStart = 16f, meetHeightEnd = 10f;

    [Header("老師父走路")]
    [Tooltip("走路動畫原本的速度（公尺/秒），跟動畫對得上腳才不會滑")]
    public float walkSpeed = 0.71f;
    [Tooltip("走路動畫播放倍率（走路速度會一起變）")]
    [Range(0.5f, 2f)] public float walkAnimSpeed = 1.1f;
    public float turnDegreesPerSecond = 240f;

    [Header("玩家走路")]
    [Tooltip("玩家走路的速度（0～1，1 = 一般走路速度）")]
    [Range(0.1f, 1f)] public float playerWalkInput = 0.3f;
    [Tooltip("按 E 之後幾秒玩家開始往庭院中間走（等門開）")]
    [Min(0f)] public float playerWalkDelay = 3.6f;

    [Header("推門")]
    [Tooltip("推門時把玩家移到門板前（手剛好碰得到門）")]
    public bool snapPlayerToDoor = true;
    [Tooltip("站的位置：手剛碰到門時，手離身體多遠（跟髖部高度的比例，從推門動畫量出來的）")]
    public float contactReach = 0.55f;
    [Tooltip("手推的時候門跟著手轉的倍率（1 = 門板剛好貼著手）")]
    [Range(0.5f, 6f)] public float doorFollowGain = 1f;
    [Tooltip("手推完之後，門自己盪到全開要幾秒")]
    [Min(0.2f)] public float doorCoastSeconds = 2.2f;
    [Tooltip("手推到底之後，手放回待機姿勢花幾秒（推門動畫後面那段一直按著門的部分不播）")]
    [Min(0.05f)] public float pushReleaseBlend = 0.6f;

    [Header("老師父喝茶")]
    [Tooltip("坐著喝茶（Tripo 的喝茶動畫）時，右手端起來的時候手上拿著茶杯（複製場景裡最近的 TeaCup；找不到就用一個簡單的陶杯）")]
    public bool holdTeaCup = true;
    [Tooltip("找不到桌上的 TeaCup 時，自動做的陶杯顏色")]
    public Color fallbackCupColor = new Color(0.82f, 0.78f, 0.68f);
    [Tooltip("喝茶動畫裡「喝」那一刻在動畫的哪裡（0～1）：用來量茶杯要拿在手的哪個位置")]
    [Range(0f, 1f)] public float sipMoment = 0.6f;
    [Tooltip("拍喝茶鏡頭時，從喝茶動畫的哪裡開始播（0～1）：讓 3 秒內剛好拍到端起來、喝、放下")]
    [Range(0f, 1f)] public float teaShotStart = 0.17f;

    [Header("對話（內容之後可以直接在這裡改）")]
    public string masterName = "老師父";
    public string playerName = "米格爾";
    public Color masterColor = new Color(0.72f, 0.9f, 0.62f);
    public DialogueLine[] lines =
    {
        new DialogueLine(false, "……門外的風，帶著沙塵的味道。"),
        new DialogueLine(false, "年輕人，你是從那片沙漠一路走過來的吧？"),
        new DialogueLine(true,  "你是誰？這裡又是什麼地方？"),
        new DialogueLine(false, "老夫不過是個在這裡喝茶、看門的老頭子。這座道場，已經很久沒有客人了。"),
        new DialogueLine(false, "你的眼神很急——是在找回去的路？還是在找打倒某個人的力量？"),
        new DialogueLine(true,  "……兩個都是。"),
        new DialogueLine(false, "呵呵，好。那就先讓老夫看看，你手上的刀有幾分斤兩。"),
    };

    [Header("對話結束：鏡頭飛回玩家")]
    [Tooltip("從俯視鏡頭飛到玩家第三人稱鏡頭要幾秒")]
    [Min(0.3f)] public float flySeconds = 2.4f;
    [Tooltip("飛行路線中間往上拱多高（公尺）")]
    public float flyArc = 1.5f;
    [Tooltip("接回玩家時鏡頭的俯角（度）")]
    public float handOffPitch = 12f;

    [Header("Boss 戰")]
    [Tooltip("對話結束、鏡頭飛回玩家之後，老師父開始 Boss 戰（DojoMasterBoss）")]
    public bool startBossFight = true;
    [Tooltip("Boss 戰場地半徑（以會面點為中心）")]
    public float bossArenaRadius = 24f;

    [Header("其他")]
    [Tooltip("只播一次（推第二次門不會再播）")]
    public bool playOnce = true;
    [Tooltip("對話之前的鏡頭可以按 Space 跳過")]
    public bool allowSkip = true;
    [Tooltip("上下黑邊（電影感）")]
    public bool letterbox = true;
    [Tooltip("對話結束後老師父待機（說話動畫放慢）的速度")]
    [Range(0f, 1f)] public float idleTalkSpeed = 0.35f;

    static readonly int SitState = Animator.StringToHash("SitIdle");
    static readonly int WalkState = Animator.StringToHash("Walk");
    static readonly int TalkState = Animator.StringToHash("Talk");
    static readonly int PushState = Animator.StringToHash("PushGate");
    static readonly int IdleState = Animator.StringToHash("Idle");

    PlayerHealth player;
    ThirdPersonController controller;
    CharacterController body;
    PlayerVisualAnimator visual;
    Animator playerAnim;
    ThirdPersonCamera tpc;
    Animator masterAnim;
    Collider[] masterColliders;
    CinemachineCamera vcam;

    bool played, running, skip, inDialogue, flying;
    float letterboxAmount;

    // 老師父
    readonly List<Vector3> pts = new List<Vector3>();
    readonly List<float> cum = new List<float>();
    float dist, total;
    bool masterWalking;
    Vector3 masterStop, playerStop, meetForward;
    float masterApproach, playerApproach, masterYVelocity;
    float playStartedAt;

    // 玩家
    readonly List<Vector3> playerRoute = new List<Vector3>();
    int playerIndex;
    bool playerWalking, playerArrived;

    // 大門
    float leafFront = 0.15f, leafBack = 0.15f; // 門板正面 / 背面離鉸鏈線幾公尺
    Vector3 outward = Vector3.back;             // 從大門指向玩家那一側
    float openingSign = 1f, faceDistance, hipsHeight = 0.9f;
    int doorPhase = -1;                        // -1 沒在控制、0 等手碰到、1 手在推、2 自己盪開、3 全開
    float doorClock, contactAt, lastGrowAt, pushMax, releaseAngle, doorAngle;

    Vector3 camPos;
    Quaternion camRot = Quaternion.identity;

    // 喝茶
    Transform armHand, armPalm, masterHead, cup, cupSource;
    bool drinking;

    public bool IsPlaying => running;

    void Start()
    {
        if (gate == null) gate = FindAnyObjectByType<DojoCourtyardGate>();
        FindPlayer();
        MeasureGateLeaves();
        if (master == null) { Debug.LogWarning("[DojoOpeningCutscene] 沒有指定老師父（master），開場演出不會播放。"); return; }
        masterAnim = master.GetComponentInChildren<Animator>();
        masterColliders = master.GetComponentsInChildren<Collider>(true);
        if (masterAnim != null) masterAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        masterHead = FindDeep(master, "Head");
        // Boss 元件先掛上（演出 / 對話期間打不到他，開打才會顯示血條）
        if (startBossFight && master.GetComponent<DojoMasterBoss>() == null) master.gameObject.AddComponent<DojoMasterBoss>();
        SitDown();
        SetupTea();
    }

    void FindPlayer()
    {
        if (player != null) return;
        player = PlayerHealth.Find();
        if (player == null) return;
        controller = player.GetComponent<ThirdPersonController>();
        body = player.GetComponent<CharacterController>();
        visual = player.GetComponentInChildren<PlayerVisualAnimator>();
        foreach (var a in player.GetComponentsInChildren<Animator>())
            if (a.isHuman) { playerAnim = a; break; }
        tpc = FindAnyObjectByType<ThirdPersonCamera>();
    }

    // ---------------- 坐在坐墊上 ----------------

    void SitDown()
    {
        if (sitPoint == null) return;
        master.SetPositionAndRotation(sitPoint.position, Flat(sitPoint.rotation));
        if (masterAnim == null) return;
        masterAnim.speed = 1f;
        masterAnim.Play(SitState, 0, 0f);
        masterAnim.Update(0f);
        // 坐姿的模型會比站姿的腳底低一截：把「身體最低點」放到坐墊表面、身體中心對準坐墊
        if (!SkinnedMeshBounds.TryGet(master.gameObject, out var b, "SB_")) return;
        Vector3 shift = new Vector3(sitPoint.position.x - b.center.x, sitPoint.position.y - b.min.y, sitPoint.position.z - b.center.z);
        Vector2 flat = Vector2.ClampMagnitude(new Vector2(shift.x, shift.z), 0.5f);
        master.position += new Vector3(flat.x, Mathf.Clamp(shift.y, -1f, 1f), flat.y);
    }

    // ---------------- 喝茶：茶杯跟著右手 ----------------
    // 動畫本身是 Tripo 做的（坐著 → 右手端到嘴邊 → 喝 → 放回腿上）。茶杯只在手抬起來的時候出現在手上，
    // 手放回腿上時縮小藏起來（腿上那一段手是掌心朝下壓在腿上，拿著杯子會穿模）；桌上原本的茶杯不動。

    void SetupTea()
    {
        drinking = false;
        if (!holdTeaCup || master == null || sitPoint == null || masterAnim == null) return;
        armHand = FindDeep(master, "Right_Hand");
        armPalm = FindDeep(master, "Right_MiddleProximal");
        if (armHand == null || masterHead == null) return;
        float best = 4f;
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name != "TeaCup") continue;
            float d = Vector3.Distance(t.position, sitPoint.position);
            if (d < best) { best = d; cupSource = t; }
        }

        // 量手的位置：腿上（休息）和嘴邊（喝）的高度，以及喝的那一刻杯子在手的哪裡
        float restT = 0.1f;
        masterAnim.Play(SitState, 0, restT); masterAnim.Update(0f);
        cupRestHeight = Palm().y - master.position.y;
        masterAnim.Play(SitState, 0, sipMoment); masterAnim.Update(0f);
        cupSipHeight = Palm().y - master.position.y;
        Vector3 toMouth = masterHead.position + Vector3.up * 0.05f - Palm();
        Vector3 cupPos = Palm() + (toMouth.sqrMagnitude > 0.0001f ? toMouth.normalized : Vector3.up) * 0.04f;
        // 杯子保持原本直立的方向，杯口稍微往嘴的方向傾
        Quaternion cupRot = Quaternion.AngleAxis(-20f, Vector3.Cross(Vector3.up, Flat(master.forward).normalized)) * (cupSource != null ? cupSource.rotation : Quaternion.identity);

        if (cupSource != null)
        {
            cup = Instantiate(cupSource.gameObject).transform;
            cup.localScale = cupSource.lossyScale; // 桌上的杯子可能在有縮放的父物件底下：複製出來要保持一樣大
            foreach (var c in cup.GetComponentsInChildren<Collider>()) { c.enabled = false; Destroy(c); }
        }
        else
        {
            cup = MakeFallbackCup();
            cupRot = Quaternion.AngleAxis(-20f, Vector3.Cross(Vector3.up, Flat(master.forward).normalized));
        }
        cup.name = "TeaCup_Master";
        cup.SetPositionAndRotation(cupPos, cupRot);
        cup.SetParent(armHand, true);                 // 之後跟著手動
        cupLocalScale = cup.localScale;
        masterAnim.Play(SitState, 0, 0f); masterAnim.Update(0f);
        drinking = true;
        UpdateCupVisibility();
    }

    /// <summary>場景沒有 TeaCup 時：一個沒有把手的小陶杯（直徑約 7 公分、高 5 公分）</summary>
    Transform MakeFallbackCup()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = new Vector3(0.07f, 0.025f, 0.07f); // 圓柱預設高 2，所以 y 0.025 = 5 公分
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var m = new Material(r.sharedMaterial) { color = fallbackCupColor };
        r.sharedMaterial = m;
        return go.transform;
    }

    Vector3 Palm() => armPalm != null ? armPalm.position : armHand.position;

    float cupRestHeight, cupSipHeight;
    Vector3 cupLocalScale;

    void UpdateCupVisibility()
    {
        if (cup == null) { drinking = false; return; }
        float raise = Mathf.InverseLerp(cupRestHeight, cupSipHeight, Palm().y - master.position.y);
        float show = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.55f, raise));
        cup.localScale = cupLocalScale * Mathf.Max(0.0001f, show);
    }

    void PutCupDown()
    {
        drinking = false;
        if (cup != null) Destroy(cup.gameObject); // 起身時手上的茶杯收掉（桌上原本的茶杯還在）
        cup = null;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---------------- 觸發 ----------------

    void Update()
    {
        letterboxAmount = Mathf.MoveTowards(letterboxAmount, running && letterbox && !flying ? 1f : 0f, Time.unscaledDeltaTime * (flying ? 1f : 2.5f));
        if (running)
        {
            // 門轉完之後先關掉門的互動（不然演出中會跳出「關閉大門」、按 E 會把門關上）
            if (gate != null && gate.enabled && !gate.IsMoving) gate.enabled = false;
            if (allowSkip && !skip && !inDialogue && !GameUI.OverlayOpen
                && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                skip = true;
            if (!playerWalking && !playerArrived && !inDialogue && Time.time - playStartedAt >= playerWalkDelay) StartPlayerWalk();
            UpdatePlayerWalk();
            return;
        }
        if (master == null || gate == null || (played && playOnce)) return;
        if (gate.IsOpen && gate.IsMoving)
        {
            FindPlayer();
            if (player != null && !player.IsDead) StartCoroutine(Play());
        }
    }

    void LateUpdate()
    {
        // 門的轉動在 DojoCourtyardGate.Update 裡；這裡在它之後蓋掉，讓門跟著手
        if (doorPhase >= 0) DriveDoor(Time.deltaTime);
        if (drinking) UpdateCupVisibility();
    }

    IEnumerator Play()
    {
        running = played = true;
        playStartedAt = Time.time;
        skip = inDialogue = masterWalking = playerWalking = playerArrived = false;
        SetPlayerLocked(true);
        StartPush();
        BuildRoutes();
        CreateCamera();

        bool met = false;
        for (int i = 0; i < shots.Count && !skip; i++)
        {
            var shot = shots[i];
            if (shot == null || !shot.gameObject.activeInHierarchy) continue;
            if (shot.kind == DojoCutsceneShot.Kind.Meeting) { yield return Meeting(shot); met = true; break; }
            yield return RunShot(shot);
        }
        if (!met) yield return Meeting(Find(DojoCutsceneShot.Kind.Meeting));

        // 面對面，開始說話
        StopPlayerWalk();
        FaceEachOther();
        if (masterAnim != null)
        {
            masterAnim.speed = 1f;
            masterAnim.CrossFadeInFixedTime(TalkState, 0.35f, 0, 0f);
        }
        yield return new WaitForSeconds(0.4f);

        inDialogue = true;
        if (masterAnim != null) masterAnim.updateMode = AnimatorUpdateMode.UnscaledTime; // 對話時遊戲暫停，老師父還是會比手畫腳
        bool done = false;
        StoryDialogue.Play(BuildLines(), () => done = true);
        while (!done) yield return null;
        inDialogue = false;

        if (masterAnim != null)
        {
            masterAnim.updateMode = AnimatorUpdateMode.Normal;
            masterAnim.speed = idleTalkSpeed;
        }
        yield return FlyToPlayer();
        Finish();
        StartBossFight();
    }

    void StartBossFight()
    {
        if (!startBossFight || master == null) return;
        var boss = master.GetComponent<DojoMasterBoss>();
        if (boss == null) boss = master.gameObject.AddComponent<DojoMasterBoss>();
        boss.arenaCenter = (masterStop + playerStop) * 0.5f;
        boss.arenaRadius = bossArenaRadius;
        boss.BeginFight();
    }

    IEnumerator RunShot(DojoCutsceneShot shot)
    {
        PlaceCamera(shot, shot.kind);
        // 拍喝茶時：從端起來前一點點開始播，3 秒內剛好拍到端起來、喝、放下
        if (!shot.masterWalks && !masterWalking && shot.kind == DojoCutsceneShot.Kind.Fixed && masterAnim != null) masterAnim.Play(SitState, 0, teaShotStart);
        if (shot.masterWalks)
        {
            BeginMasterWalk();
            float start = shot.startMeters < 0f ? total + shot.startMeters : shot.startMeters;
            start = Mathf.Min(start, total - masterApproach);
            if (dist < start) { dist = start; PlaceMaster(true); }
        }
        for (float t = 0f; t < shotSeconds && !skip; t += Time.deltaTime)
        {
            AdvanceMaster(total - masterApproach); // 會面前先停在會面鏡頭的起點，等會面鏡頭再走完
            if (shot.kind == DojoCutsceneShot.Kind.Fixed && shot.dollySpeed > 0f) Dolly(shot);
            yield return null;
        }
    }

    IEnumerator Meeting(DojoCutsceneShot shot)
    {
        StartPlayerWalk();
        BeginMasterWalk();
        float fov = shot != null ? shot.fov : 40f;
        if (skip)
        {
            // 跳過：直接到定位
            FinishDoorNow();
            dist = total; PlaceMaster(true);
            TeleportPlayer(playerStop, meetForward);
            playerArrived = true;
            OverheadCamera(1f, fov, true);
            yield break;
        }

        // 老師父、玩家都放到離停下點「走 meetingSeconds 秒」的地方，再一起慢慢走近
        if (dist < total - masterApproach) { dist = total - masterApproach; PlaceMaster(true); }
        if (Flat(playerStop - PlayerFeet()).magnitude > playerApproach)
        {
            TeleportPlayer(playerStop - meetForward * playerApproach, meetForward);
            playerIndex = playerRoute.Count - 1;
        }
        OverheadCamera(0f, fov, true);

        bool masterTalking = false;
        for (float t = 0f; t < meetingSeconds + 4f; t += Time.deltaTime)
        {
            AdvanceMaster(total);
            OverheadCamera(t / meetingSeconds, fov, false);
            if (dist >= total && !masterTalking && masterAnim != null)
            {
                masterTalking = true;
                masterAnim.speed = 1f;
                masterAnim.CrossFadeInFixedTime(TalkState, 0.3f, 0, 0f);
            }
            if (dist >= total && playerArrived && t >= meetingSeconds) break;
            if (skip) { dist = total; PlaceMaster(true); TeleportPlayer(playerStop, meetForward); playerArrived = true; OverheadCamera(1f, fov, true); break; }
            yield return null;
        }
    }

    void Finish()
    {
        StopPlayerWalk();
        FinishDoorNow();
        if (vcam != null) Destroy(vcam.gameObject);
        vcam = null;
        if (flying) flying = false; // 已經飛到玩家鏡頭的位置，直接交接，不用再重設
        else if (tpc != null && player != null)
        {
            Vector3 look = Flat(master.position - player.transform.position);
            if (look.sqrMagnitude > 0.01f) tpc.yaw = Quaternion.LookRotation(look).eulerAngles.y;
            tpc.pitch = 8f;
            tpc.SnapToTarget();
        }
        if (masterColliders != null) foreach (var c in masterColliders) if (c != null) c.enabled = true;
        if (gate != null) gate.enabled = true;
        SetPlayerLocked(false);
        running = false;
    }

    void OnDisable()
    {
        if (!running) return;
        StopAllCoroutines();
        Finish();
    }

    // ---------------- 玩家 ----------------

    void SetPlayerLocked(bool locked)
    {
        if (controller == null) return;
        if (locked)
        {
            controller.LockActions();
            controller.inputEnabled = false;
            controller.StopHorizontalMotion();
        }
        else
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            if (player == null || !player.IsDead) controller.inputEnabled = true;
        }
    }

    Vector3 PlayerFeet()
    {
        if (player == null) return transform.position;
        Vector3 p = player.transform.position;
        if (body != null && body.enabled) p.y = body.bounds.min.y;
        return p;
    }

    void TeleportPlayer(Vector3 feet, Vector3 face)
    {
        if (player == null) return;
        float offset = player.transform.position.y - PlayerFeet().y;
        feet.y = GroundAt(feet, feet.y + 1.5f, feet.y);
        bool was = body != null && body.enabled;
        if (body != null) body.enabled = false;
        player.transform.position = feet + Vector3.up * offset;
        if (body != null) body.enabled = was;
        if (controller != null)
        {
            controller.StopHorizontalMotion();
            controller.FaceDirection(face);
        }
    }

    void StartPlayerWalk()
    {
        if (playerWalking || playerArrived || controller == null) return;
        playerWalking = true;
        playerIndex = 0;
        // 用小怪 AI 那套輸入讓玩家自己走（會播走路動畫、會被牆擋住）；動作鎖定還在，所以不會攻擊 / 跳
        controller.aiControlled = true;
        controller.inputEnabled = true;
    }

    void UpdatePlayerWalk()
    {
        if (!playerWalking || controller == null || player == null || playerRoute.Count == 0) return;
        Vector3 target = playerRoute[Mathf.Min(playerIndex, playerRoute.Count - 1)];
        Vector3 d = Flat(target - player.transform.position);
        float dd = d.magnitude;
        bool last = playerIndex >= playerRoute.Count - 1;
        if (!last && dd < 0.6f) { playerIndex++; return; }
        if (last && dd < 0.12f)
        {
            playerArrived = true;
            StopPlayerWalk();
            return;
        }
        float input = last ? Mathf.Min(playerWalkInput, 0.12f + dd * 0.35f) : playerWalkInput;
        controller.SetAIInput(d / Mathf.Max(dd, 0.001f) * input);
    }

    void StopPlayerWalk()
    {
        if (!playerWalking || controller == null) { playerWalking = false; return; }
        playerWalking = false;
        controller.SetAIInput(Vector3.zero);
        controller.aiControlled = false;
        controller.inputEnabled = false;
        controller.StopHorizontalMotion();
    }

    void FaceEachOther()
    {
        if (player == null) return;
        Vector3 toPlayer = Flat(PlayerFeet() - master.position);
        if (toPlayer.sqrMagnitude > 0.01f) master.rotation = Quaternion.LookRotation(toPlayer);
        if (controller != null) controller.FaceDirection(-toPlayer);
    }

    // ---------------- 推門：門跟著手 ----------------

    /// <summary>量門板的厚度：門板正面 / 背面離鉸鏈那條線多遠（大門還關著的時候量）</summary>
    void MeasureGateLeaves()
    {
        if (gate == null) return;
        float max = float.NegativeInfinity, min = float.PositiveInfinity;
        foreach (var hinge in new[] { gate.leftHinge, gate.rightHinge })
        {
            if (hinge == null) continue;
            foreach (var r in hinge.GetComponentsInChildren<Renderer>())
            {
                var lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    float z = gate.transform.InverseTransformPoint(r.transform.TransformPoint(c)).z;
                    max = Mathf.Max(max, z); min = Mathf.Min(min, z);
                }
            }
        }
        if (!float.IsInfinity(max)) { leafFront = Mathf.Clamp(max, 0f, 1.5f); leafBack = Mathf.Clamp(-min, 0f, 1.5f); }
    }

    void StartPush()
    {
        outward = GateOutward();
        float side = gate != null ? gate.transform.InverseTransformPoint(player.transform.position).z : -1f;
        openingSign = side >= 0f ? -1f : 1f;          // 跟 DojoCourtyardGate 一樣：往遠離玩家的那一側開
        faceDistance = side >= 0f ? leafFront : leafBack;

        if (playerAnim != null)
        {
            var hips = playerAnim.GetBoneTransform(HumanBodyBones.Hips);
            if (hips != null) hipsHeight = Mathf.Clamp(hips.position.y - PlayerFeet().y, 0.3f, 2f);
        }
        bool hasPush = playerAnim != null && playerAnim.HasState(0, PushState);
        if (gate != null && snapPlayerToDoor && hasPush)
        {
            // 站到門縫正前方：推門動畫裡手剛碰到門的那一刻，手尖剛好在門板表面
            Vector3 g = gate.transform.position;
            Vector3 spot = g + outward * (faceDistance + contactReach * hipsHeight);
            spot.y = PlayerFeet().y;
            TeleportPlayer(spot, -outward);
        }
        else if (controller != null) controller.FaceDirection(-outward);
        if (visual != null) visual.PlayGesture("PushGate", 5f);

        pushMax = 0f; doorClock = 0f; doorAngle = 0f;
        doorPhase = gate != null ? (hasPush ? 0 : 2) : -1;
        releaseAngle = 0f; contactAt = lastGrowAt = 0f;
    }

    void DriveDoor(float dt)
    {
        if (gate == null) { doorPhase = -1; return; }
        doorClock += dt;
        switch (doorPhase)
        {
            case 0: // 等手碰到門
                if (HandPenetration(out _) >= 0f) { doorPhase = 1; contactAt = lastGrowAt = doorClock; }
                else if (doorClock > 1.6f) { doorPhase = 2; doorClock = 0f; releaseAngle = 0f; } // 一直沒碰到：直接盪開
                break;
            case 1: // 手往前推多少，門就轉多少（門板剛好貼著手）
            {
                float h = HandPenetration(out float radius);
                if (h > pushMax + 0.002f) { pushMax = h; lastGrowAt = doorClock; }
                doorAngle = Mathf.Min(gate.openDegrees, doorFollowGain * Mathf.Asin(Mathf.Clamp01(pushMax / Mathf.Max(0.5f, radius))) * Mathf.Rad2Deg);
                if ((doorClock - lastGrowAt > 0.15f && pushMax > 0.01f) || doorClock - contactAt > 1.8f)
                {
                    doorPhase = 2; releaseAngle = doorAngle; doorClock = 0f;
                    // 手推到底：不播動畫後面一直按著門的那段，直接把手收回待機
                    if (playerAnim != null && playerAnim.HasState(0, IdleState)) playerAnim.CrossFadeInFixedTime(IdleState, pushReleaseBlend, 0, 0f);
                }
                break;
            }
            case 2: // 手推完：門自己盪到全開
                doorAngle = Mathf.Lerp(releaseAngle, gate.openDegrees, Mathf.SmoothStep(0f, 1f, doorClock / doorCoastSeconds));
                if (doorClock >= doorCoastSeconds) { doorPhase = 3; doorAngle = gate.openDegrees; }
                break;
            case 3: // 全開：門自己的轉動還沒跑完就一直蓋掉（停在全開）
                doorAngle = gate.openDegrees;
                if (!gate.IsMoving) { ApplyDoor(doorAngle); doorPhase = -1; return; }
                break;
        }
        ApplyDoor(doorAngle);
    }

    void FinishDoorNow()
    {
        if (doorPhase < 0 || gate == null) return;
        doorAngle = gate.openDegrees;
        ApplyDoor(doorAngle);
        doorPhase = gate.IsMoving ? 3 : -1;
    }

    void ApplyDoor(float angle)
    {
        float v = angle * openingSign;
        if (gate.leftHinge) gate.leftHinge.localRotation = Quaternion.Euler(0f, -v, 0f);
        if (gate.rightHinge) gate.rightHinge.localRotation = Quaternion.Euler(0f, v, 0f);
    }

    /// <summary>手尖超過門板表面多少公尺（負的 = 還沒碰到）；radius = 手離門鉸鏈多遠</summary>
    float HandPenetration(out float radius)
    {
        radius = gate.halfOpening;
        if (playerAnim == null) return -1f;
        Vector3 g = gate.transform.position;
        Vector3 surface = g + outward * faceDistance;
        Vector3 right = Flat(gate.transform.right).normalized;
        float best = float.NegativeInfinity;
        foreach (var (handBone, armBone) in new[] { (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm), (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm) })
        {
            var hand = playerAnim.GetBoneTransform(handBone);
            if (hand == null) continue;
            var arm = playerAnim.GetBoneTransform(armBone);
            Vector3 tip = hand.position;
            if (arm != null) tip += (hand.position - arm.position).normalized * (0.12f * hipsHeight); // 手腕 → 手掌前端
            float h = Vector3.Dot(tip - surface, -outward);
            if (h > best)
            {
                best = h;
                radius = gate.halfOpening - Mathf.Abs(Vector3.Dot(tip - g, right));
            }
        }
        return float.IsNegativeInfinity(best) ? -1f : best;
    }

    /// <summary>從大門指向玩家那一側的水平方向</summary>
    Vector3 GateOutward()
    {
        if (gate == null || player == null) return Vector3.back;
        Vector3 f = Flat(gate.transform.forward).normalized;
        if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
        return gate.transform.InverseTransformPoint(player.transform.position).z >= 0f ? f : -f;
    }

    // ---------------- 路線 ----------------

    void BuildRoutes()
    {
        Vector3 inward = -outward;
        Vector3 m;
        if (meetPoint != null) { m = meetPoint.position; meetForward = Flat(meetPoint.forward).normalized; }
        else { m = (gate != null ? gate.transform.position : transform.position) + inward * 30f; meetForward = inward; }
        if (meetForward.sqrMagnitude < 0.01f) meetForward = inward;
        masterStop = m + meetForward * (meetingGap * 0.5f);
        playerStop = m - meetForward * (meetingGap * 0.5f);

        pts.Clear(); cum.Clear();
        foreach (var p in path) if (p != null) pts.Add(p.position);
        if (pts.Count == 0) pts.Add(master.position);
        pts.Add(masterStop);
        cum.Add(0f);
        for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + Vector3.Distance(Flat(pts[i - 1]), Flat(pts[i])));
        total = cum[cum.Count - 1];
        dist = 0f;
        masterApproach = Mathf.Min(total, walkSpeed * walkAnimSpeed * meetingSeconds);
        float playerSpeed = playerWalkInput * (controller != null ? controller.walkSpeed : 4.5f);
        playerApproach = playerSpeed * meetingSeconds * 0.85f; // 最後一段會放慢腳步

        playerRoute.Clear();
        if (gate != null) playerRoute.Add(gate.transform.position + inward * 1.5f); // 先穿過門洞正中間
        playerRoute.Add(playerStop);
        playerIndex = 0;
    }

    // ---------------- 老師父走路 ----------------

    Vector3 PointAt(float d, out Vector3 dir)
    {
        dir = master.forward;
        if (pts.Count == 1) return pts[0];
        int i = 0;
        while (i < pts.Count - 2 && d > cum[i + 1]) i++;
        float len = Mathf.Max(0.0001f, cum[i + 1] - cum[i]);
        dir = Flat(pts[i + 1] - pts[i]);
        return Vector3.Lerp(pts[i], pts[i + 1], Mathf.Clamp01((d - cum[i]) / len));
    }

    void BeginMasterWalk()
    {
        if (masterWalking) return;
        masterWalking = true;
        PutCupDown();
        // 起身的過程由鏡頭切換帶過：下一個鏡頭裡他已經站著在走了
        if (masterColliders != null) foreach (var c in masterColliders) if (c != null) c.enabled = false;
        if (masterAnim != null)
        {
            masterAnim.speed = walkAnimSpeed;
            masterAnim.Play(WalkState, 0, 0f);
        }
    }

    void AdvanceMaster(float limit)
    {
        if (!masterWalking || dist >= limit) return;
        dist = Mathf.Min(limit, dist + walkSpeed * walkAnimSpeed * Time.deltaTime);
        PlaceMaster(false);
    }

    void PlaceMaster(bool snap)
    {
        Vector3 p = PointAt(dist, out Vector3 dir);
        float ground = GroundAt(p, (snap ? p.y : master.position.y) + 1.2f, p.y);
        // 踏腳石、台階：高度用平滑的方式追上，不會一下子彈高
        if (snap) { p.y = ground; masterYVelocity = 0f; }
        else p.y = Mathf.SmoothDamp(master.position.y, ground, ref masterYVelocity, 0.22f, 2f, Time.deltaTime);
        master.position = p;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var want = Quaternion.LookRotation(dir);
            master.rotation = snap ? want : Quaternion.RotateTowards(master.rotation, want, turnDegreesPerSecond * Time.deltaTime);
        }
    }

    float GroundAt(Vector3 p, float fromY, float fallback)
    {
        float best = float.NegativeInfinity;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, 3.5f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsPlayerOrMaster(h.collider)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    bool IsPlayerOrMaster(Collider c)
    {
        if (c == null) return true;
        if (player != null && c.transform.IsChildOf(player.transform)) return true;
        return master != null && c.transform.IsChildOf(master);
    }

    // ---------------- 鏡頭 ----------------

    void CreateCamera()
    {
        var go = new GameObject("CM DojoOpeningCutscene");
        vcam = go.AddComponent<CinemachineCamera>();
        if (tpc != null && tpc.cinemachineCamera != null) vcam.Lens = tpc.cinemachineCamera.Lens;
        vcam.Priority = 1000; // 蓋過玩家的鏡頭；刪掉之後自動回到玩家鏡頭（切換方式是直接切）
        vcam.Prioritize();
    }

    DojoCutsceneShot Find(DojoCutsceneShot.Kind kind)
    {
        foreach (var s in shots) if (s != null && s.kind == kind) return s;
        return null;
    }

    void PlaceCamera(DojoCutsceneShot shot, DojoCutsceneShot.Kind kind)
    {
        float fov = shot != null ? shot.fov : 36f;
        Vector3 pos, look;
        Vector3 up = Vector3.up;
        switch (kind)
        {
            case DojoCutsceneShot.Kind.PlayerCloseUp:
            {
                // 玩家側面、稍微在玩家身後：看得到手推門和門板打開
                Vector3 fwd = -outward;
                Vector3 chest = PlayerFeet() + up * (hipsHeight * 1.4f);
                Vector3 right = Vector3.Cross(up, fwd);
                look = chest + fwd * 1.2f;
                pos = BestOf(look, chest + right * 2.4f + outward * 0.9f + up * 0.2f,
                                   chest - right * 2.4f + outward * 0.9f + up * 0.2f);
                break;
            }
            case DojoCutsceneShot.Kind.PlayerWalkIn:
            {
                // 門內往回看門洞：玩家從門外走進來
                Vector3 g = gate != null ? gate.transform.position : PlayerFeet();
                Vector3 inward = -outward, right = Vector3.Cross(up, inward);
                look = g + inward * 0.3f + up * 1.1f;
                pos = BestOf(look, g + inward * 6f + right * 2.8f + up * 1.45f, g + inward * 6f - right * 2.8f + up * 1.45f);
                break;
            }
            case DojoCutsceneShot.Kind.Meeting:
                OverheadCamera(0f, fov, true);
                return;
            default:
                if (shot == null) return;
                Quaternion fixedRot = shot.transform.rotation;
                if (shot.lookAtMasterFace && FacePoint(shot, out var face))
                    fixedRot = Quaternion.LookRotation(face - shot.transform.position);
                SetCamera(shot.transform.position, fixedRot, fov);
                return;
        }
        Vector3 d = look - pos;
        SetCamera(pos, d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity, fov);
    }

    /// <summary>固定鏡頭慢慢往老師父推近（方向不變）</summary>
    void Dolly(DojoCutsceneShot shot)
    {
        if (vcam == null || master == null) return;
        Vector3 aim = shot.lookAtMasterFace && FacePoint(shot, out var face) ? face : master.position + Vector3.up * 1.2f;
        Vector3 to = aim - camPos;
        float d = to.magnitude;
        if (d <= shot.dollyMinDistance) return;
        camPos += to / d * Mathf.Min(d - shot.dollyMinDistance, shot.dollySpeed * Time.deltaTime);
        vcam.transform.position = camPos;
    }

    /// <summary>老師父臉的位置（頭骨稍微往前、往上）</summary>
    bool FacePoint(DojoCutsceneShot shot, out Vector3 face)
    {
        face = default;
        if (masterHead == null && master != null) masterHead = FindDeep(master, "Head");
        if (masterHead == null) return false;
        face = masterHead.position + Vector3.up * (0.08f + shot.faceAimOffset) + Flat(master.forward).normalized * 0.06f;
        return true;
    }

    /// <summary>
    /// 對話結束：鏡頭從高空順順地飛到玩家背後，剛好接上平常的第三人稱鏡頭（飛到之後才把控制還給玩家）。
    /// 終點每一幀都跟著玩家鏡頭的實際位置（含 Cinemachine 的避障），所以接上去的那一刻不會跳。
    /// </summary>
    IEnumerator FlyToPlayer()
    {
        if (vcam == null || tpc == null || tpc.cinemachineCamera == null || player == null) yield break;
        Vector3 look = Flat(master.position - player.transform.position);
        if (look.sqrMagnitude > 0.01f) tpc.yaw = Quaternion.LookRotation(look).eulerAngles.y;
        tpc.pitch = handOffPitch;
        tpc.SnapToTarget();
        var playerCam = tpc.cinemachineCamera;
        var oldStandby = playerCam.StandbyUpdate;
        playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always; // 還沒輪到它時也每幀算位置
        flying = true;
        Vector3 fromPos = camPos;
        Quaternion fromRot = camRot;
        float fromFov = vcam.Lens.FieldOfView, toFov = playerCam.Lens.FieldOfView;
        yield return null; // 等玩家鏡頭算出第一個位置
        for (float t = 0f; t < flySeconds; t += Time.deltaTime)
        {
            var state = playerCam.State;
            Vector3 toPos = state.GetFinalPosition();
            Quaternion toRot = state.GetFinalOrientation();
            float u = Mathf.Clamp01(t / flySeconds);
            float e = u * u * u * (u * (u * 6f - 15f) + 10f); // 慢慢起飛、慢慢降落
            // 位置走一條往上拱的弧線（不會貼著地面掃過去）
            Vector3 pos = Vector3.Lerp(fromPos, toPos, e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * flyArc);
            Quaternion rot = Quaternion.Slerp(fromRot, toRot, e);
            camPos = pos; camRot = rot;
            vcam.transform.SetPositionAndRotation(pos, rot);
            var lens = vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(fromFov, toFov, e);
            vcam.Lens = lens;
            yield return null;
        }
        playerCam.StandbyUpdate = oldStandby;
    }

    /// <summary>高空俯視兩人：progress 0 = 剛開始（高）、1 = 兩人停下（低一點、斜一點）</summary>
    void OverheadCamera(float progress, float fov, bool cut)
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress));
        Vector3 center = (masterStop + playerStop) * 0.5f;
        // 一開始兩人離得比較遠：畫面中心放在兩人起點的中間，再慢慢移回會面點
        Vector3 startCenter = (masterStop + meetForward * masterApproach + playerStop - meetForward * playerApproach) * 0.5f;
        center = Vector3.Lerp(startCenter, center, k);
        Vector3 right = Vector3.Cross(Vector3.up, meetForward);
        float h = Mathf.Lerp(meetHeightStart, meetHeightEnd, k);
        // 從側邊稍微斜著往下看：兩人的路線在畫面裡是橫的
        Vector3 pos = center + Vector3.up * h - right * (h * Mathf.Lerp(0.25f, 0.55f, k));
        Vector3 look = center + Vector3.up * 0.8f;
        if (cut) pos = Vector3.Lerp(look, pos, Clearance(look, pos));
        Quaternion rot = Quaternion.LookRotation(look - pos);
        if (cut) SetCamera(pos, rot, fov);
        else if (vcam != null) { camPos = pos; camRot = rot; vcam.transform.SetPositionAndRotation(pos, rot); }
    }

    /// <summary>兩個候選位置選沒被擋住的那個；都被擋住就拉近到擋住的地方前面</summary>
    Vector3 BestOf(Vector3 look, Vector3 a, Vector3 b)
    {
        float ca = Clearance(look, a), cb = Clearance(look, b);
        if (ca >= 0.999f) return a;
        if (cb >= 0.999f) return b;
        return ca >= cb ? Vector3.Lerp(look, a, ca) : Vector3.Lerp(look, b, cb);
    }

    float Clearance(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return 1f;
        float nearest = len;
        foreach (var h in Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore))
            if (!IsPlayerOrMaster(h.collider) && !IsGateLeaf(h.collider) && h.distance < nearest) nearest = h.distance;
        return nearest >= len ? 1f : Mathf.Clamp01((nearest - 0.3f) / len);
    }

    bool IsGateLeaf(Collider c) =>
        gate != null && ((gate.leftHinge && c.transform.IsChildOf(gate.leftHinge)) || (gate.rightHinge && c.transform.IsChildOf(gate.rightHinge)));

    void SetCamera(Vector3 pos, Quaternion rot, float fov)
    {
        camPos = pos; camRot = rot;
        if (vcam == null) return;
        vcam.transform.SetPositionAndRotation(pos, rot);
        var lens = vcam.Lens;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
        vcam.PreviousStateIsValid = false; // 直接切過去，不做平滑
    }

    // ---------------- 對話 ----------------

    List<StoryDialogue.Line> BuildLines()
    {
        var list = new List<StoryDialogue.Line>();
        if (lines == null) return list;
        foreach (var l in lines)
        {
            if (l == null || string.IsNullOrWhiteSpace(l.text)) continue;
            list.Add(l.playerSpeaks
                ? StoryDialogue.L(playerName, player != null ? player.transform : null, false, l.text, PlanetStory.PlayerColor)
                : StoryDialogue.L(masterName, master, true, l.text, masterColor));
        }
        return list;
    }

    // ---------------- 畫面 ----------------

    static Texture2D black;

    void OnGUI()
    {
        if (letterboxAmount <= 0f || GameUI.OverlayOpen) return;
        GUI.depth = -1000;
        if (black == null) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
        float h = Screen.height * 0.085f * Mathf.SmoothStep(0f, 1f, letterboxAmount);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, h), black);
        GUI.DrawTexture(new Rect(0f, Screen.height - h, Screen.width, h), black);

        if (running && allowSkip && !skip && !inDialogue && !flying)
        {
            const string action = "跳過";
            var style = GameUI.SmallRight;
            float w = style.CalcSize(new GUIContent(action)).x;
            float x = Screen.width - 32f - w, y = Screen.height - h * 0.5f - 12f;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(x, y, w, 24f), action, style);
            GameUI.Key(new Rect(x - 64f, y, 58f, 24f), "Space", 0.75f);
            GUI.color = Color.white;
        }
    }

    // ---------------- 編輯器裡看路線 ----------------

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    static Quaternion Flat(Quaternion q) { var f = Flat(q * Vector3.forward); return f.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(f) : Quaternion.identity; }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Transform prev = null;
        foreach (var p in path)
        {
            if (p == null) continue;
            Gizmos.DrawSphere(p.position + Vector3.up * 0.1f, 0.18f);
            if (prev != null) Gizmos.DrawLine(prev.position + Vector3.up * 0.1f, p.position + Vector3.up * 0.1f);
            prev = p;
        }
        if (meetPoint != null)
        {
            Vector3 f = Flat(meetPoint.forward).normalized * (meetingGap * 0.5f);
            Vector3 m = meetPoint.position + Vector3.up * 0.1f;
            if (prev != null) Gizmos.DrawLine(prev.position + Vector3.up * 0.1f, m + f);
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(m + f, 0.4f); // 老師父停的地方
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
            Gizmos.DrawWireSphere(m - f, 0.4f); // 玩家停的地方
            Gizmos.DrawLine(m - f, m + f);
        }
        if (sitPoint != null)
        {
            Gizmos.color = new Color(0.5f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireSphere(sitPoint.position, 0.3f);
            Gizmos.DrawLine(sitPoint.position, sitPoint.position + sitPoint.forward * 0.8f);
        }
#if UNITY_EDITOR
        if (meetPoint != null)
        {
            Vector3 f = Flat(meetPoint.forward).normalized * (meetingGap * 0.5f);
            UnityEditor.Handles.Label(meetPoint.position + f + Vector3.up * 0.8f, "老師父停這裡");
            UnityEditor.Handles.Label(meetPoint.position - f + Vector3.up * 0.8f, "玩家停這裡");
        }
#endif
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // 選到演出物件時：在路線上標出每個老師父走路鏡頭開始時他在的位置
        var list = new List<Vector3>();
        foreach (var p in path) if (p != null) list.Add(p.position);
        if (meetPoint != null) list.Add(meetPoint.position + Flat(meetPoint.forward).normalized * (meetingGap * 0.5f));
        if (list.Count < 2) return;
        var c = new List<float> { 0f };
        for (int i = 1; i < list.Count; i++) c.Add(c[i - 1] + Vector3.Distance(Flat(list[i - 1]), Flat(list[i])));
        Vector3 At(float d)
        {
            int i = 0;
            while (i < list.Count - 2 && d > c[i + 1]) i++;
            return Vector3.Lerp(list[i], list[i + 1], Mathf.Clamp01((d - c[i]) / Mathf.Max(0.0001f, c[i + 1] - c[i])));
        }
        foreach (var s in shots)
        {
            if (s == null || !s.masterWalks || s.kind == DojoCutsceneShot.Kind.Meeting) continue;
            float d = s.startMeters < 0f ? c[c.Count - 1] + s.startMeters : s.startMeters;
            Gizmos.color = Color.green;
            Vector3 a = At(d);
            Gizmos.DrawCube(a + Vector3.up * 0.3f, Vector3.one * 0.25f);
            UnityEditor.Handles.Label(a + Vector3.up * 0.7f, s.name);
        }
    }
#endif
}
