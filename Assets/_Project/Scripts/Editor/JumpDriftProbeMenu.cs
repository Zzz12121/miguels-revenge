using UnityEditor;
using UnityEngine;

/// <summary>選單 GeminiGrowUp > 測試 > 記錄跳躍時模型的位移（Play 中才能用）：在玩家身上掛 JumpDriftProbe，自動跳 8 次並記錄</summary>
public static class JumpDriftProbeMenu
{
    [MenuItem("GeminiGrowUp/測試/記錄跳躍時模型的位移（Play 中執行）")]
    static void Run()
    {
        if (!EditorApplication.isPlaying) { EditorUtility.DisplayDialog("跳躍位移測試", "請先按 Play，站在平地上再執行。", "OK"); return; }
        ThirdPersonController c = null;
        foreach (var x in Object.FindObjectsByType<ThirdPersonController>(FindObjectsSortMode.None))
            if (!x.aiControlled && (c == null || x.GetComponent<PlayerHealth>() != null)) c = x; // 玩家（不是 AI 控制的小怪）
        if (c == null) { EditorUtility.DisplayDialog("跳躍位移測試", "場景裡找不到玩家（ThirdPersonController）。", "OK"); return; }
        var old = c.GetComponent<JumpDriftProbe>();
        if (old != null) Object.Destroy(old);
        c.gameObject.AddComponent<JumpDriftProbe>();
        Debug.Log("[JumpDriftProbe] 開始：會自動原地跳 8 次（約 25 秒），期間請不要按鍵盤滑鼠。");
    }
}
