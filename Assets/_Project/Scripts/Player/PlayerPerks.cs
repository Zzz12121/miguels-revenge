using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家的能力路線與特殊能力（能力植物給的）。
/// 第一株能力植物（蜘蛛房）決定路線：跑速 / 力量 / 血量；
/// 第二株（Boss 房火堆旁）從該路線的 6 個進階能力中隨機抽 3 個，讓玩家選 1 個：
///   跑速：輕盈腳步（跑速 +15%）/ 一步登天（二段跳）/ 疾行奔走（10% 閃避）
///         迅返戰場（翻滾後 1 秒跑速 +40%）/ 乘風啟航（衝刺速度 +25%）/ 喘息不止（精力回復每秒 30%）
///   力量：鋒利劍芒（攻擊 +10）/ 巧匠天工（長按左鍵重擊 ×2）/ 浴血奮鬥（血量低於 30% 攻擊 ×1.4）
///         滿盈痛擊（精力高於 75% 攻擊 ×1.2）/ 越戰越勇（0.5 秒內連續打同一目標，每下 +10%，最多 +50%）
///         深淵復仇（標記敵人 2 秒，期間受到的傷害結束時再打一次，冷卻 20 秒）
///   血量：強健體魄（生命 +40）/ 榮耀加身（每 12 秒一層護盾）/ 嗜血狂妄（血量低於 40% 攻擊吸血 10%）
///         治癒之祈（每 15 秒回 10 血）/ 奮勇抵抗（血量低於 60% 減傷 15%）
///         惡魔型態（生命上限變 30%，每 20 秒無敵 3 秒，期間跑速 +30%、攻擊 ×1.5）
/// 數值型的能力（跑速、攻擊、生命）直接改在對應元件上；這裡記錄拿過哪些能力，並處理需要持續判斷的能力。
/// 已獲得的能力會以簡短文字顯示在畫面右上（小地圖下方）。
/// 掛在有 PlayerHealth 的玩家物件上（沒有的話 PlayerPerks.For 會自動加上），會跟著 PlayerState 跨關卡 / 存檔。
/// </summary>
public class PlayerPerks : MonoBehaviour
{
    public enum Path { None = 0, Speed = 1, Power = 2, Health = 3 }

    // 存檔存的是數字，新的能力只能加在最後面
    public enum Perk
    {
        SpeedUp, HighJump, Dodge,
        PowerUp, HeavyAttack, Berserk,
        HealthUp, Shield, Lifesteal,
        QuickRecovery, Tailwind, SecondWind,
        FullForce, Momentum, AbyssRevenge,
        Prayer, Resolve, DemonForm,
    }

    [Header("數值")]
    [Range(0f, 1f)] public float dodgeChance = 0.1f;
    [Tooltip("護盾用掉之後多久再長出來 (秒)")]
    public float shieldInterval = 12f;
    [Tooltip("血量比例低於這個值時攻擊力上升")]
    public float berserkThreshold = 0.3f;
    public float berserkMultiplier = 1.4f;
    [Tooltip("血量比例低於這個值時攻擊會吸血")]
    public float lifestealThreshold = 0.4f;
    [Tooltip("吸血比例（造成傷害的幾成）")]
    public float lifestealRatio = 0.1f;

    [Header("跑速路線")]
    public float quickRecoveryBonus = 0.4f, quickRecoveryTime = 1f;
    public float tailwindBonus = 0.25f;
    [Tooltip("喘息不止：站著 / 走路時每秒回復的精力比例")]
    public float secondWindRegen = 0.3f;

    [Header("力量路線")]
    public float fullForceThreshold = 0.75f, fullForceMultiplier = 1.2f;
    [Tooltip("越戰越勇：距離上一下多久內打同一目標算連續 (秒)")]
    public float momentumWindow = 0.5f;
    public float momentumStep = 0.1f, momentumMax = 0.5f;
    public float abyssDuration = 2f, abyssCooldown = 20f;

    [Tooltip("巧匠天工：蓄力重擊的傷害倍率")]
    public float heavyAttackScale = 2f;
    public float powerUpDamage = 10f;

