using System.Collections;
using UnityEngine;

/// <summary>
/// 百足駭骨自己的 Boss AI（不依賴石像 Boss 的 BossController）。
///
/// 戰鬥流程：玩家進到競技場 → 吼叫開場、顯示血條、播 Boss 音樂 → 追擊並依距離選招：
///   近距離：右鐮橫掃 Slash / 四連斬 SweepCombo / 撲咬 Bite；玩家繞到側後方 → 甩尾 TailWhip（可以跳過）
///   中距離：衝刺 Dash（蓄力後直線衝過去，收尾雙鐮上撈）
///   遠距離：遠程能量彈 Ranged
///   技能：殘影圍殺（SkeletonCentipedeEncircleSkill）、蝠群襲擊（SkeletonCentipedeBatSwarmSkill），
///         蝠群護體（SkeletonCentipedeBatGuardSkill）由血量門檻自動發動，AI 會等它結束。
///   血量低於 phase2Threshold → 吼叫進入二階段：移動更快、冷卻更短。
/// 傷害判定時機用動畫事件（driver.AttackImpact），全部走 PlayerHealth.TakeDamage（翻滾無敵 / 格擋照常有效）。
/// 玩家死亡重生 → Boss 回到起點、補滿血，玩家回到場內後重新開打。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
[RequireComponent(typeof(BossHealth))]
public class SkeletonCentipedeBossAI : MonoBehaviour
{
    [Header("競技場")]
    [Tooltip("競技場中心（通常是 ArenaFloor 的位置）")]
    public Vector3 arenaCenter;
    [Tooltip("競技場半徑，Boss 不會走出（半徑 - edgeMargin）")]
    public float arenaRadius = 36f;
    public float edgeMargin = 5f;
    [Tooltip("玩家進入（半徑 + 這個值）以內就開打")]
    public float aggroPadding = 0f;
    public float startDelay = 1.5f;

    [Header("移動")]
    public float moveSpeed = 6f;
    public float turnSpeed = 140f;
    [Tooltip("和玩家保持的最近距離（從 Boss 中心算）")]
    public float stopDistance = 5f;
    public Vector2 chaseTimeRange = new Vector2(1.2f, 2.6f);
    public Vector2 recoverTimeRange = new Vector2(0.25f, 0.7f);

    [Header("近戰")]
    public float meleeRange = 8f;
    public float slashDamage = 18f;
    public float slashRange = 8f;
    public float slashHalfAngle = 70f;
    public float sweepDamage = 12f;
    public float sweepFinisherDamage = 26f;
    public float sweepCooldown = 7f;
    [Tooltip("四連斬：玩家離 Boss 超過這個距離，就邊揮邊往前追到這個距離（讓每一下都砍得到）")]
    public float sweepReach = 5.5f;
    public float sweepLungeSpeed = 11f;
    [Tooltip("四連斬前三下的擊退（很輕，玩家才不會被推出範圍）；第四下重砍用 meleeKnockback")]
    public float sweepLightKnockback = 1.5f;
    public float biteDamage = 22f;
    public float biteRange = 7.5f;
    public float tailDamage = 20f;
    public float tailRadius = 9f;
    [Tooltip("甩尾時玩家腳底離地超過這個高度就算跳過")]
    public float tailJumpFeetClearance = 0.6f;
    public float tailCooldown = 5f;
    public float meleeKnockback = 9f;

    [Header("衝刺")]
    public float dashMinDistance = 9f;
    public float dashMaxDistance = 26f;
    public float dashSpeed = 24f;
    public float dashDamage = 24f;
    public float dashContactRadius = 3.5f;
    public float dashCooldown = 6f;

