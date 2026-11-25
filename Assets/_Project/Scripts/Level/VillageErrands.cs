using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 漁村的兩個小支線（不用在場景裡放東西，也不用重建場景：進漁村時自動掛上）：
///   - 賣花的姑娘（Walker_Girl，FlowerGirlGift）：第一次說完話，她說「這是我在後山摘花時順手拔的，不知道是什麼草，送你吧」，給兩株草藥（拿去給藥師換藥水）
///   - 跑腿的男孩（Walker_Boy，ErrandRun）：請玩家幫忙跑腿——到東邊盡頭那間房子（中間那排最東邊）門口拿包裹，
///     送到海邊那排最西邊的房子，收件的人家給 100 文。任務進行中畫面右邊寫著現在要做什麼，要去的房子頭上有記號。
/// 進度記在 StoryProgress 的旗標裡，會跟著存檔（送過的草藥不會再送、跑腿做完不會再接）。
/// </summary>
public static class VillageErrands
{
    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (s, m) => Attach(s);
        }
        Attach(SceneManager.GetActiveScene());
    }

    static void Attach(Scene s)
    {
        if (s.name != VillageReturn.VillageScene) return;
        AddTo<FlowerGirlGift>("Walker_Girl");
        AddTo<ErrandRun>("Walker_Boy");
    }

    static void AddTo<T>(string name) where T : Component
    {
        var go = GameObject.Find(name);
        if (go != null && go.GetComponent<MerchantNPC>() != null && go.GetComponent<T>() == null) go.AddComponent<T>();
    }

    /// <summary>村民平常的台詞：第一次講完整的，之後講招呼（跟 MerchantNPC 原本一樣）</summary>
    public static List<string> BaseLines(MerchantNPC npc, ref bool talked)
    {
        var lines = new List<string>(!talked || npc.repeatLines == null || npc.repeatLines.Length == 0 ? npc.guideLines ?? new string[0] : npc.repeatLines);
        talked = true;
        return lines;
    }
}

/// <summary>賣花的姑娘：第一次說完話送兩株草藥（只送一次，記在存檔裡）</summary>
public class FlowerGirlGift : MonoBehaviour
{
    public const string Flag = "flowerGift";
    public int herbs = 2;

    MerchantNPC npc;
    bool talked;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        if (npc == null) { Destroy(this); return; }
        npc.linesProvider = Lines;
        npc.choiceProvider = Choice;
    }

    string[] Lines()
    {
        var lines = VillageErrands.BaseLines(npc, ref talked);
        if (!StoryProgress.Has(Flag))
        {
            lines.Add("啊，對了！這是我在後山摘花的時候順手拔的……");
            lines.Add("這不知道是什麼草，送你吧！");
        }
        return lines.ToArray();
    }

    DialogueChoice Choice()
    {
        if (StoryProgress.Has(Flag)) return null;
        return new DialogueChoice
        {
            question = $"（賣花的姑娘把 {herbs} 株草塞到你手裡）",
            options = new[] { "收下" },
            onPick = _ =>
            {
                StoryProgress.Set(Flag);
                VillageWallet.Add(VillageWallet.HerbKey, herbs);
                GameAudio.Play("SFX/RPG/cloth1", transform.position, 0.7f, 1.2f, 0.08f, 0.3f);
                ItemToast.Show("草藥", herbs, $"持有 {VillageWallet.Get(VillageWallet.HerbKey)}　可以拿去給藥師換藥水");
                return new[] { "嘿嘿，說不定是什麼好東西呢！" };
            },
        };
    }
}

/// <summary>
/// 跑腿的男孩：接下跑腿 → 到東邊盡頭那間房子（中間那排 Row_B 最東邊）門口拿包裹 → 送到海邊那排（Row_A）最西邊的房子 → 收件的人家給 100 文。
/// 包裹是程式做的小布包，放在東邊那間房子正面（朝海那一面）的屋簷外；兩邊都是走過去按互動鍵。
/// </summary>
public class ErrandRun : MonoBehaviour
{
    public const string Accepted = "errand.accepted", Picked = "errand.parcel", Done = "errand.done";
    public int reward = 100;

    MerchantNPC npc;
    bool talked, found;
    Vector3 pickupAt, dropoffAt;
    GameObject parcel;
    Material parcelMat;
    Transform player;
    GUIStyle hudStyle, markStyle;

