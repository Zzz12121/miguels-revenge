using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 圓形競技場的地板：由很多小方塊 (FloorTile) 組成，方塊之間沒有縫、用同一個材質，
/// 所以看起來是一整片。
/// - 提供「找出扇形範圍 / 某位置附近的方塊 / 某位置的地面高度」等查詢給 Boss 使用
/// - GenerateTerrain：產生隨機的小山丘和柱子（Boss 戰開始時由 BossController 呼叫）
///
/// 在 Inspector 對這個元件按右鍵：
///   「重新產生地板」→ 用新的半徑 / 方塊大小重建（編輯模式用）
///   「預覽地形」    → 遊戲執行中立刻換一個新的隨機地形
/// </summary>
public class ArenaFloor : MonoBehaviour
{
    [Header("地板")]
    [Tooltip("可站立區域的半徑 (m)")]
    public float radius = 18f;
    [Tooltip("每個小方塊的邊長 (m)")]
    public float tileSize = 1.5f;
    public float tileThickness = 1f;
    public Material tileMaterial;

    [Header("地形：小山丘")]
    [Tooltip("山丘最高多高 (m)")]
    public float hillHeight = 1.8f;
    [Tooltip("山丘大小，越小山丘越大塊、越大越零碎")]
    public float hillNoiseScale = 0.13f;
    [Tooltip("雜訊低於這個值的地方保持平地，越大平地越多 (0~1)")]
    [Range(0f, 1f)] public float hillThreshold = 0.5f;
    [Tooltip("高度以這個單位分階，做出一階一階的方塊感；0 = 不分階")]
    public float heightStep = 0.3f;

    [Header("地形：柱子")]
    public int pillarCount = 7;
    public float pillarHeight = 4.5f;
    [Tooltip("柱子之間最少間隔 (m)")]
    public float pillarSpacing = 6f;
    [Tooltip("柱子變成 2x2 大柱的機率")]
    [Range(0f, 1f)] public float bigPillarChance = 0.4f;

    [Header("地形：保留平地")]
    [Tooltip("場地中央這個半徑內保持平地（Boss 放光束的位置）")]
    public float flatCenterRadius = 4f;
    [Tooltip("玩家 / Boss 周圍這個半徑內保持平地，避免開場被方塊頂起來")]
    public float keepFlatRadius = 3f;

    [Header("地形：升起動畫")]
    public float riseDuration = 1.2f;
    [Tooltip("升起的波從中央往外擴散的速度 (m/s)")]
    public float riseWaveSpeed = 14f;

    public List<FloorTile> Tiles { get; } = new List<FloorTile>();
    public Vector3 Center => transform.position;

    readonly Dictionary<Vector2Int, FloorTile> grid = new Dictionary<Vector2Int, FloorTile>();

    void Awake()
    {
        Tiles.Clear();
        grid.Clear();
        GetComponentsInChildren(true, Tiles);
        foreach (var t in Tiles) grid[new Vector2Int(t.gridX, t.gridZ)] = t;
    }

    // ---------------- 查詢 ----------------

    /// <summary>取得某個世界座標正下方的方塊（沒有就回傳 null）</summary>
    public FloorTile GetTileAt(Vector3 worldPos)
    {
        Vector3 local = transform.InverseTransformPoint(worldPos); // 支援 Prefab 被移動 / 旋轉
        var key = new Vector2Int(Mathf.RoundToInt(local.x / tileSize), Mathf.RoundToInt(local.z / tileSize));
        return grid.TryGetValue(key, out var tile) ? tile : null;
    }

    public FloorTile GetTile(int x, int z) => grid.TryGetValue(new Vector2Int(x, z), out var t) ? t : null;

    /// <summary>這個位置下面有沒有可以站的地板</summary>
    public bool IsSolidAt(Vector3 worldPos)
    {
        var t = GetTileAt(worldPos);
        return t != null && t.IsSolid;
    }

    /// <summary>
    /// 某位置的地面高度（不含凹陷）。下面是洞或場外就回傳 false。
    /// </summary>
    public bool TryGetGroundHeight(Vector3 worldPos, out float height)
    {
        var t = GetTileAt(worldPos);
        if (t == null || !t.IsSolid) { height = 0f; return false; }
        height = t.RestTopY;
        return true;
    }

    /// <summary>水平距離在 range 以內的方塊</summary>
    public List<FloorTile> GetTilesNear(Vector3 worldPos, float range)
    {
        var result = new List<FloorTile>();
        foreach (var t in Tiles)
            if (FlatDistance(t.RestPosition, worldPos) <= range) result.Add(t);
        return result;
    }

    /// <summary>以 origin 為圓心、面向 forward 的扇形範圍內的方塊</summary>
    public List<FloorTile> GetTilesInFan(Vector3 origin, Vector3 forward, float halfAngle, float range, float minRange = 0f)
    {
        var result = new List<FloorTile>();
        forward.y = 0f;
        forward.Normalize();
        foreach (var t in Tiles)
        {
            Vector3 to = t.RestPosition - origin;
            to.y = 0f;
            float d = to.magnitude;
            if (d < minRange || d > range) continue;
            if (Vector3.Angle(forward, to) <= halfAngle) result.Add(t);
        }
        return result;
    }

    /// <summary>所有方塊立刻恢復；flatten = true 時地形也壓平</summary>
    public void ResetAllTiles(bool flatten = false)
    {
        foreach (var t in Tiles) t.ResetTile(flatten);
    }

