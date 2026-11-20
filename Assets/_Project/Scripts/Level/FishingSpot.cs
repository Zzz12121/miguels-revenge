using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 老漁夫旁邊的釣魚位子（棧橋末端，VillageEconomy 自動放好）：
///   - 跟老漁夫說話選「一起釣魚」（說完話走到位子上直接下竿），或走到位子（魚簍旁邊）按 E 下竿：武器收起來，右手拿出竹釣竿、甩出浮標落到前面的海上
///   - 等 2.5 ~ 6.5 秒（中間浮標偶爾輕輕抖一下）魚上鉤：浮標沉下去，畫面中間一次出現一個 1 ~ 9 的數字，
///     按對了換下一個（下面一排小方格是進度），在時間內全部按完就釣上來；按錯或時間到，魚就跑了。越大的魚數字越多（FishCatalog）
///   - 用手把時（魚上鉤那一刻最近用的是手把）：轉軸上寫的是 A / B / X / Y，要順時針轉左搖桿把轉軸轉過來（轉半圈 = 一格），
///     轉到正面的框裡時按對應的鈕；轉不過頭（停在還沒按的那一格），還沒轉到就按不算，按錯魚就跑了。時間是鍵盤的 1.6 倍
///   - 每一竿都有甩竿的動作：右手把釣竿舉到肩後（浮標吊在竿尖晃）、往前一甩放線，浮標劃一道弧線落到海上
///   - 釣上來的魚從水裡飛到手上，放進魚簍（PlayerInventory）；魚跑掉的話先把浮標收回來。接著自動再甩一竿；按 E 收竿
///   - 釣到的魚拿去岸邊給補網的年輕漁夫換錢（FishBuyerShop）
/// 釣竿、浮標、魚都是程式做的；手臂在所有動畫姿勢擺完之後才擺（DefaultExecutionOrder）。
/// </summary>
[DefaultExecutionOrder(32300)]
public sealed class FishingSpot : MonoBehaviour
{
    public MerchantNPC fisherman;
    [Tooltip("離位子多近可以下竿 (m)")]
    public float range = 1.5f;
    [Tooltip("浮標甩出去多遠 (m)")]
    public float castDistance = 6.5f;
    [Tooltip("海面的高度")]
    public float waterY = 0f;
    [Tooltip("多久會有魚上鉤 (秒，隨機)")]
    public Vector2 biteDelay = new Vector2(2.5f, 6.5f);
    [Tooltip("魚上鉤後，數字出現到開始計時的緩衝 (秒)")]
    public float graceTime = 0.4f;
    public float rodLength = 2.4f;

    enum State { Idle, Readying, Casting, Waiting, Bite, Landing, Escaped }
    State state;
    float stateStart;

    PlayerHealth health;
    ThirdPersonController controller;
    Animator animator;
    Transform upperArm, foreArm, hand, middleRoot;
    Transform lUpperArm, lForeArm, lHand;
    readonly Transform[][] rightFingers = new Transform[5][], leftFingers = new Transform[5][];
    float armWeight;

    // 釣竿、釣線、浮標
    Transform rod, bobber;
    LineRenderer line;
    Material lineMat;
    readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();
    Vector3 landing, castFrom, bobberPos;
    float nextBite, nibbleAt;

    // 上鉤
    FishCatalog.Fish hooked;
    int[] digits;
    int progress;
    float biteStart, digitShownAt, reelPos;

    // 拉上來的魚
    Transform caughtFish;
    Vector3 fishFrom;

    string message;
    float messageTime = float.NegativeInfinity;
    Color messageColor = Color.white;

    static AudioClip splashClip;

    // 甩竿：舉到肩後（WindUp 秒）→ 往前甩（到 ThrowEnd 秒，Release 放線）→ 浮標飛 Flight 秒落水
    const float WindUp = 0.45f, Release = 0.56f, ThrowEnd = 0.68f, Flight = 0.7f;
    bool released, snapBobber;

    bool pendingStart;

    /// <summary>老漁夫的對話選了「一起釣魚」：等對話框關掉（玩家恢復操作）之後走到位子上下竿</summary>
    public void BeginAfterDialogue() => pendingStart = true;

    bool Active => state != State.Idle;
    float Since => Time.time - stateStart;
    void Enter(State s) { state = s; stateStart = Time.time; }

    void Start()
    {
        BuildBasket();
    }

    void Update()
    {
        if (health == null)
        {
            health = PlayerHealth.Find();
            if (health == null) return;
            controller = health.GetComponent<ThirdPersonController>();
        }
        if (GameUI.OverlayOpen) return;

        if (!Active)
        {
            if (health.IsDead || StoryDialogue.Active) { pendingStart = false; return; }
            if (pendingStart)
            {
                // 對話框這一幀才關掉（MerchantNPC 先跑、恢復操作），這裡接著下竿
                if (fisherman != null && fisherman.InConversation) return;
                if (controller != null && !controller.inputEnabled) return;
                pendingStart = false;
                WarpToSpot();
                Begin();
                return;
            }
            Vector3 d = health.transform.position - transform.position;
            float dist = new Vector2(d.x, d.z).magnitude;
            if (dist > range || Mathf.Abs(d.y) > 1f) return;
            // 位子就在老漁夫旁邊：站在位子上時「釣魚」優先，不會被「與老漁夫對話」搶走
            InteractPrompt.Offer(this, dist * 0.2f, "釣魚");
            if (InteractPrompt.Pressed(this)) Begin();
            return;
        }

        if (health.IsDead || StoryDialogue.Active) { Stop(); return; }
        // 手把收線時 A / B / X / Y 都是收線要按的鈕：只有鍵盤的互動鍵能放棄
        bool quit = state == State.Bite && padMode ? InputBindings.KeyboardPressed(GameAction.Interact)
                  : InputBindings.Pressed(GameAction.Interact) || PadInput.CancelPressed;

        switch (state)
        {
            case State.Readying:
                if (quit) { Stop(); return; }
                if (PlayerLoadout.WeaponsAway || Since > 0.9f) Cast();
                break;

            case State.Casting:
                if (quit) { Stop(); return; }
                if (!released && Since >= Release)
                {
                    released = true;
                    castFrom = bobberPos;
                    GameAudio.Play("SFX/Gen/swing_whoosh_2", health.transform.position, 0.5f, 1.25f, 0.08f, 0.3f);
                }
                if (Since >= Release + Flight)
                {
                    Splash(landing, 0.7f);
                    Enter(State.Waiting);
                    nextBite = Time.time + Random.Range(biteDelay.x, biteDelay.y);
                    nibbleAt = Random.value < 0.7f ? Time.time + Random.Range(0.8f, Mathf.Max(1f, nextBite - Time.time - 0.6f)) : -1f;
                }
                break;

            case State.Waiting:
                if (quit) { Stop(); return; }
                if (Time.time >= nextBite) Hook();
                break;

            case State.Bite:
                if (quit) { Escape("收竿了，魚也跑了。", true); return; }
                ReadDigits();
                if (state == State.Bite && Time.time > biteStart + graceTime + biteSeconds) Escape("太慢了……魚跑掉了。", false);
                break;

            case State.Landing:
                if (Since >= 1.5f) Cast();
                break;

            case State.Escaped:
                if (quit) { Stop(); return; }
                if (Since >= 1.3f) Cast();
                break;
        }
    }

