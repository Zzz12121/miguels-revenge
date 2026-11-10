#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 組網格用的小工具：一個 Kit 收集多個方塊 / 圓柱 / 旋轉體，最後輸出一個 Mesh（每種材質一個子網格）。
/// UV 用公尺計算，材質的 Tiling 決定貼圖一張代表幾公尺。
/// </summary>
public sealed class JVMeshKit
{
    readonly List<Vector3> positions = new List<Vector3>();
    readonly List<Vector3> normals = new List<Vector3>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly SortedDictionary<JVMat, List<int>> subMeshes = new SortedDictionary<JVMat, List<int>>();

    /// <summary>之後加入的幾何都會先套用這個轉換（不要用負的縮放）</summary>
    public Matrix4x4 Transform = Matrix4x4.identity;

    public Material[] Materials
    {
        get
        {
            var list = new List<Material>();
            foreach (var key in subMeshes.Keys) list.Add(JapaneseVillageAssets.Get(key));
            return list.ToArray();
        }
    }

    public Mesh Build(string name)
    {
        var mesh = new Mesh { name = name };
        if (positions.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = subMeshes.Count;
        int i = 0;
        foreach (var indices in subMeshes.Values) mesh.SetTriangles(indices, i++);
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    List<int> Sub(JVMat m)
    {
        if (!subMeshes.TryGetValue(m, out var list)) subMeshes[m] = list = new List<int>();
        return list;
    }

    int Vertex(Vector3 p, Vector3 n, Vector2 uv)
    {
        positions.Add(Transform.MultiplyPoint3x4(p));
        normals.Add(Transform.MultiplyVector(n).normalized);
        uvs.Add(uv);
        return positions.Count - 1;
    }

    /// <summary>四邊形：從正面看 a → b → c → d 為順時針。a→b 是 V（貼圖上方），a→d 是 U</summary>
    public void Quad(JVMat m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uOffset = 0f, float vOffset = 0f)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        float lv = (b - a).magnitude, lv2 = (c - d).magnitude, lu = (d - a).magnitude, lu2 = (c - b).magnitude;
        Add(m, a, b, c, d, n, n, n, n,
            new Vector2(uOffset, vOffset), new Vector2(uOffset, vOffset + lv),
            new Vector2(uOffset + lu2, vOffset + lv2), new Vector2(uOffset + lu, vOffset));
    }

    /// <summary>貼圖整張貼上的四邊形，法線自己給（樹葉用朝上的法線，雙面都亮）</summary>
    public void Card(JVMat m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        // 這張貼圖的材質 Tiling 是 1，UV 就是 0~1
        Add(m, a, b, c, d, normal, normal, normal, normal, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
    }

    void Add(JVMat m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int ia = Vertex(a, na, ua), ib = Vertex(b, nb, ub), ic = Vertex(c, nc, uc), id = Vertex(d, nd, ud);
        var t = Sub(m);
        t.Add(ia); t.Add(ib); t.Add(ic);
        t.Add(ia); t.Add(ic); t.Add(id);
    }

    /// <summary>方塊（center = 中心點）</summary>
    public void Box(JVMat m, Vector3 center, Vector3 size, Quaternion? rotation = null)
    {
        Quaternion r = rotation ?? Quaternion.identity;
        Vector3 h = size * 0.5f;
        Vector3 P(float x, float y, float z) => center + r * new Vector3(x * h.x, y * h.y, z * h.z);
        float seed = (center.x + center.y * 3.1f + center.z * 7.3f) * 0.37f; // 不同方塊的貼圖位置不同
        Quad(m, P(+1, -1, +1), P(+1, +1, +1), P(-1, +1, +1), P(-1, -1, +1), seed, 0f);
        Quad(m, P(-1, -1, -1), P(-1, +1, -1), P(+1, +1, -1), P(+1, -1, -1), seed, 0f);
        Quad(m, P(+1, -1, -1), P(+1, +1, -1), P(+1, +1, +1), P(+1, -1, +1), seed, 0f);
        Quad(m, P(-1, -1, +1), P(-1, +1, +1), P(-1, +1, -1), P(-1, -1, -1), seed, 0f);
        Quad(m, P(-1, +1, -1), P(-1, +1, +1), P(+1, +1, +1), P(+1, +1, -1), seed, 0f);
        Quad(m, P(-1, -1, +1), P(-1, -1, -1), P(+1, -1, -1), P(+1, -1, +1), seed, 0f);
    }

    /// <summary>圓柱 / 圓錐：從 a 到 b，半徑 ra → rb</summary>
    public void Cylinder(JVMat m, Vector3 a, Vector3 b, float ra, float rb, int sides, bool capA = true, bool capB = true, float vStart = 0f)
    {
        Vector3 axis = (b - a).normalized;
        Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
        Vector3 w = Vector3.Cross(u, axis).normalized;
        float length = (b - a).magnitude;
        var t = Sub(m);
        int start = positions.Count;
        for (int i = 0; i <= sides; i++)
        {
            float angle = i * Mathf.PI * 2f / sides;
            Vector3 radial = u * Mathf.Cos(angle) + w * Mathf.Sin(angle);
            float uv = (float)i / sides;
            Vertex(a + radial * ra, radial, new Vector2(uv, vStart));
            Vertex(b + radial * rb, radial, new Vector2(uv, vStart + length));
        }
        for (int i = 0; i < sides; i++)
        {
            int ba = start + i * 2, bb = ba + 1, ca = ba + 2, cb = ba + 3;
            t.Add(ba); t.Add(bb); t.Add(cb);
            t.Add(ba); t.Add(cb); t.Add(ca);
        }
        void Cap(Vector3 point, float radius, Vector3 normal, bool top)
        {
            int center = Vertex(point, normal, Vector2.zero);
            int first = positions.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                Vector3 radial = u * Mathf.Cos(angle) + w * Mathf.Sin(angle);
                Vertex(point + radial * radius, normal, new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            for (int i = 0; i < sides; i++)
            {
                t.Add(center);
                if (top) { t.Add(first + i + 1); t.Add(first + i); }
                else { t.Add(first + i); t.Add(first + i + 1); }
            }
        }
        if (capA && ra > 0f) Cap(a, ra, -axis, false);
        if (capB && rb > 0f) Cap(b, rb, axis, true);
    }

    /// <summary>旋轉體：profile 是 (半徑, 高度) 由下往上排（由上往下排就變成朝內）</summary>
    public void Lathe(JVMat m, Vector3 center, Vector2[] profile, int sides, float scaleX = 1f, float scaleZ = 1f)
    {
        var t = Sub(m);
        float v = 0f;
        for (int s = 0; s < profile.Length - 1; s++)
        {
            Vector2 p0 = profile[s], p1 = profile[s + 1];
            Vector2 edge = (p1 - p0).normalized;
            float length = (p1 - p0).magnitude;
            int start = positions.Count;
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                float cos = Mathf.Cos(angle), sin = Mathf.Sin(angle);
                Vector3 n = new Vector3(cos * edge.y, -edge.x, sin * edge.y);
                float uv = i * 2f * Mathf.PI * Mathf.Max(p0.x, p1.x, 0.05f) / sides;
                Vertex(center + new Vector3(cos * p0.x * scaleX, p0.y, sin * p0.x * scaleZ), n, new Vector2(uv, v));
                Vertex(center + new Vector3(cos * p1.x * scaleX, p1.y, sin * p1.x * scaleZ), n, new Vector2(uv, v + length));
            }
            for (int i = 0; i < sides; i++)
            {
                int ba = start + i * 2, bb = ba + 1, ca = ba + 2, cb = ba + 3;
                t.Add(ba); t.Add(bb); t.Add(cb);
                t.Add(ba); t.Add(cb); t.Add(ca);
            }
            v += length;
        }
    }

    /// <summary>橢圓球（平滑法線）</summary>
    public void Ellipsoid(JVMat m, Vector3 center, Vector3 radii, int rings = 6, int segments = 10)
    {
        var t = Sub(m);
        int start = positions.Count;
        for (int r = 0; r <= rings; r++)
        {
            float phi = Mathf.PI * (-0.5f + (float)r / rings);
            for (int s = 0; s <= segments; s++)
            {
                float lambda = s * Mathf.PI * 2f / segments;
                var dir = new Vector3(Mathf.Cos(phi) * Mathf.Cos(lambda), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(lambda));
                var n = new Vector3(dir.x / radii.x, dir.y / radii.y, dir.z / radii.z);
                Vertex(center + Vector3.Scale(dir, radii), n, new Vector2((float)s / segments, (float)r / rings) * Mathf.Max(radii.x, radii.z) * 3f);
            }
        }
        int row = segments + 1;
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * row + s, b = a + row, c = b + 1, d = a + 1;
                t.Add(a); t.Add(b); t.Add(c);
                t.Add(a); t.Add(c); t.Add(d);
            }
    }
}

/// <summary>日式村莊的模型。所有模型的原點在地面、正面朝 +Z。</summary>
public static class JapaneseVillageMeshes
{
    // ---------------------------------------------------------------- 房屋

