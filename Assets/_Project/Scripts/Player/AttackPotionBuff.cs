using UnityEngine;

/// <summary>
/// 藥師給的壯力藥（喝下之後一段時間攻擊力提高）：
///   - 喝下時 Begin(ratio, 秒數)：PlayerCombat.damageMultiplier 變成原本的 (1 + ratio) 倍（近戰、弓箭、技能都吃這個倍率），時間到再減回來
///   - 身上冒出淡淡的紅黑色火焰（紅色的火苗 + 黑色的煙，從身體表面冒出來往上飄），剛喝下時比較旺、快結束時變淡
///   - 不會被存進存檔、帶到下一關變成永久的：PlayerState.Capture 讀倍率時會扣掉這個加成（PlayerState.TemporaryDamageBonus），
///     換場景後新的玩家身上再重新加上，火焰也跟著重新掛
/// 自動建立（DontDestroyOnLoad），換場景不會中斷。
/// </summary>
public class AttackPotionBuff : MonoBehaviour
{
    static AttackPotionBuff instance;

    public static bool Active => instance != null && instance.remaining > 0f;
    public static float Remaining => instance != null ? instance.remaining : 0f;
    public static float Bonus => instance != null && instance.remaining > 0f ? instance.bonus : 0f;

    float ratio, bonus, remaining, duration; // bonus = 實際加上去的量（原本的倍率 × ratio）
    PlayerCombat appliedTo;
    ParticleSystem flames, smoke;
    Transform fxOwner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        PlayerState.TemporaryDamageBonus = null;
    }

    /// <summary>喝下藥：攻擊力變成 (1 + ratio) 倍（0.3 = 多 30%），持續 seconds 秒；還在藥效中就重新計時</summary>
    public static void Begin(float ratio, float seconds)
    {
        if (instance == null)
        {
            var go = new GameObject("AttackPotionBuff");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<AttackPotionBuff>();
        }
        instance.Remove();
        instance.ratio = ratio;
        instance.remaining = instance.duration = seconds;
        instance.Apply();
        instance.burst = 1f;
        PlayerState.TemporaryDamageBonus = p =>
        {
            if (instance == null || instance.appliedTo == null || p == null) return 0f;
            return p.GetComponent<PlayerCombat>() == instance.appliedTo ? instance.bonus : 0f;
        };
    }

    float burst;

    void Apply()
    {
        var hp = PlayerHealth.Find();
        var combat = hp != null ? hp.GetComponent<PlayerCombat>() : null;
        if (combat == null) return;
        bonus = combat.damageMultiplier * ratio;
        combat.damageMultiplier += bonus;
        appliedTo = combat;
        AttachFx(hp.transform);
    }

    void Remove()
    {
        if (appliedTo != null) appliedTo.damageMultiplier = Mathf.Max(0.01f, appliedTo.damageMultiplier - bonus);
        appliedTo = null;
    }

    void Update()
    {
        if (remaining <= 0f) return;
        remaining -= Time.deltaTime;
        burst = Mathf.MoveTowards(burst, 0f, Time.deltaTime / 2.5f);
        // 換了場景（玩家換了一個）：舊的不用管（已經不在了），新的加上去、火焰重新掛
        if (appliedTo == null && (GameManager.Instance == null || !GameManager.Instance.IsLoading)) Apply();
        if (remaining <= 0f)
        {
            Remove();
            StopFx();
            PlayerState.TemporaryDamageBonus = null;
            return;
        }
        UpdateFx();
    }

    void OnDestroy()
    {
        Remove();
        if (instance == this) instance = null;
    }

    // ---------------------------------------------------------------- 紅黑火焰

    static Material flameMat, smokeMat;

    void AttachFx(Transform player)
    {
        if (fxOwner == player && flames != null) return;
        StopFx();
        fxOwner = player;
        var smr = BodyRenderer(player);
        flames = MakeSystem("PotionFlames", player, smr, false);
        smoke = MakeSystem("PotionSmoke", player, smr, true);
    }

    static SkinnedMeshRenderer BodyRenderer(Transform player)
    {
        SkinnedMeshRenderer best = null;
        float size = 0f;
        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!r.enabled || r.sharedMesh == null) continue;
            float s = r.bounds.size.sqrMagnitude;
            if (s > size) { size = s; best = r; }
        }
        return best;
    }

    ParticleSystem MakeSystem(string name, Transform player, SkinnedMeshRenderer smr, bool isSmoke)
    {
        var go = new GameObject(name);
        go.transform.SetParent(player, false);
        go.transform.localPosition = Vector3.up * 1f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.World; // 飄出去的火不跟著身體動
        main.startLifetime = isSmoke ? new ParticleSystem.MinMaxCurve(0.9f, 1.5f) : new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
        main.startSpeed = isSmoke ? new ParticleSystem.MinMaxCurve(0.05f, 0.2f) : new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        main.startSize = isSmoke ? new ParticleSystem.MinMaxCurve(0.2f, 0.38f) : new ParticleSystem.MinMaxCurve(0.1f, 0.22f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = isSmoke ? -0.06f : -0.12f; // 往上飄
        main.maxParticles = 200;
        main.startColor = Color.white;

        var em = ps.emission;
        em.rateOverTime = isSmoke ? 9f : 22f;

        var shape = ps.shape;
        if (smr != null)
        {
            shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
            shape.skinnedMeshRenderer = smr;
            shape.normalOffset = 0.03f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.45f, 1.6f, 0.3f);
        }

        // 顏色：火苗是暗紅 → 朱紅 → 消失（半透明，不是刺眼的亮光）；煙是很淡的黑
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        if (isSmoke)
            g.SetKeys(new[] { new GradientColorKey(new Color(0.08f, 0.03f, 0.03f), 0f), new GradientColorKey(new Color(0.02f, 0.01f, 0.01f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.4f, 0.25f), new GradientAlphaKey(0f, 1f) });
        else
            g.SetKeys(new[] { new GradientColorKey(new Color(0.55f, 0.04f, 0.03f), 0f), new GradientColorKey(new Color(0.95f, 0.22f, 0.08f), 0.35f), new GradientColorKey(new Color(0.2f, 0.02f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.62f, 0.2f), new GradientAlphaKey(0.3f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, isSmoke ? AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.6f) : new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.2f)));

        // 火苗左右飄忽
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = isSmoke ? 0.25f : 0.4f;
        noise.frequency = 1.2f;
        noise.scrollSpeed = 0.6f;

        var r = go.GetComponent<ParticleSystemRenderer>();
        // 火苗拉長成往上竄的火舌；煙還是一團團的
        if (isSmoke) r.renderMode = ParticleSystemRenderMode.Billboard;
        else
        {
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 2.2f;
            r.velocityScale = 0.15f;
        }
        r.sortMode = ParticleSystemSortMode.Distance;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (isSmoke) { smokeMat ??= BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false); r.sharedMaterial = smokeMat; }
        else { flameMat ??= BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false); r.sharedMaterial = flameMat; }
        ps.Play();
        return ps;
    }

    void UpdateFx()
    {
        if (fxOwner == null || flames == null) return;
        // 剛喝下時旺一點（burst），快結束的最後 10 秒慢慢變淡
        float fade = Mathf.Clamp01(remaining / 10f);
        float k = (0.55f + 0.45f * burst) * fade;
        var em = flames.emission;
        em.rateOverTime = 40f * k + 20f * burst;
        var em2 = smoke.emission;
        em2.rateOverTime = 12f * k;
    }

    void StopFx()
    {
        if (flames != null) { flames.Stop(); Destroy(flames.gameObject, 2f); }
        if (smoke != null) { smoke.Stop(); Destroy(smoke.gameObject, 2.5f); }
        flames = smoke = null;
        fxOwner = null;
    }
}
