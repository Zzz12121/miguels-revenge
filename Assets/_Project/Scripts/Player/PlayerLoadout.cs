using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>玩家可以在鐵匠鋪換的武器組合</summary>
public enum WeaponLoadout
{
    /// <summary>大刀（原本的武器）：橫掃、範圍大，可以舉盾</summary>
    Greatsword,
    /// <summary>弓 + 小刀：右鍵瞄準、左鍵射箭，箭有 MaxArrows 支；射完只能用小刀（距離短、出手快）</summary>
    BowKnife,
    /// <summary>長槍：戳擊，打得最遠、範圍窄</summary>
    Spear,
}

/// <summary>
/// 玩家目前帶的武器（鐵匠鋪換）。自動掛在每個場景的玩家身上，選的武器和剩下的箭跨場景保留（只存在記憶體，不寫存檔）。
/// 不改戰鬥的程式，只調 PlayerCombat 現成的欄位：
///   - 判定的距離 / 範圍（hitForwardOffset、hitRadius）、每段的出手時間（stepWindup / stepSwing / stepRecovery）、傷害倍率（weaponDamageScale）
///   - 弓：打開 PlayerCombat.bowMode（和維京村莊海戰同一套：右鍵瞄準、左鍵射箭），每射一支扣一支，射完自動換回近戰（小刀）
///     右鍵按住瞄準：先從腰間的箭筒抽一支箭搭上弦，接著蓄力——畫面中央的大圓往內收，收到跟小圓重合時放箭最準、威力最大，
///     太早或太晚放都會變差；沒瞄準直接射（左鍵）會亂飄。射出去的箭換成 Resources/Weapons/JV_Arrow（真的箭的樣子）
///   - 外觀：小刀、長槍換掉手上的大刀模型（交給 PlayerWeaponPresenter 一樣聚合 / 散開）；弓換成 Resources/Weapons/JV_Bow
///   - 長槍出招時把右手往前伸、槍頭朝前（戳擊）；小刀有自己的三段招式（直刺、反手橫斬、反握下刺），都蓋在原本揮刀的動作上
/// 在有 VikingBoat 的關卡（船戰）不碰弓箭模式，交給船自己控制。
/// 打擊回饋：打中東西（近戰、盾擊、箭）時鏡頭輕微震一下、畫面頓一下（頓幀），有手把的話手把也震一下。
/// </summary>
[DefaultExecutionOrder(32000)] // 在 PlayerWeaponPresenter（31000）、PlayerArcherBow 擺完之後
[DisallowMultipleComponent]
public class PlayerLoadout : MonoBehaviour
{
    public const int MaxArrows = 20;
    const float ArrowDamage = 14f;

    public static WeaponLoadout Current { get; private set; } = WeaponLoadout.Greatsword;
    public static int Arrows { get; private set; } = MaxArrows;
    /// <summary>換了武器、或箭的數量變了</summary>
    public static event System.Action Changed;
    public static PlayerLoadout Instance { get; private set; }

