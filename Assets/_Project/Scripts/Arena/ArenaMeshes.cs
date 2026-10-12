using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 用程式產生競技場用的模型：不規則的地板石板、岩石。
/// 都是「平面著色」（每個面一個法線），看起來是有稜有角的低多邊形風格。
/// 產生出來的 Mesh 不會存進專案，每次載入時由 FloorTile / ArenaRock 重新產生。
/// </summary>
public static class ArenaMeshes
{
    /// <summary>
    /// 程式產生的模型用的旗標：不存進場景、Prefab 和遊戲檔（每次載入重新產生），
    /// 沒人用到時可以被 Unity 自動清掉。
    /// </summary>
    public const HideFlags GeneratedFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;

    // =====================================================================
    //  地板石板
    // =====================================================================

    /// <summary>
    /// 石板的外觀模型。原點在石板上表面的中心，往下延伸 thickness。
    /// outline：輪廓（凸多邊形，相對於原點，x→X、y→Z）。
    /// bevel：上緣削掉的斜角寬度，石板之間會出現一條溝，才看得出一塊一塊的形狀。
    /// uvOrigin / uvSize：貼圖用「在整個地板上的位置」來貼，每 uvSize 公尺重複一次，相鄰的石板貼圖會接起來。
    /// </summary>
    public static Mesh BuildSlab(Vector2[] outline, float thickness, float bevel, Vector2 uvOrigin, float uvSize)
    {
        int n = outline.Length;
        Vector2[] inner = Inset(outline, bevel);
        float bevelDepth = inner == outline ? 0f : bevel * 0.8f;
        uvSize = Mathf.Max(uvSize, 0.01f);

        var b = new Builder();

        // 上表面
        for (int i = 1; i < n - 1; i++)
            b.Triangle(Top(inner[0], 0f), Top(inner[i], 0f), Top(inner[i + 1], 0f), Vector3.up,
                       (inner[0] + uvOrigin) / uvSize, (inner[i] + uvOrigin) / uvSize, (inner[i + 1] + uvOrigin) / uvSize);

        float along = 0f;
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector2 edge = outline[j] - outline[i];
            float length = edge.magnitude;
            Vector3 outward = new Vector3(edge.y, 0f, -edge.x); // 逆時針多邊形的外側
            float u0 = along / uvSize, u1 = (along + length) / uvSize;
            along += length;

            // 斜角
            if (bevelDepth > 0f)
                b.Quad(Top(inner[i], 0f), Top(inner[j], 0f), Top(outline[j], -bevelDepth), Top(outline[i], -bevelDepth),
                       outward + Vector3.up * Mathf.Max(length, 0.001f),
                       new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, -bevel / uvSize), new Vector2(u0, -bevel / uvSize));