    // ---------------------------------------------------------------- 流程

    void Begin()
    {
        if (controller != null)
        {
            controller.LockActions();
            controller.inputEnabled = false;
            controller.FaceDirection(transform.forward);
        }
        PlayerLoadout.Holster(this);
        ResolveBones();
        Enter(State.Readying);
    }

    /// <summary>把玩家放到位子上、面向海（CharacterController 要先關掉才能直接移動）</summary>
    void WarpToSpot()
    {
        var cc = health.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        health.transform.SetPositionAndRotation(transform.position + Vector3.up * 0.05f, Quaternion.LookRotation(transform.forward));
        Physics.SyncTransforms();
        if (cc != null && was) cc.enabled = true;
    }

    void Cast()
    {
        ShowRod(true);
        Vector3 fwd = Vector3.ProjectOnPlane(health.transform.forward, Vector3.up).normalized;
        landing = health.transform.position + fwd * castDistance;
        landing.y = waterY;
        if (state == State.Readying) snapBobber = true; // 第一竿：釣竿這一幀才拿出來，浮標等釣竿擺好再吊到竿尖
        released = false;
        caughtFish = DestroyFish();
        GameAudio.Play("SFX/RPG/cloth1", health.transform.position, 0.4f, 1.1f, 0.08f, 0.3f);
        Enter(State.Casting);
    }

    // 手把模式：轉軸上是 A B X Y（0 ~ 3）；crank = 轉軸轉到第幾格（轉左搖桿推動）
    bool padMode, stickHeld;
    float crank, lastStickAngle, biteSeconds, ratchet;
    static readonly string[] PadLabels = { "A", "B", "X", "Y" };
    static readonly Color[] PadColors = { new Color(.35f, .75f, .3f), new Color(.85f, .3f, .25f), new Color(.3f, .5f, .9f), new Color(.9f, .75f, .2f) };

    void Hook()
    {
        hooked = FishCatalog.Roll();
        padMode = PadInput.Connected && PadInput.UsingPadRecently;
        biteSeconds = hooked.seconds * (padMode ? 1.6f : 1f);
        digits = new int[hooked.digits];
        for (int i = 0; i < digits.Length; i++)
        {
            int d;
            do d = padMode ? Random.Range(0, 4) : Random.Range(1, 10); while (i >= 2 && d == digits[i - 1] && d == digits[i - 2]); // 不要連續三個一樣
            digits[i] = d;
        }
        progress = 0;
        reelPos = 0f;
        crank = -0.9f; // 手把：第一格在正面下方一點，要先轉一下
        stickHeld = false;
        biteStart = digitShownAt = Time.time;
        Splash(landing, 1f);
        GameAudio.Play("SFX/RPG/creak2", health.transform.position, 0.5f, 1.4f, 0.1f, 0.3f); // 釣竿被拉彎
        Enter(State.Bite);
    }

    /// <summary>
    /// 手把收線：順時針轉左搖桿推動轉軸（半圈 = 一格，逆時針不算），轉軸不會轉過還沒按的那一格；
    /// 那一格轉到正面的框裡（差不到 0.2 格）時按對應的 A / B / X / Y。還沒轉到就按 = 不算（轉軸抖一下），按錯 = 魚跑了
    /// </summary>
    void ReadPad()
    {
        var pad = PadInput.Pad;
        if (pad == null) { padMode = false; return; }
        Vector2 stick = PadInput.Move;
        if (stick.magnitude > 0.5f)
        {
            float ang = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
            if (stickHeld)
            {
                float d = Mathf.DeltaAngle(lastStickAngle, ang);
                if (d < 0f) // 順時針
                {
                    float before = crank;
                    crank = Mathf.Min(crank + (-d) / 180f, progress + 0.12f);
                    ratchet += crank - before;
                    if (ratchet >= 0.25f) { ratchet = 0f; GameAudio.Play("SFX/RPG/creak1", health.transform.position, 0.25f, 2.2f, 0.1f, 0.3f); }
                }
            }
            lastStickAngle = ang;
            stickHeld = true;
        }
        else stickHeld = false;

        int pressed = pad.buttonSouth.wasPressedThisFrame ? 0 : pad.buttonEast.wasPressedThisFrame ? 1 : pad.buttonWest.wasPressedThisFrame ? 2 : pad.buttonNorth.wasPressedThisFrame ? 3 : -1;
        if (pressed < 0) return;
        if (Mathf.Abs(crank - progress) > 0.2f) { digitShownAt = Time.time - 0.1f; return; } // 還沒轉到
        if (pressed != digits[progress])
        {
            Escape($"按錯了（要按 {PadLabels[digits[progress]]}）……魚跑掉了。", false);
            return;
        }
        progress++;
        digitShownAt = Time.time;
        GameAudio.Play("SFX/RPG/metalClick", health.transform.position, 0.45f, 0.9f + 0.08f * progress, 0.02f, 0.3f);
        if (progress >= digits.Length) Catch();
    }

    void ReadDigits()
    {
        if (padMode) { ReadPad(); return; }
        var kb = Keyboard.current;
        if (kb == null) return;
        for (int n = 1; n <= 9; n++)
        {
            if (!kb[Key.Digit0 + n].wasPressedThisFrame && !kb[Key.Numpad0 + n].wasPressedThisFrame) continue;
            if (n != digits[progress])
            {
                Escape($"按錯了（要按 {digits[progress]}）……魚跑掉了。", false);
                return;
            }
            progress++;
            digitShownAt = Time.time;
            GameAudio.Play("SFX/RPG/metalClick", health.transform.position, 0.45f, 0.9f + 0.08f * progress, 0.02f, 0.3f);
            if (progress >= digits.Length) { Catch(); return; }
        }
    }

    void Catch()
    {
        VillageWallet.Add(hooked.id, 1);
        Splash(bobberPos, 1.1f);
        GameAudio.Play("SFX/RPG/beltHandle1", health.transform.position, 0.8f, 1.1f, 0.05f, 0.3f);
        VictoryBanner.ShowSmall($"釣到{hooked.name}了！", $"可以賣 {hooked.price} 文　魚簍裡有 {FishCatalog.CountAll()} 條", GameUI.Good);
        caughtFish = BuildFish(hooked);
        fishFrom = bobberPos;
        Say($"{hooked.name}（{hooked.price} 文）", GameUI.Good);
        Enter(State.Landing);
    }

