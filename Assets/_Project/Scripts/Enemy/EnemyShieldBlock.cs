using UnityEngine;

/// <summary>
/// 骷髏士兵舉盾格擋：
///   - 玩家在面前揮刀時，有機會舉起盾牌（EnemyVisual 播 Block → BlockIdle）
///   - 舉盾期間，從正面打來的攻擊會被擋掉：不扣血、不硬直，噴火花＋金屬聲，玩家的刀被彈開往後退一點
///   - 從背後 / 側面打、或是技能的無視格擋傷害，照樣打得到
///   - 預設不畫盾牌（只播格擋動作）；想要的話勾 showShieldProp，會在左前臂做一面簡單的圓盾
///
/// 用法：掛在有 EnemyAI 的小怪上（Enemy_Grunt 已掛）。拿掉就不會格擋。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth))]
public class EnemyShieldBlock : MonoBehaviour
{
    [Header("什麼時候舉盾")]
    [Tooltip("玩家在這個距離內對著自己揮刀才會考慮舉盾 (m)")]
    public float reactRange = 3.6f;
    [Tooltip("玩家揮刀時舉盾的機率")]
    [Range(0f, 1f)] public float blockChance = 0.35f;
    [Tooltip("剛被打中（硬直中）時不會舉盾；兩次舉盾至少間隔多久 (秒)")]
    public float cooldown = 2.2f;
    [Tooltip("盾牌舉多久 (秒)")]
    public float blockDuration = 0.9f;
    [Tooltip("盾牌擋得到的角度（正前方左右各幾度）")]
    public float blockAngle = 75f;

    [Header("擋到時")]
    [Tooltip("把玩家往後彈多遠的力道")]
    public float playerRecoil = 5f;
    public Color sparkColor = new Color(1f, 0.75f, 0.35f);

    [Header("盾牌外觀（程式產生，預設不畫）")]
    public bool showShieldProp = false;
    [Tooltip("盾牌直徑佔身高的比例")]
    public float shieldSizeRatio = 0.3f;
    public Color shieldColor = new Color(0.22f, 0.2f, 0.17f);
    public Color shieldRimColor = new Color(0.42f, 0.38f, 0.3f);

    /// <summary>正在舉盾（EnemyAI 這段時間站著不動、面向玩家）</summary>
    public bool IsBlocking => Time.time < blockUntil && !health.IsDead;

    EnemyHealth health;
    EnemyCombat combat;
    EnemyVisual visual;
    CharacterController cc;
    PlayerCombat playerCombat;
    float blockUntil, nextBlockAllowed;
    Transform shieldProp;
    static Material shieldMat, rimMat;

    void Awake()
    {
        health = GetComponent<EnemyHealth>();
        combat = GetComponent<EnemyCombat>();
        visual = GetComponent<EnemyVisual>();
        cc = GetComponent<CharacterController>();
    }

    void OnEnable()
    {
        health.BlockCheck = TryBlock;
        health.OnDied += OnDied;
        health.OnDamaged += OnDamaged;
    }

    void OnDisable()
    {
        if (health.BlockCheck == TryBlock) health.BlockCheck = null;
        health.OnDied -= OnDied;
        health.OnDamaged -= OnDamaged;
        if (playerCombat != null) playerCombat.OnSwing -= OnPlayerSwing;
        playerCombat = null;
    }

    void Update()
    {
        if (playerCombat == null)
        {
            var ph = PlayerHealth.Find();
            if (ph != null && (playerCombat = ph.GetComponent<PlayerCombat>()) != null) playerCombat.OnSwing += OnPlayerSwing;
        }
        if (showShieldProp && shieldProp == null) BuildShield();
        if (shieldProp != null)
        {
            // 死亡沉下去 / 翻落橋下之後模型被藏起來：盾牌跟著藏
            bool visible = !health.IsDead || AnyBodyVisible();
            if (shieldProp.gameObject.activeSelf != visible) shieldProp.gameObject.SetActive(visible);
        }
    }

    // ---------------- 舉盾 ----------------

    void OnPlayerSwing(int index)
    {
        if (health.IsDead || IsBlocking || Time.time < nextBlockAllowed) return;
        if (combat != null && combat.IsAttacking) return;
        var ai = GetComponent<EnemyAI>();
        if (ai != null && ai.CurrentState != EnemyAI.State.Chase) return;
        if (Time.time - health.LastHitTime < health.staggerTime) return; // 硬直中
        Transform p = playerCombat.transform;
        Vector3 toPlayer = Vector3.ProjectOnPlane(p.position - transform.position, Vector3.up);
        if (toPlayer.magnitude > reactRange) return;
        // 玩家要面向自己（真的是砍向我），自己也大致面向玩家
        if (Vector3.Angle(p.forward, -toPlayer) > 70f || Vector3.Angle(transform.forward, toPlayer) > 100f) return;
        if (Random.value > blockChance) return;
        RaiseShield(blockDuration);
    }

