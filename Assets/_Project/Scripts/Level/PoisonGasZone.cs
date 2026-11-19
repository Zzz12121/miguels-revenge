using UnityEngine;

/// <summary>
/// 毒氣區：一團會飄動的綠色毒霧（程式產生粒子，不用做 Prefab）。
/// 玩家走進去的那一下會被嗆到（PlayerHitReaction.PoisonGas：摀住口鼻、全身狂抖），
/// 之後每 tickInterval 秒扣一點血；嗆到的姿勢最快每 reactionCooldown 秒才會再出現一次，玩家有機會逃出去。
///
/// 用法：空物件 Add Component → PoisonGasZone，調 radius 就好。
/// lifetime > 0 時會自己消散刪掉（給敵人放毒氣彈用：new GameObject().AddComponent&lt;PoisonGasZone&gt;()）。
/// </summary>
public class PoisonGasZone : MonoBehaviour
{
    [Tooltip("毒氣範圍半徑 (m)")]
    public float radius = 3f;
    [Tooltip("毒氣有多高 (m)，超過這個高度（例如跳起來）不會吸到")]
    public float height = 2.2f;
    [Tooltip("每次扣多少血")]
    public float tickDamage = 4f;
    [Tooltip("待在裡面每隔幾秒扣一次血")]
    public float tickInterval = 0.8f;
    [Tooltip("嗆到的姿勢最快每隔幾秒出現一次")]
    public float reactionCooldown = 3.5f;
    [Tooltip("幾秒後消散並刪除；0 = 永久")]
    public float lifetime = 0f;
    public Color gasColor = new Color(0.38f, 0.75f, 0.2f, 0.42f);

    PlayerHealth player;
    float nextTick, nextReaction;
    float bornAt;
    ParticleSystem cloud;
    static Material gasMaterial;

    void Start()
    {
        bornAt = Time.time;
        player = PlayerHealth.Find();
        BuildCloud();
    }

    void Update()
    {
        if (lifetime > 0f && Time.time - bornAt > lifetime)
        {
            if (cloud != null) cloud.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 2.5f);
            enabled = false;
            return;
        }
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }
        if (player.IsDead || !Inside(player)) return;
        if (Time.time < nextTick) return;
        nextTick = Time.time + tickInterval;

        bool react = Time.time >= nextReaction;
        if (player.TakeDamage(tickDamage, Vector3.zero, false, "吸入毒氣", react,
                              react ? PlayerHitReaction.PoisonGas : PlayerHitReaction.Auto) && react)
            nextReaction = Time.time + reactionCooldown;
    }

    bool Inside(PlayerHealth p)
    {
        Vector3 d = p.transform.position - transform.position;
        float feet = p.FeetY - transform.position.y;
        d.y = 0f;
        return d.sqrMagnitude <= radius * radius && feet < height && feet > -1f;
    }

    void BuildCloud()
    {
        var go = new GameObject("VFX_PoisonGas");
        go.transform.SetParent(transform, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        cloud = go.AddComponent<ParticleSystem>();
        cloud.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = cloud.main;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.45f, radius * 0.9f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = gasColor;
        main.gravityModifier = -0.015f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80; // 半透明大片粒子很吃 overdraw，數量壓低

        var emission = cloud.emission;
        emission.rateOverTime = 18f;

        var shape = cloud.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = radius * 0.75f;

        var rot = cloud.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

        var noise = cloud.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.35f;
        noise.scrollSpeed = 0.2f;

        // 淡入 → 維持 → 淡出
        var col = cloud.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 1f, 0.7f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var size = cloud.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.25f));

        var r = go.GetComponent<ParticleSystemRenderer>();
        if (gasMaterial == null) gasMaterial = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        r.sharedMaterial = gasMaterial;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.sortingFudge = 2f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        cloud.Play();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.2f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
