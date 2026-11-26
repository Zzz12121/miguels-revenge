using UnityEngine;

/// <summary>
/// 玩家的動作音效（只加聲音，不改任何動作）：
///   起跳：一陣上揚的風聲；二段跳：再高一點的風聲
///   落地：在空中待夠久才有「咚」一聲，掉得越快越重
///   揮刀：在原本的刀聲底下墊一層風切聲
///   打中：在原本的「砍中」聲底下墊一層悶響，傷害高的換成更重的那種
/// 音效檔在 Resources/Audio/SFX/Gen/（`Art/Audio/synth_sfx.py` 合成的）。PlayerCombat 開場時自動掛上。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerSfx : MonoBehaviour
{
    [Range(0f, 2f)] public float jumpVolume = 0.55f;
    [Range(0f, 2f)] public float landVolume = 0.7f;
    [Range(0f, 2f)] public float swingVolume = 0.45f;
    [Range(0f, 2f)] public float hitVolume = 0.9f;
    [Tooltip("傷害超過這個值就用比較重的打擊聲")]
    public float heavyHitDamage = 30f;
    [Tooltip("在空中至少待這麼久，落地才有聲音（走下小台階不會咚一聲）")]
    public float minAirTime = 0.32f;

    static readonly string[] Hits = { "SFX/Gen/hit_impact_1", "SFX/Gen/hit_impact_2", "SFX/Gen/hit_impact_3", "SFX/Gen/hit_impact_4" };
    static readonly string[] HeavyHits = { "SFX/Gen/hit_heavy_1", "SFX/Gen/hit_heavy_2" };
    static readonly string[] Swings = { "SFX/Gen/swing_whoosh_1", "SFX/Gen/swing_whoosh_2", "SFX/Gen/swing_whoosh_3" };
    static readonly string[] Jumps = { "SFX/Gen/jump_whoosh_1", "SFX/Gen/jump_whoosh_2" };
    static readonly string[] Lands = { "SFX/Gen/jump_land_1", "SFX/Gen/jump_land_2" };

    ThirdPersonController controller;
    CharacterController body;
    MeleeCombat melee;
    PlayerHealth health;
    bool wasGrounded = true;
    float airborneSince, lastHitSound, lastJumpSound;
    float lowestVy;

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        body = GetComponent<CharacterController>();
        melee = GetComponent<MeleeCombat>();
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        if (controller != null) controller.OnDoubleJump += DoubleJump;
        if (melee != null) melee.OnSwingActive += Swing;
        PlayerCombat.OnHitLanded += HitLanded;
    }

    void OnDisable()
    {
        if (controller != null) controller.OnDoubleJump -= DoubleJump;
        if (melee != null) melee.OnSwingActive -= Swing;
        PlayerCombat.OnHitLanded -= HitLanded;
    }

    void Update()
    {
        if (body == null || !body.enabled || Time.timeScale <= 0f) return;
        bool grounded = body.isGrounded;
        float vy = body.velocity.y;
        if (wasGrounded && !grounded)
        {
            airborneSince = Time.time;
            lowestVy = 0f;
            // 往上離地 = 起跳（走下台階、被擊飛往下掉的不算）
            if (vy > 2.5f && !Dead) Jump(1f);
        }
        if (!grounded) lowestVy = Mathf.Min(lowestVy, vy);
        if (!wasGrounded && grounded && !Dead)
        {
            float air = Time.time - airborneSince;
            if (air >= minAirTime)
            {
                float speed = -lowestVy;
                float k = Mathf.InverseLerp(3f, 16f, speed);
                string clip = k > 0.7f ? "SFX/Gen/jump_land_heavy" : Lands[Random.Range(0, Lands.Length)];
                GameAudio.Play(clip, transform.position, landVolume * Mathf.Lerp(0.45f, 1.1f, k), Mathf.Lerp(1.12f, 0.92f, k), 0.06f, 1f, 25f);
            }
        }
        wasGrounded = grounded;
    }

    bool Dead => health != null && health.IsDead;

    void Jump(float pitch)
    {
        if (Time.time - lastJumpSound < 0.12f) return;
        lastJumpSound = Time.time;
        GameAudio.Play(Jumps[Random.Range(0, Jumps.Length)], transform.position + Vector3.up, jumpVolume, pitch, 0.06f, 1f, 25f);
    }

    void DoubleJump() => Jump(1.2f);

    void Swing(int combo)
    {
        float pitch = 1f + 0.06f * Mathf.Clamp(combo, 0, 4);
        GameAudio.Play(Swings[Random.Range(0, Swings.Length)], transform.position + Vector3.up, swingVolume, pitch, 0.08f, 1f, 25f);
    }

    void HitLanded(IDamageable target, Vector3 point, float damage, bool flag)
    {
        // 一刀砍中好幾個敵人時只墊一次，不然會疊得很大聲
        if (Time.time - lastHitSound < 0.05f) return;
        lastHitSound = Time.time;
        bool heavy = flag || damage >= heavyHitDamage; // flag = 重擊（第三刀、跳劈這類）
        var names = heavy ? HeavyHits : Hits;
        GameAudio.Play(names[Random.Range(0, names.Length)], point, hitVolume * (heavy ? 1.1f : 1f), heavy ? 0.95f : 1.05f, 0.08f, 1f, 35f);
    }
}
