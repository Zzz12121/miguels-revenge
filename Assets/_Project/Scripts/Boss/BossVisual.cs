using UnityEngine;

/// <summary>
/// 把 Boss 的招式接到模型動畫（Animator）。
/// - 平常：依照 Boss 的移動速度，自動在 Idle / Walk 之間切換
/// - 出招：BossController 呼叫 PlayAction("Slam", 幾秒後打中)，
///   這裡會自動調整動畫播放速度，讓動畫裡「打中的那一格」剛好對上程式判定傷害的時間
/// - 出招結束：BossController 呼叫 EndAction，回到 Idle / Walk
///
/// Animator 裡的狀態名稱要跟招式名稱一樣：
///   Idle、Walk、Swipe、Slam、Roar、Beam、Jump、JumpLand、Death
/// 沒有這個元件時，Boss 就照舊只用程式做前傾 / 後仰。
/// </summary>
public class BossVisual : MonoBehaviour
{
    [Tooltip("模型上的 Animator")]
    public Animator animator;

    [Tooltip("有模型動畫時，程式原本的「整個身體前傾 / 後仰 / 伸縮」保留多少。0 = 全部交給動畫，1 = 跟膠囊時一樣")]
    [Range(0f, 1f)] public float proceduralLean = 0.3f;
    [Tooltip("移動速度超過這個值就播走路動畫 (m/s)")]
    public float walkThreshold = 0.4f;
    [Tooltip("走路動畫原本對應的移動速度 (m/s)。Boss 走得比這個快，走路動畫就播得比較快")]
    public float walkAnimSpeed = 3.5f;
    [Tooltip("動畫之間的過渡時間 (秒)")]
    public float crossFade = 0.15f;

    [System.Serializable]
    public struct ActionTiming
    {
        [Tooltip("Animator 裡的狀態名稱")]
        public string state;
        [Tooltip("動畫裡「打中」那一格的時間（秒，以原本速度播放時）。0 = 不對時間，用原本速度播")]
        public float hitTime;
    }

    [Tooltip("每個招式動畫裡打中的時間。在 Blender 改了動畫的節奏，就改這裡（格數 ÷ 24）")]
    public ActionTiming[] timings =
    {
        new ActionTiming { state = "Swipe", hitTime = 17f / 24f },
        new ActionTiming { state = "Slam",  hitTime = 34f / 24f },
        new ActionTiming { state = "Roar",  hitTime = 9f / 24f },
        new ActionTiming { state = "Beam",  hitTime = 11f / 24f },
        new ActionTiming { state = "Jump",  hitTime = 16f / 24f },
    };

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
    public string CurrentState { get; private set; } = "";

    bool actionActive;
    Vector3 lastPos;
    float smoothedSpeed;

    void Start()
    {
        lastPos = transform.position;
        if (HasAnimator) Go("Idle", 0f);
    }

    void Update()
    {
        if (!HasAnimator || Time.deltaTime <= 0f) return;

        // 用位置變化算出移動速度（不需要 Boss 另外告訴我）
        Vector3 delta = transform.position - lastPos;
        delta.y = 0f;
        lastPos = transform.position;
        float speed = delta.magnitude / Time.deltaTime;
        smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-10f * Time.deltaTime));

        if (actionActive) return;

        if (smoothedSpeed > walkThreshold)
        {
            if (CurrentState != "Walk") Go("Walk", crossFade);
            animator.speed = Mathf.Clamp(smoothedSpeed / Mathf.Max(walkAnimSpeed, 0.01f), 0.5f, 2.5f);
        }
        else
        {
            if (CurrentState != "Idle") Go("Idle", crossFade);
            animator.speed = 1f;
        }
    }

    /// <summary>
    /// 播放招式動畫。secondsUntilHit = 程式裡從現在到「打中」還有幾秒，
    /// 會據此調整播放速度；不需要對時間就傳 -1。
    /// </summary>
    public void PlayAction(string state, float secondsUntilHit = -1f)
    {
        if (!HasAnimator) return;
        actionActive = true;

        float speed = 1f;
        float hit = HitTimeOf(state);
        if (secondsUntilHit > 0f && hit > 0f) speed = Mathf.Clamp(hit / secondsUntilHit, 0.3f, 3f);
        animator.speed = speed;
        Go(state, crossFade * 0.5f);
    }

    /// <summary>招式結束，回到 Idle / Walk</summary>
    public void EndAction()
    {
        actionActive = false;
        if (HasAnimator) animator.speed = 1f;
    }

    /// <summary>戰鬥重置：直接回到 Idle</summary>
    public void ResetVisual()
    {
        actionActive = false;
        smoothedSpeed = 0f;
        lastPos = transform.position;
        if (!HasAnimator) return;
        animator.speed = 1f;
        Go("Idle", 0f);
    }

    float HitTimeOf(string state)
    {
        if (timings == null) return 0f;
        foreach (var t in timings)
            if (t.state == state) return t.hitTime;
        return 0f;
    }

    void Go(string state, float fade)
    {
        if (!animator.HasState(0, Animator.StringToHash(state)))
        {
            Debug.LogWarning($"[BossVisual] Animator 裡沒有「{state}」這個狀態");
            CurrentState = state; // 避免每一幀都警告
            return;
        }
        CurrentState = state;
        if (fade <= 0f) animator.Play(state, 0, 0f);
        else animator.CrossFadeInFixedTime(state, fade, 0, 0f);
    }
}
