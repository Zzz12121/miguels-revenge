using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// 不可破壞物件的提示：玩家每次揮刀，在攻擊判定的時間內檢查有沒有掃到 ImmortalObject；
/// 掃到就在打中的位置顯示一個紅色六角標籤（展開 → 停留 → 淡出），永遠面向鏡頭、遠近大小一致。
/// 其他系統也可以直接呼叫 ImmortalObjectFeedback.Show(位置)。
/// </summary>
public class ImmortalObjectFeedback : MonoBehaviour
{
    public static ImmortalObjectFeedback Instance { get; private set; }

    [Header("外觀")]
    [Tooltip("六角標籤底圖（半透明 Unlit）")]
    public Material labelMaterial;
    public string text = "Immortal Object";
    public Color textColor = new Color(1f, 0.9f, 0.9f);
    [Tooltip("標籤寬高比（貼圖 512×128 = 4）")]
    public float aspect = 4f;
    [Tooltip("標籤高度 = 與鏡頭的距離 × 這個值（遠近看起來一樣大）")]
    public float sizePerMeter = 0.05f;
    public float minHeight = 0.35f;

    [Header("時間")]
    public float expandTime = 0.12f;
    public float holdTime = 0.9f;
    public float fadeTime = 0.35f;
    [Tooltip("同一個地方短時間內不重複跳")]
    public float minInterval = 0.3f;

    [Header("偵測")]
    [Tooltip("每次揮刀後檢查幾秒")]
    public float swingCheckTime = 0.3f;
    public string hitSound = "SFX/rock_hit";

    MeleeCombat combat;
    Transform player;
    readonly Collider[] buffer = new Collider[32];
    float lastShowTime = -10f;
    Vector3 lastShowPos;
    readonly List<Popup> pool = new List<Popup>();

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    class Popup
    {
        public Transform root, quad;
        public MeshRenderer quadRenderer;
        public TextMeshPro label;
        public MaterialPropertyBlock mpb;
        public bool busy;
    }

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (combat != null) combat.OnSwing -= OnSwing;
    }

    void Start()
    {
        var ph = PlayerHealth.Find();
        if (ph == null) return;
        player = ph.transform;
        combat = ph.GetComponent<MeleeCombat>();
        if (combat != null) combat.OnSwing += OnSwing;
    }

    void OnSwing(int index)
    {
        if (isActiveAndEnabled) StartCoroutine(CheckSwing());
    }

    IEnumerator CheckSwing()
    {
        float end = Time.time + swingCheckTime;
        while (Time.time < end)
        {
            Vector3 center = player.position + player.forward * combat.hitForwardOffset;
            int n = Physics.OverlapSphereNonAlloc(center, combat.hitRadius, buffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                var c = buffer[i];
                var immortal = c.GetComponentInParent<ImmortalObject>();
                if (immortal == null) continue;
                bool closest = c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider m && m.convex);
                Vector3 point = closest ? c.ClosestPoint(center) : c.bounds.ClosestPoint(center);
                Show(point, string.IsNullOrEmpty(immortal.labelOverride) ? null : immortal.labelOverride);
                yield break; // 一刀只跳一次
            }
            yield return null;
        }
    }

    /// <summary>在 position 顯示標籤（text 為 null 用預設文字）。</summary>
    public void Show(Vector3 position, string labelText = null)
    {
        if (Time.time - lastShowTime < minInterval && (position - lastShowPos).sqrMagnitude < 4f) return;
        lastShowTime = Time.time;
        lastShowPos = position;

        var cam = Camera.main;
        if (cam != null) position += (cam.transform.position - position).normalized * 0.4f; // 浮在牆面前一點
        var p = GetPopup();
        p.label.text = labelText ?? text;
        StartCoroutine(Animate(p, position));
        if (!string.IsNullOrEmpty(hitSound)) GameAudio.Play(hitSound, position, 0.6f, 1.6f, 0.05f, 1f, 40f);
    }

    Popup GetPopup()
    {
        foreach (var p in pool) if (!p.busy) return p;

        var root = new GameObject("ImmortalObjectLabel").transform;
        root.SetParent(transform, false);
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Destroy(quad.GetComponent<Collider>());
        quad.name = "Plate";
        quad.transform.SetParent(root, false);
        var mr = quad.GetComponent<MeshRenderer>();
        mr.sharedMaterial = labelMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.sortingOrder = 100;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(root, false);
        textGo.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        var tmp = textGo.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.characterSpacing = 4f;
        tmp.enableAutoSizing = true; // 文字太長時自動縮小，塞得進標籤
        tmp.fontSizeMin = 1f;
        tmp.fontSizeMax = 3.2f;
        tmp.color = textColor;
        tmp.sortingOrder = 101;
        tmp.rectTransform.sizeDelta = new Vector2(aspect * 0.8f, 0.6f);

        var popup = new Popup { root = root, quad = quad.transform, quadRenderer = mr, label = tmp, mpb = new MaterialPropertyBlock() };
        root.gameObject.SetActive(false);
        pool.Add(popup);
        return popup;
    }

    IEnumerator Animate(Popup p, Vector3 position)
    {
        p.busy = true;
        p.root.gameObject.SetActive(true);
        p.root.position = position;
        float total = expandTime + holdTime + fadeTime;
        float t = 0f;
        while (t < total)
        {
            t += Time.deltaTime;
            var cam = Camera.main;
            float height = minHeight;
            if (cam != null)
            {
                // 永遠面向鏡頭，遠近看起來一樣大
                p.root.rotation = Quaternion.LookRotation(p.root.position - cam.transform.position, cam.transform.up);
                height = Mathf.Max(minHeight, Vector3.Distance(cam.transform.position, p.root.position) * sizePerMeter);
            }
            float open = expandTime > 0f ? Mathf.Clamp01(t / expandTime) : 1f;
            open = 1f - (1f - open) * (1f - open); // ease-out：從中間往左右展開
            float alpha = t < expandTime + holdTime ? 1f : 1f - Mathf.Clamp01((t - expandTime - holdTime) / Mathf.Max(0.01f, fadeTime));

            p.root.localScale = Vector3.one * height;
            p.quad.localScale = new Vector3(aspect * open, 1f, 1f);
            p.mpb.SetColor(BaseColorId, new Color(1f, 1f, 1f, alpha));
            p.quadRenderer.SetPropertyBlock(p.mpb);
            p.label.alpha = open >= 0.85f ? alpha : 0f; // 展開到差不多才出現文字
            yield return null;
        }
        p.root.gameObject.SetActive(false);
        p.busy = false;
    }
}
