using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 自動存檔（不用在場景裡放東西，自動建立、跨場景存在）：
///   - 跟村民 / 商人說完話（MerchantNPC 對話、商店關掉）→「和村長說完話」
///   - 劇情對話播完（StoryDialogue）→「和○○說完話」
///   - 一場戰鬥結束（CombatLog.Recorded：勝利 / 敗北 / 脫離；木人、過招這種練習不算）→「打倒了雙尾虎」「敗給了道場老師父」
/// 都存到主畫面選的那一個存檔欄（GameManager.SaveGame，記下這次存檔的原因，主畫面選存檔時顯示「上次存檔」）。
/// 等到可以安全存的時候才存：沒在載入、沒開選單、沒在對話 / 戰鬥 / 過場、玩家活著；中間換了場景就不存（進場景本來就會自動存）。
/// 兩次自動存檔至少隔幾秒（連續觸發時只存最後一個原因）。存好時右下角淡淡顯示「已自動存檔」。
/// 進場景的自動存檔、暫停選單的手動存檔照舊（GameManager）。
/// </summary>
public class AutoSave : MonoBehaviour
{
    const float Delay = 0.6f;        // 觸發後等一下再存（對話的結果，例如給東西、開門，先做完）
    const float MinInterval = 4f;    // 兩次自動存檔至少隔幾秒

    static AutoSave host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("AutoSave").AddComponent<AutoSave>();
        DontDestroyOnLoad(host.gameObject);
    }

    readonly List<MerchantNPC> npcs = new List<MerchantNPC>();
    float npcsRefreshedAt = -10f;
    MerchantNPC talking;
    bool storyWas;
    string storyOther;

    string pending, pendingScene;
    float pendingAt, lastSaveAt = -100f;
    string shownReason;
    float shownAt = -100f;
    GUIStyle style;

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        CombatLog.Recorded += OnFightRecorded;
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        CombatLog.Recorded -= OnFightRecorded;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        npcs.Clear();
        npcsRefreshedAt = -10f;
        talking = null;
        storyWas = false;
        pending = null;   // 換了場景：進場景本來就會存
    }

    /// <summary>要求存檔（等到可以安全存的時候）；連續要求的話用最後一個原因</summary>
    public static void Request(string reason)
    {
        if (host == null || string.IsNullOrEmpty(reason)) return;
        host.pending = reason;
        host.pendingScene = SceneManager.GetActiveScene().name;
        host.pendingAt = Time.unscaledTime + Delay;
    }

    void OnFightRecorded(CombatRecord r)
    {
        if (r == null || r.training || r.result == CombatLog.Practice) return;
        string who = string.IsNullOrEmpty(r.opponent) ? null : r.opponent;
        string reason = r.result == CombatLog.Win ? (who != null ? $"打倒了{who}" : "打贏了一場戰鬥")
                      : r.result == CombatLog.Loss ? (who != null ? $"敗給了{who}" : "倒下了")
                      : (who != null ? $"和{who}的戰鬥結束" : "戰鬥結束");
        Request(reason);
    }

    void Update()
    {
        // 村民 / 商人的對話：對話（含商店、選項）結束的那一刻
        if (Time.unscaledTime - npcsRefreshedAt > 1f)
        {
            npcsRefreshedAt = Time.unscaledTime;
            npcs.Clear();
            npcs.AddRange(FindObjectsByType<MerchantNPC>(FindObjectsInactive.Exclude));
        }
        MerchantNPC now = null;
        foreach (var n in npcs)
            if (n != null && n.InConversation) { now = n; break; }
        if (talking != null && now == null) Request($"和{NameOf(talking)}說完話");
        talking = now;

        // 劇情對話：記下對方是誰（右邊的人），播完的那一刻
        bool story = StoryDialogue.Active;
        if (story)
        {
            var (name, right) = StoryDialogue.CurrentLine;
            if (right && !string.IsNullOrEmpty(name)) storyOther = name;
        }
        else if (storyWas)
        {
            Request(!string.IsNullOrEmpty(storyOther) ? $"和{storyOther}說完話" : "劇情對話結束");
            storyOther = null;
        }
        storyWas = story;

        if (pending != null && Time.unscaledTime >= pendingAt && Time.unscaledTime - lastSaveAt >= MinInterval) TrySave();
    }

    static string NameOf(MerchantNPC n) => !string.IsNullOrEmpty(n.speakerName) ? n.speakerName : n.name;

    void TrySave()
    {
        var gm = GameManager.Instance;
        string scene = SceneManager.GetActiveScene().name;
        if (scene != pendingScene) { pending = null; return; }
        if (gm == null || SaveSystem.ActiveSlot < 0 || gm.FindLevel(scene) == null) { pending = null; return; }   // 沒選存檔欄、不是可以存檔的關卡
        // 還不能存：等一下再試
        if (gm.IsLoading || gm.IsOverlayOpen || gm.IsGameOver || gm.IsGameWon || Time.timeScale <= 0f) return;
        if (StoryDialogue.Active || talking != null || VillageOpening.Active || PaintingSlideshow.Active || CombatLog.InFight) return;
        var hp = PlayerHealth.Find();
        if (hp == null || hp.IsDead) return;

        string reason = pending;
        pending = null;
        lastSaveAt = Time.unscaledTime;
        if (gm.SaveGame(reason))
        {
            shownReason = reason;
            shownAt = Time.unscaledTime;
        }
    }

    /// <summary>右下角淡淡的「已自動存檔　和村長說完話」</summary>
    void OnGUI()
    {
        float since = Time.unscaledTime - shownAt;
        if (since > 2.6f || string.IsNullOrEmpty(shownReason) || GameUI.OverlayOpen) return;
        GUI.depth = -900;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontSize = 18 };
            if (MenuSkin.Font != null) style.font = MenuSkin.Font;
        }
        float a = Mathf.Clamp01(since / 0.25f) * Mathf.Clamp01((2.6f - since) / 0.6f);
        string text = $"已自動存檔　{shownReason}";
        var r = new Rect(0f, Screen.height - 64f, Screen.width - 36f, 26f);
        style.normal.textColor = new Color(0f, 0f, 0f, 0.6f * a);
        GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style);
        style.normal.textColor = new Color(1f, 0.94f, 0.82f, 0.9f * a);
        GUI.Label(r, text, style);
    }
}