    public static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // ---------------- 隨機地形 ----------------

    /// <summary>
    /// 產生新的隨機地形（小山丘 + 柱子），方塊會從中央往外依序升起。
    /// keepFlat：這些位置附近保持平地（通常傳入玩家和 Boss 的位置）。
    /// </summary>
    public void GenerateTerrain(IList<Vector3> keepFlat)
    {
        var heights = new Dictionary<FloorTile, float>();
        var pillars = new HashSet<FloorTile>();
        Vector2 offset = new Vector2(Random.Range(0f, 1000f), Random.Range(0f, 1000f));

        // 1) 小山丘：Perlin 雜訊
        foreach (var t in Tiles)
        {
            float h = 0f;
            if (t.canCollapse)
            {
                float n = Mathf.PerlinNoise(t.gridX * hillNoiseScale + offset.x, t.gridZ * hillNoiseScale + offset.y);
                h = Mathf.InverseLerp(hillThreshold, 1f, n) * hillHeight;

                // 靠近中央、玩家、Boss 的地方慢慢變平
                h *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(flatCenterRadius, flatCenterRadius + 3f, FlatDistance(t.RestPosition, Center)));
                h *= FlatFactor(t, keepFlat);

                if (heightStep > 0f) h = Mathf.Round(h / heightStep) * heightStep;
            }
            heights[t] = h;
        }

        // 2) 柱子：隨機挑幾個互相隔開的位置
        var candidates = new List<FloorTile>();
        foreach (var t in Tiles)
        {
            if (!t.canCollapse) continue;
            float d = FlatDistance(t.RestPosition, Center);
            if (d < flatCenterRadius + 2f || d > radius - 2.5f) continue;
            if (FlatFactor(t, keepFlat, 2.5f) < 1f) continue;
            candidates.Add(t);
        }
        Shuffle(candidates);

        var chosen = new List<FloorTile>();
        foreach (var c in candidates)
        {
            if (chosen.Count >= pillarCount) break;
            bool tooClose = false;
            foreach (var other in chosen)
                if (FlatDistance(c.RestPosition, other.RestPosition) < pillarSpacing) { tooClose = true; break; }
            if (tooClose) continue;
            chosen.Add(c);

            float ph = pillarHeight * Random.Range(0.75f, 1.25f);
            if (heightStep > 0f) ph = Mathf.Round(ph / heightStep) * heightStep;

            var group = new List<FloorTile> { c };
            if (Random.value < bigPillarChance)
            {
                group.Add(GetTile(c.gridX + 1, c.gridZ));
                group.Add(GetTile(c.gridX, c.gridZ + 1));
                group.Add(GetTile(c.gridX + 1, c.gridZ + 1));
            }
            foreach (var g in group)
            {
                if (g == null || !g.canCollapse) continue;
                heights[g] = ph;
                pillars.Add(g);
            }
        }

        // 3) 套用：從中央往外一圈一圈升起
        foreach (var kv in heights)
        {
            float d = FlatDistance(kv.Key.RestPosition, Center);
            kv.Key.SetHeight(kv.Value, pillars.Contains(kv.Key), d / riseWaveSpeed, riseDuration);
        }
    }

    float FlatFactor(FloorTile t, IList<Vector3> keepFlat, float extra = 0f)
    {
        if (keepFlat == null) return 1f;
        float f = 1f;
        foreach (var p in keepFlat)
        {
            float d = FlatDistance(t.RestPosition, p);
            f = Mathf.Min(f, Mathf.InverseLerp(keepFlatRadius + extra, keepFlatRadius + extra + 2f, d));
        }
        return f;
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    [ContextMenu("預覽地形 (執行中 Preview Terrain)")]
    void PreviewTerrain()
    {
        if (!Application.isPlaying) { Debug.LogWarning("請在按下 Play 之後再使用「預覽地形」"); return; }
        var keep = new List<Vector3>();
        var player = GameObject.FindWithTag("Player");
        if (player != null) keep.Add(player.transform.position);
        GenerateTerrain(keep);
    }

    // ---------------- 產生地板（編輯模式） ----------------

    [ContextMenu("重新產生地板 (Regenerate Floor)")]
    public void Generate()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        // 外圈多鋪一圈，讓方塊一路延伸到牆壁裡面，邊緣才不會有縫
        float fillRadius = radius + tileSize;
        int n = Mathf.CeilToInt(fillRadius / tileSize);
        int count = 0;

        for (int x = -n; x <= n; x++)
        {
            for (int z = -n; z <= n; z++)
            {
                Vector3 local = new Vector3(x * tileSize, -tileThickness * 0.5f, z * tileSize);
                float d = new Vector2(local.x, local.z).magnitude;
                if (d > fillRadius) continue;

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Tile_{x}_{z}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = local;
                go.transform.localScale = new Vector3(tileSize, tileThickness, tileSize);
                if (tileMaterial != null) go.GetComponent<MeshRenderer>().sharedMaterial = tileMaterial;

                var tile = go.AddComponent<FloorTile>();
                tile.gridX = x;
                tile.gridZ = z;
                tile.canCollapse = d <= radius - tileSize * 0.5f; // 貼牆的外圈不塌
                count++;
            }
        }
        Debug.Log($"[ArenaFloor] 產生了 {count} 個地板方塊");
    }
}
