using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能瞄準：被準心對到的敵人整個身體變紅（取代原本貼在身上的小圓圈），表示可以攻擊。
///   Show(敵人, ready)：ready = true（技能 1 蓄滿、可以出招）→ 鮮紅色一閃一閃；false（還在蓄力）→ 淡淡的紅
///   換成別的敵人 / 沒對到任何東西 → 前一個自動恢復原本的顏色
/// 做法和 EnemyHealth 受傷閃白一樣：用 MaterialPropertyBlock 改 _BaseColor（乘在貼圖上，看得出紋路），不改材質本身。
/// </summary>
public static class VoidRendAimHighlight
{
    public static Color AimColor = new Color(1f, .16f, .12f, 1f);

    static Transform current;
    static readonly List<Renderer> renderers = new List<Renderer>();
    static MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { current = null; renderers.Clear(); mpb = null; }

    public static void Show(Transform target, bool ready)
    {
        if (target != current) { Clear(); if (target != null) Collect(target); }
        if (current == null) return;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        float pulse = ready ? .78f + .22f * Mathf.Sin(Time.unscaledTime * 9f) : .45f;
        Color c = Color.Lerp(Color.white, AimColor, pulse);
        mpb.Clear();
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(mpb);
    }

    public static void Clear()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.Clear();
        foreach (var r in renderers) if (r != null) r.SetPropertyBlock(mpb);
        renderers.Clear();
        current = null;
    }

    static void Collect(Transform target)
    {
        current = target;
        foreach (var r in target.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer || !r.enabled) continue;
            renderers.Add(r);
        }
    }
}
