using System.Reflection;
using UnityEngine;

/// <summary>
/// 攻擊輸入緩衝：讓招式一個接一個，不會因為「按的時候還在做別的動作」而被吃掉（PlayerLoadout.Awake 自動掛上）。
///   - 連擊最後一刀收刀時、長槍組合技（破陣突刺 / 飛身貫地）、跳劈、盾滑、幻影突進、刀被牆彈回來的時候按攻擊：
///     先記著（buffer 秒內有效），那個動作一結束就馬上接新的一輪連擊
///   - 連擊中途按的照原本的規則接下一刀（PlayerCombat 自己會記），這裡不重複
///   - 翻滾中按的不記：「翻滾、攻擊、翻滾」的盾滑就是靠翻滾中按攻擊觸發的，記了會搶先出一刀
/// PlayerCombat 是隊友的，這裡不改它：只在它之前（執行順序 -5）看玩家按了攻擊時是不是在忙，動作結束時呼叫它的開始連擊（BeginAttack，用反射）。
/// 拿弓瞄準、蓄力重擊模式、受擊僵直、選單 / 對話 / 過場中不緩衝；還舉著盾的時候等放下再出。
/// </summary>
[DefaultExecutionOrder(-5)]
[DisallowMultipleComponent]
public sealed class AttackFlow : MonoBehaviour
{
    [Tooltip("按下攻擊之後，動作還沒結束的話記著多久（秒）")]
    public float buffer = 0.45f;

    static readonly MethodInfo BeginAttack = typeof(MeleeCombat).GetMethod("BeginAttack", BindingFlags.Instance | BindingFlags.NonPublic);

    PlayerCombat combat;
    ThirdPersonController motor;
    PlayerHealth health;
    SpearArts spear;
    LeapCleaveStrike leap;
    ShieldSlideStrike slide;
    PhantomStrike phantom;
    float pendingAt = -10f;
    int swingsAtPress;
    int swings;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        motor = GetComponent<ThirdPersonController>();
        health = GetComponent<PlayerHealth>();
        if (combat == null || BeginAttack == null) { enabled = false; return; }
        combat.OnSwing += _ => swings++;
    }

    /// <summary>玩家現在在做某個動作，按攻擊不會馬上出刀</summary>
    bool Busy()
    {
        if (spear == null) spear = GetComponent<SpearArts>();
        if (leap == null) leap = GetComponent<LeapCleaveStrike>();
        if (slide == null) slide = GetComponent<ShieldSlideStrike>();
        if (phantom == null) phantom = GetComponent<PhantomStrike>();
        return combat.IsAttacking || BladeRebound.Busy
               || (spear != null && spear.Playing) || (leap != null && leap.Playing) || (slide != null && slide.Playing)
               || (phantom != null && phantom.Busy);
    }

    /// <summary>這種時候不緩衝（不是在做攻擊動作，或是另一套操作）</summary>
    bool Blocked() =>
        health == null || health.IsDead || combat.bowMode || combat.IsAimingBow || combat.heavyAttackEnabled
        || (motor != null && motor.IsStunned) || GameUI.OverlayOpen || StoryDialogue.Active || VitalsHud.Hidden
        || Time.timeScale <= 0f || Cursor.lockState != CursorLockMode.Locked || SkillAimCancellation.IsAiming;

    void Update()
    {
        if (Blocked()) { pendingAt = -10f; return; }
        float now = Time.unscaledTime;
        // 在 PlayerCombat 之前看：按下攻擊的這一刻在忙，就先記著
        if (InputBindings.Pressed(GameAction.Attack) && Busy())
        {
            pendingAt = now;
            swingsAtPress = swings;
        }
        if (pendingAt < 0f) return;
        if (now - pendingAt > buffer) { pendingAt = -10f; return; }
        // 記著的那一下已經變成連擊的下一刀了（PlayerCombat 自己接上）：不用再出
        if (swings != swingsAtPress && combat.IsAttacking) { pendingAt = -10f; return; }
        if (Busy()) return;
        // 招式剛結束、還舉著盾（盾滑的護盾）或還沒把操作還給玩家：再等一下
        if (combat.ShieldUp || (motor != null && !motor.inputEnabled)) return;
        // 動作結束了：馬上接新的一輪連擊
        pendingAt = -10f;
        BeginAttack.Invoke(combat, null);
    }
}
