using UnityEngine;

/// <summary>
/// 地面上的圓形警戒區（Boss 跳砸 / 從天而降的落點）。
///
///   外圈亮線 = 會被打到的範圍；裡面那一片淡淡的顏色會從中心往外「長」，長滿的那一刻就是砸下來的時候。
///
/// 用法：
///   var mark = GroundTelegraph.Show(落點, 半徑, 顏色, 幾秒後砸下來);
///   ... 砸下來時 mark.Hide(true);
/// 不需要事先在場景裡放任何東西，圓盤的網格和材質都是程式產生的。
/// </summary>
public class GroundTelegraph : MonoBehaviour
{
    const int Segments = 72;
    const float RingInner = 0.955f;      // 外圈亮線的內緣（佔半徑的比例）
    const float HeightAboveGround = 0.06f;

    static Mesh discMesh, ringMesh;
    static Material template;

    Transform grow;
    Material ringMat, baseMat, growMat;
    Color color;
    float radius, fillTime, age;
    float fadeTime = -1f, fadeTimer;
    bool flash;

    /// <summary>
    /// 在 center 的地面上顯示半徑 radius 的圓。fillTime = 幾秒後攻擊落下（裡面的顏色用這麼久長滿）；
    /// 0 = 直接顯示整個圓。
    /// </summary>
    public static GroundTelegraph Show(Vector3 center, float radius, Color color, float fillTime)
    {
        EnsureShared();
        if (template == null) return null; // 找不到可以用的 Shader：寧可不畫，也不要讓 Boss 的招式整個中斷

        var go = new GameObject("GroundTelegraph");
        go.transform.position = center + Vector3.up * HeightAboveGround;
        var t = go.AddComponent<GroundTelegraph>();
        t.Build(radius, color, fillTime);
        return t;
    }

    /// <summary>收掉。flash = true：先整片閃一下再消失（攻擊落下的瞬間用）</summary>
    public void Hide(bool flash = false)
    {
        if (fadeTime > 0f) return;
        this.flash = flash;
        fadeTime = fadeTimer = flash ? 0.25f : 0.15f;
    }

    void Build(float radius, Color color, float fillTime)
    {
        this.radius = Mathf.Max(radius, 0.1f);
        this.color = color;
        this.fillTime = Mathf.Max(fillTime, 0f);

        baseMat = new Material(template);
        growMat = new Material(template);
        ringMat = new Material(template);
        Part("Base", discMesh, baseMat, 0f).localScale = Vector3.one * this.radius;
        grow = Part("Grow", discMesh, growMat, 0.01f);
        Part("Ring", ringMesh, ringMat, 0.02f).localScale = Vector3.one * this.radius;
        Refresh(0f, 1f);
    }

    Transform Part(string partName, Mesh mesh, Material mat, float lift)
    {
        var go = new GameObject(partName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * lift; // 三層稍微錯開高度，不會互相閃爍
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go.transform;
    }

    void Update()
    {
        age += Time.deltaTime;
        float k = fillTime > 0f ? Mathf.Clamp01(age / fillTime) : 1f;

        float fade = 1f;
        if (fadeTime > 0f)
        {
            fadeTimer -= Time.deltaTime;
            if (fadeTimer <= 0f) { Destroy(gameObject); return; }
            fade = fadeTimer / fadeTime;
            if (flash) k = 1f;
        }
        // 放著沒人收（例如戰鬥被重置）也不會永遠留在地上
        else if (age > fillTime + 4f) { Hide(); }

        Refresh(k, fade);
    }

    void Refresh(float k, float fade)
    {
        grow.localScale = Vector3.one * (radius * Mathf.Max(k, 0.001f));
        // 越接近落下，閃得越快
        float pulse = 0.5f + 0.5f * Mathf.Sin(age * Mathf.Lerp(6f, 22f, k));
        float boost = flash && fadeTime > 0f ? 2.2f : 1f;
        SetColor(ringMat, Mathf.Lerp(0.75f, 1f, pulse) * fade);
        SetColor(baseMat, 0.16f * fade * boost);
        SetColor(growMat, Mathf.Lerp(0.26f, 0.4f, pulse) * fade * boost);
    }

    void SetColor(Material m, float alpha)
    {
        var c = new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    void OnDestroy()
    {
        // 程式產生的材質要自己清掉
        if (baseMat != null) Destroy(baseMat);
        if (growMat != null) Destroy(growMat);
        if (ringMat != null) Destroy(ringMat);
    }

    // =====================================================================
    //  共用的網格和材質（第一次用到時才建立）
    // =====================================================================

    static void EnsureShared()
    {
        if (discMesh == null) discMesh = BuildRing("TelegraphDisc", 0f);
        if (ringMesh == null) ringMesh = BuildRing("TelegraphRing", RingInner);
        if (template == null) template = BuildMaterial();
    }

    /// <summary>半徑 1、平躺在地上的圓環（inner = 0 就是實心圓盤）</summary>
    static Mesh BuildRing(string meshName, float inner)
    {
        var verts = new Vector3[(Segments + 1) * 2];
        var tris = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            float a = i / (float)Segments * Mathf.PI * 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            verts[i * 2] = dir * inner;
            verts[i * 2 + 1] = dir;
        }
        for (int i = 0; i < Segments; i++)
        {
            int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
            int t = i * 6;
            // 從上面往下看是順時針 = 正面朝上
            tris[t] = a; tris[t + 1] = c; tris[t + 2] = b;
            tris[t + 3] = b; tris[t + 4] = c; tris[t + 5] = d;
        }
        var mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>不受光照影響的半透明材質（跟 Boss 憤怒特效用同一種寫法）</summary>
    static Material BuildMaterial()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            m.SetFloat("_Surface", 1f);     // Transparent
            m.SetFloat("_Blend", 0f);       // Alpha
            m.SetFloat("_SrcBlend", 5f);    // SrcAlpha
            m.SetFloat("_DstBlend", 10f);   // OneMinusSrcAlpha
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", 1f);  // One
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", 10f); // OneMinusSrcAlpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);        // 兩面都畫
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = 3000;
        }
        else
        {
            sh = Shader.Find("Sprites/Default");
            if (sh == null)
            {
                Debug.LogWarning("[GroundTelegraph] 找不到半透明的 Shader，地上的警戒圈不會顯示。");
                return null;
            }
            m = new Material(sh);
        }
        m.name = "GroundTelegraph";
        m.hideFlags = HideFlags.HideAndDontSave;
        return m;
    }
}
