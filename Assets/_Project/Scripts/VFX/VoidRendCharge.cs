using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 技能 1（虛空連斬）的啟動方式：不再暫停時間，改成像拉弓一樣「按住 1 蓄力」。
///   按住 1：拔刀、右手把刀拉到頭的右側，刀尖一直指著畫面中間對準的目標（左手往前指），
///           鏡頭平順地拉近到肩膀後面；可以移動但比較慢（前後左右小碎步，Animator 的 SwordCharge 狀態）
///   放開 1：取消，鏡頭平順地拉回原本的樣子
///   蓄滿之後左鍵：對準的目標開始原本的技能（蹲低 → 爆衝 → 轉刀 → 22 段突進斬 → 收招）
/// 目標 = 畫面中間（準心）附近的敵人；滑鼠一樣是鎖住的，轉鏡頭就是瞄準。
/// 技能 2～4 還是原本的時間暫停選目標（之後會刪掉，先不動）。
/// 沒有 ThirdPersonController 的舊場景也用原本的時間暫停。
/// 動畫：Art/PlayerCatWarrior/charge_pose.py 在 Blender 手擺（CW_SwordAim + CW_SwordStepF/B/L/R），
///       選單 GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫 會在 Animator 加上 SwordCharge。
/// </summary>
public sealed partial class VoidRendSkillDemo
{
    [Header("Skill 1 / 按住 1 蓄力（放開取消，蓄滿後左鍵選目標）")]
    [Tooltip("蓄滿要幾秒（蓄滿才能按左鍵出招）")]
    [Min(.05f)] public float chargeSeconds = .9f;
    [Tooltip("蓄力時移動速度的倍率（0.09 × 走路 4.5 m/s ≈ 0.4 m/s，剛好跟小碎步動畫的腳步一樣快，不會滑）")]
    [Range(.02f, 1f)] public float chargeMoveSpeedScale = .09f;
    [Tooltip("蓄力時鏡頭：離角色多遠、往右偏多少（越肩）、注視點高度、視角")]
    public float chargeCameraDistance = 3.4f, chargeCameraShoulder = .55f, chargeCameraHeight = .6f, chargeCameraFov = 50f;
    [Tooltip("鏡頭拉近 / 拉回要幾秒")]
    [Range(.05f, 2f)] public float chargeCameraBlendSeconds = .4f;
    [Tooltip("角色轉向鏡頭方向的速度（度 / 秒）")]
    public float chargeTurnSpeed = 600f;
    [Tooltip("準心附近多少像素內的敵人算對準")]
    public float chargeAimPixels = 170f;
    [Tooltip("小碎步動畫一秒走幾公尺（往前 / 往旁邊）：用來調動畫播放速度，腳才不會在地上滑（charge_pose.py 改步幅 / 格數的話要跟著改）")]
    public float chargeStepForwardMps = .40f, chargeStepSideMps = .24f;
    [Tooltip("蓄力時刀身的光點顏色")]
    public Color chargeColor = new Color(.45f, .9f, 1f, 1f);

    bool charging, chargeFull, chargeDriverWasEnabled, chargeAimOverride;
    float chargeStart, nextChargeSpark, chargeAnimSpeed = 1f;
    int chargeSpeedLease;
    TargetSelection chargeSelection;
    // 蓄力時暫時換掉的鏡頭瞄準設定（等鏡頭完全拉回來才還原，不然拉回的途中會跳）
    float savedAimDistance, savedAimShoulder, savedAimFov, savedAimBlendSpeed;
    Vector3 savedAimOffset;

    /// <summary>正在按住 1 蓄力</summary>
    public bool IsCharging => charging;
    /// <summary>蓄力進度 0～1</summary>
    public float ChargeProgress => charging ? Mathf.Clamp01((Time.time - chargeStart) / chargeSeconds) : 0f;

    bool CanUseChargeStart => practiceController != null && casterAnimator != null && casterAnimator.isHuman;

