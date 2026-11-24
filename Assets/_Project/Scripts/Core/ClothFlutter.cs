using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 選項畫面那塊破布按鈕「被微風吹」：選到（手把 / 方向鍵）或滑鼠移上去時，整塊布起伏成波浪、微微擺動、放大一點點；
/// 離開後慢慢停下來。掛在按鈕（Image）上；按鈕上的字掛另一個 ClothFlutter、driver 指向按鈕的，跟著同一陣風一起飄。
/// 布（Image）會先切成很多直條再彎，字只把每個字的四個角跟著移（字很小，不用切）。
/// </summary>
[RequireComponent(typeof(Graphic))]
public sealed class ClothFlutter : BaseMeshEffect, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("字用：跟著這塊布一起飄（自己不判斷有沒有選到）")]
    public ClothFlutter driver;
    [Tooltip("波浪最大起伏 (px)")]
    public float amplitude = 4f;
    [Tooltip("布切成直條，每條多寬 (px)")]
    public float segment = 12f;

    bool hover;
    float amount, lastAmount, phase;
    Selectable selectable;

    static readonly List<UIVertex> src = new List<UIVertex>(), dst = new List<UIVertex>();

    protected override void Awake()
    {
        base.Awake();
        phase = Random.value * 20f;
        selectable = GetComponent<Selectable>();
    }

    public void OnPointerEnter(PointerEventData e) => hover = true;
    public void OnPointerExit(PointerEventData e) => hover = false;

    protected override void OnDisable()
    {
        hover = false;
        amount = 0f;
        transform.localRotation = Quaternion.identity;
        if (driver == null) transform.localScale = Vector3.one;
        base.OnDisable();
    }

    float Amount => driver != null ? driver.amount : amount;
    float Clock => Time.unscaledTime + (driver != null ? driver.phase : phase);
    Rect Area => (driver != null ? (RectTransform)driver.transform : (RectTransform)transform).rect;

    void Update()
    {
        if (driver == null)
        {
            var es = EventSystem.current;
            bool on = hover || (es != null && es.currentSelectedGameObject == gameObject);
            if (selectable != null && !selectable.interactable) on = false;
            // 風來得快、停得慢
            amount = Mathf.MoveTowards(amount, on ? 1f : 0f, Time.unscaledDeltaTime * (on ? 3.5f : 1.6f));
            float t = Clock;
            float sway = amount * (1.1f * Mathf.Sin(t * 2.3f) + 0.45f * Mathf.Sin(t * 4.1f + 1.3f));
            transform.localRotation = Quaternion.Euler(0f, 0f, sway);
            transform.localScale = Vector3.one * (1f + 0.035f * Mathf.SmoothStep(0f, 1f, amount));
        }
        float a = Amount;
        if (a > 0.0005f || lastAmount > 0.0005f) graphic.SetVerticesDirty();
        lastAmount = a;
    }

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive()) return;
        float a = Amount;
        if (a <= 0.0005f) return;
        Rect r = Area;
        float t = Clock;
        src.Clear();
        dst.Clear();
        vh.GetUIVertexStream(src);
        bool split = driver == null;   // 布要切；字不用
        // 每 6 個點是一個四邊形（0,1,2 / 2,3,0）：Image 的順序是 左下、左上、右上、右下
        for (int i = 0; i + 5 < src.Count; i += 6)
        {
            UIVertex v0 = src[i], v1 = src[i + 1], v2 = src[i + 2], v3 = src[i + 4];
            int n = split ? Mathf.Clamp(Mathf.CeilToInt(Mathf.Abs(v3.position.x - v0.position.x) / Mathf.Max(2f, segment)), 1, 64) : 1;
            if (n == 1)
            {
                for (int k = 0; k < 6; k++) dst.Add(Bend(src[i + k], r, t, a));
                continue;
            }
            UIVertex prevBottom = Bend(v0, r, t, a), prevTop = Bend(v1, r, t, a);
            for (int k = 1; k <= n; k++)
            {
                float f = (float)k / n;
                UIVertex bottom = Bend(Lerp(v0, v3, f), r, t, a), top = Bend(Lerp(v1, v2, f), r, t, a);
                dst.Add(prevBottom); dst.Add(prevTop); dst.Add(top);
                dst.Add(top); dst.Add(bottom); dst.Add(prevBottom);
                prevBottom = bottom;
                prevTop = top;
            }
        }
        vh.Clear();
        vh.AddUIVertexTriangleStream(dst);
    }

    /// <summary>風從左邊吹過來：上下起伏（越往右越大），加一點細碎的抖動和左右的輕晃</summary>
    UIVertex Bend(UIVertex v, Rect r, float t, float a)
    {
        Vector3 p = v.position;
        float x01 = r.width > 1f ? Mathf.Clamp01((p.x - r.xMin) / r.width) : 0.5f;
        float y01 = r.height > 1f ? Mathf.Clamp01((p.y - r.yMin) / r.height) : 0.5f;
        float dy = amplitude * (Mathf.Sin(p.x * 0.042f - t * 5.4f) * (0.35f + 0.65f * x01)
                                + 0.35f * Mathf.Sin(p.x * 0.13f - t * 9.1f + p.y * 0.05f));
        float dx = amplitude * 0.4f * Mathf.Sin(p.y * 0.11f - t * 6.3f + p.x * 0.017f) * (0.5f + 0.5f * y01);
        p.x += dx * a;
        p.y += dy * a;
        v.position = p;
        return v;
    }

    static UIVertex Lerp(UIVertex a, UIVertex b, float f)
    {
        var v = a;
        v.position = Vector3.Lerp(a.position, b.position, f);
        v.color = Color32.Lerp(a.color, b.color, f);
        v.uv0 = Vector4.Lerp(a.uv0, b.uv0, f);
        v.uv1 = Vector4.Lerp(a.uv1, b.uv1, f);
        v.normal = Vector3.Lerp(a.normal, b.normal, f);
        v.tangent = Vector4.Lerp(a.tangent, b.tangent, f);
        return v;
    }
}