            // 側面
            b.Quad(Top(outline[i], -bevelDepth), Top(outline[j], -bevelDepth), Top(outline[j], -thickness), Top(outline[i], -thickness),
                   outward,
                   new Vector2(u0, -bevelDepth / uvSize), new Vector2(u1, -bevelDepth / uvSize),
                   new Vector2(u1, -thickness / uvSize), new Vector2(u0, -thickness / uvSize));
        }
        return b.ToMesh("Slab");
    }

    /// <summary>石板的碰撞模型：單純的柱體（上表面是完整的平面，走在上面不會被溝卡到）</summary>
    public static Mesh BuildSlabCollider(Vector2[] outline, float thickness)
    {
        int n = outline.Length;
        var verts = new Vector3[n * 2];
        for (int i = 0; i < n; i++)
        {
            verts[i] = Top(outline[i], 0f);
            verts[n + i] = Top(outline[i], -thickness);
        }
        var tris = new List<int>();
        for (int i = 1; i < n - 1; i++)
        {
            tris.Add(0); tris.Add(i + 1); tris.Add(i);             // 上（朝上）
            tris.Add(n); tris.Add(n + i); tris.Add(n + i + 1);     // 下（朝下）
        }
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            tris.Add(i); tris.Add(j); tris.Add(n + j);
            tris.Add(i); tris.Add(n + j); tris.Add(n + i);
        }
        var mesh = new Mesh { name = "SlabCollider" };
        mesh.vertices = verts;
        mesh.triangles = tris.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    static Vector3 Top(Vector2 p, float y) => new Vector3(p.x, y, p.y);

    /// <summary>
    /// 把凸多邊形往內縮 amount。縮完如果壞掉（石板太小、邊太短）就縮少一點；
    /// 真的不行就回傳原本的 outline（= 不做斜角）。
    /// </summary>
    static Vector2[] Inset(Vector2[] outline, float amount)
    {
        int n = outline.Length;
        for (int attempt = 0; attempt < 3 && amount > 0.005f; attempt++, amount *= 0.5f)
        {
            var result = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                Vector2 prev = outline[(i + n - 1) % n], cur = outline[i], next = outline[(i + 1) % n];
                Vector2 e0 = (cur - prev).normalized, e1 = (next - cur).normalized;
                Vector2 n0 = new Vector2(-e0.y, e0.x), n1 = new Vector2(-e1.y, e1.x); // 逆時針多邊形的內側
                Vector2 miter = n0 + n1;
                float denom = 1f + Vector2.Dot(n0, n1);
                miter = denom > 0.2f ? miter / denom : miter.normalized * 2.5f;
                if (miter.magnitude > 2.5f) miter = miter.normalized * 2.5f;
                result[i] = cur + miter * amount;
            }
            if (IsConvex(result)) return result;
        }
        return outline;
    }

    static bool IsConvex(Vector2[] poly)
    {
        int n = poly.Length;
        if (ShatterPattern.SignedArea(poly) <= 0f) return false;
        for (int i = 0; i < n; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % n], c = poly[(i + 2) % n];
            if ((b.x - a.x) * (c.y - b.y) - (b.y - a.y) * (c.x - b.x) < -1e-5f) return false;
        }
        return true;
    }

    // =====================================================================
    //  岩石
    // =====================================================================

    /// <summary>
    /// 一顆岩石。原點在地面的高度、岩石的正中央；底部有一小段埋在地下。
    /// 做法：一顆球被十幾個隨機方向的平面「削」過，剩下的就是有稜有角的石頭（一定是凸的）。
    /// spire = true 會做成往上收窄、頂端被斜斜削平、稍微歪一邊的岩柱；false 是圓胖的石塊。
    /// detail：1 = 80 個面（碰撞用），2 = 320 個面（外觀用）。同一個 seed 兩種精細度的形狀一樣。
    /// </summary>
    public static Mesh BuildRock(int seed, float radius, float height, bool spire, int detail)
    {
        var rng = new System.Random(seed);

        // 削石頭的平面
        int planeCount = spire ? 16 : 12;
        var planeNormals = new Vector3[planeCount];
        var planeDistances = new float[planeCount];
        for (int i = 0; i < planeCount; i++)
        {
            planeNormals[i] = RandomDirection(rng);
            planeDistances[i] = Mathf.Lerp(0.62f, 0.96f, (float)rng.NextDouble());
        }
        if (spire)
        {
            // 岩柱：幾刀接近垂直的大切面（像被劈過的立石），再加一刀斜的把頂端削平
            for (int i = 0; i < 4; i++)
            {
                float a = (i + (float)rng.NextDouble()) * Mathf.PI * 0.5f;
                planeNormals[i] = new Vector3(Mathf.Cos(a), Mathf.Lerp(-0.15f, 0.25f, (float)rng.NextDouble()), Mathf.Sin(a)).normalized;
                planeDistances[i] = Mathf.Lerp(0.55f, 0.8f, (float)rng.NextDouble());
            }
            float topAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
            float tilt = Mathf.Lerp(0.25f, 0.6f, (float)rng.NextDouble());
            planeNormals[4] = new Vector3(Mathf.Cos(topAngle) * tilt, 1f, Mathf.Sin(topAngle) * tilt).normalized;
            planeDistances[4] = Mathf.Lerp(0.72f, 0.9f, (float)rng.NextDouble());
        }
        float leanAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
        float lean = spire ? Mathf.Lerp(0.1f, 0.45f, (float)rng.NextDouble()) : 0f;
        float squashX = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
        float squashZ = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
        float yaw = (float)rng.NextDouble() * Mathf.PI * 2f;
        float cosYaw = Mathf.Cos(yaw), sinYaw = Mathf.Sin(yaw);

        Icosphere(detail, out var dirs, out var tris);
        var points = new Vector3[dirs.Count];
        for (int i = 0; i < dirs.Count; i++)
        {
            Vector3 d = dirs[i];
            float r = 1f;
            for (int k = 0; k < planeCount; k++)
            {
                float dot = Vector3.Dot(d, planeNormals[k]);
                if (dot > 0.001f) r = Mathf.Min(r, planeDistances[k] / dot);
            }
            Vector3 p = d * r;

            // 單位石頭（-1~1）→ 實際大小：下面約四分之一埋在地下
            const float buried = 0.5f;
            float t = (p.y + buried) / (1f + buried);          // 0 = 地面，1 = 頂端（地下是負的）
            float y = t * height;
            float taper = spire ? Mathf.Lerp(1f, 0.5f, Mathf.Pow(Mathf.Clamp01(t), 1.8f)) : 1f;
            float x = p.x * squashX * taper, z = p.z * squashZ * taper;
            float rx = x * cosYaw - z * sinYaw, rz = x * sinYaw + z * cosYaw;
            float leanAmount = lean * Mathf.Clamp01(t) * Mathf.Clamp01(t);
            points[i] = new Vector3((rx + Mathf.Cos(leanAngle) * leanAmount) * radius, y, (rz + Mathf.Sin(leanAngle) * leanAmount) * radius);
        }

        var b = new Builder();
        for (int i = 0; i < tris.Count; i += 3)
        {
            Vector3 a = points[tris[i]], c1 = points[tris[i + 1]], c2 = points[tris[i + 2]];
            Vector3 centre = (a + c1 + c2) / 3f;
            Vector3 outward = centre - new Vector3(0f, height * 0.35f, 0f);
            b.Triangle(a, c1, c2, outward,
                       new Vector2(a.x + a.z, a.y) * 0.5f, new Vector2(c1.x + c1.z, c1.y) * 0.5f, new Vector2(c2.x + c2.z, c2.y) * 0.5f);
        }
        return b.ToMesh(spire ? "RockSpire" : "Rock");
    }

    static Vector3 RandomDirection(System.Random rng)
    {
        float y = (float)rng.NextDouble() * 2f - 1f;
        float a = (float)rng.NextDouble() * Mathf.PI * 2f;
        float r = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
        return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
    }

    /// <summary>正二十面體細分成的球（detail = 細分幾次）</summary>
    static void Icosphere(int detail, out List<Vector3> verts, out List<int> tris)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        verts = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        tris = new List<int>
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        };

        for (int level = 0; level < detail; level++)
        {
            var cache = new Dictionary<long, int>();
            var next = new List<int>(tris.Count * 4);
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Midpoint(verts, cache, a, b), bc = Midpoint(verts, cache, b, c), ca = Midpoint(verts, cache, c, a);
                next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = next;
        }
    }

    static int Midpoint(List<Vector3> verts, Dictionary<long, int> cache, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        if (cache.TryGetValue(key, out int index)) return index;
        verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
        cache[key] = verts.Count - 1;
        return verts.Count - 1;
    }

    // =====================================================================
    //  組模型的小工具：每個面有自己的頂點和法線（平面著色）
    // =====================================================================

    class Builder
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        /// <summary>加一個三角形；facing 是這個面「應該朝向」的大概方向，用來決定正反面</summary>
        public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return; // 退化成一條線的三角形
            if (Vector3.Dot(normal, facing) < 0f)
            {
                (b, c) = (c, b);
                (ub, uc) = (uc, ub);
                normal = -normal;
            }
            normal.Normalize();
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            normals.Add(normal); normals.Add(normal); normals.Add(normal);
            uvs.Add(ua); uvs.Add(ub); uvs.Add(uc);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            Triangle(a, b, c, facing, ua, ub, uc);
            Triangle(a, c, d, facing, ua, uc, ud);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