    static bool Has(string f) => StoryProgress.Has(f);
    bool Active => Has(Accepted) && !Has(Done);

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        if (npc == null) { Destroy(this); return; }
        npc.linesProvider = Lines;
        npc.choiceProvider = Choice;
        var east = Farthest("Row_B", 1f);
        var west = Farthest("Row_A", -1f);
        found = east != null && west != null;
        if (found)
        {
            pickupAt = FrontOf(east);
            dropoffAt = FrontOf(west);
            Debug.Log($"[ErrandRun] 拿包裹：{east.name} {pickupAt}，送到：{west.name} {dropoffAt}");
        }
        else Debug.LogWarning("[ErrandRun] 找不到 Row_A / Row_B 的房子，跑腿任務做不了");
    }

    void OnDestroy()
    {
        if (parcel != null) Destroy(parcel);
        if (parcelMat != null) Destroy(parcelMat);
    }

    // ---------------------------------------------------------------- 對話

    string[] Lines()
    {
        if (Has(Done)) return new[] { "謝謝你幫我跑腿！老闆還誇我動作快，嘿嘿。" };
        if (Has(Picked)) return new[] { "包裹拿到了嗎？快送去海邊那排最西邊的房子！" };
        if (Has(Accepted)) return new[] { "包裹在東邊盡頭那間房子門口！中間那排、最東邊的那間。" };
        var lines = VillageErrands.BaseLines(npc, ref talked);
        lines.Add("啊！你看起來很可靠……可以幫我一個忙嗎？");
        lines.Add("老闆要我把一個包裹從東邊盡頭那間房子送到海邊的房子，可是我還有三個地方要跑，根本來不及！");
        return lines.ToArray();
    }

    DialogueChoice Choice()
    {
        if (Has(Accepted)) return null;
        return new DialogueChoice
        {
            question = "要幫跑腿的男孩送包裹嗎？",
            options = new[] { "好，交給我", "現在沒空" },
            onPick = i =>
            {
                if (i != 0) return new[] { "好吧……那我自己跑。唉，腳都要斷了。" };
                StoryProgress.Set(Accepted);
                return new[]
                {
                    "太好了！包裹放在東邊盡頭那間房子門口——中間那排、最東邊的那間。",
                    $"送到海邊那排最西邊的房子，那家人會給你跑腿錢，有 {reward} 文喔！",
                };
            },
        };
    }

    // ---------------------------------------------------------------- 拿包裹、送包裹

    void Update()
    {
        if (!found) return;
        if (player == null) { var hp = PlayerHealth.Find(); if (hp == null) return; player = hp.transform; }
        bool needParcel = Has(Accepted) && !Has(Picked);
        if (needParcel && parcel == null) parcel = MakeParcel(pickupAt);
        if (parcel != null && parcel.activeSelf != needParcel) parcel.SetActive(needParcel);
        if (GameUI.OverlayOpen || StoryDialogue.Active) return;

        if (needParcel)
        {
            float d = Flat(pickupAt - player.position).magnitude;
            if (d < 2.8f)
            {
                InteractPrompt.Offer(this, d, "拿起包裹");
                if (InteractPrompt.Pressed(this)) Pick();
            }
        }
        else if (Has(Picked) && !Has(Done))
        {
            float d = Flat(dropoffAt - player.position).magnitude;
            if (d < 3f)
            {
                InteractPrompt.Offer(this, d, "送包裹");
                if (InteractPrompt.Pressed(this)) Deliver();
            }
        }
    }

    void Pick()
    {
        StoryProgress.Set(Picked);
        if (parcel != null) parcel.SetActive(false);
        GameAudio.Play("SFX/RPG/handleSmallLeather", pickupAt, 0.8f, 1f, 0.05f, 0.3f);
        ItemToast.Show("跑腿的包裹", 0, "送到海邊那排最西邊的房子");
        AutoSave.Request("拿到跑腿的包裹");
    }

    void Deliver()
    {
        StoryProgress.Set(Done);
        VillageWallet.Add(VillageWallet.CoinKey, reward);
        GameAudio.Play("SFX/RPG/handleCoins", dropoffAt, 0.9f, 1f, 0.05f, 0.3f);
        var color = new Color(0.95f, 0.8f, 0.55f);
        StoryDialogue.Play(new List<StoryDialogue.Line>
        {
            StoryDialogue.L("海邊人家", null, true, "哎呀，是那孩子託你送來的？從東邊盡頭一路走到這裡，辛苦你了。", color),
            StoryDialogue.L("海邊人家", null, true, $"這是跑腿錢，{reward} 文，收下吧。", color),
        }, () => ItemToast.Show("錢", reward, $"跑腿完成　持有 {VillageWallet.Coins} 文", "文"));
    }

    // ---------------------------------------------------------------- 房子、包裹

    /// <summary>那一排最東（sign = 1）/ 最西（sign = -1）的房子</summary>
    static Transform Farthest(string row, float sign)
    {
        var r = GameObject.Find(row);
        if (r == null) return null;
        Transform best = null;
        float bestX = float.NegativeInfinity;
        foreach (Transform c in r.transform)
        {
            if (!c.name.StartsWith("House_")) continue;
            float x = c.position.x * sign;
            if (x > bestX) { bestX = x; best = c; }
        }
        return best;
    }

    /// <summary>房子正面（朝海、-Z）屋簷外面一點的地上（踏石之類的上面）</summary>
    static Vector3 FrontOf(Transform house)
    {
        bool any = false;
        var b = new Bounds(house.position, Vector3.zero);
        foreach (var r in house.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        Vector3 p = new Vector3(house.position.x, house.position.y, (any ? b.min.z : house.position.z - 8f) - 0.9f);
        var terrain = Terrain.activeTerrain;
        if (terrain != null) p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 2.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) p.y = Mathf.Max(p.y, hit.point.y);
        return p;
    }

    /// <summary>包裹：藍染的布包著一個方盒，上面打了一個結</summary>
    GameObject MakeParcel(Vector3 at)
    {
        var root = new GameObject("ErrandParcel");
        root.transform.position = at;
        root.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
        parcelMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        parcelMat.SetColor("_BaseColor", new Color(0.16f, 0.22f, 0.45f));
        parcelMat.SetFloat("_Smoothness", 0.15f);
        void Part(PrimitiveType type, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = parcelMat;
        }
        Part(PrimitiveType.Cube, new Vector3(0f, 0.17f, 0f), new Vector3(0.5f, 0.34f, 0.4f));
        Part(PrimitiveType.Sphere, new Vector3(0f, 0.38f, 0f), new Vector3(0.2f, 0.12f, 0.2f));
        Part(PrimitiveType.Sphere, new Vector3(-0.12f, 0.4f, 0f), new Vector3(0.14f, 0.08f, 0.1f));
        Part(PrimitiveType.Sphere, new Vector3(0.12f, 0.4f, 0f), new Vector3(0.14f, 0.08f, 0.1f));
        return root;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // ---------------------------------------------------------------- 畫面：右邊的任務、目標的記號

    void OnGUI()
    {
        if (!found || !Active || player == null || GameUI.OverlayOpen || VitalsHud.Hidden || StoryDialogue.Active) return;
        bool picked = Has(Picked);
        Vector3 target = picked ? dropoffAt : pickupAt;
        float dist = Flat(target - player.position).magnitude;

        // 右邊（小地圖下面）：現在要做什麼
        hudStyle ??= new GUIStyle(GameUI.Small) { alignment = TextAnchor.MiddleRight, fontSize = 17, wordWrap = false };
        string text = picked ? "跑腿：把包裹送到海邊那排最西邊的房子" : "跑腿：到東邊盡頭的房子（中間那排最東邊）拿包裹";
        var r = new Rect(Screen.width - 520f, Screen.height * 0.36f, 496f, 26f);
        hudStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, hudStyle);
        hudStyle.normal.textColor = GameUI.Accent;
        GUI.Label(r, text, hudStyle);
        var r2 = new Rect(r.x, r.y + 22f, r.width, 22f);
        hudStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(r2.x + 1.5f, r2.y + 1.5f, r2.width, r2.height), $"還有 {dist:F0} m", hudStyle);
        hudStyle.normal.textColor = GameUI.TextColor;
        GUI.Label(r2, $"還有 {dist:F0} m", hudStyle);

        // 目標頭上的記號（看得到的時候）
        var cam = Camera.main;
        if (cam == null || dist < 2.5f) return;
        Vector3 sp = cam.WorldToScreenPoint(target + Vector3.up * (2.6f + Mathf.Sin(Time.time * 2.4f) * 0.15f));
        if (sp.z <= 0f) return;
        markStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        markStyle.fontSize = Mathf.RoundToInt(Mathf.Lerp(30f, 18f, Mathf.Clamp01(dist / 120f)));
        var mr = new Rect(sp.x - 60f, Screen.height - sp.y - 22f, 120f, 44f);
        string mark = picked ? "▼ 送到這裡" : "▼ 包裹";
        markStyle.normal.textColor = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(mr.x + 2f, mr.y + 2f, mr.width, mr.height), mark, markStyle);
        markStyle.normal.textColor = GameUI.Accent;
        GUI.Label(mr, mark, markStyle);
    }
}