    void BeginCharge()
    {
        if (!CanUseChargeStart) { BeginTargeting(PreviewSkill.VoidRend); return; } // 舊場景：原本的時間暫停
        if (playing || charging || !SkillAimCancellation.Register(CancelCharge)) return;
        charging = true; chargeFull = false;
        chargeStart = Time.time; nextChargeSpark = 0f;
        chargeSelection = default;
        Stage = "Charging VoidRend";

        // 移動：可以走但變慢；不能跳 / 翻滾 / 普攻（左鍵留給出招）；面向鏡頭方向
        practiceController.LockActions();
        chargeSpeedLease = practiceController.AddSpeedMultiplierLease(chargeMoveSpeedScale);
        practiceController.lockRotation = true;

        // 動畫：PlayerVisualAnimator 會一直切走路 / 待機，蓄力時先關掉，由這裡播 SwordCharge
        if (practiceAnimatorDriver != null) { chargeDriverWasEnabled = practiceAnimatorDriver.enabled; practiceAnimatorDriver.enabled = false; }
        if (casterAnimator.HasState(0, Animator.StringToHash("SwordCharge")))
            casterAnimator.CrossFadeInFixedTime("SwordCharge", .18f, 0, 0f);
        if (weaponPresenter) weaponPresenter.BeginSkillDraw();

        // 鏡頭：用 ThirdPersonCamera 的瞄準（平順地拉近到肩膀後面）
        if (practiceOrbitCamera != null)
        {
            if (!chargeAimOverride)
            {
                savedAimDistance = practiceOrbitCamera.aimDistance; savedAimShoulder = practiceOrbitCamera.aimShoulder;
                savedAimFov = practiceOrbitCamera.aimFov; savedAimOffset = practiceOrbitCamera.aimOffset;
                savedAimBlendSpeed = practiceOrbitCamera.aimBlendSpeed;
                chargeAimOverride = true;
            }
            practiceOrbitCamera.aimDistance = chargeCameraDistance;
            practiceOrbitCamera.aimShoulder = chargeCameraShoulder;
            practiceOrbitCamera.aimOffset = Vector3.up * chargeCameraHeight;
            practiceOrbitCamera.aimFov = chargeCameraFov;
            practiceOrbitCamera.aimBlendSpeed = 1f / chargeCameraBlendSeconds;
            practiceOrbitCamera.SetAiming(true, caster);
        }
        if (weapon != null && finisherWeaponTip != null) SpawnParticles(finisherWeaponTip.position, 10, .4f, 1.4f, .01f, .03f, chargeColor);
    }

