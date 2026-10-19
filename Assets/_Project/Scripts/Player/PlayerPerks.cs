using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家的能力路線與特殊能力（能力植物給的）。
/// 第一株能力植物（蜘蛛房）決定路線：跑速 / 力量 / 血量；
/// 第二株（Boss 房火堆旁）依路線給三選一的進階能力：
///   跑速：輕盈腳步（跑速 +10%）/ 一步登天（二段跳）/ 疾行奔走（5% 閃避）
///   力量：鋒利劍芒（攻擊 +10）/ 巧匠天工（長按左鍵重擊 ×1.5）/ 浴血奮鬥（血量低於 30% 攻擊 ×1.4）
///   血量：強健體魄（生命 +20）/ 榮耀加身（每 10 秒一層護盾）/ 嗜血狂妄（血量低於 40% 攻擊吸血 10%）
/// 數值型的能力（跑速、攻擊、生命）直接改在對應元件上；這裡記錄拿過哪些能力，並處理需要持續判斷的能力。
/// 已獲得的能力會以簡短文字顯示在畫面右上（小地圖下方）。
/// 掛在有 PlayerHealth 的玩家物件上（沒有的話 PlayerPerks.For 會自動加上），會跟著 PlayerState 跨關卡 / 存檔。
/// </summary>
public class PlayerPerks : MonoBehaviour
{
    public enum Path { None = 0, Speed = 1, Power = 2, Health = 3 }

    public enum Perk
    {
        SpeedUp, HighJump, Dodge,
        PowerUp, HeavyAttack, Berserk,
        HealthUp, Shield, Lifesteal,
    }

    [Header("數值")]
    [Range(0f, 1f)] public float dodgeChance = 0.05f;
    [Tooltip("護盾用掉之後多久再長出來 (秒)")]
    public float shieldInterval = 10f;
    [Tooltip("血量比例低於這個值時攻擊力上升")]
    public float berserkThreshold = 0.3f;
    public float berserkMultiplier = 1.4f;
    [Tooltip("血量比例低於這個值時攻擊會吸血")]
    public float lifestealThreshold = 0.4f;
    [Tooltip("吸血比例（造成傷害的幾成）")]
    public float lifestealRatio = 0.1f;

    /// <summary>第一株能力植物選的路線</summary>
    public Path UpgradePath { get; set; }
    /// <summary>第一株能力植物給的效果（顯示用，例如「跑速 +15%」）</summary>
    public string BasicUpgrade { get; set; }
    // 依照拿到的順序（右上角的清單照這個順序顯示）
    readonly List<Perk> perks = new List<Perk>();

