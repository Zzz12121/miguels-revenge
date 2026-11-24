using UnityEngine;

/// <summary>
/// 百足駭骨三階段分身的血條：浮在分身頭上，寫「分身 1」「分身 2」。
/// 本體的血條還是在畫面下方（BossHealth），分身的 BossHealth 不畫（ShowBar = false），改由這個畫。
/// 由 SkeletonCentipedeBossAI 召喚分身時自動加上，不用手動放。
/// 被打到時先掉紅色、後面的金色慢慢跟上（跟本體血條一樣）；分身倒下後血條淡出。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkeletonCentipedeCloneHealthBar : MonoBehaviour
{
    public string label = "分身";
    [Tooltip("血條在模型頂端再往上多高（公尺）")]
    public float heightAboveHead = 1.2f;
    public float width = 150f, height = 9f;

    const float FadeSeconds = 1.2f;

    BossHealth health;
    Renderer[] renderers;
    float trailing, diedAt = -1f;
    GUIStyle nameStyle;

    void Awake()
    {
        health = GetComponent<BossHealth>();
        renderers = GetComponentsInChildren<Renderer>();
    }

    void Update()
    {
        if (health == null) return;
        if (trailing < health.Health || trailing > health.maxHealth) trailing = health.Health;
        trailing = Mathf.MoveTowards(trailing, health.Health, health.maxHealth * 0.6f * Time.deltaTime);
        if (health.IsDead && diedAt < 0f) diedAt = Time.time;
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || health == null) return;
        if (!CalligraphyWorldIsolation.Allows(transform) || GameUI.OverlayOpen || VitalsHud.Hidden) return;
        float alpha = 1f;
        if (diedAt >= 0f)
        {
            alpha = 1f - (Time.time - diedAt) / FadeSeconds;
            if (alpha <= 0f) return;
        }
        var cam = Camera.main;
        if (cam == null || !TopOfBody(out Vector3 top)) return;
        Vector3 s = cam.WorldToScreenPoint(top + Vector3.up * heightAboveHead);
        if (s.z <= 0f) return;   // 在鏡頭後面

        float x = s.x - width * 0.5f, y = Screen.height - s.y;
        Color old = GUI.color;

        nameStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
        var nameRect = new Rect(s.x - 80f, y - 20f, 160f, 18f);
        nameStyle.normal.textColor = new Color(0f, 0f, 0f, 0.8f * alpha);
        GUI.Label(new Rect(nameRect.x + 1f, nameRect.y + 1f, nameRect.width, nameRect.height), label, nameStyle);
        nameStyle.normal.textColor = new Color(1f, 0.92f, 0.85f, alpha);
        GUI.Label(nameRect, label, nameStyle);

        float max = Mathf.Max(1f, health.maxHealth);
        GUI.color = new Color(0f, 0f, 0f, 0.7f * alpha);
        GUI.DrawTexture(new Rect(x - 2f, y - 2f, width + 4f, height + 4f), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.5f, 0.9f * alpha);
        GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(trailing / max), height), Texture2D.whiteTexture);
        GUI.color = new Color(0.7f, 0.08f, 0.08f, alpha);
        GUI.DrawTexture(new Rect(x, y, width * Mathf.Clamp01(health.Health / max), height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    /// <summary>模型最高點（骨架動畫會一直變，所以每次重算）</summary>
    bool TopOfBody(out Vector3 top)
    {
        top = transform.position;
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return true;
        top = new Vector3(b.center.x, b.max.y, b.center.z);
        return true;
    }
}