    void UpdateCharge()
    {
        if (!PlayerSkillAvailability.TryUse() || GameUI.OverlayOpen || !practiceController.isActiveAndEnabled || practiceController.IsStunned)
        { CancelCharge(); return; }
        if (Keyboard.current.escapeKey.wasPressedThisFrame) { SkillAimCancellation.CancelForEscape(); return; }
        if (!Keyboard.current.digit1Key.isPressed) { CancelCharge(); return; } // 放開 1 = 取消

        float p = ChargeProgress;
        if (p >= 1f && !chargeFull)
        {
            chargeFull = true;
            Vector3 tip = ChargeTipPosition();
            SpawnRing(tip, .05f, .7f, .03f, .35f, pale);
            SpawnParticles(tip, 24, .45f, 2.6f, .012f, .04f, pale);
        }

        // 面向鏡頭的方向（像拉弓）：刀尖跟著準心
        if (skillCamera != null)
        {
            Vector3 f = Vector3.ProjectOnPlane(skillCamera.transform.forward, Vector3.up);
            if (f.sqrMagnitude > .001f)
                caster.rotation = Quaternion.RotateTowards(caster.rotation, Quaternion.LookRotation(f), chargeTurnSpeed * Time.deltaTime);
        }

        // 小碎步：角色自己座標的移動速度（-1～1）→ SwordCharge 的方向；動畫播放速度跟著實際速度，腳不會滑
        Vector3 v = caster.InverseTransformDirection(practiceController.HorizontalVelocity);
        float full = Mathf.Max(.01f, practiceController.walkSpeed * chargeMoveSpeedScale);
        Vector2 dir = new Vector2(v.x, v.z) / full;
        if (dir.sqrMagnitude > 1f) dir.Normalize();
        casterAnimator.SetFloat("ChargeX", dir.x, .08f, Time.deltaTime);
        casterAnimator.SetFloat("ChargeY", dir.y, .08f, Time.deltaTime);
        float speed = new Vector2(v.x, v.z).magnitude;
        float side = Mathf.Abs(v.x) / Mathf.Max(.01f, Mathf.Abs(v.x) + Mathf.Abs(v.z));
        float wantAnim = speed > .15f ? Mathf.Clamp(speed / Mathf.Lerp(chargeStepForwardMps, chargeStepSideMps, side), .5f, 2f) : 1f;
        chargeAnimSpeed = Mathf.MoveTowards(chargeAnimSpeed, wantAnim, Time.deltaTime * 4f);
        casterAnimator.speed = chargeAnimSpeed;

        // 準心（畫面中間）附近的目標
        chargeSelection = skillCamera != null ? FindTargetAt(new Vector2(Screen.width * .5f, Screen.height * .5f), chargeAimPixels) : default;
        hoveredSelection = chargeSelection;
        hoveredTarget = chargeSelection.marker;
        UpdateTargetingReticle(chargeSelection);

        // 刀身的光點：越蓄越多
        if (Time.time >= nextChargeSpark)
        {
            nextChargeSpark = Time.time + Mathf.Lerp(.12f, .04f, p);
            Vector3 tip = ChargeTipPosition();
            SpawnParticles(tip + Random.insideUnitSphere * .12f, chargeFull ? 3 : 1 + Mathf.RoundToInt(p * 2f), .35f, .5f + p,
                .008f, .018f + .02f * p, Color.Lerp(chargeColor, pale, p));
        }

        // 蓄滿 + 有對準的目標：左鍵出招
        if (chargeFull && chargeSelection.IsValid && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            LaunchFromCharge();
    }

    /// <summary>LateUpdate（Animator 算完之後）：刀尖指著目標（沒有目標就指著準心的方向）</summary>
    void ApplyChargeAim()
    {
        if (!charging || weapon == null || rightHand == null) return;
        Vector3 aimPoint;
        if (chargeSelection.IsValid) aimPoint = chargeSelection.WorldPoint;
        else if (skillCamera != null) aimPoint = skillCamera.transform.position + skillCamera.transform.forward * 30f;
        else aimPoint = rightHand.position + caster.forward * 10f;
        Vector3 dir = aimPoint - rightHand.position;
        // 刀不要指到身體後面 / 太下面（目標在背後時先指前方，等角色轉過去）
        Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
        if (Vector3.Dot(flat, caster.forward) < 0f) dir = caster.forward + Vector3.up * .2f;
        float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Time.time - chargeStart) / .25f)); // 舉刀的時候慢慢轉過去
        Vector3 rest = (caster.forward + Vector3.up * .25f).normalized;
        AlignWeaponBlade(Vector3.Slerp(rest, dir.normalized, w), Vector3.up);
    }

    Vector3 ChargeTipPosition()
    {
        if (finisherWeaponTip != null) return finisherWeaponTip.position;
        if (weapon != null) return weapon.position + weapon.TransformDirection(weaponBladeLocalDirection) * 1.1f;
        return caster.position + Vector3.up;
    }

    /// <summary>蓄力結束（取消或出招都會呼叫）：還原移動、動畫、鏡頭</summary>
    void EndChargeState(bool launching)
    {
        if (!charging) return;
        charging = false;
        SkillAimCancellation.Release(CancelCharge);
        if (practiceController != null)
        {
            practiceController.UnlockActions();
            practiceController.RemoveSpeedMultiplierLease(chargeSpeedLease);
            practiceController.lockRotation = false;
        }
        chargeSpeedLease = 0;
        if (casterAnimator != null)
        {
            casterAnimator.speed = 1f; chargeAnimSpeed = 1f;
            if (!launching) casterAnimator.CrossFadeInFixedTime("Idle", .2f, 0, 0f);
        }
        if (practiceAnimatorDriver != null) practiceAnimatorDriver.enabled = chargeDriverWasEnabled;
        if (practiceOrbitCamera != null) practiceOrbitCamera.SetAiming(false, caster); // 鏡頭平順地拉回
        hoveredTarget = null; hoveredSelection = default;
        UpdateTargetingReticle(default);
    }

    void CancelCharge()
    {
        if (!charging) return;
        EndChargeState(false);
        if (weaponPresenter) weaponPresenter.EndSkillDraw(true);
        Stage = "Ready";
    }

    void LaunchFromCharge()
    {
        TargetSelection selection = chargeSelection;
        EndChargeState(true);
        calligraphyCast = CalligraphyAttackContext.NewCast();
        calligraphySkill = (int)PreviewSkill.VoidRend;
        calligraphyHit = 0;
        lockedSelection = selection;
        target = selection.marker.transform;
        PrepareSkillOrigin();
        SuspendPracticeControlsForSkill();
        StartCoroutine(GuardedSequence(PlaySequence()));
    }

    /// <summary>每幀：鏡頭完全拉回來之後才把瞄準設定還原（弓箭模式用的是原本的設定）</summary>
    void UpdateChargeCameraRestore()
    {
        if (!chargeAimOverride || charging || practiceOrbitCamera == null) return;
        if (practiceOrbitCamera.isActiveAndEnabled && practiceOrbitCamera.AimBlend > .001f) return;
        if (playing) return; // 技能中鏡頭是關掉的，等技能結束、鏡頭拉回後再還原
        practiceOrbitCamera.aimDistance = savedAimDistance; practiceOrbitCamera.aimShoulder = savedAimShoulder;
        practiceOrbitCamera.aimFov = savedAimFov; practiceOrbitCamera.aimOffset = savedAimOffset;
        practiceOrbitCamera.aimBlendSpeed = savedAimBlendSpeed;
        chargeAimOverride = false;
    }

    void DrawChargeUI()
    {
        if (!charging) return;
        float p = ChargeProgress;
        Vector2 c = new Vector2(Screen.width * .5f, Screen.height * .5f);
        Color old = GUI.color;
        // 準心
        GUI.color = chargeSelection.IsValid ? (chargeFull ? new Color(1f, .35f, .3f, .95f) : new Color(1f, .8f, .5f, .9f)) : new Color(1f, 1f, 1f, .7f);
        GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x - 14f, c.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x + 6f, c.y - 1f, 8f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x - 1f, c.y - 14f, 2f, 8f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(c.x - 1f, c.y + 6f, 2f, 8f), Texture2D.whiteTexture);
        // 蓄力條
        Rect bar = new Rect(c.x - 60f, c.y + 26f, 120f, 5f);
        GUI.color = new Color(0f, 0f, 0f, .45f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = chargeFull ? pale : chargeColor;
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * p, bar.height), Texture2D.whiteTexture);
        GUI.color = old;
        GUIStyle hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 15, normal = { textColor = new Color(.85f, .95f, 1f, .9f) } };
        string text = !chargeFull ? "蓄力中…（放開 1 取消）" : chargeSelection.IsValid ? "左鍵：虛空連斬" : "把準心對準目標";
        GUI.Label(new Rect(c.x - 150f, c.y + 34f, 300f, 24f), text, hint);
    }
}
