using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家的攻擊：
/// - 滑鼠左鍵：近戰揮劍連擊（三段連擊），攻擊方向 = 鏡頭朝向
/// - 滑鼠右鍵（按一下）：舉盾衝撞（盾擊）：往前頂一步，前方的敵人受到傷害並被大力撞開
/// - 滑鼠右鍵（按住）：舉盾格擋。正面的攻擊完全擋下，但連續擋第 3 下會破防（那一下照樣扣血、盾牌放下一會兒）
/// - 在船上（bowMode）：右鍵按住瞄準、左鍵射箭
/// </summary>
public class PlayerCombat : MeleeCombat
{
    [Header("測試與倍率")]
    [Tooltip("玩家攻擊力倍率")]
    public float damageMultiplier = 1f;

    [Header("盾擊（右鍵，在陸地上）")]
    public float shieldBashDamage = 8f;
    [Tooltip("把敵人撞開的力道")]
    public float shieldBashKnockback = 10f;
    [Tooltip("舉起盾牌到撞出去的時間 (秒)")]
    public float shieldBashWindup = 0.22f;
    [Tooltip("有撞擊判定的時間 (秒)")]
    public float shieldBashActive = 0.15f;
    [Tooltip("撞完之後收盾的時間 (秒)")]
    public float shieldBashRecovery = 0.45f;
    [Tooltip("兩次盾擊之間的冷卻 (秒)")]
    public float shieldBashCooldown = 0.8f;
    [Tooltip("盾擊時往前衝的力道")]
    public float shieldBashLunge = 7f;
    [Tooltip("判定球離角色中心往前多遠 / 半徑")]
    public float shieldBashReach = 1.1f, shieldBashRadius = 1.3f;

    [Header("格擋（右鍵按住，在陸地上）")]
    [Tooltip("連續擋幾下會破防：第幾下的攻擊會穿過盾牌扣血（3 = 擋兩下，第三下扣血）")]
    public int blockHitsBeforeBreak = 3;
    [Tooltip("舉盾時的移動速度倍率")]
    [Range(0f, 1f)] public float blockMoveMultiplier = 0.35f;
    [Tooltip("破防之後多久不能再舉盾 (秒)")]
    public float guardBreakTime = 0.8f;
    [Tooltip("攻擊要從正面多少範圍內來才擋得住（和面向的夾角餘弦，0.3 ≈ 前方 145 度）")]
    [Range(-1f, 1f)] public float blockFrontDot = 0.3f;

    /// <summary>正在盾擊（舉著盾牌往前頂）</summary>
    public bool IsShieldBashing { get; private set; }
    /// <summary>正在按住右鍵舉盾格擋</summary>
    public bool IsBlocking { get; private set; }
    /// <summary>盾牌舉著（格擋或盾擊中）：正面攻擊會被擋下</summary>
    public bool ShieldUp => IsBlocking || IsShieldBashing;
    /// <summary>這一輪連續擋了幾下（放下盾牌或破防就歸零）</summary>
    public int BlockStreak { get; private set; }
    /// <summary>盾擊開始時通知外面（模型動畫用）</summary>
    public event System.Action OnShieldBash;
    /// <summary>開始 / 停止格擋</summary>
    public event System.Action<bool> OnBlockChanged;
    /// <summary>用盾牌擋下攻擊（參數 true = 這一下破防了）</summary>
    public event System.Action<bool> OnBlockedHit;
    float nextBashTime, guardBrokenUntil = float.NegativeInfinity, lastBlockTime = float.NegativeInfinity;
    bool lastBlockBroke;

    [Header("弓箭（在船上，VikingBoat 登船時打開）")]
    [Tooltip("打開後：右鍵按住瞄準（鏡頭拉近放大），左鍵射箭；不能揮劍")]
    public bool bowMode;
    public float arrowSpeed = 70f;
    [Tooltip("兩支箭之間的冷卻 (秒)")]
    public float arrowCooldown = 0.55f;
    [Tooltip("沒有瞄準直接射時，箭的散佈角度（度）")]
    public float hipFireSpread = 2.5f;

    /// <summary>正在按住右鍵瞄準</summary>
    public bool IsAimingBow { get; private set; }
    /// <summary>瞄準中、或剛射完箭：角色面向由弓箭控制（VikingBoat 不要把角色轉回船頭）</summary>
    public bool ControlsFacing => IsAttacking || ShieldUp || (bowMode && (IsAimingBow || Time.time < faceAimUntil));
    public event System.Action<bool> OnBowAim;   // true = 開始瞄準，false = 放開
    public event System.Action OnBowShot;

    float nextArrowTime, faceAimUntil;
    float hitMarkerTime = float.NegativeInfinity;
    bool hitMarkerValid;