    void Escape(string why, bool thenStop)
    {
        Say(why, GameUI.Bad);
        GameAudio.Play("SFX/RPG/cloth3", health.transform.position, 0.5f, 0.8f, 0.05f, 0.3f);
        hooked = null;
        if (thenStop) Stop();
        else Enter(State.Escaped);
    }

    void Stop()
    {
        bool was = Active;
        state = State.Idle;
        ShowRod(false);
        caughtFish = DestroyFish();
        if (!was) return;
        PlayerLoadout.Unholster(this);
        if (controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            if (health == null || !health.IsDead) controller.inputEnabled = true;
        }
    }

    void OnDisable() { if (Active) Stop(); }

    void OnDestroy()
    {
        foreach (var o in owned) if (o != null) Destroy(o);
        if (rod != null) Destroy(rod.gameObject);
        if (bobber != null) Destroy(bobber.gameObject);
        if (line != null) Destroy(line.gameObject);
    }

    void Say(string text, Color color)
    {
        message = text;
        messageColor = color;
        messageTime = Time.time;
    }

    // ---------------------------------------------------------------- 姿勢、釣竿、浮標（所有動畫擺完之後）

    void ResolveBones()
    {
        if (health == null) return;
        if (animator == null) animator = health.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return;
        upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        foreArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        middleRoot = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        lUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lForeArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        lHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        // 手指：拇指、食指、中指、無名指、小指，各三節（HumanBodyBones 裡左手從 LeftThumbProximal 起、右手從 RightThumbProximal 起連續排）
        for (int f = 0; f < 5; f++)
        {
            rightFingers[f] = new Transform[3];
            leftFingers[f] = new Transform[3];
            for (int k = 0; k < 3; k++)
            {
                rightFingers[f][k] = animator.GetBoneTransform(HumanBodyBones.RightThumbProximal + f * 3 + k);
                leftFingers[f][k] = animator.GetBoneTransform(HumanBodyBones.LeftThumbProximal + f * 3 + k);
            }
        }
    }

    void LateUpdate()
    {
        if (health == null) return;
        bool holding = state != State.Idle && state != State.Readying;
        armWeight = Mathf.MoveTowards(armWeight, holding ? 1f : 0f, Time.deltaTime * (holding ? 6f : 4f));
        Transform p = health.transform;
        // 甩竿的擺動：-1 = 舉到肩後、0 = 平常、+1 = 往前甩到底
        float swing = state == State.Casting ? CastSwing(Since) : 0f;

        // 釣竿的方向：往前上方伸出去；魚上鉤時舉高、抖動；甩竿時先往後倒、再往前甩
        float lift = state == State.Bite ? 1f : state == State.Landing ? Mathf.Clamp01(1f - (Since - 0.6f) / 0.6f) : 0f;
        Vector3 dir = (p.forward * Mathf.Lerp(0.82f, 0.5f, lift) + Vector3.up * Mathf.Lerp(0.55f, 0.86f, lift) + p.right * 0.06f).normalized;
        if (swing < 0f) dir = Vector3.Slerp(dir, (-p.forward * 0.55f + Vector3.up * 0.8f + p.right * 0.12f).normalized, -swing);
        else if (swing > 0f) dir = Vector3.Slerp(dir, (p.forward * 0.95f + Vector3.up * 0.25f).normalized, swing);
        if (state == State.Bite)
        {
            float shake = 0.04f + 0.03f * Mathf.Sin(Time.time * 5f);
            dir = (dir + p.right * Mathf.Sin(Time.time * 23f) * shake + p.forward * Mathf.Sin(Time.time * 17f) * shake).normalized;
        }
        // 兩手握竿：右手在肚子前面握竿尾，左手往前一點扶著竿子（手指都握起來）
        if (armWeight > 0.001f) PoseHands(armWeight, swing, dir);
        if (!holding || rod == null) return;
        Vector3 grip = RightPalm();
        rod.SetPositionAndRotation(grip - dir * 0.1f, Quaternion.FromToRotation(Vector3.up, dir)); // 竿尾只露出手掌後面一點（再長會插進肚子）
        Vector3 tip = RodTip();

        // 浮標
        float t = Time.time;
        switch (state)
        {
            case State.Casting:
            {
                if (snapBobber) { bobberPos = tip + Vector3.down * 0.45f; snapBobber = false; }
                if (!released)
                {
                    // 還沒放線：浮標吊在竿尖下面，跟著竿子晃（慢半拍）
                    bobberPos = Vector3.Lerp(bobberPos, tip + Vector3.down * 0.45f, Time.deltaTime * 14f);
                    break;
                }
                float k = Mathf.Clamp01((Since - Release) / Flight);
                bobberPos = Vector3.Lerp(castFrom, landing, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.6f);
                break;
            }
            case State.Waiting:
            {
                float nib = nibbleAt > 0f ? Mathf.Clamp01(1f - Mathf.Abs(t - nibbleAt) / 0.12f) : 0f;
                bobberPos = landing + Vector3.up * (0.03f + Mathf.Sin(t * 1.7f) * 0.012f - nib * 0.035f);
                break;
            }
            case State.Bite:
                bobberPos = landing + new Vector3(Mathf.Sin(t * 31f) * 0.03f, -0.07f + Mathf.Sin(t * 19f) * 0.02f, Mathf.Sin(t * 27f) * 0.03f);
                break;
            case State.Landing:
                bobberPos = Vector3.Lerp(bobberPos, tip + Vector3.down * 0.9f, Time.deltaTime * 6f);
                break;
            case State.Escaped:
                // 魚跑了：浮著一下，再把浮標收回竿尖
                if (Since < 0.45f) bobberPos = landing + Vector3.up * (0.03f + Mathf.Sin(t * 1.7f) * 0.012f);
                else bobberPos = Vector3.Lerp(bobberPos, tip + Vector3.down * 0.45f, Time.deltaTime * 5f);
                break;
        }
        bobber.position = bobberPos;
        DrawLine(tip, bobberPos, state == State.Bite || state == State.Landing ? 0.02f : state == State.Casting ? (released ? 0.25f : 0.02f) : 0.12f);

        // 拉上來的魚：從水裡甩出一道弧線飛到手邊，扭著身體，最後收進魚簍
        if (caughtFish != null)
        {
            float k = Mathf.Clamp01(Since / 0.65f);
            Vector3 to = grip + p.forward * 0.35f + Vector3.down * 0.2f;
            caughtFish.position = Vector3.Lerp(fishFrom, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 1.4f);
            caughtFish.rotation = Quaternion.LookRotation(Vector3.Lerp(Vector3.up, p.right, k) + p.forward * 0.1f, -p.forward)
                                  * Quaternion.Euler(0f, Mathf.Sin(Time.time * 26f) * 25f, 0f);
            if (Since > 1.1f) caughtFish = DestroyFish();
        }
    }

