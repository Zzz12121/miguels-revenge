using System.Collections;
using UnityEngine;

/// <summary>
/// 青眼惡魔的 Boss AI（基本招式版）。
///
/// 流程：玩家進入 aggroRange → 顯示血條、開打 → 追擊（Run）→ 依距離與冷卻挑一招：
///   橫斬 Slash、重劈 Heavy（前衝＋重擊退）、三連斬 Combo、突刺 Thrust（遠一點也能衝過來刺）。
///   玩家在近距離揮刀時有機率舉盾格擋（格擋期間不受傷）。
/// 每一招的動畫狀態、長度、命中時間、範圍、傷害、擊退、前衝、冷卻、權重都在 Inspector 的 attacks 清單調整。
/// 傷害走 PlayerHealth.TakeDamage（翻滾無敵 / 舉盾照常有效）；血量與血條用 BossHealth。
/// 玩家死亡重生 → 惡魔回到起點補滿血，玩家再靠近就重新開打。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BossHealth))]
public class GreenEyeDemonAI : MonoBehaviour
{
    [System.Serializable]
    public class Attack
    {
        public string name = "Slash";
        [Tooltip("Animator 裡的狀態名稱")]
        public string state = "Slash";
        [Tooltip("整個動作的長度（秒）")]
        public float duration = 1.3f;
        [Tooltip("每一下命中的時間點（秒，從動作開始算）")]
        public float[] hitTimes = { 0.5f };
        public float range = 3.4f;
        [Tooltip("扇形半角（度）")]
        public float halfAngle = 70f;
        public float damage = 14f;
        public float knockback = 5f;
        public float knockUp = 2.5f;
        [Tooltip("第一下命中前往玩家衝多遠（0 = 不衝）")]
        public float lungeDistance = 0f;
        public float lungeTime = 0.25f;
        [Tooltip("出招前幾秒內持續轉向玩家")]
        public float trackTime = 0.35f;
        [Tooltip("和玩家的距離至少這麼遠才會用（突刺這類衝過去的招）")]
        public float minDistance = 0f;
        public float cooldown = 0f;
        [Tooltip("被選中的機率權重")]
        public float weight = 1f;
        [System.NonSerialized] public float nextReady;

        public float Reach => range + lungeDistance;
    }

    [Header("模型")]
    public Animator animator;
    public string idleState = "Idle";
    public string runState = "Run";
    public string blockState = "Block";
    public string deathState = "Death";
    [Tooltip("Run 動畫原速時的移動速度（m/s），用來對齊腳步")]
    public float runAnimSpeed = 4.5f;
    public float crossFade = 0.12f;

    [Header("移動")]
    public float moveSpeed = 4.5f;
    public float turnSpeed = 220f;
    [Tooltip("追到這個距離就停下來出招")]
    public float stopDistance = 2.6f;
    [Tooltip("玩家進到這個距離內就開打")]
    public float aggroRange = 25f;
    [Tooltip("（選填）限制在圓形場地內：半徑 0 = 不限制")]
    public Vector3 arenaCenter;
    public float arenaRadius = 0f;
    public Vector2 recoverTimeRange = new Vector2(0.35f, 0.8f);

    [Header("招式")]
    public Attack[] attacks = DefaultAttacks();

    [Header("格擋")]
    [Range(0f, 1f)] public float blockChance = 0.3f;
    public float blockDuration = 0.6f;
    public float blockCooldown = 4f;
    public float blockRange = 4.5f;
    public string blockSound = "SFX/RPG/metalPot2";

    [Header("音樂")]
    public AudioClip bossMusic;
    public float musicVolume = 0.5f;

    public bool FightActive { get; private set; }
    public string CurrentAction { get; private set; } = "沉睡";
    public event System.Action OnFightStarted;
    public event System.Action OnDefeated;
    public event System.Action OnFightReset;

    static readonly int RunSpeedId = Animator.StringToHash("RunSpeed");

    BossHealth health;
    PlayerHealth player;
    MeleeCombat playerCombat;
    Vector3 homePos;
    Quaternion homeRot;
    bool dead, attacking, blocking;
    float nextBlock;
    string currentState;
    Coroutine brain;

    bool PlayerAlive => player != null && !player.IsDead;