    /// <summary>暖簾：掛在門上的幾片布（原點在橫桿中心）</summary>
    public static JVMeshKit Noren(JVMat cloth, float width = 2.2f, float length = 0.75f, int strips = 4)
    {
        var kit = new JVMeshKit();
        kit.Cylinder(JVMat.WoodLight, new Vector3(-width * 0.5f - 0.1f, 0f, 0f), new Vector3(width * 0.5f + 0.1f, 0f, 0f), 0.022f, 0.022f, 6);
        float w = width / strips;
        for (int i = 0; i < strips; i++)
        {
            float x = -width * 0.5f + w * (i + 0.5f);
            kit.Box(cloth, new Vector3(x, -length * 0.5f - 0.01f, 0.005f), new Vector3(w - 0.025f, length, 0.018f));
            kit.Box(JVMat.White, new Vector3(x, -length * 0.42f, 0.017f), new Vector3((w - 0.025f) * 0.42f, length * 0.32f, 0.008f));
        }
        return kit;
    }

    // ---------------------------------------------------------------- 習武場與卷軸

    /// <summary>木樁人偶的底座和主幹（原點在地面）</summary>
    public static JVMeshKit DummyBase()
    {
        var kit = new JVMeshKit();
        kit.Cylinder(JVMat.Stone, Vector3.zero, new Vector3(0f, 0.18f, 0f), 0.42f, 0.36f, 12);
        kit.Cylinder(JVMat.WoodDark, new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.8f, 0f), 0.2f, 0.18f, 10);
        return kit;
    }

