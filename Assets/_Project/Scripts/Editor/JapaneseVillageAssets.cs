#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>日式村莊用到的材質種類（JapaneseVillageMeshes 用這個當子網格的材質）</summary>
public enum JVMat
{
    Stone, WoodDark, WoodLight,     BambooCulm,     Skin, Hair, HairGrey, Straw, Black, White,
    ClothIndigo, ClothRed, ClothOchre, ClothGrey, ClothGreen, ClothPurple, ClothBrown, ClothTeal,
    Lacquer, LanternGlow, Rope, Ceramic, Net, HerbLeaf,
}

/// <summary>
/// 日式村莊的程式產生貼圖與材質。專案裡沒有日式素材，所以貼圖都是原創、用程式畫的：
/// 同樣的程式會畫出一模一樣的 PNG，可以重複執行，想換色直接改這裡或改 Materials/JapaneseVillage 底下的材質。
/// </summary>
public static class JapaneseVillageAssets
{
    public const string MatDir = "Assets/_Project/Materials/JapaneseVillage";

    static readonly Dictionary<JVMat, Material> materials = new Dictionary<JVMat, Material>();

    public static Material Get(JVMat m) => materials[m];

    // ---------------------------------------------------------------- 材質

    public static void BuildAll()
    {
        EnsureFolder(MatDir);
        materials.Clear();

        // 貼圖一張代表幾公尺：網格的 UV 是用公尺算的，材質的 Tiling = 1 / 公尺
        var (thatch, _) = TextureWithNormal("T_JV_Thatch", 512, ThatchPixel, 1.5f);
        var (woodD, woodDN) = TextureWithNormal("T_JV_WoodDark", 512, (float u, float v, out Color c, out float h) => WoodPixel(u, v, new Color(0.33f, 0.235f, 0.16f), 11, out c, out h), 1.3f);
        var (woodL, woodLN) = TextureWithNormal("T_JV_WoodLight", 512, (float u, float v, out Color c, out float h) => WoodPixel(u, v, new Color(0.55f, 0.40f, 0.25f), 23, out c, out h), 1.3f);
        var (stone, stoneN) = TextureWithNormal("T_JV_Stone", 512, StonePixel, 0.7f);
        var culm = PlainTexture("T_JV_BambooCulm", 128, CulmPixel);
        var netTex = AlphaTexture("T_JV_Net", 256, DrawNet);

        Lit(JVMat.Stone, Color.white, stone, stoneN, 2f, 0.12f);
        Lit(JVMat.WoodDark, Color.white, woodD, woodDN, 1.25f, 0.15f);
        Lit(JVMat.WoodLight, Color.white, woodL, woodLN, 1.25f, 0.18f);
        Lit(JVMat.BambooCulm, Color.white, culm, null, 1f, 0.22f);
        Lit(JVMat.Straw, new Color(0.86f, 0.74f, 0.48f), thatch, null, 0.8f, 0.03f);
        AlphaClip(JVMat.Net, netTex, 0.4f);

        Plain(JVMat.Skin, new Color(0.93f, 0.75f, 0.60f), 0.25f);
        Plain(JVMat.Hair, new Color(0.05f, 0.04f, 0.04f), 0.35f);
        Plain(JVMat.HairGrey, new Color(0.68f, 0.68f, 0.66f), 0.25f);
        Plain(JVMat.Black, new Color(0.04f, 0.04f, 0.05f), 0.3f);
        Plain(JVMat.White, new Color(0.92f, 0.91f, 0.86f), 0.1f);
        Plain(JVMat.ClothIndigo, new Color(0.10f, 0.17f, 0.36f), 0.08f);
        Plain(JVMat.ClothRed, new Color(0.58f, 0.11f, 0.11f), 0.08f);
        Plain(JVMat.ClothOchre, new Color(0.74f, 0.53f, 0.20f), 0.08f);
        Plain(JVMat.ClothGrey, new Color(0.37f, 0.38f, 0.40f), 0.08f);
        Plain(JVMat.ClothGreen, new Color(0.24f, 0.38f, 0.25f), 0.08f);
        Plain(JVMat.ClothPurple, new Color(0.34f, 0.20f, 0.42f), 0.08f);
        Plain(JVMat.ClothBrown, new Color(0.33f, 0.21f, 0.13f), 0.08f);
        Plain(JVMat.ClothTeal, new Color(0.13f, 0.40f, 0.42f), 0.08f);
        Plain(JVMat.Lacquer, new Color(0.72f, 0.12f, 0.07f), 0.55f);
        Plain(JVMat.Rope, new Color(0.72f, 0.62f, 0.40f), 0.05f);
        Plain(JVMat.Ceramic, new Color(0.24f, 0.17f, 0.13f), 0.6f);
        Plain(JVMat.HerbLeaf, new Color(0.30f, 0.62f, 0.24f), 0.15f);

        var glow = Plain(JVMat.LanternGlow, new Color(0.95f, 0.30f, 0.15f), 0.3f);
        glow.EnableKeyword("_EMISSION");
        glow.SetColor("_EmissionColor", new Color(1.1f, 0.42f, 0.16f));
        glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        AssetDatabase.SaveAssets();
    }

