using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 道場場景（DojoArena_ArchitecturePreview）後院河邊的石頭只有外觀、沒有碰撞，玩家會直接穿過去。
/// 選單 GeminiGrowUp > 道場 > 補上河邊石頭的碰撞：
///   在目前開著的場景裡，找出名字有 Boulder / Rock 的石頭（LODGroup），
///   還沒有任何碰撞體的，就在 LOD0 的模型上加 MeshCollider（形狀跟石頭一樣）。
///   已經有碰撞的（例如 GardenRock、踏腳石）不會重複加。可以 Ctrl+Z 復原，加完記得存場景。
/// </summary>
public static class DojoRockColliderTool
{
    static readonly string[] RockNameKeys = { "Boulder", "Rock" };

    [MenuItem("GeminiGrowUp/道場/補上河邊石頭的碰撞")]
    static void AddRockColliders()
    {
        var scene = EditorSceneManager.GetActiveScene();
        int rocks = 0, colliders = 0, skipped = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var lod in root.GetComponentsInChildren<LODGroup>(true))
        {
            if (!IsRock(lod.transform)) continue;
            // 這顆石頭（含子物件）已經有碰撞 → 不動
            if (lod.GetComponentsInChildren<Collider>(true).Length > 0) { skipped++; continue; }
            var lods = lod.GetLODs();
            if (lods.Length == 0) continue;
            bool added = false;
            foreach (var r in lods[0].renderers)
            {
                if (r == null) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mc = Undo.AddComponent<MeshCollider>(r.gameObject);
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                colliders++; added = true;
            }
            if (added) rocks++;
        }
        if (colliders > 0) EditorSceneManager.MarkSceneDirty(scene);
        EditorUtility.DisplayDialog("道場石頭碰撞",
            $"場景「{scene.name}」：\n幫 {rocks} 顆石頭加上碰撞（共 {colliders} 個 MeshCollider）。\n原本就有碰撞、跳過：{skipped} 顆。\n\n" +
            (colliders > 0 ? "記得 Ctrl+S 存場景（不滿意可以 Ctrl+Z 復原）。" : "沒有需要補的石頭。"), "OK");
    }

    /// <summary>名字（自己或上一層）有 Boulder / Rock 才算石頭；蕨類、灌木、草不算</summary>
    static bool IsRock(Transform t)
    {
        for (int i = 0; i < 2 && t != null; i++, t = t.parent)
            if (RockNameKeys.Any(k => t.name.Contains(k))) return true;
        return false;
    }
}