    /// <summary>木樁人偶的上半部（轉軸在主幹中心）：軀幹、纏繩、頭、一隻固定的短腿，正面朝 +Z</summary>
    public static JVMeshKit DummyBody()
    {
        var kit = new JVMeshKit();
        kit.Cylinder(JVMat.WoodDark, new Vector3(0f, 0.8f, 0f), new Vector3(0f, 1.72f, 0f), 0.18f, 0.16f, 10);
        kit.Cylinder(JVMat.Straw, new Vector3(0f, 1.02f, 0f), new Vector3(0f, 1.3f, 0f), 0.2f, 0.2f, 10);
        kit.Cylinder(JVMat.WoodLight, new Vector3(0f, 1.72f, 0f), new Vector3(0f, 2.02f, 0f), 0.13f, 0.11f, 10);
        kit.Cylinder(JVMat.WoodLight, new Vector3(0f, 0.72f, 0.12f), new Vector3(0.14f, 0.42f, 0.55f), 0.05f, 0.04f, 6);
        return kit;
    }

    /// <summary>會揮動的兩隻木臂（原點在肩膀中間，往 +Z 伸出去）</summary>
    public static JVMeshKit DummyArms()
    {
        var kit = new JVMeshKit();
        foreach (float side in new[] { -1f, 1f })
            kit.Cylinder(JVMat.WoodLight, new Vector3(side * 0.1f, 0f, 0.1f), new Vector3(side * 0.24f, 0.05f, 0.82f), 0.05f, 0.04f, 6);
        return kit;
    }

    /// <summary>吊在木條下的小燈籠（lit = 過關時才顯示、會發光的那個）</summary>
    public static JVMeshKit BeamLantern(bool lit)
    {
        var kit = new JVMeshKit();
        kit.Ellipsoid(lit ? JVMat.LanternGlow : JVMat.White, new Vector3(0f, -0.26f, 0f), new Vector3(0.16f, 0.22f, 0.16f) * (lit ? 1.03f : 1f));
        if (!lit) kit.Cylinder(JVMat.Rope, new Vector3(0f, 0f, 0f), new Vector3(0f, -0.08f, 0f), 0.012f, 0.012f, 5);
        return kit;
    }

    /// <summary>線裝書：封面 + 書頁 + 書脊的綁線 + 題籤（原點在底部中心，書脊在 -X）</summary>
    public static JVMeshKit Book(JVMat cover)
    {
        var kit = new JVMeshKit();
        const float w = 0.24f, d = 0.34f, h = 0.035f;
        kit.Box(cover, new Vector3(0f, h - 0.003f, 0f), new Vector3(w, 0.006f, d));
        kit.Box(cover, new Vector3(0f, 0.003f, 0f), new Vector3(w, 0.006f, d));
        kit.Box(JVMat.White, new Vector3(0.004f, h * 0.5f, 0f), new Vector3(w - 0.012f, h - 0.012f, d - 0.012f));
        for (int i = 0; i < 4; i++)
            kit.Box(JVMat.Rope, new Vector3(-w * 0.5f + 0.02f, h * 0.5f, -d * 0.36f + i * d * 0.24f), new Vector3(0.008f, h + 0.002f, 0.006f));
        kit.Box(JVMat.White, new Vector3(-w * 0.12f, h + 0.0015f, d * 0.08f), new Vector3(0.045f, 0.002f, d * 0.6f));
        return kit;
    }

    /// <summary>平放在地上的一張紙（古畫、詩）：只有上面一面，UV 0~1，畫的上方朝 +Z</summary>
    public static JVMeshKit Sheet(float width, float height)
    {
        var kit = new JVMeshKit();
        float hw = width * 0.5f, hh = height * 0.5f;
        kit.Card(JVMat.White, new Vector3(-hw, 0f, -hh), new Vector3(-hw, 0f, hh), new Vector3(hw, 0f, hh), new Vector3(hw, 0f, -hh), Vector3.up);
        return kit;
    }

    // ---------------------------------------------------------------- 神社、石燈籠、鳥居

