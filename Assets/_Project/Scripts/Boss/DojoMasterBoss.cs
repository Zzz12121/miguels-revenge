using System.Collections;
using UnityEngine;

/// <summary>
/// 道場老師父的 Boss 戰（開場演出的對話結束、鏡頭飛回玩家之後由 DojoOpeningCutscene 呼叫 BeginFight 開打）。
/// 血條、受傷閃白、「強敵已擊敗」都用 BossHealth（跟森林島的百足駭骨同一套）；開打時播 Boss 音樂、倒下時停掉。
///
/// 招式（老師父只有走路 / 說話 / 跪坐的動畫，出招的姿勢用骨架疊加：手臂 IK + 上身扭轉 / 前傾）：
///   掌擊（近）  ：右手往後蓄力 → 往前滑步一掌（扇形範圍），二階段會變成左右三連掌
///   震地（中）  ：雙手高舉（地上出現紅圈預告）→ 雙掌拍地，震波一圈往外擴散；跳起來就能躲過
///   瞬步（遠）  ：一團煙消失、出現在玩家背後，馬上接一掌
///   血量低於 phase2Threshold → 二階段：動作更快、冷卻更短、掌擊變三連掌
/// 玩家死亡重生 → 老師父回到開打的位置、補滿血；玩家走回庭院（arenaRadius 內）待滿 startDelay 秒就重新開打。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BossHealth))]
public sealed class DojoMasterBoss : MonoBehaviour
{
    [Header("Boss")]
    public string bossName = "道場老師父";
    public float maxHealth = 3000f;
    [Tooltip("Boss 音樂（不填 = Resources/Audio/Music/boss_fight_music，跟森林島一樣）")]
    public AudioClip bossMusic;
    public float musicVolume = 0.5f;

    [Header("場地（開打時自動設成老師父當下的位置）")]
    public Vector3 arenaCenter;
    public float arenaRadius = 24f;
    [Tooltip("重生後玩家回到場內待滿幾秒重新開打")]
    public float startDelay = 1.5f;

    [Header("移動")]
    [Tooltip("追玩家的速度（公尺/秒）")]
    public float moveSpeed = 1.8f;
    [Tooltip("走路動畫原本的速度（公尺/秒），用來對腳步")]
    public float walkAnimMetersPerSecond = 0.71f;
    public float turnSpeed = 320f;

    [Header("掌擊")]
    public float palmRange = 2.6f;
    public float palmHalfAngle = 55f;
    public float palmDamage = 16f;
    public float palmWindup = 0.45f;
    [Tooltip("出掌時往前滑步幾公尺")]
    public float palmLunge = 1.4f;
    public float palmKnockback = 7f;
    [Tooltip("二階段三連掌每一下的傷害")]
    public float comboDamage = 10f;

    [Header("震地")]
    public float shockDamage = 20f;
    public float shockRadius = 8f;
    [Tooltip("震波擴散速度（公尺/秒）")]
    public float shockSpeed = 11f;
    public float shockWindup = 0.85f;
    public float shockCooldown = 7f;
    [Tooltip("震波經過時，玩家腳底離地超過這個高度就算跳過")]
    public float jumpClearance = 0.45f;

    [Header("瞬步")]
    [Tooltip("玩家離這麼遠以上才會瞬步")]
    public float flashStepDistance = 7f;
    public float flashStepCooldown = 5f;

    [Header("二階段")]
    [Range(0f, 1f)] public float phase2Threshold = 0.5f;
    public float phase2SpeedMultiplier = 1.3f;
    public float phase2CooldownMultiplier = 0.7f;

    public bool FightActive { get; private set; }
    public bool IsDefeated => dead;
    public event System.Action OnFightStarted, OnDefeated;

    static readonly int WalkState = Animator.StringToHash("Walk");
    static readonly int TalkState = Animator.StringToHash("Talk");
    static readonly int KneelState = Animator.StringToHash("Kneel");
    static readonly Color DustColor = new Color(0.95f, 0.88f, 0.7f, 0.8f);