    /// <summary>甩竿的擺動（t = 開始甩竿後幾秒）：慢慢舉到肩後 → 很快地往前甩過頭 → 回到平常</summary>
    static float CastSwing(float t)
    {
        if (t < WindUp) return -Mathf.SmoothStep(0f, 1f, t / WindUp);
        if (t < ThrowEnd) return Mathf.Lerp(-1f, 0.8f, Mathf.SmoothStep(0f, 1f, (t - WindUp) / (ThrowEnd - WindUp)));
        return Mathf.Lerp(0.8f, 0f, Mathf.SmoothStep(0f, 1f, (t - ThrowEnd) / 0.45f));
    }

    Vector3 rightGrip;
    bool rightGripValid;

    /// <summary>右手握竿的地方（掌心，釣竿從這裡穿過）</summary>
    Vector3 RightPalm()
    {
        if (rightGripValid) return rightGrip;
        if (hand == null) return health.transform.position + Vector3.up * 1f + health.transform.right * 0.2f + health.transform.forward * 0.3f;
        return middleRoot != null ? Vector3.Lerp(hand.position, middleRoot.position, 0.75f) : hand.position;
    }

    /// <summary>
    /// 兩手握竿（雙骨 IK，手肘往外下方彎）：
    ///   - 右手：平常在肚子前面偏左一點（肩膀往前下方約半隻手臂）；甩竿時舉到右肩旁、再往前甩出去
    ///   - 左手：握在右手前面約五分之一隻手臂的竿子上（甩竿時鬆開一些）
    ///   - 手掌順著前臂的方向、食指那一側朝竿尖，竿子斜斜地橫過掌心；IK 先把手腕放好、轉好手掌，再照掌心離手腕多遠修正目標再解一次，
    ///     掌心（不是手腕）才會剛好在竿子上；最後手指往掌心彎、握住竿子
    /// </summary>
    void PoseHands(float w, float swing, Vector3 rodDir)
    {
        if (upperArm == null || foreArm == null || hand == null) { ResolveBones(); if (hand == null) return; }
        Transform p = health.transform;
        float armLen = Vector3.Distance(upperArm.position, foreArm.position) + Vector3.Distance(foreArm.position, hand.position);
        Vector3 s = upperArm.position;
        // 平常：手在肚子前面再往前一點（貓戰士的肚子往前凸約 13 cm，手要在它前面），偏身體中間一點，左手才搆得到
        Vector3 rest = s + (p.forward * 0.62f - Vector3.up * 0.5f - p.right * 0.1f) * armLen;
        Vector3 back = s + (p.forward * 0.05f + Vector3.up * 0.25f + p.right * 0.25f) * armLen;
        Vector3 front = s + (p.forward * 0.8f - Vector3.up * 0.3f) * armLen;
        Vector3 target = swing < 0f ? Vector3.Lerp(rest, back, -swing) : Vector3.Lerp(rest, front, swing);
        rightGrip = Grip(upperArm, foreArm, hand, rightFingers, false, target, rodDir,
                         s + (p.right * 0.8f - Vector3.up * 0.6f - p.forward * 0.2f) * armLen, w);
        rightGripValid = true;

        if (lUpperArm == null || lForeArm == null || lHand == null) return;
        float lw = w * (1f - Mathf.Abs(swing) * 0.85f);
        if (lw < 0.001f) return;
        Vector3 ls = lUpperArm.position;
        Grip(lUpperArm, lForeArm, lHand, leftFingers, true, rightGrip + rodDir * (0.22f * armLen), rodDir,
             ls + (-p.right * 0.8f - Vector3.up * 0.6f - p.forward * 0.2f) * armLen, lw);
    }

    /// <summary>一隻手握在竿子的 point 上：雙骨 IK 兩次（第二次照掌心的偏移修正）、轉手掌、彎手指。回傳掌心最後在哪</summary>
    static Vector3 Grip(Transform a, Transform b, Transform c, Transform[][] fingers, bool left, Vector3 point, Vector3 rodDir, Vector3 hint, float w)
    {
        Quaternion oa = a.rotation, ob = b.localRotation, oc = c.localRotation;
        Vector3 target = point, palmN = c.forward;
        for (int pass = 0; pass < 2; pass++)
        {
            TwoBone(a, b, c, target, hint, 1f);
            palmN = OrientHand(b, c, fingers, left, rodDir);
            target += point - PalmCenter(c, fingers, palmN);
        }
        if (w < 0.999f)
        {
            Quaternion fa = a.rotation, fb = b.localRotation, fc = c.localRotation;
            a.rotation = Quaternion.Slerp(oa, fa, w);
            b.localRotation = Quaternion.Slerp(ob, fb, w);
            c.localRotation = Quaternion.Slerp(oc, fc, w);
        }
        Curl(fingers, palmN, w);
        return PalmCenter(c, fingers, palmN);
    }

    /// <summary>
    /// 竿子的中心在哪裡：手腕往中指根部 75%（指根那一帶，握拳時竿子在這裡）、再往掌心外面 3.5 cm
    /// （手掌本身的厚度 + 竿子的半徑），竿子才是被手指握住、不是插在掌心裡
    /// </summary>
    static Vector3 PalmCenter(Transform hand, Transform[][] fingers, Vector3 palmN)
    {
        var middle = fingers[2][0];
        Vector3 c = middle != null ? Vector3.Lerp(hand.position, middle.position, 0.75f) : hand.position;
        return c + palmN * 0.035f;
    }

    /// <summary>
    /// 轉手掌：手指順著前臂的方向、食指那一側盡量朝竿尖，竿子就斜斜地橫過掌心（像握掃把柄）。回傳掌心朝哪裡（手指往這邊彎）。
    /// 手的方向用「手指方向（手腕 → 中指根部）」和「小指 → 食指」量；掌心 = 手指 × 小指到食指（右手；左手反過來）
    /// </summary>
    static Vector3 OrientHand(Transform forearm, Transform hand, Transform[][] fingers, bool left, Vector3 rodDir)
    {
        Transform index = fingers[1][0], middle = fingers[2][0], little = fingers[4][0];
        if (middle == null || index == null || little == null) return hand.forward;
        Vector3 f = (middle.position - hand.position).normalized;
        Vector3 n = Vector3.Cross(f, index.position - little.position);
        if (left) n = -n;
        if (n.sqrMagnitude < 1e-8f) return hand.forward;
        n.Normalize();
        // 手背順著前臂（手腕不折），竿子斜斜地橫過掌心：繞著前臂轉到食指那一側盡量朝竿尖
        Vector3 fd = (hand.position - forearm.position).normalized;
        Vector3 sd = Vector3.ProjectOnPlane(rodDir, fd);
        if (sd.sqrMagnitude < 1e-8f) return n;
        sd.Normalize();
        Vector3 nd = left ? Vector3.Cross(sd, fd) : Vector3.Cross(fd, sd);
        hand.rotation = Quaternion.LookRotation(fd, nd) * Quaternion.Inverse(Quaternion.LookRotation(f, n)) * hand.rotation;
        return nd;
    }

