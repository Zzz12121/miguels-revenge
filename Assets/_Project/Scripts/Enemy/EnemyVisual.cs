using UnityEngine;

/// <summary>
/// 把小怪的狀態接到模型動畫（Animator）。裝了模型的小怪才需要（安裝骷髏模型時會自動加上）。
///
/// Animator 裡的狀態名稱：
///   Idle、Walk、Run          依移動速度自動切換，動畫播放速度會跟著移動速度調整
///   Slash01、Slash02、Stab   連擊的第 1、2、3 段。會自動調整播放速度，讓動畫砍下去的那一刻對上傷害判定
///   Hit                      被打中
///   Death                    死亡
///   Jump、Fall               起跳、在空中往下掉（跳躍、從天而降、掉進洞裡）
///   Spawn                    從地底冒出來（波次戰在遠處生成時）
///   Scream                   發現玩家時吼一聲
/// 少了哪個狀態，那個動作就維持上一個動畫，不會出錯。
/// </summary>
[RequireComponent(typeof(EnemyAI))]
public class EnemyVisual : MonoBehaviour
{
    [Tooltip("模型上的 Animator")]
    public Animator animator;

    [Header("移動動畫")]
    [Tooltip("速度低於這個值算站著不動 (m/s)")]
    public float idleThreshold = 0.3f;
    [Tooltip("速度超過這個值播跑步，否則播走路 (m/s)")]
    public float runThreshold = 2.5f;
    [Tooltip("走路動畫原本對應的移動速度 (m/s)。實際走得比這個快，動畫就播得比較快")]
    public float walkAnimSpeed = 1.6f;
    [Tooltip("跑步動畫原本對應的移動速度 (m/s)")]
    public float runAnimSpeed = 4.2f;
    [Tooltip("動畫之間的過渡時間 (秒)")]
    public float crossFade = 0.12f;

    [System.Serializable]
    public struct AttackAnim
    {
        [Tooltip("Animator 裡的狀態名稱")]
        public string state;
        [Tooltip("動畫裡「砍到」那一刻的時間（秒，以原本速度播放時）")]
        public float hitTime;
        [Tooltip("整段動畫的長度（秒，以原本速度播放時）。0 = 用 hitTime 推算")]
        public float clipLength;
    }

    [Header("攻擊動畫（依序對應連擊的第 1、2、3 段）")]
    public AttackAnim[] attacks =
    {
        new AttackAnim { state = "Slash01", hitTime = 0.33f },
        new AttackAnim { state = "Slash02", hitTime = 0.40f },
        new AttackAnim { state = "Stab",    hitTime = 0.42f },
    };

    [System.Serializable]
    public struct MoveAnim
    {
        public EnemyCombat.Move move;
        [Tooltip("Animator 裡的狀態名稱")]
        public string state;
        [Tooltip("動畫裡「砍到」那一刻的時間（秒，以原本速度播放時）")]
        public float hitTime;
    }

    [Header("招式動畫（EnemyCombat 勾選 useMoveSet 時用，依招式挑動畫）")]
    public MoveAnim[] moveAnims =
    {
        new MoveAnim { move = EnemyCombat.Move.DiagonalSlash, state = "Slash01",    hitTime = 0.33f },
        new MoveAnim { move = EnemyCombat.Move.Thrust,        state = "Stab",       hitTime = 0.42f },
        // 直砍：Mixamo 劍盾 attack — 手臂高舉過頭、上身前壓劈下（約 1.17 秒砍到）
        new MoveAnim { move = EnemyCombat.Move.Overhead,      state = "SS_Attack1", hitTime = 1.17f },
        // 橫掃：Mixamo 劍盾 attack (2) — 原地轉一整圈水平掃（約 0.84 秒掃到）
        new MoveAnim { move = EnemyCombat.Move.Sweep,         state = "SS_Attack2", hitTime = 0.84f },
    };

    [Header("舉盾格擋（EnemyShieldBlock 用）")]
    [Tooltip("舉起盾牌的動畫，播完接著播 blockHoldState 撐住")]
    public string blockState = "Block";
    public string blockHoldState = "BlockIdle";
    [Tooltip("盾牌擋到攻擊時的動畫（被震一下）；沒有就重播 blockState")]
    public string blockImpactState = "BlockImpact";
    [Tooltip("舉盾動畫多久後切到撐住的動畫 (秒)")]
    public float blockRaiseTime = 0.45f;

    [Header("橋邊被打倒")]
    [Tooltip("翻滾掉下去時播的動畫（沒有就維持死亡動畫）")]
    public string ledgeFallState = "Fall";

