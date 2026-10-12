#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 安裝波次戰（Boss 房）
///
/// 把 ArenaWaves 元件加到 BossRoom.prefab 的大門（BossGate）上，並指定小怪 Prefab。
/// 不執行也能玩（遊戲開始時會自動加上，並拿場景裡的小怪當範本），
/// 但執行之後才能在 Prefab 的 Inspector 裡調整波次的數值並存下來。
/// 已經裝過再執行：只會補上沒填的欄位，不會動你調過的數值。
/// </summary>
public static class WavesInstaller
{
    const string GruntPath = "Assets/_Project/Prefabs/Enemy_Grunt.prefab";
    const string AmbusherPath = "Assets/_Project/Prefabs/Enemy_Ambusher.prefab";

    [MenuItem("GeminiGrowUp/安裝波次戰（Boss 房）")]
    public static void Install()
    {
        string path = Floor02Builder.FindBossRoomPrefab();
        if (path == null)
        {
            EditorUtility.DisplayDialog("找不到 BossRoom", "專案裡找不到名為 BossRoom 的 Prefab。", "OK");
            return;
        }

        var grunt = AssetDatabase.LoadAssetAtPath<GameObject>(GruntPath);
        var ambusher = AssetDatabase.LoadAssetAtPath<GameObject>(AmbusherPath);

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var gate = root.GetComponentInChildren<BossGate>(true);
            if (gate == null)
            {
                EditorUtility.DisplayDialog("找不到大門", "BossRoom 裡沒有 BossGate，沒有修改。", "OK");
                return;
            }

            var waves = gate.GetComponent<ArenaWaves>();
            if (waves == null) waves = gate.gameObject.AddComponent<ArenaWaves>();
            if (waves.boss == null) waves.boss = root.GetComponentInChildren<BossController>(true);
            if (waves.arena == null) waves.arena = root.GetComponentInChildren<ArenaFloor>(true);
            if (waves.gruntPrefab == null) waves.gruntPrefab = grunt;
            if (waves.ambusherPrefab == null) waves.ambusherPrefab = ambusher != null ? ambusher : grunt;

            gate.wavesBeforeBoss = true;
            gate.waves = waves;

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        string note = grunt == null ? "\n\n⚠ 找不到 Enemy_Grunt.prefab，小怪欄位是空的，遊戲會改用場景裡的小怪當範本。" : "";
        Debug.Log("[GeminiGrowUp] 波次戰已安裝到 BossRoom 的大門（BossGate → ArenaWaves）。");
        EditorUtility.DisplayDialog("完成",
            "ArenaWaves 已加到 BossRoom 的 BossGate 上。\n打開 BossRoom Prefab → 選 BossGate，就能調整每一波的時間、小怪數量和切線。" + note, "OK");
    }
}
#endif
