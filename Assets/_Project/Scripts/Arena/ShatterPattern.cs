using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把一個圓形範圍切成很多塊不規則的凸多邊形（像碎裂的石板地）。ArenaFloor 產生地板時使用。
///
/// 做法（Voronoi 圖）：
///   1. 在蜂巢狀的格點上撒種子點，每個點隨機偏移一點；隨機拿掉一些點，旁邊的石板就會變大
///   2. 每個種子點的石板 =「離這個點比離其他點都近」的範圍，一定是凸多邊形，而且彼此剛好密合
///   3. 把種子點往石板的重心移一點再算一次（Lloyd 鬆弛），石板會比較圓潤、少一些尖角
///   4. 把太短的邊（幾公分）縮成一個點，相鄰的石板一起改，所以還是密合的
///   最外圈的石板會貼著圓整齊地收邊（做法見 Generate 裡的說明）
///
/// 同一個 seed 一定產生同樣的結果。
/// </summary>
public static class ShatterPattern
{
    public class Cell
    {
        /// <summary>種子點。「某個位置屬於哪一塊」= 離哪個種子點最近</summary>
        public Vector2 site;
        /// <summary>重心（石板的中心位置）</summary>
        public Vector2 centroid;
        /// <summary>輪廓（凸多邊形，x→X、y→Z，從 +Y 往下看是逆時針）</summary>
        public Vector2[] outline;
    }

    /// <param name="radius">要鋪滿的圓的半徑。最外圈的石板會剛好貼著這個圓收邊，不會超出去</param>
    /// <param name="spacing">種子點的間距，大約等於石板的平均寬度</param>
    /// <param name="jitter">種子點隨機偏移的幅度（0 = 整齊的蜂巢，0.45 = 很不規則）</param>
    /// <param name="dropChance">拿掉種子點的機率，越大 → 大石板越多、大小差異越大</param>
    /// <param name="relaxIterations">鬆弛次數（0 = 尖角多，2 = 很圓潤）</param>
    public static List<Cell> Generate(float radius, float spacing, float jitter, float dropChance, int relaxIterations, int seed)
    {
        spacing = Mathf.Max(spacing, 0.2f);
        var rng = new System.Random(seed);

        // 1) 種子點：只撒在圓裡面
        var sites = new List<Vector2>();
        float rowHeight = spacing * 0.8660254f;
        float maxSiteRadius = radius - spacing * 0.2f; // 太貼邊的種子點會變成很薄的石板
        int rows = Mathf.CeilToInt(radius / rowHeight);
        int cols = Mathf.CeilToInt(radius / spacing) + 1;
        for (int j = -rows; j <= rows; j++)
        {
            for (int i = -cols; i <= cols; i++)
            {
                float x = (i + ((j & 1) != 0 ? 0.5f : 0f)) * spacing;
                float z = j * rowHeight;
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                float dist = Mathf.Sqrt((float)rng.NextDouble()) * jitter * spacing;
                bool drop = rng.NextDouble() < dropChance;
                var p = new Vector2(x + Mathf.Cos(angle) * dist, z + Mathf.Sin(angle) * dist);
                if (drop || p.magnitude > maxSiteRadius) continue;
                sites.Add(p);
            }
        }
        int realCount = sites.Count;

        // 2) + 3) 算石板 → 種子點往重心移 → 再算一次
        List<List<Vector2>> polys = null;
        for (int it = 0; it <= relaxIterations; it++)
        {
            // 收邊的技巧：靠近邊緣的種子點，在圓的外面放一個「鏡像」的假種子點（沿著半徑方向對稱）。
            // 真的和假的中間那條分界線剛好貼著圓，所以最外圈的石板會沿著圓整齊地切齊，不會有碎片。
            sites.RemoveRange(realCount, sites.Count - realCount);
            for (int i = 0; i < realCount; i++)
            {
                float r = sites[i].magnitude;
                if (r < radius - spacing * 3.5f || r < 0.001f) continue;
                sites.Add(sites[i] * ((2f * radius - r) / r));
            }

            polys = ComputeCells(sites, spacing);
            if (it == relaxIterations) break;
            for (int i = 0; i < realCount; i++)
            {
                if (polys[i] == null) continue;
                Vector2 moved = Vector2.Lerp(sites[i], Centroid(polys[i]), 0.75f);
                if (moved.magnitude > maxSiteRadius) moved = moved.normalized * maxSiteRadius;
                sites[i] = moved;
            }
        }

        var cells = new List<Cell>();
        for (int i = 0; i < realCount; i++)
        {
            var poly = polys[i];
            if (poly == null) continue;
            if (Mathf.Abs(SignedArea(poly)) < spacing * spacing * 0.02f) continue; // 小到看不見的碎片
            cells.Add(new Cell { site = sites[i], centroid = Centroid(poly), outline = poly.ToArray() });
        }
        WeldShortEdges(cells, spacing * 0.08f);
        return cells;
    }

