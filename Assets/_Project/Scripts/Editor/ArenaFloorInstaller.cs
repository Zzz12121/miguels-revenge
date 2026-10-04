#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 更新 Boss 房地板（不規則石板）
///
/// 修改 BossRoom.prefab：
///   1. 把地板換成不規則的石板（每次執行排列都不一樣；想固定就在 ArenaFloor 的 Layout Seed 填一個數字）
///   2. 設定岩石的材質（M_ArenaRock）。岩石本身是 Boss 戰開始時才由程式產生的，Prefab 裡看不到
///   3. 產生 Assets/_Project/Prefabs/PushableRock.prefab：可以推的石塊，拖進任何場景都能用
///
/// 石板的大小、不規則程度、斜角寬度、岩石的數量和大小，都在 BossRoom 裡 Floor 物件的 ArenaFloor 元件上調。
/// 調完石板形狀的數值後，對 ArenaFloor 按右鍵「重新產生地板」或再執行一次這個選單。
/// </summary>
public static class ArenaFloorInstaller
{
    const string RockPrefabPath = "Assets/_Project/Prefabs/PushableRock.prefab";

    [MenuItem("GeminiGrowUp/更新 Boss 房地板（不規則石板）")]
    public static void Install()
    {
        string path = Floor02Builder.FindBossRoomPrefab();
        if (path == null)
        {
            EditorUtility.DisplayDialog("找不到 BossRoom", "專案裡找不到名為 BossRoom 的 Prefab。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("更新 Boss 房地板",
                "會把 BossRoom.prefab 的地板重新產生成不規則的石板，並建立 PushableRock.prefab。\n" +
                "Boss、牆壁、大門不會被改動。建議先 commit。要繼續嗎？",
                "繼續", "取消"))
            return;

        // 岩石的材質：已經有了就沿用（保留你調過的顏色），沒有才建立
        var rockMat = FindMaterial("M_ArenaRock");
        if (rockMat == null) rockMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaRock", new Color(0.36f, 0.35f, 0.39f));

        int slabs = 0;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var floor = root.GetComponentInChildren<ArenaFloor>(true);
            if (floor == null)
            {
                Debug.LogError("[GeminiGrowUp] BossRoom 裡找不到 ArenaFloor，沒有修改。");
                return;
            }
            // 舊版的方塊只有 1m 厚；石板要比跳砸的凹陷深度（1.6m）厚，凹下去時才不會看到底下
            if (floor.tileThickness < 2.5f) floor.tileThickness = 2.5f;
            if (floor.rockMaterial == null) floor.rockMaterial = rockMat;
            floor.Generate();
            slabs = floor.transform.childCount;
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        BuildPushableRockPrefab(rockMat);
        AssetDatabase.SaveAssets();
        Debug.Log($"[GeminiGrowUp] Boss 房地板已換成 {slabs} 塊不規則石板。可以推的石塊 Prefab：{RockPrefabPath}");
    }

    static Material FindMaterial(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Material"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<Material>(p);
        }
        return null;
    }

    /// <summary>可以推的石塊：拖進場景就能用（不需要 ArenaFloor）。已經有了就不覆蓋，保留你調過的數值</summary>
    static void BuildPushableRockPrefab(Material rockMat)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(RockPrefabPath) != null) return;
        BossArenaBuilder.EnsureFolder("Assets/_Project/Prefabs");

        var go = new GameObject("PushableRock");
        try
        {
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = rockMat;
            go.AddComponent<MeshCollider>().convex = true;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var rock = go.AddComponent<ArenaRock>();
            rock.generateMesh = true;
            rock.seed = 0;
            rock.radius = 0.9f;
            rock.height = 1.4f;
            rock.spire = false;
            rock.pushable = true;
            rock.pushSpeedFactor = 0.45f;
            rock.material = rockMat;

            PrefabUtility.SaveAsPrefabAsset(go, RockPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }
}
#endif
