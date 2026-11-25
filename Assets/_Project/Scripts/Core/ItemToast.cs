using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 拿到東西的提示（草藥、虎皮、錢、魚、地圖、包裹、箭……），跟選單同一套風格（MenuSkin）：
/// 畫面上方中間垂下一塊破布（選項按鈕那種，剛出現時被風吹一下），左邊蓋一個朱印「得」，
/// 墨色的大字寫拿到什麼、幾個（「草藥 +2」），下面一行小字說明（「持有 5　可以拿去給藥師換藥水」）。
/// 停 2.6 秒後往上淡出。同時拿到好幾樣由上往下排（最多 4 塊）；同一樣東西連著拿會合在同一塊布上（數量加上去、重新計時、布再被風吹一下；字不動）。
/// 用法：ItemToast.Show("草藥", 2, "持有 5")、ItemToast.Show("錢", 100, "共 350 文", "文")、ItemToast.Show("漁村地圖", 0, "按【N】打開")。
/// 不用放進場景（第一次呼叫時自己建立，換場景不會消失）；暫停選單、全螢幕畫面開著時藏起來、也不計時。
/// 圖載不到（MenuSkin.Available = false）時是深色半透明的條子、淺色的字。
/// </summary>
public class ItemToast : MonoBehaviour
{
    const float Width = 480f, Height = 96f, Gap = 12f, TopY = 210f;   // 1920×1080 的畫布單位：第一塊布的上緣離畫面頂端多遠
    const float Hold = 2.6f, InTime = 0.4f, OutTime = 0.5f, WindTime = 1.1f;
    const int MaxEntries = 4;

    sealed class Entry
    {
        public string item, unit;
        public int amount;
        public RectTransform rect;
        public CanvasGroup group;
        public ClothFlutter flutter;
        public Text title, detail;
        public float age, windUntil, y;
        public bool leaving;
        public float leaveAge;
    }

    static ItemToast host;
    RectTransform root;
    CanvasGroup rootGroup;
    readonly List<Entry> entries = new List<Entry>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    /// <summary>
    /// 顯示拿到東西。amount &gt; 0 時寫「名稱 +數量單位」，0 = 只寫名稱（地圖、包裹這種一件的東西）；
    /// detail = 下面一行小字（持有多少、拿去做什麼），可以是 null
    /// </summary>
    public static void Show(string item, int amount = 0, string detail = null, string unit = null)
    {
        if (string.IsNullOrEmpty(item)) return;
        if (host == null)
        {
            var go = new GameObject("ItemToast");
            DontDestroyOnLoad(go);
            host = go.AddComponent<ItemToast>();
            host.Build();
        }
        host.Add(item, amount, detail, unit ?? "");
    }

    void Build()
    {
        var canvas = UIKit.CreateCanvas("ItemToastCanvas", 60, transform);
        root = (RectTransform)canvas.transform;
        rootGroup = canvas.gameObject.AddComponent<CanvasGroup>();
        rootGroup.blocksRaycasts = false;
        rootGroup.interactable = false;
    }

    void OnDestroy() { if (host == this) host = null; }

    void Add(string item, int amount, string detail, string unit)
    {
        // 同一樣東西還掛著：合在一起
        foreach (var e in entries)
        {
            if (e.leaving || e.item != item || e.unit != unit) continue;
            e.amount += amount;
            e.title.text = TitleOf(e.item, e.amount, e.unit);
            e.detail.text = detail ?? "";
            e.age = Mathf.Min(e.age, InTime);
            e.windUntil = e.age + WindTime;
            return;
        }
        // 太多了：最舊的先收掉
        int alive = 0;
        foreach (var e in entries) if (!e.leaving) alive++;
        if (alive >= MaxEntries)
            foreach (var e in entries)
                if (!e.leaving) { e.leaving = true; e.leaveAge = 0f; break; }
        entries.Add(Make(item, amount, detail ?? "", unit));
    }

    static string TitleOf(string item, int amount, string unit) => amount > 0 ? $"{item} +{amount}{(string.IsNullOrEmpty(unit) ? "" : " " + unit)}" : item;