    [Header("血量路線")]
    public float healthUpAmount = 40f;
    public float prayerInterval = 15f, prayerHeal = 10f;
    public float resolveThreshold = 0.6f, resolveReduction = 0.15f;
    [Tooltip("惡魔型態：生命上限變成原本的幾成")]
    public float demonHealthRatio = 0.3f;
    public float demonInterval = 20f, demonDuration = 3f;
    public float demonSpeedMultiplier = 1.3f, demonDamageMultiplier = 1.5f;

    /// <summary>第一株能力植物選的路線</summary>
    public Path UpgradePath { get; set; }
    /// <summary>第一株能力植物給的效果（顯示用，例如「跑速 +15%」）</summary>
    public string BasicUpgrade { get; set; }
    // 依照拿到的順序（右上角的清單照這個順序顯示）
    readonly List<Perk> perks = new List<Perk>();

    PlayerHealth health;
    ThirdPersonController ctrl;
    bool shieldReady;
    float shieldReadyAt;
    float popupTime = float.NegativeInfinity;
    string popupText;

    // 越戰越勇：每個目標上一次被打的時間和層數
    readonly Dictionary<IDamageable, (float time, int stacks)> momentum = new Dictionary<IDamageable, (float, int)>();
    int lastMomentumStacks;
    float lastMomentumTime = float.NegativeInfinity;
    float abyssReadyAt;
    float prayerNextAt;
    float demonNextAt, demonUntil = float.NegativeInfinity;
    Light demonGlow;

