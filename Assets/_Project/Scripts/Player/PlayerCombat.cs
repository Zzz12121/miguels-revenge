using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家的攻擊：
/// - 滑鼠左鍵：近戰揮劍連擊（三段連擊），攻擊方向 = 鏡頭朝向
/// - 滑鼠右鍵（按一下）：舉盾 blockTapWindow 秒後自動放下，長按沒有額外效果。舉盾本身不會傷到敵人（盾擊衝撞預設關閉：shieldBashEnabled）。
///   正面的攻擊完全擋下 → 盾反：把攻擊的敵人彈開（硬直），接著自動往前突刺，造成兩倍傷害。
///   連續擋第 3 下會破防（那一下照樣扣血、盾牌放下一會兒）
/// - shieldBashEnabled 打開時，右鍵按一下 = 舊的舉盾衝撞（盾擊）
/// - 弓箭（bowMode：船戰登船，或拿到弓箭技能 / 鐵匠鋪換弓）：右鍵按住瞄準、左鍵射箭，陸地和船上的敵人都射得到
/// </summary>
public class PlayerCombat : MeleeCombat
{
    [Header("測試與倍率")]
    [Tooltip("玩家攻擊力倍率")]
    public float damageMultiplier = 1f;

    [Header("盾擊（右鍵按一下，在陸地上）")]
    [Tooltip("打開：右鍵按一下 = 往前頂的盾擊（會傷到敵人）。關閉（預設）：舉盾不會造成傷害，傷害只來自盾反突刺")]
    public bool shieldBashEnabled = false;
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

    [Header("格擋（右鍵按一下，在陸地上）")]
    [Tooltip("按一下右鍵後盾牌舉著多久 (秒)；長按不會延長，要再擋就要再按一次")]
    public float blockTapWindow = 0.35f;
    [Tooltip("盾牌放下後多久才能再按 (秒)，避免一直連點")]
    public float blockTapCooldown = 0.25f;
    [Tooltip("連續擋幾下會破防：第幾下的攻擊會穿過盾牌扣血（3 = 擋兩下，第三下扣血）")]
    public int blockHitsBeforeBreak = 3;
    [Tooltip("舉盾時的移動速度倍率。格擋必須站穩，因此預設為 0。")]
    [Range(0f, 1f)] public float blockMoveMultiplier = 0f;
    [Tooltip("破防之後多久不能再舉盾 (秒)")]
    public float guardBreakTime = 0.8f;
    [Tooltip("攻擊要從正面多少範圍內來才擋得住（和面向的夾角餘弦，0.3 ≈ 前方 145 度）")]
    [Range(-1f, 1f)] public float blockFrontDot = 0.3f;

    [Header("盾反（擋下攻擊 → 彈開敵人 → 突刺）")]
    public bool parryEnabled = true;
    [Tooltip("在擋下的那個方向多遠內找攻擊的敵人 (m)")]
    public float parrySearchRadius = 3.6f;
    [Tooltip("把敵人彈開的力道")]
    public float parryKnockback = 9f;
    [Tooltip("敵人被彈開後硬直幾秒（不扣血）")]
    public float parryStagger = 0.75f;
    [Tooltip("彈開之後隔多久出突刺 (秒)")]
    public float counterDelay = 0.12f;
    [Tooltip("突刺往前衝的力道")]
    public float counterLunge = 10f;
    [Tooltip("突刺有判定的時間 / 刺完收招的時間 (秒)")]
    public float counterActive = 0.16f, counterRecovery = 0.32f;
    [Tooltip("突刺判定：離角色多遠 / 半徑")]
    public float counterReach = 1.7f, counterRadius = 0.9f;
    [Tooltip("突刺傷害 = 第一段揮砍傷害 × 這個倍率（固定被打幾下就死的敵人：算這麼多下）")]
    public float counterDamageMultiplier = 2f;

    /// <summary>正在盾反突刺（彈開敵人後往前刺）</summary>
    public bool IsShieldCountering { get; private set; }
    /// <summary>盾反成功（盾牌上的位置、攻擊來的方向）：特效用</summary>
    public event System.Action<Vector3, Vector3> OnShieldParry;
    /// <summary>開始突刺（參數 = 突刺總長度秒數）：動畫用</summary>
    public event System.Action<float> OnShieldCounter;
    /// <summary>突刺目前的進度（0~1：0~delay 是準備、之後是刺出與收招）。姿勢用</summary>
    public float CounterProgress { get; private set; }
    Coroutine counterRoutine;
    readonly Collider[] parryOverlap = new Collider[32];