    // ---------------- 把太短的邊縮成一個點 ----------------

    /// <summary>
    /// Voronoi 圖常常有只有幾公分長的邊，做斜角時會壞掉，看起來也像破圖。
    /// 這裡把這種短邊的兩個端點合併成中間的一個點。共用這個端點的石板會一起改，所以石板之間還是密合的。
    /// </summary>
    static void WeldShortEdges(List<Cell> cells, float minEdge)
    {
        // 1) 給每個頂點一個編號：不同石板上位置幾乎一樣的頂點算同一個
        const float bin = 0.02f, same = 0.004f;
        var grid = new Dictionary<long, List<int>>();
        var points = new List<Vector2>();
        var ids = new List<int[]>(cells.Count);
        foreach (var c in cells)
        {
            var cellIds = new int[c.outline.Length];
            for (int i = 0; i < c.outline.Length; i++)
            {
                Vector2 p = c.outline[i];
                int gx = Mathf.FloorToInt(p.x / bin), gy = Mathf.FloorToInt(p.y / bin);
                int found = -1;
                for (int dx = -1; dx <= 1 && found < 0; dx++)
                    for (int dy = -1; dy <= 1 && found < 0; dy++)
                        if (grid.TryGetValue(Key(gx + dx, gy + dy), out var list))
                            foreach (int k in list)
                                if ((points[k] - p).sqrMagnitude <= same * same) { found = k; break; }
                if (found < 0)
                {
                    found = points.Count;
                    points.Add(p);
                    long key = Key(gx, gy);
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(found);
                }
                cellIds[i] = found;
            }
            ids.Add(cellIds);
        }

        // 2) 短邊的兩端歸成同一組
        var parent = new int[points.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        for (int c = 0; c < cells.Count; c++)
        {
            var cellIds = ids[c];
            for (int i = 0; i < cellIds.Length; i++)
            {
                int a = cellIds[i], b = cellIds[(i + 1) % cellIds.Length];
                if (a != b && (points[a] - points[b]).magnitude < minEdge) parent[Find(parent, a)] = Find(parent, b);
            }
        }

        // 3) 每一組移到這組頂點的平均位置
        var sum = new Vector2[points.Count];
        var count = new int[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            int root = Find(parent, i);
            sum[root] += points[i];
            count[root]++;
        }

        // 4) 用新的位置重組每塊石板
        for (int c = cells.Count - 1; c >= 0; c--)
        {
            var poly = new List<Vector2>();
            int lastRoot = -1, firstRoot = -1;
            foreach (int id in ids[c])
            {
                int root = Find(parent, id);
                if (root == lastRoot) continue;
                if (firstRoot < 0) firstRoot = root;
                lastRoot = root;
                poly.Add(sum[root] / count[root]);
            }
            if (poly.Count > 1 && lastRoot == firstRoot) poly.RemoveAt(poly.Count - 1);
            if (poly.Count < 3) { cells.RemoveAt(c); continue; }
            cells[c].outline = poly.ToArray();
            cells[c].centroid = Centroid(poly);
        }
    }

    static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }

    // ---------------- Voronoi ----------------

    static List<List<Vector2>> ComputeCells(List<Vector2> sites, float spacing)
    {
        // 把種子點放進格子裡，找鄰居時只要看附近幾格
        float bucket = spacing * 1.5f;
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < sites.Count; i++)
        {
            long key = Key(Mathf.FloorToInt(sites[i].x / bucket), Mathf.FloorToInt(sites[i].y / bucket));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }

