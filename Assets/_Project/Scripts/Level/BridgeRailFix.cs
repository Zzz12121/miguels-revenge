using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 吊橋兩側的隱形扶手（道場 → 吊橋、ForestIsland 的吊橋都是 DojoMountainPathBuilder / ForestIslandBuilder 做的）：
/// 原本每 4m 一塊、高 2.2m 的箱子（RailWall），玩家跳得上去站在繩子上，射出去的箭也會插在半空中、鏡頭會被它往前推。
///
/// 場景載入時自動換掉（不用重建場景）：每一側合成「一條」沿著吊橋弧度的細長碰撞體（MeshCollider），
/// 高度和扶手繩一樣（RailHeight），頂端是尖的（站不上去）。走路會被擋住，但跳得過去——
/// 跳過繩子、掉到橋面以下 FallKillDepth 公尺就摔死（BridgeFallKill）。
/// 它掛 ArrowPassThrough：箭、鏡頭、視線檢查都直接穿過，箭不會插在上面。
/// </summary>
public static class BridgeRailFix
{
    public const float RailHeight = 1.2f;    // 從橋面往上多高（扶手繩的高度，跳得過去）
    public const float FallKillDepth = 2.5f; // 掉到橋面以下多深就算摔死
    public const float RailThickness = 0.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Fix();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => Fix();

    static void Fix()
    {
        // 依父物件（每座橋的 WalkColliders）分組
        var byParent = new Dictionary<Transform, List<BoxCollider>>();
        foreach (var box in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (box.name != "RailWall" || box.isTrigger || box.transform.parent == null) continue;
            if (!byParent.TryGetValue(box.transform.parent, out var list)) byParent[box.transform.parent] = list = new List<BoxCollider>();
            list.Add(box);
        }
        foreach (var kv in byParent) Rebuild(kv.Key, kv.Value);
    }

    static void Rebuild(Transform parent, List<BoxCollider> walls)
    {
        if (walls.Count == 0) return;
        // 橋的方向 = 每塊牆的 forward 平均；左右 = 和橋中線的相對位置
        Vector3 along = Vector3.zero, center = Vector3.zero;
        foreach (var w in walls) { along += w.transform.forward; center += w.transform.position; }
        along.y = 0f;
        if (along.sqrMagnitude < 1e-4f) return;
        along.Normalize();
        center /= walls.Count;
        Vector3 right = Vector3.Cross(Vector3.up, along);

        var left = new List<BoxCollider>(); var rightSide = new List<BoxCollider>();
        foreach (var w in walls) (Vector3.Dot(w.transform.position - center, right) < 0f ? left : rightSide).Add(w);
        var lLine = Build(parent, "RailBarrier_L", left, along);
        var rLine = Build(parent, "RailBarrier_R", rightSide, along);
        foreach (var w in walls) Object.Destroy(w.gameObject);
        if (lLine != null && rLine != null)
            parent.gameObject.AddComponent<BridgeFallKill>().Setup(lLine, rLine);
    }

    /// <summary>
    /// 一側的牆 → 沿著每塊牆的底邊（橋面高度）連成一條折線，往上拉 RailHeight。
    /// 截面是細長的三角形（底寬 RailThickness、頂端尖的）：擋得住走路，頂端站不上去。回傳那條折線（橋面高度）。
    /// </summary>
    static List<Vector3> Build(Transform parent, string name, List<BoxCollider> walls, Vector3 along)
    {
        if (walls.Count == 0) return null;
        walls.Sort((a, b) => Vector3.Dot(a.transform.position, along).CompareTo(Vector3.Dot(b.transform.position, along)));
        var line = new List<Vector3>();
        foreach (var w in walls)
        {
            Vector3 half = w.transform.forward * (w.size.z * 0.5f);
            Vector3 bottom = w.transform.TransformPoint(w.center) - Vector3.up * (w.size.y * 0.5f);
            AddPoint(line, bottom - half, along);
            AddPoint(line, bottom + half, along);
        }
        if (line.Count < 2) return null;

        // 三角形截面：內側底、外側底、頂端（尖）
        Vector3 side = Vector3.Cross(Vector3.up, along) * (RailThickness * 0.5f);
        var verts = new List<Vector3>(); var tris = new List<int>();
        foreach (var p in line)
        {
            verts.Add(p - side); verts.Add(p + side); verts.Add(p + Vector3.up * RailHeight);
        }
        for (int i = 0; i < line.Count - 1; i++)
        {
            int a = i * 3, b = (i + 1) * 3;
            Tri(tris, a, b, b + 2); Tri(tris, a, b + 2, a + 2);             // 內側斜面
            Tri(tris, a + 1, a + 2, b + 2); Tri(tris, a + 1, b + 2, b + 1); // 外側斜面
            Tri(tris, a, a + 1, b + 1); Tri(tris, a, b + 1, b);             // 底
        }
        int last = (line.Count - 1) * 3;
        Tri(tris, 0, 2, 1);
        Tri(tris, last, last + 1, last + 2);

        var go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        var mesh = new Mesh { name = name };
        // 網格是世界座標；父物件有縮放的話換成本地座標
        for (int i = 0; i < verts.Count; i++) verts[i] = go.transform.InverseTransformPoint(verts[i]);
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        go.AddComponent<ArrowPassThrough>();
        return line;
    }