    PlayerHealth health;
    bool attackQueued;

    protected override void Awake()
    {
        base.Awake();
        health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        if (bowMode)
        {
            UpdateBow();
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;

        bool leftClick = false, rightClick = false, rightHeld = false;
        var mouse = Mouse.current;
        if (mouse != null)
        {
            leftClick = mouse.leftButton.wasPressedThisFrame;
            rightClick = mouse.rightButton.wasPressedThisFrame;
            rightHeld = mouse.rightButton.isPressed;
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        else
        {
            leftClick = Input.GetMouseButtonDown(0);
            rightClick = Input.GetMouseButtonDown(1);
            rightHeld = Input.GetMouseButton(1);
        }
#endif

        // 受擊僵直中不能攻擊 / 舉盾
        if (controller.IsStunned)
        {
            attackQueued = false;
            if (IsBlocking) SetBlocking(false, !rightHeld);
            return;
        }

        // 右鍵按一下：舉盾衝撞（揮劍連擊中、破防中不能用）
        bool guardBroken = Time.time < guardBrokenUntil;
        if (rightClick && !IsAttacking && !IsShieldBashing && !guardBroken && Time.time >= nextBashTime)
        {
            if (IsBlocking) SetBlocking(false, false);
            StartCoroutine(ShieldBash());
            return;
        }

        // 右鍵按住：舉盾格擋（盾擊結束時還按著就接著舉著）
        bool wantBlock = rightHeld && !IsAttacking && !IsShieldBashing && !guardBroken;
        if (wantBlock != IsBlocking) SetBlocking(wantBlock, !rightHeld);
        if (IsBlocking) controller.FaceDirection(GetAimDirection());

        // 左鍵：近戰連擊（攻擊中再按 = 接下一段）；舉盾時不能揮劍
        if (leftClick && !ShieldUp)
        {
            if (!BeginAttack()) attackQueued = true;
        }
    }

    // ---------------- 格擋（右鍵按住） ----------------

    /// <summary>開始 / 停止舉盾。released = 玩家放開右鍵（連續格擋的次數歸零）</summary>
    void SetBlocking(bool on, bool released)
    {
        if (IsBlocking == on) return;
        IsBlocking = on;
        if (on)
        {
            controller.lockRotation = true;
            controller.speedMultiplier = blockMoveMultiplier;
            GameAudio.Play("SFX/RPG/clothBelt2", transform.position, 0.6f, 1.1f, 0.05f, 1f, 20f);
        }
        else
        {
            controller.lockRotation = false;
            controller.speedMultiplier = 1f;
            if (released) BlockStreak = 0;
        }
        OnBlockChanged?.Invoke(on);
    }

    /// <summary>
    /// 舉盾時擋下正面的攻擊（PlayerHealth 受傷時呼叫）：回傳實際要扣的傷害。
    /// 擋下 → 0；連續擋到第 blockHitsBeforeBreak 下 → 破防，這一下照扣、盾牌放下一會兒。
    /// knockback = 攻擊把玩家推開的方向（用來判斷攻擊從哪裡來；沒有方向的攻擊例如光束擋不住）
    /// </summary>
    public float ApplyShieldBlock(float damage, Vector3 knockback)
    {
        if (!ShieldUp || bowMode || knockback.sqrMagnitude < 0.0001f) return damage;
        Vector3 from = -knockback; // 攻擊從哪個方向來
        from.y = 0f;
        if (from.sqrMagnitude < 0.0001f || Vector3.Dot(from.normalized, transform.forward) < blockFrontDot) return damage;

        BlockStreak++;
        lastBlockTime = Time.time;
        Vector3 shieldPoint = transform.position + Vector3.up * 0.3f + transform.forward * 0.6f;
        if (BlockStreak >= Mathf.Max(1, blockHitsBeforeBreak))
        {
            // 破防：這一下穿過盾牌，盾牌放下一會兒
            BlockStreak = 0;
            lastBlockBroke = true;
            guardBrokenUntil = Time.time + guardBreakTime;
            SetBlocking(false, false);
            GameAudio.Play("SFX/RPG/metalPot1", shieldPoint, 1f, 0.6f, 0.05f, 1f, 35f);
            CombatVFX.Impact(shieldPoint, new Color(1f, 0.45f, 0.3f));
            ThirdPersonCamera.ShakeMain(0.2f, 0.2f);
            OnBlockedHit?.Invoke(true);
            return damage;
        }

        lastBlockBroke = false;
        GameAudio.Play("SFX/RPG/metalPot3", shieldPoint, 1f, 1.2f, 0.05f, 1f, 30f);
        CombatVFX.Impact(shieldPoint, new Color(1f, 0.95f, 0.7f));
        ThirdPersonCamera.ShakeMain(0.06f, 0.1f);
        OnBlockedHit?.Invoke(false);
        return 0f;
    }

    // ---------------- 盾擊（右鍵） ----------------

    readonly Collider[] bashOverlap = new Collider[32];

    System.Collections.IEnumerator ShieldBash()
    {
        IsShieldBashing = true;
        nextBashTime = Time.time + shieldBashCooldown;
        controller.FaceDirection(GetAimDirection());
        controller.lockRotation = true;
        controller.speedMultiplier = 0.2f;
        OnShieldBash?.Invoke();
        GameAudio.Play("SFX/RPG/clothBelt", transform.position, 0.8f, 0.9f, 0.05f, 1f, 25f);

        // 舉盾
        yield return new WaitForSeconds(shieldBashWindup);

        // 往前頂 + 判定
        controller.AddImpulse(transform.forward * shieldBashLunge);
        var hit = new HashSet<object>();
        float t = 0f;
        while (t < shieldBashActive)
        {
            BashHitCheck(hit);
            t += Time.deltaTime;
            yield return null;
        }

        // 收盾
        yield return new WaitForSeconds(shieldBashRecovery);
        controller.speedMultiplier = 1f;
        controller.lockRotation = false;
        IsShieldBashing = false;
    }

    void BashHitCheck(HashSet<object> alreadyHit)
    {
        Vector3 center = transform.position + transform.forward * shieldBashReach;
        int n = Physics.OverlapSphereNonAlloc(center, shieldBashRadius, bashOverlap, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = bashOverlap[i];
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || alreadyHit.Contains(target)) continue;
            alreadyHit.Add(target);

            Vector3 point = c.ClosestPoint(center);
            target.TakeDamage(shieldBashDamage * damageMultiplier, point);

            // 盾擊的重點是把敵人撞開：額外的大力擊退
            var enemyMove = (target as Component) != null ? ((Component)target).GetComponent<ThirdPersonController>() : null;
            if (enemyMove != null)
            {
                Vector3 push = enemyMove.transform.position - transform.position;
                push.y = 0f;
                enemyMove.AddImpulse(push.normalized * shieldBashKnockback + Vector3.up * 2.5f);
            }

            GameAudio.Play("SFX/RPG/metalPot2", point, 1f, 0.7f, 0.05f, 1f, 35f);
            CombatVFX.Impact(point, new Color(0.85f, 0.9f, 1f));
            ThirdPersonCamera.ShakeMain(0.15f, 0.15f);
        }
    }

    // ---------------- 弓箭（在船上） ----------------

    void UpdateBow()
    {
        var mouse = Mouse.current;
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool aim = locked && mouse != null && mouse.rightButton.isPressed;
        if (aim != IsAimingBow)
        {
            IsAimingBow = aim;
            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam != null) cam.SetAiming(aim, transform);
            OnBowAim?.Invoke(aim);
        }
        if (IsAimingBow) FaceCamera();

        if (locked && mouse != null && mouse.leftButton.wasPressedThisFrame && Time.time >= nextArrowTime)
            ShootArrow();
    }

