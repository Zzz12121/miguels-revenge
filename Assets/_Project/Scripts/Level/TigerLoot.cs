using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 打跑雙尾虎之後掉在地上的戰利品：虎皮、虎牙（TigerBeast 逃走時 TigerLoot.Drop 生成）。
/// 走近按 E 撿起來，存進背包（PlayerInventory，資源名 TigerPelt / TigerFang），拿去鐵匠鋪升級武器（BlacksmithShop）。
/// 模型都是程式做的：虎皮 = 攤平的一張皮（橘底黑紋、肚子白，四條腿和尾巴），虎牙 = 彎彎的象牙色尖牙。
/// 地上的戰利品會微微發光、冒一點小亮點，遠遠就看得到。
/// </summary>
public class LootPickup : MonoBehaviour
{
    public string resourceKey;
    public string displayName;
    public int amount = 1;
    public float interactRange = 2f;

    Transform player;
    Light glint;
    float phase;

    void Start()
    {
        var hp = PlayerHealth.Find();
        if (hp != null) player = hp.transform;
        phase = Random.value * 10f;
        var lg = new GameObject("Glint");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = Vector3.up * 0.35f;
        glint = lg.AddComponent<Light>();
        glint.type = LightType.Point;
        glint.color = new Color(1f, 0.82f, 0.55f);
        glint.range = 1.6f;
        glint.intensity = 0.6f;
        glint.shadows = LightShadows.None;
    }

    void Update()
    {
        if (glint != null) glint.intensity = 0.45f + 0.25f * Mathf.Sin(Time.time * 2.2f + phase);
        if (player == null)
        {
            var hp = PlayerHealth.Find();
            if (hp == null) return;
            player = hp.transform;
        }
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > interactRange) return;
        InteractPrompt.Offer(this, dist, $"撿起{displayName}");
        if (InteractPrompt.Pressed(this)) Pick();
    }

    void Pick()
    {
        VillageWallet.Add(resourceKey, amount);
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.8f, 1.1f, 0.05f, 0.3f);
        VictoryBanner.ShowSmall($"{displayName} +{amount}", "可以拿去鐵匠鋪升級武器", GameUI.Good);
        Destroy(gameObject);
    }
}

public static class TigerLoot
{
    public const string PeltKey = "TigerPelt", FangKey = "TigerFang";
    public const int FangCount = 2;

    /// <summary>在 center 旁邊放一張虎皮、一對虎牙（groundY：給一個位置回傳地面高度）</summary>
    public static void Drop(Vector3 center, Vector3 forward, System.Func<Vector3, float> groundY)
    {
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-4f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 pp = center + right * 1.1f;
        pp.y = groundY(pp);
        var pelt = new GameObject("TigerLoot_Pelt");
        pelt.transform.SetPositionAndRotation(pp, Quaternion.LookRotation(forward) * Quaternion.Euler(0f, 25f, 0f));
        AddVisual(pelt.transform, PeltMesh(), PeltMaterial());
        var a = pelt.AddComponent<LootPickup>();
        a.resourceKey = PeltKey; a.displayName = "虎皮"; a.amount = 1;

        Vector3 fp = center - right * 1.1f + forward * 0.3f;
        fp.y = groundY(fp);
        var fangs = new GameObject("TigerLoot_Fangs");
        fangs.transform.SetPositionAndRotation(fp, Quaternion.LookRotation(forward));
        var fangMesh = FangMesh();
        var fangMat = Lit(new Color(0.93f, 0.89f, 0.78f), 0.55f);
        for (int i = 0; i < FangCount; i++)
        {
            var f = new GameObject($"Fang_{i + 1}");
            f.transform.SetParent(fangs.transform, false);
            // 兩根牙側躺在地上，尖端朝外、稍微交叉
            f.transform.localPosition = new Vector3(i == 0 ? -0.06f : 0.07f, 0.025f, i == 0 ? 0f : 0.05f);
            f.transform.localRotation = Quaternion.Euler(0f, i == 0 ? -35f : 140f, 0f) * Quaternion.Euler(0f, 0f, 90f);
            AddVisual(f.transform, fangMesh, fangMat);
        }
        var b = fangs.AddComponent<LootPickup>();
        b.resourceKey = FangKey; b.displayName = "虎牙"; b.amount = FangCount;
    }

    static void AddVisual(Transform t, Mesh mesh, Material mat)
    {
        t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = t.gameObject.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
    }