    static void AddPoint(List<Vector3> line, Vector3 p, Vector3 along)
    {
        // 相鄰兩塊牆的端點重疊：只要往前推進的點（避免折線來回）
        if (line.Count > 0 && Vector3.Dot(p - line[line.Count - 1], along) < 0.05f)
        {
            line[line.Count - 1] = Vector3.Lerp(line[line.Count - 1], p, 0.5f);
            return;
        }
        line.Add(p);
    }

    static void Tri(List<int> t, int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }
}

/// <summary>
/// 只擋角色的碰撞體（例如吊橋的隱形扶手）：箭（PlayerArrow、敵人的箭）、鏡頭的防擋、視線檢查都直接穿過它，
/// 箭不會插在上面。
/// </summary>
public sealed class ArrowPassThrough : MonoBehaviour
{
    public static bool On(Collider c) => c != null && c.GetComponent<ArrowPassThrough>() != null;
}

/// <summary>
/// 跳過吊橋扶手繩掉下去 = 摔死：玩家在兩條扶手線外側（離橋中線超過半寬），
/// 而且腳底掉到那個位置的橋面以下 FallKillDepth 公尺 → PlayerHealth.Kill。
/// （道場那一側原本的 TrailBoundsKillZone 會把掉出去的人救回山路，這裡在它之前就判定摔死。）
/// </summary>
public sealed class BridgeFallKill : MonoBehaviour
{
    readonly List<Vector3> center = new List<Vector3>();
    readonly List<float> halfWidth = new List<float>();
    PlayerHealth player;

    public void Setup(List<Vector3> left, List<Vector3> right)
    {
        // 中線 = 左右扶手線對應點的中點（兩條線點數可能不同：依比例取樣）
        int n = Mathf.Max(left.Count, right.Count);
        for (int i = 0; i < n; i++)
        {
            float t = n > 1 ? i / (float)(n - 1) : 0f;
            Vector3 l = Sample(left, t), r = Sample(right, t);
            center.Add((l + r) * 0.5f);
            halfWidth.Add(Vector3.Distance(new Vector3(l.x, 0f, l.z), new Vector3(r.x, 0f, r.z)) * 0.5f);
        }
    }

    static Vector3 Sample(List<Vector3> line, float t)
    {
        if (line.Count == 1) return line[0];
        float f = t * (line.Count - 1);
        int i = Mathf.Min(Mathf.FloorToInt(f), line.Count - 2);
        return Vector3.Lerp(line[i], line[i + 1], f - i);
    }

    void Update()
    {
        if (center.Count < 2) return;
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }
        if (player.IsDead) return;
        Vector3 p = player.transform.position;
        Vector2 pf = new Vector2(p.x, p.z);

        // 最近的中線線段
        float best = float.MaxValue, deckY = 0f, hw = 0f, along = 0f; int bestSeg = -1;
        for (int i = 0; i < center.Count - 1; i++)
        {
            Vector2 a = new Vector2(center[i].x, center[i].z), b = new Vector2(center[i + 1].x, center[i + 1].z);
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude > 1e-6f ? Vector2.Dot(pf - a, ab) / ab.sqrMagnitude : 0f;
            float tc = Mathf.Clamp01(t);
            float d = Vector2.Distance(pf, a + ab * tc);
            if (d < best)
            {
                best = d; bestSeg = i; along = t;
                deckY = Mathf.Lerp(center[i].y, center[i + 1].y, tc);
                hw = Mathf.Lerp(halfWidth[i], halfWidth[i + 1], tc);
            }
        }
        if (bestSeg < 0) return;
        // 橋的兩端以外（橋頭的地面上）不算
        if ((bestSeg == 0 && along < 0f) || (bestSeg == center.Count - 2 && along > 1f)) return;
        if (best <= hw + 0.15f) return;                               // 還在扶手線內側
        if (player.FeetY > deckY - BridgeRailFix.FallKillDepth) return; // 還沒掉下去（例如剛跳出去還在半空）
        player.Kill("從吊橋上摔下去");
    }
}
