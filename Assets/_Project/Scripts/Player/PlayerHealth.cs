using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家血量：受傷、無敵時間、掉出場地死亡、自動重生，並用 OnGUI 畫一個簡單血條。
/// 每次倒下會扣 GameManager 的一條命，命用完就不再重生，改由 GameManager 顯示 Game Over。
/// 在維京村莊（有水的關卡）會自動加上 PlayerOxygen（氧氣條）。
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("受傷後的無敵時間 (秒)，避免同一招連續扣血")]
    public float invincibleTime = 0.1f;
    [Tooltip("受傷後的僵直時間 (秒)：不能移動、攻擊，正在揮的劍會被打斷")]
    public float hitStunTime = 0.1f;
    [Tooltip("低於這個高度就判定掉下去死亡")]
    public float killY = -8f;
    public float respawnDelay = 2.5f;
    [Tooltip("重生點，留空就用遊戲開始時的位置（碰到 Checkpoint 會自動改掉）")]
    public Transform respawnPoint;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>腳底的世界座標高度（用來判斷有沒有跳過衝擊波 / 光束）</summary>
    public float FeetY => transform.position.y + cc.center.y - cc.height * 0.5f;

    /// <summary>
    /// 找到場景裡真正的玩家（會移動的那個物件）。
    /// 不要用 FindWithTag("Player").transform.root：PlayerRig 的根物件不會跟著角色移動。
    /// </summary>
    public static PlayerHealth Find() => FindAnyObjectByType<PlayerHealth>();

    public event System.Action OnDied;
    public event System.Action OnRespawned;
    /// <summary>受到傷害（參數是傷害量），模型動畫用</summary>
    public event System.Action<float> OnDamaged;
    /// <summary>受擊姿勢決定好了（參數：姿勢、擊退的水平方向）。PlayerHitPose 用它擺姿勢</summary>
    public event System.Action<PlayerHitReaction, Vector3> OnHitReaction;

    [Header("受擊姿勢")]
    [Tooltip("Auto 時：擊退往上的力道 >= 這個值就當成「被打飛」")]
    public float autoKnockFlyUpForce = 6f;
    [Tooltip("被打飛：最少往上飛多快 (m/s)、水平飛多快的倍率")]
    public float knockFlyUpSpeed = 7.5f;
    public float knockFlyHorizontalScale = 0.9f;
    [Tooltip("被打飛落地後躺在地上多久、爬起來多久 (秒)；這段時間無敵")]
    public float knockDownLieTime = 0.55f;
    public float knockDownGetUpTime = 0.6f;
    [Tooltip("吸到毒氣：摀住口鼻狂抖多久 (秒)，期間不能動")]
    public float poisonGasStunTime = 1.6f;
    [Tooltip("防禦姿勢：定在原地多久 (秒)。比骷髏接力的間隔短一點，玩家才有空檔翻滾逃出去")]
    public float guardBraceStunTime = 0.35f;

    /// <summary>正在被打飛（空中＋躺地＋爬起來）</summary>
    public bool IsKnockedDown { get; private set; }
    /// <summary>上一次受擊的姿勢</summary>
    public PlayerHitReaction LastReaction { get; private set; } = PlayerHitReaction.Standard;
    Coroutine knockDownRoutine;

    CharacterController cc;
    ThirdPersonController controller;
    PlayerCombat combat;
    PlayerPerks perks;
    Vector3 spawnPos;
    Quaternion spawnRot;
    float lastHitTime = float.NegativeInfinity;
    float lastFlashTime = float.NegativeInfinity;
    float dodgeInvulnerableUntil = float.NegativeInfinity;
    readonly System.Collections.Generic.HashSet<Object> skillInvulnerability = new();
    readonly System.Collections.Generic.HashSet<Object> practiceProtection = new();
    public void SetPracticeProtection(Object owner, bool active)
    {
        if (active && owner) practiceProtection.Add(owner);
        else practiceProtection.Remove(owner);
    }
    public void SetSkillInvulnerability(Object owner, bool active)
    {
        if (active && owner) skillInvulnerability.Add(owner);
        else skillInvulnerability.Remove(owner);
    }
    string deathReason = "";

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        controller = GetComponent<ThirdPersonController>();
        Health = maxHealth;
        spawnPos = transform.position;
        spawnRot = transform.rotation;

        // 還沒加攻擊元件的話自動加上（想調整數值請手動 Add Component → PlayerCombat）
        combat = GetComponent<PlayerCombat>();
        if (combat == null)
        {
            combat = gameObject.AddComponent<PlayerCombat>();
            Debug.Log("[Player] 沒有 PlayerCombat，已自動加上。想調整攻擊數值請在 Player 上手動 Add Component → PlayerCombat。");
        }

        // 消耗品：Q 喝治癒藥水、G 喝精力藥水
        if (GetComponent<PlayerItems>() == null) gameObject.AddComponent<PlayerItems>();

        // 精力條：衝刺 / 翻滾 / 跳躍消耗，站著不動時回復
        if (GetComponent<PlayerStamina>() == null) gameObject.AddComponent<PlayerStamina>();
        if (GetComponent<PlayerSkillAvailability>() == null) gameObject.AddComponent<PlayerSkillAvailability>();

        // 有水的關卡：潛到水裡要消耗氧氣
        if (PlayerOxygen.SceneHasWater(gameObject.scene.name) && GetComponent<PlayerOxygen>() == null)
            gameObject.AddComponent<PlayerOxygen>();
    }

    void Update()
    {
        if (!IsDead && transform.position.y < killY) Die("掉下去了");
    }

    /// <summary>造成傷害；回傳 true 代表這次真的有打到（不在無敵時間內）</summary>
    /// <param name="ignoreInvincibility">true = 持續傷害（例如光束），不受也不觸發受傷後的無敵時間</param>
    /// <param name="deathReason">這一下把玩家打死時，死亡畫面顯示的原因</param>
    /// <summary>上一次「一般受擊」（會播受擊動畫、會僵直）的時間；持續傷害、被打飛、毒氣、架擋不算</summary>
    public float StandardHitTime { get; private set; } = -99f;

    public bool TakeDamage(float amount, Vector3 knockback = default, bool ignoreInvincibility = false,
                           string deathReason = "被打倒了", bool applyHitStun = true,
                           PlayerHitReaction reaction = PlayerHitReaction.Auto)
    {
        // Painting attacks use the explicit scoped hit gate; outside enemies cannot hit through it.
        if(CalligraphyPaintingWorld.BlocksTether && !CalligraphyPlayerHitGate.DeliveringPaintingDamage)return false;
        if (IsDead) return false;
        skillInvulnerability.RemoveWhere(owner => !owner);
        if (skillInvulnerability.Count > 0) return false;
        // 翻滾是完整無敵：連原本可忽略受傷無敵時間的持續傷害也不能穿透。
        if ((controller != null && controller.IsRolling) || Time.unscaledTime < dodgeInvulnerableUntil) return false;
        if (!ignoreInvincibility)
        {
            if (Time.time - lastHitTime < invincibleTime) return false;
            lastHitTime = Time.time;
        }
        // 舉盾（右鍵）時正面的攻擊會被盾牌擋下：不扣血、只被往後推一點（連續擋到破防那一下才會扣血）
        if (combat != null && amount > 0f)
        {
            amount = combat.ApplyShieldBlock(amount, knockback);
            if (amount <= 0f)
            {
                controller.AddImpulse(knockback * 0.35f);
                return true;
            }
        }
        // 能力植物：閃避 / 護盾，整下傷害不算
        if (perks == null) perks = GetComponent<PlayerPerks>();
        if (perks != null && perks.TryNegateHit()) return false;
        if (perks != null) amount *= perks.IncomingDamageScale;
        lastFlashTime = Time.time;
        practiceProtection.RemoveWhere(owner => !owner);
        Health = Mathf.Max(practiceProtection.Count > 0 ? 1f : 0f, Health - amount);
        bool stunHit = applyHitStun && !ignoreInvincibility && amount > 0f;
        PlayerHitReaction resolved = ResolveReaction(reaction, knockback, stunHit);
        // 一般受擊（播 Hit / hit_to_xxx、會僵直的那種）：PlayerVisualAnimator 看到就把移動鎖到受擊動畫播完
        if (stunHit && (resolved == PlayerHitReaction.Standard || resolved == PlayerHitReaction.Auto)) StandardHitTime = Time.time;
        Vector3 flatKnock = new Vector3(knockback.x, 0f, knockback.z);
        switch (resolved)
        {
            case PlayerHitReaction.KnockedFlying:
                // 面向攻擊方、往後拋飛（姿勢由 PlayerHitPose 擺：蜷縮 → 張開 → 仰躺）
                if (flatKnock.sqrMagnitude > 0.0001f) controller.FaceDirection(-flatKnock);
                controller.Launch(flatKnock * knockFlyHorizontalScale + Vector3.up * Mathf.Max(knockFlyUpSpeed, knockback.y));
                break;
            case PlayerHitReaction.PoisonGas:
            case PlayerHitReaction.GuardBrace:
                controller.StopHorizontalMotion(); // 定在原地，不會被推著滑走
                break;
            default:
                controller.AddImpulse(knockback);
                break;
        }
        // 受擊僵直（持續傷害例如光束不算，不然會一直被定住）
        if (stunHit)
        {
            float stun = resolved switch
            {
                PlayerHitReaction.PoisonGas => poisonGasStunTime,
                PlayerHitReaction.GuardBrace => guardBraceStunTime,
                PlayerHitReaction.KnockedFlying => 0.5f,
                _ => hitStunTime,
            };
            if (stun > 0f) controller.Stun(stun);
            combat.CancelAttack();
        }
        if (resolved != PlayerHitReaction.Auto)
        {
            LastReaction = resolved;
            if (resolved == PlayerHitReaction.KnockedFlying && Health > 0f)
            {
                if (knockDownRoutine != null) StopCoroutine(knockDownRoutine);
                knockDownRoutine = StartCoroutine(KnockDownRoutine());
            }
            if (Health > 0f) OnHitReaction?.Invoke(resolved, flatKnock.sqrMagnitude > 0.0001f ? flatKnock.normalized : -transform.forward);
        }
        ThirdPersonCamera.ShakeMain(0.15f, 0.2f);
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.8f, 0.8f, 0.08f, 0.3f);
        if (Health <= 0f) Die(deathReason);
        else OnDamaged?.Invoke(amount);
        return true;
    }

    /// <summary>
    /// 決定這一下用哪種受擊姿勢。回傳 Auto = 不換姿勢（持續傷害、不會僵直的攻擊），照舊只推一下。
    /// </summary>
    PlayerHitReaction ResolveReaction(PlayerHitReaction requested, Vector3 knockback, bool stunHit)
    {
        if (!stunHit) return PlayerHitReaction.Auto;
        if (IsKnockedDown) return PlayerHitReaction.Auto; // 飛在空中又被打到：不重新起飛，只扣血
        if (requested != PlayerHitReaction.Auto) return requested;
        return knockback.y >= autoKnockFlyUpForce ? PlayerHitReaction.KnockedFlying : PlayerHitReaction.Standard;
    }

    /// <summary>被打飛：空中一直僵直，落地後躺著＋爬起來的期間無敵、不能動</summary>
    IEnumerator KnockDownRoutine()
    {
        IsKnockedDown = true;
        float t = 0f;
        // 起飛那幾幀還算在地上：至少等 0.15 秒再開始判斷落地
        while (!IsDead && (t < 0.15f || !controller.IsGrounded) && t < 3f)
        {
            controller.Stun(0.1f);
            t += Time.deltaTime;
            yield return null;
        }
        if (!IsDead)
        {
            controller.StopHorizontalMotion();
            SetSkillInvulnerability(this, true);
            controller.Stun(knockDownLieTime + knockDownGetUpTime);
            ThirdPersonCamera.ShakeMain(0.12f, 0.18f);
            GameAudio.Play("SFX/rock_hit", transform.position, 0.35f, 0.7f, 0.05f, 0.4f, 30f);
            yield return new WaitForSeconds(knockDownLieTime + knockDownGetUpTime);
        }
        SetSkillInvulnerability(this, false);
        IsKnockedDown = false;
        knockDownRoutine = null;
    }

    /// <summary>直接死亡（不受無敵、翻滾、盾牌影響），例如掉出步道、墜入山谷。走原本的死亡與重生流程。</summary>
    public void Kill(string reason) { if (!IsDead) Die(reason); }

    public void GrantDodgeInvulnerability(float seconds)
    {
        dodgeInvulnerableUntil = Mathf.Max(dodgeInvulnerableUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
    }

    /// <summary>補滿血（檢查點使用）</summary>
    public void HealFull()
    {
        if (!IsDead) Health = maxHealth;
    }

    /// <summary>直接設定最大生命和目前血量（換關卡 / 讀檔時由 GameManager 套用）</summary>
    public void SetHealth(float max, float current)
    {
        maxHealth = Mathf.Max(1f, max);
        Health = Mathf.Clamp(current, 1f, maxHealth);
        IsDead = false;
    }

    /// <summary>提高最大生命，並補上同樣的血量（能力植物使用）</summary>
    public void IncreaseMaxHealth(float amount)
    {
        maxHealth += amount;
        if (!IsDead) Health = Mathf.Min(maxHealth, Health + amount);
    }

    /// <summary>回復指定血量（篝火互動使用）</summary>
    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    void Die(string reason)
    {
        if (IsDead) return;
        IsDead = true;
        Health = 0f;
        deathReason = reason;
        if (knockDownRoutine != null) { StopCoroutine(knockDownRoutine); knockDownRoutine = null; }
        IsKnockedDown = false;
        SetSkillInvulnerability(this, false);
        controller.inputEnabled = false;
        OnDied?.Invoke();

        // 扣一條命；命用完就不重生，GameManager 會在 respawnDelay 秒後顯示 Game Over
        var gm = GameManager.Instance;
        if (gm != null && !gm.OnPlayerDied(reason, respawnDelay)) return;
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        Vector3 pos = respawnPoint != null ? respawnPoint.position : spawnPos;
        Quaternion rot = respawnPoint != null ? respawnPoint.rotation : spawnRot;

        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.SetPositionAndRotation(pos, rot);
        cc.enabled = true;

        controller.ResetMotion();
        controller.inputEnabled = true;
        Health = maxHealth;
        IsDead = false;
        lastHitTime = Time.time; // 重生後給一點無敵時間
        OnRespawned?.Invoke();
    }

    /// <summary>VitalsHud（日式的血量 / 精力條）在畫的時候設成 true：這裡和 PlayerStamina 就不畫原本的長方形血條、精力條</summary>
    public static bool SimpleBarHidden;

    // ---------- 簡易 HUD（之後可以換成正式 UI） ----------
    void OnGUI()
    {
        const float w = 260f, h = 18f, x = 20f, y = 20f;
        if (!SimpleBarHidden)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.15f, 0.15f);
            GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 6, y, w, h), $"HP {Mathf.CeilToInt(Health)} / {maxHealth}");
        }

        var gm = GameManager.Instance;
        if (gm != null && !SimpleBarHidden) // VitalsHud 用血條旁的血滴表示剩下幾條命
        {
            GUI.color = new Color(1f, 0.55f, 0.55f);
            GUI.Label(new Rect(x + w + 12, y, 200, h + 4), $"生命 × {gm.Lives}");
            GUI.color = Color.white;
        }

        // 受傷時畫面閃紅
        float flash = 1f - Mathf.Clamp01((Time.time - lastFlashTime) / 0.3f);
        if (flash > 0f && !IsDead)
        {
            GUI.color = new Color(1f, 0f, 0f, 0.25f * flash);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        if (IsDead && (gm == null || !gm.IsOverlayOpen))
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), $"你死了（{deathReason}）", style);
            if (gm != null && gm.Lives > 0)
            {
                style.fontSize = 24;
                style.fontStyle = FontStyle.Normal;
                GUI.Label(new Rect(0, 70, Screen.width, Screen.height), $"剩餘生命 {gm.Lives}，即將在檢查點復活…", style);
            }
        }
        GUI.color = Color.white;
    }
}