    public static PlayerPerks For(GameObject player)
    {
        if (player == null) return null;
        var hp = player.GetComponentInChildren<PlayerHealth>();
        GameObject host = hp != null ? hp.gameObject : player;
        var p = host.GetComponent<PlayerPerks>();
        return p != null ? p : host.AddComponent<PlayerPerks>();
    }

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        ctrl = GetComponent<ThirdPersonController>();
    }

    void OnEnable() { if (ctrl != null) ctrl.OnRollEnded += OnRollEnded; }

    void OnDisable()
    {
        if (ctrl != null) ctrl.OnRollEnded -= OnRollEnded;
        EndDemon();
    }

    public bool Has(Perk perk) => perks.Contains(perk);

    public void Add(Perk perk)
    {
        if (Has(perk)) return;
        perks.Add(perk);
        if (perk == Perk.Shield) shieldReady = true;
        ResetTimers();
        ApplyMovementPerks();
    }

    public IEnumerable<Perk> All => perks;

    /// <summary>讀檔 / 換關卡時套用（數值型能力已經存在 PlayerState 的數值裡，這裡只恢復記錄）</summary>
    public void SetAll(Path path, string basicUpgrade, IEnumerable<Perk> list)
    {
        UpgradePath = path;
        BasicUpgrade = basicUpgrade;
        perks.Clear();
        foreach (var p in list) if (!perks.Contains(p)) perks.Add(p);
        shieldReady = Has(Perk.Shield);
        ResetTimers();
        ApplyMovementPerks();
    }

    void ResetTimers()
    {
        prayerNextAt = Time.time + prayerInterval;
        demonNextAt = Time.time + demonInterval;
        abyssReadyAt = 0f;
    }

    void ApplyMovementPerks()
    {
        if (ctrl != null) ctrl.doubleJumpEnabled = Has(Perk.HighJump);
        var combat = GetComponent<PlayerCombat>();
        if (combat != null)
        {
            combat.heavyAttackEnabled = Has(Perk.HeavyAttack);
            if (combat.heavyAttackEnabled) combat.heavyDamageScale = heavyAttackScale;
        }
        var stamina = GetComponent<PlayerStamina>();
        if (stamina != null && Has(Perk.SecondWind))
            stamina.idleRegenPerSecond = Mathf.Max(stamina.idleRegenPerSecond, secondWindRegen);
    }

    void Start() => ApplyMovementPerks();

    void Update()
    {
        if (Has(Perk.Shield) && !shieldReady && Time.time >= shieldReadyAt)
        {
            shieldReady = true;
            GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.5f, 1.4f, 0f, 0.3f);
        }

        bool alive = health != null && !health.IsDead;

        // 治癒之祈：每 15 秒回 10 血
        if (Has(Perk.Prayer) && Time.time >= prayerNextAt)
        {
            prayerNextAt = Time.time + prayerInterval;
            if (alive && health.Health < health.maxHealth)
            {
                health.Heal(prayerHeal);
                CombatVFX.Heal(transform);
                Popup($"治癒之祈 +{prayerHeal:0}");
            }
        }

        // 惡魔型態：每 20 秒無敵 3 秒
        if (Has(Perk.DemonForm))
        {
            if (DemonActive)
            {
                if (demonGlow != null) demonGlow.intensity = 3f + 1.5f * Mathf.Sin(Time.time * 12f);
            }
            else if (demonGlow != null) EndDemon();
            if (alive && Time.time >= demonNextAt) BeginDemon();
        }
    }

    bool DemonActive => Time.time < demonUntil;

    void BeginDemon()
    {
        demonNextAt = Time.time + demonInterval;
        demonUntil = Time.time + demonDuration;
        health.SetSkillInvulnerability(this, true);
        if (ctrl != null) ctrl.ApplyTemporarySpeedBoost(demonSpeedMultiplier, demonDuration);
        if (demonGlow == null)
        {
            var go = new GameObject("DemonGlow");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.up * 1.2f;
            demonGlow = go.AddComponent<Light>();
            demonGlow.type = LightType.Point;
            demonGlow.color = new Color(1f, 0.15f, 0.1f);
            demonGlow.range = 5f;
        }
        CombatVFX.Ring(transform.position + Vector3.up * 0.1f, new Color(1f, 0.2f, 0.1f), 2.5f);
        GameAudio.Play("SFX/RPG/metalPot3", transform.position, 1f, 0.5f, 0.05f, 0.3f);
        Popup("惡魔型態！");
    }

    void EndDemon()
    {
        if (health != null) health.SetSkillInvulnerability(this, false);
        if (demonGlow != null) Destroy(demonGlow.gameObject);
        demonGlow = null;
    }

    void OnRollEnded()
    {
        if (Has(Perk.QuickRecovery) && ctrl != null)
            ctrl.ApplyTemporarySpeedBoost(1f + quickRecoveryBonus, quickRecoveryTime);
    }

    float HealthRatio => health != null && health.maxHealth > 0f ? health.Health / health.maxHealth : 1f;

    float StaminaRatio
    {
        get
        {
            var s = GetComponent<PlayerStamina>();
            return s != null ? s.Stamina01 : 0f;
        }
    }

    /// <summary>受到傷害前呼叫：回傳 true = 這一下被閃掉 / 被護盾擋掉，完全不扣血</summary>
    public bool TryNegateHit()
    {
        if (Has(Perk.Dodge) && Random.value < dodgeChance)
        {
            Popup("閃避！");
            GameAudio.Play("SFX/RPG/clothBelt", transform.position, 0.8f, 1.4f, 0.05f, 0.3f);
            return true;
        }
        if (Has(Perk.Shield) && shieldReady)
        {
            shieldReady = false;
            shieldReadyAt = Time.time + shieldInterval;
            Popup("護盾抵擋");
            GameAudio.Play("SFX/RPG/metalPot3", transform.position + Vector3.up, 1f, 1.3f, 0.05f, 0.3f);
            CombatVFX.Impact(transform.position + Vector3.up, new Color(0.5f, 0.85f, 1f));
            return true;
        }
        return false;
    }

    /// <summary>受到傷害的倍率（奮勇抵抗：低血量減傷）</summary>
    public float IncomingDamageScale => Has(Perk.Resolve) && HealthRatio < resolveThreshold ? 1f - resolveReduction : 1f;

    /// <summary>玩家造成傷害時的額外倍率（不分目標的部分）</summary>
    public float DamageScale
    {
        get
        {
            float s = 1f;
            if (Has(Perk.Berserk) && HealthRatio < berserkThreshold) s *= berserkMultiplier;
            if (Has(Perk.FullForce) && StaminaRatio > fullForceThreshold) s *= fullForceMultiplier;
            if (Has(Perk.DemonForm) && DemonActive) s *= demonDamageMultiplier;
            return s;
        }
    }

    /// <summary>打中某個目標時的倍率：DamageScale × 越戰越勇的連擊層數（會累加層數，每次出手只呼叫一次）</summary>
    public float DamageScaleFor(IDamageable target)
    {
        float s = DamageScale;
        if (!Has(Perk.Momentum) || target == null) return s;
        int stacks = 0;
        if (momentum.TryGetValue(target, out var m) && Time.time - m.time <= momentumWindow)
            stacks = Mathf.Min(m.stacks + 1, Mathf.RoundToInt(momentumMax / momentumStep));
        momentum[target] = (Time.time, stacks);
        lastMomentumStacks = stacks;
        lastMomentumTime = Time.time;
        if (momentum.Count > 32) momentum.Clear();
        return s * (1f + stacks * momentumStep);
    }

    /// <summary>玩家打中敵人之後呼叫：低血量吸血、深淵印記</summary>
    public void OnDealtDamage(float damage, IDamageable target = null)
    {
        if (Has(Perk.Lifesteal) && health != null && HealthRatio < lifestealThreshold)
            health.Heal(damage * lifestealRatio);

        if (Has(Perk.AbyssRevenge) && target != null)
        {
            AbyssMark.Record(target, damage);
            if (Time.time >= abyssReadyAt && !target.IsDead)
            {
                AbyssMark.Apply(target, abyssDuration);
                abyssReadyAt = Time.time + abyssCooldown;
            }
        }
    }

    void Popup(string text)
    {
        popupText = text;
        popupTime = Time.time;
    }

    void OnGUI()
    {
        if (GameUI.OverlayOpen) return;

        // 血條、精力條下方的小狀態列
        float x = 20f, y = 62f;
        var style = new GUIStyle(GameUI.Small) { fontStyle = FontStyle.Bold };
        void Line(string text, Color color)
        {
            GameUI.Shadowed(new Rect(x, y, 340f, 22f), text, style, color);
            y += 22f;
        }

        if (Has(Perk.Shield))
        {
            float left = Mathf.Max(0f, shieldReadyAt - Time.time);
            Line(shieldReady ? "◆ 榮耀加身：護盾就緒" : $"◇ 榮耀加身：護盾充能 {left:0.0}s",
                 shieldReady ? new Color(0.5f, 0.85f, 1f) : GameUI.MutedColor);
        }
        if (Has(Perk.Berserk) && HealthRatio < berserkThreshold)
            Line($"浴血奮鬥：攻擊 ×{berserkMultiplier:0.0#}", GameUI.Danger);
        if (Has(Perk.Lifesteal) && HealthRatio < lifestealThreshold)
            Line($"嗜血狂妄：吸血 {lifestealRatio * 100f:0}%", new Color(1f, 0.4f, 0.6f));
        if (Has(Perk.FullForce) && StaminaRatio > fullForceThreshold)
            Line($"滿盈痛擊：攻擊 ×{fullForceMultiplier:0.0#}", PlayerItems.StaminaColor);
        if (Has(Perk.Momentum) && lastMomentumStacks > 0 && Time.time - lastMomentumTime <= momentumWindow)
            Line($"越戰越勇：攻擊 +{lastMomentumStacks * momentumStep * 100f:0}%", GameUI.Accent);
        if (Has(Perk.AbyssRevenge))
        {
            float left = abyssReadyAt - Time.time;
            Line(left <= 0f ? "◆ 深淵復仇：就緒" : $"◇ 深淵復仇：冷卻 {left:0.0}s",
                 left <= 0f ? AbyssMark.MarkColor : GameUI.MutedColor);
        }
        if (Has(Perk.Prayer))
            Line($"治癒之祈：{Mathf.Max(0f, prayerNextAt - Time.time):0}s 後回復 {prayerHeal:0}", GameUI.MutedColor);
        if (Has(Perk.Resolve) && HealthRatio < resolveThreshold)
            Line($"奮勇抵抗：減傷 {resolveReduction * 100f:0}%", new Color(0.5f, 0.85f, 1f));
        if (Has(Perk.DemonForm))
        {
            if (DemonActive) Line($"◆ 惡魔型態：無敵 {demonUntil - Time.time:0.0}s", GameUI.Danger);
            else Line($"◇ 惡魔型態：{Mathf.Max(0f, demonNextAt - Time.time):0}s", GameUI.MutedColor);
        }

        DrawPerkList();

        float since = Time.time - popupTime;
        if (since < 0.8f) GameUI.Banner(popupText, new Color(0.7f, 0.95f, 1f), 1f - since / 0.8f);
        GUI.color = Color.white;
    }

    /// <summary>右上角（小地圖下方）：已獲得的能力，簡短文字</summary>
    void DrawPerkList()
    {
        if (string.IsNullOrEmpty(BasicUpgrade) && perks.Count == 0) return;

        var rows = new List<(string text, Color color)>();
        if (!string.IsNullOrEmpty(BasicUpgrade)) rows.Add(($"{PathName(UpgradePath)}之種　{BasicUpgrade}", GameUI.Good));
        foreach (var p in perks)
        {
            var o = Describe(p);
            rows.Add(($"{o.title}　{o.brief}", GameUI.TextColor));
        }

        // 小地圖是 1920x1080 參考解析度、寬高各半縮放的 Canvas，右上角約 340 像素高
        float uiScale = Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));
        const float w = 320f, rowH = 22f, margin = 24f;
        var panel = new Rect(Screen.width - w - margin, 340f * uiScale + 8f, w, 36f + rows.Count * rowH);
        GameUI.Panel(panel);

        var title = new GUIStyle(GameUI.Small) { fontStyle = FontStyle.Bold };
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(panel.x + 12f, panel.y + 6f, w - 24f, 24f), "已獲得能力", title);
        float y = panel.y + 30f;
        foreach (var (text, color) in rows)
        {
            GUI.color = color;
            GUI.Label(new Rect(panel.x + 12f, y, w - 24f, rowH), text, GameUI.Small);
            y += rowH;
        }
        GUI.color = Color.white;
    }

    static string PathName(Path p) => p == Path.Speed ? "疾風" : p == Path.Power ? "力量" : p == Path.Health ? "生命" : "能力";

    // ---------- 第二株能力植物的選項 ----------

    public struct Option
    {
        public Perk perk;
        public string title;
        /// <summary>選單卡片上的說明（可以換行）</summary>
        public string detail;
        /// <summary>右上角清單用的一行簡短說明</summary>
        public string brief;
    }

    static Option O(Perk perk, string title, string detail, string brief) =>
        new Option { perk = perk, title = title, detail = detail, brief = brief };

    /// <summary>某條路線全部 6 個進階能力（第二株植物從中隨機抽 3 個）</summary>
    public static Option[] OptionsFor(Path path)
    {
        switch (path)
        {
            case Path.Speed:
                return new[]
                {
                    O(Perk.SpeedUp,       "輕盈腳步", "跑速 +15%", "跑速 +15%"),
                    O(Perk.HighJump,      "一步登天", "空中再按空白鍵\n可以二段跳", "二段跳"),
                    O(Perk.Dodge,         "疾行奔走", "10% 機率\n閃避攻擊", "10% 閃避"),
                    O(Perk.QuickRecovery, "迅返戰場", "翻滾後 1 秒內\n跑速 +40%", "翻滾後加速 40%"),
                    O(Perk.Tailwind,      "乘風啟航", "衝刺速度 +25%", "衝刺 +25%"),
                    O(Perk.SecondWind,    "喘息不止", "精力回復\n每秒 20% → 30%", "精力回復 30%/s"),
                };
            case Path.Power:
                return new[]
                {
                    O(Perk.PowerUp,      "鋒利劍芒", "攻擊力 +10", "攻擊 +10"),
                    O(Perk.HeavyAttack,  "巧匠天工", "長按左鍵蓄力\n重擊傷害 ×2", "蓄力重擊 ×2"),
                    O(Perk.Berserk,      "浴血奮鬥", "血量低於 30%\n攻擊力 ×1.4", "低血攻擊 ×1.4"),
                    O(Perk.FullForce,    "滿盈痛擊", "精力高於 75%\n攻擊力 +20%", "高精力攻擊 +20%"),
                    O(Perk.Momentum,     "越戰越勇", "0.5 秒內連續攻擊\n同一目標每下 +10%\n最多 +50%", "連擊 +10%～50%"),
                    O(Perk.AbyssRevenge, "深淵復仇", "命中時標記敵人 2 秒\n結束時期間傷害\n再打一次（冷卻 20 秒）", "標記傷害重現"),
                };
            case Path.Health:
                return new[]
                {
                    O(Perk.HealthUp,  "強健體魄", "最大生命 +40", "生命 +40"),
                    O(Perk.Shield,    "榮耀加身", "每 12 秒獲得\n一層護盾", "12 秒護盾"),
                    O(Perk.Lifesteal, "嗜血狂妄", "血量低於 40%\n攻擊吸血 10%", "低血吸血 10%"),
                    O(Perk.Prayer,    "治癒之祈", "每 15 秒\n回復 10 點生命", "每 15 秒 +10 HP"),
                    O(Perk.Resolve,   "奮勇抵抗", "血量低於 60%\n受到傷害 -15%", "低血減傷 15%"),
                    O(Perk.DemonForm, "惡魔型態", "生命上限變成 30%\n每 20 秒無敵 3 秒\n期間跑速 +30%、攻擊 ×1.5", "每 20 秒無敵 3 秒"),
                };
            default:
                return new Option[0];
        }
    }

    /// <summary>從路線的 6 個能力裡隨機抽 count 個（不重複、已經有的不抽）</summary>
    public Option[] RollOptions(Path path, int count = 3)
    {
        var pool = new List<Option>();
        foreach (var o in OptionsFor(path)) if (!Has(o.perk)) pool.Add(o);
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }
        if (pool.Count > count) pool.RemoveRange(count, pool.Count - count);
        return pool.ToArray();
    }

    static Option Describe(Perk perk)
    {
        foreach (var path in new[] { Path.Speed, Path.Power, Path.Health })
            foreach (var o in OptionsFor(path))
                if (o.perk == perk) return o;
        return new Option { perk = perk, title = perk.ToString(), detail = "", brief = "" };
    }

    /// <summary>選了第二株植物的某個能力：數值型的直接加上去，並記錄下來。回傳顯示用的說明</summary>
    public string Grant(Perk perk)
    {
        if (Has(perk)) return "";
        switch (perk)
        {
            case Perk.SpeedUp:
                if (ctrl != null) { ctrl.walkSpeed *= 1.15f; ctrl.sprintSpeed *= 1.15f; }
                break;
            case Perk.Tailwind:
                if (ctrl != null) ctrl.sprintSpeed *= 1f + tailwindBonus;
                break;
            case Perk.PowerUp:
            {
                var combat = GetComponent<PlayerCombat>();
                if (combat != null && combat.comboDamage != null)
                    for (int i = 0; i < combat.comboDamage.Length; i++) combat.comboDamage[i] += powerUpDamage;
                break;
            }
            case Perk.HealthUp:
                if (health != null) health.IncreaseMaxHealth(healthUpAmount);
                break;
            case Perk.DemonForm:
                if (health != null)
                {
                    float max = Mathf.Max(1f, health.maxHealth * demonHealthRatio);
                    health.SetHealth(max, Mathf.Min(health.Health, max));
                }
                break;
        }
        Add(perk);
        var o = Describe(perk);
        return $"{o.title}：{o.detail.Replace("\n", "")}";
    }
}
