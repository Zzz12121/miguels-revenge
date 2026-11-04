using System;
using UnityEngine;

/// <summary>
/// 百足駭骨的動畫介面：Boss AI 只要呼叫這裡的方法，不用直接碰 Animator 參數。
///
///   SetMoveSpeed(0~1)   待機 ↔ 爬行（大於 0.1 就爬，數值越大腳步越快）
///   PlaySlash()         揮砍前肢（右鐮刀橫掃）
///   PlaySweepCombo()    連續大範圍揮砍（右 → 左 → 右 → 雙鐮重砍，共 4 段命中）
///   PlayTailWhip(side)  甩尾（身體像鞭子一樣橫掃一大圈；Left / Right / Alternate 左右輪流）
///   PlayDash()          衝刺攻擊（蓄力 → 原地高速爬行 → 雙鐮上撈收尾；實際位移由 AI 在 DashStart~DashEnd 之間移動）
///   PlayRanged(target)  遠程（嘴前聚三顆能量彈，扇形射向目標點）
///   PlayPounce()        撲擊（縮起 → 騰空 → 落地重砸；殘影圍殺技能的收尾，跳躍位移由 SkeletonCentipedeEncircleSkill 負責）
///   PlayRoar()          吼叫（立起身、雙鐮高舉、大顎全開；召喚蝙蝠的兩個技能用，召喚時機是 Roar 事件）
///   PlayBite()          撲咬（上半身後仰再往前撲）
///   PlayHit()           受擊硬直
///   PlayDeath()         死亡（倒地後停在最後一格）
///   Revive()            從死亡狀態回到待機（重生 / 重試用）
///
/// 攻擊判定時機由動畫事件決定：AttackImpact("Slash" / "Bite" / "Sweep1"~"Sweep4" / "TailWhip" / "Ranged") 在刀刃或大顎命中的那一格觸發，
/// SweepSwing("Right" / "Left" / "Both") 在每一刀開始揮出時觸發（給刀光特效用），
/// TailSwing("Left" / "Right") 在尾巴開始甩出時觸發，RangedCharge 在開始聚能量時觸發，
/// DashCharge / DashStart / DashEnd 標出衝刺的蓄力、開始衝、停下，AttackImpact("Dash") 是收尾上撈命中，
/// DeathFinished 在死亡動畫結束時觸發。動畫片段由 VFX/Characters/Skeleton Centipede/Bake Animations 產生。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public class SkeletonCentipedeAnimationDriver : MonoBehaviour
{
    public static readonly int SpeedParam = Animator.StringToHash("Speed");
    public static readonly int SlashParam = Animator.StringToHash("Slash");
    public static readonly int BiteParam = Animator.StringToHash("Bite");
    public static readonly int SweepParam = Animator.StringToHash("Sweep");
    public static readonly int TailWhipLeftParam = Animator.StringToHash("TailWhipLeft");
    public static readonly int TailWhipRightParam = Animator.StringToHash("TailWhipRight");
    public static readonly int DashParam = Animator.StringToHash("Dash");
    public static readonly int PounceParam = Animator.StringToHash("Pounce");
    public static readonly int RoarParam = Animator.StringToHash("Roar");
    public static readonly int RangedParam = Animator.StringToHash("Ranged");
    public static readonly int HitParam = Animator.StringToHash("Hit");
    public static readonly int DieParam = Animator.StringToHash("Die");
    public static readonly int DeadParam = Animator.StringToHash("Dead");
    public static readonly int CrawlRateParam = Animator.StringToHash("CrawlRate");

    [Tooltip("爬行動畫在 Speed = 1 時的播放倍率")]
    [SerializeField] float maxCrawlPlaybackSpeed = 1.6f;

    /// <summary>攻擊命中格觸發，參數是攻擊名稱（"Slash" 或 "Bite"）。</summary>
    public event Action<string> AttackImpact;

    /// <summary>連續揮砍每一刀開始揮出時觸發，參數是哪一把鐮刀（"Right"、"Left"、"Both"）。</summary>
    public event Action<string> SweepSwing;

    public enum TailWhipSide { Alternate, Left, Right }

    /// <summary>甩尾開始甩出時觸發，參數是尾巴往哪邊掃（"Left" / "Right"）。</summary>
    public event Action<string> TailSwing;

    /// <summary>衝刺開始蓄力（壓低身體）。</summary>
    public event Action DashCharge;

    /// <summary>衝刺開始：AI 從這裡開始把 Boss 往前推。</summary>
    public event Action DashStart;

    /// <summary>衝刺結束：AI 在這裡停止位移，接著是收尾上撈。</summary>
    public event Action DashEnd;

    /// <summary>目前是否在 DashStart 和 DashEnd 之間。</summary>
    public bool IsDashing { get; private set; }

    /// <summary>遠程攻擊開始聚能量時觸發；真正射出是 AttackImpact("Ranged")。</summary>
    public event Action RangedCharge;

    /// <summary>死亡動畫播完時觸發。</summary>
    public event Action DeathFinished;

    Animator animator;
    bool nextTailLeft = true;

    public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();
    public bool IsDead { get; private set; }

    /// <summary>遠程攻擊瞄準的點；沒指定就往正前方射。</summary>
    public Vector3? RangedTargetPoint { get; set; }

    void Awake()
    {
        animator = GetComponent<Animator>();
        animator.applyRootMotion = false;
    }

    public void SetMoveSpeed(float normalizedSpeed)
    {
        float speed = Mathf.Clamp01(normalizedSpeed);
        Animator.SetFloat(SpeedParam, speed);
        // 只有 Crawl 狀態吃這個倍率，攻擊和受擊永遠用原速
        Animator.SetFloat(CrawlRateParam, Mathf.Lerp(1f, maxCrawlPlaybackSpeed, speed));
    }

    public void PlaySlash() => Fire(SlashParam);
    public void PlayBite() => Fire(BiteParam);
    public void PlaySweepCombo() => Fire(SweepParam);
    public void PlayTailWhip(TailWhipSide side = TailWhipSide.Alternate)
    {
        if (side == TailWhipSide.Alternate)
        {
            side = nextTailLeft ? TailWhipSide.Left : TailWhipSide.Right;
            nextTailLeft = !nextTailLeft;
        }
        Fire(side == TailWhipSide.Left ? TailWhipLeftParam : TailWhipRightParam);
    }

    public void PlayDash() => Fire(DashParam);
    public void PlayPounce() => Fire(PounceParam);
    public void PlayRoar() => Fire(RoarParam);

    /// <summary>吼叫到最大聲的那一格（召喚蝙蝠的時機）。</summary>
    public event Action Roar;

    public void OnRoar() => Roar?.Invoke();

    /// <summary>直接指定爬行動畫的播放倍率（殘影圍殺高速繞圈用）；之後呼叫 SetMoveSpeed 會蓋回來。</summary>
    public void SetCrawlPlaybackRate(float rate) => Animator.SetFloat(CrawlRateParam, Mathf.Max(0.1f, rate));

    public void PlayRanged(Vector3? targetPoint = null)
    {
        if (targetPoint.HasValue) RangedTargetPoint = targetPoint;
        Fire(RangedParam);
    }
    public void PlayHit() => Fire(HitParam);

    public void PlayDeath()
    {
        if (IsDead) return;
        IsDead = true;
        OnDashEnd();
        Animator.SetFloat(SpeedParam, 0f);
        Animator.SetBool(DeadParam, true);
        Animator.SetTrigger(DieParam);
    }

    public void Revive()
    {
        IsDead = false;
        Animator.SetBool(DeadParam, false);
        Animator.ResetTrigger(DieParam);
        Animator.Play("Idle", 0, 0f);
    }

    void Fire(int trigger)
    {
        if (IsDead) return;
        if (trigger != DashParam) OnDashEnd(); // 衝到一半被其他動作打斷，也要通知停下
        Animator.SetTrigger(trigger);
    }

    // ── 動畫事件接收（名稱要和 Baker 寫進片段的事件一致） ──

    public void OnAttackImpact(string attackName) => AttackImpact?.Invoke(attackName);

    public void OnSweepSwing(string arm) => SweepSwing?.Invoke(arm);

    public void OnTailSwing(string side) => TailSwing?.Invoke(side);

    public void OnDashCharge() => DashCharge?.Invoke();

    /// <summary>撲擊離地那一格（目前只當作同步參考，跳躍由技能元件依時間控制）。</summary>
    public event Action PounceLeap;

    public void OnPounceLeap() => PounceLeap?.Invoke();

    public void OnDashStart()
    {
        IsDashing = true;
        DashStart?.Invoke();
    }

    public void OnDashEnd()
    {
        if (!IsDashing) return;
        IsDashing = false;
        DashEnd?.Invoke();
    }

    public void OnRangedCharge() => RangedCharge?.Invoke();

    public void OnDeathFinished() => DeathFinished?.Invoke();
}