    Entry Make(string item, int amount, string detail, string unit)
    {
        bool skin = MenuSkin.Available;
        var rt = UIKit.Rect("Toast_" + item, root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, Height));
        rt.pivot = new Vector2(0.5f, 1f);
        var group = rt.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        // 布
        var cloth = rt.gameObject.AddComponent<Image>();
        cloth.raycastTarget = false;
        var sprite = MenuSkin.Cloth;
        if (sprite != null) { cloth.sprite = sprite; cloth.type = Image.Type.Sliced; cloth.color = MenuSkin.ClothNormal; }
        else cloth.color = GameUI.PanelColor;
        var flutter = rt.gameObject.AddComponent<ClothFlutter>();
        flutter.amplitude = 5f;

        // 朱印「得」
        UIKit.Seal(rt, "得", new Vector2(-Width * 0.5f + 54f, 0f), 54f);   // 子物件的位置以布的中心為準

        Color ink = skin ? MenuSkin.Ink : GameUI.TextColor;
        var title = Label("Title", rt, TitleOf(item, amount, unit), 32, ink, new Vector2(34f, 16f), new Vector2(Width - 150f, 40f), FontStyle.Bold);
        var sub = Label("Detail", rt, detail, 20, new Color(ink.r, ink.g, ink.b, 0.72f), new Vector2(34f, -20f), new Vector2(Width - 150f, 28f), FontStyle.Normal);

        var e = new Entry { item = item, unit = unit, amount = amount, rect = rt, group = group, flutter = flutter, title = title, detail = sub, windUntil = WindTime };
        // 從上面那一塊的位置開始（新的排在最下面）
        e.y = TargetY(CountAlive());
        rt.anchoredPosition = new Vector2(0f, -e.y + 24f);
        return e;
    }

    Text Label(string name, Transform parent, string text, int size, Color color, Vector2 pos, Vector2 box, FontStyle style)
    {
        var t = UIKit.Label(name, parent, text, size, color, pos, box, TextAnchor.MiddleLeft, style);
        var f = MenuSkin.Font;
        if (f != null) t.font = f;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        var sh = t.GetComponent<Shadow>();
        if (sh != null)
        {
            if (MenuSkin.Available) { sh.effectColor = new Color(1f, 0.94f, 0.8f, 0.25f); sh.effectDistance = new Vector2(1f, -1f); }
            else sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
        }
        return t;
    }

    int CountAlive()
    {
        int n = 0;
        foreach (var e in entries) if (!e.leaving) n++;
        return n;
    }

    static float TargetY(int index) => TopY + index * (Height + Gap);

    void Update()
    {
        if (root == null) return;
        // 暫停選單、全螢幕畫面開著：藏起來、不計時
        bool hidden = GameUI.OverlayOpen || (GameManager.Instance != null && GameManager.Instance.InMainMenu);
        rootGroup.alpha = Mathf.MoveTowards(rootGroup.alpha, hidden ? 0f : 1f, Time.unscaledDeltaTime * 8f);
        if (hidden) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);

        int slot = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            e.age += dt;
            if (!e.leaving && e.age > InTime + Hold) { e.leaving = true; e.leaveAge = 0f; }
            float alpha;
            float targetY;
            if (e.leaving)
            {
                e.leaveAge += dt;
                float k = Mathf.Clamp01(e.leaveAge / OutTime);
                alpha = 1f - k;
                targetY = e.y - 30f * k;   // 往上飄走
                if (k >= 1f)
                {
                    Destroy(e.rect.gameObject);
                    entries.RemoveAt(i);
                    i--;
                    continue;
                }
            }
            else
            {
                alpha = Mathf.SmoothStep(0f, 1f, e.age / InTime);
                targetY = TargetY(slot++);
                // 排的位置變了（上面那塊收掉了）：慢慢挪上去
                e.y = Mathf.Lerp(e.y, targetY, 1f - Mathf.Exp(-dt * 10f));
                targetY = e.y;
            }
            // 剛出現：從上面垂下來一點點
            float drop = (1f - Mathf.SmoothStep(0f, 1f, e.age / InTime)) * 24f;
            e.rect.anchoredPosition = new Vector2(0f, -targetY + drop);
            e.group.alpha = alpha;
            // 剛出現 / 合在一起時布被風吹一下，之後停住（只有布在飄，字不動）
            e.flutter.forced = !e.leaving && e.age < e.windUntil;
        }
    }
}
