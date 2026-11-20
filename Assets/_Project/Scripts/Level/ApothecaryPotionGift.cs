using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 倒下之後（VillageReturn 設 Pending）去找藥師：說完話，藥師叫住玩家，給一瓶壯力藥（喝了攻擊力暫時提高）。
///   1. 藥師走回桌前（磨藥的板凳旁），右手拿起桌上的藥缽，抬到葫蘆藥瓶上面斜斜地倒（一道暗紅的藥汁流進瓶口）
///   2. 放回藥缽、拿起藥瓶，走到玩家面前、伸手遞過去（左手受傷不能動，全部用右手）
///   3. 玩家接過來仰頭喝完（PotionDrink），身上冒出淡淡的紅黑火焰（AttackPotionBuff）
/// 過程中玩家不能動，藥師的日常暫停（NpcRoutine.Scripted），做完走回原本那一站。一次倒下送一瓶。
/// 整段是過場演出（跟道場開場演出同一種拍法）：上下黑邊、好幾個固定鏡頭依劇情切換（每個鏡頭慢慢往人推近、跟著人轉），
/// 叫住玩家 → 遠景藥師走回板凳坐下 → 桌邊特寫倒藥、拿起藥瓶 → 側面拍藥師走過來 → 越過玩家肩膀拍遞藥 →
/// 玩家喝藥的特寫 → 全身火焰冒出來 → 鏡頭飛回玩家背後交還操作。對話以外按 Space 可以跳過（直接喝完、加上藥效）。
/// </summary>
[DefaultExecutionOrder(200)] // NpcRoutine 擺完姿勢之後才擺拿東西的右手
public class ApothecaryPotionGift : MonoBehaviour
{
    /// <summary>倒下過、還沒拿這次的藥</summary>
    public static bool Pending;

    [Tooltip("桌上的藥缽（倒藥用）")]
    public Transform mortar;
    [Tooltip("藥缽的大小（m）：支點在缽底中心（PlaceKitPiece 擺的），網格的外框不準，用這個算缽口")]
    public Vector3 mortarSize = new Vector3(0.24f, 0.13f, 0.24f);
    [Tooltip("桌上的藥瓶（裝藥、遞給玩家）；支點在瓶底中心")]
    public Transform gourd;
    [Tooltip("藥瓶多高（m）")]
    public float bottleHeight = 0.24f;
    [Tooltip("攻擊力提高多少（0.3 = 原本的 1.3 倍）")]
    public float bonus = 0.3f;
    [Tooltip("藥效幾秒")]
    public float duration = 120f;

    [Header("過場鏡頭")]
    [Tooltip("對話以外按 Space 跳過")]
    public bool allowSkip = true;
    [Tooltip("最後鏡頭飛回玩家背後花幾秒")]
    public float flySeconds = 1.6f;

    NpcRoutine routine;
    MerchantNPC npc;
    Transform playerTf;
    bool buffGiven;
    Animator anim;
    Transform rArm, rFore, rHand, rMiddle, rThumb, chest;
    bool wasTalking, running;
    ThirdPersonController lockedCtrl;

    // 右手：要伸到哪裡（世界座標）、伸多少（0 ~ 1）、手腕再轉多少（倒藥）
    Vector3 handTarget;
    float armW, tilt;
    Vector3 tiltAxis = Vector3.forward;
    // 拿在手上的東西：跟著手掌（拿起來那一刻的相對位置）
    Transform held;
    Vector3 heldPos;
    Quaternion heldRot;
    bool grabNow;
    // 東西原本放的地方（做完放回去）
    Vector3 mortarHome, gourdHome;
    Quaternion mortarHomeRot, gourdHomeRot;
    Transform mortarParent, gourdParent;
    LineRenderer stream;