    static Material Lit(Color c, float smoothness, Texture2D tex = null)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Runtime_TigerLoot" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tex != null ? Color.white : c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tex != null ? Color.white : c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
        }
        return m;
    }

    // ---------------- 虎皮 ----------------

    /// <summary>
    /// 攤平的虎皮（約 1.5 × 1.1 m）：中間是身體（橢圓），往外伸出四條腿和一條尾巴；整張是一片網格，
    /// UV 的 v = 從背脊（0.5）到兩側（0 / 1），u = 頭到尾。邊緣稍微捲起、中間有一點起伏，看起來是軟的皮
    /// </summary>
    static Mesh PeltMesh()
    {
        const int NU = 36, NV = 24;
        const float L = 1.5f, W = 1.1f;
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        var keep = new bool[(NU + 1) * (NV + 1)];
        for (int i = 0; i <= NU; i++)
            for (int j = 0; j <= NV; j++)
            {
                float u = i / (float)NU, v = j / (float)NV;
                float x = (v - 0.5f) * W, z = (0.5f - u) * L; // +z = 頭
                keep[i * (NV + 1) + j] = Inside(x, z);
                float edge = EdgeDistance(x, z);
                float y = 0.012f + 0.02f * Mathf.Sin(u * 9f) * Mathf.Sin(v * 7f) + Mathf.Clamp01(0.06f - edge) * 0.35f;
                verts.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(u, v));
            }
        for (int i = 0; i < NU; i++)
            for (int j = 0; j < NV; j++)
            {
                int a = i * (NV + 1) + j, b = a + 1, c = a + NV + 1, d = c + 1;
                if (!(keep[a] && keep[b] && keep[c] && keep[d])) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
                tris.Add(b); tris.Add(d); tris.Add(c);
            }
        var mesh = new Mesh { name = "Runtime_TigerPelt" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // 虎皮的輪廓：身體橢圓 + 四條腿（往斜前 / 斜後伸出去）+ 尾巴 + 頭
    static bool Inside(float x, float z) => EdgeDistance(x, z) > 0f;

    static float EdgeDistance(float x, float z)
    {
        float body = 1f - Mathf.Sqrt(x * x / (0.27f * 0.27f) + z * z / (0.5f * 0.5f));
        float d = body * 0.27f;
        d = Mathf.Max(d, Capsule(x, z, new Vector2(0f, 0.48f), new Vector2(0f, 0.62f), 0.13f));          // 頭
        d = Mathf.Max(d, Capsule(x, z, new Vector2(0.15f, 0.3f), new Vector2(0.5f, 0.5f), 0.075f));      // 右前腳
        d = Mathf.Max(d, Capsule(x, z, new Vector2(-0.15f, 0.3f), new Vector2(-0.5f, 0.5f), 0.075f));    // 左前腳
        d = Mathf.Max(d, Capsule(x, z, new Vector2(0.15f, -0.3f), new Vector2(0.5f, -0.5f), 0.08f));     // 右後腳
        d = Mathf.Max(d, Capsule(x, z, new Vector2(-0.15f, -0.3f), new Vector2(-0.5f, -0.5f), 0.08f));   // 左後腳
        d = Mathf.Max(d, Capsule(x, z, new Vector2(0f, -0.45f), new Vector2(0.14f, -0.74f), 0.035f));    // 尾巴
        return d;
    }

    static float Capsule(float x, float z, Vector2 a, Vector2 b, float r)
    {
        Vector2 p = new Vector2(x, z), ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return r - Vector2.Distance(p, a + ab * t);
    }

    static Material peltMat;

    static Material PeltMaterial()
    {
        if (peltMat != null) return peltMat;
        // 貼圖：u = 頭到尾（x 方向），v = 左到右；背脊橘、兩側淡、肚子邊白；一條條橫過背的黑紋（中間粗、往兩側變細、有點彎）
        const int TW = 256, TH = 128;
        var tex = new Texture2D(TW, TH, TextureFormat.RGB24, true) { name = "Runtime_TigerPelt", wrapMode = TextureWrapMode.Clamp };
        Color orange = new Color(0.86f, 0.45f, 0.13f), pale = new Color(0.93f, 0.72f, 0.45f), white = new Color(0.93f, 0.9f, 0.82f), black = new Color(0.08f, 0.06f, 0.05f);
        for (int y = 0; y < TH; y++)
            for (int x = 0; x < TW; x++)
            {
                float u = x / (float)TW, v = y / (float)TH;
                float side = Mathf.Abs(v - 0.5f) * 2f;              // 0 = 背脊，1 = 邊
                Color c = Color.Lerp(orange, pale, Mathf.SmoothStep(0.45f, 0.85f, side));
                c = Color.Lerp(c, white, Mathf.SmoothStep(0.82f, 0.97f, side));
                // 黑紋：沿 u 每隔一段一條，往兩側彎（v 越外偏移越多），越外越細
                float stripe = u * 15f + side * side * 0.9f + Mathf.Sin(v * 21f) * 0.12f;
                float f = Mathf.Abs(Mathf.Repeat(stripe, 1f) - 0.5f) * 2f; // 0 = 紋的中心
                float width = Mathf.Lerp(0.32f, 0.08f, side) * (0.8f + 0.4f * Mathf.PerlinNoise(u * 6f, v * 6f));
                if (f < width && side < 0.9f) c = Color.Lerp(c, black, Mathf.SmoothStep(width, width * 0.6f, f));
                float n = 0.9f + 0.2f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                tex.SetPixel(x, y, c * n);
            }
        tex.Apply();
        peltMat = Lit(Color.white, 0.15f, tex);
        return peltMat;
    }

    // ---------------- 虎牙 ----------------

    /// <summary>一根虎牙（約 10 cm）：根部粗、往尖端變細，沿著一個弧彎下去；沿 +Y 長（根在原點）</summary>
    static Mesh FangMesh()
    {
        const int Rings = 10, Sides = 10;
        const float Length = 0.1f, Root = 0.017f, Curve = 0.035f;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int k = 0; k <= Rings; k++)
        {
            float t = k / (float)Rings;
            Vector3 c = new Vector3(0f, t * Length, -Curve * t * t);
            float r = Root * Mathf.Pow(1f - t, 0.8f) + 0.0008f;
            for (int s = 0; s < Sides; s++)
            {
                float a = s * Mathf.PI * 2f / Sides;
                verts.Add(c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r * 0.8f));
            }
        }
        for (int k = 0; k < Rings; k++)
            for (int s = 0; s < Sides; s++)
            {
                int a = k * Sides + s, b = k * Sides + (s + 1) % Sides, c = a + Sides, d = b + Sides;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        // 根部封口
        int center = verts.Count;
        verts.Add(Vector3.zero);
        for (int s = 0; s < Sides; s++) { tris.Add(center); tris.Add(s); tris.Add((s + 1) % Sides); }
        var mesh = new Mesh { name = "Runtime_TigerFang" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