    /// <summary>正在盾擊（舉著盾牌往前頂）</summary>
    public bool IsShieldBashing { get; private set; }
    /// <summary>正在按住右鍵舉盾格擋</summary>
    public bool IsBlocking { get; private set; }
    /// <summary>盾牌舉著（格擋或盾擊中）：正面攻擊會被擋下</summary>
    public bool ShieldUp => IsBlocking || IsShieldBashing || IsShieldCountering || SkillShield;
    /// <summary>技能自己舉盾（例如 × □ × 的滑剷繞背斬）：只負責讓盾牌顯示出來，不會真的格擋</summary>
    [System.NonSerialized] public bool SkillShield;
    /// <summary>這一輪連續擋了幾下（放下盾牌或破防就歸零）</summary>
    public int BlockStreak { get; private set; }
    /// <summary>盾擊開始時通知外面（模型動畫用）</summary>
    public event System.Action OnShieldBash;
    /// <summary>玩家的近戰 / 盾擊打中目標（目標、命中點、傷害、是否重擊）。打擊感回饋（PlayerHitFeedback）用。</summary>
    public static event System.Action<IDamageable, Vector3, float, bool> OnHitLanded;
    /// <summary>開始 / 停止格擋</summary>
    public event System.Action<bool> OnBlockChanged;
    /// <summary>用盾牌擋下攻擊（參數 true = 這一下破防了）</summary>
    public event System.Action<bool> OnBlockedHit;
    float nextBashTime, guardBrokenUntil = float.NegativeInfinity, lastBlockTime = float.NegativeInfinity;
    bool lastBlockBroke;
    float blockWindowEnd = float.NegativeInfinity, nextBlockTapTime;
    Coroutine shieldBashRoutine;

