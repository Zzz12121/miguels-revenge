using UnityEngine;

/// <summary>
/// 把玩家的狀態接到聖騎士模型的動畫（Animator）。由選單 GeminiGrowUp > 安裝玩家（聖騎士 + Cinemachine）加上。
///
/// Animator 裡的狀態名稱：
///   Locomotion               移動混合樹（參數 Speed = 移動速度）：待機 → 走路 → 跑步連續混合，切換不會跳
///   Idle、Walk、Run          沒有 Locomotion 混合樹時（舊的 Animator）才用，依速度門檻切換
///   Jump、Fall、Land         起跳、在空中、落地
///   Slash01、Slash02、Stab   近戰連擊的第 1、2、3 段（左鍵）
///   ShieldBash               右鍵舉盾衝撞（完整播完才回到移動動畫）
///   Hit                      受到傷害
///   Death                    死亡（重生後回到 Idle）
///   BowDraw                  在船上拉弓（Mixamo Longbow 的拉弓放箭）：瞄準時停在拉滿弓、射箭時放手
/// 少了哪個狀態，那個動作就維持上一個動畫，不會出錯。
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class PlayerVisual : MonoBehaviour
{
    [Tooltip("模型上的 Animator")]
    public Animator animator;

    [Header("移動動畫")]
    [Tooltip("速度低於這個值算站著不動 (m/s)")]
    public float idleThreshold = 0.25f;
    [Tooltip("速度超過這個值播跑步，否則播走路 (m/s)")]
    public float runThreshold = 2.6f;
    [Tooltip("走路動畫原本對應的移動速度 (m/s)")]
    public float walkAnimSpeed = 1.8f;
    [Tooltip("跑步動畫原本對應的移動速度 (m/s)")]
    public float runAnimSpeed = 4.5f;
    public float crossFade = 0.2f;
    [Tooltip("移動速度參數的平滑時間 (秒)：加速、減速時走跑動畫慢慢混過去，不會一下子跳")]
    public float speedDamp = 0.12f;

    [Header("攻擊動畫（連擊第 1、2、3 段）")]
    public EnemyVisual.AttackAnim[] attacks =
    {
        new EnemyVisual.AttackAnim { state = "Slash01", hitTime = 0.5f },
        new EnemyVisual.AttackAnim { state = "Slash02", hitTime = 0.5f },
        new EnemyVisual.AttackAnim { state = "Stab",    hitTime = 0.5f },
    };
    [Tooltip("連擊動畫的播放速度（1 = 原本速度，動畫完整播放；傷害判定的時間會照這個速度算）")]
    public float attackAnimSpeed = 1f;
    [Tooltip("每一刀有傷害判定的時間 (秒)")]
    public float attackHitWindow = 0.18f;
    [Tooltip("砍完之後的收招時間（佔剩下動畫的比例）。1 = 等整段動畫播完才算結束")]
    [Range(0.2f, 1f)] public float comboRecoveryRatio = 1f;
    [Tooltip("收招播到多少比例之後才能接下一段（有先按攻擊鍵的話）。越接近 1 動畫越完整")]
    [Range(0f, 1f)] public float comboChainAfter = 0.9f;
    [Tooltip("受擊動畫最短播多久 (秒)。實際會等受擊動畫完整播完才回到移動動畫")]
    public float hitDuration = 0.35f;
    [Tooltip("落地動畫最短播多久 (秒)。實際會等落地動畫完整播完")]
    public float landDuration = 0.25f;

    [Header("弓箭（在船上）")]
    [Tooltip("拉弓動畫播到哪裡算拉滿弓（0~1），瞄準時停在這裡")]
    [Range(0.2f, 0.9f)] public float bowFullDraw = 0.55f;
    [Tooltip("射箭時放手的動畫播放速度")]
    public float bowReleaseSpeed = 1.8f;

    [Header("其他")]
    [Tooltip("隱藏攻擊元件自動產生的方塊劍（模型手上已經有劍盾）")]
    public bool hideDefaultWeapon = true;

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

    ThirdPersonController controller;
    PlayerCombat combat;
    PlayerHealth health;
    string current = "";
    float busyUntil;       // 攻擊 / 受擊 / 落地動畫播放中，不切回移動動畫
    bool wasGrounded = true;
    float airTime;
    bool dead;
    bool bowAiming, bowShown;
    Transform bowProp, leftHand;
    Transform[] propJoints;      // 劍和盾各自綁的骨頭（Sword_joint、Shield_joint）
    Vector3[] propJointScales;

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false; // 移動由 ThirdPersonController 控制，動畫只負責姿勢
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            // Starter Assets 的跳躍 / 走路動畫內建 OnLand、OnFootstep 事件，要有元件接住才不會一直跳警告
            if (animator.GetComponent<AnimationEventSink>() == null) animator.gameObject.AddComponent<AnimationEventSink>();
        }

        if (combat != null)
        {
            combat.OnSwing += OnSwing;
            combat.OnBowAim += OnBowAim;
            combat.OnBowShot += OnBowShot;
            combat.OnShieldBash += OnShieldBash;
        }
        if (health != null)
        {
            health.OnDamaged += OnHit;
            health.OnDied += OnDied;
            health.OnRespawned += OnRespawned;
        }
    }

    void Start()
    {
        SyncCombatTiming();
        // MeleeCombat 在 Awake 產生的方塊劍：模型手上已經有劍了，藏起來（判定不受影響）
        if (hideDefaultWeapon && HasAnimator && combat != null && combat.weaponPivot != null)
            foreach (Transform child in combat.weaponPivot) child.gameObject.SetActive(false);
        Go("Idle", 0f, true);
    }

    void OnDestroy()
    {
        if (combat != null)
        {
            combat.OnSwing -= OnSwing;
            combat.OnBowAim -= OnBowAim;
            combat.OnBowShot -= OnBowShot;
            combat.OnShieldBash -= OnShieldBash;
        }
        if (health != null)
        {
            health.OnDamaged -= OnHit;
            health.OnDied -= OnDied;
            health.OnRespawned -= OnRespawned;
        }
    }

    void Update()
    {
        if (!HasAnimator || dead) return;

        // 移動速度一直更新給混合樹（播攻擊 / 受擊時也更新，回到移動動畫時才接得順）
        if (HasLocomotion)
        {
            float speedParam = controller.isActiveAndEnabled ? controller.CurrentSpeed : 0f;
            animator.SetFloat(SpeedId, speedParam, speedDamp, Time.deltaTime);
        }

        // 拉弓瞄準：播到拉滿弓就停住
        if (bowAiming && current == "BowDraw" && Time.time >= busyUntil)
        {
            var st = animator.GetCurrentAnimatorStateInfo(0);
            animator.speed = st.IsName("BowDraw") && st.normalizedTime >= bowFullDraw ? 0f : 1f;
            return;
        }

        // 搭船時移動腳本被停用：站在船上，不要當成在空中往下掉
        if (!controller.isActiveAndEnabled)
        {
            wasGrounded = true;
            airTime = 0f;
            if (Time.time >= busyUntil && !bowAiming && (combat == null || !combat.IsAttacking))
            {
                animator.speed = 1f;
                Go("Idle", crossFade);
            }
            return;
        }

        bool grounded = controller.IsGrounded;
        airTime = grounded ? 0f : airTime + Time.deltaTime;

        // 起跳：離開地面而且往上
        if (wasGrounded && !grounded && controller.VerticalVelocity > 1f)
        {
            animator.speed = 1f;
            Go("Jump", 0.05f, true);
        }
        // 落地：在空中待了一陣子才播，走下小台階不播
        else if (!wasGrounded && grounded && current == "Fall")
        {
            animator.speed = 1f;
            Go("Land", 0.08f, true);
            busyUntil = Time.time + landDuration;
            HoldForFull("Land");
        }
        wasGrounded = grounded;

        if (Time.time < busyUntil) return;
        if (combat != null && (combat.IsAttacking || combat.IsShieldBashing)) return;

        if (!grounded)
        {
            // 過了最高點、或從高處走下去一段時間 → 下墜
            if ((current == "Jump" && controller.VerticalVelocity < 0f) || (current != "Jump" && airTime > 0.25f))
            {
                animator.speed = 1f;
                Go("Fall", 0.15f);
            }
            return;
        }

        float v = controller.CurrentSpeed;
        if (HasLocomotion)
        {
            // 混合樹負責待機 / 走 / 跑之間的過渡；比跑步動畫原本的速度還快（衝刺）時，動畫跟著播快一點
            animator.speed = v > runAnimSpeed ? Mathf.Clamp(v / Mathf.Max(runAnimSpeed, 0.01f), 1f, 1.6f) : 1f;
            Go("Locomotion", crossFade);
            return;
        }
        if (v < idleThreshold)
        {
            animator.speed = 1f;
            Go("Idle", crossFade);
        }
        else if (v < runThreshold && !controller.IsPushing)
        {
            animator.speed = Mathf.Clamp(v / Mathf.Max(walkAnimSpeed, 0.01f), 0.6f, 1.6f);
            Go("Walk", crossFade);
        }
        else
        {
            animator.speed = Mathf.Clamp(v / Mathf.Max(runAnimSpeed, 0.01f), 0.5f, 1.8f);
            Go("Run", crossFade);
        }
    }

    // ---------------- 事件 ----------------

    /// <summary>連擊的每一刀：播對應的攻擊動畫，並調整速度讓「砍到」對上傷害判定</summary>
    void OnSwing(int index)
    {
        if (!HasAnimator || dead || attacks == null || attacks.Length == 0) return;
        var a = attacks[index % attacks.Length];
        animator.speed = attackAnimSpeed; // 傷害判定的時間是照這個速度算的（SyncCombatTiming）
        Go(a.state, 0.05f, true);
        busyUntil = 0f;
    }

    /// <summary>
    /// 三段攻擊動畫長短不一樣、砍到的時間也不一樣：照每個動畫算出各自的抬手 / 判定 / 硬直時間交給 PlayerCombat，
    /// 這樣三段動畫畫面上砍到的那一刻都會造成傷害
    /// </summary>
    void SyncCombatTiming()
    {
        if (!HasAnimator || combat == null || attacks == null || attacks.Length == 0) return;
        int n = combat.comboDamage.Length;
        var windup = new float[n];
        var swing = new float[n];
        var recovery = new float[n];
        float speed = Mathf.Max(attackAnimSpeed, 0.1f);
        for (int i = 0; i < n; i++)
        {
            var a = attacks[i % attacks.Length];
            float length = a.clipLength > 0f ? a.clipLength : a.hitTime / 0.45f;
            float hit = a.hitTime / speed;                         // 播到砍下去的那一刻（秒）
            windup[i] = Mathf.Max(0.06f, hit - attackHitWindow * 0.5f);
            swing[i] = attackHitWindow;                             // 判定時間跨過砍到的那一刻
            float rest = Mathf.Max(0f, length / speed - windup[i] - swing[i]);
            recovery[i] = Mathf.Max(0.15f, rest * comboRecoveryRatio);
        }
        combat.stepWindup = windup;
        combat.stepSwing = swing;
        combat.stepRecovery = recovery;
        combat.chainAfter = comboChainAfter;
    }


    /// <summary>右鍵盾擊：舉盾往前頂，動畫完整播完</summary>
    void OnShieldBash()
    {
        if (!HasAnimator || dead) return;
        animator.speed = 1f;
        Go("ShieldBash", 0.06f, true);
        busyUntil = Time.time + combat.shieldBashWindup + combat.shieldBashActive + combat.shieldBashRecovery;
        HoldForFull("ShieldBash");
    }

    // ---------------- 弓箭 ----------------

    void OnBowAim(bool aiming)
    {
        bowAiming = aiming;
        if (!HasAnimator || dead) return;
        if (aiming)
        {
            animator.speed = 1f;
            Go("BowDraw", 0.1f, true); // 開始拉弓，Update 裡拉滿就停住
        }
        else if (Time.time >= busyUntil)
        {
            animator.speed = 1f;
            Go("Idle", 0.2f);
        }
    }

    void OnBowShot()
    {
        if (!HasAnimator || dead) return;
        // 已經拉滿：直接放手往下播；沒瞄準就射：從頭快速拉弓放箭
        if (current != "BowDraw") Go("BowDraw", 0.05f, true);
        animator.speed = bowReleaseSpeed;
        busyUntil = Time.time + 0.45f;
        StartCoroutine(RedrawAfterShot());
    }

    /// <summary>放完箭：還在瞄準就重新拉弓，沒有就回到待機</summary>
    System.Collections.IEnumerator RedrawAfterShot()
    {
        yield return new WaitForSeconds(0.45f);
        if (dead) yield break;
        animator.speed = 1f;
        if (bowAiming) Go("BowDraw", 0.15f, true);
        else Go("Idle", 0.2f);
    }

    // 動畫算完之後才擺弓、縮劍盾（不然會被這一幀的動畫蓋掉、或晚一幀跟不上手）
    void LateUpdate()
    {
        if (HasAnimator) UpdateBowProp();
    }

    /// <summary>
    /// 在船上（弓箭模式）：拿走劍和盾、左手拿一把弓；下船後換回來。
    /// 劍和盾是用「骨頭」找的，不是用網格名稱：這個 Mixamo FBX 的網格名稱是錯開的
    /// （名字叫 Sword 的其實是身體、叫 Shield 的其實是頭盔），但劍和盾各自 100% 綁在 Sword_joint、Shield_joint 上，
    /// 把這兩根骨頭縮成 0，劍和盾就會完全消失，模型其他部分不受影響
    /// </summary>
    void UpdateBowProp()
    {
        bool want = combat != null && combat.bowMode && !dead;
        if (propJoints == null) FindPropJoints();
        if (want != bowShown)
        {
            bowShown = want;
            if (!want)
                for (int i = 0; i < propJoints.Length; i++)
                    if (propJoints[i] != null) propJoints[i].localScale = propJointScales[i];
            if (want && bowProp == null) bowProp = BuildBow();
            if (bowProp != null) bowProp.gameObject.SetActive(want);
        }
        if (!want) return;
        foreach (var j in propJoints) if (j != null) j.localScale = Vector3.zero;
        if (bowProp == null) return;

        // 弓跟著左手，弓身直立、朝著角色前方
        if (leftHand == null && animator.isHuman) leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        Vector3 pos = leftHand != null ? leftHand.position : transform.position + Vector3.up * 0.5f + transform.forward * 0.5f;
        bowProp.SetPositionAndRotation(pos, Quaternion.LookRotation(transform.forward, Vector3.up));
    }

    void FindPropJoints()
    {
        var list = new System.Collections.Generic.List<Transform>();
        foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("Sword_joint") || t.name.EndsWith("Shield_joint")) list.Add(t);
        propJoints = list.ToArray();
        propJointScales = new Vector3[propJoints.Length];
        for (int i = 0; i < propJoints.Length; i++) propJointScales[i] = propJoints[i].localScale;
        if (propJoints.Length < 2)
            Debug.LogWarning($"[PlayerVisual] 只找到 {propJoints.Length} 根劍盾骨頭（Sword_joint / Shield_joint），在船上可能還看得到劍或盾", this);
    }

    /// <summary>用 LineRenderer 畫一把長弓：弧形的弓身 + 一條弓弦</summary>
    Transform BuildBow()
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
            float t = i / (n - 1f) * 2f - 1f;                       // -1 ~ 1（下 → 上）
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

    void OnHit(float amount)
    {
        if (!HasAnimator || dead) return;
        if (combat != null && (combat.IsAttacking || combat.IsShieldBashing)) return; // 攻擊、舉盾中被打不打斷
        if (bowAiming) return;                            // 拉弓瞄準時不打斷
        animator.speed = 1f;
        Go("Hit", 0.05f, true);
        busyUntil = Time.time + hitDuration;              // 先擋一下，下一幀量到動畫長度再改成完整長度
        if (hitRoutine != null) StopCoroutine(hitRoutine);
        hitRoutine = StartCoroutine(HoldForFullHitAnimation());
    }

    Coroutine hitRoutine;

    System.Collections.IEnumerator HoldForFullHitAnimation()
    {
        yield return HoldForFullRoutine("Hit");
        hitRoutine = null;
    }

    /// <summary>這個動畫要完整播完才回到移動動畫（受擊、落地）</summary>
    void HoldForFull(string state) => StartCoroutine(HoldForFullRoutine(state));

    /// <summary>等 Animator 切到這個狀態之後量它的長度，把 busyUntil 延到動畫播完</summary>
    System.Collections.IEnumerator HoldForFullRoutine(string state)
    {
        yield return null;
        if (dead || current != state) yield break;
        var next = animator.GetNextAnimatorStateInfo(0);
        var st = next.IsName(state) ? next : animator.GetCurrentAnimatorStateInfo(0);
        float speed = Mathf.Max(animator.speed, 0.01f);
        if (st.IsName(state) && st.length > 0f)
            busyUntil = Mathf.Max(busyUntil,
                Time.time + st.length * (1f - Mathf.Repeat(st.normalizedTime, 1f)) / speed - Time.deltaTime);
    }

    // ---------------- 移動混合樹 ----------------

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int LocomotionId = Animator.StringToHash("Locomotion");
    int hasLocomotion = -1; // -1 = 還沒檢查

    /// <summary>Animator 裡有沒有 Locomotion 混合樹（舊的 Animator 沒有，就用 Idle / Walk / Run 切換）</summary>
    bool HasLocomotion
    {
        get
        {
            if (hasLocomotion < 0 && animator.isInitialized) hasLocomotion = animator.HasState(0, LocomotionId) ? 1 : 0;
            return hasLocomotion == 1;
        }
    }

    void OnDied()
    {
        dead = true;
        if (!HasAnimator) return;
        animator.speed = 1f;
        Go("Death", 0.08f, true);
    }

    void OnRespawned()
    {
        dead = false;
        busyUntil = 0f;
        wasGrounded = true;
        if (!HasAnimator) return;
        animator.speed = 1f;
        Go("Idle", 0f, true);
    }

    void Go(string state, float fade, bool restart = false)
    {
        if (!restart && current == state) return;
        if (!animator.HasState(0, Animator.StringToHash(state))) return;
        current = state;
        if (fade <= 0f) animator.Play(state, 0, 0f);
        else animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }
}