    /// <summary>現在近戰用的是小刀（弓的箭射完了）</summary>
    public static bool UsingKnife => Current == WeaponLoadout.BowKnife && Arrows <= 0;
    public static bool HasArrows => Current == WeaponLoadout.BowKnife && Arrows > 0;

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Current = WeaponLoadout.Greatsword;
        Arrows = MaxArrows;
        Changed = null;
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (s, m) => Attach();
        }
        Attach();
    }

    static void Attach()
    {
        foreach (var c in FindObjectsByType<PlayerCombat>(FindObjectsSortMode.None))
            if (c.GetComponent<PlayerHealth>() != null && c.GetComponent<PlayerLoadout>() == null)
                c.gameObject.AddComponent<PlayerLoadout>();
    }

    /// <summary>換武器。換成弓的時候箭補滿</summary>
    public static void Equip(WeaponLoadout loadout)
    {
        Current = loadout;
        if (loadout == WeaponLoadout.BowKnife) Arrows = MaxArrows;
        if (Instance != null) Instance.Apply();
        Changed?.Invoke();
    }

    // ---------------- 各武器的數值（相對原本大刀的判定 / 出手時間） ----------------

    enum Melee { Greatsword, Knife, Spear }

    struct Stats
    {
        public float offset, radius, time, damage;
        public Stats(float offset, float radius, float time, float damage) { this.offset = offset; this.radius = radius; this.time = time; this.damage = damage; }
    }

    // 原本：判定中心在前方 1.3 m、半徑 1.4 m（最遠 2.7 m）
    static readonly Stats GreatswordStats = new Stats(1.25f, 1.15f, 1f, 1f);   // 前方 1.6 m、半徑 1.6 m：最遠約 3.2 m，比原本多 0.5 m
    static readonly Stats KnifeStats = new Stats(0.72f, 0.75f, 0.6f, 0.7f);    // 最遠約 2 m；出手快 40%、每下傷害 70%
    static readonly Stats SpearStats = new Stats(1.85f, 0.85f, 1.05f, 1f);     // 前方 2.4 m、半徑 1.2 m：1.2 ~ 3.6 m，左右窄

    static Stats StatsFor(Melee m) => m == Melee.Knife ? KnifeStats : m == Melee.Spear ? SpearStats : GreatswordStats;

    static Melee MeleeFor(WeaponLoadout w) => w == WeaponLoadout.Spear ? Melee.Spear : w == WeaponLoadout.BowKnife ? Melee.Knife : Melee.Greatsword;

    // ---------------- 玩家身上 ----------------

    PlayerCombat combat;
    PlayerWeaponPresenter presenter;
    PlayerArcherBow archerBow;
    Animator animator;
    bool boatLevel, managingBow;

    float baseOffset, baseRadius;
    bool baseCaptured;
    float[] baseWindup, baseSwing, baseRecovery, writtenWindup;

    Renderer[] saberRenderers;
    Transform tip;
    Vector3 tipRest;
    bool tipCaptured;
    readonly Dictionary<Melee, GameObject> weapons = new Dictionary<Melee, GameObject>();
    readonly Dictionary<Melee, Vector3> weaponTips = new Dictionary<Melee, Vector3>();
    Melee shownMelee = Melee.Greatsword;

    GameObject tripoBow;
    Renderer erikaBow;

    float attackStart = -1f, swingAt = -1f, thrustWeight;
    Vector3 spearRestPos, knifeRestPos;
    Quaternion spearRestRot, knifeRestRot;
    float knifeWeight;
    Vector3 knifeUpper, knifeFore, knifeBlade;
    float knifeLean, knifeTwist;
    Transform spine;
    Texture2D bowIcon, arrowIcon, knifeIcon;
    int swingIndex;
    Transform upperArm, foreArm, hand;
    float notice = -1f;
    GUIStyle hudStyle;

    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    void Awake()
    {
        Instance = this;
        combat = GetComponent<PlayerCombat>();
        presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        archerBow = GetComponent<PlayerArcherBow>();
        combat.OnSwing += i => { swingAt = Time.time; swingIndex = i; };
        combat.OnBowShot += OnBowShot;
        combat.OnDealtHit += OnDealtHit;
        combat.OnArrowSpawned += OnArrowSpawned;
    }

    // ---------------- 打擊回饋 ----------------

    float hitStopUntil, rumbleUntil = -1f;

    void OnDealtHit(float damage, IDamageable target)
    {
        float k = Mathf.Clamp01(damage / 30f);
        ThirdPersonCamera.ShakeMain(0.05f + 0.07f * k, 0.12f);
        // 頓幀：一下打中好幾個也只頓一次；別的東西正在改時間（暫停、慢動作）就不頓
        if (Time.unscaledTime >= hitStopUntil && Time.timeScale >= 0.99f)
        {
            float stop = 0.04f + 0.03f * k;
            hitStopUntil = Time.unscaledTime + stop + 0.05f;
            StartCoroutine(HitStop(stop));
        }
        var pad = Gamepad.current;
        if (pad != null && InputBindings.PadEnabled)
        {
            pad.SetMotorSpeeds(0.15f + 0.25f * k, 0.3f + 0.4f * k);
            rumbleUntil = Time.unscaledTime + 0.1f + 0.06f * k;
        }
    }

    const float HitStopScale = 0.05f;

    System.Collections.IEnumerator HitStop(float seconds)
    {
        Time.timeScale = HitStopScale;
        yield return new WaitForSecondsRealtime(seconds);
        if (Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f;
    }

    void Update()
    {
        if (rumbleUntil >= 0f && Time.unscaledTime >= rumbleUntil)
        {
            rumbleUntil = -1f;
            Gamepad.current?.SetMotorSpeeds(0f, 0f);
        }
    }

    void OnDisable()
    {
        if (Mathf.Approximately(Time.timeScale, HitStopScale)) Time.timeScale = 1f;
        if (rumbleUntil >= 0f) Gamepad.current?.SetMotorSpeeds(0f, 0f);
        rumbleUntil = -1f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        boatLevel = FindFirstObjectByType<VikingBoat>() != null;
        Apply();
    }

    float baseHipSpread, baseArrowSpeed;

    void CaptureBase()
    {
        if (baseCaptured) return;
        baseCaptured = true;
        baseOffset = combat.hitForwardOffset;
        baseRadius = combat.hitRadius;
        baseHipSpread = combat.hipFireSpread;
        baseArrowSpeed = combat.arrowSpeed;
    }

    /// <summary>依目前的武器設定判定、出手時間、外觀、弓箭模式</summary>
    void Apply()
    {
        CaptureBase();
        var melee = MeleeFor(Current);
        var s = StatsFor(melee);
        combat.hitForwardOffset = baseOffset * s.offset;
        combat.hitRadius = baseRadius * s.radius;
        combat.weaponDamageScale = s.damage;
        combat.arrowDamage = Current == WeaponLoadout.BowKnife ? ArrowDamage : 0f;
        ApplyTiming();

        if (!boatLevel)
        {
            bool wantBow = HasArrows;
            if (wantBow != combat.bowMode)
            {
                if (!wantBow) StopAiming();
                combat.bowMode = wantBow;
            }
            managingBow = wantBow;
            if (!wantBow) ResetBowTuning();
        }
        ShowMelee(melee);
    }

    /// <summary>每段出手時間 = 原本（PlayerVisualAnimator 設定的）× 武器的倍率；別人重新設定過就以新的為準</summary>
    void ApplyTiming()
    {
        if (combat.stepWindup == null || combat.stepSwing == null || combat.stepRecovery == null) return;
        if (combat.stepWindup != writtenWindup)
        {
            baseWindup = (float[])combat.stepWindup.Clone();
            baseSwing = (float[])combat.stepSwing.Clone();
            baseRecovery = (float[])combat.stepRecovery.Clone();
        }
        float k = StatsFor(MeleeFor(Current)).time;
        var w = new float[baseWindup.Length];
        var sw = new float[baseSwing.Length];
        var r = new float[baseRecovery.Length];
        for (int i = 0; i < w.Length; i++) w[i] = baseWindup[i] * k;
        for (int i = 0; i < sw.Length; i++) sw[i] = baseSwing[i] * k;
        for (int i = 0; i < r.Length; i++) r[i] = baseRecovery[i] * k;
        combat.stepWindup = writtenWindup = w;
        combat.stepSwing = sw;
        combat.stepRecovery = r;
    }

    void OnBowShot()
    {
        if (!managingBow) return;
        Arrows = Mathf.Max(0, Arrows - 1);
        if (Arrows == 0)
        {
            notice = Time.unscaledTime;
            GameAudio.Play("SFX/RPG/drawKnife2", transform.position, 0.8f, 1.1f, 0f, 0f);
            Apply(); // 箭射完：關掉弓箭模式，換成小刀
        }
        Changed?.Invoke();
    }

    /// <summary>還在瞄準時關掉弓箭模式：鏡頭、瞄準狀態要一起收回來（PlayerCombat 的瞄準只在弓箭模式裡更新）</summary>
    void StopAiming()
    {
        if (!combat.IsAimingBow) return;
        var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.SetAiming(false, transform);
        typeof(PlayerCombat).GetField("<IsAimingBow>k__BackingField", Private)?.SetValue(combat, false);
        var evt = typeof(PlayerCombat).GetField("OnBowAim", Private)?.GetValue(combat) as System.Action<bool>;
        evt?.Invoke(false);
    }

    // ---------------- 外觀：近戰武器 ----------------

    void ShowMelee(Melee melee)
    {
        if (presenter == null || presenter.BladePivot == null) return;
        var pivot = presenter.BladePivot;
        var field = typeof(PlayerWeaponPresenter).GetField("weaponRenderers", Private);
        if (saberRenderers == null)
        {
            // 第一次：記下原本大刀的網格（PlayerWeaponPresenter 自己也會找一次，這裡用同樣的規則）
            var list = new List<Renderer>();
            foreach (var r in pivot.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer) && !(r is LineRenderer)) list.Add(r);
            saberRenderers = list.ToArray();
            tip = presenter.BladeTip;
        }
        if (tip != null && !tipCaptured)
        {
            tipCaptured = true;
            tipRest = tip.localPosition;
        }

        bool drawn = presenter.IsDrawn;
        GameObject want = melee == Melee.Greatsword ? null : GetWeapon(melee, pivot);
        foreach (var kv in weapons) if (kv.Value != null) kv.Value.SetActive(kv.Key == melee);
        foreach (var r in saberRenderers)
        {
            if (r == null) continue;
            bool on = want == null && drawn;
            r.enabled = on;
            r.forceRenderingOff = !on;
        }

        Renderer[] shown = saberRenderers;
        if (want != null)
        {
            shown = want.GetComponentsInChildren<Renderer>(true);
            foreach (var r in shown) { r.enabled = drawn; r.forceRenderingOff = !drawn; }
        }
        field?.SetValue(presenter, shown); // 聚合 / 散開、溶解效果都改成作用在現在的武器上
        if (tip != null) tip.localPosition = want != null && weaponTips.TryGetValue(melee, out var t) ? t : tipRest;
        shownMelee = melee;
    }

    GameObject GetWeapon(Melee melee, Transform pivot)
    {
        if (weapons.TryGetValue(melee, out var existing) && existing != null) return existing;
        var prefab = Resources.Load<GameObject>(melee == Melee.Knife ? "Weapons/JV_Knife" : "Weapons/JV_Spear");
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerLoadout] 找不到 Resources/Weapons 裡的 {melee}（日式村莊的 Builder 會建立），先用原本的大刀");
            return null;
        }
        var go = Instantiate(prefab, pivot, false);
        go.name = $"Loadout_{melee}";
        foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = pivot.gameObject.layer;

        // 武器模型沿 +Y（握把在原點）；轉成跟原本刀身同一個方向（握把 → 刀尖）
        Vector3 bladeDir = tip != null ? pivot.InverseTransformPoint(tip.parent.TransformPoint(tipRest)).normalized : Vector3.forward;
        if (bladeDir.sqrMagnitude < 0.5f) bladeDir = Vector3.forward;
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, bladeDir);
        go.transform.localScale = Vector3.one / Mathf.Max(0.0001f, pivot.lossyScale.x); // 模型是公尺，掛點可能有縮放
        if (melee == Melee.Spear) { spearRestPos = go.transform.localPosition; spearRestRot = go.transform.localRotation; }
        if (melee == Melee.Knife) { knifeRestPos = go.transform.localPosition; knifeRestRot = go.transform.localRotation; }
        var tipMarker = go.transform.Find("Tip");
        if (tip != null && tipMarker != null) weaponTips[melee] = tip.parent.InverseTransformPoint(tipMarker.position);
        weapons[melee] = go;
        return go;
    }

    // ---------------- 每幀 ----------------

    void LateUpdate()
    {
        if (combat == null) return;
        if (combat.stepWindup != null && combat.stepWindup != writtenWindup) ApplyTiming(); // PlayerVisualAnimator 重新設定過
        // 換關卡 / 船戰之後弓箭模式被別人關掉了：恢復成小刀以外的狀態一致
        if (!boatLevel && managingBow && !combat.bowMode) managingBow = false;

        var melee = MeleeFor(Current);
        if (melee != shownMelee) ShowMelee(melee);
        TrackAttack(melee);
        UpdateSpearThrust(melee);
        UpdateKnifeMoves(melee);
        UpdateBowModel();
        UpdateArrowDraw();
    }

    // ---------------- 出招的進度 ----------------

    bool attacking;

    void TrackAttack(Melee melee)
    {
        attacking = melee != Melee.Greatsword && combat.IsAttacking && !combat.IsAirSlash && !combat.IsPlunging && !combat.IsDashSlash;
        if (attacking && attackStart < 0f) attackStart = Time.time;
        if (!attacking) attackStart = -1f;
        if (animator == null) animator = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman && upperArm == null)
        {
            upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            foreArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            spine = animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine);
        }
    }

    /// <summary>
    /// 這一段出招進行到哪：OnSwing 在抬手「之前」發出，之後依序是抬手（windup）、出手（swing）、收招（recovery）。
    /// pull / strike / recover 都是 0 ~ 1。還沒出第一招時回傳 false。
    /// </summary>
    bool Phase(out int index, out float pull, out float strike, out float recover)
    {
        index = swingIndex;
        pull = strike = recover = 0f;
        if (!attacking || swingAt < attackStart) return false;
        float t = Time.time - swingAt;
        float wu = Mathf.Max(0.02f, combat.WindupFor(index)), sw = Mathf.Max(0.02f, combat.SwingFor(index));
        float rec = combat.stepRecovery != null && index < combat.stepRecovery.Length ? combat.stepRecovery[index] : combat.recoveryTime;
        if (t < wu) { pull = Mathf.SmoothStep(0f, 1f, t / wu); return true; }
        pull = 1f;
        t -= wu;
        if (t < sw) { strike = Mathf.SmoothStep(0f, 1f, t / sw); return true; }
        strike = 1f;
        recover = Mathf.Clamp01((t - sw) / Mathf.Max(0.05f, rec));
        return true;
    }

    /// <summary>刀尖 / 槍尖的標記（拖尾特效用）跟著換過位置的武器</summary>
    void FollowTip(GameObject weapon)
    {
        if (tip == null) return;
        var marker = weapon.transform.Find("Tip");
        if (marker != null) tip.position = marker.position;
    }

    /// <summary>+Y 朝 dir 的旋轉（武器模型都沿 +Y）；flat = 刀面朝哪邊</summary>
    static Quaternion AlongY(Vector3 dir, Vector3 flat) =>
        Quaternion.LookRotation(dir.normalized, flat.sqrMagnitude > 1e-6f ? flat : Vector3.up) * Quaternion.Euler(90f, 0f, 0f);

    /// <summary>長槍：抬手時右手往後收，出手時往前伸直、槍頭朝前，收招時收回來</summary>
    void UpdateSpearThrust(Melee melee)
    {
        thrustWeight = Mathf.MoveTowards(thrustWeight, melee == Melee.Spear && attacking ? 1f : 0f, Time.deltaTime * 10f);
        if (!weapons.TryGetValue(Melee.Spear, out var spear) || spear == null) return;
        // 每幀先回到握在手上的樣子，再依出招的程度疊上戳擊
        spear.transform.SetLocalPositionAndRotation(spearRestPos, spearRestRot);
        if (thrustWeight <= 0.001f || melee != Melee.Spear) return;

        Phase(out _, out float pull, out float strike, out float recover);
        float extend = strike * (1f - recover);
        pull *= 1f - strike;

        Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
        float w = thrustWeight;
        if (upperArm != null && foreArm != null && hand != null)
        {
            Vector3 rest = -up * 0.6f + fwd * 0.5f + right * 0.3f;
            Vector3 back = -up * 0.8f - fwd * 0.45f + right * 0.35f;
            Vector3 reach = fwd - up * 0.12f + right * 0.08f;
            Vector3 armDir = extend > 0f ? Vector3.Slerp(Vector3.Slerp(rest, back, 1f - recover).normalized, reach.normalized, extend) : Vector3.Slerp(rest.normalized, back.normalized, pull);
            Aim(upperArm, foreArm, armDir, w);
            Aim(foreArm, hand, fwd - up * 0.05f, w);
        }
        Vector3 handPos = hand != null ? hand.position : transform.position + up * 1.1f + fwd * 0.4f;
        Vector3 dir = (fwd - up * 0.06f).normalized;
        spear.transform.position = Vector3.Lerp(spear.transform.position, handPos + dir * (0.35f * extend - 0.2f * pull), w);  // 握把（原點）在手上
        spear.transform.rotation = Quaternion.Slerp(spear.transform.rotation, AlongY(dir, up), w);
        FollowTip(spear);
    }

    /// <summary>小刀一招的姿勢：上臂、前臂、刀身的方向（角色座標：x = 右、y = 上、z = 前），身體前傾 / 扭轉的角度</summary>
    struct KnifePose
    {
        public Vector3 upper, fore, blade, flat;
        public float lean, twist;
        public KnifePose(Vector3 upper, Vector3 fore, Vector3 blade, Vector3 flat, float lean, float twist)
        { this.upper = upper; this.fore = fore; this.blade = blade; this.flat = flat; this.lean = lean; this.twist = twist; }
    }

    // 小刀的三段招式：(抬手姿勢, 出手姿勢)
    static readonly KnifePose[][] KnifeMoves =
    {
        // 1. 直刺：手收在腰間，往前一刺到底
        new[]
        {
            new KnifePose(new Vector3(0.3f, -0.7f, -0.5f), new Vector3(0f, 0.1f, 1f), new Vector3(0f, 0f, 1f), Vector3.up, -4f, -18f),
            new KnifePose(new Vector3(0.05f, -0.05f, 1f), new Vector3(0f, 0f, 1f), new Vector3(0f, -0.05f, 1f), Vector3.up, 10f, 22f),
        },
        // 2. 反手橫斬：手收到左肩前，往右橫掃過胸前
        new[]
        {
            new KnifePose(new Vector3(-0.6f, 0.15f, 0.6f), new Vector3(-0.9f, 0.2f, 0.2f), new Vector3(-1f, 0.05f, 0.3f), Vector3.up, 4f, -32f),
            new KnifePose(new Vector3(0.8f, 0f, 0.5f), new Vector3(0.9f, 0f, 0.4f), new Vector3(0.8f, -0.05f, 0.6f), Vector3.up, 6f, 28f),
        },
        // 3. 反握下刺：刀反握高舉過頭，往前下方扎下去
        new[]
        {
            new KnifePose(new Vector3(0.2f, 0.9f, 0.25f), new Vector3(0f, 0.7f, 0.3f), new Vector3(0f, -0.5f, 0.85f), Vector3.forward, -10f, -8f),
            new KnifePose(new Vector3(0.15f, -0.35f, 0.9f), new Vector3(0f, -0.6f, 0.8f), new Vector3(0f, -0.85f, 0.5f), Vector3.forward, 24f, 6f),
        },
    };

    Vector3 ToWorld(Vector3 v) => (transform.right * v.x + Vector3.up * v.y + transform.forward * v.z).normalized;

    /// <summary>小刀：自己的三段招式，覆蓋原本揮大刀的手臂動作（腳步、身體位移照原本的）</summary>
    void UpdateKnifeMoves(Melee melee)
    {
        if (!weapons.TryGetValue(Melee.Knife, out var knife) || knife == null) return;
        knife.transform.SetLocalPositionAndRotation(knifeRestPos, knifeRestRot);

        float target = 0f;
        KnifePose pose = default;
        if (melee == Melee.Knife && Phase(out int index, out float pull, out float strike, out float recover))
        {
            var move = KnifeMoves[index % KnifeMoves.Length];
            target = (strike > 0f ? 1f : pull) * (1f - recover * 0.85f);
            float k = strike;
            pose = new KnifePose(
                Vector3.Slerp(move[0].upper.normalized, move[1].upper.normalized, k),
                Vector3.Slerp(move[0].fore.normalized, move[1].fore.normalized, k),
                Vector3.Slerp(move[0].blade.normalized, move[1].blade.normalized, k),
                k < 0.5f ? move[0].flat : move[1].flat,
                Mathf.Lerp(move[0].lean, move[1].lean, k),
                Mathf.Lerp(move[0].twist, move[1].twist, k));
        }
        knifeWeight = Mathf.MoveTowards(knifeWeight, target, Time.deltaTime * 12f);
        if (knifeWeight <= 0.001f) return;

        // 姿勢平滑一點（換下一招時不要瞬間跳過去）
        float a = 1f - Mathf.Exp(-60f * Time.deltaTime);
        if (target > 0f)
        {
            if (knifeUpper == Vector3.zero) { knifeUpper = pose.upper; knifeFore = pose.fore; knifeBlade = pose.blade; }
            knifeUpper = Vector3.Slerp(knifeUpper, pose.upper, a);
            knifeFore = Vector3.Slerp(knifeFore, pose.fore, a);
            knifeBlade = Vector3.Slerp(knifeBlade, pose.blade, a);
            knifeLean = Mathf.Lerp(knifeLean, pose.lean, a);
            knifeTwist = Mathf.Lerp(knifeTwist, pose.twist, a);
        }

        float w = knifeWeight;
        if (spine != null)
            spine.rotation = Quaternion.AngleAxis(knifeTwist * w, Vector3.up) * Quaternion.AngleAxis(knifeLean * w, transform.right) * spine.rotation;
        if (upperArm != null && foreArm != null && hand != null)
        {
            Aim(upperArm, foreArm, ToWorld(knifeUpper), w);
            Aim(foreArm, hand, ToWorld(knifeFore), w);
        }
        if (hand != null)
        {
            Vector3 flat = pose.flat == Vector3.forward ? transform.forward : Vector3.up;
            knife.transform.position = Vector3.Lerp(knife.transform.position, hand.position, w);
            knife.transform.rotation = Quaternion.Slerp(knife.transform.rotation, AlongY(ToWorld(knifeBlade), flat), w);
        }
        FollowTip(knife);
    }

    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir.normalized), w) * bone.rotation;
    }

    // ---------------- 外觀：弓 ----------------

    /// <summary>
    /// 弓箭模式時 PlayerArcherBow 會把維京船戰的弓（Erika 的弓）掛在左手；帶的是鐵匠鋪的弓時，
    /// 把那把弓藏起來、換成 Resources/Weapons/JV_Bow（擺在同一個位置、同一個方向、同樣長）。
    /// </summary>
    void UpdateBowModel()
    {
        if (archerBow == null) archerBow = GetComponent<PlayerArcherBow>();
        if (archerBow == null || !archerBow.Ready) return;
        bool want = Current == WeaponLoadout.BowKnife && !boatLevel;
        var mount = typeof(PlayerArcherBow).GetField("mount", Private)?.GetValue(archerBow) as Transform;
        var archer = typeof(PlayerArcherBow).GetField("archer", Private)?.GetValue(archerBow) as GameObject;
        if (mount == null || archer == null) return;

        if (erikaBow == null)
            foreach (var smr in archer.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var m in smr.sharedMaterials)
                    if (erikaBow == null && m != null && m.name.StartsWith(archerBow.bowMaterialName)) erikaBow = smr;
        if (erikaBow == null) return;

        if (want && tripoBow == null && mount.gameObject.activeInHierarchy) BuildTripoBow(mount, (SkinnedMeshRenderer)erikaBow);
        erikaBow.enabled = !(want && tripoBow != null);
        if (tripoBow != null) tripoBow.SetActive(want);
        if (want && tripoBow != null && tripoBow.activeInHierarchy) PlaceBow();
    }

    /// <summary>測試 / 調位置用：開著時弓一直擺成瞄準的樣子</summary>
    public static bool PreviewAimPose;

    float aimBlend;
    Transform leftHand;

    /// <summary>
    /// 弓的位置（左手握著弓的握把，弓的中心在手上）：
    ///   沒瞄準：弓斜斜地往前上方拿在身體左側，弓弦朝身體
    ///   瞄準中：弓直立在身體前方、稍微往右傾，弓背朝前（弓弦朝自己）
    /// </summary>
    void PlaceBow()
    {
        if (leftHand == null)
        {
            if (animator == null) animator = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman) leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand == null) return;
        }
        bool aiming = combat.IsAimingBow || PreviewAimPose;
        aimBlend = Mathf.MoveTowards(aimBlend, aiming ? 1f : 0f, Time.deltaTime * 8f);
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        var cam = Camera.main;
        if (aiming && cam != null)
        {
            Vector3 cf = cam.transform.forward;
            cf.y = 0f;
            if (cf.sqrMagnitude > 0.01f) fwd = cf.normalized;
            side = Vector3.Cross(up, fwd);
        }
        // JV_Bow：弓身沿 +Y、弓背朝 +Z
        Quaternion carry = Quaternion.LookRotation(-side, (up * 0.85f + fwd * 0.45f).normalized);
        Quaternion draw = Quaternion.LookRotation(fwd, (up + side * 0.14f).normalized);
        tripoBow.transform.SetPositionAndRotation(leftHand.position + fwd * 0.04f * aimBlend, Quaternion.Slerp(carry, draw, aimBlend));
    }

    void BuildTripoBow(Transform mount, SkinnedMeshRenderer erika)
    {
        var prefab = Resources.Load<GameObject>("Weapons/JV_Bow");
        if (prefab == null) return;

        // Erika 的弓在掛點座標裡的形狀：主軸（弓身長的方向）、中心、長度
        var baked = new Mesh();
        erika.BakeMesh(baked, true);
        var toWorld = Matrix4x4.TRS(erika.transform.position, erika.transform.rotation, Vector3.one);
        var verts = baked.vertices;
        Destroy(baked);
        if (verts.Length == 0) return;
        var pts = new Vector3[verts.Length];
        Vector3 c = Vector3.zero;
        for (int i = 0; i < verts.Length; i++) { pts[i] = mount.InverseTransformPoint(toWorld.MultiplyPoint3x4(verts[i])); c += pts[i]; }
        c /= pts.Length;
        Vector3 axis = PrincipalAxis(pts, c);
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in pts) { float d = Vector3.Dot(p - c, axis); lo = Mathf.Min(lo, d); hi = Mathf.Max(hi, d); }
        float length = hi - lo;
        c += axis * ((lo + hi) * 0.5f);

        tripoBow = Instantiate(prefab, mount, false);
        tripoBow.name = "Loadout_Bow";
        foreach (var col in tripoBow.GetComponentsInChildren<Collider>(true)) Destroy(col);
        // JV_Bow：弓身沿 +Y、長 BowPrefabLength、弓背朝 +Z。弓背朝角色前方
        Quaternion toAxis = Quaternion.FromToRotation(Vector3.up, axis);
        Vector3 forwardLocal = Vector3.ProjectOnPlane(mount.InverseTransformDirection(transform.forward), axis);
        Vector3 backNow = toAxis * Vector3.forward;
        float twist = forwardLocal.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(backNow, forwardLocal, axis) : 0f;
        tripoBow.transform.localRotation = Quaternion.AngleAxis(twist, axis) * toAxis;
        tripoBow.transform.localPosition = c;
        tripoBow.transform.localScale = Vector3.one * (length / BowPrefabLength);
    }

    /// <summary>JV_Bow 的弓身長度（公尺，Builder 做 Prefab 時縮放成這個長度）</summary>
    public const float BowPrefabLength = 1.25f;

    static Vector3 PrincipalAxis(Vector3[] pts, Vector3 c)
    {
        // 共變異數矩陣的最大特徵向量（冪次法）
        float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
        foreach (var p0 in pts)
        {
            Vector3 p = p0 - c;
            xx += p.x * p.x; xy += p.x * p.y; xz += p.x * p.z; yy += p.y * p.y; yz += p.y * p.z; zz += p.z * p.z;
        }
        Vector3 v = new Vector3(0.3f, 1f, 0.2f).normalized;
        for (int i = 0; i < 32; i++)
        {
            v = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z);
            if (v.sqrMagnitude < 1e-12f) return Vector3.up;
            v.Normalize();
        }
        return v;
    }

    // ---------------- 弓：取箭、蓄力、箭的外觀 ----------------

    /// <summary>從腰間抽箭搭上弦要多久 (秒)</summary>
    public const float FetchTime = 0.5f;
    /// <summary>大圓從最大收到跟小圓重合要多久 (秒)</summary>
    public const float ChargeTime = 1.1f;
    const float BigRing = 70f, SmallRing = 12f; // 圓的半徑（像素，1080p）
    const float HipSpread = 9f, WorstAimSpread = 6f;

    bool wasAiming;
    float fetchStart = -1f, chargeStart = -1f;
    GameObject quiver, quiverArrows, handArrow;
    Texture2D ringTex;

    /// <summary>蓄力準不準：0 = 最差，1 = 大圓剛好跟小圓重合（最準、威力最大）</summary>
    public float ChargeQuality { get; private set; }
    /// <summary>正在從腰間抽箭</summary>
    public bool Fetching => fetchStart >= 0f;

    /// <summary>大圓現在的半徑（像素，1080p）：從 BigRing 往內收，ChargeTime 秒剛好收到 SmallRing，之後繼續收（太晚放）</summary>
    float RingRadius()
    {
        if (chargeStart < 0f) return BigRing;
        float t = (Time.time - chargeStart) / ChargeTime;
        return Mathf.Max(3f, SmallRing + (BigRing - SmallRing) * (1f - t));
    }

    void ResetBowTuning()
    {
        if (!baseCaptured) return;
        combat.hipFireSpread = baseHipSpread;
        combat.aimedSpread = 0f;
        combat.arrowSpeed = baseArrowSpeed;
        fetchStart = chargeStart = -1f;
        ChargeQuality = 0f;
        wasAiming = false;
        if (handArrow != null) handArrow.SetActive(false);
        if (quiver != null) quiver.SetActive(false);
    }

    void UpdateArrowDraw()
    {
        bool bow = managingBow && combat.bowMode && Current == WeaponLoadout.BowKnife;
        if (!bow)
        {
            if (quiver != null && quiver.activeSelf) quiver.SetActive(false);
            if (handArrow != null && handArrow.activeSelf) handArrow.SetActive(false);
            return;
        }
        EnsureQuiver();
        if (quiverArrows != null) quiverArrows.SetActive(Arrows > 1);

        bool aiming = combat.IsAimingBow || PreviewAimPose;
        if (aiming && !wasAiming) StartFetch();
        if (!aiming) { fetchStart = chargeStart = -1f; }
        wasAiming = aiming;

        if (fetchStart >= 0f && Time.time - fetchStart >= FetchTime)
        {
            fetchStart = -1f;
            chargeStart = Time.time; // 箭搭好了：開始蓄力，大圓往內收
        }

        // 準不準：大圓跟小圓差多少
        if (aiming && chargeStart >= 0f)
        {
            float diff = Mathf.Abs(RingRadius() - SmallRing) / (BigRing - SmallRing);
            ChargeQuality = Mathf.Clamp01(1f - diff * 1.6f);
        }
        else ChargeQuality = 0f;
        float q = ChargeQuality;
        combat.hipFireSpread = HipSpread;
        combat.aimedSpread = Mathf.Lerp(WorstAimSpread, 0f, q);
        combat.arrowDamage = aiming ? ArrowDamage * Mathf.Lerp(0.5f, 1.6f, q) : ArrowDamage * 0.6f;
        combat.arrowSpeed = baseArrowSpeed * (aiming ? Mathf.Lerp(0.75f, 1.2f, q) : 0.85f);

        PoseFetch(aiming);
    }

    void StartFetch()
    {
        if (Arrows <= 0) return;
        fetchStart = Time.time;
        chargeStart = -1f;
        GameAudio.Play("SFX/RPG/drawKnife1", transform.position, 0.45f, 1.6f, 0.1f, 1f, 15f);
    }

    void OnArrowSpawned(GameObject arrow)
    {
        // 換成真的箭：藏起原本的圓柱和方塊，掛上 JV_Arrow（模型沿 +Y，箭飛的方向是 +Z）
        var prefab = Resources.Load<GameObject>("Weapons/JV_Arrow");
        if (prefab != null && Current == WeaponLoadout.BowKnife && managingBow)
        {
            foreach (var r in arrow.GetComponentsInChildren<Renderer>()) r.enabled = false;
            var model = Instantiate(prefab, arrow.transform, false);
            model.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (var c in model.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        // 射出去了：還在瞄準的話再從腰間抽下一支
        if (handArrow != null) handArrow.SetActive(false);
        chargeStart = -1f;
        if (combat.IsAimingBow || PreviewAimPose) StartFetch();
    }

    void EnsureQuiver()
    {
        if (quiver != null) { if (!quiver.activeSelf) quiver.SetActive(true); return; }
        if (animator == null) animator = presenter != null ? presenter.GetComponentInChildren<Animator>(true) : GetComponentInChildren<Animator>(true);
        var hipsBone = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        var prefab = Resources.Load<GameObject>("Weapons/JV_Quiver");
        if (hipsBone == null || prefab == null) return;
        // 箭筒掛在右後腰：開口朝上、稍微往後斜（照角色的方向擺，再掛到骨盆上跟著動）
        quiver = Instantiate(prefab);
        quiver.name = "Loadout_Quiver";
        foreach (var c in quiver.GetComponentsInChildren<Collider>()) Destroy(c);
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        quiver.transform.SetPositionAndRotation(hipsBone.position + side * 0.2f - fwd * 0.16f - up * 0.05f,
            Quaternion.LookRotation(fwd, (up - fwd * 0.35f + side * 0.15f).normalized));
        quiver.transform.SetParent(hipsBone, true);
        quiverArrows = quiver.transform.Find("Arrows")?.gameObject;
        foreach (var t in quiver.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;

        var arrowPrefab = Resources.Load<GameObject>("Weapons/JV_Arrow");
        if (arrowPrefab != null)
        {
            handArrow = Instantiate(arrowPrefab);
            handArrow.name = "Loadout_HandArrow";
            foreach (var c in handArrow.GetComponentsInChildren<Collider>()) Destroy(c);
            handArrow.SetActive(false);
        }
    }

    /// <summary>
    /// 抽箭：右手往後伸到右腰的箭筒 → 抽出一支往上帶到弓弦（之後交給拉弓的動畫）。
    /// 抽出來的箭跟著右手；搭上弦以後擺在右手和弓之間、朝瞄準的方向。
    /// </summary>
    void PoseFetch(bool aiming)
    {
        if (animator == null || !animator.isHuman) return;
        if (rArmBone == null)
        {
            rArmBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            rForeBone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            rHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (leftHand == null) leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        }
        if (rArmBone == null || rHandBone == null) return;
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        var cam = Camera.main;
        Vector3 aimDir = cam != null ? cam.transform.forward : fwd;

        if (fetchStart >= 0f)
        {
            float f = Mathf.Clamp01((Time.time - fetchStart) / FetchTime);
            float w = f < 0.15f ? f / 0.15f : f > 0.8f ? (1f - f) / 0.2f : 1f;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.8f, f)); // 0 = 手在箭筒，1 = 手到弓弦
            Vector3 reachArm = -up * 0.55f - fwd * 0.55f + side * 0.5f, reachFore = -up * 0.85f - fwd * 0.3f + side * 0.1f;
            Vector3 nockArm = fwd * 0.55f + up * 0.05f + side * 0.2f, nockFore = fwd * 0.25f + up * 0.25f - side * 0.7f;
            Aim(rArmBone, rForeBone, Vector3.Slerp(reachArm.normalized, nockArm.normalized, k), w);
            Aim(rForeBone, rHandBone, Vector3.Slerp(reachFore.normalized, nockFore.normalized, k), w);
            if (handArrow != null)
            {
                handArrow.SetActive(f > 0.2f); // 手伸到箭筒才抽出來
                // 抽出來時箭頭朝上（從箭筒拔出），帶到弓弦時轉成朝前
                Vector3 dir = Vector3.Slerp((up - fwd * 0.3f).normalized, aimDir, k);
                handArrow.transform.SetPositionAndRotation(rHandBone.position + dir * 0.3f, Quaternion.LookRotation(dir, up) * Quaternion.Euler(90f, 0f, 0f));
            }
        }
        else if (aiming && chargeStart >= 0f && handArrow != null)
        {
            // 搭在弦上：箭尾在右手，箭頭朝瞄準的方向
            handArrow.SetActive(true);
            handArrow.transform.SetPositionAndRotation(rHandBone.position + aimDir * 0.4f, Quaternion.LookRotation(aimDir, up) * Quaternion.Euler(90f, 0f, 0f));
        }
        else if (handArrow != null && handArrow.activeSelf) handArrow.SetActive(false);
    }

    Transform rArmBone, rForeBone, rHandBone;

    Texture2D Ring()
    {
        if (ringTex != null) return ringTex;
        const int N = 256;
        ringTex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "AimRing" };
        var px = new Color32[N * N];
        float c = (N - 1) * 0.5f, r = N * 0.5f - 3f, half = 2.2f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float d = Mathf.Abs(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) - r);
                byte a = (byte)(Mathf.Clamp01(half + 0.8f - d) * 255f);
                px[y * N + x] = new Color32(255, 255, 255, a);
            }
        ringTex.SetPixels32(px);
        ringTex.Apply();
        return ringTex;
    }

    /// <summary>畫面中央的兩個圓：小圓固定、大圓往內收；重合時變成金色</summary>
    void DrawAimRings()
    {
        if (!managingBow || !(combat.IsAimingBow || PreviewAimPose) || Current != WeaponLoadout.BowKnife) return;
        float ui = Screen.height / 1080f;
        Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var tex = Ring();
        var prev = GUI.color;
        Color gold = GameUI.Accent;
        float q = ChargeQuality;
        // 小圓
        float rs = SmallRing * ui;
        GUI.color = new Color(gold.r, gold.g, gold.b, 0.95f);
        GUI.DrawTexture(new Rect(c.x - rs, c.y - rs, rs * 2f, rs * 2f), tex);
        // 大圓：還在抽箭時淡淡的，蓄力時越接近小圓越亮、越金
        if (fetchStart < 0f && chargeStart >= 0f || fetchStart >= 0f)
        {
            float rb = RingRadius() * ui;
            float alpha = fetchStart >= 0f ? 0.25f : Mathf.Lerp(0.5f, 1f, q);
            GUI.color = Color.Lerp(new Color(1f, 1f, 1f, alpha), new Color(gold.r, gold.g, gold.b, 1f), q * q);
            GUI.DrawTexture(new Rect(c.x - rb, c.y - rb, rb * 2f, rb * 2f), tex);
            if (q > 0.92f)
            {
                // 重合：外面再亮一圈
                float rg = rs * 1.35f;
                GUI.color = new Color(gold.r, gold.g, gold.b, (q - 0.92f) / 0.08f * 0.8f);
                GUI.DrawTexture(new Rect(c.x - rg, c.y - rg, rg * 2f, rg * 2f), tex);
            }
        }
        GUI.color = prev;
    }

    // ---------------- 畫面：箭的數量 ----------------

    void OnGUI()
    {
        if (Current != WeaponLoadout.BowKnife || boatLevel || GameUI.OverlayOpen) return;
        DrawAimRings();
        var hp = GetComponent<PlayerHealth>();
        if (hp != null && hp.IsDead) return;
        if (bowIcon == null)
        {
            bowIcon = Resources.Load<Texture2D>("UI/JV_Icon_Bow");
            arrowIcon = Resources.Load<Texture2D>("UI/JV_Icon_Arrow");
            knifeIcon = Resources.Load<Texture2D>("UI/JV_Icon_Knife");
        }

        // 右下角：弓（箭射完變小刀）+ 剩下幾支箭（兩排各 10 支，用掉的變暗）
        const int PerRow = 10;
        const float ArrowW = 8f, ArrowH = 22f, Gap = 15f;
        var box = new Rect(Screen.width - 250f, Screen.height - 160f, 230f, 58f);
        GameUI.Panel(box);
        var prev = GUI.color;
        var icon = Arrows > 0 ? bowIcon : knifeIcon;
        GUI.color = Color.white;
        if (icon != null) GUI.DrawTexture(new Rect(box.x + 6f, box.y + 5f, 48f, 48f), icon, ScaleMode.ScaleToFit, true);
        for (int i = 0; i < MaxArrows; i++)
        {
            int row = i / PerRow, col = i % PerRow;
            var r = new Rect(box.x + 64f + col * Gap, box.y + 5f + row * (ArrowH + 4f), ArrowW, ArrowH);
            GUI.color = i < Arrows ? Color.white : new Color(1f, 1f, 1f, 0.16f);
            if (arrowIcon != null) GUI.DrawTexture(r, arrowIcon, ScaleMode.ScaleToFit, true);
            else GUI.DrawTexture(new Rect(r.x + 3f, r.y, 2f, r.height), Texture2D.whiteTexture);
        }

        if (notice >= 0f && Time.unscaledTime - notice < 3f)
        {
            hudStyle ??= new GUIStyle(GameUI.Small) { fontSize = 17, alignment = TextAnchor.MiddleRight };
            GUI.color = new Color(GameUI.Accent.r, GameUI.Accent.g, GameUI.Accent.b, Mathf.Clamp01(3f - (Time.unscaledTime - notice)));
            GUI.Label(new Rect(box.x - 140f, box.y - 30f, box.width + 130f, 26f), "箭射完了，換成小刀（回鐵匠鋪補箭）", hudStyle);
        }
        GUI.color = prev;
    }
}
