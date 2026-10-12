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
    }

    [Header("攻擊動畫（依序對應連擊的第 1、2、3 段）")]
    public AttackAnim[] attacks =
    {
        new AttackAnim { state = "Slash01", hitTime = 0.33f },
        new AttackAnim { state = "Slash02", hitTime = 0.40f },
        new AttackAnim { state = "Stab",    hitTime = 0.42f },
    };

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
    bool dead;
    readonly System.Collections.Generic.HashSet<string> warned = new System.Collections.Generic.HashSet<string>();

    void Awake()
    {
        ai = GetComponent<EnemyAI>();
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
        controller = GetComponent<ThirdPersonController>();
        if (animator == null) animator = GetComponentInChildren<Animator>(true);

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

        float v = controller.CurrentSpeed;
        if (v < idleThreshold)
        {
            animator.speed = 1f;
            Go("Idle", crossFade);
        }
        else if (v < runThreshold)
        {
            animator.speed = Mathf.Clamp(v / Mathf.Max(walkAnimSpeed, 0.01f), 0.6f, 1.8f);
            Go("Walk", crossFade);
        }
        else
        {
            animator.speed = Mathf.Clamp(v / Mathf.Max(runAnimSpeed, 0.01f), 0.7f, 2f);
            Go("Run", crossFade);
        }
    }

    // ---------------- 事件 ----------------

    /// <summary>連擊的每一刀開始時：播對應的攻擊動畫，並調整速度讓「砍到」對上傷害判定</summary>
    void OnSwing(int index)
    {
        if (!HasAnimator || dead || attacks == null || attacks.Length == 0) return;
        var a = attacks[index % attacks.Length];
        float untilHit = combat.windupTime + combat.swingTime * 0.5f;
        animator.speed = a.hitTime > 0f && untilHit > 0f ? Mathf.Clamp(a.hitTime / untilHit, 0.5f, 2f) : 1f;
        Go(a.state, 0.06f, true);
    }

    void OnHit(float amount)
    {
        if (!HasAnimator || dead) return;
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
