using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 蜘蛛巢穴的蜘蛛行為（掛在有 EnemyAI 的蜘蛛上；SpiderWebAttack 會自動加上）：
///
/// 1. 卵：一開始蜘蛛藏在卵裡（看不到、不會動），玩家走近 hatchRadius 就孵化（打卵也會直接孵化）。
/// 2. 鑽地：蜘蛛被打之後會先鑽進沙裡潛伏（打不到），地面上有一團隆起的沙丘朝玩家移動、頭上顯示「潛伏中」；
///    潛伏結束後在玩家腳下出現紅色警示圈，紅圈結束時蜘蛛跳出地面咬人，站在圈裡會受傷。
///    跳出地面用的是 EnemyAI.BeginEmerge（紅圈、傷害判定都在那裡）。兩次鑽地之間有冷卻。
/// 玩家死亡重生時：潛伏中的蜘蛛直接回到地面、回原位；還在卵裡的維持在卵裡。
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class SpiderBurrow : MonoBehaviour
{
    [Header("數值調整（遊戲開始時套用在場景裡設定的數值上）")]
    [Tooltip("血量倍率（場景裡的 Max Health × 這個值）")]
    public float healthMultiplier = 3f;
    [Tooltip("大小倍率（模型、碰撞體、血條高度一起縮放）")]
    public float sizeMultiplier = 0.7f;
    [Tooltip("發現玩家的距離 (m)；放棄追擊 / 拉回的距離至少會跟它一樣，不然一發現就放棄")]
    public float alertRadius = 45f;

    [Header("卵")]
    public bool startInEgg = true;
    [Tooltip("玩家走到多近卵會孵化 (m)")]
    public float hatchRadius = 12f;
    public Color eggColor = new Color(0.88f, 0.85f, 0.74f);

    [Header("鑽地偷襲")]
    [Tooltip("兩次鑽地之間至少隔多久 (秒)")]
    public float burrowCooldown = 8f;
    [Tooltip("鑽進沙裡要多久 (秒)")]
    public float burrowTime = 0.5f;
    [Tooltip("在沙裡潛伏多久才出現紅圈 (秒)")]
    public float lurkTime = 2.2f;
    [Tooltip("紅色警示圈出現多久後跳出來 (秒)")]
    public float warnTime = 0.9f;
    [Tooltip("潛伏時沙丘移動的速度 (m/s)")]
    public float lurkSpeed = 7f;
    [Tooltip("跳出地面咬到的傷害")]
    public float biteDamage = 18f;
    [Tooltip("咬人範圍（紅圈半徑）(m)")]
    public float biteRadius = 2.2f;
    public Color sandColor = new Color(0.86f, 0.7f, 0.45f);

    enum Phase { Egg, Hatching, Normal, Burrowing, Lurking, Emerging }
    Phase phase = Phase.Normal;

    EnemyAI ai;
    EnemyHealth health;
    EnemyCombat combat;
    ThirdPersonController controller;
    CharacterController cc;
    SpiderWebAttack web;
    PlayerHealth playerHealth;
    Renderer[] renderers;
    Collider[] colliders;

    GameObject egg;
    Transform eggBody;
    GameObject mound;
    ParticleSystem moundDust;
    float nextBurrowTime;
    static bool tipShown;

    // 專案關掉了進 Play 時重新載入腳本，static 變數要自己清，不然第二次 Play 就不會再顯示說明
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => tipShown = false;
    float tipTime = float.NegativeInfinity;

    public bool IsHidden => phase == Phase.Egg || phase == Phase.Hatching || phase == Phase.Burrowing || phase == Phase.Lurking;

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        health = GetComponent<EnemyHealth>();
        combat = GetComponent<EnemyCombat>();
        controller = GetComponent<ThirdPersonController>();
        cc = GetComponent<CharacterController>();
        web = GetComponent<SpiderWebAttack>();
        ApplyTuning();
    }

    /// <summary>
    /// 血量、大小、發現距離直接在程式裡調整：這樣不用改場景檔，
    /// 也不會因為 Unity 沒重新載入場景、或用舊場景存檔蓋掉而失效。
    /// </summary>
    void ApplyTuning()
    {
        if (health != null && healthMultiplier > 0f)
        {
            health.maxHealth *= healthMultiplier;
            health.barHeightOffset *= sizeMultiplier;
        }
        if (sizeMultiplier > 0f) transform.localScale *= sizeMultiplier;
        if (ai != null && alertRadius > 0f)
        {
            ai.alertRadius = alertRadius;
            ai.leashRadius = Mathf.Max(ai.leashRadius, alertRadius);
            ai.giveUpDistance = Mathf.Max(ai.giveUpDistance, alertRadius);
        }
    }

    void Start()
    {
        // EnemyHealth 在 Awake 時用舊的 Max Health 填了血，這裡補滿到調整後的血量
        if (health != null) health.HealFull();

        renderers = GetComponentsInChildren<Renderer>(true);
        var cols = new List<Collider>();
        foreach (var c in GetComponentsInChildren<Collider>(true))
            if (!(c is CharacterController)) cols.Add(c);
        colliders = cols.ToArray();

        playerHealth = PlayerHealth.Find();
        if (playerHealth != null) playerHealth.OnRespawned += OnPlayerRespawned;
        if (health != null) health.OnDamaged += OnDamaged;

        if (startInEgg) EnterEgg();
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= OnPlayerRespawned;
        if (health != null) health.OnDamaged -= OnDamaged;
        if (mound != null) Destroy(mound);
    }

    // =====================================================================
    //  卵
    // =====================================================================

    void EnterEgg()
    {
        phase = Phase.Egg;
        float width = EggWidth(); // 要在模型藏起來之前量大小
        Hide(true);
        SnapToGround(); // 場景裡的蜘蛛可能浮在半空中，卵要放在地上
        BuildEgg(width);
    }

    /// <summary>依照蜘蛛模型的大小決定卵的寬度</summary>
    float EggWidth()
    {
        var b = new Bounds(transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return Mathf.Clamp(Mathf.Max(b.size.x, b.size.z) * 0.75f, 0.9f, 3f);
    }

    /// <summary>把蜘蛛（和之後的卵）放到正下方的地面上：先找碰撞體，找不到再用地形高度</summary>
    void SnapToGround()
    {
        Vector3 p = transform.position;
        float ground = float.NegativeInfinity;
        // 自己的碰撞體在 Hide 時已經關掉，往下打一條線找到的第一個東西就是地面
        var hits = Physics.RaycastAll(p + Vector3.up * 0.5f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform) || h.collider is CharacterController) continue;
            if (h.point.y > ground) ground = h.point.y;
        }
        if (float.IsNegativeInfinity(ground))
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            ground = terrain.SampleHeight(p) + terrain.transform.position.y;
        }
        transform.position += Vector3.up * (ground - FeetY);
    }

    /// <summary>藏起來：AI、移動、碰撞、射絲都關掉，模型也看不到（還在卵裡 / 鑽進沙裡）</summary>
    void Hide(bool hideModel)
    {
        if (combat != null) combat.CancelAttack();
        controller.SetAIInput(Vector3.zero);
        controller.ResetMotion();
        ai.enabled = false;
        controller.enabled = false;
        cc.enabled = false;
        if (web != null) web.enabled = false;
        health.ForceShowBar = false;
        SetColliders(false);
        if (hideModel) SetVisible(false);
    }

    /// <summary>回到地面：恢復 AI、移動、碰撞、射絲和模型</summary>
    void Show()
    {
        SetVisible(true);
        SetColliders(true);
        cc.enabled = true;
        controller.enabled = true;
        controller.ResetMotion();
        if (web != null) web.enabled = true;
        ai.enabled = true;
    }

    void SetVisible(bool on)
    {
        foreach (var r in renderers) if (r != null) r.enabled = on;
    }

    void SetColliders(bool on)
    {
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    float FeetY => transform.position.y - (cc != null ? cc.height * 0.5f - cc.center.y : 1f);

    void BuildEgg(float width)
    {
        float height = width * 1.3f;

        var shell = MakeMaterial(eggColor, 0.55f);
        var spots = MakeMaterial(eggColor * 0.75f, 0.3f);
        var silk = MakeMaterial(new Color(0.95f, 0.95f, 0.92f, 1f), 0.2f);

        egg = new GameObject("SpiderEgg");
        egg.transform.SetParent(transform, false);
        egg.transform.position = new Vector3(transform.position.x, FeetY, transform.position.z);
        egg.transform.rotation = Quaternion.identity;

        // 底下一圈蛛絲
        Part(egg.transform, PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0f), new Vector3(width * 1.6f, 0.01f, width * 1.6f), silk, false);

        // 主要的卵（可以被打：碰撞體留著，打到會算在蜘蛛身上 → 直接孵化）
        eggBody = Part(egg.transform, PrimitiveType.Sphere, new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, width), shell, true);
        // 卵上的斑點
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad, y = Random.Range(0.25f, 0.75f);
            var p = new Vector3(Mathf.Cos(a) * 0.48f, y - 0.5f, Mathf.Sin(a) * 0.48f);
            var s = Part(eggBody, PrimitiveType.Sphere, p, new Vector3(0.18f, 0.12f, 0.18f), spots, false);
            s.localRotation = Quaternion.LookRotation(p.normalized);
        }
        // 旁邊兩顆小卵
        Part(egg.transform, PrimitiveType.Sphere, new Vector3(width * 0.62f, width * 0.25f, width * 0.2f), Vector3.one * width * 0.5f, shell, false);
        Part(egg.transform, PrimitiveType.Sphere, new Vector3(-width * 0.5f, width * 0.2f, -width * 0.42f), Vector3.one * width * 0.4f, shell, false);
    }

    void UpdateEgg()
    {
        if (playerHealth == null || playerHealth.IsDead) return;
        Vector3 d = playerHealth.transform.position - transform.position;
        float dy = Mathf.Abs(d.y);
        d.y = 0f;
        float dist = d.magnitude;

        // 玩家接近時卵開始抖動，越近抖越大
        if (eggBody != null)
        {
            float near = Mathf.Clamp01(1f - (dist - hatchRadius) / (hatchRadius * 0.8f));
            float wobble = Mathf.Sin(Time.time * 9f) * 6f * near;
            eggBody.localRotation = Quaternion.Euler(wobble, 0f, wobble * 0.6f);
        }
        if (dist <= hatchRadius && dy < 5f) StartCoroutine(Hatch());
    }

    IEnumerator Hatch()
    {
        phase = Phase.Hatching;
        GameAudio.Play("SFX/RPG/creak1", transform.position, 0.8f, 1.3f, 0.1f, 1f, 30f);

        // 劇烈抖動
        float t = 0f;
        Vector3 baseScale = eggBody != null ? eggBody.localScale : Vector3.one;
        while (t < 0.7f && eggBody != null)
        {
            t += Time.deltaTime;
            float s = 1f + Mathf.Sin(t * 40f) * 0.06f * (t / 0.7f); // 越來越鼓
            eggBody.localRotation = Quaternion.Euler(Mathf.Sin(t * 47f) * 14f, 0f, Mathf.Cos(t * 39f) * 14f);
            eggBody.localScale = new Vector3(baseScale.x * s, baseScale.y * (2f - s), baseScale.z * s);
            yield return null;
        }

        // 破殼
        Vector3 center = eggBody != null ? eggBody.position : transform.position + Vector3.up;
        CombatVFX.Impact(center, eggColor);
        CombatVFX.Puff(center, Vector3.up, new Color(0.7f, 0.9f, 0.45f, 0.8f));
        GameAudio.Play("SFX/RPG/dropLeather", center, 1f, 1.4f, 0.1f, 1f, 35f);
        SpawnShells(center);
        if (egg != null) Destroy(egg);

        Show();
        controller.AddImpulse(Vector3.up * 5f);
        yield return null;
        yield return null;
        phase = Phase.Normal;
        nextBurrowTime = Time.time + 1.5f; // 剛孵化不會馬上被打就鑽地
        ai.Alert(true);
    }

    void SpawnShells(Vector3 center)
    {
        var mat = MakeMaterial(eggColor, 0.5f);
        for (int i = 0; i < 7; i++)
        {
            var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.name = "EggShell";
            c.transform.position = center + Random.insideUnitSphere * 0.3f;
            c.transform.localScale = new Vector3(Random.Range(0.15f, 0.3f), 0.04f, Random.Range(0.15f, 0.3f));
            c.transform.rotation = Random.rotation;
            if (mat != null) c.GetComponent<Renderer>().sharedMaterial = mat;
            var rb = c.AddComponent<Rigidbody>();
            rb.mass = 0.1f;
            rb.linearVelocity = Random.insideUnitSphere * 3f + Vector3.up * 4f;
            rb.angularVelocity = Random.insideUnitSphere * 10f;
            Destroy(c, 2.5f);
        }
    }

    // =====================================================================
    //  鑽地 → 潛伏 → 紅圈 → 跳出咬人
    // =====================================================================

    void OnDamaged(float amount)
    {
        if (phase == Phase.Egg) { StartCoroutine(Hatch()); return; }
        if (phase != Phase.Normal || health.IsDead || Time.time < nextBurrowTime) return;
        if (playerHealth == null || playerHealth.IsDead) return;
        StartCoroutine(Burrow());
    }

    IEnumerator Burrow()
    {
        phase = Phase.Burrowing;
        nextBurrowTime = float.PositiveInfinity;
        Hide(false);

        // 一邊噴沙一邊往下沉
        Vector3 top = transform.position;
        Vector3 ground = new Vector3(top.x, FeetY, top.z);
        GameAudio.Play("SFX/roar_rock_crumble", ground, 0.5f, 1.6f, 0.1f, 1f, 35f);
        float t = 0f, nextPuff = 0f;
        while (t < burrowTime)
        {
            t += Time.deltaTime;
            transform.position = top + Vector3.down * (t / burrowTime) * 1.6f;
            if (t >= nextPuff)
            {
                nextPuff = t + 0.1f;
                CombatVFX.Puff(ground + Random.insideUnitSphere * 0.5f, Vector3.up, sandColor);
            }
            if (health.IsDead) { EndHidden(); yield break; }
            yield return null;
        }
        SetVisible(false);
        transform.position = top;
        CombatVFX.Ring(ground, sandColor, 1.8f);

        // 潛伏：沙丘朝玩家移動
        phase = Phase.Lurking;
        if (!tipShown) { tipShown = true; tipTime = Time.time; }
        CreateMound(ground);
        t = 0f;
        while (t < lurkTime)
        {
            if (health.IsDead || playerHealth == null || playerHealth.IsDead) { EndHidden(); yield break; }
            t += Time.deltaTime;
            Vector3 target = playerHealth.transform.position;
            Vector3 p = Vector3.MoveTowards(mound.transform.position, new Vector3(target.x, mound.transform.position.y, target.z), lurkSpeed * Time.deltaTime);
            p.y = GroundY(p);
            mound.transform.position = p;
            mound.transform.localScale = Vector3.one * (1f + Mathf.Sin(Time.time * 12f) * 0.08f);
            yield return null;
        }

        // 紅圈出現在玩家腳下，時間到從地底跳出來咬人（EnemyAI.BeginEmerge 負責紅圈和傷害）
        phase = Phase.Emerging;
        Vector3 at = playerHealth.transform.position;
        at.y = GroundY(at);
        if (mound != null) mound.transform.position = at;
        ai.stompDamage = biteDamage;
        ai.stompRadius = biteRadius;
        ai.enabled = true;
        ai.BeginEmerge(at, warnTime);
        GameAudio.Play("SFX/floor_warning", at, 0.6f, 1.4f, 0.05f, 1f, 30f);

        // 等它真的跳出來
        while (ai.IsUnderground)
        {
            if (health.IsDead) { EndHidden(); yield break; }
            yield return null;
        }

        if (mound != null) Destroy(mound);
        SetVisible(true);
        SetColliders(true);
        if (web != null) web.enabled = true;
        controller.AddImpulse(Vector3.up * 6f);
        CombatVFX.Ring(new Vector3(transform.position.x, FeetY, transform.position.z), sandColor, biteRadius);
        CombatVFX.Impact(transform.position + Vector3.up * 0.5f, GameUI.Danger);
        GameAudio.Play("SFX/RPG/knifeSlice2", transform.position, 0.9f, 0.7f, 0.08f, 1f, 30f);
        phase = Phase.Normal;
        nextBurrowTime = Time.time + burrowCooldown;
    }

    /// <summary>潛伏途中被打死（燃燒）或玩家死掉：收掉沙丘</summary>
    void EndHidden()
    {
        if (mound != null) Destroy(mound);
        if (health.IsDead)
        {
            phase = Phase.Normal;
            ai.enabled = true; // 讓 EnemyAI 進入死亡狀態
        }
    }

    float GroundY(Vector3 p)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
        return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 10f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y;
    }

    /// <summary>潛伏時地上那團隆起的沙丘（往上噴沙）</summary>
    void CreateMound(Vector3 pos)
    {
        mound = new GameObject("SpiderSandMound");
        mound.transform.position = pos;
        Part(mound.transform, PrimitiveType.Sphere, Vector3.zero, new Vector3(1.4f, 0.45f, 1.4f), MakeMaterial(sandColor * 0.9f, 0.05f), false);

        var go = new GameObject("Dust");
        go.transform.SetParent(mound.transform, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        moundDust = go.AddComponent<ParticleSystem>();
        moundDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = moundDust.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        main.startColor = new Color(sandColor.r, sandColor.g, sandColor.b, 0.9f);
        main.gravityModifier = 0.8f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = moundDust.emission;
        em.rateOverTime = 40f;
        var shape = moundDust.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.5f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        moundDust.Play();
    }

    // =====================================================================

    void Update()
    {
        if (phase == Phase.Egg) UpdateEgg();
    }

    void OnPlayerRespawned()
    {
        if (phase == Phase.Egg || phase == Phase.Hatching) return;
        if (phase == Phase.Normal) return; // EnemyAI 自己會處理
        StopAllCoroutines();
        if (mound != null) Destroy(mound);
        phase = Phase.Normal;
        if (health.IsDead) return;
        Show();
        ai.ResetEnemy();
        nextBurrowTime = Time.time + 2f;
    }

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (GameUI.OverlayOpen) return;

        // 第一次鑽地時的說明
        float since = Time.time - tipTime;
        if (since < 3.5f)
        {
            var style = new GUIStyle(GameUI.Center) { fontSize = 22 };
            GameUI.Shadowed(new Rect(0f, Screen.height * 0.2f, Screen.width, 32f), "蜘蛛鑽進沙裡了！看到腳下的紅圈就快閃開", style,
                            new Color(1f, 0.85f, 0.6f, Mathf.Clamp01((3.5f - since) / 0.5f)));
        }

        // 沙丘頭上：潛伏中 / 要出來了
        if ((phase != Phase.Lurking && phase != Phase.Emerging) || mound == null) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(mound.transform.position + Vector3.up * 1.4f);
        if (sp.z <= 0f) return;
        bool warn = phase == Phase.Emerging;
        string text = warn ? "！" : "▼ 潛伏中";
        var s = new GUIStyle(GameUI.Center) { fontSize = warn ? 34 : 16 };
        Color c = warn ? GameUI.Danger : new Color(1f, 0.85f, 0.6f);
        if (warn) c.a = 0.6f + 0.4f * Mathf.Sin(Time.time * 20f);
        GameUI.Shadowed(new Rect(sp.x - 80f, Screen.height - sp.y - 20f, 160f, 40f), text, s, c);
    }

    // ---------------- 小工具 ----------------

    static Transform Part(Transform parent, PrimitiveType type, Vector3 localPos, Vector3 scale, Material mat, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    static Material MakeMaterial(Color c, float smoothness)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        var m = new Material(shader);
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        return m;
    }
}
