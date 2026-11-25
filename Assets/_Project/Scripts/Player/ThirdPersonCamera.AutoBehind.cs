using UnityEngine;

/// <summary>
/// 進攻、翻滾、跳躍時，鏡頭自動轉到角色背後（跟著角色面向的方向），不用一直自己甩鏡頭。
///   - 揮刀 / 反擊、翻滾中、跳起來到落地之前：鏡頭的水平角度平滑轉到角色正後方
///   - 玩家正在用滑鼠 / 右搖桿轉鏡頭時不搶：手動轉過之後 autoBehindManualHold 秒內不自動轉
///   - 瞄準（拉弓、技能瞄準）、游標沒鎖定（選單）時不動
/// 只改水平角度（yaw），上下角度照玩家自己調的。
/// </summary>
public partial class ThirdPersonCamera
{
    [Header("進攻 / 翻滾 / 跳躍時自動轉到背後")]
    [Tooltip("開著：揮刀、翻滾、跳躍時鏡頭自動轉到角色背後")]
    public bool autoBehindOnAction = true;
    [Tooltip("轉到背後的平滑時間（秒）：越小轉得越快")]
    public float autoBehindSmooth = 0.2f;
    [Tooltip("手動轉過鏡頭之後，幾秒內不自動轉（不跟玩家搶鏡頭）")]
    public float autoBehindManualHold = 0.6f;

    PlayerCombat autoBehindCombat;
    Transform autoBehindOwner;
    float autoYawVelocity, lastManualLook = -10f;
    bool jumpAirborne;

    /// <summary>HandleInput 之前記下 yaw，之後比對：有變 = 玩家手動轉了鏡頭</summary>
    void NoteManualLook(float yawBefore)
    {
        if (Mathf.Abs(Mathf.DeltaAngle(yawBefore, yaw)) > 0.05f) lastManualLook = Time.unscaledTime;
    }

    /// <summary>角色正在進攻 / 翻滾 / 跳躍時，鏡頭的水平角度慢慢轉到角色背後</summary>
    void AutoBehind()
    {
        if (!autoBehindOnAction || target == null) return;
        if (autoBehindOwner != target)
        {
            autoBehindOwner = target;
            autoBehindCombat = target.GetComponent<PlayerCombat>();
            jumpAirborne = false;
        }

        // 跳躍：往上跳離地面那一刻開始，到落地為止（從高處走下去不算）
        if (targetMotor != null)
        {
            if (targetMotor.IsGrounded) jumpAirborne = false;
            else if (targetMotor.VerticalVelocity > 0.5f) jumpAirborne = true;
        }
        bool attacking = autoBehindCombat != null && !autoBehindCombat.bowMode &&
                         (autoBehindCombat.IsAttacking || autoBehindCombat.IsShieldCountering);
        bool rolling = targetMotor != null && targetMotor.IsRolling;
        if (!(attacking || rolling || jumpAirborne)) { autoYawVelocity = 0f; return; }

        if (Time.unscaledTime - lastManualLook < autoBehindManualHold) { autoYawVelocity = 0f; return; }
        if (aimBlend > 0.05f || SkillAimCancellation.IsAiming || Cursor.lockState != CursorLockMode.Locked) return;
        if (Time.timeScale <= 0f || CalligraphyPaintingWorld.BlocksTether) return;

        float want = target.eulerAngles.y;
        yaw = Mathf.SmoothDampAngle(yaw, want, ref autoYawVelocity, autoBehindSmooth, Mathf.Infinity, Time.unscaledDeltaTime);
    }
}
