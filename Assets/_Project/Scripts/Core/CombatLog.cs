using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>一場戰鬥的紀錄（戰鬥紀錄簿裡的一行）</summary>
[System.Serializable]
public class CombatRecord
{
    public string when;      // 「11/16 21:05」
    public string place;     // 「黑衣人的道場」
    public string opponent;  // 「道場老師父」「骷髏兵 ×3」
    public string result;    // 勝利 / 敗北 / 脫離 / 練習
    public string detail;    // 敗北的原因（「被打倒了」「掉下去了」），其他時候空白
    public float seconds;
    public int hits, kills;
    public float dealt, taken;
    public bool training;

    public bool Won => result == CombatLog.Win;
    public bool Lost => result == CombatLog.Loss;
}

/// <summary>
/// 戰鬥紀錄：自動記下玩家打過的每一場架（不用在場景裡放東西，第一次載入場景就自動開始）。
///   - 開始：玩家打中敵人、或被打到
///   - 結束：7 秒都沒有再交手（對手全倒 = 勝利，不然 = 脫離）、玩家倒下（敗北）、換場景
///   - 記下：時間、地點、對手、結果、花了多久、命中幾下、造成 / 受到多少傷害、打倒幾個
/// 木樁人偶、僧侶過招算「練習」。打到火把、石頭、樹這些東西不算戰鬥。
/// 紀錄存在 Application.persistentDataPath/combat_history.json（最多留 200 場），關掉遊戲再開還在；
/// batchmode（自動測試）只記在記憶體裡，不寫檔。暫居地的戰鬥紀錄簿（CombatHistoryBook）會把它們列出來。
/// </summary>
public class CombatLog : MonoBehaviour
{
    public const string Win = "勝利", Loss = "敗北", Escape = "脫離", Practice = "練習";
    const int MaxRecords = 200;
    const float EndAfterQuiet = 7f;

    [System.Serializable]
    class Data
    {
        public List<CombatRecord> records = new List<CombatRecord>();
        public int deaths;
    }

    static Data data;
    static CombatLog host;

    /// <summary>所有紀錄（舊的在前面）</summary>
    public static IReadOnlyList<CombatRecord> Records { get { Load(); return data.records; } }
    /// <summary>總共倒下幾次</summary>
    public static int Deaths { get { Load(); return data.deaths; } }
    /// <summary>剛記下一場（參數是那一場）</summary>
    public static event System.Action<CombatRecord> Recorded;
    /// <summary>最近一次敗北（這次開遊戲以來；沒有就是 null）</summary>
    public static CombatRecord LastDefeat { get; private set; }

    static string FilePath => Path.Combine(Application.persistentDataPath, "combat_history.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { data = null; host = null; Recorded = null; LastDefeat = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("CombatLog").AddComponent<CombatLog>();
        DontDestroyOnLoad(host.gameObject);
    }