    public static Attack[] DefaultAttacks() => new[]
    {
        new Attack { name = "橫斬", state = "Slash", duration = 1.3f, hitTimes = new[] { 0.5f },
                     range = 3.4f, halfAngle = 75f, damage = 14f, knockback = 5f, weight = 1.2f },
        new Attack { name = "重劈", state = "Heavy", duration = 1.73f, hitTimes = new[] { 0.9f },
                     range = 3.8f, halfAngle = 40f, damage = 26f, knockback = 10f, knockUp = 5f,
                     lungeDistance = 2.5f, trackTime = 0.6f, cooldown = 5f, weight = 0.8f },
        new Attack { name = "三連斬", state = "Combo", duration = 2.33f, hitTimes = new[] { 0.55f, 1.1f, 1.65f },
                     range = 3.4f, halfAngle = 70f, damage = 10f, knockback = 1.5f, knockUp = 0.5f,
                     lungeDistance = 1f, trackTime = 1.4f, cooldown = 6f, weight = 0.8f },
        new Attack { name = "突刺", state = "Thrust", duration = 1.0f, hitTimes = new[] { 0.4f },
                     range = 3.2f, halfAngle = 25f, damage = 12f, knockback = 6f,
                     lungeDistance = 4f, lungeTime = 0.3f, minDistance = 3.5f, cooldown = 3f, weight = 1f },
    };

    void Awake()
    {
        health = GetComponent<BossHealth>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false;
        homePos = transform.position;
        homeRot = transform.rotation;
    }

    void OnEnable() => health.OnDied += OnDied;