    [Header("遠程")]
    public float rangedMinDistance = 10f;
    [Tooltip("超過這個距離不用遠程（能量彈射程 45m）")]
    public float rangedMaxDistance = 40f;
    [Tooltip("發射時預判玩家位置的比例（0 = 不預判、1 = 完整預判飛行時間內的移動）")]
    [Range(0f, 1f)] public float rangedLead = 0.8f;
    [Tooltip("能量彈飛行中，落點每秒最多往玩家修正幾公尺（0 = 不追蹤）")]
    public float rangedHomingRate = 7f;
    [Tooltip("三顆能量彈依序射出的間隔（要大於玩家受傷無敵時間 0.1 秒，每一顆才都打得到）")]
    public float rangedLaunchInterval = 0.15f;
    [Tooltip("能量彈命中的擊退（輕，玩家才不會被第一顆推出後面幾顆的落點）")]
    public float rangedKnockback = 2f;
    public float rangedDamage = 14f;
    public float rangedSplashRadius = 2.5f;
    public float rangedCooldown = 4.5f;

    [Header("技能冷卻（秒）")]
    public float encircleCooldown = 16f;
    public float encircleMinDistance = 7f;
    public float batSwarmCooldown = 20f;
    public float firstSkillDelay = 8f;

    [Header("二階段")]
    [Range(0f, 1f)] public float phase2Threshold = 0.5f;
    public float phase2SpeedMultiplier = 1.25f;
    public float phase2CooldownMultiplier = 0.7f;

    [Header("音樂")]
    public AudioClip bossMusic;
    public float musicVolume = 0.5f;

    // 動畫片段長度（秒），和 Animations/SkeletonCentipede 的片段一致
    const float SlashLen = 1.4f, SweepLen = 3f, BiteLen = 1.27f, TailLen = 1.8f, DashLen = 2f, RangedLen = 1.6f, RoarLen = 2.2f;

    public bool FightActive { get; private set; }
    public string CurrentAction { get; private set; } = "沉睡";
    public event System.Action OnFightStarted;
    public event System.Action OnDefeated;

    SkeletonCentipedeAnimationDriver driver;
    BossHealth health;
    SkeletonCentipedeEncircleSkill encircle;
    SkeletonCentipedeBatSwarmSkill batSwarm;
    SkeletonCentipedeBatGuardSkill batGuard;
    SkeletonCentipedeRangedVfx rangedVfx;
    PlayerHealth player;

    Vector3 homePos;
    Quaternion homeRot;
    float homeY;
    bool dead, phase2, dashing, dashHit;
    Vector3 dashDir;
    float inArenaTimer;
    float nextSweep, nextTail, nextDash, nextRanged, nextEncircle, nextBatSwarm;
    Coroutine brain;
    Vector3 playerVelocity, lastPlayerPos;
    CharacterController playerController;
    float rangedProjectileSpeed = 22f;