    /// <summary>地形的細節草貼圖（BuildAll 之後才有值）</summary>
    static Material Lit(JVMat id, Color color, Texture2D map, Texture2D normal, float metersPerTile, float smoothness)
    {
        var m = NewMaterial(id);
        m.SetColor("_BaseColor", color);
        m.SetTexture("_BaseMap", map);
        float scale = metersPerTile > 0f ? 1f / metersPerTile : 1f;
        m.SetTextureScale("_BaseMap", new Vector2(scale, scale));
        m.SetFloat("_Smoothness", smoothness);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Plain(JVMat id, Color color, float smoothness)
    {
        var m = NewMaterial(id);
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>挖空的雙面材質（竹葉、漁網）</summary>
    static void AlphaClip(JVMat id, Texture2D map, float cutoff)
    {
        var m = NewMaterial(id);
        m.SetColor("_BaseColor", Color.white);
        m.SetTexture("_BaseMap", map);
        m.SetFloat("_Smoothness", 0.05f);
        m.SetFloat("_AlphaClip", 1f);
        m.SetFloat("_Cutoff", cutoff);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_ALPHATEST_ON");
        m.SetOverrideTag("RenderType", "TransparentCutout");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(m);
    }

    static Material NewMaterial(JVMat id)
    {
        string path = $"{MatDir}/M_JV_{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.enableInstancing = true;
        materials[id] = m;
        return m;
    }

    /// <summary>在 MatDir 底下建立或更新一個使用指定 Shader 的材質（天空、地形）</summary>
    public static Material ShaderMaterial(string name, string shader)
    {
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find(shader));
            AssetDatabase.CreateAsset(m, path);
        }
        return m;
    }

    // ---------------------------------------------------------------- 貼圖輸出

    delegate void Pixel(float u, float v, out Color color, out float height);

    static (Texture2D, Texture2D) TextureWithNormal(string name, int size, Pixel f, float normalStrength)
    {
        var colors = new Color[size * size];
        var heights = new float[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                f((x + 0.5f) / size, (y + 0.5f) / size, out colors[y * size + x], out heights[y * size + x]);
            }

        var normals = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float l = heights[y * size + (x + size - 1) % size], r = heights[y * size + (x + 1) % size];
                float d = heights[(y + size - 1) % size * size + x], t = heights[(y + 1) % size * size + x];
                var n = new Vector3((l - r) * normalStrength * size / 64f, (d - t) * normalStrength * size / 64f, 1f).normalized;
                normals[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

        var albedo = SavePng(name, size, colors);
        Import(albedo, TextureImporterType.Default, TextureWrapMode.Repeat, false);
        var normal = SavePng(name + "_n", size, normals);
        Import(normal, TextureImporterType.NormalMap, TextureWrapMode.Repeat, false);
        return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedo), AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
    }