    PlayerHealth health;
    bool shieldReady;
    float shieldReadyAt;
    float popupTime = float.NegativeInfinity;
    string popupText;

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
    }

    public bool Has(Perk perk) => perks.Contains(perk);

    public void Add(Perk perk)
    {
        if (Has(perk)) return;
        perks.Add(perk);
        if (perk == Perk.Shield) shieldReady = true;
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
        ApplyMovementPerks();
    }

    void ApplyMovementPerks()
    {
        var ctrl = GetComponent<ThirdPersonController>();
        if (ctrl != null) ctrl.doubleJumpEnabled = Has(Perk.HighJump);
        var combat = GetComponent<PlayerCombat>();
        if (combat != null) combat.heavyAttackEnabled = Has(Perk.HeavyAttack);
    }

    void Start() => ApplyMovementPerks();

    void Update()
    {
        if (Has(Perk.Shield) && !shieldReady && Time.time >= shieldReadyAt)
        {
            shieldReady = true;
            GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.5f, 1.4f, 0f, 0.3f);
        }
    }

    float HealthRatio => health != null && health.maxHealth > 0f ? health.Health / health.maxHealth : 1f;

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

    /// <summary>玩家造成傷害時的額外倍率（低血量狂暴）</summary>
    public float DamageScale => Has(Perk.Berserk) && HealthRatio < berserkThreshold ? berserkMultiplier : 1f;

    /// <summary>玩家打中敵人之後呼叫：低血量吸血</summary>
    public void OnDealtDamage(float damage)
    {
        if (!Has(Perk.Lifesteal) || health == null || HealthRatio >= lifestealThreshold) return;
        health.Heal(damage * lifestealRatio);
    }

    void Popup(string text)
    {
        popupText = text;
        popupTime = Time.time;
    }

    void OnGUI()
    {
        if (GameUI.OverlayOpen) return;

        // 血條下方的小狀態列
        float x = 20f, y = 46f;
        var style = new GUIStyle(GameUI.Small) { fontStyle = FontStyle.Bold };
        if (Has(Perk.Shield))
        {
            float left = Mathf.Max(0f, shieldReadyAt - Time.time);
            GameUI.Shadowed(new Rect(x, y, 300f, 22f), shieldReady ? "◆ 榮耀加身：護盾就緒" : $"◇ 榮耀加身：護盾充能 {left:0.0}s", style,
                            shieldReady ? new Color(0.5f, 0.85f, 1f) : GameUI.MutedColor);
            y += 22f;
        }
        if (Has(Perk.Berserk) && HealthRatio < berserkThreshold)
        {
            GameUI.Shadowed(new Rect(x, y, 300f, 22f), $"浴血奮鬥：攻擊 ×{berserkMultiplier:0.0#}", style, GameUI.Danger);
            y += 22f;
        }
        if (Has(Perk.Lifesteal) && HealthRatio < lifestealThreshold)
            GameUI.Shadowed(new Rect(x, y, 300f, 22f), $"嗜血狂妄：吸血 {lifestealRatio * 100f:0}%", style, new Color(1f, 0.4f, 0.6f));

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
            rows.Add(($"{o.title}　{o.detail.Replace("\n", "")}", GameUI.TextColor));
        }

        // 小地圖是 1920x1080 參考解析度、寬高各半縮放的 Canvas，右上角約 340 像素高
        float uiScale = Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f));
        const float w = 300f, rowH = 22f, margin = 24f;
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
        public string detail;
    }

    public static Option[] OptionsFor(Path path)
    {
        switch (path)
        {
            case Path.Speed:
                return new[]
                {
                    new Option { perk = Perk.SpeedUp,  title = "輕盈腳步", detail = "跑速 +10%" },
                    new Option { perk = Perk.HighJump, title = "一步登天", detail = "空中再按空白鍵\n可以二段跳" },
                    new Option { perk = Perk.Dodge,    title = "疾行奔走", detail = "5% 機率\n閃避攻擊" },
                };
            case Path.Power:
                return new[]
                {
                    new Option { perk = Perk.PowerUp,     title = "鋒利劍芒", detail = "攻擊力 +10" },
                    new Option { perk = Perk.HeavyAttack, title = "巧匠天工", detail = "長按左鍵蓄力\n重擊傷害 ×1.5" },
                    new Option { perk = Perk.Berserk,     title = "浴血奮鬥", detail = "血量低於 30%\n攻擊力 ×1.4" },
                };
            case Path.Health:
                return new[]
                {
                    new Option { perk = Perk.HealthUp,  title = "強健體魄", detail = "最大生命 +20" },
                    new Option { perk = Perk.Shield,    title = "榮耀加身", detail = "每 10 秒獲得\n一層護盾" },
                    new Option { perk = Perk.Lifesteal, title = "嗜血狂妄", detail = "血量低於 40%\n攻擊吸血 10%" },
                };
            default:
                return new Option[0];
        }
    }

    static Option Describe(Perk perk)
    {
        foreach (var path in new[] { Path.Speed, Path.Power, Path.Health })
            foreach (var o in OptionsFor(path))
                if (o.perk == perk) return o;
        return new Option { perk = perk, title = perk.ToString(), detail = "" };
    }

    /// <summary>選了第二株植物的某個能力：數值型的直接加上去，並記錄下來。回傳顯示用的說明</summary>
    public string Grant(Perk perk)
    {
        if (Has(perk)) return "";
        switch (perk)
        {
            case Perk.SpeedUp:
            {
                var ctrl = GetComponent<ThirdPersonController>();
                if (ctrl != null) { ctrl.walkSpeed *= 1.1f; ctrl.sprintSpeed *= 1.1f; }
                break;
            }
            case Perk.PowerUp:
            {
                var combat = GetComponent<PlayerCombat>();
                if (combat != null && combat.comboDamage != null)
                    for (int i = 0; i < combat.comboDamage.Length; i++) combat.comboDamage[i] += 10f;
                break;
            }
            case Perk.HealthUp:
                if (health != null) health.IncreaseMaxHealth(20f);
                break;
        }
        Add(perk);
        var o = Describe(perk);
        return $"{o.title}：{o.detail.Replace("\n", "")}";
    }
}