    void FaceCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 f = cam.transform.forward;
        f.y = 0f;
        if (f.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(f);
    }

    void ShootArrow()
    {
        nextArrowTime = Time.time + arrowCooldown;
        faceAimUntil = Time.time + 0.5f;
        FaceCamera();

        var cam = Camera.main;
        Vector3 origin = transform.position + Vector3.up * 0.5f + transform.forward * 0.6f;
        Vector3 target;
        // 往畫面正中央（準心）射：從鏡頭打一條線，找第一個不是自己 / 自己船的東西
        Ray ray = cam != null ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)) : new Ray(origin, transform.forward);
        if (!IsAimingBow)
        {
            // 沒瞄準：準心附近隨機偏一點
            ray.direction = Quaternion.Euler(Random.Range(-hipFireSpread, hipFireSpread), Random.Range(-hipFireSpread, hipFireSpread), 0f) * ray.direction;
        }
        target = ray.GetPoint(150f);
        float best = float.MaxValue;
        var aimHits = Physics.RaycastAll(ray, 150f, ~0, QueryTriggerInteraction.Ignore);
        Transform boat = transform.parent;
        foreach (var h in aimHits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (boat != null && h.collider.transform.IsChildOf(boat)) continue;
            if (h.distance < best) { best = h.distance; target = h.point; }
        }

        Vector3 dir = (target - origin).normalized;
        var arrowObj = EnemyBoat.CreateArrow(origin, dir);
        Destroy(arrowObj.GetComponent<Collider>()); // 玩家的箭用射線判定，不需要觸發碰撞體
        var arrow = arrowObj.AddComponent<PlayerArrow>();
        arrow.velocity = dir * arrowSpeed;
        arrow.owner = transform;
        arrow.ownerBoat = boat;
        arrow.shooter = this;

        GameAudio.Play("SFX/RPG/drawKnife3", origin, 0.85f, 1.6f, 0.08f, 0.5f, 30f);
        CombatVFX.Puff(origin, dir, new Color(0.9f, 0.95f, 1f, 0.5f));
        OnBowShot?.Invoke();
    }

    /// <summary>PlayerArrow 射中東西時呼叫：valid = 射中船員（準心閃紅色 ✕），false = 射到船身</summary>
    public void NotifyArrowHit(bool valid)
    {
        hitMarkerTime = Time.time;
        hitMarkerValid = valid;
    }

    void OnGUI()
    {
        if (health != null && health.IsDead) return;
        var gm = GameManager.Instance;
        if (gm != null && gm.IsOverlayOpen) return;
        if (!bowMode)
        {
            DrawShieldHud();
            return;
        }

        // 準心：瞄準時比較小、比較亮
        float cx = Screen.width * 0.5f, cy = Screen.height * 0.5f;
        float gap = IsAimingBow ? 4f : 10f, len = IsAimingBow ? 9f : 7f;
        GUI.color = IsAimingBow ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 1f, 1f, 0.6f);
        GUI.DrawTexture(new Rect(cx - gap - len, cy - 1f, len, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + gap, cy - 1f, len, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - gap - len, 2f, len), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy + gap, 2f, len), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - 1f, 2f, 2f), Texture2D.whiteTexture);

        // 命中提示：射中船員 → 紅色 ✕
        float since = Time.time - hitMarkerTime;
        if (since < 0.35f && hitMarkerValid)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 0.25f, 0.2f, 1f - since / 0.35f);
            GUI.Label(new Rect(cx - 30f, cy - 30f, 60f, 60f), "✕", style);
        }

        var hint = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(1f, 1f, 1f, 0.75f);
        GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f), "右鍵按住瞄準　左鍵射箭　只有射中船上的弓箭手才有效", hint);
        GUI.color = Color.white;
    }

    /// <summary>舉盾時畫面下方顯示盾牌還能擋幾下；破防時跳出「破防！」</summary>
    void DrawShieldHud()
    {
        float sinceBlock = Time.time - lastBlockTime;
        bool show = ShieldUp || sinceBlock < 1f;
        if (!show) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        float cx = Screen.width * 0.5f, y = Screen.height * 0.72f;
        if (lastBlockBroke && sinceBlock < 1f)
        {
            GUI.color = new Color(1f, 0.4f, 0.3f, 1f - sinceBlock);
            GUI.Label(new Rect(cx - 150f, y - 40f, 300f, 34f), "破防！", style);
        }
        if (!ShieldUp) { GUI.color = Color.white; return; }

        // 盾牌耐久：亮的格子 = 還能完全擋下幾下
        int total = Mathf.Max(1, blockHitsBeforeBreak);
        int left = total - 1 - BlockStreak; // 最後一格是「破防的那一下」
        const float w = 26f, h = 10f, gap = 6f;
        float x0 = cx - (total * w + (total - 1) * gap) * 0.5f;
        for (int i = 0; i < total; i++)
        {
            bool lit = i < left;
            GUI.color = lit ? new Color(0.85f, 0.9f, 1f, 0.95f) : new Color(1f, 0.35f, 0.3f, 0.6f);
            GUI.DrawTexture(new Rect(x0 + i * (w + gap), y, w, h), Texture2D.whiteTexture);
        }
        GUI.color = new Color(1f, 1f, 1f, 0.8f);
        var small = new GUIStyle(style) { fontSize = 15, fontStyle = FontStyle.Normal };
        GUI.Label(new Rect(cx - 200f, y + 12f, 400f, 24f), left > 0 ? $"格擋中：還能擋 {left} 下" : "再被打中就會破防！", small);
        GUI.color = Color.white;
    }

    protected override void OnSwingStart(int index) => attackQueued = false;

    protected override bool WantsNextHit() => attackQueued;

    protected override Vector3 GetAimDirection()
    {
        Transform cam = controller != null ? controller.cameraTransform : null;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        return cam != null ? cam.forward : transform.forward;
    }

    protected override bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit)
    {
        var target = c.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead || alreadyHit.Contains(target)) return false;

        alreadyHit.Add(target);
        target.TakeDamage(damage * damageMultiplier, hitPoint);
        ThirdPersonCamera.ShakeMain(0.08f, 0.1f);
        return true;
    }
}
