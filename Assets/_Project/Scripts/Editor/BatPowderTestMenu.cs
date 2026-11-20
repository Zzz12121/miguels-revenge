using UnityEditor;
using UnityEngine;

/// <summary>
/// 測試石粉用（只在 Play 模式能按）：
///   在玩家旁邊放 3 顆石頭 → 走過去按互動鍵（E）撿，右下角出現「石粉 ×n」
///   模擬蝙蝠來襲提示 → 畫面中間閃「蝙蝠來了！按【F】撒石粉」，按 F 撒出去
/// </summary>
public static class BatPowderTestMenu
{
    [MenuItem("GeminiGrowUp/測試：在玩家旁邊放 3 顆石粉石頭")]
    static void SpawnStones()
    {
        var player = PlayerHealth.Find();
        if (player == null) { Debug.LogWarning("[石粉測試] 找不到玩家"); return; }
        int n = PowderStone.Scatter(player.transform.position + player.transform.forward * 3f, 3, 0.8f, 2.5f, Random.Range(1, 99999));
        Debug.Log($"[石粉測試] 放了 {n} 顆石頭（玩家前方約 3m）。目前石粉：{BatPowder.Count(player)}/{BatPowder.Max}");
    }

    [MenuItem("GeminiGrowUp/測試：模擬蝙蝠來襲提示")]
    static void WarnSwarm()
    {
        BatPowder.WarnSwarm(6f);
        var player = PlayerHealth.Find();
        Debug.Log($"[石粉測試] 蝙蝠來襲提示 6 秒。目前石粉：{BatPowder.Count(player)}（0 份時不會顯示提示）");
    }

    [MenuItem("GeminiGrowUp/測試：在玩家旁邊放 3 顆石粉石頭", true)]
    [MenuItem("GeminiGrowUp/測試：模擬蝙蝠來襲提示", true)]
    static bool PlayingOnly() => Application.isPlaying;
}