    [Header("流暢度")]
    [Tooltip("移動動畫播放速度的平滑程度（越大跟得越快）")]
    public float animSpeedSmoothing = 8f;
    [Tooltip("換成走 / 跑 / 站之後至少維持多久才會再換 (秒)，避免速度在門檻附近時一直抖")]
    public float minLocomotionHold = 0.2f;
    [Tooltip("畫面外的骷髏只算動畫、不更新骨架（省效能）")]
    public bool cullOffscreen = true;

    [Header("橫移 / 原地轉身 / 挑釁")]
    [Tooltip("面向玩家橫著走時（圍攻繞圈）播的動畫；沒有這兩個狀態就用一般走路")]
    public string strafeLeftState = "StrafeLeft", strafeRightState = "StrafeRight";
    [Tooltip("橫移動畫原本對應的移動速度 (m/s)")]
    public float strafeAnimSpeed = 3.6f;
    [Tooltip("站著原地轉身時播的動畫")]
    public string turnLeftState = "TurnLeft", turnRightState = "TurnRight";
    [Tooltip("站著時轉得比這個快 (度/秒) 就播轉身動畫")]
    public float turnInPlaceRate = 100f;
    [Tooltip("挑釁（圍攻的空檔吼一聲）的動畫")]
    public string tauntState = "Scream";

    [Header("其他")]
    [Tooltip("從地底冒出來的動畫播多久之後，小怪才開始行動 (秒)")]
    public float spawnDuration = 0.85f;
    [Tooltip("死亡動畫的長度 (秒)，播完才沉入地面")]
    public float deathDuration = 1.5f;

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

    EnemyAI ai;
    EnemyCombat combat;
    EnemyHealth health;
    ThirdPersonController controller;