    void OnDisable()
    {
        health.OnDied -= OnDied;
        if (player != null) { player.OnDied -= OnPlayerDied; player.OnRespawned -= OnPlayerRespawned; }
        if (playerCombat != null) playerCombat.OnSwing -= OnPlayerSwing;
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null)
        {
            player.OnDied += OnPlayerDied;
            player.OnRespawned += OnPlayerRespawned;
            playerCombat = player.GetComponent<MeleeCombat>();
            if (playerCombat != null) playerCombat.OnSwing += OnPlayerSwing;
        }
        Play(idleState, 0f);
    }

    void Update()
    {
        if (dead || FightActive) return;
        if (PlayerAlive && FlatDistance(player.transform.position, transform.position) <= aggroRange) BeginFight();
    }

    // ===================== 戰鬥流程 =====================

    public void BeginFight()
    {
        if (FightActive || dead) return;
        FightActive = true;
        health.ShowBar = true;
        foreach (var a in attacks) a.nextReady = 0f;
        GameAudio.PlayMusic(GameAudio.Pick(bossMusic, "Music/boss_fight_music"), musicVolume, 1.5f);
        OnFightStarted?.Invoke();
        brain = StartCoroutine(Brain());
    }

    IEnumerator Brain()
    {
        while (FightActive && !dead)
        {
            if (!PlayerAlive || blocking) { SetRunning(0f); yield return null; continue; }

            float dist = FlatDistance(player.transform.position, transform.position);
            var attack = PickAttack(dist);
            if (attack != null)
            {
                yield return DoAttack(attack);
                yield return Recover();
                continue;
            }

            // 追擊
            CurrentAction = "追擊";
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            TurnTowards(to, 1f);
            float speed = dist > stopDistance ? moveSpeed * Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(Vector3.Dot(transform.forward, to.normalized))) : 0f;
            Move(transform.forward * speed * Time.deltaTime);
            SetRunning(speed);
            yield return null;
        }
        SetRunning(0f);
    }

    Attack PickAttack(float dist)
    {
        float t = Time.time, total = 0f;
        foreach (var a in attacks)
            if (Usable(a, dist, t)) total += a.weight;
        if (total <= 0f) return null;
        float roll = Random.value * total;
        foreach (var a in attacks)
        {
            if (!Usable(a, dist, t)) continue;
            roll -= a.weight;
            if (roll <= 0f) return a;
        }
        return null;
    }

    static bool Usable(Attack a, float dist, float t) =>
        t >= a.nextReady && dist >= a.minDistance && dist <= a.Reach && a.weight > 0f;

    IEnumerator DoAttack(Attack a)
    {
        attacking = true;
        CurrentAction = a.name;
        a.nextReady = Time.time + a.cooldown;
        SetRunning(0f);
        Play(a.state, crossFade);

        float start = Time.time;
        float firstHit = a.hitTimes.Length > 0 ? a.hitTimes[0] : a.duration * 0.5f;
        int next = 0;
        while (Time.time - start < a.duration && !dead)
        {
            float t = Time.time - start;
            Vector3 to = PlayerAlive ? player.transform.position - transform.position : transform.forward;
            to.y = 0f;
            if (t < a.trackTime) TurnTowards(to, 1.2f);

            // 第一下命中前的前衝（衝到剛好打得到就停）
            if (a.lungeDistance > 0f && t >= firstHit - a.lungeTime && t < firstHit)
            {
                float gap = to.magnitude - a.range * 0.6f;
                if (gap > 0f) Move(transform.forward * Mathf.Min(gap, a.lungeDistance / Mathf.Max(0.01f, a.lungeTime) * Time.deltaTime));
            }

            while (next < a.hitTimes.Length && t >= a.hitTimes[next])
            {
                TryHit(a);
                next++;
            }
            yield return null;
        }
        attacking = false;
    }

    IEnumerator Recover()
    {
        CurrentAction = "收招";
        Play(idleState, crossFade);
        float end = Time.time + Random.Range(recoverTimeRange.x, recoverTimeRange.y);
        while (Time.time < end && !dead)
        {
            if (PlayerAlive)
            {
                Vector3 to = player.transform.position - transform.position;
                to.y = 0f;
                TurnTowards(to, 0.6f);
            }
            yield return null;
        }
    }

    void TryHit(Attack a)
    {
        if (!PlayerAlive) return;
        Vector3 to = player.transform.position - transform.position;
        float height = to.y;
        to.y = 0f;
        if (to.magnitude > a.range || Vector3.Angle(transform.forward, to) > a.halfAngle || Mathf.Abs(height) > 3f) return;
        Vector3 dir = to.sqrMagnitude > 0.001f ? to.normalized : transform.forward;
        if (player.TakeDamage(a.damage, dir * a.knockback + Vector3.up * a.knockUp, false, $"被青眼惡魔的{a.name}斬殺"))
            ThirdPersonCamera.ShakeMain(0.15f, 0.2f);
    }

    // ===================== 格擋 =====================

    void OnPlayerSwing(int index)
    {
        if (!FightActive || dead || attacking || blocking || Time.time < nextBlock || !PlayerAlive) return;
        Vector3 to = player.transform.position - transform.position;
        to.y = 0f;
        if (to.magnitude > blockRange || Random.value > blockChance) return;
        nextBlock = Time.time + blockCooldown;
        StartCoroutine(Block(to));
    }

    IEnumerator Block(Vector3 toPlayer)
    {
        blocking = true;
        CurrentAction = "格擋";
        SetRunning(0f);
        if (toPlayer.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toPlayer);
        Play(blockState, 0.06f);
        health.Invulnerable = true;
        if (!string.IsNullOrEmpty(blockSound)) GameAudio.Play(blockSound, transform.position + Vector3.up * 1.5f, 0.9f, 0.8f, 0.05f, 1f, 35f);
        float end = Time.time + blockDuration;
        while (Time.time < end && !dead) yield return null;
        health.Invulnerable = false;
        blocking = false;
        if (!dead) Play(idleState, crossFade);
    }

    // ===================== 死亡 / 重置 =====================

    void OnDied()
    {
        dead = true;
        FightActive = false;
        attacking = blocking = false;
        StopAllCoroutines();
        health.Invulnerable = false;
        SetRunning(0f);
        Play(deathState, 0.1f);
        CurrentAction = "擊敗";
        GameAudio.StopMusic(2f);
        ThirdPersonCamera.ShakeMain(0.3f, 0.6f);
        OnDefeated?.Invoke();
    }

    void OnPlayerDied()
    {
        if (dead) return;
        FightActive = false;
        attacking = blocking = false;
        StopAllCoroutines();
        health.Invulnerable = false;
        SetRunning(0f);
        Play(idleState, crossFade);
        CurrentAction = "等待玩家";
    }

    void OnPlayerRespawned()
    {
        if (dead) return;
        transform.SetPositionAndRotation(homePos, homeRot);
        health.ResetHealth();
        health.ShowBar = false;
        GameAudio.StopMusic(1f);
        Play(idleState, 0f);
        CurrentAction = "沉睡";
        OnFightReset?.Invoke();
    }

    // ===================== 小工具 =====================

    void Play(string state, float fade)
    {
        if (animator == null || string.IsNullOrEmpty(state)) return;
        if (!animator.HasState(0, Animator.StringToHash(state))) return;
        currentState = state;
        if (fade <= 0f) animator.Play(state, 0, 0f);
        else animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }

    void SetRunning(float speed)
    {
        if (animator == null) return;
        if (speed > 0.2f)
        {
            if (currentState != runState) Play(runState, crossFade);
            animator.SetFloat(RunSpeedId, Mathf.Clamp(speed / Mathf.Max(0.01f, runAnimSpeed), 0.5f, 1.6f));
        }
        else if (currentState == runState) Play(idleState, crossFade);
    }

    void TurnTowards(Vector3 dir, float factor)
    {
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * factor * Time.deltaTime);
    }

    void Move(Vector3 delta)
    {
        Vector3 p = transform.position + delta;
        if (arenaRadius > 0f)
        {
            Vector3 off = p - arenaCenter;
            off.y = 0f;
            float max = arenaRadius - 2f;
            if (off.magnitude > max) p = new Vector3(arenaCenter.x, p.y, arenaCenter.z) + off.normalized * max;
        }
        transform.position = p;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    void OnDrawGizmosSelected()
    {
        if (attacks == null) return;
        Gizmos.color = new Color(0.2f, 1f, 0.8f, 0.5f);
        foreach (var a in attacks) Gizmos.DrawWireSphere(transform.position, a.range);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, aggroRange);
    }
}
