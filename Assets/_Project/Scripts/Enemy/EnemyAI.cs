using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小怪 AI。移動用 ThirdPersonController（跟玩家同一套），攻擊用 EnemyCombat（跟玩家同一套揮劍）。
///
/// 狀態：
///   Idle     待機：站在原位，玩家進入警戒範圍（而且看得到）就發現玩家
///   Alerted  發現：頭上冒「!」愣一下，同時叫醒附近的同伴
///   Chase    追擊：靠近玩家、在攻擊距離內打 1~3 下連擊；被打中會硬直
///   Return   放棄：玩家跑太遠或自己離原位太遠，就走回原位並回滿血
///   Dropping 從天而降（陷阱小怪）：由 AmbushTrigger 呼叫 BeginDrop，落地時砸傷附近的玩家
///   Jumping  跳躍：前面是缺口 / 高台時跳到能更靠近玩家的地方，或直接撲向玩家
///   Dead     死亡
///
/// 跳躍：追擊時前方沒有地板（或有一階跳得上去的高台），會找「跳得到、而且比現在更靠近玩家」的落點跳過去；
/// 找不到就停在邊緣等。跳到一半落點的地板塌了，就會掉下去。
/// 玩家死亡重生時，所有小怪會復活並回到原位。
/// </summary>
[RequireComponent(typeof(ThirdPersonController), typeof(EnemyCombat), typeof(EnemyHealth))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Alerted, Chase, Return, Dropping, Dead, Jumping, Spawning }

    [Header("巡邏與遊蕩")]
    [Tooltip("未發現玩家時，是否在出生點周邊自由四處遊蕩走動")]
    public bool enableWander = true;
    [Tooltip("四處走動的半徑範圍 (m)")]
    public float wanderRadius = 8f;
    [Tooltip("走動到一個目標點後的停頓等待時間 (秒，最小/最大)")]
    public Vector2 wanderWaitTime = new Vector2(2f, 4.5f);
    [Tooltip("遊蕩走動時的移動速度比例 (0.5 = 半速漫步)")]
    [Range(0.2f, 1f)] public float wanderSpeedMultiplier = 0.5f;

    [Header("警戒")]
    [Tooltip("玩家進入這個半徑就會被發現 (m)")]
    public float alertRadius = 9f;
    [Tooltip("高度差超過這個值就不會發現（避免隔著樓層發現玩家）")]
    public float alertHeight = 4f;
    [Tooltip("需要看得到玩家才會發現（中間有牆擋住就不會）")]
    public bool requireLineOfSight = true;
    [Tooltip("發現玩家後愣住多久才開始追 (秒)，頭上會出現 !")]
    public float reactionTime = 0.5f;
    [Tooltip("發現玩家時，附近這個半徑內的同伴也會一起被叫醒 (m)")]
    public float callForHelpRadius = 6f;

    [Header("追擊")]
    [Tooltip("離玩家超過這個距離就衝刺（跟玩家按 Shift 一樣快）；0 = 不衝刺")]
    public float sprintDistance = 7f;
    [Tooltip("離開原本位置超過這個距離就放棄、走回去 (m)")]
    public float leashRadius = 20f;
    [Tooltip("玩家跑到這麼遠就放棄 (m)")]
    public float giveUpDistance = 18f;
    [Tooltip("和其他小怪保持的距離，避免擠成一團 (m)")]
    public float separationRadius = 1.6f;

    [Header("攻擊")]
    [Tooltip("離玩家這麼近就開始攻擊 (m)")]
    public float attackRange = 2.3f;
    [Tooltip("兩輪連擊之間休息多久 (秒，隨機範圍)")]
    public Vector2 attackInterval = new Vector2(0.8f, 1.8f);
    [Tooltip("一輪最多打幾下（跟玩家一樣最多三段）")]
    [Range(1, 3)] public int maxComboHits = 3;

    [Header("重生")]
    [Tooltip("勾選 = 玩家死亡重生時，已經被打死的這隻不會復活（只有開新的一局才會重置）；活著的還是會回到原位")]
    public bool stayDeadOnPlayerRespawn = false;

    [Header("跳躍")]
    [Tooltip("會不會跳（跨越缺口、跳上高台、撲向玩家）")]
    public bool canJump = true;
    [Tooltip("最遠能跳多遠 (m)")]
    public float maxJumpDistance = 9f;
    [Tooltip("能跳上多高的高台 (m)")]
    public float maxJumpUp = 1.5f;
    [Tooltip("願意往下跳多深 (m)")]
    public float maxJumpDown = 2.5f;
    [Tooltip("起跳前蹲一下的時間 (秒)")]
    public float jumpWindup = 0.2f;
    [Tooltip("撲向玩家：玩家在這個距離範圍內時，偶爾會直接跳到玩家身上 (m)。兩個都設 0 = 不撲")]
    public Vector2 pounceRange = new Vector2(4.5f, 7.5f);
    [Tooltip("兩次撲擊之間隔多久 (秒，隨機範圍)")]
    public Vector2 pounceCooldown = new Vector2(6f, 11f);
    [Tooltip("撲到玩家身上的傷害")]
    public float pounceDamage = 10f;
    [Tooltip("落地時離玩家這麼近才算撲中 (m)")]
    public float pounceRadius = 1.6f;

    [Header("從天而降（陷阱小怪用）")]
    [Tooltip("落地時砸到玩家的範圍 (m)")]
    public float stompRadius = 2f;
    public float stompDamage = 15f;

    public State CurrentState => state;
    /// <summary>正在跟玩家戰鬥中</summary>
    public bool IsEngaged => state == State.Alerted || state == State.Chase || state == State.Dropping
                          || state == State.Jumping || state == State.Spawning;

    static readonly List<EnemyAI> All = new List<EnemyAI>();

    ThirdPersonController controller;
    EnemyCombat combat;
    EnemyHealth health;
    CharacterController cc;
    Transform player;
    PlayerHealth playerHealth;

    Vector3 homePos;
    Quaternion homeRot;
    State state = State.Idle;
    float stateTimer;

    Vector3 wanderTarget;
    float wanderWaitTimer;
    bool isWanderingMoving;
    float staggerTimer;
    float nextAttackTime;
    bool wasAttacking;

    // 跳躍
    Vector3 jumpTarget;
    float jumpApex;
    bool jumpLaunched, jumpIsPounce;
    float nextJumpSearchTime, nextPounceTime;

    // 競技場模式（波次戰生成的小怪）：不管多遠都追玩家，不會放棄走回原位
    bool alwaysAggro;

    GameObject dropMarker;
    float dropStartY, dropGroundY;
    static Material dropMarkerMat;
    readonly RaycastHit[] rayHits = new RaycastHit[16];
    static readonly Collider[] overlapHits = new Collider[8];

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
        cc = GetComponent<CharacterController>();
        controller.aiControlled = true;

        homePos = transform.position;
        homeRot = transform.rotation;

        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        combat.target = player;
        health.OnDamaged += OnDamaged;
        health.OnDied += OnDied;
    }

    void OnEnable()
    {
        All.Add(this);
        if (playerHealth != null) playerHealth.OnRespawned += ResetEnemy;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (playerHealth != null) playerHealth.OnRespawned -= ResetEnemy;
        ClearDropMarker();
    }

    // =====================================================================

    void Update()
    {
        if (state == State.Dead) return;

        // 掉出場地
        if (transform.position.y < -20f) { health.Kill(); return; }

        float dt = Time.deltaTime;
        stateTimer += dt;
        if (staggerTimer > 0f) staggerTimer -= dt;

        // 一輪連擊結束 → 休息一下再打
        if (wasAttacking && !combat.IsAttacking)
            nextAttackTime = Time.time + Random.Range(attackInterval.x, attackInterval.y);
        wasAttacking = combat.IsAttacking;

        health.ForceShowBar = IsEngaged && !emergePending; // 還在地底下的時候不顯示血條
        bool playerAlive = player != null && (playerHealth == null || !playerHealth.IsDead);

        switch (state)
        {
            case State.Idle:
                UpdateIdleWander(playerAlive);
                break;

            case State.Alerted:
                controller.SetAIInput(Vector3.zero);
                if (playerAlive) TurnTowards(FlatToPlayer(), 360f);
                if (stateTimer >= reactionTime) SetState(State.Chase);
                break;

            case State.Chase:
                UpdateChase(playerAlive);
                break;

            case State.Return:
                UpdateReturn(playerAlive);
                break;

            case State.Dropping:
                UpdateDrop();
                break;

            case State.Jumping:
                UpdateJump();
                break;

            case State.Spawning:
                if (emergePending) { UpdateEmerge(); break; }
                controller.SetAIInput(Vector3.zero);
                if (stateTimer >= spawnTime)
                {
                    nextPounceTime = Time.time + Random.Range(1.5f, 4f);
                    SetState(State.Chase);
                }
                break;
        }
    }

    float spawnTime;

    // 從地底鑽出來
    bool emergePending;        // 還在地底下（看不到、打不到）
    bool emergeAnimStarted;
    float emergeWarn;
    const float EmergeLead = 0.2f; // 鑽出來的動畫開始播之後，大約過這麼久身體才衝出地面

    /// <summary>還在地底下等著鑽出來</summary>
    public bool IsUnderground => state == State.Spawning && emergePending;

    /// <summary>
    /// 從 point 的地底鑽出來（取代從天而降）：地上先出現紅圈 warnTime 秒，然後小怪衝出地面，
    /// 站在紅圈裡的玩家會受傷。需要有模型動畫（EnemyVisual）；沒有模型的小怪請用 BeginDrop。
    /// </summary>
    public void BeginEmerge(Vector3 point, float warnTime)
    {
        if (state == State.Dead) return;
        float half = transform.position.y - FeetY;                 // 角色中心離腳底多高
        float groundY = FindGroundY(point, 3f, point.y - half);

        combat.CancelAttack();
        controller.ResetMotion();
        controller.SetAIInput(Vector3.zero);
        controller.enabled = false;   // 在地底下的時候不移動、不受重力
        cc.enabled = false;           // 也沒有碰撞（玩家不會撞到看不見的東西，也打不到）
        transform.position = new Vector3(point.x, groundY + half + 0.05f, point.z);
        if (player != null) FaceInstant(FlatToPlayer());

        emergeWarn = Mathf.Max(warnTime, EmergeLead + 0.05f);
        emergePending = true;
        emergeAnimStarted = false;
        var visual = GetComponent<EnemyVisual>();
        spawnTime = visual != null ? visual.spawnDuration : 0.6f;
        if (visual != null) visual.PlayUnderground();

        ClearDropMarker();
        dropMarker = CreateDropMarker(new Vector3(point.x, groundY + 0.03f, point.z));
        SetState(State.Spawning);
    }

    void UpdateEmerge()
    {
        // 地上的紅圈越來越大
        if (dropMarker != null)
        {
            float d = Mathf.Lerp(0.6f, stompRadius * 2f, Mathf.Clamp01(stateTimer / emergeWarn));
            dropMarker.transform.localScale = new Vector3(d, 0.01f, d);
        }

        // 動畫提早一點開始播，這樣「衝出地面」剛好對上紅圈結束
        if (!emergeAnimStarted && stateTimer >= emergeWarn - EmergeLead)
        {
            emergeAnimStarted = true;
            var visual = GetComponent<EnemyVisual>();
            if (visual != null) visual.PlaySpawn();
        }
        if (stateTimer < emergeWarn) return;

        // 衝出地面！
        emergePending = false;
        ClearDropMarker();
        cc.enabled = true;
        controller.enabled = true;
        controller.ResetMotion();
        ThirdPersonCamera.ShakeMain(0.2f, 0.2f);
        GameAudio.Play("SFX/rock_hit", transform.position, 0.45f, 1.5f, 0.08f, 1f, 40f);

        if (player != null && playerHealth != null && !playerHealth.IsDead)
        {
            Vector3 to = player.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            if (to.magnitude <= stompRadius && Mathf.Abs(dy) < 2.5f)
                playerHealth.TakeDamage(stompDamage, dir * 7f + Vector3.up * 6f);
        }

        nextAttackTime = Time.time + 0.6f;
        stateTimer = EmergeLead; // 動畫已經播了這麼久，剩下的時間播完就開始追
    }

    /// <summary>
    /// 從地底冒出來（波次戰在遠處生成時）：播冒出來的動畫，duration 秒後開始追玩家。
    /// </summary>
    public void BeginSpawn(float duration)
    {
        if (state == State.Dead) return;
        spawnTime = duration;
        controller.SetAIInput(Vector3.zero);
        if (player != null) FaceInstant(FlatToPlayer());
        var visual = GetComponent<EnemyVisual>();
        if (visual != null) visual.PlaySpawn();
        SetState(State.Spawning);
    }

    /// <summary>
    /// 移到 point 的地面上，然後從地底冒出來（沒有紅圈、不會傷到玩家）。走廊的陷阱用。
    /// </summary>
    public void BeginSpawnAt(Vector3 point, float duration)
    {
        if (state == State.Dead) return;
        float half = transform.position.y - FeetY;
        float groundY = FindGroundY(point, 3f, point.y - half);
        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.position = new Vector3(point.x, groundY + half + 0.05f, point.z);
        cc.enabled = true;
        controller.enabled = true;
        controller.ResetMotion();
        BeginSpawn(duration);
    }

    /// <summary>
    /// 競技場模式（ArenaWaves 生成的小怪用）：不管玩家多遠都會追，不會放棄。
    /// home = 玩家死掉時走回去的位置。
    /// </summary>
    public void SetArenaMode(Vector3 home)
    {
        alwaysAggro = true;
        homePos = home;
    }

    /// <summary>不用愣住，直接開始追玩家</summary>
    public void ForceChase()
    {
        if (state == State.Dead || state == State.Dropping || state == State.Jumping || state == State.Spawning) return;
        nextPounceTime = Time.time + Random.Range(1.5f, 4f);
        SetState(State.Chase);
    }

    // ---------------- 閒置時在出生點附近遊蕩 ----------------

    void UpdateIdleWander(bool playerAlive)
    {
        // 隨時檢測玩家是否進入警戒範圍
        if (playerAlive && CanDetectPlayer())
        {
            controller.SetAIInput(Vector3.zero);
            Alert(true);
            return;
        }

        if (!enableWander)
        {
            controller.SetAIInput(Vector3.zero);
            TurnTowards(homeRot * Vector3.forward, 180f);
            return;
        }

        // 遊蕩邏輯：移動 vs 停頓等待
        if (isWanderingMoving)
        {
            Vector3 diff = wanderTarget - transform.position;
            diff.y = 0f;
            float dist = diff.magnitude;

            if (dist <= 0.6f)
            {
                // 到達漫步目標點，轉為停頓待機
                isWanderingMoving = false;
                wanderWaitTimer = Random.Range(wanderWaitTime.x, wanderWaitTime.y);
                controller.SetAIInput(Vector3.zero);
            }
            else
            {
                Vector3 moveDir = (diff.normalized + Separation() * 0.5f).normalized * wanderSpeedMultiplier;
                controller.SetAIInput(moveDir);
                TurnTowards(diff, 200f);
            }
        }
        else
        {
            controller.SetAIInput(Vector3.zero);
            wanderWaitTimer -= Time.deltaTime;
            if (wanderWaitTimer <= 0f)
            {
                PickNewWanderTarget();
            }
        }
    }

    void PickNewWanderTarget()
    {
        Vector2 circle = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePos + new Vector3(circle.x, 0f, circle.y);

        // 利用 Raycast 確保遊蕩點貼在地面上且非懸空
        if (Physics.Raycast(wanderTarget + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f))
        {
            wanderTarget.y = hit.point.y;
        }

        isWanderingMoving = true;
    }

    void UpdateChase(bool playerAlive)
    {
        if (!playerAlive) { SetState(State.Return); return; }

        Vector3 to = FlatToPlayer();
        float dist = to.magnitude;
        if (!alwaysAggro && (dist > giveUpDistance || FlatDistance(transform.position, homePos) > leashRadius))
        {
            SetState(State.Return);
            return;
        }

        // 硬直中或正在揮劍：不移動（揮劍時的轉向和踏步由 EnemyCombat 處理）
        if (staggerTimer > 0f || combat.IsAttacking)
        {
            controller.SetAIInput(Vector3.zero);
            return;
        }

        // 撲向玩家
        if (canJump && controller.IsGrounded && pounceRange.y > 0f && Time.time >= nextPounceTime
            && dist >= pounceRange.x && dist <= Mathf.Min(pounceRange.y, maxJumpDistance) && TryPounce(to))
            return;

        bool stopAtEdge = false;
        if (dist > attackRange * 0.8f)
        {
            Vector3 toN = to.normalized;
            // 前面是缺口，或有一階走不上去的高台 → 找地方跳。跑得越快看得越遠，才來得及在邊緣前停下來
            Ground ahead = controller.IsGrounded ? ProbeAhead(toN, 1.1f + controller.CurrentSpeed * 0.08f) : Ground.Walk;
            if ((ahead == Ground.Gap || ahead == Ground.Ledge) && canJump && Time.time >= nextJumpSearchTime)
            {
                nextJumpSearchTime = Time.time + 0.3f;
                if (FindJumpLanding(toN, dist, out Vector3 landing, out float apex)) { BeginJump(landing, apex, false); return; }
            }
            stopAtEdge = ahead == Ground.Gap; // 跳不過去：停在邊緣等，不要自己走進洞裡
        }

        if (stopAtEdge)
        {
            controller.SetAIInput(Vector3.zero);
            TurnTowards(to, 360f);
        }
        else if (dist > attackRange * 0.8f)
        {
            Vector3 toN = to.normalized;
            Vector3 dir = toN + Separation();
            bool sprint = sprintDistance > 0f && dist > sprintDistance;
            controller.SetAIInput(dir.normalized, sprint);
        }
        else
        {
            controller.SetAIInput(Vector3.zero);
            TurnTowards(to, 360f);
        }

        if (dist <= attackRange && Time.time >= nextAttackTime && Vector3.Angle(transform.forward, to) < 50f)
            combat.StartCombo(Random.Range(1, maxComboHits + 1));
    }

    void UpdateReturn(bool playerAlive)
    {
        // 玩家又靠近（而且在自己的地盤內）→ 再追一次
        if (playerAlive && CanDetectPlayer() && FlatDistance(player.position, homePos) < leashRadius)
        {
            SetState(State.Chase);
            return;
        }

        Vector3 to = homePos - transform.position;
        to.y = 0f;
        if (to.magnitude < 0.4f)
        {
            controller.SetAIInput(Vector3.zero);
            health.HealFull();
            SetState(State.Idle);
            return;
        }
        controller.SetAIInput((to.normalized + Separation() * 0.5f).normalized);
    }

    // =====================================================================
    //  發現玩家 / 被打
    // =====================================================================

    bool CanDetectPlayer()
    {
        Vector3 d = player.position - transform.position;
        if (Mathf.Abs(d.y) > alertHeight) return false;
        d.y = 0f;
        if (d.magnitude > alertRadius) return false;
        return !requireLineOfSight || HasLineOfSight();
    }

    bool HasLineOfSight()
    {
        Vector3 a = transform.position + Vector3.up * 0.6f;
        Vector3 b = player.position + Vector3.up * 0.6f;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(a, d / len, rayHits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider is CharacterController) continue; // 角色不算遮擋
            return false;
        }
        return true;
    }

    /// <summary>發現玩家。callForHelp = true 時會叫醒附近的同伴</summary>
    public void Alert(bool callForHelp)
    {
        if (state != State.Idle && state != State.Return) return;
        nextPounceTime = Time.time + Random.Range(2f, 5f);
        SetState(State.Alerted);

        if (!callForHelp) return;
        foreach (var other in All)
        {
            if (other == this) continue;
            if (FlatDistance(other.transform.position, transform.position) <= callForHelpRadius)
                other.Alert(false);
        }
    }

    void OnDamaged(float amount)
    {
        staggerTimer = health.staggerTime;
        combat.CancelAttack();
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + health.staggerTime + 0.2f);

        // 從背後偷襲 → 直接進入戰鬥（不愣住）
        if (state == State.Idle || state == State.Return || state == State.Alerted)
        {
            SetState(State.Chase);
            foreach (var other in All)
                if (other != this && FlatDistance(other.transform.position, transform.position) <= callForHelpRadius)
                    other.Alert(false);
        }
    }

    void OnDied()
    {
        SetState(State.Dead);
        emergePending = false;
        combat.CancelAttack();
        controller.SetAIInput(Vector3.zero);
        ClearDropMarker();
    }

    /// <summary>玩家重生時：復活並回到原位（勾了 stayDeadOnPlayerRespawn 的話，已經被打死的不會復活）</summary>
    public void ResetEnemy()
    {
        if (stayDeadOnPlayerRespawn && health.IsDead) return;
        combat.CancelAttack();
        ClearDropMarker();
        health.Revive();

        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.SetPositionAndRotation(homePos, homeRot);
        cc.enabled = true;

        controller.enabled = true;
        controller.ResetMotion();
        controller.SetAIInput(Vector3.zero);
        isWanderingMoving = false;
        wanderWaitTimer = Random.Range(0.5f, 2f);
        staggerTimer = 0f;
        nextAttackTime = 0f;
        nextPounceTime = 0f;
        wasAttacking = false;
        emergePending = false;
        var visual = GetComponent<EnemyVisual>();
        if (visual != null) visual.ResetVisual();
        SetState(State.Idle);
    }

    // =====================================================================
    //  跳躍
    // =====================================================================

    float FeetY => transform.position.y - (cc != null ? cc.height * 0.5f - cc.center.y : 1f);

    /// <summary>前方某一點的地面狀況</summary>
    enum Ground
    {
        Walk,   // 走得過去
        Gap,    // 沒有地板（洞、場外）
        Ledge,  // 有一階走不上去、但跳得上去的高台
        Drop,   // 比較低的地面（走下去或跳下去都可以）
        Wall,   // 牆、柱子、太高的障礙物
    }

    /// <summary>檢查 pos 正下方的地面。groundY = 地面高度（Gap / Wall 時沒有意義）</summary>
    Ground Probe(Vector3 pos, out float groundY)
    {
        float feet = FeetY;
        Vector3 origin = new Vector3(pos.x, feet + maxJumpUp + 0.6f, pos.z);
        groundY = feet;

        // 檢查點在東西裡面 → 是牆或很高的柱子（從裡面往下打的射線打不到它自己）。角色不算
        int inside = Physics.OverlapSphereNonAlloc(origin, 0.05f, overlapHits, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < inside; i++)
            if (!(overlapHits[i] is CharacterController)) return Ground.Wall;

        int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, maxJumpUp + 0.6f + maxJumpDown, ~0, QueryTriggerInteraction.Ignore);
        float top = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider is CharacterController) continue;
            if (rayHits[i].point.y > top) top = rayHits[i].point.y;
        }
        if (float.IsNegativeInfinity(top)) return Ground.Gap;

        groundY = top;
        float dy = top - feet;
        float step = cc != null ? cc.stepOffset + 0.1f : 0.45f;
        if (dy > maxJumpUp) return Ground.Wall;
        if (dy > step) return Ground.Ledge;
        if (dy < -1.2f) return Ground.Drop;
        return Ground.Walk;
    }

    float StepHeight => cc != null ? cc.stepOffset + 0.1f : 0.45f;

    /// <summary>
    /// 沿著 dir 往前看 distance 公尺，每 0.35m 取一點：
    /// 遇到洞回傳 Gap；相鄰兩點之間突然高一階（走不上去但跳得上去）回傳 Ledge；
    /// 斜坡、樓梯（每一小步都走得上去）算 Walk。
    /// </summary>
    Ground ProbeAhead(Vector3 dir, float distance)
    {
        float prevY = FeetY;
        float start = cc != null ? cc.radius : 0.5f;
        for (float s = start; ; s += 0.35f)
        {
            bool last = s >= distance;
            if (last) s = distance;
            Ground g = Probe(transform.position + dir * s, out float y);
            if (g == Ground.Gap) return Ground.Gap;
            if (g == Ground.Wall) return Ground.Wall;
            if (y - prevY > StepHeight) return Ground.Ledge;
            prevY = y;
            if (last) return Ground.Walk;
        }
    }

    /// <summary>
    /// 往玩家的方向（以及左右偏一點的方向）找落點：要跳得到、站得住、而且比現在更靠近玩家。
    /// apex = 這一跳需要跳多高才不會撞到中間的東西。
    /// </summary>
    bool FindJumpLanding(Vector3 toPlayerDir, float distToPlayer, out Vector3 landing, out float apex)
    {
        landing = Vector3.zero;
        apex = 0f;
        float best = distToPlayer - 0.5f;
        bool found = false;
        float feet = FeetY;

        foreach (float a in JumpAngles)
        {
            Vector3 d = Quaternion.Euler(0f, a, 0f) * toPlayerDir;
            bool blocked = false;
            float highest = feet;   // 路上最高的地面（跳的時候要越過它）
            float prevY = feet;     // 上一個取樣點的地面高度（用來分辨「斜坡」和「突然高一階」）

            for (float s = 0.6f; s + 0.7f <= maxJumpDistance; s += 0.5f)
            {
                Vector3 p = transform.position + d * s;
                Ground g = Probe(p, out float y1);
                if (g == Ground.Wall) break;                 // 中間有牆 / 柱子，這個方向不跳
                if (g != Ground.Gap) highest = Mathf.Max(highest, y1);

                if (!blocked)
                {
                    // 還走得過去（平地、斜坡、樓梯、往下的小落差）就繼續往前看
                    if (g != Ground.Gap && y1 - prevY <= StepHeight) { prevY = y1; continue; }
                    blocked = true;                          // 從這裡開始是缺口或高台
                }
                if (g == Ground.Gap) continue;

                // 缺口 / 高台之後第一個站得住的地方（再往裡面 0.7m，不要落在邊緣）
                Vector3 q = p + d * 0.7f;
                Ground g2 = Probe(q, out float y2);
                if (g2 == Ground.Wall) break;
                if (g2 == Ground.Gap || Mathf.Abs(y1 - y2) > 0.5f) continue;

                float score = FlatDistance(q, player.position);
                if (score < best)
                {
                    best = score;
                    landing = new Vector3(q.x, y2, q.z);
                    apex = Mathf.Max(highest, y2) - feet + 0.7f;
                    found = true;
                }
                break;
            }
        }
        return found;
    }

    static readonly float[] JumpAngles = { 0f, 25f, -25f, 50f, -50f };

    bool TryPounce(Vector3 toPlayer)
    {
        nextPounceTime = Time.time + 1f; // 這次不行的話，1 秒後再試
        if (staggerTimer > 0f || combat.IsAttacking || !HasLineOfSight()) return false;

        // 落在玩家面前一點點，不要剛好站在頭上
        Vector3 target = player.position - toPlayer.normalized * ((cc != null ? cc.radius : 0.5f) + 0.5f);
        Ground g = Probe(target, out float y);
        if (g == Ground.Gap || g == Ground.Wall) return false;

        BeginJump(new Vector3(target.x, y, target.z), 0f, true);
        nextPounceTime = Time.time + Random.Range(pounceCooldown.x, pounceCooldown.y);
        return true;
    }

    void BeginJump(Vector3 landing, float apex, bool pounce)
    {
        jumpTarget = landing;
        jumpApex = apex;
        jumpIsPounce = pounce;
        jumpLaunched = false;
        controller.SetAIInput(Vector3.zero);
        SetState(State.Jumping);
    }

    void UpdateJump()
    {
        Vector3 flat = jumpTarget - transform.position;
        flat.y = 0f;

        if (!jumpLaunched)
        {
            // 蹲一下、轉向落點（這時候被打就取消）
            controller.SetAIInput(Vector3.zero);
            if (staggerTimer > 0f) { SetState(State.Chase); return; }
            TurnTowards(flat, 720f);
            if (stateTimer < jumpWindup) return;

            // 落點的高度是「地面」，角色中心要再加上半個身高
            float half = FeetY - transform.position.y;
            Vector3 target = new Vector3(jumpTarget.x, jumpTarget.y - half, jumpTarget.z);
            float apex = Mathf.Max(Mathf.Clamp(flat.magnitude * 0.3f, 1.2f, 3f), jumpApex);
            controller.Launch(controller.LaunchVelocityTo(target, apex));
            FaceInstant(flat);
            jumpLaunched = true;
            stateTimer = 0f;
            return;
        }

        controller.SetAIInput(Vector3.zero);
        bool landed = stateTimer > 0.15f && controller.IsGrounded;
        if (!landed && stateTimer < 4f) return;   // 還在空中（落點塌了就會一直掉，掉出場地由 Update 處理）

        if (landed && jumpIsPounce && player != null && playerHealth != null && !playerHealth.IsDead)
        {
            Vector3 to = player.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            if (to.magnitude <= pounceRadius && Mathf.Abs(dy) < 2f)
                playerHealth.TakeDamage(pounceDamage, dir * 6f + Vector3.up * 4f);
            if (dy < -1f) controller.AddImpulse(-dir * 6f); // 踩在玩家頭上 → 滑下來
        }
        if (landed) controller.ResetMotion(); // 落地就站穩，不要往前滑出去
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + 0.35f);
        SetState(State.Chase);
    }

    // =====================================================================
    //  從天而降（陷阱小怪）
    // =====================================================================

    /// <summary>從 landingPoint 正上方 height 公尺處掉下來，落地時砸傷附近的玩家</summary>
    public void BeginDrop(Vector3 landingPoint, float height)
    {
        dropGroundY = FindGroundY(landingPoint, height, landingPoint.y - 1f);

        Vector3 start = new Vector3(landingPoint.x, dropGroundY + height, landingPoint.z);
        cc.enabled = false;
        transform.position = start;
        cc.enabled = true;
        controller.enabled = true;
        controller.ResetMotion();
        controller.SetAIInput(Vector3.zero);
        isWanderingMoving = false;
        wanderWaitTimer = Random.Range(0.5f, 2f);
        if (player != null) FaceInstant(FlatToPlayer());

        dropStartY = start.y;
        dropMarker = CreateDropMarker(new Vector3(landingPoint.x, dropGroundY + 0.03f, landingPoint.z));
        SetState(State.Dropping);
    }

    void UpdateDrop()
    {
        controller.SetAIInput(Vector3.zero);

        // 地上的紅圈隨著小怪落下越來越大
        if (dropMarker != null)
        {
            float k = Mathf.InverseLerp(dropStartY, dropGroundY + 1f, transform.position.y);
            float d = Mathf.Lerp(0.6f, stompRadius * 2f, k);
            dropMarker.transform.localScale = new Vector3(d, 0.01f, d);
        }

        if ((stateTimer > 0.15f && controller.IsGrounded) || stateTimer > 5f) Land();
    }

    void Land()
    {
        ClearDropMarker();
        ThirdPersonCamera.ShakeMain(0.25f, 0.25f);
        GameAudio.Play("SFX/rock_hit", transform.position, 0.45f, 1.4f, 0.08f, 1f, 40f);

        if (player != null && playerHealth != null && !playerHealth.IsDead)
        {
            Vector3 to = player.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            if (to.magnitude <= stompRadius && Mathf.Abs(dy) < 2.5f)
                playerHealth.TakeDamage(stompDamage, dir * 7f + Vector3.up * 6f);

            // 剛好踩在玩家頭上 → 滑下來
            if (dy < -1f) controller.AddImpulse(-dir * 6f);
        }

        nextAttackTime = Time.time + 0.6f;
        nextPounceTime = Time.time + Random.Range(3f, 6f);
        SetState(State.Chase);
    }

    float FindGroundY(Vector3 point, float height, float fallback)
    {
        Vector3 origin = new Vector3(point.x, point.y + height + 5f, point.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, height + 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider is CharacterController) continue;
            if (rayHits[i].point.y > best) best = rayHits[i].point.y; // 最上面的地面
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    static GameObject CreateDropMarker(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "DropMarker";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.6f, 0.01f, 0.6f);

        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (dropMarkerMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                dropMarkerMat = new Material(shader);
                dropMarkerMat.SetColor("_BaseColor", new Color(0.9f, 0.1f, 0.1f));
                dropMarkerMat.SetColor("_Color", new Color(0.9f, 0.1f, 0.1f));
            }
        }
        if (dropMarkerMat != null) r.sharedMaterial = dropMarkerMat;
        return go;
    }

    void ClearDropMarker()
    {
        if (dropMarker != null) Destroy(dropMarker);
        dropMarker = null;
    }

    // =====================================================================
    //  工具
    // =====================================================================

    void SetState(State s)
    {
        state = s;
        stateTimer = 0f;
    }

    Vector3 FlatToPlayer()
    {
        Vector3 v = player.position - transform.position;
        v.y = 0f;
        return v;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    Vector3 Separation()
    {
        Vector3 push = Vector3.zero;
        foreach (var o in All)
        {
            if (o == this || o.state == State.Dead) continue;
            Vector3 d = transform.position - o.transform.position;
            d.y = 0f;
            float m = d.magnitude;
            if (m > 0.001f && m < separationRadius) push += d / m * (1f - m / separationRadius);
        }
        return push;
    }

    void TurnTowards(Vector3 flatDir, float degreesPerSecond)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flatDir),
                                                      degreesPerSecond * Time.deltaTime);
    }

    void FaceInstant(Vector3 flatDir)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flatDir);
    }

    void OnGUI()
    {
        if (state != State.Alerted) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * (health.barHeightOffset + 0.4f));
        if (sp.z <= 0f || sp.z > 40f) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(1f, 0.85f, 0.2f);
        GUI.Label(new Rect(sp.x - 20f, Screen.height - sp.y - 40f, 40f, 40f), "!", style);
        GUI.color = Color.white;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePos : transform.position;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);   // 黃 = 警戒範圍
        Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(home, wanderRadius);               // 綠 = 遊蕩範圍
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(home, leashRadius);                 // 灰 = 最遠追到哪
    }
}