    BossHealth health;
    Animator anim;
    PlayerHealth player;
    CharacterController playerBody;
    Transform hips, spine, rUpper, rLower, rHand, lUpper, lLower, lHand;
    Vector3 homePos;
    Quaternion homeRot;
    bool armed, dead, phase2, walkingAnim;
    float inArenaTimer, nextShock, nextFlash, yVelocity;
    Coroutine brain;

    // 骨架疊加的姿勢（都在老師父自己的座標系：x 右、y 上、z 前）
    enum Stance { None, Guard, WindupRight, StrikeRight, WindupLeft, StrikeLeft, Raise, Slam, Crouch }
    Stance stance = Stance.None;
    float stanceSpeed = 8f, ikWeight;
    Vector3 rTarget, lTarget;          // 現在的手的位置（慢慢追上目標姿勢）
    float twist, lean, crouch;

    float SpeedMul => phase2 ? phase2SpeedMultiplier : 1f;
    float Cd(float s) => s * (phase2 ? phase2CooldownMultiplier : 1f);
    bool PlayerAlive => player != null && !player.IsDead;

    void Awake()
    {
        health = GetComponent<BossHealth>();
        health.bossName = bossName;
        health.maxHealth = maxHealth;
        health.ResetHealth();
        health.ShowBar = false;
        health.Invulnerable = true;   // 演出 / 對話時打不到
        anim = GetComponentInChildren<Animator>();
        hips = Find("Hips"); spine = Find("Spine");
        rUpper = Find("Right_UpperArm"); rLower = Find("Right_LowerArm"); rHand = Find("Right_Hand");
        lUpper = Find("Left_UpperArm"); lLower = Find("Left_LowerArm"); lHand = Find("Left_Hand");
        // 會移動的碰撞體：kinematic Rigidbody，玩家的攻擊才打得到、也擋得住（跟百足駭骨一樣）
        var rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    void OnEnable() { health.OnDied += OnBossDied; }
    void OnDisable()
    {
        health.OnDied -= OnBossDied;
        if (player != null) { player.OnDied -= OnPlayerDied; player.OnRespawned -= OnPlayerRespawned; }
    }

    void FindPlayer()
    {
        if (player != null) return;
        player = PlayerHealth.Find();
        if (player == null) return;
        playerBody = player.GetComponent<CharacterController>();
        player.OnDied += OnPlayerDied;
        player.OnRespawned += OnPlayerRespawned;
    }

    // ===================== 開打 =====================

    /// <summary>開場演出結束時呼叫：從現在的位置開打</summary>
    public void BeginFight()
    {
        if (FightActive || dead) return;
        FindPlayer();
        if (!armed)
        {
            armed = true;
            homePos = transform.position;
            homeRot = transform.rotation;
            if (arenaCenter == Vector3.zero) arenaCenter = homePos;
        }
        FightActive = true;
        foreach (var c in GetComponentsInChildren<Collider>(true)) c.enabled = true;
        health.bossName = bossName;
        health.ShowBar = true;
        health.Invulnerable = false;
        phase2 = health.Health <= health.maxHealth * phase2Threshold;
        nextShock = Time.time + 3f;
        nextFlash = Time.time + 4f;
        GameAudio.PlayBattleMusic(GameAudio.Pick(bossMusic, "Music/boss_fight_music"), musicVolume, 1.5f);
        OnFightStarted?.Invoke();
        brain = StartCoroutine(Brain());
    }

    void Update()
    {
        if (dead || !armed || FightActive) return;
        // 玩家重生之後：回到庭院裡待滿 startDelay 秒就重新開打
        if (PlayerAlive && Flat(player.transform.position - arenaCenter).magnitude <= arenaRadius)
        {
            inArenaTimer += Time.deltaTime;
            if (inArenaTimer >= startDelay) { inArenaTimer = 0f; BeginFight(); }
        }
        else inArenaTimer = 0f;
    }

    IEnumerator Brain()
    {
        // 開場：轉向玩家、擺出架式
        SetStance(Stance.Guard, 6f);
        PlayTalkIdle();
        yield return FacePlayer(0.8f);
        yield return Wait(0.4f);

        while (FightActive && !dead)
        {
            if (!PlayerAlive) { StopWalking(); yield return null; continue; }

            if (!phase2 && health.Health <= health.maxHealth * phase2Threshold)
            {
                yield return EnterPhase2();
                continue;
            }

            float d = DistanceToPlayer();
            if (d >= flashStepDistance && Time.time >= nextFlash)
            {
                yield return FlashStep();
                yield return Palm(true, palmWindup * 0.6f, palmDamage);
                yield return Recover(0.5f);
            }
            else if (d <= palmRange)
            {
                if (phase2 && Random.value < 0.55f) yield return PalmCombo();
                else yield return Palm(true, palmWindup, palmDamage);
                yield return Recover(Random.Range(0.45f, 0.8f) / SpeedMul);
            }
            else if (d <= shockRadius * 0.85f && Time.time >= nextShock && Random.value < 0.6f)
            {
                yield return Shockwave();
                yield return Recover(0.7f / SpeedMul);
            }
            else yield return Chase(Random.Range(1.2f, 2.4f));
        }
    }

    // ===================== 招式 =====================

    IEnumerator Chase(float maxTime)
    {
        SetStance(Stance.Guard, 6f);
        float end = Time.time + maxTime;
        while (Time.time < end && PlayerAlive && !dead && FightActive)
        {
            float d = DistanceToPlayer();
            if (d <= palmRange * 0.85f) break;
            StartWalking();
            TurnTowardsPlayer(1f);
            Vector3 to = Flat(player.transform.position - transform.position).normalized;
            MoveTo(transform.position + to * moveSpeed * SpeedMul * Time.deltaTime, false);
            yield return null;
        }
        StopWalking();
    }

    /// <summary>掌擊：rightHand = 右手出掌（左手收在腰），windup = 蓄力秒數</summary>
    IEnumerator Palm(bool rightHand, float windup, float damage)
    {
        StopWalking();
        SetStance(rightHand ? Stance.WindupRight : Stance.WindupLeft, 9f);
        GameAudio.Play("SFX/RPG/cloth3", transform.position + Vector3.up, 0.7f, Random.Range(0.9f, 1.05f));
        float end = Time.time + windup / SpeedMul;
        while (Time.time < end && !dead) { TurnTowardsPlayer(1.4f); yield return null; }
        if (dead) yield break;

        // 出掌：一邊往前滑步、一邊把手推出去
        SetStance(rightHand ? Stance.StrikeRight : Stance.StrikeLeft, 28f);
        GameAudio.Play("SFX/RPG/knifeSlice2", transform.position + Vector3.up, 0.8f, Random.Range(0.75f, 0.85f));
        Vector3 dir = transform.forward;
        float lunge = palmLunge;
        if (PlayerAlive) lunge = Mathf.Clamp(DistanceToPlayer() - 0.9f, 0f, palmLunge); // 不撞進玩家身體
        Vector3 from = transform.position, to = from + dir * lunge;
        for (float t = 0f; t < 0.14f && !dead; t += Time.deltaTime)
        {
            MoveTo(Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.14f)), false);
            yield return null;
        }
        if (dead) yield break;
        MoveTo(to, false);
        Transform hand = rightHand ? rHand : lHand;
        Vector3 palmPos = hand != null ? hand.position : transform.position + Vector3.up * 1.3f + dir;
        CombatVFX.Puff(palmPos, dir, DustColor);
        if (PlayerAlive && InFan(palmRange, palmHalfAngle))
        {
            Hurt(damage, dir, "被老師父一掌打飛", palmKnockback, 3f);
            CombatVFX.Impact(palmPos + dir * 0.3f, new Color(1f, 0.85f, 0.55f));
        }
        yield return Wait(0.22f);
        SetStance(Stance.Guard, 7f);
    }

    IEnumerator PalmCombo()
    {
        yield return Palm(true, palmWindup * 0.75f, comboDamage);
        yield return Palm(false, 0.22f, comboDamage);
        yield return Palm(true, 0.3f, comboDamage * 1.5f);
    }

    IEnumerator Shockwave()
    {
        StopWalking();
        nextShock = Time.time + Cd(shockCooldown);
        yield return FacePlayer(0.3f);
        // 預告：雙手高舉、地上出現紅圈
        SetStance(Stance.Raise, 5f);
        GameAudio.Play("SFX/floor_warning", transform.position, 0.8f, 1f);
        var warn = MakeRing("DojoMaster_ShockWarning", new Color(1f, 0.25f, 0.15f, 0.55f), 0.12f);
        float end = Time.time + shockWindup / SpeedMul;
        while (Time.time < end && !dead)
        {
            float k = 1f - (end - Time.time) / (shockWindup / SpeedMul);
            DrawRing(warn, transform.position, shockRadius, 0.08f + 0.12f * k);
            yield return null;
        }
        if (warn != null) Destroy(warn.gameObject);
        if (dead) yield break;

        // 雙掌拍地：震波往外擴散
        SetStance(Stance.Slam, 30f);
        yield return Wait(0.08f);
        GameAudio.Play("SFX/roar_rock_crumble", transform.position, 0.9f, Random.Range(1.1f, 1.25f));
        ThirdPersonCamera.ShakeMain(0.35f, 0.5f);
        Vector3 center = transform.position;
        CombatVFX.Ring(center, DustColor, shockRadius * 0.6f);
        var wave = MakeRing("DojoMaster_Shockwave", new Color(1f, 0.85f, 0.55f, 0.9f), 0.35f);
        bool hit = false;
        float r = 0.5f;
        while (r < shockRadius && !dead)
        {
            float prev = r;
            r = Mathf.Min(shockRadius, r + shockSpeed * Time.deltaTime);
            DrawRing(wave, center, r, Mathf.Lerp(0.45f, 0.1f, r / shockRadius));
            if (!hit && PlayerAlive)
            {
                float pd = Flat(player.transform.position - center).magnitude;
                if (pd >= prev - 0.6f && pd <= r + 0.6f)
                {
                    hit = true; // 每次震波只判定一次
                    if (!PlayerAirborne()) Hurt(shockDamage, player.transform.position - center, "被震地波震飛", 9f, 6f);
                }
            }
            yield return null;
        }
        if (wave != null) Destroy(wave.gameObject);
        yield return Wait(0.3f);
        SetStance(Stance.Guard, 5f);
    }

    IEnumerator FlashStep()
    {
        StopWalking();
        nextFlash = Time.time + Cd(flashStepCooldown);
        SetStance(Stance.Crouch, 12f);
        yield return Wait(0.25f / SpeedMul);
        if (dead || !PlayerAlive) yield break;
        // 一團煙消失，出現在玩家背後（被牆擋住就改成側邊 / 前面）
        Vector3 p = player.transform.position;
        Vector3 back = -Flat(player.transform.forward).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, back);
        Vector3 dest = p + back * 1.9f;
        foreach (var c in new[] { p + back * 1.9f, p + side * 1.9f, p - side * 1.9f, p - back * 1.9f })
        {
            if (Flat(c - arenaCenter).magnitude > arenaRadius - 1f) continue;
            if (Physics.Linecast(p + Vector3.up, c + Vector3.up, out var block, ~0, QueryTriggerInteraction.Ignore) && !IsPlayerOrSelf(block.collider)) continue;
            dest = c; break;
        }
        CombatVFX.Puff(transform.position + Vector3.up * 0.9f, Vector3.up, DustColor);
        CombatVFX.Ring(transform.position, DustColor, 1.2f);
        GameAudio.Play("SFX/RPG/cloth4", transform.position + Vector3.up, 0.9f, 1.2f);
        MoveTo(dest, true);
        transform.rotation = Quaternion.LookRotation(Flat(p - dest).sqrMagnitude > 0.01f ? Flat(p - dest) : transform.forward);
        CombatVFX.Puff(transform.position + Vector3.up * 0.9f, Vector3.up, DustColor);
        GameAudio.Play("SFX/RPG/knifeSlice", transform.position + Vector3.up, 0.7f, 0.7f);
        SetStance(Stance.Guard, 12f);
    }

    IEnumerator EnterPhase2()
    {
        phase2 = true;
        StopWalking();
        SetStance(Stance.Raise, 4f);
        GameAudio.Play("SFX/roar_rock_crumble", transform.position, 1f, 0.8f);
        ThirdPersonCamera.ShakeMain(0.3f, 1f);
        CombatVFX.Ring(transform.position, new Color(1f, 0.6f, 0.3f, 0.9f), 4f);
        yield return Wait(1f);
        SetStance(Stance.Guard, 6f);
        nextFlash = Time.time;
    }

    IEnumerator Recover(float seconds)
    {
        StopWalking();
        PlayTalkIdle();
        float end = Time.time + seconds;
        while (Time.time < end && !dead) { TurnTowardsPlayer(0.6f); yield return null; }
    }

    // ===================== 傷害 / 死亡 / 重來 =====================

    void Hurt(float damage, Vector3 pushDir, string reason, float knockback, float knockUp)
    {
        pushDir.y = 0f;
        Vector3 knock = (pushDir.sqrMagnitude > 0.001f ? pushDir.normalized : transform.forward) * knockback + Vector3.up * knockUp;
        if (player.TakeDamage(damage, knock, false, reason)) ThirdPersonCamera.ShakeMain(0.2f, 0.25f);
    }

    bool InFan(float range, float halfAngle)
    {
        Vector3 to = Flat(player.transform.position - transform.position);
        return to.magnitude <= range && Vector3.Angle(transform.forward, to) <= halfAngle;
    }

    bool PlayerAirborne()
    {
        float feet = playerBody != null ? playerBody.bounds.min.y : player.transform.position.y;
        return feet - GroundAt(player.transform.position, feet + 0.3f, feet) > jumpClearance;
    }

    void OnBossDied()
    {
        dead = true;
        FightActive = false;
        StopAllCoroutines();
        StopWalking();
        SetStance(Stance.None, 4f);
        if (anim != null)
        {
            anim.speed = 1f;
            anim.CrossFadeInFixedTime(anim.HasState(0, KneelState) ? KneelState : TalkState, 0.5f, 0, 0f);
        }
        GameAudio.StopMusic(2f);
        GameAudio.Play("SFX/victory_stinger", transform.position, 0.8f, 1f, 0f, 1f, 200f);
        ThirdPersonCamera.ShakeMain(0.4f, 0.8f);
        OnDefeated?.Invoke();
    }

    void OnPlayerDied()
    {
        if (dead || !FightActive) return;
        FightActive = false;
        StopAllCoroutines();
        StopWalking();
        SetStance(Stance.Guard, 4f);
        PlayTalkIdle();
    }

    void OnPlayerRespawned()
    {
        if (dead || !armed) return;
        // 重新來過：回到開打的位置、補滿血，玩家走回庭院就重新開打
        StopAllCoroutines();
        FightActive = false;
        transform.SetPositionAndRotation(homePos, homeRot);
        health.ResetHealth();
        health.ShowBar = false;
        health.Invulnerable = false;
        phase2 = false;
        inArenaTimer = 0f;
        SetStance(Stance.Guard, 6f);
        PlayTalkIdle();
        GameAudio.StopMusic(1f);
    }

    // ===================== 移動 / 動畫 =====================

    void StartWalking()
    {
        if (anim == null) return;
        anim.speed = Mathf.Clamp(moveSpeed * SpeedMul / Mathf.Max(0.1f, walkAnimMetersPerSecond), 0.5f, 3f);
        if (!walkingAnim) anim.CrossFadeInFixedTime(WalkState, 0.15f, 0, 0f);
        walkingAnim = true;
    }

    void StopWalking()
    {
        if (!walkingAnim) return;
        walkingAnim = false;
        PlayTalkIdle();
    }

    void PlayTalkIdle()
    {
        if (anim == null) return;
        anim.speed = 0.35f; // 說話動畫放慢當作站著的待機（手臂由架式蓋掉）
        anim.CrossFadeInFixedTime(TalkState, 0.2f, 0, 0f);
    }

    void MoveTo(Vector3 p, bool snap)
    {
        Vector3 off = Flat(p - arenaCenter);
        float limit = arenaRadius - 1f;
        if (off.magnitude > limit) p = arenaCenter + off.normalized * limit + Vector3.up * p.y;
        float ground = GroundAt(p, transform.position.y + 1.2f, transform.position.y);
        if (snap) { p.y = ground; yVelocity = 0f; }
        else p.y = Mathf.SmoothDamp(transform.position.y, ground, ref yVelocity, 0.2f, 3f, Time.deltaTime);
        transform.position = p;
    }

    float GroundAt(Vector3 p, float fromY, float fallback)
    {
        float best = float.NegativeInfinity;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, fromY, p.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (IsPlayerOrSelf(h.collider)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    bool IsPlayerOrSelf(Collider c)
    {
        if (c == null) return true;
        if (c.transform.IsChildOf(transform)) return true;
        return player != null && c.transform.IsChildOf(player.transform);
    }

    void TurnTowardsPlayer(float factor)
    {
        if (!PlayerAlive) return;
        Vector3 to = Flat(player.transform.position - transform.position);
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * factor * SpeedMul * Time.deltaTime);
    }

    IEnumerator FacePlayer(float maxTime)
    {
        float end = Time.time + maxTime;
        while (Time.time < end && PlayerAlive && !dead)
        {
            Vector3 to = Flat(player.transform.position - transform.position);
            if (to.sqrMagnitude < 0.01f || Vector3.Angle(transform.forward, to) < 5f) break;
            TurnTowardsPlayer(1.5f);
            yield return null;
        }
    }

    IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end && !dead) yield return null;
    }

    float DistanceToPlayer() => PlayerAlive ? Flat(player.transform.position - transform.position).magnitude : float.PositiveInfinity;

    // ===================== 骨架疊加的出招姿勢 =====================

    void SetStance(Stance s, float speed) { stance = s; stanceSpeed = speed; }

    void LateUpdate()
    {
        bool active = (FightActive || armed) && !dead && stance != Stance.None;
        ikWeight = Mathf.MoveTowards(ikWeight, active ? 1f : 0f, Time.deltaTime * 4f);
        if (ikWeight <= 0.001f || rHand == null || lHand == null || spine == null) return;

        // 目標姿勢（老師父座標系，身高約 1.75 m）：右手 / 左手的位置、上身扭轉（+ = 往右轉）、前傾、下蹲
        Vector3 rGoal, lGoal; float twistGoal, leanGoal, crouchGoal;
        switch (stance)
        {
            case Stance.WindupRight: rGoal = new Vector3(0.28f, 1.02f, -0.12f); lGoal = new Vector3(-0.05f, 1.3f, 0.45f); twistGoal = 25f; leanGoal = -4f; crouchGoal = 0.08f; break;
            case Stance.StrikeRight: rGoal = new Vector3(0.04f, 1.3f, 0.78f); lGoal = new Vector3(-0.26f, 1.0f, -0.02f); twistGoal = -22f; leanGoal = 14f; crouchGoal = 0.12f; break;
            case Stance.WindupLeft: rGoal = new Vector3(0.05f, 1.3f, 0.45f); lGoal = new Vector3(-0.28f, 1.02f, -0.12f); twistGoal = -25f; leanGoal = -4f; crouchGoal = 0.08f; break;
            case Stance.StrikeLeft: rGoal = new Vector3(0.26f, 1.0f, -0.02f); lGoal = new Vector3(-0.04f, 1.3f, 0.78f); twistGoal = 22f; leanGoal = 14f; crouchGoal = 0.12f; break;
            case Stance.Raise: rGoal = new Vector3(0.25f, 1.85f, 0.12f); lGoal = new Vector3(-0.25f, 1.85f, 0.12f); twistGoal = 0f; leanGoal = -8f; crouchGoal = 0f; break;
            case Stance.Slam: rGoal = new Vector3(0.3f, 0.6f, 0.5f); lGoal = new Vector3(-0.3f, 0.6f, 0.5f); twistGoal = 0f; leanGoal = 32f; crouchGoal = 0.2f; break;
            case Stance.Crouch: rGoal = new Vector3(0.2f, 1.1f, 0.25f); lGoal = new Vector3(-0.2f, 1.1f, 0.25f); twistGoal = 0f; leanGoal = 18f; crouchGoal = 0.2f; break;
            default: rGoal = new Vector3(0.16f, 1.25f, 0.38f); lGoal = new Vector3(-0.1f, 1.12f, 0.3f); twistGoal = 8f; leanGoal = 5f; crouchGoal = 0.04f; break; // 架式
        }
        if (stance == Stance.None) { rGoal = rTarget; lGoal = lTarget; twistGoal = leanGoal = crouchGoal = 0f; }
        float k = 1f - Mathf.Exp(-stanceSpeed * Time.deltaTime);
        if (rTarget == Vector3.zero) { rTarget = rGoal; lTarget = lGoal; }
        rTarget = Vector3.Lerp(rTarget, rGoal, k);
        lTarget = Vector3.Lerp(lTarget, lGoal, k);
        twist = Mathf.Lerp(twist, twistGoal, k);
        lean = Mathf.Lerp(lean, leanGoal, k);
        crouch = Mathf.Lerp(crouch, crouchGoal, k);

        Vector3 up = Vector3.up, right = transform.right;
        if (hips != null) hips.position -= up * (crouch * ikWeight);
        spine.rotation = Quaternion.AngleAxis(twist * ikWeight, up) * Quaternion.AngleAxis(lean * ikWeight, right) * spine.rotation;
        Arm(rUpper, rLower, rHand, rTarget, 1f);
        Arm(lUpper, lLower, lHand, lTarget, -1f);
    }

    void Arm(Transform upper, Transform lower, Transform hand, Vector3 localGoal, float side)
    {
        if (upper == null || lower == null || hand == null) return;
        Vector3 goal = transform.TransformPoint(localGoal);
        goal = Vector3.Lerp(hand.position, goal, ikWeight);
        Vector3 pole = upper.position + transform.right * (0.5f * side) - Vector3.up * 0.45f - transform.forward * 0.15f; // 手肘往外、往下
        SolveTwoBone(upper, lower, hand, goal, pole);
    }

    static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        Vector3 pa = a.position;
        float la = Vector3.Distance(pa, b.position), lb = Vector3.Distance(b.position, c.position);
        if (la < 0.0001f || lb < 0.0001f) return;
        Vector3 toTarget = target - pa;
        float d = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(la - lb) + 0.001f, la + lb - 0.001f);
        Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : (c.position - pa).normalized;
        Vector3 bend = Vector3.ProjectOnPlane(pole - pa, dir);
        if (bend.sqrMagnitude < 1e-8f) bend = Vector3.ProjectOnPlane(b.position - pa, dir);
        bend.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        Vector3 elbow = pa + dir * (cosA * la) + bend * (Mathf.Sqrt(1f - cosA * cosA) * la);
        a.rotation = Quaternion.FromToRotation(b.position - pa, elbow - pa) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, pa + dir * d - b.position) * b.rotation;
    }

    // ===================== 地上的圈 =====================

    static Material ringMaterial;

    LineRenderer MakeRing(string name, Color color, float width)
    {
        var go = new GameObject(name);
        var lr = go.AddComponent<LineRenderer>();
        if (ringMaterial == null) ringMaterial = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);
        lr.sharedMaterial = ringMaterial;
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.positionCount = 64;
        lr.startColor = lr.endColor = color;
        lr.startWidth = lr.endWidth = width;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    void DrawRing(LineRenderer lr, Vector3 center, float radius, float width)
    {
        if (lr == null) return;
        lr.startWidth = lr.endWidth = width;
        for (int i = 0; i < lr.positionCount; i++)
        {
            float a = i / (float)lr.positionCount * Mathf.PI * 2f;
            Vector3 p = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            p.y = center.y + 0.06f;
            lr.SetPosition(i, p);
        }
    }

    // ===================== 小工具 =====================

    Transform Find(string boneName) => FindDeep(transform, boneName);

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            var found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
