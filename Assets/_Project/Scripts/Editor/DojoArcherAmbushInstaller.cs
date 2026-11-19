#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 道場：吊橋山路的弓箭手埋伏
///
/// 在道場後山往吊橋的後半段山路上，放 4 名埋伏弓箭手（維京村莊敵艦的 BoatArcher 範本 + AmbushArcher），
/// 兩名躲在巨石後、兩名躲在黑松後，都在路的上坡側。老頭逃走之後玩家追上山路才會觸發（ArcherAmbushGroup）。
/// 獨立放在場景根目錄的「ArcherAmbush (→ 吊橋)」，不改後山 Prefab；重建後山時會自動重新放一次。
/// 可重複執行。
/// </summary>
public static class DojoArcherAmbushInstaller
{
    const string RootName = "ArcherAmbush (→ 吊橋)";
    const string ArcherPrefab = "Assets/_Project/Resources/Prefabs/BoatArcher.prefab";
    const string RockPrefab = "Waldemarst/FreeJapaneseGarden/Prefabs/Misc/Rocks/JG_Rock_A_01";
    const string PinePrefab = "Waldemarst/FreeJapaneseGarden/Prefabs/Trees/BlackPine/Tree_BlackPine_Big_Green_01";

    // 埋伏點在「第 7 個山路點 → 第 10 個山路點」這段（最後一段上坡、接近山頂平台與吊橋）的比例位置
    const int FromPoint = 7, ToPoint = 10;
    static readonly (float along, bool rock)[] Spots = { (0.14f, true), (0.4f, false), (0.64f, true), (0.88f, false) };
    const float CoverOffset = 7.2f, HideOffset = 9.0f, PeekStep = 2.0f;