    static void Load()
    {
        if (data != null) return;
        data = new Data();
        if (Application.isBatchMode) return;
        try
        {
            if (File.Exists(FilePath)) data = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath)) ?? new Data();
        }
        catch (System.Exception e) { Debug.LogWarning($"[CombatLog] 讀不到戰鬥紀錄：{e.Message}"); data = new Data(); }
        if (data.records == null) data.records = new List<CombatRecord>();
    }

    static void Save()
    {
        if (Application.isBatchMode || data == null) return;
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(data, true)); }
        catch (System.Exception e) { Debug.LogWarning($"[CombatLog] 存不了戰鬥紀錄：{e.Message}"); }
    }

    /// <summary>清掉全部紀錄（測試用）</summary>
    public static void Clear()
    {
        Load();
        data.records.Clear();
        data.deaths = 0;
        Save();
    }

    // ---------------- 一場戰鬥進行中 ----------------

    class Opponent
    {
        public Component target;
        public string name;
        public float dealt;
        public bool killed, boss, training;
    }

    readonly Dictionary<Component, Opponent> opponents = new Dictionary<Component, Opponent>();
    bool active;
    float startTime, lastActivity, dealt, taken;
    int hits;
    PlayerHealth player;
    PlayerCombat combat;

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    void Start() => Hook();

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        // 換場景：還沒結束的那場照現在的狀況記下來
        if (active) Finish(null);
        Hook();
    }

    void Hook()
    {
        Unhook();
        player = PlayerHealth.Find();
        if (player == null) return;
        combat = player.GetComponent<PlayerCombat>();
        player.OnDamaged += OnPlayerDamaged;
        player.OnDied += OnPlayerDied;
        if (combat != null) combat.OnDealtHit += OnDealtHit;
    }

    void Unhook()
    {
        if (player != null) { player.OnDamaged -= OnPlayerDamaged; player.OnDied -= OnPlayerDied; }
        if (combat != null) combat.OnDealtHit -= OnDealtHit;
        player = null;
        combat = null;
    }

    void OnDestroy() => Unhook();

    void Begin()
    {
        if (active) return;
        active = true;
        startTime = lastActivity = Time.time;
        dealt = taken = 0f;
        hits = 0;
        opponents.Clear();
    }

    void OnDealtHit(float damage, IDamageable target)
    {
        if (target != null && Ignored(target)) return;
        Begin();
        lastActivity = Time.time;
        hits++;
        dealt += damage;
        if (target != null) Track(target, damage);
    }

    void OnPlayerDamaged(float amount)
    {
        Begin();
        lastActivity = Time.time;
        taken += amount;
        if (opponents.Count == 0) TrackNearest();
    }

    void OnPlayerDied()
    {
        Load();
        data.deaths++;
        string reason = DeathReason();
        if (!active) Begin();
        if (opponents.Count == 0) TrackNearest();
        Finish(reason);
    }

    /// <summary>箭射中了東西（箭自己會消失，PlayerCombat 不知道射中誰）：把 at 附近的目標算進這一場</summary>
    public static void RegisterArrowHit(Vector3 at)
    {
        if (host == null || !host.active) return;
        foreach (var c in Physics.OverlapSphere(at, 0.8f, ~0, QueryTriggerInteraction.Collide))
        {
            var d = c.GetComponentInParent<IDamageable>();
            if (d != null && !Ignored(d)) { host.Track(d, 0f); return; }
        }
    }

    void Track(IDamageable target, float damage)
    {
        var c = target as Component;
        if (c == null) return;
        if (!opponents.TryGetValue(c, out var o))
        {
            o = new Opponent { target = c, name = NameOf(c, out bool boss), boss = boss, training = IsTraining(c) };
            opponents[c] = o;
        }
        o.dealt += damage;
    }

    /// <summary>被打到但還沒打中任何人：最近的那個敵人大概就是對手</summary>
    void TrackNearest()
    {
        if (player == null) return;
        IDamageable best = null;
        float bestD = 25f;
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
        {
            if (!(mb is IDamageable d) || d.IsDead || Ignored(d) || mb.transform.IsChildOf(player.transform)) continue;
            float dist = Vector3.Distance(mb.transform.position, player.transform.position);
            if (dist < bestD) { bestD = dist; best = d; }
        }
        if (best != null) Track(best, 0f);
    }

    void Update()
    {
        if (!active) return;
        foreach (var o in opponents.Values)
            if (!o.killed && o.target != null && ((IDamageable)o.target).IsDead) { o.killed = true; lastActivity = Time.time; }
        if (Time.time - lastActivity > EndAfterQuiet) Finish(null);
    }

    void OnApplicationQuit() { if (active) Finish(null); }

    /// <summary>這一場結束，記下來。defeatReason 不是 null = 玩家倒下了</summary>
    void Finish(string defeatReason)
    {
        if (!active) return;
        active = false;
        bool training = opponents.Count > 0;
        int kills = 0, alive = 0;
        foreach (var o in opponents.Values)
        {
            if (!o.training) training = false;
            if (o.killed || (o.target != null && ((IDamageable)o.target).IsDead)) kills++;
            else alive++;
        }
        // 只打了一兩下、也沒被打：不算一場（例如路過時不小心砍到）
        if (defeatReason == null && hits < 2 && taken <= 0f && kills == 0) return;

        string result = defeatReason != null ? Loss : training ? Practice : opponents.Count > 0 && alive == 0 ? Win : Escape;
        var rec = new CombatRecord
        {
            when = System.DateTime.Now.ToString("MM/dd HH:mm"),
            place = PlaceName(SceneManager.GetActiveScene().name, training),
            opponent = DescribeOpponents(defeatReason),
            result = result,
            detail = defeatReason ?? "",
            seconds = Mathf.Max(0f, (defeatReason != null ? Time.time : lastActivity) - startTime),
            hits = hits,
            kills = kills,
            dealt = Mathf.Round(dealt),
            taken = Mathf.Round(taken),
            training = training,
        };
        Load();
        data.records.Add(rec);
        if (data.records.Count > MaxRecords) data.records.RemoveRange(0, data.records.Count - MaxRecords);
        Save();
        if (rec.Lost) LastDefeat = rec;
        opponents.Clear();
        Recorded?.Invoke(rec);
    }

    string DescribeOpponents(string defeatReason)
    {
        if (opponents.Count == 0) return string.IsNullOrEmpty(defeatReason) ? "不明的對手" : $"（{defeatReason}）";
        // 同名的合起來；Boss 優先，其次是打得最多的
        var groups = new Dictionary<string, (int count, float dealt, bool boss)>();
        foreach (var o in opponents.Values)
        {
            groups.TryGetValue(o.name, out var g);
            groups[o.name] = (g.count + 1, g.dealt + o.dealt, g.boss || o.boss);
        }
        string main = null;
        (int count, float dealt, bool boss) best = default;
        foreach (var kv in groups)
            if (main == null || (kv.Value.boss && !best.boss) || (kv.Value.boss == best.boss && kv.Value.dealt > best.dealt))
            { main = kv.Key; best = kv.Value; }
        string text = best.count > 1 ? $"{main} ×{best.count}" : main;
        if (groups.Count > 1) text += " 等";
        return text;
    }

    /// <summary>最近一次敗北的對手（村民安慰的時候用；沒有就是「黑衣人」）</summary>
    public static string LastDefeatOpponent
    {
        get
        {
            var o = LastDefeat?.opponent;
            if (string.IsNullOrEmpty(o) || o.StartsWith("（") || o == "不明的對手") return "黑衣人";
            int k = o.IndexOf(" ×");
            if (k > 0) o = o.Substring(0, k);
            if (o.EndsWith(" 等")) o = o.Substring(0, o.Length - 2);
            return o;
        }
    }

    // ---------------- 名稱 ----------------

    static readonly string[] IgnoredTypes = { "Knockable", "KnockableTorch", "ForestDestructibleRock", "ForestFallingTree", "ShuraBreakable" };
    static readonly string[] TrainingTypes = { "TrainingDummy", "MonkSparring", "CalligraphyPracticeTarget" };

    static bool Ignored(IDamageable d)
    {
        if (!(d is Component c) || c == null) return true;
        for (var t = c.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            if (System.Array.IndexOf(IgnoredTypes, t.Name) >= 0) return true;
        return false;
    }

    static bool IsTraining(Component c) => System.Array.IndexOf(TrainingTypes, c.GetType().Name) >= 0;

    static readonly Dictionary<string, string> TypeNames = new Dictionary<string, string>
    {
        { "TrainingDummy", "木樁人偶" }, { "MonkSparring", "僧侶（過招）" }, { "TigerBeast", "雙尾虎" },
        { "CalligraphyPracticeTarget", "練習用的墨跡" }, { "CalligraphyClone", "墨分身" }, { "CalligraphyBossCombat", "墨畫之龍" },
        { "BoatCrewMember", "敵船船員" }, { "EnemyBoat", "敵船" }, { "SandWormTail", "沙蟲" },
    };

    static readonly (string key, string name)[] ObjectNameHints =
    {
        ("Centipede", "百足駭骨"), ("Skeleton", "骷髏兵"), ("GreenEye", "綠眼惡鬼"), ("Golem", "石巨人"), ("Paladin", "聖騎士"),
        ("Archer", "弓箭手"), ("Ninja", "黑衣人"), ("Soldier", "黑衣人"), ("Viking", "維京戰士"), ("Spider", "毒蛛"), ("Clock", "時間吞噬者"),
    };

    static readonly BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>對手的名字：先找物件上的 bossName / enemyName（道場老師父、時間吞噬者…），再照類型、物件名稱猜</summary>
    static string NameOf(Component c, out bool boss)
    {
        boss = false;
        string found = null;
        foreach (var mb in c.GetComponentsInParent<MonoBehaviour>())
        {
            if (mb == null) continue;
            foreach (var field in new[] { "bossName", "enemyName", "displayName" })
            {
                var f = mb.GetType().GetField(field, AnyInstance);
                if (f == null || f.FieldType != typeof(string)) continue;
                var v = f.GetValue(mb) as string;
                if (string.IsNullOrEmpty(v)) continue;
                boss |= field == "bossName";
                // BossHealth 的預設名字（高塔守衛）讓同一個物件上別的元件（DojoMasterBoss…）的名字優先
                if (found == null || mb.GetType().Name != "BossHealth") found = v;
            }
            if (found != null && mb.GetType().Name != "BossHealth") break;
        }
        if (found != null) return found;
        if (TypeNames.TryGetValue(c.GetType().Name, out var n)) return n;
        string obj = c.gameObject.name;
        foreach (var (key, name) in ObjectNameHints)
            if (obj.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) return name;
        obj = obj.Replace("(Clone)", "").Trim();
        foreach (char ch in obj) if (ch > 0x2E80) return obj; // 物件名稱本來就是中文
        return "敵兵";
    }

    static string PlaceName(string scene, bool training)
    {
        switch (scene)
        {
            case "JapaneseVillage": return training ? "祠堂的習武場" : "漁村";
            case "DojoArena_ArchitecturePreview": return "黑衣人的道場";
            case "ForestIsland": return "霧海孤島森林";
            case "Planet": return "沙漠";
            case "The_Viking_Village": return "維京村莊";
            case "Floor02": return "地下城";
            default: return scene;
        }
    }

    static FieldInfo deathReasonField;
    string DeathReason()
    {
        if (player == null) return "被打倒了";
        deathReasonField ??= typeof(PlayerHealth).GetField("deathReason", AnyInstance);
        var r = deathReasonField?.GetValue(player) as string;
        return string.IsNullOrEmpty(r) ? "被打倒了" : r;
    }
}