    /// <summary>
    /// 雙骨 IK：a = 上臂、b = 前臂、c = 手。先彎手肘讓手到目標的距離剛好，再整隻手臂轉過去，最後繞著肩膀到目標的線轉，讓手肘朝 hint
    /// </summary>
    static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 hint, float w)
    {
        Quaternion origA = a.rotation, origB = b.localRotation;
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        Vector3 toT = target - a.position;
        float lt = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 0.01f, la + lb - 0.005f);
        Vector3 u = a.position - b.position, v = c.position - b.position;
        Vector3 n = Vector3.Cross(u, v);
        if (n.sqrMagnitude < 1e-8f) n = Vector3.Cross(toT, hint - a.position);
        if (n.sqrMagnitude > 1e-8f)
        {
            float cur = Vector3.Angle(u, v);
            float want = Mathf.Acos(Mathf.Clamp((la * la + lb * lb - lt * lt) / (2f * la * lb), -1f, 1f)) * Mathf.Rad2Deg;
            b.rotation = Quaternion.AngleAxis(want - cur, n.normalized) * b.rotation;
        }
        Vector3 cur2 = c.position - a.position;
        if (cur2.sqrMagnitude > 1e-8f && toT.sqrMagnitude > 1e-8f) a.rotation = Quaternion.FromToRotation(cur2, toT) * a.rotation;
        // 手肘朝 hint
        Vector3 d = toT.normalized;
        Vector3 e = Vector3.ProjectOnPlane(b.position - a.position, d), h = Vector3.ProjectOnPlane(hint - a.position, d);
        if (e.sqrMagnitude > 1e-8f && h.sqrMagnitude > 1e-8f) a.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(e, h, d), d) * a.rotation;
        if (w < 0.999f)
        {
            Quaternion finA = a.rotation, finB = b.localRotation;
            a.rotation = Quaternion.Slerp(origA, finA, w);
            b.localRotation = Quaternion.Slerp(origB, finB, w);
        }
    }

    /// <summary>手指往掌心彎：每一節照順序轉（大拇指彎少一點）</summary>
    static void Curl(Transform[][] fingers, Vector3 palm, float w)
    {
        for (int f = 0; f < 5; f++)
        {
            var chain = fingers[f];
            if (chain == null) continue;
            for (int k = 0; k < 3; k++)
            {
                var bone = chain[k];
                if (bone == null) break;
                Vector3 tip = k < 2 && chain[k + 1] != null ? chain[k + 1].position : bone.position + (bone.position - (k > 0 && chain[k - 1] != null ? chain[k - 1].position : bone.parent.position)) * 0.6f;
                Vector3 dir = tip - bone.position;
                Vector3 axis = Vector3.Cross(dir, palm);
                if (axis.sqrMagnitude < 1e-10f) continue;
                float angle = (f == 0 ? (k == 0 ? 20f : 25f) : (k == 0 ? 55f : k == 1 ? 65f : 45f)) * w;
                bone.rotation = Quaternion.AngleAxis(angle, axis.normalized) * bone.rotation;
            }
        }
    }

    Vector3 RodTip() => rod != null ? rod.TransformPoint(new Vector3(0f, rodLength, 0f)) : transform.position;

    /// <summary>釣線：竿尖到浮標，中間往下垂（sag = 下垂量佔長度的比例）</summary>
    void DrawLine(Vector3 a, Vector3 b, float sag)
    {
        const int n = 12;
        line.positionCount = n;
        float len = Vector3.Distance(a, b);
        for (int i = 0; i < n; i++)
        {
            float u = i / (n - 1f);
            line.SetPosition(i, Vector3.Lerp(a, b, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * len * sag * 0.25f));
        }
    }

    void ShowRod(bool on)
    {
        if (on && rod == null) BuildRod();
        if (rod != null) rod.gameObject.SetActive(on);
        if (bobber != null) bobber.gameObject.SetActive(on);
        if (line != null) line.enabled = on;
    }

    Material Lit(Color c, float metallic = 0f, float smooth = 0.3f)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Runtime_Fishing" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        owned.Add(m);
        return m;
    }

    static GameObject Part(PrimitiveType type, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(pos, rot);
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>竹釣竿（+Y 朝竿尖，原點在竿尾）：下面一段粗、纏著繩子的握把，上面細，每隔一段一個竹節</summary>
    void BuildRod()
    {
        rod = new GameObject("PlayerFishingRod").transform;
        var bamboo = Lit(new Color(.72f, .62f, .36f), 0f, .45f);
        var node = Lit(new Color(.5f, .4f, .2f), 0f, .35f);
        var grip = Lit(new Color(.25f, .15f, .08f), 0f, .2f);
        float L = rodLength;
        Part(PrimitiveType.Cylinder, rod, grip, new Vector3(0f, 0.18f, 0f), Quaternion.identity, new Vector3(.034f, .18f, .034f));
        Part(PrimitiveType.Cylinder, rod, bamboo, new Vector3(0f, 0.36f + (L * 0.5f - 0.36f) * 0.5f, 0f), Quaternion.identity, new Vector3(.024f, (L * 0.5f - 0.36f) * 0.5f, .024f));
        Part(PrimitiveType.Cylinder, rod, bamboo, new Vector3(0f, L * 0.75f, 0f), Quaternion.identity, new Vector3(.013f, L * 0.25f, .013f));
        for (int i = 1; i < 6; i++)
        {
            float y = 0.36f + (L - 0.36f) * i / 6f;
            float r = y < L * 0.5f ? .028f : .017f;
            Part(PrimitiveType.Cylinder, rod, node, new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(r, .008f, r));
        }
        bobber = new GameObject("PlayerBobber").transform;
        Part(PrimitiveType.Sphere, bobber, Lit(new Color(.75f, .1f, .07f), 0f, .6f), Vector3.zero, Quaternion.identity, new Vector3(.07f, .09f, .07f));
        Part(PrimitiveType.Sphere, bobber, Lit(new Color(.95f, .93f, .88f), 0f, .6f), new Vector3(0f, .05f, 0f), Quaternion.identity, Vector3.one * .045f);
        line = new GameObject("PlayerFishingLine").AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = 0.006f;
        line.numCapVertices = 1;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineMat = new Material(Shader.Find("Sprites/Default")) { name = "Runtime_FishingLine" };
        owned.Add(lineMat);
        line.sharedMaterial = lineMat;
        line.startColor = line.endColor = new Color(.92f, .92f, .88f, .85f);
    }

    /// <summary>
    /// 拉上來的魚（+Z = 頭）：背、肚子兩種顏色（背是一顆往上偏、比較小的橢圓蓋在肚子上），尾鰭、背鰭，每一種的花紋不一樣：
    ///   竹莢魚 青灰背、銀白肚、一條黃色的側線；鯖魚 藍綠背上幾條深色的波紋；鱸魚 灰黑背、銀肚；
    ///   真鯛 粉紅、身上幾顆藍點；金目鯛 鮮紅、一雙大大的金色眼睛
    /// </summary>
    Transform BuildFish(FishCatalog.Fish f)
    {
        var root = new GameObject("CaughtFish").transform;
        float L = f.length;
        Color back, belly, fin;
        switch (f.id)
        {
            case "Fish_Aji": back = new Color(.36f, .46f, .5f); belly = new Color(.86f, .88f, .86f); fin = new Color(.6f, .62f, .5f); break;
            case "Fish_Saba": back = new Color(.08f, .36f, .38f); belly = new Color(.82f, .85f, .84f); fin = new Color(.15f, .25f, .27f); break;
            case "Fish_Suzuki": back = new Color(.2f, .22f, .24f); belly = new Color(.78f, .8f, .8f); fin = new Color(.3f, .32f, .33f); break;
            case "Fish_Tai": back = new Color(.85f, .35f, .4f); belly = new Color(.97f, .78f, .74f); fin = new Color(.8f, .3f, .3f); break;
            default: back = new Color(.85f, .08f, .05f); belly = new Color(.95f, .35f, .25f); fin = new Color(.75f, .1f, .06f); break;
        }
        var bellyM = Lit(belly, 0.45f, 0.85f);
        var backM = Lit(back, 0.35f, 0.8f);
        var finM = Lit(fin, 0.1f, 0.5f);
        Part(PrimitiveType.Sphere, root, bellyM, Vector3.zero, Quaternion.identity, new Vector3(L * 0.2f, L * 0.34f, L));
        Part(PrimitiveType.Sphere, root, backM, new Vector3(0f, L * 0.045f, -L * 0.01f), Quaternion.identity, new Vector3(L * 0.205f, L * 0.27f, L * 0.96f));
        Part(PrimitiveType.Cube, root, finM, new Vector3(0f, 0f, -L * 0.55f), Quaternion.Euler(45f, 0f, 0f), new Vector3(L * 0.02f, L * 0.22f, L * 0.22f));
        Part(PrimitiveType.Cube, root, finM, new Vector3(0f, L * 0.17f, -L * 0.05f), Quaternion.Euler(-20f, 0f, 0f), new Vector3(L * 0.015f, L * 0.1f, L * 0.35f));
        bool bigEyes = f.id == "Fish_Kinme";
        var eye = Lit(Color.black, 0f, .9f);
        var gold = Lit(new Color(.95f, .75f, .2f), .6f, .9f);
        for (int side = -1; side <= 1; side += 2)
        {
            float er = L * (bigEyes ? 0.085f : 0.05f);
            if (bigEyes) Part(PrimitiveType.Sphere, root, gold, new Vector3(side * L * 0.085f, L * 0.06f, L * 0.34f), Quaternion.identity, Vector3.one * er);
            Part(PrimitiveType.Sphere, root, eye, new Vector3(side * L * (bigEyes ? 0.1f : 0.088f), L * 0.06f, L * 0.36f), Quaternion.identity, Vector3.one * er * (bigEyes ? 0.55f : 1f));
            switch (f.id)
            {
                case "Fish_Aji": // 黃色的側線
                    Part(PrimitiveType.Cube, root, Lit(new Color(.92f, .8f, .25f), .3f, .7f), new Vector3(side * L * 0.098f, L * 0.0f, -L * 0.02f), Quaternion.identity, new Vector3(L * 0.01f, L * 0.03f, L * 0.75f));
                    break;
                case "Fish_Saba": // 背上深色的波紋
                    var stripe = Lit(new Color(.03f, .12f, .14f), .2f, .6f);
                    for (int k = 0; k < 4; k++)
                        Part(PrimitiveType.Cube, root, stripe, new Vector3(side * L * 0.085f, L * 0.09f, L * (0.22f - k * 0.14f)), Quaternion.Euler(0f, 0f, side * 25f) * Quaternion.Euler(30f, 0f, 0f), new Vector3(L * 0.01f, L * 0.09f, L * 0.025f));
                    break;
                case "Fish_Tai": // 幾顆藍點
                    var blue = Lit(new Color(.3f, .6f, .95f), .5f, .9f);
                    for (int k = 0; k < 4; k++)
                        Part(PrimitiveType.Sphere, root, blue, new Vector3(side * L * 0.096f, L * (0.03f + 0.03f * (k % 2)), L * (0.18f - k * 0.12f)), Quaternion.identity, Vector3.one * L * 0.022f);
                    break;
            }
        }
        return root;
    }

    Transform DestroyFish()
    {
        if (caughtFish != null) Destroy(caughtFish.gameObject);
        return null;
    }

    /// <summary>位子旁邊放一個竹編的魚簍（看得出這裡可以釣魚）</summary>
    void BuildBasket()
    {
        var basket = new GameObject("FishingSpot_Basket").transform;
        basket.SetParent(transform, false);
        basket.localPosition = new Vector3(0.55f, 0f, -0.25f);
        var straw = Lit(new Color(.6f, .48f, .26f), 0f, .2f);
        var rim = Lit(new Color(.4f, .3f, .15f), 0f, .2f);
        Part(PrimitiveType.Cylinder, basket, straw, new Vector3(0f, .14f, 0f), Quaternion.identity, new Vector3(.3f, .14f, .3f));
        Part(PrimitiveType.Cylinder, basket, rim, new Vector3(0f, .29f, 0f), Quaternion.identity, new Vector3(.24f, .015f, .24f));
        Part(PrimitiveType.Cylinder, basket, rim, new Vector3(0f, .1f, 0f), Quaternion.identity, new Vector3(.31f, .012f, .31f));
    }

    // ---------------------------------------------------------------- 水花

    void Splash(Vector3 at, float strength)
    {
        if (splashClip == null) splashClip = MakeSplash();
        GameAudio.PlayAt(splashClip, at, 0.55f * strength, Random.Range(0.9f, 1.15f) / Mathf.Max(0.6f, strength), 1f, 30f);
        FishingRipple.Spawn(new Vector3(at.x, waterY + 0.02f, at.z), 0.35f + 0.4f * strength);
    }

    /// <summary>水花聲（程式合成）：一下濾過的雜訊 + 兩個往上滑的「啵」</summary>
    static AudioClip MakeSplash()
    {
        const int rate = 22050;
        int n = (int)(rate * 0.55f);
        var data = new float[n];
        float lp = 0f;
        var rng = new System.Random(7);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp += (noise - lp) * Mathf.Lerp(0.5f, 0.08f, t / 0.55f);
            float env = Mathf.Min(1f, t / 0.006f) * Mathf.Exp(-t * 9f);
            float bloop = 0f;
            for (int k = 0; k < 2; k++)
            {
                float t0 = 0.05f + k * 0.09f, bt = t - t0;
                if (bt > 0f && bt < 0.08f) bloop += Mathf.Sin(2f * Mathf.PI * (300f + 900f * bt / 0.08f) * bt) * Mathf.Sin(bt / 0.08f * Mathf.PI) * 0.25f;
            }
            data[i] = Mathf.Clamp(lp * env * 1.6f + bloop, -1f, 1f);
        }
        var clip = AudioClip.Create("Splash", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------------------------------------------------------- 畫面

    void OnGUI()
    {
        if (!Active || GameUI.OverlayOpen || !CalligraphyWorldIsolation.Allows(transform)) return;
        GUI.depth = -1150;
        float sw = Screen.width, sh = Screen.height;

        // 右上：魚簍
        GameUI.Shadowed(new Rect(sw - 340f, 20f, 320f, 26f), $"魚簍 {FishCatalog.CountAll()} 條", GameUI.SmallRight, GameUI.TextColor); // 魚的數量只在釣魚時顯示

        if (state == State.Bite && digits != null) DrawDigits(sw, sh);
        else if (state == State.Waiting || state == State.Casting || state == State.Readying)
            Hint(new Rect((sw - 360f) * 0.5f, sh * 0.72f, 360f, 44f), state == State.Waiting ? "等魚上鉤……" : "下竿……");
        else if (state == State.Escaped || state == State.Landing)
            Hint(new Rect((sw - 360f) * 0.5f, sh * 0.72f, 360f, 44f), "再甩一竿……");

        float since = Time.time - messageTime;
        if (since < 1.6f)
        {
            var c = messageColor;
            c.a = 1f - Mathf.Clamp01((since - 1.1f) / 0.5f);
            GameUI.Shadowed(new Rect(0f, sh * 0.42f, sw, 36f), message, new GUIStyle(GameUI.Center) { fontSize = 24 }, c);
        }
    }

    /// <summary>畫面下方的小框：「等魚上鉤……　[E] 收竿」</summary>
    void Hint(Rect r, string text)
    {
        GameUI.Panel(r, GameUI.MutedColor);
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(r.x + 16f, r.y, r.width - 140f, r.height), text, GameUI.Small);
        string key = InteractPrompt.KeyName;
        float kw = Mathf.Max(26f, GameUI.Small.CalcSize(new GUIContent(key)).x + 12f);
        GUI.Label(new Rect(r.xMax - 64f, r.y, 60f, r.height), "收竿", GameUI.Small);
        GameUI.Key(new Rect(r.xMax - 72f - kw, r.y + 9f, kw, 26f), key);
        GUI.color = Color.white;
    }

    GUIStyle titleStyle;

    GUIStyle reelDigitStyle;
    static Texture2D dot;

    /// <summary>實心的圓（手把按鈕的底），邊緣抗鋸齒</summary>
    static Texture2D Dot
    {
        get
        {
            if (dot != null) return dot;
            const int n = 48;
            dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n * 0.5f + 0.5f, y - n * 0.5f + 0.5f).magnitude;
                    dot.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(n * 0.5f - 1f - d)));
                }
            dot.Apply();
            return dot;
        }
    }

    /// <summary>
    /// 上鉤了：數字寫在一個木頭轉軸（捲線的線軸）上，現在要按的那個在正面（金色的牌子上），下一個從下面轉上來、按過的轉到上面去；
    /// 按對了轉軸就轉一格、右邊的搖柄跟著轉（像在收線）。下面一排小方格是進度，最下面是時間條
    /// </summary>
    void DrawDigits(float sw, float sh)
    {
        titleStyle ??= new GUIStyle(GameUI.Center) { fontSize = 19 };
        reelDigitStyle ??= new GUIStyle(GameUI.Title) { fontSize = 26 };
        // 鍵盤：轉軸自己轉到下一格；手把：轉軸停在搖桿轉到的地方
        reelPos = padMode ? crank : Mathf.Lerp(reelPos, progress, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
        const float panelW = 320f, panelH = 214f;
        var panel = new Rect((sw - panelW) * 0.5f, sh * 0.6f - panelH * 0.5f, panelW, panelH);
        GameUI.Panel(panel);
        string title = padMode ? "轉左搖桿收線，轉到框裡按對應的鈕！" : digits.Length >= 8 ? "上鉤了！是條大魚——快收線！" : "上鉤了！快收線！";
        GameUI.Shadowed(new Rect(panel.x, panel.y + 6f, panel.width, 26f), title, titleStyle, GameUI.Accent);

        // 轉軸：中間是捲著線的木頭圓筒（橫條的明暗表現圓柱），兩邊是比較高的擋板
        const float drumW = 120f, drumH = 112f;
        var drum = new Rect(panel.center.x - drumW * 0.5f, panel.y + 38f, drumW, drumH);
        Color wood = new Color(.55f, .38f, .22f), woodDark = new Color(.25f, .16f, .09f), line = new Color(.85f, .82f, .72f);
        const int strips = 16;
        for (int k = 0; k < strips; k++)
        {
            float a0 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (float)k / strips), a1 = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, (k + 1f) / strips);
            float y0 = drum.center.y + Mathf.Sin(a0) * drumH * 0.5f, y1 = drum.center.y + Mathf.Sin(a1) * drumH * 0.5f;
            float lit = Mathf.Cos((a0 + a1) * 0.5f);
            // 線一圈圈捲在上面：跟著轉軸轉的亮線
            float wrap = Mathf.Repeat((a0 + a1) * 0.5f / (Mathf.PI / 9f) + reelPos * 3f, 1f);
            Color c = Color.Lerp(woodDark, Color.Lerp(wood, line, wrap < 0.35f ? 0.55f : 0.2f), 0.35f + 0.65f * lit);
            GUI.color = c;
            GUI.DrawTexture(new Rect(drum.x, y0, drum.width, Mathf.Max(1f, y1 - y0 + 0.5f)), Texture2D.whiteTexture);
        }
        GUI.color = woodDark;
        GUI.DrawTexture(new Rect(drum.x - 12f, drum.y - 8f, 12f, drum.height + 16f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(drum.xMax, drum.y - 8f, 12f, drum.height + 16f), Texture2D.whiteTexture);

        // 數字：沿著圓筒排，每一個差 40 度；越轉到側面越扁、越淡
        var saved = GUI.matrix;
        for (int i = Mathf.Max(0, progress - 2); i < Mathf.Min(digits.Length, progress + 3); i++)
        {
            float th = (i - reelPos) * 40f * Mathf.Deg2Rad;
            if (Mathf.Abs(th) > 1.4f) continue;
            float cs = Mathf.Cos(th);
            float y = drum.center.y + Mathf.Sin(th) * drumH * 0.5f;
            var r = new Rect(drum.center.x - 22f, y - 20f, 44f, 40f);
            GUI.matrix = saved;
            GUIUtility.ScaleAroundPivot(new Vector2(1f, Mathf.Max(0.05f, cs)), new Vector2(r.center.x, r.center.y));
            bool current = i == progress, done = i < progress;
            if (current && !padMode)
            {
                float pop = 1f - Mathf.Clamp01((Time.time - digitShownAt) / 0.15f);
                GUI.color = new Color(0f, 0f, 0f, .6f);
                GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
                GUI.color = Color.Lerp(GameUI.Accent, Color.white, pop * 0.5f + 0.15f * Mathf.Sin(Time.time * 12f));
                GUI.DrawTexture(r, Texture2D.whiteTexture);
            }
            if (padMode)
            {
                // 手把：圓形的按鈕（A 綠、B 紅、X 藍、Y 黃），按過的變淡
                Color bc = PadColors[digits[i]];
                GUI.color = new Color(bc.r, bc.g, bc.b, (done ? 0.35f : 1f) * Mathf.Max(0.2f, cs));
                GUI.DrawTexture(new Rect(r.center.x - 17f, r.center.y - 17f, 34f, 34f), Dot);
                GUI.color = new Color(1f, 1f, 1f, (done ? 0.5f : 1f) * cs);
                reelDigitStyle.normal.textColor = Color.white;
                GUI.Label(r, PadLabels[digits[i]], reelDigitStyle);
            }
            else
            {
                GUI.color = current ? new Color(.1f, .07f, .04f, 1f) : done ? new Color(.45f, .85f, .5f, cs * cs) : new Color(.95f, .9f, .8f, cs * cs);
                reelDigitStyle.normal.textColor = Color.white;
                GUI.Label(r, digits[i].ToString(), reelDigitStyle);
            }
        }
        GUI.matrix = saved;
        if (padMode)
        {
            // 正面的框：要按的那一格轉進來時框變亮（還沒轉到就按：框抖一下）
            bool inWindow = progress < digits.Length && Mathf.Abs(crank - progress) <= 0.2f;
            float shake = Mathf.Clamp01(1f - (Time.time - digitShownAt) / 0.12f) * (Time.time - digitShownAt < 0.12f && !inWindow ? 4f : 0f);
            var win = new Rect(drum.x - 4f + Mathf.Sin(Time.time * 60f) * shake, drum.center.y - 24f, drum.width + 8f, 48f);
            GUI.color = inWindow ? new Color(1f, 0.85f, 0.4f, 0.9f + 0.1f * Mathf.Sin(Time.time * 14f)) : new Color(1f, 1f, 1f, 0.35f);
            GUI.DrawTexture(new Rect(win.x, win.y, win.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(win.x, win.yMax - 3f, win.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(win.x, win.y, 3f, win.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(win.xMax - 3f, win.y, 3f, win.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // 搖柄：右邊擋板中間一個軸心，一根柄跟著轉軸轉，柄的末端一顆握把
        Vector2 hub = new Vector2(drum.xMax + 6f, drum.center.y);
        GUIUtility.RotateAroundPivot(reelPos * 120f, hub);
        GUI.color = new Color(.35f, .24f, .14f);
        GUI.DrawTexture(new Rect(hub.x - 3f, hub.y - 3f, 34f, 6f), Texture2D.whiteTexture);
        GUI.color = new Color(.7f, .55f, .35f);
        GUI.DrawTexture(new Rect(hub.x + 26f, hub.y - 7f, 12f, 14f), Texture2D.whiteTexture);
        GUI.matrix = saved;
        GUI.color = woodDark;
        GUI.DrawTexture(new Rect(hub.x - 5f, hub.y - 5f, 10f, 10f), Texture2D.whiteTexture);

        // 進度：一排小方格
        const float pip = 9f, pipGap = 5f;
        float rowW = digits.Length * pip + (digits.Length - 1) * pipGap;
        float px = panel.center.x - rowW * 0.5f, py = drum.yMax + 14f;
        for (int i = 0; i < digits.Length; i++)
        {
            var q = new Rect(px + i * (pip + pipGap), py, pip, pip);
            GUI.color = new Color(0f, 0f, 0f, .7f);
            GUI.DrawTexture(new Rect(q.x - 1f, q.y - 1f, q.width + 2f, q.height + 2f), Texture2D.whiteTexture);
            GUI.color = i < progress ? new Color(.35f, .8f, .4f, 1f) : i == progress ? GameUI.Accent : new Color(.55f, .52f, .48f, .9f);
            GUI.DrawTexture(q, Texture2D.whiteTexture);
        }
        GUI.color = Color.white;

        // 時間條：緩衝時間內是滿的，之後越來越短，剩不到三成變紅
        float left = 1f - Mathf.Clamp01((Time.time - biteStart - graceTime) / biteSeconds);
        var bar = new Rect(panel.x + 24f, panel.yMax - 26f, panel.width - 48f, 8f);
        GameUI.ProgressBar(bar, left, left < 0.3f ? GameUI.Danger : GameUI.Accent);
        GUI.color = GameUI.MutedColor;
        if (!padMode) GUI.Label(new Rect(panel.x, panel.yMax - 18f, panel.width - 12f, 18f), $"{InteractPrompt.KeyName} 放棄", new GUIStyle(GameUI.SmallRight) { fontSize = 12 });
        GUI.color = Color.white;
    }
}

/// <summary>水面上一圈往外擴散、慢慢變淡的漣漪</summary>
public sealed class FishingRipple : MonoBehaviour
{
    static Material material;
    LineRenderer ring;
    float t, size;
    const float Life = 0.9f;

    public static void Spawn(Vector3 at, float size)
    {
        var go = new GameObject("FishingRipple");
        go.transform.position = at;
        var r = go.AddComponent<FishingRipple>();
        r.size = size;
        r.ring = go.AddComponent<LineRenderer>();
        r.ring.useWorldSpace = false;
        r.ring.loop = true;
        r.ring.positionCount = 28;
        r.ring.widthMultiplier = 0.025f;
        r.ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (material == null) material = new Material(Shader.Find("Sprites/Default")) { name = "Runtime_FishingRipple" };
        r.ring.sharedMaterial = material;
        r.Update();
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / Life);
        float radius = Mathf.Lerp(0.06f, size, Mathf.Sqrt(k));
        for (int i = 0; i < ring.positionCount; i++)
        {
            float a = i * Mathf.PI * 2f / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
        }
        ring.startColor = ring.endColor = new Color(1f, 1f, 1f, 0.6f * (1f - k));
        if (k >= 1f) Destroy(gameObject);
    }
}