    static readonly Color Medicine = new Color(0.42f, 0.06f, 0.05f, 0.95f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Pending = false;
        VitalsHud.Hidden = false;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    /// <summary>
    /// 場景還沒用新版 Builder 重建（藥師身上沒有這個元件）：載入村子時自動掛上，照名字找桌上的藥缽和葫蘆藥瓶。
    /// </summary>
    static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        foreach (var npc in FindObjectsByType<MerchantNPC>(FindObjectsSortMode.None))
        {
            if (npc.speakerName != "藥師" || npc.GetComponent<ApothecaryPotionGift>() != null) continue;
            var m = GameObject.Find("Apothecary_Mortar");
            var g = GameObject.Find("Apothecary_PotionBottle") ?? GameObject.Find("Apothecary_Gourd");
            if (m == null || g == null) { Debug.LogWarning("[ApothecaryPotionGift] 找不到藥缽 / 葫蘆藥瓶，藥師不會送藥（重建村子的場景）"); continue; }
            if (m.isStatic || g.isStatic) Debug.LogWarning("[ApothecaryPotionGift] 藥缽 / 藥瓶是 Static，拿起來可能看不到在動：請用 JapaneseVillageBuilder.Build 重建村子");
            var gift = npc.gameObject.AddComponent<ApothecaryPotionGift>();
            gift.mortar = m.transform;
            gift.gourd = g.transform;
        }
    }