    [MenuItem("GeminiGrowUp/道場：吊橋山路的弓箭手埋伏")]
    public static void Menu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != DojoMountainPathBuilder.DojoScenePath)
            scene = EditorSceneManager.OpenScene(DojoMountainPathBuilder.DojoScenePath, OpenSceneMode.Single);
        if (!Place()) return;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>在目前開著的道場場景放埋伏（不存檔）。找不到山路就回傳 false。</summary>
    public static bool Place()
    {
        var zone = Object.FindAnyObjectByType<TrailBoundsKillZone>(FindObjectsInactive.Include);
        if (zone == null || zone.pathPoints == null || zone.pathPoints.Length <= ToPoint)
        {
            Debug.LogWarning("[ArcherAmbush] 場景裡找不到後山步道（TrailBoundsKillZone）。先執行「道場：建立後山瀑布與山路」。");
            return false;
        }
        ArcherInstaller.EnsureLocomotionStates();
        var archerPf = AssetDatabase.LoadAssetAtPath<GameObject>(ArcherPrefab);
        if (archerPf == null) { Debug.LogWarning($"[ArcherAmbush] 找不到 {ArcherPrefab}（選單：GeminiGrowUp > 安裝敵艦弓箭手）"); return false; }
        var rockPf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{RockPrefab}.prefab");
        var pinePf = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{PinePrefab}.prefab");
        var terrain = FindTerrain();

        var old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        var group = root.AddComponent<ArcherAmbushGroup>();

        var path = new List<Vector3>();
        foreach (var p in zone.pathPoints) path.Add(zone.transform.TransformPoint(p));

        var archers = new List<AmbushArcher>();
        int n = 0;
        foreach (var (along, rock) in Spots)
        {
            PointAlong(path, along, out Vector3 p, out Vector3 dir);
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            // 上坡側；那一側太靠近另一段山路（之字形轉彎）就換邊
            float up = Ground(terrain, p + side * 8f), down = Ground(terrain, p - side * 8f);
            if (down > up) side = -side;
            if (DistToPath(path, p + side * HideOffset) < HideOffset - 1f) side = -side;

            Vector3 cover = p + side * CoverOffset;
            Vector3 hide = p + side * HideOffset;
            float stepSign = n % 2 == 0 ? 1f : -1f;
            Vector3 peek = hide + dir * (PeekStep * stepSign) - side * 0.6f;
            cover.y = Ground(terrain, cover); hide.y = Ground(terrain, hide); peek.y = Ground(terrain, peek);
            Quaternion facePath = Quaternion.LookRotation(-side);

            // 掩體
            var coverPf = rock ? rockPf : pinePf;
            if (coverPf != null)
            {
                var c = DojoMountainPathBuilder.Place(root.transform, coverPf, rock ? $"AmbushBoulder_{n + 1}" : $"AmbushPine_{n + 1}",
                                                      cover + Vector3.down * 0.25f, Quaternion.Euler(0f, 37f * n + 15f, 0f), 1f);
                if (rock) FitHeight(c, 2.5f, 3.4f);
                else c.transform.localScale *= 1.25f;
                EnsureCollider(c, rock);
            }

            // 弓箭手（Prefab 實例，掛 AmbushArcher）
            var a = (GameObject)PrefabUtility.InstantiatePrefab(archerPf);
            a.name = $"AmbushArcher_{n + 1}";
            a.transform.SetParent(root.transform, false);
            a.transform.SetPositionAndRotation(hide, facePath);
            var amb = a.GetComponent<AmbushArcher>();
            if (amb == null) amb = a.AddComponent<AmbushArcher>();
            amb.hidePoint = hide;
            amb.peekPoint = peek;
            amb.hideSeconds = 1.4f + 0.3f * n;   // 每個人的節奏錯開
            archers.Add(amb);
            n++;
        }

        group.archers = archers.ToArray();
        PointAlong(path, 0f, out Vector3 trigger, out _);
        group.triggerPoint = trigger;
        Debug.Log($"[ArcherAmbush] 已在往吊橋的山路放 {archers.Count} 名埋伏弓箭手（{RootName}）。老頭逃走後玩家走到山路第 {FromPoint} 個轉角附近就會觸發。");
        return true;
    }

    static Terrain FindTerrain()
    {
        foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Exclude))
            if (t.name == "MountainTerrain") return t;
        return Terrain.activeTerrain;
    }

    static float Ground(Terrain t, Vector3 p)
    {
        if (t == null) return p.y;
        return t.SampleHeight(p) + t.transform.position.y;
    }

    /// <summary>FromPoint → ToPoint 這段山路上，比例 along 的位置與前進方向（水平）</summary>
    static void PointAlong(List<Vector3> path, float along, out Vector3 pos, out Vector3 dir)
    {
        float total = 0f;
        for (int i = FromPoint; i < ToPoint; i++) total += Flat(path[i + 1] - path[i]).magnitude;
        float want = Mathf.Clamp01(along) * total;
        for (int i = FromPoint; i < ToPoint; i++)
        {
            float len = Flat(path[i + 1] - path[i]).magnitude;
            if (want <= len || i == ToPoint - 1)
            {
                pos = Vector3.Lerp(path[i], path[i + 1], Mathf.Clamp01(want / Mathf.Max(len, 0.001f)));
                dir = Flat(path[i + 1] - path[i]).normalized;
                return;
            }
            want -= len;
        }
        pos = path[FromPoint]; dir = Vector3.forward;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static float DistToPath(List<Vector3> path, Vector3 p)
    {
        float best = float.MaxValue;
        var q = new Vector2(p.x, p.z);
        for (int i = 0; i < path.Count - 1; i++)
        {
            var a = new Vector2(path[i].x, path[i].z); var ab = new Vector2(path[i + 1].x, path[i + 1].z) - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            best = Mathf.Min(best, Vector2.Distance(q, a + ab * t));
        }
        return best;
    }

    static void FitHeight(GameObject go, float minH, float maxH)
    {
        bool has = false; var b = new Bounds();
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        if (!has || b.size.y < 0.01f) return;
        float target = Mathf.Clamp(b.size.y, minH, maxH);
        float k = target / b.size.y;
        var basePos = go.transform.position;
        go.transform.localScale *= k;
        go.transform.position = basePos;
    }

    /// <summary>掩體要擋得住箭和玩家：沒有碰撞體就補一個</summary>
    static void EnsureCollider(GameObject go, bool rock)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>()) if (!c.isTrigger) return;
        if (rock)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null) { mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh; break; }
        }
        else
        {
            var cap = go.AddComponent<CapsuleCollider>();
            float s = Mathf.Max(0.01f, go.transform.lossyScale.y);
            cap.radius = 0.55f / s;
            cap.height = 7f / s;
            cap.center = new Vector3(0f, 3.5f / s, 0f);
        }
    }
}
#endif