    /// <summary>舉盾 seconds 秒（其他系統也可以直接叫）</summary>
    public void RaiseShield(float seconds)
    {
        if (health.IsDead) return;
        blockUntil = Time.time + seconds;
        nextBlockAllowed = blockUntil + cooldown;
        if (combat != null) combat.CancelAttack();
        if (visual != null) visual.PlayBlock(seconds);
    }

    /// <summary>EnemyHealth 受傷前問一下：正面打在盾上就擋掉</summary>
    bool TryBlock(Vector3 hitPoint)
    {
        if (!IsBlocking) return false;
        Vector3 from = Vector3.ProjectOnPlane(hitPoint - transform.position, Vector3.up);
        Transform attacker = playerCombat != null ? playerCombat.transform : null;
        if (attacker != null) from = Vector3.ProjectOnPlane(attacker.position - transform.position, Vector3.up);
        if (from.sqrMagnitude > 0.0001f && Vector3.Angle(transform.forward, from) > blockAngle) return false;

        Vector3 spark = shieldProp != null ? shieldProp.position : transform.position + Vector3.up * 1.2f + transform.forward * 0.5f;
        CombatVFX.Impact(spark, sparkColor);
        CombatVFX.Impact(spark, sparkColor);
        GameAudio.Play("SFX/RPG/metalClick", spark, 0.9f, 0.7f, 0.1f, 1f, 35f);
        GameAudio.Play("SFX/RPG/metalPot2", spark, 0.7f, 1.3f, 0.1f, 1f, 35f);
        ThirdPersonCamera.ShakeMain(0.06f, 0.1f);
        if (visual != null) visual.PlayBlockImpact();
        // 玩家的刀被彈開
        if (attacker != null && from.sqrMagnitude > 0.0001f)
        {
            var pc = attacker.GetComponent<ThirdPersonController>();
            if (pc != null) pc.AddImpulse(from.normalized * playerRecoil);
        }
        return true;
    }

    void OnDamaged(float amount) => EndBlock(); // 從背後被打到：盾牌放下
    void OnDied() => EndBlock();

    void EndBlock()
    {
        blockUntil = 0f;
        if (visual != null) visual.StopBlock();
    }

    // ---------------- 盾牌外觀 ----------------

    bool AnyBodyVisible()
    {
        foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>())
            if (r.enabled) return true;
        return false;
    }

    void BuildShield()
    {
        var anim = visual != null ? visual.animator : GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) { showShieldProp = false; return; }
        Transform forearm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (forearm == null || hand == null) { showShieldProp = false; return; }

        float height = cc != null ? cc.height : 2f;
        float diameter = height * shieldSizeRatio;
        if (shieldMat == null) shieldMat = MakeMat(shieldColor);
        if (rimMat == null) rimMat = MakeMat(shieldRimColor);

        var root = new GameObject("ShieldProp").transform;
        // 圓盤：Cylinder 的軸是 Y，壓扁之後 Y 就是盾面的法線
        var disk = Part(PrimitiveType.Cylinder, root, new Vector3(diameter, diameter * 0.04f, diameter), Vector3.zero, shieldMat);
        Part(PrimitiveType.Cylinder, root, new Vector3(diameter * 1.06f, diameter * 0.025f, diameter * 1.06f), Vector3.up * -0.003f, rimMat);
        Part(PrimitiveType.Sphere, root, Vector3.one * diameter * 0.22f, Vector3.up * diameter * 0.03f, rimMat); // 中間的凸起
        disk.name = "Face";

        // 綁在左前臂靠手腕的位置；盾面朝身體外側（左邊），盾面跟前臂平行
        Vector3 along = (hand.position - forearm.position).normalized;
        Vector3 outward = Vector3.ProjectOnPlane(-transform.right, along).normalized;
        if (outward.sqrMagnitude < 0.01f) outward = -transform.right;
        root.SetPositionAndRotation(Vector3.Lerp(forearm.position, hand.position, 0.6f) + outward * diameter * 0.12f,
                                    Quaternion.LookRotation(along, outward));
        root.SetParent(forearm, true);
        shieldProp = root;
    }

    static Transform Part(PrimitiveType type, Transform parent, Vector3 scale, Vector3 localPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        // LookRotation(along, outward)：本地 Y = outward → 圓柱的軸剛好朝外
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go.transform;
    }

    static Material MakeMat(Color c)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader) { name = "M_SkeletonShield_Runtime" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.55f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.35f);
        return m;
    }
}