        var result = new List<List<Vector2>>(sites.Count);
        var neighbours = new List<int>();
        for (int i = 0; i < sites.Count; i++)
        {
            Vector2 s = sites[i];

            // 從一個大正方形開始，被每個鄰居「一刀一刀切掉離鄰居比較近的那一半」
            float half = spacing * 3f;
            var poly = new List<Vector2>
            {
                s + new Vector2(-half, -half), s + new Vector2(half, -half),
                s + new Vector2(half, half), s + new Vector2(-half, half),
            };

            neighbours.Clear();
            int bx = Mathf.FloorToInt(s.x / bucket), by = Mathf.FloorToInt(s.y / bucket);
            for (int dx = -3; dx <= 3; dx++)
                for (int dy = -3; dy <= 3; dy++)
                    if (buckets.TryGetValue(Key(bx + dx, by + dy), out var list))
                        foreach (int k in list)
                            if (k != i) neighbours.Add(k);
            neighbours.Sort((a, b) => (sites[a] - s).sqrMagnitude.CompareTo((sites[b] - s).sqrMagnitude));

            foreach (int k in neighbours)
            {
                Vector2 d = sites[k] - s;
                float dist = d.magnitude;
                if (dist < 0.0001f) continue;
                if (dist > MaxDistance(poly, s) * 2f) break; // 更遠的鄰居已經切不到了
                poly = Clip(poly, s + d * 0.5f, d / dist);
                if (poly.Count < 3) break;
            }

            poly = Clean(poly);
            if (poly.Count < 3) { result.Add(null); continue; }
            if (SignedArea(poly) < 0f) poly.Reverse();
            result.Add(poly);
        }
        return result;
    }

    static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

    static float MaxDistance(List<Vector2> poly, Vector2 from)
    {
        float max = 0f;
        foreach (var p in poly) max = Mathf.Max(max, (p - from).sqrMagnitude);
        return Mathf.Sqrt(max);
    }

    /// <summary>只留下 dot(p - point, normal) &lt;= 0 的那一半</summary>
    static List<Vector2> Clip(List<Vector2> poly, Vector2 point, Vector2 normal)
    {
        var result = new List<Vector2>(poly.Count + 1);
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            float da = Vector2.Dot(a - point, normal);
            float db = Vector2.Dot(b - point, normal);
            if (da <= 0f) result.Add(a);
            if ((da < 0f && db > 0f) || (da > 0f && db < 0f))
                result.Add(a + (b - a) * (da / (da - db)));
        }
        return result;
    }

    /// <summary>拿掉幾乎重疊的頂點</summary>
    static List<Vector2> Clean(List<Vector2> poly)
    {
        var result = new List<Vector2>(poly.Count);
        foreach (var p in poly)
            if (result.Count == 0 || (result[result.Count - 1] - p).sqrMagnitude > 1e-6f) result.Add(p);
        if (result.Count > 1 && (result[0] - result[result.Count - 1]).sqrMagnitude <= 1e-6f)
            result.RemoveAt(result.Count - 1);
        return result;
    }

    // ---------------- 多邊形小工具 ----------------

    /// <summary>面積（逆時針為正）</summary>
    public static float SignedArea(IList<Vector2> poly)
    {
        float sum = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            sum += a.x * b.y - b.x * a.y;
        }
        return sum * 0.5f;
    }

    public static Vector2 Centroid(IList<Vector2> poly)
    {
        float area = 0f, cx = 0f, cy = 0f;
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            float cross = a.x * b.y - b.x * a.y;
            area += cross;
            cx += (a.x + b.x) * cross;
            cy += (a.y + b.y) * cross;
        }
        if (Mathf.Abs(area) < 1e-8f)
        {
            Vector2 sum = Vector2.zero;
            foreach (var p in poly) sum += p;
            return sum / Mathf.Max(poly.Count, 1);
        }
        return new Vector2(cx, cy) / (3f * area);
    }

    /// <summary>點在不在凸多邊形裡面（多邊形要是逆時針）</summary>
    public static bool Contains(IList<Vector2> poly, Vector2 p)
    {
        for (int i = 0; i < poly.Count; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Count];
            if ((b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x) < 0f) return false;
        }
        return true;
    }
}