    [Header("弓箭（VikingBoat 登船、或 PlayerLoadout 換成弓時打開）")]
    [Tooltip("打開後：右鍵按住瞄準（鏡頭拉近放大），左鍵射箭；不能揮劍")]
    public bool bowMode;
    public float arrowSpeed = 70f;
    [Tooltip("兩支箭之間的冷卻 (秒)")]
    public float arrowCooldown = 0.55f;
    [Tooltip("沒有瞄準直接射時，箭的散佈角度（度）")]
    public float hipFireSpread = 2.5f;
    [Tooltip("箭射中一般目標（IDamageable）時的傷害。拿弓時由 PlayerLoadout 依蓄力設定")]
    public float arrowDamage = 0f;
    [Tooltip("瞄準中（右鍵按住）射箭時的散佈角度（度）；0 = 正中準心。鐵匠鋪的弓依蓄力的程度設定")]
    public float aimedSpread = 0f;
    /// <summary>剛射出一支箭（箭的 GameObject）：換箭的外觀用</summary>
    public event System.Action<GameObject> OnArrowSpawned;

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
        // 跳劈落地：地面衝擊 + 鏡頭震動
        OnPlungeLanded += p =>
        {
            CombatVFX.Impact(p + transform.forward * hitForwardOffset + Vector3.up * .1f, new Color(1f, .85f, .55f));
            ThirdPersonCamera.ShakeMain(.28f, .22f);
            GameAudio.PlayAt(GameAudio.Pick(hitSound, "SFX/RPG/chop"), p, soundVolume, .7f, 1f, 30f);
        };
        // 所有場景都共用同一個玩家物件；缺少時自動補上雙極牽引技能，
        // 靶場與正式關卡不需要各自維護一份技能 Prefab。
        if (GetComponent<EnergyTetherSkill>() == null)
            gameObject.AddComponent<EnergyTetherSkill>();
        // 跳躍、落地、揮刀、打中的音效（只加聲音）
        if (GetComponent<PlayerSfx>() == null) gameObject.AddComponent<PlayerSfx>();
        // 揮刀時上半身擺動更明顯（只疊在動作上面）
        if (GetComponent<SwingBodyAccent>() == null) gameObject.AddComponent<SwingBodyAccent>();
        // 拉弓瞄準時走路：腿輪流踏步，不會整個人在地上滑（只動腿）
        if (GetComponent<BowAimFootwork>() == null) gameObject.AddComponent<BowAimFootwork>();
        // 幻影分身突進（空中側閃時按左鍵）/ 剪刀斬：自動補上
        phantom = GetComponent<PhantomStrike>();
        if (phantom == null) phantom = gameObject.AddComponent<PhantomStrike>();
    }

    PhantomStrike phantom;

    void Update()
    {
        if (health != null && health.IsDead) return;
        if (controller != null && (controller.IsRolling || controller.ActionsLocked)) return;
        if (bowMode)
        {
            UpdateBow();
            return;
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;

        // 攻擊 / 格擋的按鍵在暫停選單「設定」裡可以改（預設 滑鼠左鍵 / 右鍵，手把 X / LT）
        bool leftClick = InputBindings.Pressed(GameAction.Attack);
        bool rightClick = InputBindings.Pressed(GameAction.Block);

        // 受擊僵直中不能攻擊 / 舉盾
        if (controller.IsStunned)
        {
            attackQueued = false;
            if (IsBlocking) SetBlocking(false, true);
            return;
        }

        // 幻影分身突進進行中：交給它；空中側閃時按左鍵 = 分身突進、分身還在時按左鍵 = 剪刀斬
        if (phantom != null)
        {
            if (phantom.Busy) return;
            if (leftClick && !ShieldUp && (phantom.TryStartFromDash() || phantom.TryScissor())) return;
        }

        // 右鍵按一下：舉盾衝撞（揮劍連擊中、破防中不能用）
        bool guardBroken = Time.time < guardBrokenUntil;
        if (IsShieldCountering) return;   // 突刺中：交給盾反流程
        if (shieldBashEnabled && rightClick && !IsAttacking && !IsShieldBashing && !guardBroken && Time.time >= nextBashTime)
        {
            if (IsBlocking) SetBlocking(false, false);
            shieldBashRoutine = StartCoroutine(ShieldBash());
            return;
        }

        // 右鍵按一下：舉盾 blockTapWindow 秒，時間到自動放下（長按沒有效果）
        if (rightClick && !IsBlocking && !IsAttacking && !IsShieldBashing && !guardBroken && Time.time >= nextBlockTapTime)
        {
            blockWindowEnd = Time.time + blockTapWindow;
            SetBlocking(true, false);
        }
        if (IsBlocking && (Time.time >= blockWindowEnd || IsAttacking || IsShieldBashing || guardBroken))
            SetBlocking(false, true);
        if (IsBlocking) controller.FaceDirection(GetAimDirection());

        // 有「重擊」能力：沒在攻擊時按下左鍵先開始蓄力，放開才出手；按住超過 heavyHoldTime 就是重擊（舉盾時不能蓄力）
        if (heavyAttackEnabled && !IsAttacking && !ShieldUp)
        {
            bool held = InputBindings.Held(GameAction.Attack);
            if (leftClick) { charging = true; chargeStart = Time.time; chargeFull = false; }
            if (charging && !chargeFull && held && Time.time - chargeStart >= heavyHoldTime)
            {
                chargeFull = true;
                GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.7f, 1.5f, 0f, 0.3f);
                CombatVFX.Impact(transform.position + Vector3.up, new Color(1f, 0.6f, 0.2f));
            }
            if (charging && !held)
            {
                charging = false;
                heavyChain = chargeFull;
                BeginAttack();
            }
            return;
        }
        charging = false;

        // 左鍵：近戰連擊（攻擊中再按 = 接下一段）；舉盾、盾擊時不能揮劍
        if (leftClick && !ShieldUp)
        {
            heavyChain = false;
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
            // 舉盾前若正在跑，不能讓既有的水平慣性繼續把角色往前滑。
            controller.StopHorizontalMotion();
            GameAudio.Play("SFX/RPG/clothBelt2", transform.position, 0.6f, 1.1f, 0.05f, 1f, 20f);
        }
        else
        {
            controller.lockRotation = false;
            controller.speedMultiplier = 1f;
            if (released) BlockStreak = 0;
            nextBlockTapTime = Time.time + blockTapCooldown;
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
            CombatVFX.Impact(shieldPoint, new Color(1f, 0.45f, 0.3f));   // 盾牌不放金屬聲
            ThirdPersonCamera.ShakeMain(0.2f, 0.2f);
            OnBlockedHit?.Invoke(true);
            return damage;
        }

        lastBlockBroke = false;
        if (parryEnabled && !IsShieldCountering && !IsShieldBashing)
        {
            Vector3 parryPoint = transform.position + Vector3.up * 1.1f + transform.forward * 0.7f;
            var attacker = FindParryTarget(from.normalized);
            OnShieldParry?.Invoke(parryPoint, from.normalized);
            OnBlockedHit?.Invoke(false);
            if (attacker != null)
            {
                ParryPush(attacker);
                counterRoutine = StartCoroutine(ShieldCounter(attacker));
            }
            return 0f;
        }
        CombatVFX.Impact(shieldPoint, new Color(1f, 0.95f, 0.7f));
        ThirdPersonCamera.ShakeMain(0.06f, 0.1f);
        OnBlockedHit?.Invoke(false);
        return 0f;
    }

    // ---------------- 盾反（擋下 → 彈開 → 突刺） ----------------

    /// <summary>擋下的那個方向上，離盾牌最近的敵人（小怪 / Boss，任何 IDamageable）</summary>
    Component FindParryTarget(Vector3 from)
    {
        Vector3 center = transform.position + Vector3.up + from * (parrySearchRadius * 0.5f);
        int n = Physics.OverlapSphereNonAlloc(center, parrySearchRadius * 0.6f, parryOverlap, ~0, QueryTriggerInteraction.Ignore);
        Component best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var c = parryOverlap[i];
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || !(target is Component comp)) continue;
            Vector3 to = comp.transform.position - transform.position; to.y = 0f;
            if (to.magnitude > parrySearchRadius || Vector3.Dot(to.normalized, from) < 0.2f) continue;
            if (to.magnitude < bestD) { bestD = to.magnitude; best = comp; }
        }
        return best;
    }

    void ParryPush(Component attacker)
    {
        Vector3 push = attacker.transform.position - transform.position; push.y = 0f;
        push = push.sqrMagnitude > 0.001f ? push.normalized : transform.forward;
        var move = attacker.GetComponent<ThirdPersonController>();
        if (move != null) move.AddImpulse(push * parryKnockback + Vector3.up * 2f);
        var ai = attacker.GetComponent<EnemyAI>();
        if (ai != null) ai.Parried(parryStagger);
    }

    System.Collections.IEnumerator ShieldCounter(Component target)
    {
        IsShieldCountering = true;
        if (IsBlocking) SetBlocking(false, false);
        controller.lockRotation = true;
        controller.speedMultiplier = 0f;
        float total = counterDelay + counterActive + counterRecovery;
        OnShieldCounter?.Invoke(total);
        float start = Time.time;
        var presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (presenter != null) presenter.DrawForSeconds(total + 0.3f);

        // 轉向被彈開的敵人，稍微蓄勢
        while (Time.time - start < counterDelay)
        {
            FaceTarget(target);
            CounterProgress = (Time.time - start) / total;
            yield return null;
        }

        // 刺出：往前衝 + 判定（同一個敵人只算一次）
        FaceTarget(target);
        controller.AddImpulse(transform.forward * counterLunge);
        GameAudio.Play("SFX/RPG/drawKnife2", transform.position + Vector3.up, 0.9f, 1.25f, 0.05f, 1f, 30f);
        var hit = new HashSet<object>();
        while (Time.time - start < counterDelay + counterActive)
        {
            CounterHitCheck(hit);
            CounterProgress = (Time.time - start) / total;
            yield return null;
        }

        // 收招
        while (Time.time - start < total)
        {
            CounterProgress = (Time.time - start) / total;
            yield return null;
        }
        EndCounter();
    }

    void FaceTarget(Component target)
    {
        if (target == null) return;
        Vector3 d = target.transform.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 0.01f) controller.FaceDirection(d.normalized);
    }

    void CounterHitCheck(HashSet<object> alreadyHit)
    {
        Vector3 center = transform.position + Vector3.up * 1.1f + transform.forward * counterReach;
        int n = Physics.OverlapSphereNonAlloc(center, counterRadius, parryOverlap, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = parryOverlap[i];
            if (!CalligraphyWorldIsolation.Allows(c.transform)) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || alreadyHit.Contains(target)) continue;
            alreadyHit.Add(target);

            bool supportsClosest = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex);
            Vector3 point = supportsClosest ? c.ClosestPoint(center) : c.bounds.ClosestPoint(center);
            float baseDamage = comboDamage.Length > 0 ? comboDamage[0] : 12f;
            float dealt = baseDamage * counterDamageMultiplier * DamageScale(false, target);
            if (target is EnemyHealth eh) eh.TakeDamage(dealt, point, counterDamageMultiplier);
            else target.TakeDamage(dealt, point);
            AfterHit(dealt, target);
            OnHitLanded?.Invoke(target, point, dealt, true);
            GameAudio.Play("SFX/RPG/knifeSlice", point, 1f, 0.8f, 0.05f, 1f, 35f);
            CombatVFX.Impact(point, new Color(1f, 0.75f, 0.3f));
            ThirdPersonCamera.ShakeMain(0.18f, 0.14f);
        }
    }

    void EndCounter()
    {
        if (counterRoutine != null) { StopCoroutine(counterRoutine); counterRoutine = null; }
        if (!IsShieldCountering) return;
        IsShieldCountering = false;
        CounterProgress = 0f;
        if (controller != null) { controller.speedMultiplier = 1f; controller.lockRotation = false; }
    }

    [Header("重擊（能力植物「重擊」會打開）")]
    [Tooltip("打開後：長按左鍵蓄力，放開打出重擊")]
    public bool heavyAttackEnabled;
    [Tooltip("按住多久算重擊 (秒)")]
    public float heavyHoldTime = 0.4f;
    public float heavyDamageScale = 1.5f;

    bool charging, chargeFull, heavyChain, heavySwing;
    float chargeStart;

    [Tooltip("手上武器的傷害倍率（鐵匠鋪換武器時由 PlayerLoadout 設定，不會跟著存檔 / 換關卡；大刀 = 1）")]
    [System.NonSerialized] public float weaponDamageScale = 1f;

    /// <summary>玩家造成傷害的總倍率：基本倍率 × 武器 × 能力植物（低血量狂暴）</summary>
    float DamageScale(bool heavy, IDamageable target)
    {
        if (perks == null) perks = GetComponent<PlayerPerks>();
        return damageMultiplier * weaponDamageScale * (heavy ? heavyDamageScale : 1f) * (perks != null ? perks.DamageScaleFor(target) : 1f);
    }

    /// <summary>打中東西的瞬間（近戰、盾擊、箭）：傷害、打中的目標（箭射中時是 null）。打擊回饋（震動）用</summary>
    public event System.Action<float, IDamageable> OnDealtHit;

    /// <summary>技能自己造成傷害之後呼叫：跟普攻打中一樣算進打擊回饋（頓幀、震動、音效、戰鬥紀錄、能力加成）</summary>
    /// <param name="countsAsSkill">true = 技能打的（不累積技能條）；false = 一般招式（例如 × □ × 滑剷繞背斬），跟普攻一樣累積</param>
    public void ReportSkillHit(float dealt, IDamageable target, Vector3 point, bool heavy, bool countsAsSkill = true)
    {
        ReportingSkillHit = countsAsSkill;   // 技能條（SkillGauge）只算普攻 / 一般招式：技能打的不累積
        try
        {
            AfterHit(dealt, target);
            OnHitLanded?.Invoke(target, point, dealt, heavy);
        }
        finally { ReportingSkillHit = false; }
    }
    /// <summary>正在回報技能造成的傷害（OnDealtHit 的接收者用來分辨普攻 / 技能）</summary>
    public static bool ReportingSkillHit { get; private set; }

    void AfterHit(float dealt, IDamageable target)
    {
        if (perks != null) perks.OnDealtDamage(dealt, target);
        OnDealtHit?.Invoke(dealt, target);
    }

    PlayerPerks perks;

    // ---------------- 盾擊（右鍵） ----------------

    readonly Collider[] bashOverlap = new Collider[32];

    /// <summary>
    /// 翻滾、跳躍或外部技能接管角色時，統一清掉所有暫時動作鎖。
    /// 避免協程被中斷後殘留低速倍率或鎖定轉向，造成角色看似無法移動。
    /// </summary>
    public void CancelTransientActions()
    {
        CancelAttack();
        CancelShieldBash();
        EndCounter();
        if (IsBlocking) SetBlocking(false, true);
        charging = false;
        chargeFull = false;
        heavyChain = false;
        attackQueued = false;

        if (IsAimingBow)
        {
            IsAimingBow = false;
            OnBowAim?.Invoke(false);
            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam != null) cam.SetAiming(false, transform);
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        CancelShieldBash();
        EndCounter();
        if (IsBlocking) SetBlocking(false, true);
        charging = false;
        attackQueued = false;

        if (IsAimingBow)
        {
            IsAimingBow = false;
            OnBowAim?.Invoke(false);
        }
    }

    void CancelShieldBash()
    {
        if (shieldBashRoutine != null)
        {
            StopCoroutine(shieldBashRoutine);
            shieldBashRoutine = null;
        }

        IsShieldBashing = false;
        if (controller != null)
        {
            controller.speedMultiplier = 1f;
            controller.lockRotation = false;
        }
    }

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
        shieldBashRoutine = null;
    }

    void BashHitCheck(HashSet<object> alreadyHit)
    {
        Vector3 center = transform.position + transform.forward * shieldBashReach;
        int n = Physics.OverlapSphereNonAlloc(center, shieldBashRadius, bashOverlap, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < n; i++)
        {
            var c = bashOverlap[i];
            if (!CalligraphyWorldIsolation.Allows(c.transform)) continue;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || alreadyHit.Contains(target)) continue;
            alreadyHit.Add(target);

            Vector3 point = c.ClosestPoint(center);
            float dealt = shieldBashDamage * DamageScale(false, target);
            target.TakeDamage(dealt, point);
            AfterHit(dealt, target);
            OnHitLanded?.Invoke(target, point, dealt, true);

            // 盾擊的重點是把敵人撞開：額外的大力擊退
            var enemyMove = (target as Component) != null ? ((Component)target).GetComponent<ThirdPersonController>() : null;
            if (enemyMove != null)
            {
                Vector3 push = enemyMove.transform.position - transform.position;
                push.y = 0f;
                enemyMove.AddImpulse(push.normalized * shieldBashKnockback + Vector3.up * 2.5f);
            }

            CombatVFX.Impact(point, new Color(0.85f, 0.9f, 1f));   // 盾擊打中：不放金屬聲
            ThirdPersonCamera.ShakeMain(0.15f, 0.15f);
        }
    }

    // ---------------- 弓箭（在船上） ----------------

    void UpdateBow()
    {
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool aim = locked && InputBindings.Held(GameAction.BowAim);   // 預設 滑鼠右鍵 / 手把 L2
        if (aim != IsAimingBow)
        {
            IsAimingBow = aim;
            var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam != null) cam.SetAiming(aim, transform);
            OnBowAim?.Invoke(aim);
        }
        if (IsAimingBow) FaceCamera();

        // 射箭：攻擊鍵（滑鼠左鍵 / □），或拉弓瞄準中按「瞄準確認」（手把 R2；沒瞄準時 R2 是衝刺，不射）
        bool fire = InputBindings.Pressed(GameAction.Attack) || (IsAimingBow && InputBindings.PadPressed(GameAction.AimConfirm));
        if (locked && fire && Time.time >= nextArrowTime)
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
        float spread = IsAimingBow ? aimedSpread : hipFireSpread;
        if (spread > 0f)
        {
            // 沒瞄準（或沒蓄滿力）：準心附近隨機偏一點
            ray.direction = Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * ray.direction;
        }
        target = ray.GetPoint(150f);
        float best = float.MaxValue;
        var aimHits = Physics.RaycastAll(ray, 150f, ~0, QueryTriggerInteraction.Ignore);
        Transform boat = transform.parent;
        foreach (var h in aimHits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (boat != null && h.collider.transform.IsChildOf(boat)) continue;
            if (ArrowPassThrough.On(h.collider)) continue;
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
        arrow.damage = arrowDamage * damageMultiplier;
        // 海面判定（箭低於 waterLevel 就當成掉進海裡）只有船戰用得到：在陸地上地面大約在 y = 0，
        // 瞄準敵人胸口的箭一往下掉就會被當成落海、還沒碰到敵人就消失
        if (FindFirstObjectByType<VikingBoat>() == null) arrow.waterLevel = float.NegativeInfinity;
        OnArrowSpawned?.Invoke(arrowObj);

        GameAudio.Play("SFX/RPG/drawKnife3", origin, 0.85f, 1.6f, 0.08f, 0.5f, 30f);
        CombatVFX.Puff(origin, dir, new Color(0.9f, 0.95f, 1f, 0.5f));
        OnBowShot?.Invoke();
    }

    /// <summary>PlayerArrow 射中東西時呼叫：valid = 射中船員（準心閃紅色 ✕），false = 射到船身</summary>
    public void NotifyArrowHit(bool valid)
    {
        hitMarkerTime = Time.time;
        hitMarkerValid = valid;
        if (valid) OnDealtHit?.Invoke(arrowDamage, null);
    }

    /// <summary>不顯示畫面下方船戰用的射箭說明（日式村莊的弓由 PlayerLoadout 打開這個）</summary>
    [System.NonSerialized] public bool hideBowHint;

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

        if (!hideBowHint)
        {
            var hint = new GUIStyle(GUI.skin.label) { fontSize = 18, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(0f, Screen.height - 70f, Screen.width, 30f), "右鍵按住瞄準　左鍵射箭　只有射中船上的弓箭手才有效", hint);
        }
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

    [Header("揮刀的空氣波動（判定傷害的那一刻，打沒打到都有）")]
    public bool strikeAirWave = true;
    [Tooltip("波動半徑 = 判定球半徑 × 這個")]
    [Range(.2f, 1.5f)] public float strikeWaveScale = .75f;
    [Tooltip("扭曲強度：一般刀 / 第三刀、重擊、跳劈落地")]
    [Range(0f, 1f)] public float strikeWaveStrength = .22f, heavyWaveStrength = .32f;
    [Tooltip("擴散時間（秒）")]
    public float strikeWaveTime = .28f, heavyWaveTime = .4f;

    /// <summary>每一刀判定傷害的那一刻（判定球的中心、半徑）。幻影分身用這個跟著本尊一起砍</summary>
    public event System.Action<Vector3, float> OnStrike;

    protected override void OnStrikeMoment(Vector3 center, float radius, int index, bool landing)
    {
        OnStrike?.Invoke(center, radius);
        if (!strikeAirWave) return;
        bool big = landing || index == 2 || heavySwing;
        Vector3 at = center + Vector3.up * (landing ? -.3f : .2f); // 判定球中心大約在腰的高度；跳劈落地放低一點
        float r = radius * strikeWaveScale * (big ? 1.3f : 1f);
        ShockwaveFx.Spawn(at, r, big ? heavyWaveTime : strikeWaveTime, big ? heavyWaveStrength : strikeWaveStrength);
        if (big) ShockwaveFx.Spawn(at, r * .55f, heavyWaveTime * .8f, strikeWaveStrength, .07f); // 第二圈
    }

    protected override void OnSwingStart(int index)
    {
        attackQueued = false;
        heavySwing = index == 0 && heavyChain; // 重擊只算蓄力放開的那一刀
        if (heavySwing) ThirdPersonCamera.ShakeMain(0.12f, 0.15f);
    }

    protected override bool WantsNextHit() => attackQueued;

    protected override Vector3 GetAimDirection()
    {
        Transform cam = controller != null ? controller.cameraTransform : null;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        return cam != null ? cam.forward : transform.forward;
    }

    protected override bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit)
    {
        if(!CalligraphyWorldIsolation.Allows(c.transform))return false;
        var target = c.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead || alreadyHit.Contains(target)) return false;

        alreadyHit.Add(target);
        float dealt = damage * DamageScale(heavySwing, target);
        target.TakeDamage(dealt, hitPoint);
        AfterHit(dealt, target);
        OnHitLanded?.Invoke(target, hitPoint, dealt, heavySwing || IsDashSlash); // 幻影突進 / 剪刀斬：打中的手感算重擊（頓幀久一點）
        if (heavySwing) CombatVFX.Impact(hitPoint, new Color(1f, 0.6f, 0.2f));
        ThirdPersonCamera.ShakeMain(heavySwing ? 0.18f : 0.08f, 0.1f);
        return true;
    }
}