    /// <summary>小祠堂：房屋的迷你版 + 注連繩</summary>
    public static JVMeshKit ShrineRope(float width)
    {
        var kit = new JVMeshKit();
        const int segments = 10;
        for (int i = 0; i < segments; i++)
        {
            float x0 = -width * 0.5f + width * i / segments, x1 = -width * 0.5f + width * (i + 1) / segments;
            float s0 = Mathf.Sin(Mathf.PI * i / segments), s1 = Mathf.Sin(Mathf.PI * (i + 1) / segments);
            kit.Cylinder(JVMat.Rope, new Vector3(x0, -s0 * 0.22f, 0f), new Vector3(x1, -s1 * 0.22f, 0f), 0.05f + s0 * 0.05f, 0.05f + s1 * 0.05f, 6);
        }
        for (int i = 1; i < 5; i++)
        {
            float x = -width * 0.5f + width * i / 5f;
            float s = Mathf.Sin(Mathf.PI * i / 5f) * 0.22f;
            kit.Box(JVMat.White, new Vector3(x, -s - 0.24f, 0f), new Vector3(0.1f, 0.16f, 0.012f));
            kit.Box(JVMat.White, new Vector3(x + 0.05f, -s - 0.4f, 0f), new Vector3(0.1f, 0.16f, 0.012f));
        }
        return kit;
    }

    public static JVMeshKit OfferingBox()
    {
        var kit = new JVMeshKit();
        kit.Box(JVMat.WoodDark, new Vector3(0f, 0.32f, 0f), new Vector3(1.2f, 0.64f, 0.6f));
        kit.Box(JVMat.WoodLight, new Vector3(0f, 0.66f, 0f), new Vector3(1.32f, 0.05f, 0.7f));
        return kit;
    }

    // ---------------------------------------------------------------- 海岸

    /// <summary>小木船：長 length，原點在船底中央，船頭朝 +Z</summary>
    public static JVMeshKit Boat(float length = 6.4f, float beam = 1.5f, float depth = 0.68f, bool fishingGear = true)
    {
        var kit = new JVMeshKit();
        const int stations = 10;
        Vector3 Outer(int i, int level, float side)
        {
            float s = (float)i / stations, c = 2f * s - 1f;
            float halfBeam = beam * 0.5f * Mathf.Pow(Mathf.Clamp01(1f - c * c), 0.55f);
            float sheer = depth + 0.5f * c * c * Mathf.Abs(c) * (c > 0f ? 1f : 0.35f);
            float keel = 0.2f * c * c * c * c;
            float z = c * length * 0.5f;
            return level switch
            {
                0 => new Vector3(0f, keel, z),
                1 => new Vector3(side * halfBeam * 0.72f, Mathf.Lerp(keel, sheer, 0.3f), z),
                _ => new Vector3(side * halfBeam, sheer, z),
            };
        }

        for (int i = 0; i < stations; i++)
            for (int level = 0; level < 2; level++)
            {
                Vector3 a0 = Outer(i, level, 1f), b0 = Outer(i, level + 1, 1f), c0 = Outer(i + 1, level + 1, 1f), d0 = Outer(i + 1, level, 1f);
                kit.Quad(JVMat.WoodDark, a0, b0, c0, d0);
                kit.Quad(JVMat.WoodLight, d0, c0, b0, a0); // 內側
                Vector3 a1 = Outer(i + 1, level, -1f), b1 = Outer(i + 1, level + 1, -1f), c1 = Outer(i, level + 1, -1f), d1 = Outer(i, level, -1f);
                kit.Quad(JVMat.WoodDark, a1, b1, c1, d1);
                kit.Quad(JVMat.WoodLight, d1, c1, b1, a1);
            }

        // 坐板與船緣
        foreach (float z in new[] { -length * 0.22f, length * 0.04f, length * 0.26f })
            kit.Box(JVMat.WoodLight, new Vector3(0f, depth * 0.78f, z), new Vector3(beam * 0.9f, 0.05f, 0.26f));
        for (int i = 1; i < stations - 1; i++)
        {
            foreach (float side in new[] { 1f, -1f })
            {
                Vector3 p0 = Outer(i, 2, side), p1 = Outer(i + 1, 2, side);
                kit.Cylinder(JVMat.WoodLight, p0, p1, 0.03f, 0.03f, 4, false, false);
            }
        }
        if (fishingGear)
        {
            kit.Ellipsoid(JVMat.Net, new Vector3(0.1f, depth * 0.7f, -length * 0.3f), new Vector3(0.45f, 0.2f, 0.42f), 4, 8);
            kit.Cylinder(JVMat.WoodLight, new Vector3(-0.3f, depth * 0.85f, 0.4f), new Vector3(0.1f, depth * 0.9f, -2.3f), 0.022f, 0.018f, 5);
        }
        return kit;
    }

