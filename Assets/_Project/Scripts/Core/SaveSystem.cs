using System.IO;
using UnityEngine;

/// <summary>存檔內容：在哪一關、剩幾條命、遊玩時間、玩家狀態（血量 / 升級 / 背包）</summary>
[System.Serializable]
public class SaveData
{
    public int version = 1;
    public string sceneName;
    public int lives;
    public float playTime;
    public string savedAt;
    public PlayerState player;
}

/// <summary>
/// 存檔讀寫（JSON 檔，放在 Application.persistentDataPath/save.json）。
/// 讀檔時會從存檔那一關的起點開始，帶著存檔時的血量、能力和背包。
/// </summary>
public static class SaveSystem
{
    const string FileName = "save.json";

    public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);
    public static bool HasSave => File.Exists(FilePath);

    public static bool Save(SaveData data)
    {
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

    /// <summary>讀取存檔；沒有存檔或檔案壞掉回傳 null</summary>
    public static SaveData Load()
    {
        if (!HasSave) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(FilePath));
            return data != null && !string.IsNullOrEmpty(data.sceneName) ? data : null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[SaveSystem] 存檔讀取失敗：{e.Message}");
            return null;
        }
    }

    public static void Delete()
    {
        if (HasSave) File.Delete(FilePath);
    }
}
