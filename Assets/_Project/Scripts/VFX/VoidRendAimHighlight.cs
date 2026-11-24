using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 技能瞄準：被準心對到的敵人整個身體變紅（取代原本貼在身上的小圓圈），表示可以攻擊。
///   Show(敵人, ready)：ready = true（技能 1 蓄滿、可以出招）→ 鮮紅色一閃一閃；false（還在蓄力）→ 淡淡的紅
///   換成別的敵人 / 沒對到任何東西 → 前一個自動恢復原本的顏色
///
/// 不管什麼敵人都要紅：很多系統每一幀都會改敵人的 MaterialPropertyBlock（受傷閃白、眼睛發光、書法分身的墨色……），
/// 直接設會被蓋掉。所以紅色只在「攝影機要畫這一幀之前」套上去（beginContextRendering），畫完馬上把原本的還回去
/// （endContextRendering）：別人的設定完全不受影響，畫面上看到的永遠是紅色。
/// 貼圖暫時換成白色、整隻染成紅色（深色的敵人也看得出來），不改材質本身。
/// </summary>
public static class VoidRendAimHighlight
{
    public static Color AimColor = new Color(1f, .16f, .12f, 1f);

    static Transform current;
    static bool ready;
    static readonly List<Renderer> renderers = new List<Renderer>();
    static readonly List<MaterialPropertyBlock> saved = new List<MaterialPropertyBlock>();
    static MaterialPropertyBlock tint;
    static bool hooked, applied;
    static int lastShowFrame = -1;
    static float nextCollect;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; renderers.Clear(); saved.Clear(); tint = null; applied = false; lastShowFrame = -1; }

    /// <summary>每一幀呼叫（瞄準中）。target 換了就換人</summary>
    public static void Show(Transform target, bool readyToFire)
    {
        if (target != current) { Clear(); if (target != null) { current = target; Collect(); } }
        if (current == null) return;
        ready = readyToFire;
        lastShowFrame = Time.frameCount;
        if (!hooked)
        {
            hooked = true;
            RenderPipelineManager.beginContextRendering += BeforeRender;
            RenderPipelineManager.endContextRendering += AfterRender;
        }
    }

    public static void Clear()
    {
        Restore();
        current = null;
        renderers.Clear();
    }

    /// <summary>現在被標紅的是不是這個敵人</summary>
    public static bool IsShowing(Transform target) => current != null && current == target;

    static void Collect()
    {
        renderers.Clear();
        nextCollect = Time.unscaledTime + .5f;   // 半秒重新找一次：敵人換裝 / 開關模型（書法分身、布娃娃）時也跟得上
        if (current == null) return;
        foreach (var r in current.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || !r.enabled) continue;
            renderers.Add(r);
        }
    }

    static void BeforeRender(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (applied) Restore();
        if (current == null) return;
        // 瞄準的那一方超過兩幀沒有再呼叫 Show（例如瞄準被中斷）：自動清掉，不會一直紅
        if (Time.frameCount - lastShowFrame > 2) { Clear(); return; }
        if (Time.unscaledTime >= nextCollect) Collect();
        if (tint == null) tint = new MaterialPropertyBlock();

        // 蓄滿（可以出招）：鮮紅一閃一閃；還在蓄力：比較暗的紅
        float pulse = ready ? .85f + .15f * Mathf.Sin(Time.unscaledTime * 9f) : .55f;
        Color mul = AimColor * pulse;
        while (saved.Count < renderers.Count) saved.Add(new MaterialPropertyBlock());
        for (int i = 0; i < renderers.Count; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(saved[i]);   // 別的系統這一幀設好的（閃白、發光……），畫完還回去
            r.GetPropertyBlock(tint);
            // 貼圖暫時換成白色、整個染成紅色：黑衣服、骷髏這類深色的敵人用乘的看不出來，直接整隻變紅（光影照樣有）
            var m = r.sharedMaterial;
            float alpha = m != null && m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId).a : 1f;
            Color c = mul; c.a = alpha;
            tint.SetTexture(BaseMapId, Texture2D.whiteTexture);
            tint.SetTexture(MainTexId, Texture2D.whiteTexture);
            tint.SetColor(BaseColorId, c);
            tint.SetColor(ColorId, c);
            r.SetPropertyBlock(tint);
        }
        applied = true;
    }

    static void AfterRender(ScriptableRenderContext context, List<Camera> cameras) => Restore();

    static void Restore()
    {
        if (!applied) return;
        applied = false;
        for (int i = 0; i < renderers.Count && i < saved.Count; i++)
            if (renderers[i] != null) renderers[i].SetPropertyBlock(saved[i]);
    }
}