    static Texture2D PlainTexture(string name, int size, Pixel f)
    {
        var colors = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                f((x + 0.5f) / size, (y + 0.5f) / size, out colors[y * size + x], out _);
        string path = SavePng(name, size, colors);
        Import(path, TextureImporterType.Default, TextureWrapMode.Repeat, false);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D AlphaTexture(string name, int size, System.Action<Color[], int> draw)
    {
        var colors = new Color[size * size];
        draw(colors, size);
        string path = SavePng(name, size, colors);
        Import(path, TextureImporterType.Default, TextureWrapMode.Clamp, true);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static string SavePng(string name, int size, Color[] colors)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(colors);
        tex.Apply();
        string path = $"{MatDir}/{name}.png";
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        return path;
    }

    static void Import(string path, TextureImporterType type, TextureWrapMode wrap, bool alpha)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = type;
        importer.wrapMode = wrap;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 4;
        importer.mipmapEnabled = true;
        if (alpha)
        {
            importer.alphaIsTransparency = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.45f;
        }
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- 貼圖內容

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1274126177;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0x7fffffff) / 2147483647f;
        }
    }

    /// <summary>可以無縫重複的雜訊：u、v 在 0~1，px、py 是格子數</summary>
    static float Noise(float u, float v, int px, int py, int seed)
    {
        float x = u * px, y = v * py;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        int x0 = ((xi % px) + px) % px, x1 = (x0 + 1) % px;
        int y0 = ((yi % py) + py) % py, y1 = (y0 + 1) % py;
        return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx),
                          Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
    }

    static float Fbm(float u, float v, int px, int py, int seed, int octaves = 4)
    {
        float sum = 0f, amp = 0.5f, total = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Noise(u, v, px, py, seed + i * 17) * amp;
            total += amp;
            amp *= 0.5f;
            px *= 2;
            py *= 2;
        }
        return sum / total;
    }

    /// <summary>茅草：沿斜面方向（V）拉長的條紋</summary>
    static void ThatchPixel(float u, float v, out Color c, out float h)
    {
        float streak = Fbm(u, v, 64, 5, 7, 4);
        float clump = Fbm(u, v, 8, 6, 9, 3);
        h = streak * 0.8f + clump * 0.2f;
        c = Color.Lerp(new Color(0.24f, 0.19f, 0.11f), new Color(0.66f, 0.55f, 0.33f), Mathf.Clamp01(streak * 1.7f - 0.35f));
        c *= Mathf.Lerp(0.72f, 1.1f, clump);
        c.a = 1f;
    }

    /// <summary>直向木板：每片一個色調，板與板之間有溝</summary>
    static void WoodPixel(float u, float v, Color baseColor, int seed, out Color c, out float h)
    {
        const int planks = 5;
        float pu = u * planks;
        int plank = Mathf.FloorToInt(pu);
        float fu = pu - plank;
        float groove = Mathf.SmoothStep(0f, 0.05f, fu) * Mathf.SmoothStep(0f, 0.05f, 1f - fu);
        float grain = Fbm(u + plank * 0.21f, v, 80, 4, seed, 4);
        float tone = Mathf.Lerp(0.8f, 1.2f, Hash(plank, 0, seed));
        h = groove * 0.7f + grain * 0.3f;
        c = baseColor * tone * Mathf.Lerp(0.72f, 1.25f, grain) * Mathf.Lerp(0.45f, 1f, groove);
        c.a = 1f;
    }

    /// <summary>石材：Voronoi 切出的不規則石塊</summary>
    static void StonePixel(float u, float v, out Color c, out float h)
    {
        const int cells = 7;
        float x = u * cells, y = v * cells;
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float d1 = 9f, d2 = 9f;
        int idX = 0, idY = 0;
        for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int cx = xi + ox, cy = yi + oy;
                int wx = ((cx % cells) + cells) % cells, wy = ((cy % cells) + cells) % cells;
                float px = cx + Hash(wx, wy, 31), py = cy + Hash(wx, wy, 37);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < d1) { d2 = d1; d1 = d; idX = wx; idY = wy; }
                else if (d < d2) d2 = d;
            }
        float seam = Mathf.SmoothStep(0f, 0.13f, d2 - d1);
        float grain = Fbm(u, v, 24, 24, 41, 4);
        h = seam * 0.8f + grain * 0.2f;
        float tone = Mathf.Lerp(0.72f, 1.15f, Hash(idX, idY, 43));
        c = new Color(0.70f, 0.68f, 0.63f) * tone * Mathf.Lerp(0.82f, 1.12f, grain) * Mathf.Lerp(0.45f, 1f, seam);
        c.a = 1f;
    }

    /// <summary>竹竿：綠色 + 每節的深色環（V 方向一張 = 一節）</summary>
    static void CulmPixel(float u, float v, out Color c, out float h)
    {
        float streak = Fbm(u, v, 12, 2, 71, 3);
        var body = Color.Lerp(new Color(0.13f, 0.30f, 0.11f), new Color(0.27f, 0.45f, 0.16f), streak);
        float node = Mathf.Abs(v - 0.5f);
        c = node < 0.014f ? new Color(0.17f, 0.24f, 0.10f) : Color.Lerp(new Color(0.40f, 0.50f, 0.25f), body, Mathf.SmoothStep(0.014f, 0.06f, node));
        c.a = 1f;
        h = 0f;
    }

    /// <summary>畫一片葉子（紡錘形）到 RGBA 緩衝區</summary>
    static void Blade(Color[] px, int size, Vector2 from, Vector2 to, float width, Color color)
    {
        Vector2 axis = to - from;
        float len2 = axis.sqrMagnitude;
        int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.x, to.x) - width));
        int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(from.x, to.x) + width));
        int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(from.y, to.y) - width));
        int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(from.y, to.y) + width));
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                var d = new Vector2(x + 0.5f, y + 0.5f) - from;
                float t = Vector2.Dot(d, axis) / len2;
                if (t < 0f || t > 1f) continue;
                float dist = Mathf.Abs(d.x * axis.y - d.y * axis.x) / Mathf.Sqrt(len2);
                if (dist > width * Mathf.Sin(Mathf.Pow(t, 0.65f) * Mathf.PI)) continue;
                px[y * size + x] = color * Mathf.Lerp(1.12f, 0.82f, t);
                px[y * size + x].a = 1f;
            }
    }

    static void DrawNet(Color[] px, int size)
    {
        const int cells = 4;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size * cells, v = (float)y / size * cells;
                float a = Mathf.Abs((u + v) - Mathf.Round(u + v));
                float b = Mathf.Abs((u - v) - Mathf.Round(u - v));
                bool knot = Mathf.Min(a, b) < 0.09f;
                px[y * size + x] = knot ? new Color(0.40f, 0.36f, 0.26f, 1f) : new Color(0.40f, 0.36f, 0.26f, 0f);
            }
    }

    /// <summary>
    /// 海面反射用的天空 Cubemap：水面系統內建的那張偏灰，這裡畫一張和場景天空配色的漸層。
    /// 只有上下方向會變色，所以不需要真的拍天空。
    /// </summary>
    public static Cubemap SkyReflection(string path, Color zenith, Color horizon)
    {
        const int size = 64;
        var cube = new Cubemap(size, TextureFormat.RGBA32, true);
        var pixels = new Color[size * size];
        foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = (x + 0.5f) / size * 2f - 1f;
                    float b = 1f - (y + 0.5f) / size * 2f; // Cubemap 的第 0 列在面的上方
                    Vector3 dir = face switch
                    {
                        CubemapFace.PositiveY => new Vector3(a, 1f, -b),
                        CubemapFace.NegativeY => new Vector3(a, -1f, b),
                        CubemapFace.PositiveX => new Vector3(1f, b, -a),
                        CubemapFace.NegativeX => new Vector3(-1f, b, a),
                        CubemapFace.PositiveZ => new Vector3(a, b, 1f),
                        _ => new Vector3(-a, b, -1f),
                    };
                    float elevation = Mathf.Clamp01(dir.normalized.y);
                    pixels[y * size + x] = Color.Lerp(horizon, zenith, Mathf.Pow(elevation, 0.55f));
                }
            cube.SetPixels(pixels, face);
        }
        cube.Apply(true, false);
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(cube, path);
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