    void Start()
    {
        routine = GetComponent<NpcRoutine>();
        npc = GetComponent<MerchantNPC>();
        anim = routine != null ? routine.animator : GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rFore = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            rMiddle = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            rThumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            chest = anim.GetBoneTransform(HumanBodyBones.Chest) ?? anim.GetBoneTransform(HumanBodyBones.Spine);
        }
        if (mortar != null) { mortarHome = mortar.position; mortarHomeRot = mortar.rotation; mortarParent = mortar.parent; }
        if (gourd != null) { gourdHome = gourd.position; gourdHomeRot = gourd.rotation; gourdParent = gourd.parent; }
    }

    void Update()
    {
        letterboxAmount = Mathf.MoveTowards(letterboxAmount, cine && !flying ? 1f : 0f, Time.unscaledDeltaTime * (flying ? 1.2f : 2.5f));
        if (running && cine && allowSkip && !flying && !StoryDialogue.Active
            && Time.unscaledTime - cineStart > 0.5f && Time.unscaledTime - dialogueSeen > 0.3f // 關掉對話的那一下 Space 不算跳過
            && Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            SkipNow();
            return;
        }
        if (running || npc == null || routine == null) return;
        bool talking = npc.InConversation;
        // 說完話（對話框關掉）：還沒送過這次的藥就叫住玩家
        if (wasTalking && !talking && Pending && !routine.AtDoor && !routine.Hidden && !StoryDialogue.Active && mortar != null && gourd != null && rHand != null)
            StartCoroutine(Run());
        wasTalking = talking;
    }

    void OnDisable()
    {
        PlayerLoadout.Unholster(this);
        UnlockPlayer();
        if (cine) EndCamera();
        if (routine != null && running) routine.ResumeRoutine();
    }

    // ---------------------------------------------------------------- 流程

    IEnumerator Run()
    {
        running = true;
        Pending = false;
        var hp = PlayerHealth.Find();
        if (hp == null) { running = false; yield break; }
        var player = hp.transform;
        playerTf = player;
        buffGiven = false;
        LockPlayer(player.GetComponent<ThirdPersonController>());
        PlayerLoadout.Holster(this); // 武器先收起來，喝完再拔出來
        routine.Scripted = true;
        npc.enabled = false; // 送藥的時候不能再跟他說話
        float s = transform.lossyScale.y;

        // 鏡頭 1：越過玩家的肩膀拍藥師叫住玩家（玩家轉過來面向他）
        BeginCamera();
        FacePlayerTo(transform.position);
        Vector3 toApo = Flat(transform.position - player.position).normalized;
        OverShoulder(player, toApo, 1.4f, 1.75f, () => Face(transform, 1.5f), 36f, 0.08f, 1.6f);
        yield return Say("藥師", transform, true, "等等，先別走。我把剛磨好的藥倒一瓶給你。", GameUI.Accent);

        // 1. 走回磨藥的板凳、坐下（桌子很矮，站著手搆不到）
        var seat = routine.stops[0];
        Vector3 toTable = Flat(mortar.position - seat.position);
        if (toTable.sqrMagnitude < 1e-4f) toTable = Quaternion.Euler(0f, seat.yaw, 0f) * Vector3.forward;
        toTable.Normalize();
        // 鏡頭 2：桌子對面的遠景，藥師走回板凳坐下（鏡頭跟著他轉、慢慢推近）
        Vector3 across = Vector3.Cross(Vector3.up, toTable);
        Shot(mortar.position + toTable * 2.6f + across * 1.3f + Vector3.up * 1.45f, () => transform.position + Vector3.up * 0.75f * s, 40f, 0.18f, 2f);
        yield return WalkTo(seat.position, toTable);
        if (seat.seatY > 0f)
        {
            routine.ScriptedSeatY = seat.seatY;
            yield return new WaitForSeconds(0.6f);
        }

        // 2. 右手拿起藥缽，抬到藥瓶上面倒
        Vector3 mCenter = mortar.position + Vector3.up * mortarSize.y * 0.5f;
        // 鏡頭 3：桌邊低角度的特寫：藥缽、藥瓶在畫面中間，看得到藥汁流進瓶口
        {
            Vector3 mid = Vector3.Lerp(mCenter, gourd.position + Vector3.up * bottleHeight, 0.5f) + Vector3.up * 0.12f;
            Shot(mid + toTable * 1.05f + across * 0.45f + Vector3.up * 0.28f, () => mid, 36f, 0.04f, 0.8f);
        }
        Vector3 grip = mCenter + Flat(transform.position - mCenter).normalized * mortarSize.x * 0.45f + Vector3.up * mortarSize.y * 0.15f;
        yield return MoveHand(grip, 0.5f, 0f, 1f);
        Grab(mortar);
        yield return null;
        Vector3 gourdMouth = gourd.position + Vector3.up * bottleHeight;
        Vector3 toGourd = Flat(gourdMouth - mCenter).normalized;
        tiltAxis = Vector3.Cross(Vector3.up, toGourd); // 往藥瓶那邊斜
        Vector3 above = gourdMouth + Vector3.up * (0.1f + mortarSize.y) - toGourd * mortarSize.x * 0.45f;
        yield return MoveHand(above, 0.7f, 1f, 1f); // 拿著藥缽：手一直伸著，不要掉回動畫的姿勢
        yield return Tilt(0f, 70f, 0.45f);
        GameAudio.Play("SFX/Gen/drink_pour", gourdMouth, 0.5f, 0.85f, 0.05f, 1f, 16f);
        float pour = 0f;
        while (pour < 1.3f)
        {
            pour += Time.deltaTime;
            DrawStream(gourdMouth, toGourd, pour);
            yield return null;
        }
        if (stream != null) stream.enabled = false;
        yield return Tilt(70f, 0f, 0.4f);
        yield return MoveHand(grip, 0.6f, 1f, 1f);
        Release(mortar, mortarParent, mortarHome, mortarHomeRot);

        // 3. 拿起藥瓶、站起來，走到玩家面前遞過去
        Vector3 gGrip = gourd.position + Vector3.up * bottleHeight * 0.42f + Flat(transform.position - gourd.position).normalized * 0.03f;
        yield return MoveHand(gGrip, 0.45f, 1f, 1f);
        Grab(gourd);
        yield return null;
        if (routine.ScriptedSeatY > 0f)
        {
            routine.ScriptedSeatY = 0f;
            yield return new WaitForSeconds(0.55f);
        }
        Vector3 toPlayer = Flat(player.position - transform.position);
        // 鏡頭 4：側面拍藥師拿著藥瓶走向玩家（兩個人都在畫面裡）
        {
            Vector3 a = transform.position, b = player.position;
            Vector3 perp = Vector3.Cross(Vector3.up, Flat(b - a).normalized);
            Vector3 c = (a + b) * 0.5f;
            Vector3 camA = c + perp * 3.8f + Vector3.up * 1.5f, camB = c - perp * 3.8f + Vector3.up * 1.5f;
            Vector3 look0 = c + Vector3.up * 1f;
            Shot(Clearance(look0, camA) >= Clearance(look0, camB) ? camA : camB, () => Vector3.Lerp(transform.position, player.position, 0.35f) + Vector3.up * 1f * s, 38f, 0.1f, 2.5f);
        }
        Vector3 meet = player.position - toPlayer.normalized * 1.05f * s;
        meet.y = GroundAt(meet, transform.position.y);
        // 走過去的時候藥瓶拿在胸前
        carrying = true;
        yield return WalkTo(meet, Flat(player.position - meet));
        carrying = false;
        // 玩家轉過來面向藥師
        Vector3 back = Flat(transform.position - player.position);
        if (back.sqrMagnitude > 1e-4f) player.rotation = Quaternion.LookRotation(back);
        // 鏡頭 5：越過玩家的肩膀拍藥師把藥瓶遞過來
        toApo = Flat(transform.position - player.position).normalized;
        OverShoulder(player, toApo, 1.1f, 1.7f, () => Vector3.Lerp(Face(transform, 1.35f), Face(player, 1.35f), 0.5f) - Vector3.up * 0.25f, 36f, 0.06f, 1.8f, preferSide: true);
        Vector3 offer = (chest != null ? chest.position : transform.position + Vector3.up * 1.2f * s) + transform.forward * 0.45f * s - Vector3.up * 0.05f;
        yield return MoveHand(offer, 0.5f, 1f, 1f);
        yield return Say("藥師", transform, true, "喝了吧。藥效撐不了太久，趁身子還熱著，去把場子討回來。", GameUI.Accent);

        // 4. 交給玩家：玩家接過去喝（PotionDrink），藥師的手放下、走回去
        // 鏡頭 6：玩家喝藥的特寫（斜前方、頭肩）
        {
            Vector3 toA = Flat(transform.position - player.position).normalized;
            PickAround(player.position, 1.5f, 1.55f, (toA * 0.55f + Vector3.Cross(Vector3.up, toA) * 0.85f).normalized, false, transform, () => Face(player, 1.45f), 32f, 0.05f, 1f);
        }
        var drink = player.gameObject.AddComponent<PotionDrink>();
        drink.bottle = gourd;
        drink.upright = gourdHomeRot;
        drink.bottleHeight = bottleHeight;
        held = null;
        float f = 0f;
        while (f < 1f) { f += Time.deltaTime / 0.4f; armW = 1f - f; yield return null; }
        armW = 0f;
        routine.ResumeRoutine();
        npc.enabled = true;

        while (drink != null && !drink.Done) yield return null;
        if (drink != null) Destroy(drink);
        // 鏡頭 7：全身，火焰冒出來
        {
            Vector3 toA = Flat(transform.position - player.position).normalized;
            PickAround(player.position, 2.9f, 1.25f, (toA * 0.4f - Vector3.Cross(Vector3.up, toA) * 0.9f).normalized, false, transform, () => player.position + Vector3.up * 0.95f, 40f, 0.12f, 2.2f);
        }
        AttackPotionBuff.Begin(bonus, duration);
        buffGiven = true;
        PlayerLoadout.Unholster(this);
        GameAudio.Play("SFX/dragon_roar", player.position, 0.18f, 1.6f, 0.05f, 1f, 12f); // 很小聲、很高的一聲，像火燒起來
        yield return new WaitForSeconds(1.1f);
        yield return Say("米格爾", player, false, "……身體裡像有火在燒。", PlanetStory.PlayerColor);
        // 鏡頭飛回玩家背後，交還操作
        yield return FlyToPlayer();
        EndCamera();
        UnlockPlayer();

        // 藥瓶放回桌上（玩家喝完收起來了，鋪子裡還有別的瓶子）
        yield return new WaitForSeconds(2f);
        Release(gourd, gourdParent, gourdHome, gourdHomeRot);
        gourd.gameObject.SetActive(true);
        running = false;
    }

    bool carrying;

    // ---------------------------------------------------------------- 過場鏡頭

    CinemachineCamera vcam;
    ThirdPersonCamera tpc;
    bool cine, flying;
    float letterboxAmount, cineStart, dialogueSeen;
    Vector3 camPos;
    Quaternion camRot;
    System.Func<Vector3> shotLook;
    float shotDolly, shotMinDist;
    static Texture2D black;

    void BeginCamera()
    {
        tpc = FindAnyObjectByType<ThirdPersonCamera>();
        if (vcam == null)
        {
            vcam = new GameObject("CM ApothecaryPotionGift").AddComponent<CinemachineCamera>();
            if (tpc != null && tpc.cinemachineCamera != null) vcam.Lens = tpc.cinemachineCamera.Lens;
            vcam.Priority = 1000; // 蓋過玩家的鏡頭；刪掉之後回到玩家鏡頭
            vcam.Prioritize();
        }
        cine = true;
        flying = false;
        cineStart = Time.unscaledTime;
        VitalsHud.Hidden = true;
        InteractPrompt.Block(this);
    }

    /// <summary>切到一個固定鏡頭（直接切，不平滑）：之後每幀轉向 look、往它推近 dolly m/s，近到 minDist 就停；被牆擋住就拉到牆前面</summary>
    void Shot(Vector3 pos, System.Func<Vector3> look, float fov, float dolly, float minDist)
    {
        if (vcam == null) return;
        Vector3 l = look();
        pos = Vector3.Lerp(l, pos, Clearance(l, pos));
        camPos = pos;
        camRot = Quaternion.LookRotation(l - pos);
        shotLook = look;
        shotDolly = dolly;
        shotMinDist = minDist;
        vcam.transform.SetPositionAndRotation(camPos, camRot);
        var lens = vcam.Lens;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
        vcam.PreviousStateIsValid = false;
    }

    /// <summary>
    /// 拍兩個人（越過玩家的肩膀，或側面的雙人鏡頭）：繞著兩個人一圈找鏡頭位置，挑沒被牆、柱子、欄杆擋住的，
    /// 越接近想要的方向（越過肩膀 = 玩家斜後方；側面 = 兩人連線的側邊）越好。玩家常常背對著房子，硬拍的話鏡頭會被拉到玩家身上
    /// </summary>
    void OverShoulder(Transform player, Vector3 toApo, float back, float height, System.Func<Vector3> look, float fov, float dolly, float minDist, bool preferSide = false)
    {
        Vector3 side = Vector3.Cross(Vector3.up, toApo);
        if (preferSide)
            PickAround(Vector3.Lerp(player.position, transform.position, 0.5f), 2.7f, height * 0.85f, side, true, null, look, fov, dolly, minDist);
        else
            PickAround(player.position, Mathf.Max(1.2f, back + 0.2f), height, (-toApo + side * 0.55f).normalized, false, null, look, fov, dolly, minDist);
    }

    /// <summary>
    /// 繞著 center 一圈（16 個方向、半徑 radius）找鏡頭位置：沒被牆、柱子擋住的優先，再看離想要的方向（ideal；
    /// either = 正反兩邊都可以，例如兩人連線的側邊）多近。occluder：拍玩家時藥師不能擋在鏡頭前面
    /// </summary>
    void PickAround(Vector3 center, float radius, float height, Vector3 ideal, bool either, Transform occluder, System.Func<Vector3> look, float fov, float dolly, float minDist)
    {
        Vector3 l = look();
        Vector3 best = center + ideal * radius + Vector3.up * height;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < 16; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
            Vector3 c = center + d * radius + Vector3.up * height;
            float align = either ? Mathf.Abs(Vector3.Dot(d, ideal)) : Vector3.Dot(d, ideal);
            float clear = Mathf.Min(Clearance(l, c), Clearance(center + Vector3.up * 1.2f, c));
            if (occluder != null)
            {
                // 擋在中間：occluder 的身體（胸口高度）離鏡頭到目標的線段太近
                Vector3 o = occluder.position + Vector3.up * 1.1f * occluder.lossyScale.y, seg = l - c;
                float t = Mathf.Clamp01(Vector3.Dot(o - c, seg) / Mathf.Max(1e-4f, seg.sqrMagnitude));
                if ((c + seg * t - o).magnitude < 0.45f && t > 0.05f && t < 0.95f) clear = Mathf.Min(clear, 0.2f);
            }
            float score = (clear >= 0.999f ? 10f : clear * 5f) + align * 2f;
            if (score > bestScore) { bestScore = score; best = c; }
        }
        Shot(best, look, fov, dolly, minDist);
    }

    /// <summary>交還操作時玩家鏡頭的方向：先試面向的正後方，被牆擋住就左右轉著找一個鏡頭擺得下的方向</summary>
    float HandOffYaw()
    {
        float baseYaw = playerTf.eulerAngles.y;
        Vector3 head = playerTf.position + Vector3.up * 1.4f;
        float bestYaw = baseYaw, bestClear = -1f;
        foreach (float d in new[] { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 180f })
        {
            float yaw = baseYaw + d;
            Vector3 camAt = head - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 3.2f + Vector3.up * 0.6f;
            float c = Clearance(head, camAt);
            if (c >= 0.999f) return yaw;
            if (c > bestClear) { bestClear = c; bestYaw = yaw; }
        }
        return bestYaw;
    }

    void UpdateShot()
    {
        if (!cine || flying || vcam == null || shotLook == null) return;
        Vector3 l = shotLook();
        Vector3 to = l - camPos;
        float d = to.magnitude;
        if (d > shotMinDist && shotDolly > 0f) camPos += to / d * Mathf.Min(d - shotMinDist, shotDolly * Time.deltaTime);
        if (to.sqrMagnitude > 1e-4f) camRot = Quaternion.Slerp(camRot, Quaternion.LookRotation(l - camPos), 1f - Mathf.Exp(-4f * Time.deltaTime));
        vcam.transform.SetPositionAndRotation(camPos, camRot);
    }

    /// <summary>鏡頭從最後一個鏡頭順順地飛到玩家背後（終點每幀跟著玩家鏡頭的實際位置），接上去不會跳</summary>
    IEnumerator FlyToPlayer()
    {
        if (vcam == null || tpc == null || tpc.cinemachineCamera == null || playerTf == null) yield break;
        tpc.yaw = HandOffYaw();
        tpc.pitch = 12f;
        tpc.SnapToTarget();
        var playerCam = tpc.cinemachineCamera;
        var oldStandby = playerCam.StandbyUpdate;
        playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        flying = true;
        Vector3 fromPos = camPos;
        Quaternion fromRot = camRot;
        float fromFov = vcam.Lens.FieldOfView, toFov = playerCam.Lens.FieldOfView;
        yield return null;
        for (float t = 0f; t < flySeconds; t += Time.deltaTime)
        {
            var state = playerCam.State;
            float u = Mathf.Clamp01(t / flySeconds);
            float e = u * u * u * (u * (u * 6f - 15f) + 10f);
            camPos = Vector3.Lerp(fromPos, state.GetFinalPosition(), e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.5f);
            camRot = Quaternion.Slerp(fromRot, state.GetFinalOrientation(), e);
            vcam.transform.SetPositionAndRotation(camPos, camRot);
            var lens = vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(fromFov, toFov, e);
            vcam.Lens = lens;
            yield return null;
        }
        playerCam.StandbyUpdate = oldStandby;
    }

    void EndCamera()
    {
        if (vcam != null) Destroy(vcam.gameObject);
        vcam = null;
        if (!flying && tpc != null && playerTf != null)
        {
            tpc.yaw = HandOffYaw();
            tpc.pitch = 12f;
            tpc.SnapToTarget();
        }
        flying = false;
        cine = false;
        shotLook = null;
        VitalsHud.Hidden = false;
        InteractPrompt.Unblock(this);
    }

    /// <summary>look → pos 中間有沒有東西擋著（玩家、藥師、藥缽、藥瓶不算）：1 = 沒擋，否則是擋住的地方（比例，再往前留 0.25 m）</summary>
    float Clearance(Vector3 look, Vector3 pos)
    {
        Vector3 d = pos - look;
        float len = d.magnitude;
        if (len < 0.01f) return 1f;
        float nearest = len;
        foreach (var h in Physics.RaycastAll(look, d / len, len, ~0, QueryTriggerInteraction.Ignore))
        {
            var t = h.collider.transform;
            if (t.IsChildOf(transform) || (playerTf != null && t.IsChildOf(playerTf)) || (mortar != null && t.IsChildOf(mortar)) || (gourd != null && t.IsChildOf(gourd))) continue;
            if (h.distance < nearest) nearest = h.distance;
        }
        return nearest >= len ? 1f : Mathf.Clamp01((nearest - 0.25f) / len);
    }

    /// <summary>臉的位置（頭骨往上一點、往前一點）；找不到頭就用身高的比例</summary>
    static Vector3 Face(Transform who, float fallbackHeight)
    {
        var a = who.GetComponentInChildren<Animator>();
        var head = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return who.position + Vector3.up * fallbackHeight * who.lossyScale.y;
        return head.position + Vector3.up * 0.07f + Flat(who.forward).normalized * 0.05f;
    }

    void FacePlayerTo(Vector3 p)
    {
        if (playerTf == null) return;
        Vector3 d = Flat(p - playerTf.position);
        if (d.sqrMagnitude > 1e-4f) playerTf.rotation = Quaternion.LookRotation(d);
    }

    void LockPlayer(ThirdPersonController c)
    {
        lockedCtrl = c;
        if (c == null) return;
        c.LockActions();
        c.inputEnabled = false;
        c.StopHorizontalMotion();
    }

    void UnlockPlayer()
    {
        if (lockedCtrl == null) return;
        lockedCtrl.UnlockActions();
        lockedCtrl.SuppressJump(0.4f); // 跳過用的 Space 不要變成跳
        var hp = lockedCtrl.GetComponent<PlayerHealth>();
        if (hp == null || !hp.IsDead) lockedCtrl.inputEnabled = true;
        lockedCtrl = null;
    }

    /// <summary>跳過：直接收尾（藥缽、藥瓶放回去，藥師回去做自己的事，玩家喝完、加上藥效），鏡頭切回玩家背後</summary>
    void SkipNow()
    {
        StopAllCoroutines();
        if (stream != null) stream.enabled = false;
        tilt = 0f; armW = 0f; carrying = false; held = null;
        if (mortar != null) Release(mortar, mortarParent, mortarHome, mortarHomeRot);
        if (playerTf != null) { var d = playerTf.GetComponent<PotionDrink>(); if (d != null) Destroy(d); }
        if (gourd != null) { Release(gourd, gourdParent, gourdHome, gourdHomeRot); gourd.gameObject.SetActive(true); }
        routine.ScriptedSeatY = 0f;
        routine.ResumeRoutine();
        npc.enabled = true;
        if (!buffGiven) { AttackPotionBuff.Begin(bonus, duration); buffGiven = true; }
        PlayerLoadout.Unholster(this);
        EndCamera();
        UnlockPlayer();
        running = false;
    }

    void OnGUI()
    {
        if (letterboxAmount <= 0f || GameUI.OverlayOpen) return;
        GUI.depth = -1000; // 對話框（-1200）畫在黑邊上面
        if (black == null) { black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply(); }
        float h = Screen.height * 0.085f * Mathf.SmoothStep(0f, 1f, letterboxAmount);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, h), black);
        GUI.DrawTexture(new Rect(0f, Screen.height - h, Screen.width, h), black);
        if (cine && allowSkip && !flying && !StoryDialogue.Active)
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

    static IEnumerator Say(string name, Transform who, bool right, string text, Color color)
    {
        StoryDialogue.Play(new List<StoryDialogue.Line> { StoryDialogue.L(name, who, right, text, color) });
        yield return null;
        while (StoryDialogue.Active) yield return null;
    }

    IEnumerator WalkTo(Vector3 target, Vector3 face)
    {
        float speed = routine.walkSpeed * 0.9f;
        Vector3 start = transform.position;
        float total = Flat(target - start).magnitude, done = 0f;
        while (done < total - 0.02f)
        {
            float step = speed * Time.deltaTime;
            done = Mathf.Min(total, done + step);
            Vector3 p = Vector3.Lerp(start, target, total > 1e-4f ? done / total : 1f);
            transform.position = p;
            Vector3 dir = Flat(target - start);
            if (dir.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 300f * Time.deltaTime);
            routine.ScriptedSpeed = speed;
            yield return null;
        }
        routine.ScriptedSpeed = 0f;
        face.y = 0f;
        if (face.sqrMagnitude < 1e-4f) yield break;
        Quaternion want = Quaternion.LookRotation(face);
        float t = 0f;
        while (Quaternion.Angle(transform.rotation, want) > 1f && t < 1f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, 240f * Time.deltaTime);
            yield return null;
        }
    }

    /// <summary>右手在 seconds 秒內從現在的位置移到 target；armW 從 w0 混到 w1</summary>
    IEnumerator MoveHand(Vector3 target, float seconds, float w0, float w1)
    {
        // 從手現在實際的位置開始（上一幀擺好的手掌中心），不會跳一下
        Vector3 from = rHand != null ? NpcRoutine.Palm(rHand, rMiddle, rFore, transform.lossyScale.y) : target;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / Mathf.Max(0.05f, seconds));
            float k = Mathf.SmoothStep(0f, 1f, t);
            handTarget = Vector3.Lerp(from, target, k);
            armW = Mathf.Lerp(w0, w1, k);
            yield return null;
        }
    }

    IEnumerator Tilt(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.deltaTime / seconds);
            tilt = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
    }

    void Grab(Transform item)
    {
        held = item;
        grabNow = true; // LateUpdate 擺好手以後記下相對位置
        item.SetParent(null, true);
    }

    void Release(Transform item, Transform parent, Vector3 pos, Quaternion rot)
    {
        if (held == item) held = null;
        item.SetParent(parent, true);
        item.SetPositionAndRotation(pos, rot);
    }

    /// <summary>藥汁：從藥缽靠藥瓶那一邊的缽口，一道往下彎的細流流進瓶口（一開始細、中間粗、快倒完又變細）</summary>
    void DrawStream(Vector3 mouth, Vector3 toGourd, float t)
    {
        if (stream == null)
        {
            var go = new GameObject("PotionStream");
            go.transform.SetParent(transform, false);
            stream = go.AddComponent<LineRenderer>();
            stream.positionCount = 8;
            stream.useWorldSpace = true;
            stream.sharedMaterial = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
            stream.startColor = stream.endColor = Medicine;
            stream.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            stream.numCapVertices = 2;
        }
        stream.enabled = true;
        // 缽口靠藥瓶那一邊：缽底中心沿著缽（已經斜了）的上方往上一個缽高，再往藥瓶那邊半個缽寬
        Vector3 lip = mortar.position + mortar.up * mortarSize.y * 0.95f + toGourd * mortarSize.x * 0.5f;
        float flow = Mathf.Clamp01(t / 0.2f) * Mathf.Clamp01((1.3f - t) / 0.3f);
        stream.widthMultiplier = 0.016f * (0.4f + 0.6f * flow);
        for (int i = 0; i < 8; i++)
        {
            float u = i / 7f;
            Vector3 p = Vector3.Lerp(lip, mouth, u);
            p += toGourd * Mathf.Sin(u * Mathf.PI) * 0.03f;                 // 流出去先往外一點再落下
            p += Vector3.Cross(Vector3.up, toGourd) * Mathf.Sin(Time.time * 23f + u * 6f) * 0.002f; // 細細地晃
            stream.SetPosition(i, p);
        }
    }

    // ---------------------------------------------------------------- 右手

    void LateUpdate()
    {
        if (StoryDialogue.Active) dialogueSeen = Time.unscaledTime;
        UpdateShot();
        if (rArm == null || rFore == null || rHand == null) return;
        if (armW > 0.001f || held != null)
        {
            float s = transform.lossyScale.y;
            Vector3 target = handTarget;
            if (carrying && chest != null) target = chest.position + transform.forward * 0.32f * s + transform.right * 0.12f * s - Vector3.up * 0.12f * s;
            float w = carrying ? 1f : armW;
            Vector3 pole = rArm.position - Vector3.up * 0.45f * s + transform.right * 0.4f * s - transform.forward * 0.1f * s;
            Vector3 palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, s) - rHand.position;
            NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, w);
            if (w > 0.999f)
            {
                palmOff = NpcRoutine.Palm(rHand, rMiddle, rFore, s) - rHand.position;
                NpcRoutine.ReachIK(rArm, rFore, rHand, target - palmOff, pole, 1f);
            }
            if (Mathf.Abs(tilt) > 0.01f) rHand.rotation = Quaternion.AngleAxis(tilt, tiltAxis) * rHand.rotation;
            NpcRoutine.TurnPalm(rHand, rMiddle, rThumb, false, -transform.right, w * 0.6f);
        }
        if (held != null)
        {
            if (grabNow)
            {
                grabNow = false;
                heldPos = Quaternion.Inverse(rHand.rotation) * (held.position - rHand.position);
                heldRot = Quaternion.Inverse(rHand.rotation) * held.rotation;
            }
            held.SetPositionAndRotation(rHand.position + rHand.rotation * heldPos, rHand.rotation * heldRot);
        }
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    float GroundAt(Vector3 p, float fallback)
    {
        var hits = Physics.RaycastAll(new Vector3(p.x, fallback + 2f, p.z), Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }
}