    /// <summary>曬網架：兩根木柱 + 一張網</summary>
    public static JVMeshKit NetRack(float width = 4.2f, float height = 2.1f)
    {
        var kit = new JVMeshKit();
        foreach (float x in new[] { -width * 0.5f, width * 0.5f })
            kit.Cylinder(JVMat.WoodDark, new Vector3(x, -0.3f, 0f), new Vector3(x, height, 0f), 0.06f, 0.05f, 6);
        kit.Cylinder(JVMat.WoodLight, new Vector3(-width * 0.5f - 0.25f, height - 0.06f, 0f), new Vector3(width * 0.5f + 0.25f, height - 0.06f, 0f), 0.035f, 0.035f, 6);
        const int folds = 6;
        for (int i = 0; i < folds; i++)
        {
            float x0 = -width * 0.5f + width * i / folds, x1 = -width * 0.5f + width * (i + 1) / folds;
            float y0 = height - 0.1f, y1 = 0.45f + Mathf.Sin(i * 1.4f) * 0.08f, y2 = 0.45f + Mathf.Sin((i + 1) * 1.4f) * 0.08f;
            float z0 = (i % 2 == 0) ? -0.05f : 0.05f, z1 = (i % 2 == 0) ? 0.05f : -0.05f;
            Vector3 n = Vector3.forward * 0.4f + Vector3.up;
            kit.Card(JVMat.Net, new Vector3(x0, y1, z0), new Vector3(x0, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y2, z1), n);
        }
        return kit;
    }

    /// <summary>木頭架：掛魚乾</summary>
    public static JVMeshKit FishRack(float width = 3f, float height = 1.75f)
    {
        var kit = new JVMeshKit();
        foreach (float x in new[] { -width * 0.5f, width * 0.5f })
        {
            kit.Cylinder(JVMat.WoodDark, new Vector3(x, -0.3f, -0.45f), new Vector3(x, height, 0f), 0.045f, 0.04f, 5);
            kit.Cylinder(JVMat.WoodDark, new Vector3(x, -0.3f, 0.45f), new Vector3(x, height, 0f), 0.045f, 0.04f, 5);
        }
        kit.Cylinder(JVMat.WoodLight, new Vector3(-width * 0.5f - 0.15f, height, 0f), new Vector3(width * 0.5f + 0.15f, height, 0f), 0.03f, 0.03f, 6);
        var rng = new System.Random(9);
        for (int i = 0; i < 11; i++)
        {
            float x = -width * 0.5f + 0.2f + (width - 0.4f) * i / 10f;
            float len = 0.34f + (float)rng.NextDouble() * 0.1f;
            kit.Cylinder(JVMat.Rope, new Vector3(x, height, 0f), new Vector3(x, height - 0.14f, 0f), 0.006f, 0.006f, 3, false, false);
            kit.Ellipsoid(JVMat.ClothGrey, new Vector3(x, height - 0.14f - len * 0.5f, 0f), new Vector3(0.042f, len * 0.5f, 0.016f), 4, 6);
        }
        return kit;
    }

    // ---------------------------------------------------------------- 小道具

    public static JVMeshKit WaterJar()
    {
        var kit = new JVMeshKit();
        kit.Lathe(JVMat.Ceramic, Vector3.zero, new[]
        {
            new Vector2(0f, 0f), new Vector2(0.22f, 0f), new Vector2(0.38f, 0.22f), new Vector2(0.41f, 0.44f),
            new Vector2(0.32f, 0.72f), new Vector2(0.34f, 0.78f), new Vector2(0f, 0.78f),
        }, 12);
        return kit;
    }

    public static JVMeshKit WoodPile()
    {
        var kit = new JVMeshKit();
        for (int row = 0; row < 3; row++)
            for (int i = 0; i < 4 - row; i++)
            {
                float x = -0.33f + row * 0.11f + i * 0.22f, y = 0.11f + row * 0.19f;
                kit.Cylinder(JVMat.WoodLight, new Vector3(x, y, -0.65f), new Vector3(x, y, 0.65f), 0.105f, 0.105f, 6);
            }
        return kit;
    }

    public static JVMeshKit TeaBench(float width = 1.9f)
    {
        var kit = new JVMeshKit();
        kit.Box(JVMat.ClothRed, new Vector3(0f, 0.46f, 0f), new Vector3(width, 0.07f, 0.72f));
        foreach (float x in new[] { -width * 0.42f, width * 0.42f })
            foreach (float z in new[] { -0.27f, 0.27f })
                kit.Box(JVMat.WoodDark, new Vector3(x, 0.21f, z), new Vector3(0.07f, 0.44f, 0.07f));
        return kit;
    }

    /// <summary>大紅傘（野點傘）</summary>
    public static JVMeshKit Parasol(float radius = 1.7f, float height = 2.5f)
    {
        var kit = new JVMeshKit();
        kit.Cylinder(JVMat.WoodLight, new Vector3(0f, -0.3f, 0f), new Vector3(0f, height + 0.45f, 0f), 0.03f, 0.024f, 6);
        kit.Lathe(JVMat.ClothRed, new Vector3(0f, height, 0f), new[]
        {
            new Vector2(radius, 0f), new Vector2(radius * 0.68f, 0.3f), new Vector2(radius * 0.3f, 0.48f), new Vector2(0.02f, 0.56f),
        }, 14);
        kit.Lathe(JVMat.ClothRed, new Vector3(0f, height, 0f), new[]
        {
            new Vector2(0.02f, 0.56f), new Vector2(radius * 0.3f, 0.48f), new Vector2(radius * 0.68f, 0.3f), new Vector2(radius, 0f),
        }, 14);
        return kit;
    }

