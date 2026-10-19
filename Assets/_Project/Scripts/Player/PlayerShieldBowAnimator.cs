using UnityEngine;

/// <summary>
/// 盾牌與弓箭的動畫（貓戰士 PlayerVisualAnimator 的補充，掛在同一個物件上，會自動加上）：
///   ShieldBash   右鍵按一下舉盾衝撞（整段播完）
///   BlockIdle    右鍵按住舉盾格擋
///   BlockHit     用盾牌擋下攻擊（破防的那一下由 PlayerVisualAnimator 播受擊）
///   BowDraw      第二關在船上拉弓：瞄準時停在拉滿弓、射箭時放手（Mixamo Longbow 的拉弓放箭）
/// 動畫狀態在 AC_PlayerCatWarrior.controller（用聖騎士的 Mixamo 動畫，Humanoid 可以直接套用）。
/// 弓箭模式時左手拿一把用 LineRenderer 畫的長弓，光劍收在腰上。
/// 播這些動畫的時候 Overriding = true，PlayerVisualAnimator 不會切去待機 / 移動動畫。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerShieldBowAnimator : MonoBehaviour
{
    [Tooltip("拉弓動畫播到多少比例算「拉滿弓」，瞄準時停在這裡")]
    [Range(0.2f, 0.9f)] public float bowFullDraw = 0.55f;
    [Tooltip("放箭時動畫的播放速度")]
    public float bowReleaseSpeed = 1.8f;
    [Tooltip("盾牌擋下攻擊的動畫要播多久 (秒)")]
    public float blockHitDuration = 0.35f;

    Animator animator;
    PlayerCombat combat;
    PlayerHealth health;
    PlayerWeaponPresenter weaponPresenter;

    string current;          // 目前由這裡播的狀態（null = 交給 PlayerVisualAnimator）
    float busyUntil;         // 盾擊 / 擋下 / 放箭還沒播完
    bool bowAiming, bowShown;
    float redrawAt = float.PositiveInfinity;
    Transform bowProp, leftHand;

    /// <summary>正在播盾牌 / 弓箭動畫：PlayerVisualAnimator 不要切別的動畫</summary>
    public bool Overriding => current != null;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        animator = GetComponentInChildren<Animator>(true);
        weaponPresenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
    }

    void OnEnable()
    {
        if (combat != null)
        {
            combat.OnShieldBash += HandleShieldBash;
            combat.OnBlockChanged += HandleBlockChanged;
            combat.OnBlockedHit += HandleBlockedHit;
            combat.OnBowAim += HandleBowAim;
            combat.OnBowShot += HandleBowShot;
        }
        if (health != null) health.OnDied += Release;
    }

    void OnDisable()
    {
        if (combat != null)
        {
            combat.OnShieldBash -= HandleShieldBash;
            combat.OnBlockChanged -= HandleBlockChanged;
            combat.OnBlockedHit -= HandleBlockedHit;
            combat.OnBowAim -= HandleBowAim;
            combat.OnBowShot -= HandleBowShot;
        }
        if (health != null) health.OnDied -= Release;
        Release();
    }

    bool CanAnimate => animator != null && animator.runtimeAnimatorController != null && (health == null || !health.IsDead);

    void Play(string state, float fade, float speed = 1f)
    {
        if (!CanAnimate) return;
        animator.speed = speed;
        animator.CrossFadeInFixedTime(state, fade, 0, 0f);
        current = state;
    }

    /// <summary>交還給 PlayerVisualAnimator（回到待機）</summary>
    void Release()
    {
        if (current == null) return;
        current = null;
        bowAiming = false;
        redrawAt = float.PositiveInfinity;
        if (animator == null) return;
        animator.speed = 1f;
        if (health == null || !health.IsDead) animator.CrossFadeInFixedTime("Idle", 0.15f, 0, 0f);
    }

    void Update()
    {
        if (!CanAnimate) { current = null; return; }

        // 拉弓瞄準：播到拉滿弓就停住
        if (current == "BowDraw" && bowAiming && Time.time >= busyUntil)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.speed = st.IsName("BowDraw") && st.normalizedTime >= bowFullDraw ? 0f : 1f;
        }
        // 放完箭：還在瞄準就重新拉弓，沒有就回到待機
        if (Time.time >= redrawAt)
        {
            redrawAt = float.PositiveInfinity;
            if (bowAiming) Play("BowDraw", 0.15f);
            else Release();
        }

        if (Time.time < busyUntil) return;

        // 盾擊 / 擋下播完：還舉著盾就回到格擋姿勢，不然交還
        if (current == "ShieldBash" || current == "BlockHit")
        {
            if (combat != null && combat.IsBlocking) Play("BlockIdle", 0.1f);
            else Release();
        }
        else if (current == "BlockIdle" && (combat == null || !combat.IsBlocking))
        {
            Release();
        }
    }

    // ---------------- 盾牌 ----------------

    void HandleShieldBash()
    {
        Play("ShieldBash", 0.06f);
        busyUntil = Time.time + combat.shieldBashWindup + combat.shieldBashActive + combat.shieldBashRecovery;
    }

    void HandleBlockChanged(bool blocking)
    {
        if (blocking && Time.time >= busyUntil) Play("BlockIdle", 0.1f);
    }

    void HandleBlockedHit(bool broke)
    {
        if (broke)
        {
            // 破防：放下盾牌，受擊動畫交給 PlayerVisualAnimator
            busyUntil = 0f;
            Release();
            return;
        }
        if (combat.IsShieldBashing) return; // 盾擊中擋到不打斷盾擊動畫
        Play("BlockHit", 0.05f, 1.2f);
        busyUntil = Time.time + blockHitDuration;
    }

    // ---------------- 弓箭 ----------------

    void HandleBowAim(bool aiming)
    {
        bowAiming = aiming;
        if (aiming) Play("BowDraw", 0.1f);
        else if (Time.time >= busyUntil) Release();
    }

    void HandleBowShot()
    {
        // 已經拉滿：直接放手往下播；沒瞄準就射：從頭快速拉弓放箭
        if (current != "BowDraw") Play("BowDraw", 0.05f, bowReleaseSpeed);
        else animator.speed = bowReleaseSpeed;
        busyUntil = Time.time + 0.45f;
        redrawAt = busyUntil;
    }

    // 動畫算完之後才擺弓（不然會晚一幀跟不上手）
    void LateUpdate()
    {
        bool want = combat != null && combat.bowMode && (health == null || !health.IsDead);
        if (want != bowShown)
        {
            bowShown = want;
            if (want && bowProp == null) bowProp = BuildBow();
            if (bowProp != null) bowProp.gameObject.SetActive(want);
            if (want && weaponPresenter != null) weaponPresenter.SheatheNow(); // 拿弓時光劍收在腰上
            if (!want && current == "BowDraw") Release();
        }
        if (!want || bowProp == null) return;

        // 弓跟著左手，弓身直立、朝著角色前方
        if (leftHand == null && animator != null && animator.isHuman) leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Vector3 pos = leftHand != null ? leftHand.position : transform.position + Vector3.up * 0.5f + transform.forward * 0.5f;
        bowProp.SetPositionAndRotation(pos, Quaternion.LookRotation(transform.forward, Vector3.up));
    }

    /// <summary>用 LineRenderer 畫一把長弓：弧形的弓身 + 一條弓弦</summary>
    static Transform BuildBow()
    {
        var root = new GameObject("PlayerBow").transform;
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        var limb = new GameObject("Limb").AddComponent<LineRenderer>();
        limb.transform.SetParent(root, false);
        limb.useWorldSpace = false;
        limb.material = new Material(shader) { color = new Color(0.4f, 0.25f, 0.12f) };
        limb.widthMultiplier = 0.05f;
        const int n = 13;
        limb.positionCount = n;
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f) * 2f - 1f;                                   // -1 ~ 1（下 → 上）
            limb.SetPosition(i, new Vector3(0f, t * 0.75f, (1f - t * t) * 0.2f)); // 往前彎
        }

        var str = new GameObject("String").AddComponent<LineRenderer>();
        str.transform.SetParent(root, false);
        str.useWorldSpace = false;
        str.material = new Material(shader) { color = new Color(0.9f, 0.9f, 0.85f) };
        str.widthMultiplier = 0.008f;
        str.positionCount = 2;
        str.SetPosition(0, new Vector3(0f, -0.75f, 0f));
        str.SetPosition(1, new Vector3(0f, 0.75f, 0f));
        return root;
    }
}
