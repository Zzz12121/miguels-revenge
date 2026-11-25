using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>存檔內容：在哪一關、剩幾條命、遊玩時間、玩家狀態（血量 / 升級 / 背包）、劇情進度</summary>
[System.Serializable]
public class SaveData
{
    public int version = 2;
    public string sceneName;
    public int lives;
    public float playTime;
    public string savedAt;
    public PlayerState player;
    /// <summary>漁村這條主線的進度（後院的門、武器、看過開場了沒、倒下幾次）；舊版存檔沒有這一段（null）</summary>
    public StoryProgress story;
    /// <summary>這次為什麼存檔（「來到漁村」「和村長說完話」「打倒了雙尾虎」「手動存檔」……），主畫面選存檔時顯示</summary>
    public string lastEvent;
}

/// <summary>
/// 漁村主線跨場景的進度（平常放在各自的 static 欄位裡，存檔時收集、讀檔時放回去）。
/// 背包裡的東西（錢、地圖、虎皮、鍛造過的武器、造型）已經在 PlayerState 的背包裡，不用另外存。
/// </summary>
[System.Serializable]
public class StoryProgress
{
    /// <summary>村長家後院通往道場的大門已經解鎖（VillageProgress.GateUnlocked）</summary>
    public bool gateUnlocked;
    /// <summary>開場（漁村的景、回憶、醒來）已經播過了</summary>
    public bool openingSeen;
    /// <summary>打倒霧海孤島的百足（看過工作人員名單）</summary>
    public bool centipedeDefeated;
    /// <summary>武器（PlayerLoadout）：手上的、切到弓之前的近戰武器、有沒有弓、剩幾支箭</summary>
    public int weapon;
    public int lastMelee;
    public bool bowUnlocked;
    public int arrows = -1;
    /// <summary>這個存檔總共倒下幾次（GameManager.OnPlayerDied 每倒下一次加一）</summary>
    public int defeats;
    /// <summary>支線任務、只發生一次的事（賣花姑娘送的草藥「flowerGift」、跑腿「errand.accepted / errand.parcel / errand.done」……）</summary>
    public List<string> flags = new List<string>();

    /// <summary>這次遊戲有沒有這個旗標</summary>
    public static bool Has(string flag) => Current.flags != null && Current.flags.Contains(flag);

    /// <summary>記下一個旗標（下一次存檔時寫進去）</summary>
    public static void Set(string flag)
    {
        if (Current.flags == null) Current.flags = new List<string>();
        if (!Current.flags.Contains(flag)) Current.flags.Add(flag);
    }

    /// <summary>這次遊戲的進度（記憶體裡的）：開新遊戲時清掉，讀檔時換成存檔的</summary>
    public static StoryProgress Current = new StoryProgress();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Current = new StoryProgress();

    /// <summary>收集現在的進度（存檔用）</summary>
    public static StoryProgress Capture()
    {
        var s = new StoryProgress
        {
            gateUnlocked = VillageProgress.GateUnlocked,
            openingSeen = Current.openingSeen,
            centipedeDefeated = Current.centipedeDefeated,
            weapon = (int)PlayerLoadout.Current,
            lastMelee = (int)PlayerLoadout.LastMelee,
            bowUnlocked = PlayerLoadout.BowUnlocked,
            arrows = PlayerLoadout.Arrows,
            defeats = Current.defeats,
            flags = Current.flags != null ? new List<string>(Current.flags) : new List<string>(),
        };
        return s;
    }

    /// <summary>放回去（讀檔）；null = 舊版存檔，當成新的進度</summary>
    public static void Apply(StoryProgress s)
    {
        if (s == null) { ResetForNewGame(); Current.openingSeen = true; return; }   // 舊版存檔：已經在玩了，不再播開場
        Current = new StoryProgress
        {
            openingSeen = s.openingSeen, centipedeDefeated = s.centipedeDefeated, defeats = s.defeats,
            flags = s.flags != null ? new List<string>(s.flags) : new List<string>(),
        };
        VillageProgress.GateUnlocked = s.gateUnlocked;
        PlayerLoadout.RestoreSaved((WeaponLoadout)s.weapon, (WeaponLoadout)s.lastMelee, s.bowUnlocked, s.arrows);
    }

    /// <summary>開新遊戲：進度全部歸零</summary>
    public static void ResetForNewGame()
    {
        Current = new StoryProgress();
        VillageProgress.GateUnlocked = false;
        PlayerLoadout.ResetForNewGame();
    }
}

/// <summary>
/// 存檔讀寫（JSON 檔，放在 Application.persistentDataPath）。有三個存檔欄位（save_1.json ~ save_3.json）：
/// 主畫面左上選一個欄位才能開始遊戲（ActiveSlot），之後自動存檔、暫停選單的存檔 / 讀檔都是這個欄位。
/// 讀檔時會從存檔那一關的起點開始，帶著存檔時的血量、能力、背包和劇情進度。
/// 舊版只有一個 save.json：第一次用到時搬進存檔一。
/// </summary>
public static class SaveSystem
{
    public const int SlotCount = 3;
    const string LegacyFileName = "save.json";

    /// <summary>現在用哪一個存檔欄位（0 ~ SlotCount-1）；-1 = 還沒選（例如編輯器裡直接從漁村按 Play），不存檔</summary>
    public static int ActiveSlot { get; set; } = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { ActiveSlot = -1; migrated = false; }

    static bool migrated;

    public static string SlotPath(int slot) => Path.Combine(Application.persistentDataPath, $"save_{slot + 1}.json");
    public static string FilePath => ActiveSlot >= 0 ? SlotPath(ActiveSlot) : null;
    public static bool HasSave => FilePath != null && File.Exists(FilePath);
    public static bool HasSlot(int slot) { Migrate(); return File.Exists(SlotPath(slot)); }

    /// <summary>舊版的 save.json 搬進存檔一（存檔一是空的才搬）</summary>
    static void Migrate()
    {
        if (migrated) return;
        migrated = true;
        try
        {
            string legacy = Path.Combine(Application.persistentDataPath, LegacyFileName);
            if (File.Exists(legacy) && !File.Exists(SlotPath(0))) File.Move(legacy, SlotPath(0));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] 舊存檔搬移失敗：{e.Message}"); }
    }

    public static bool Save(SaveData data)
    {
        if (ActiveSlot < 0) return false;
        Migrate();
        try
        {
            data.savedAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SaveSystem] 存檔失敗：{e.Message}");
            return false;
        }
    }

    /// <summary>讀取現在這個欄位的存檔；沒有存檔或檔案壞掉回傳 null</summary>
    public static SaveData Load() => ActiveSlot >= 0 ? Peek(ActiveSlot) : null;

    /// <summary>讀某一個欄位（主畫面的存檔欄顯示用）；沒有或壞掉回傳 null</summary>
    public static SaveData Peek(int slot)
    {
        if (!HasSlot(slot)) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SlotPath(slot)));
            return data != null && !string.IsNullOrEmpty(data.sceneName) ? data : null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveSystem] 存檔 {slot + 1} 讀取失敗：{e.Message}");
            return null;
        }
    }

    public static void Delete() { if (ActiveSlot >= 0) DeleteSlot(ActiveSlot); }

    public static void DeleteSlot(int slot)
    {
        try { if (HasSlot(slot)) File.Delete(SlotPath(slot)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] 刪除存檔 {slot + 1} 失敗：{e.Message}"); }
    }
}
