#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 整理專案（第一層改成第二層、合併資料夾、刪除範例）
///
/// 1. Floor01.unity 改名成 Floor02.unity（Boss 關改成第二關，第一關留給之後的操作教學）
/// 2. Assets/Prefabs 搬到 Assets/_Project/Prefabs（所有自己做的東西都集中在 _Project 底下）
/// 3. 刪掉 Unity 範本附的東西：Assets/Scenes（SampleScene）、Assets/TutorialInfo、Assets/Readme.asset
///    （刪除的檔案會進資源回收筒，需要的話還找得回來）
/// 4. Build Settings 拿掉 SampleScene，Floor02 排第一個
///
/// 用 Unity 的 AssetDatabase 搬移，所有 Prefab / 場景之間的連結都不會斷。可以重複執行。
/// </summary>
public static class ProjectCleanup
{
    const string OldScene = "Assets/_Project/Scenes/Floor01.unity";
    const string NewScene = "Assets/_Project/Scenes/Floor02.unity";
    const string OldPrefabDir = "Assets/Prefabs";
    const string NewPrefabDir = "Assets/_Project/Prefabs";
    static readonly string[] ToDelete = { "Assets/Scenes", "Assets/TutorialInfo", "Assets/Readme.asset" };

    [MenuItem("GeminiGrowUp/整理專案（第一層改成第二層、合併資料夾、刪除範例）")]
    public static void Run()
    {
        if (!EditorUtility.DisplayDialog("整理專案",
                "會做這些事：\n" +
                "・Floor01 場景改名成 Floor02\n" +
                "・Assets/Prefabs 搬到 Assets/_Project/Prefabs\n" +
                "・刪除 SampleScene、TutorialInfo、Readme（進資源回收筒）\n" +
                "・Build Settings 拿掉 SampleScene\n\n" +
                "建議先 commit。要繼續嗎？", "繼續", "取消"))
            return;

        // 先存檔，並換到一個空場景，避免搬動 / 刪除正在開著的場景
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var log = new List<string>();

        // 1. 場景改名
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(OldScene) != null)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NewScene) != null)
                log.Add("⚠ Floor02.unity 已經存在，Floor01 沒有改名");
            else
            {
                string err = AssetDatabase.RenameAsset(OldScene, "Floor02");
                log.Add(string.IsNullOrEmpty(err) ? "Floor01 → Floor02" : "⚠ 場景改名失敗：" + err);
            }
        }

        // 2. Prefab 資料夾
        if (AssetDatabase.IsValidFolder(OldPrefabDir))
        {
            if (!AssetDatabase.IsValidFolder(NewPrefabDir))
            {
                string err = AssetDatabase.MoveAsset(OldPrefabDir, NewPrefabDir);
                log.Add(string.IsNullOrEmpty(err) ? "Assets/Prefabs → Assets/_Project/Prefabs" : "⚠ Prefab 資料夾搬移失敗：" + err);
            }
            else
            {
                // 目標資料夾已經有了：一個一個搬
                foreach (string guid in AssetDatabase.FindAssets("", new[] { OldPrefabDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    string target = NewPrefabDir + "/" + System.IO.Path.GetFileName(path);
                    string err = AssetDatabase.MoveAsset(path, target);
                    if (!string.IsNullOrEmpty(err)) log.Add($"⚠ {path} 搬移失敗：{err}");
                }
                if (AssetDatabase.FindAssets("", new[] { OldPrefabDir }).Length == 0)
                    AssetDatabase.DeleteAsset(OldPrefabDir);
                log.Add("Assets/Prefabs 裡的檔案已搬到 Assets/_Project/Prefabs");
            }
        }

        // 3. 刪除範本附的東西
        foreach (string path in ToDelete)
        {
            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadMainAssetAtPath(path) == null) continue;
            log.Add(AssetDatabase.MoveAssetToTrash(path) ? "已刪除 " + path : "⚠ 刪不掉 " + path);
        }

        // 4. Build Settings：拿掉不存在的場景，Floor02 排第一
        var scenes = EditorBuildSettings.scenes
            .Select(s => new EditorBuildSettingsScene(AssetDatabase.GUIDToAssetPath(s.guid.ToString()), s.enabled))
            .Where(s => !string.IsNullOrEmpty(s.path) && AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) != null)
            .GroupBy(s => s.path).Select(g => g.First())
            .OrderBy(s => s.path == NewScene ? 0 : 1)
            .ToList();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NewScene) != null && !scenes.Any(s => s.path == NewScene))
            scenes.Insert(0, new EditorBuildSettingsScene(NewScene, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        log.Add("Build Settings：" + string.Join("、", scenes.Select(s => System.IO.Path.GetFileNameWithoutExtension(s.path))));

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(NewScene) != null)
            EditorSceneManager.OpenScene(NewScene);

        Debug.Log("[GeminiGrowUp] 專案整理完成：\n" + string.Join("\n", log));
        EditorUtility.DisplayDialog("整理完成", string.Join("\n", log), "OK");
    }
}
#endif