    string current = "Idle";
    EnemyAI.State lastState = EnemyAI.State.Idle;
    float hitUntil;
    float blockUntil, blockHoldAt = float.PositiveInfinity;
    float locomotionSince, lastYaw, yawRate;
    bool dead;
    readonly System.Collections.Generic.HashSet<string> warned = new System.Collections.Generic.HashSet<string>();

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
        controller = GetComponent<ThirdPersonController>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null && cullOffscreen) animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        lastYaw = transform.eulerAngles.y;

        if (combat != null) combat.OnSwing += OnSwing;
        if (health != null)
        {
            health.OnDamaged += OnHit;
            health.OnDied += OnDied;
        }
    }

    void OnDestroy()
    {
        if (combat != null) combat.OnSwing -= OnSwing;
        if (health != null)
        {
            health.OnDamaged -= OnHit;
            health.OnDied -= OnDied;
        }
    }

    void Update()
    {
        if (!HasAnimator || dead) return;

        var st = ai.CurrentState;
        bool entered = st != lastState;
        lastState = st;

        if (Time.time < blockUntil)                       // 舉盾中
        {
            if (Time.time >= blockHoldAt) { blockHoldAt = float.PositiveInfinity; Go(blockHoldState, 0.12f); }
            return;
        }
        if (Time.time < hitUntil) return;                 // 正在播受擊動畫
        if (combat != null && combat.IsAttacking) return; // 正在播攻擊動畫（OnSwing 播的）

        switch (st)
        {
            case EnemyAI.State.Dropping:
                animator.speed = 1f;
                Go("Fall", 0.1f);
                break;

            case EnemyAI.State.Jumping:
                if (entered)
                {
                    animator.speed = 1f;
                    Go("Jump", 0.05f, true);
                }
                else if (!controller.IsGrounded && controller.VerticalVelocity < -2f)
                    Go("Fall", 0.15f); // 過了最高點，開始往下掉
                break;

            case EnemyAI.State.Spawning:
                // 動畫在 PlaySpawn / PlayUnderground 就開始播了；萬一那時候 Animator 還沒準備好，這裡補播
                if (current != "Spawn")
                {
                    Go("Spawn", 0f, true);
                    animator.speed = frozen ? 0f : 1f;
                }
                break;

            case EnemyAI.State.Alerted:
                if (entered)
                {
                    animator.speed = 1.3f;
                    Go("Scream", crossFade, true);
                }
                break;

            case EnemyAI.State.Dead:
                break;

            default:
                Locomotion();
                break;
        }
    }

    void Locomotion()
    {
        // 掉進洞裡
        if (!controller.IsGrounded && controller.VerticalVelocity < -5f)
        {
            animator.speed = 1f;
            Go("Fall", 0.15f);
            return;
        }

        // 轉身速度（度/秒，平滑過）
        float yaw = transform.eulerAngles.y;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        yawRate = Mathf.Lerp(yawRate, Mathf.DeltaAngle(lastYaw, yaw) / dt, 1f - Mathf.Exp(-12f * dt));
        lastYaw = yaw;

        Vector3 vel = controller.HorizontalVelocity;
        float v = vel.magnitude;
        Vector3 local = transform.InverseTransformDirection(vel);

        // 門檻加一點緩衝（hysteresis）：速度在門檻附近時不會在兩個動畫之間來回跳
        bool moving = current == "Idle" || current == turnLeftState || current == turnRightState || current == tauntState
            ? v > idleThreshold * 1.35f : v > idleThreshold * 0.7f;
        bool running = current == "Run" ? v > runThreshold * 0.88f : v > runThreshold * 1.08f;
        // 橫著走，或面向玩家往後退（退的時候用側步，不要倒著播走路變成太空漫步）
        bool strafing = moving && !running && (Mathf.Abs(local.x) > Mathf.Abs(local.z) * 1.25f || local.z < -0.35f * v);

        string want; float speed;
        if (!moving)
        {
            if (Mathf.Abs(yawRate) > turnInPlaceRate && HasStateCached(yawRate > 0f ? turnRightState : turnLeftState))
            {
                want = yawRate > 0f ? turnRightState : turnLeftState;
                speed = Mathf.Clamp(Mathf.Abs(yawRate) / 90f, 0.8f, 2f); // 動畫本身約 1 秒轉 90 度
            }
            else { want = "Idle"; speed = 1f; }
        }
        else if (strafing && HasStateCached(local.x < 0f ? strafeLeftState : strafeRightState))
        {
            want = local.x < 0f ? strafeLeftState : strafeRightState;
            speed = Mathf.Clamp(v / Mathf.Max(strafeAnimSpeed, 0.01f), 0.6f, 1.6f);
        }
        else if (!running)
        {
            want = "Walk";
            speed = Mathf.Clamp(v / Mathf.Max(walkAnimSpeed, 0.01f), 0.6f, 1.8f);
        }
        else
        {
            want = "Run";
            speed = Mathf.Clamp(v / Mathf.Max(runAnimSpeed, 0.01f), 0.7f, 2f);
        }

        // 剛換過動畫：至少維持 minLocomotionHold 秒（從攻擊 / 受擊回來時不受限制）
        bool fromLocomotion = current == "Idle" || current == "Walk" || current == "Run" || current == strafeLeftState
            || current == strafeRightState || current == turnLeftState || current == turnRightState;
        if (want != current && fromLocomotion && Time.time - locomotionSince < minLocomotionHold
            && !(want == "Idle" && current != "Idle" && v < 0.05f))
        {
            want = current;
        }
        if (want != current)
        {
            locomotionSince = Time.time;
            Go(want, fromLocomotion ? crossFade : crossFade * 1.6f); // 從攻擊收招回來淡得久一點，比較順
        }
        // 播放速度平滑地跟上移動速度，不要每幀跳動
        animator.speed = Mathf.Lerp(animator.speed, speed, 1f - Mathf.Exp(-animSpeedSmoothing * dt));
    }

    readonly System.Collections.Generic.Dictionary<string, bool> stateCache = new System.Collections.Generic.Dictionary<string, bool>();
    bool HasStateCached(string state)
    {
        if (string.IsNullOrEmpty(state)) return false;
        if (!stateCache.TryGetValue(state, out bool has))
            stateCache[state] = has = animator.HasState(0, Animator.StringToHash(state));
        return has;
    }

    /// <summary>挑釁吼一聲（圍攻的空檔用），播完自動回到移動動畫</summary>
    public void PlayTaunt(float seconds = 1.1f)
    {
        if (!HasAnimator || dead || Time.time < blockUntil || combat != null && combat.IsAttacking) return;
        if (!HasStateCached(tauntState)) return;
        animator.speed = 1.15f;
        Go(tauntState, 0.12f, true);
        hitUntil = Time.time + seconds;
    }

    // ---------------- 事件 ----------------

    /// <summary>連擊的每一刀開始時：播對應的攻擊動畫，並調整速度讓「砍到」對上傷害判定</summary>
    void OnSwing(int index)
    {
        if (!HasAnimator || dead) return;
        blockUntil = 0f;
        float untilHit = combat.WindupFor(index) + combat.SwingFor(index) * 0.5f;
        // 招式：依這一刀的招式挑動畫（Animator 裡沒有那個狀態就退回原本依段數的動畫）
        var move = combat.CurrentMove;
        if (move != null && moveAnims != null)
            foreach (var m in moveAnims)
            {
                if (m.move != move.move || string.IsNullOrEmpty(m.state)) continue;
                if (!animator.HasState(0, Animator.StringToHash(m.state))) break;
                animator.speed = m.hitTime > 0f && untilHit > 0f ? Mathf.Clamp(m.hitTime / untilHit, 0.5f, 2f) : 1f;
                Go(m.state, 0.06f, true);
                return;
            }
        if (attacks == null || attacks.Length == 0) return;
        var a = attacks[index % attacks.Length];
        animator.speed = a.hitTime > 0f && untilHit > 0f ? Mathf.Clamp(a.hitTime / untilHit, 0.5f, 2f) : 1f;
        Go(a.state, 0.06f, true);
    }

    void OnHit(float amount)
    {
        if (!HasAnimator || dead) return;
        StopBlock();
        var st = ai.CurrentState;
        if (st == EnemyAI.State.Spawning || st == EnemyAI.State.Dropping) return; // 這兩個動作不被打斷
        animator.speed = 1f;
        Go("Hit", 0.05f, true);
        hitUntil = Time.time + Mathf.Max(0.25f, health != null ? health.staggerTime : 0.3f);
    }

    void OnDied()
    {
        dead = true;
        frozen = false;
        if (!HasAnimator) return;
        animator.speed = 1f;
        Go("Death", 0.08f, true);
    }

    /// <summary>舉盾格擋 seconds 秒：先播舉盾，再撐住</summary>
    public void PlayBlock(float seconds)
    {
        if (!HasAnimator || dead) return;
        animator.speed = 1f;
        Go(blockState, 0.06f, true);
        blockUntil = Time.time + seconds;
        blockHoldAt = Time.time + blockRaiseTime;
        hitUntil = 0f;
    }

    /// <summary>盾牌擋到攻擊：被震一下（還在舉盾中）</summary>
    public void PlayBlockImpact()
    {
        if (!HasAnimator || dead) return;
        animator.speed = 1f;
        string st = animator.HasState(0, Animator.StringToHash(blockImpactState)) ? blockImpactState : blockState;
        Go(st, 0.04f, true);
        blockHoldAt = Time.time + 0.25f;
    }

    /// <summary>放下盾牌（被打斷、時間到）</summary>
    public void StopBlock()
    {
        blockUntil = 0f;
        blockHoldAt = float.PositiveInfinity;
    }

    /// <summary>被打出橋外：翻滾掉下去時的動畫</summary>
    public void PlayLedgeFall()
    {
        if (!HasAnimator) return;
        animator.speed = 1.2f;
        Go(ledgeFallState, 0.1f, true);
    }

    [ContextMenu("Preview/斜砍")] void PreviewDiagonal() => PreviewMove(EnemyCombat.Move.DiagonalSlash);
    [ContextMenu("Preview/突刺")] void PreviewThrust() => PreviewMove(EnemyCombat.Move.Thrust);
    [ContextMenu("Preview/直砍")] void PreviewOverhead() => PreviewMove(EnemyCombat.Move.Overhead);
    [ContextMenu("Preview/橫掃")] void PreviewSweep() => PreviewMove(EnemyCombat.Move.Sweep);
    [ContextMenu("Preview/舉盾格擋")] void PreviewBlock() => PlayBlock(1.2f);

    void PreviewMove(EnemyCombat.Move move)
    {
        if (!HasAnimator || moveAnims == null) return;
        foreach (var m in moveAnims)
            if (m.move == move) { animator.speed = 1f; Go(m.state, 0.06f, true); hitUntil = Time.time + 1.2f; return; }
    }

    bool frozen; // 停在「鑽出來」動畫的第一格（整個人在地底下）

    /// <summary>躲在地底下等著鑽出來：停在 Spawn 動畫的第一格（那一格整個身體都在地面以下）</summary>
    public void PlayUnderground()
    {
        if (!HasAnimator) return;
        Go("Spawn", 0f, true);
        animator.speed = 0f;
        frozen = true;
    }

    /// <summary>從地底鑽出來（EnemyAI.BeginSpawn / BeginEmerge 呼叫）</summary>
    public void PlaySpawn()
    {
        if (!HasAnimator) return;
        bool waiting = frozen && current == "Spawn";
        frozen = false;
        animator.speed = 1f;
        if (!waiting) Go("Spawn", 0f, true); // 已經停在第一格等著的話，放開就好，不用重播
    }

    /// <summary>復活 / 重置：回到待機</summary>
    public void ResetVisual()
    {
        dead = false;
        frozen = false;
        hitUntil = 0f;
        StopBlock();
        lastState = EnemyAI.State.Idle;
        if (!HasAnimator || !animator.isActiveAndEnabled) { current = "Idle"; return; }
        animator.speed = 1f;
        Go("Idle", 0f, true);
    }

    void Go(string state, float fade, bool restart = false)
    {
        if (!restart && current == state) return;
        if (!animator.HasState(0, Animator.StringToHash(state)))
        {
            if (warned.Add(state)) Debug.LogWarning($"[EnemyVisual] Animator 裡沒有「{state}」這個狀態", this);
            return;
        }
        current = state;
        if (fade <= 0f) animator.Play(state, 0, 0f);
        else animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }
}