    // ---------------------------------------------------------------- 植物

    /// <summary>草藥：一叢綠葉 + 發亮的紅果，在竹林地面上也看得到</summary>
    public static JVMeshKit Herb()
    {
        var kit = new JVMeshKit();
        const int leaves = 9;
        for (int i = 0; i < leaves; i++)
        {
            float angle = i * Mathf.PI * 2f / leaves;
            float lift = i % 2 == 0 ? 0.18f : 0.3f;
            var dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            kit.Ellipsoid(JVMat.HerbLeaf, dir * 0.15f + Vector3.up * lift, new Vector3(0.09f, 0.035f, 0.09f) + dir * 0.08f, 3, 7);
            kit.Cylinder(JVMat.HerbLeaf, Vector3.zero, dir * 0.14f + Vector3.up * lift, 0.014f, 0.01f, 4, false, false);
        }
        kit.Cylinder(JVMat.HerbLeaf, Vector3.zero, Vector3.up * 0.46f, 0.016f, 0.01f, 5, false, false);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * 0.5f + 0.4f;
            kit.Ellipsoid(JVMat.LanternGlow, new Vector3(Mathf.Cos(angle) * 0.05f, 0.47f + i * 0.015f, Mathf.Sin(angle) * 0.05f), Vector3.one * 0.038f, 3, 6);
        }
        return kit;
    }

    /// <summary>行商的攤位：木箱、竹簍、瓶罐，背後一根竿子掛布招牌</summary>
    public static JVMeshKit MerchantStall()
    {
        var kit = new JVMeshKit();
        kit.Box(JVMat.WoodLight, new Vector3(0f, 0.42f, 0f), new Vector3(2.4f, 0.1f, 1f));
        foreach (float x in new[] { -1.05f, 1.05f })
            foreach (float z in new[] { -0.38f, 0.38f })
                kit.Box(JVMat.WoodDark, new Vector3(x, 0.19f, z), new Vector3(0.1f, 0.5f, 0.1f));
        kit.Box(JVMat.ClothIndigo, new Vector3(0f, 0.475f, 0f), new Vector3(2.46f, 0.015f, 0.92f));
        kit.Lathe(JVMat.Ceramic, new Vector3(-0.8f, 0.48f, 0.08f), new[]
        {
            new Vector2(0f, 0f), new Vector2(0.1f, 0f), new Vector2(0.15f, 0.14f), new Vector2(0.07f, 0.3f), new Vector2(0.08f, 0.36f), new Vector2(0f, 0.36f),
        }, 10);
        kit.Lathe(JVMat.White, new Vector3(-0.42f, 0.48f, -0.14f), new[]
        {
            new Vector2(0f, 0f), new Vector2(0.07f, 0f), new Vector2(0.1f, 0.1f), new Vector2(0.04f, 0.22f), new Vector2(0.05f, 0.27f), new Vector2(0f, 0.27f),
        }, 10);
        kit.Lathe(JVMat.Straw, new Vector3(0.5f, 0.48f, 0f), new[]
        {
            new Vector2(0f, 0f), new Vector2(0.26f, 0f), new Vector2(0.34f, 0.2f), new Vector2(0.3f, 0.2f), new Vector2(0f, 0.12f),
        }, 12);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 1.26f;
            kit.Ellipsoid(JVMat.HerbLeaf, new Vector3(0.5f + Mathf.Cos(angle) * 0.13f, 0.72f, Mathf.Sin(angle) * 0.13f), new Vector3(0.09f, 0.05f, 0.09f), 3, 6);
        }

        // 布招牌
        kit.Cylinder(JVMat.WoodDark, new Vector3(1.32f, -0.2f, -0.45f), new Vector3(1.32f, 3.1f, -0.45f), 0.04f, 0.03f, 6);
        kit.Cylinder(JVMat.WoodDark, new Vector3(1.32f, 3.0f, -0.45f), new Vector3(0.62f, 3.0f, -0.45f), 0.02f, 0.02f, 5);
        kit.Box(JVMat.ClothIndigo, new Vector3(0.97f, 2.2f, -0.45f), new Vector3(0.56f, 1.55f, 0.02f));
        kit.Box(JVMat.White, new Vector3(0.97f, 2.32f, -0.462f), new Vector3(0.26f, 0.26f, 0.008f));
        kit.Box(JVMat.White, new Vector3(0.97f, 2.32f, -0.438f), new Vector3(0.26f, 0.26f, 0.008f));
        return kit;
    }

    // ---------------------------------------------------------------- 村民

    public enum NpcHat { None, Kasa, Headband, HairBun, Bald }

    public struct NpcSpec
    {
        public JVMat robe, obi;
        public JVMat hair;
        public NpcHat hat;
        public bool backpack;     // 行商的木箱
        public bool woodBundle;   // 樵夫背的竹子
        public bool fishingRod;   // 釣竿 + 釣線
        public float rodDrop;     // 釣線往下垂多長（站在棧橋上到水面的高度）
        public bool cane;         // 拐杖
        public bool fishBasket;   // 手邊的魚簍
    }

    /// <summary>村民模型：和 Planet 的商人一樣是簡單幾何組成，高約 1.75 m，原點在腳底</summary>
    public static JVMeshKit Villager(in NpcSpec s, bool separateLimbs = false)
    {
        var kit = new JVMeshKit();

        // 和服：下擺外張的旋轉體
        kit.Lathe(s.robe, Vector3.zero, new[]
        {
            new Vector2(0f, 0.1f), new Vector2(0.29f, 0.1f), new Vector2(0.27f, 0.45f), new Vector2(0.21f, 0.82f),
            new Vector2(0.2f, 0.95f), new Vector2(0.24f, 1.24f), new Vector2(0.21f, 1.36f), new Vector2(0.075f, 1.42f), new Vector2(0f, 1.42f),
        }, 12, 1f, 0.82f);

        // 衣襟（V 字領）
        var collarL = Quaternion.Euler(0f, 0f, -24f);
        var collarR = Quaternion.Euler(0f, 0f, 24f);
        kit.Box(JVMat.White, new Vector3(-0.055f, 1.25f, 0.19f), new Vector3(0.05f, 0.25f, 0.02f), collarL);
        kit.Box(JVMat.White, new Vector3(0.055f, 1.25f, 0.19f), new Vector3(0.05f, 0.25f, 0.02f), collarR);

        // 腰帶
        kit.Lathe(s.obi, Vector3.zero, new[]
        {
            new Vector2(0.222f, 0.83f), new Vector2(0.215f, 1.03f),
        }, 12, 1f, 0.84f);
        kit.Box(s.obi, new Vector3(0f, 0.95f, -0.21f), new Vector3(0.26f, 0.2f, 0.09f));

        // 袖子（把手放在身前）+ 手；會走路的村民手腳是獨立的部位（VillagerArm / VillagerFoot）
        if (!separateLimbs) foreach (float side in new[] { 1f, -1f })
        {
            var shoulder = new Vector3(side * 0.235f, 1.3f, 0f);
            var elbow = new Vector3(side * 0.29f, 1.03f, 0.03f);
            var wrist = new Vector3(side * 0.17f, 0.95f, 0.22f);
            kit.Cylinder(s.robe, shoulder, elbow, 0.075f, 0.1f, 8);
            kit.Cylinder(s.robe, elbow, wrist, 0.1f, 0.11f, 8);
            kit.Ellipsoid(JVMat.Skin, wrist + new Vector3(-side * 0.02f, -0.01f, 0.08f), new Vector3(0.042f, 0.045f, 0.055f), 4, 7);
            kit.Box(JVMat.Black, new Vector3(side * 0.1f, 0.04f, 0.05f), new Vector3(0.11f, 0.05f, 0.24f));
        }

        // 頭、五官
        var head = new Vector3(0f, 1.57f, 0.01f);
        kit.Cylinder(JVMat.Skin, new Vector3(0f, 1.38f, 0f), new Vector3(0f, 1.48f, 0.01f), 0.06f, 0.055f, 8);
        kit.Ellipsoid(JVMat.Skin, head, new Vector3(0.122f, 0.145f, 0.13f), 6, 12);
        foreach (float side in new[] { 1f, -1f })
        {
            kit.Ellipsoid(JVMat.Black, head + new Vector3(side * 0.048f, 0.012f, 0.113f), new Vector3(0.015f, 0.018f, 0.012f), 3, 6);
            kit.Box(s.hair == JVMat.HairGrey ? JVMat.HairGrey : JVMat.Black, head + new Vector3(side * 0.05f, 0.05f, 0.118f), new Vector3(0.042f, 0.009f, 0.01f));
        }
        kit.Box(JVMat.ClothRed, head + new Vector3(0f, -0.075f, 0.118f), new Vector3(0.035f, 0.006f, 0.012f));

        // 頭髮、帽子
        if (s.hat != NpcHat.Bald)
            kit.Ellipsoid(s.hair, head + new Vector3(0f, 0.04f, -0.024f), new Vector3(0.132f, 0.13f, 0.132f), 6, 12);
        switch (s.hat)
        {
            case NpcHat.Kasa:
                {
                    var p = new Vector3(0f, 1.66f, 0f);
                    var profile = new[] { new Vector2(0.47f, 0f), new Vector2(0.27f, 0.13f), new Vector2(0.03f, 0.27f), new Vector2(0f, 0.29f) };
                    kit.Lathe(JVMat.Straw, p, profile, 14);
                    var inner = new[] { profile[3], profile[2], profile[1], profile[0] };
                    kit.Lathe(JVMat.WoodLight, p, inner, 14);
                    break;
                }
            case NpcHat.Headband:
                kit.Lathe(JVMat.White, head + Vector3.up * 0.03f, new[] { new Vector2(0.134f, 0f), new Vector2(0.135f, 0.045f) }, 12);
                kit.Ellipsoid(s.hair, head + new Vector3(0f, 0.15f, -0.04f), new Vector3(0.03f, 0.055f, 0.08f), 4, 8);
                break;
            case NpcHat.HairBun:
                kit.Ellipsoid(s.hair, head + new Vector3(0f, 0.14f, -0.11f), new Vector3(0.09f, 0.075f, 0.08f), 5, 9);
                kit.Cylinder(JVMat.Lacquer, head + new Vector3(-0.15f, 0.16f, -0.11f), head + new Vector3(0.15f, 0.13f, -0.11f), 0.006f, 0.006f, 4);
                break;
            case NpcHat.Bald:
                kit.Ellipsoid(JVMat.HairGrey, head + new Vector3(0f, -0.045f, -0.05f), new Vector3(0.128f, 0.075f, 0.098f), 4, 10);
                break;
        }

        if (s.backpack)
        {
            kit.Box(JVMat.WoodDark, new Vector3(0f, 1.1f, -0.38f), new Vector3(0.55f, 0.85f, 0.34f));
            kit.Box(JVMat.WoodLight, new Vector3(0f, 1.1f, -0.2f), new Vector3(0.58f, 0.06f, 0.05f));
            kit.Box(JVMat.ClothIndigo, new Vector3(0f, 1.6f, -0.38f), new Vector3(0.62f, 0.24f, 0.4f));
            foreach (float side in new[] { 1f, -1f })
                kit.Box(JVMat.Rope, new Vector3(side * 0.16f, 1.28f, -0.03f), new Vector3(0.035f, 0.32f, 0.4f), Quaternion.Euler(30f, 0f, 0f));
        }
        if (s.woodBundle)
        {
            var rng = new System.Random(5);
            for (int i = 0; i < 9; i++)
            {
                float x = -0.2f + 0.05f * i, z = -0.3f - (float)rng.NextDouble() * 0.14f;
                kit.Cylinder(JVMat.BambooCulm, new Vector3(x * 0.8f, 0.3f, z), new Vector3(x * 1.2f, 2.5f, z - 0.12f), 0.03f, 0.022f, 5);
            }
            kit.Lathe(JVMat.Rope, new Vector3(0f, 1.18f, -0.38f), new[] { new Vector2(0.27f, 0f), new Vector2(0.27f, 0.05f) }, 10, 1f, 0.45f);
        }
        if (s.cane)
            kit.Cylinder(JVMat.WoodDark, new Vector3(0.36f, 0f, 0.34f), new Vector3(0.22f, 1.02f, 0.3f), 0.018f, 0.018f, 5);
        if (s.fishBasket)
        {
            kit.Lathe(JVMat.Straw, new Vector3(-0.56f, 0f, 0.18f), new[]
            {
                new Vector2(0f, 0f), new Vector2(0.22f, 0f), new Vector2(0.28f, 0.2f), new Vector2(0.18f, 0.42f), new Vector2(0.14f, 0.42f),
            }, 10);
        }
        if (s.fishingRod)
        {
            var grip = new Vector3(0.17f, 1.0f, 0.3f);
            var tip = new Vector3(0.45f, 2.75f, 3.7f);
            kit.Cylinder(JVMat.BambooCulm, grip, tip, 0.025f, 0.008f, 5);
            var bobber = new Vector3(tip.x, -s.rodDrop + 0.04f, tip.z);
            kit.Cylinder(JVMat.White, tip, bobber, 0.0045f, 0.0045f, 3, false, false);
            kit.Ellipsoid(JVMat.ClothRed, bobber, new Vector3(0.05f, 0.07f, 0.05f), 4, 7);
        }
        return kit;
    }

    public static readonly Vector3 ArmShoulderLeft = new Vector3(-0.235f, 1.3f, 0f);
    public static readonly Vector3 ArmShoulderRight = new Vector3(0.235f, 1.3f, 0f);
    public static readonly Vector3 FootLeft = new Vector3(-0.1f, 0f, 0.05f);
    public static readonly Vector3 FootRight = new Vector3(0.1f, 0f, 0.05f);

    /// <summary>會擺動的手臂：原點在肩膀，自然下垂（side：左 -1、右 +1）</summary>
    public static JVMeshKit VillagerArm(JVMat robe, float side)
    {
        var kit = new JVMeshKit();
        var elbow = new Vector3(side * 0.05f, -0.27f, 0f);
        var wrist = new Vector3(side * 0.04f, -0.5f, 0.05f);
        kit.Cylinder(robe, Vector3.zero, elbow, 0.075f, 0.1f, 8);
        kit.Cylinder(robe, elbow, wrist, 0.1f, 0.11f, 8);
        kit.Ellipsoid(JVMat.Skin, wrist + new Vector3(0f, -0.05f, 0.015f), new Vector3(0.042f, 0.055f, 0.05f), 4, 7);
        return kit;
    }

    /// <summary>草鞋：原點在腳底</summary>
    public static JVMeshKit VillagerFoot()
    {
        var kit = new JVMeshKit();
        kit.Box(JVMat.Black, new Vector3(0f, 0.04f, 0f), new Vector3(0.11f, 0.05f, 0.24f));
        return kit;
    }
}
#endif