    float Speed => moveSpeed * (phase2 ? phase2SpeedMultiplier : 1f);
    float Cd(float seconds) => seconds * (phase2 ? phase2CooldownMultiplier : 1f);
    bool SkillBusy => (encircle != null && encircle.IsRunning) || (batSwarm != null && batSwarm.IsRunning) || (batGuard != null && batGuard.IsRunning);
    bool PlayerAlive => player != null && !player.IsDead;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        health = GetComponent<BossHealth>();
        encircle = GetComponent<SkeletonCentipedeEncircleSkill>();
        batSwarm = GetComponent<SkeletonCentipedeBatSwarmSkill>();
        batGuard = GetComponent<SkeletonCentipedeBatGuardSkill>();
        rangedVfx = GetComponent<SkeletonCentipedeRangedVfx>();
        homePos = transform.position;
        homeRot = transform.rotation;
        homeY = homePos.y;
    }

    void OnEnable()
    {
        driver.AttackImpact += OnAttackImpact;
        driver.DashStart += OnDashStart;
        driver.DashEnd += OnDashEnd;
        health.OnDied += OnBossDied;
        if (rangedVfx != null) rangedVfx.ProjectileHit += OnProjectileHit;
    }

    void OnDisable()
    {
        driver.AttackImpact -= OnAttackImpact;
        driver.DashStart -= OnDashStart;
        driver.DashEnd -= OnDashEnd;
        health.OnDied -= OnBossDied;
        if (rangedVfx != null) rangedVfx.ProjectileHit -= OnProjectileHit;
        if (player != null) { player.OnDied -= OnPlayerDied; player.OnRespawned -= OnPlayerRespawned; }
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null)
        {
            player.OnDied += OnPlayerDied;
            player.OnRespawned += OnPlayerRespawned;
        }
        driver.SetMoveSpeed(0f);
        if (rangedVfx != null)
        {
            rangedProjectileSpeed = rangedVfx.projectileSpeed;
            rangedVfx.homingTarget = player != null ? player.transform : null;
            rangedVfx.homingRate = rangedHomingRate;
            rangedVfx.launchInterval = rangedLaunchInterval;
            // 玩家的軸心在膠囊中間（不是腳底），追蹤要對準身體中心
            if (player != null) rangedVfx.homingOffset = PlayerBodyCenter() - player.transform.position;
        }
        if (player != null) lastPlayerPos = player.transform.position;
    }

    void Update()
    {
        TrackPlayerVelocity();
        if (dead) return;

        if (!FightActive)
        {
            // 玩家在場內待滿 startDelay 秒就開打
            if (PlayerAlive && FlatDistance(player.transform.position, arenaCenter) <= arenaRadius + aggroPadding)
            {
                inArenaTimer += Time.deltaTime;
                if (inArenaTimer >= startDelay) BeginFight();
            }
            else inArenaTimer = 0f;
            return;
        }

        if (dashing)
        {
            Vector3 p = transform.position + dashDir * dashSpeed * Time.deltaTime;
            transform.position = ClampToArena(p);
            if (!dashHit && PlayerAlive && FlatDistance(player.transform.position, transform.position) < dashContactRadius)
            {
                dashHit = true;
                Hurt(dashDamage * 0.6f, dashDir, "被百足駭骨撞飛");
            }
        }
        else if (!SkillBusy)
        {
            // 技能（跳躍）結束後回到地面高度
            var pos = transform.position;
            if (Mathf.Abs(pos.y - homeY) > 0.001f) transform.position = new Vector3(pos.x, Mathf.MoveTowards(pos.y, homeY, 20f * Time.deltaTime), pos.z);
        }
    }

    // ===================== 戰鬥流程 =====================

    public void BeginFight()
    {
        if (FightActive || dead) return;
        FightActive = true;
        health.ShowBar = true;
        float t = Time.time;
        nextEncircle = nextBatSwarm = t + firstSkillDelay;
        nextSweep = nextTail = nextDash = nextRanged = t;
        GameAudio.PlayMusic(GameAudio.Pick(bossMusic, "Music/boss_fight_music"), musicVolume, 1.5f);
        OnFightStarted?.Invoke();
        brain = StartCoroutine(Brain());
    }

    IEnumerator Brain()
    {
        CurrentAction = "開場吼叫";
        yield return FacePlayer(0.6f);
        driver.PlayRoar();
        ThirdPersonCamera.ShakeMain(0.25f, 1f);
        yield return Wait(RoarLen);

        while (FightActive && !dead)
        {
            if (!PlayerAlive || SkillBusy)
            {
                driver.SetMoveSpeed(0f);
                CurrentAction = SkillBusy ? "技能中" : "等待";
                yield return null;
                continue;
            }

            if (!phase2 && health.Health <= health.maxHealth * phase2Threshold)
            {
                yield return EnterPhase2();
                continue;
            }

            yield return ChooseAndAct();
            if (!SkillBusy) yield return Wait(Random.Range(recoverTimeRange.x, recoverTimeRange.y) * (phase2 ? 0.7f : 1f));
        }
    }

    IEnumerator ChooseAndAct()
    {
        float t = Time.time;
        float dist = FlatDistance(player.transform.position, transform.position);
        float angle = SignedAngleToPlayer();

        // 技能
        if (encircle != null && t >= nextEncircle && dist >= encircleMinDistance && Random.value < 0.45f)
        {
            nextEncircle = t + Cd(encircleCooldown);
            CurrentAction = "殘影圍殺";
            if (encircle.TryStart()) { yield return WaitWhileSkill(); yield break; }
        }
        if (batSwarm != null && t >= nextBatSwarm && Random.value < 0.35f)
        {
            nextBatSwarm = t + Cd(batSwarmCooldown);
            CurrentAction = "蝠群襲擊";
            yield return FacePlayer(0.5f);
            if (batSwarm.TryStart()) { yield return WaitWhileSkill(); yield break; }
        }

        // 近距離
        if (dist <= meleeRange)
        {
            if (Mathf.Abs(angle) > 95f && t >= nextTail)
            {
                nextTail = t + Cd(tailCooldown);
                CurrentAction = "甩尾";
                driver.PlayTailWhip(angle > 0f ? SkeletonCentipedeAnimationDriver.TailWhipSide.Right : SkeletonCentipedeAnimationDriver.TailWhipSide.Left);
                yield return Wait(TailLen);
                yield break;
            }

            yield return FacePlayer(0.45f);
            float roll = Random.value;
            if (t >= nextSweep && roll < 0.35f)
            {
                nextSweep = t + Cd(sweepCooldown);
                CurrentAction = "四連斬";
                driver.PlaySweepCombo();
                yield return SweepComboAdvance();
            }
            else if (roll < 0.7f)
            {
                CurrentAction = "鐮刀橫掃";
                driver.PlaySlash();
                yield return TrackWhile(SlashLen, 0.4f, 1f);
            }
            else
            {
                CurrentAction = "撲咬";
                driver.PlayBite();
                yield return TrackWhile(BiteLen, 0.4f, 1f);
            }
            yield break;
        }

        // 中距離：衝刺
        if (dist >= dashMinDistance && dist <= dashMaxDistance && t >= nextDash && Random.value < 0.55f)
        {
            nextDash = t + Cd(dashCooldown);
            CurrentAction = "衝刺";
            yield return FacePlayer(0.5f);
            driver.PlayDash();
            yield return TrackWhile(DashLen, 0.45f, 1f); // 蓄力時繼續瞄準，DashStart 之後鎖定方向
            yield break;
        }

        // 遠距離：能量彈
        if (dist >= rangedMinDistance && dist <= rangedMaxDistance && t >= nextRanged && Random.value < 0.6f)
        {
            nextRanged = t + Cd(rangedCooldown);
            CurrentAction = "遠程能量彈";
            yield return FacePlayer(0.5f);
            driver.PlayRanged(PredictedAimPoint());
            float start = Time.time;
            while (Time.time - start < RangedLen && !dead)
            {
                // 發射（AttackImpact "Ranged"）之前每一幀都更新預判的瞄準點
                if (PlayerAlive) driver.RangedTargetPoint = PredictedAimPoint();
                if (Time.time - start < 0.75f) TurnTowardsPlayer(1.2f);
                yield return null;
            }
            yield break;
        }

        yield return Chase(Random.Range(chaseTimeRange.x, chaseTimeRange.y));
    }

    IEnumerator EnterPhase2()
    {
        phase2 = true;
        CurrentAction = "二階段";
        driver.SetMoveSpeed(0f);
        health.Invulnerable = true;
        yield return FacePlayer(0.5f);
        driver.PlayRoar();
        ThirdPersonCamera.ShakeMain(0.4f, 1.4f);
        yield return Wait(RoarLen);
        health.Invulnerable = false;
        nextEncircle = Mathf.Min(nextEncircle, Time.time + 2f);
    }

    IEnumerator Chase(float duration)
    {
        CurrentAction = "追擊";
        float end = Time.time + duration;
        while (Time.time < end && PlayerAlive && !SkillBusy)
        {
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            if (dist <= meleeRange * 0.8f) break;
            TurnTowardsPlayer(1f);
            float facing = Mathf.Clamp01(Vector3.Dot(transform.forward, to.normalized));
            float speed = dist > stopDistance ? Speed * Mathf.Lerp(0.3f, 1f, facing) : 0f;
            transform.position = ClampToArena(transform.position + transform.forward * speed * Time.deltaTime);
            driver.SetMoveSpeed(speed / Mathf.Max(0.01f, moveSpeed * phase2SpeedMultiplier));
            yield return null;
        }
        driver.SetMoveSpeed(0f);
    }

    // ===================== 傷害 =====================

    void OnAttackImpact(string attack)
    {
        if (!FightActive || dead || !PlayerAlive) return;
        switch (attack)
        {
            case "Slash":
                if (InFan(slashRange, slashHalfAngle)) Hurt(slashDamage, transform.forward, "被鐮刀斬殺");
                break;
            case "Sweep1":
            case "Sweep2":
            case "Sweep3":
                if (InFan(sweepReach + 2.5f, 85f)) Hurt(sweepDamage, transform.forward, "被四連斬撕碎", sweepLightKnockback, 0.5f);
                break;
            case "Sweep4":
                if (InFan(sweepReach + 3f, 60f)) Hurt(sweepFinisherDamage, transform.forward, "被雙鐮重砍劈開");
                break;
            case "Bite":
                if (InFan(biteRange, 35f)) Hurt(biteDamage, transform.forward, "被百足駭骨咬碎");
                break;
            case "TailWhip":
            {
                float d = FlatDistance(player.transform.position, transform.position);
                bool jumped = player.FeetY - homeY > tailJumpFeetClearance;
                if (d <= tailRadius && !jumped)
                {
                    Vector3 away = player.transform.position - transform.position;
                    Hurt(tailDamage, away, "被尾巴掃飛");
                }
                break;
            }
            case "Dash":
                if (InFan(slashRange, 70f)) Hurt(dashDamage, transform.forward, "被衝刺上撈挑飛");
                break;
            // "Ranged" 的傷害在能量彈爆炸時算（OnProjectileHit）；"Pounce" 由殘影圍殺技能自己處理
        }
    }

    void OnProjectileHit(Vector3 pos, Collider hit)
    {
        if (!FightActive || dead || !PlayerAlive) return;
        bool direct = hit != null && hit.GetComponentInParent<PlayerHealth>() == player;
        bool splash = Vector3.Distance(pos, PlayerBodyCenter()) <= rangedSplashRadius;
        if (direct || splash) Hurt(rangedDamage, player.transform.position - pos, "被能量彈炸飛", rangedKnockback, 1.5f);
    }

    void OnDashStart()
    {
        dashing = true;
        dashHit = false;
        Vector3 to = PlayerAlive ? player.transform.position - transform.position : transform.forward;
        to.y = 0f;
        dashDir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dashDir);
    }

    void OnDashEnd() => dashing = false;

    void Hurt(float damage, Vector3 pushDir, string reason, float knockback = -1f, float knockUp = 4f)
    {
        pushDir.y = 0f;
        if (knockback < 0f) knockback = meleeKnockback;
        Vector3 knock = (pushDir.sqrMagnitude > 0.001f ? pushDir.normalized : transform.forward) * knockback + Vector3.up * knockUp;
        if (player.TakeDamage(damage, knock, false, reason)) ThirdPersonCamera.ShakeMain(0.2f, 0.25f);
    }

    bool InFan(float range, float halfAngle)
    {
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        return to.magnitude <= range && Vector3.Angle(transform.forward, to) <= halfAngle;
    }

    // ===================== 死亡 / 重置 =====================

    void OnBossDied()
    {
        dead = true;
        FightActive = false;
        dashing = false;
        StopAllCoroutines();
        if (encircle != null) encircle.Stop();
        driver.PlayDeath();
        CurrentAction = "擊敗";
        GameAudio.StopMusic(2f);
        ThirdPersonCamera.ShakeMain(0.4f, 0.8f);
        OnDefeated?.Invoke();
    }

    void OnPlayerDied()
    {
        if (dead) return;
        FightActive = false;
        dashing = false;
        if (brain != null) StopCoroutine(brain);
        StopAllCoroutines();
        if (encircle != null) encircle.Stop();
        driver.SetMoveSpeed(0f);
        CurrentAction = "等待玩家";
    }

    void OnPlayerRespawned()
    {
        if (dead) return;
        // 重新來過：回到起點、補滿血，玩家再進場就重新開打
        transform.SetPositionAndRotation(homePos, homeRot);
        health.ResetHealth();
        health.ShowBar = false;
        health.Invulnerable = false;
        if (batGuard != null) batGuard.ResetThresholds();
        driver.Revive();
        phase2 = false;
        inArenaTimer = 0f;
        GameAudio.StopMusic(1f);
        CurrentAction = "沉睡";
    }

    // ===================== 小工具 =====================

    IEnumerator Wait(float seconds)
    {
        float end = Time.time + seconds;
        while (Time.time < end && !dead) yield return null;
    }

    IEnumerator WaitWhileSkill()
    {
        driver.SetMoveSpeed(0f);
        yield return null;
        while (SkillBusy && !dead) yield return null;
    }

    /// <summary>播動作 duration 秒；前 trackTime 秒以 trackFactor 的轉速繼續轉向玩家。</summary>
    IEnumerator TrackWhile(float duration, float trackTime, float trackFactor)
    {
        float start = Time.time;
        while (Time.time - start < duration && !dead)
        {
            if (Time.time - start < trackTime && !dashing) TurnTowardsPlayer(trackFactor);
            yield return null;
        }
    }

    IEnumerator FacePlayer(float maxTime)
    {
        float end = Time.time + maxTime;
        while (Time.time < end && PlayerAlive && Mathf.Abs(SignedAngleToPlayer()) > 6f)
        {
            TurnTowardsPlayer(1.6f);
            yield return null;
        }
    }

    /// <summary>
    /// 四連斬：動畫原地揮砍，Boss 一邊轉向一邊往前追到 sweepReach，
    /// 每一刀之間玩家被輕推開也會被追上（第四下 1.88 秒之後就不再移動）。
    /// </summary>
    IEnumerator SweepComboAdvance()
    {
        const float lastHit = 1.88f;
        float start = Time.time;
        while (Time.time - start < SweepLen && !dead)
        {
            float t = Time.time - start;
            if (t < lastHit && PlayerAlive)
            {
                TurnTowardsPlayer(1.3f);
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                float gap = to.magnitude - sweepReach;
                if (gap > 0f)
                {
                    float step = Mathf.Min(gap, sweepLungeSpeed * (phase2 ? phase2SpeedMultiplier : 1f) * Time.deltaTime);
                    transform.position = ClampToArena(transform.position + to.normalized * step);
                }
            }
            yield return null;
        }
    }

    void TrackPlayerVelocity()
    {
        if (player == null || Time.deltaTime <= 0f) return;
        Vector3 p = player.transform.position;
        Vector3 v = (p - lastPlayerPos) / Time.deltaTime;
        v.y = 0f;
        if (v.magnitude > 30f) v = Vector3.zero; // 重生 / 瞬移
        playerVelocity = Vector3.Lerp(playerVelocity, v, 1f - Mathf.Exp(-8f * Time.deltaTime));
        lastPlayerPos = p;
    }

    /// <summary>玩家身體（CharacterController 膠囊）的中心。玩家物件的軸心本來就在膠囊中間，不能再往上加。</summary>
    Vector3 PlayerBodyCenter()
    {
        if (playerController == null && player != null) playerController = player.GetComponent<CharacterController>();
        return playerController != null ? playerController.bounds.center : player.transform.position;
    }

    /// <summary>瞄準玩家胸口，並依能量彈飛行時間預判玩家會走到哪裡。</summary>
    Vector3 PredictedAimPoint()
    {
        Vector3 chest = PlayerBodyCenter();
        float flight = FlatDistance(chest, transform.position) / Mathf.Max(1f, rangedProjectileSpeed);
        return chest + playerVelocity * flight * rangedLead;
    }

    void TurnTowardsPlayer(float factor)
    {
        if (!PlayerAlive) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), turnSpeed * factor * Time.deltaTime);
    }

    float SignedAngleToPlayer()
    {
        if (player == null) return 0f;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        return Vector3.SignedAngle(transform.forward, to, Vector3.up);
    }

    Vector3 ClampToArena(Vector3 p)
    {
        Vector3 off = p - arenaCenter;
        off.y = 0f;
        float max = Mathf.Max(1f, arenaRadius - edgeMargin);
        if (off.magnitude > max) off = off.normalized * max;
        return new Vector3(arenaCenter.x + off.x, p.y, arenaCenter.z + off.z);
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, meleeRange);
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.4f);
        Gizmos.DrawWireSphere(arenaCenter, arenaRadius - edgeMargin);
    }
}
