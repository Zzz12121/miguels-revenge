using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 漁村的兩個小支線（不用在場景裡放東西，也不用重建場景：進漁村時自動掛上）：
///   - 賣花的姑娘（Walker_Girl，FlowerGirlGift）：第一次說完話，她說「這是我在後山摘花時順手拔的，不知道是什麼草，送你吧」，給兩株草藥（拿去給藥師換藥水）
///   - 跑腿的男孩（Walker_Boy，ErrandRun）：請玩家幫忙跑腿——到東邊盡頭那間房子（中間那排最東邊）找穿藍色衣服的老闆拿包裹，
///     送到海邊那排最西邊的房子交給穿綠色衣服的人，收件的人家給 100 文。任務進行中畫面左邊寫著現在要做什麼，要找的人頭上有箭頭。
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
/// 跑腿的男孩：接下跑腿 → 到東邊盡頭那間房子（中間那排 Row_B 最東邊）門口，找穿藍色衣服的老闆拿包裹 →
/// 送到海邊那排（Row_A）最西邊的房子，交給門口穿綠色衣服的人 → 收件的人家給 100 文。
/// 兩個人都是進漁村時在房子門口生出來的（複製場景裡村民的模型：收網的漁夫是藍衣服、宅院的家僕是綠衣服，
/// 不用重建場景），走過去對話就能拿 / 交包裹。任務進行中畫面左邊（祠堂的試煉下面）寫著現在要做什麼，
/// 要找的人頭上有一個箭頭（不寫字）。
/// </summary>
public class ErrandRun : MonoBehaviour
{
    public const string Accepted = "errand.accepted", Picked = "errand.parcel", Done = "errand.done";
    public int reward = 100;

    const string SenderName = "雜貨店老闆", ReceiverName = "海邊人家";
    const string SenderLook = "藍色", ReceiverLook = "綠色";

    MerchantNPC npc, sender, receiver;
    bool talked, found;
    Vector3 pickupAt, dropoffAt;
    GameObject parcel;
    Material parcelMat;
    Transform player;
    ShrineTrial shrine;
    GUIStyle titleStyle, lineStyle, hintStyle, markStyle;

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
        if (east == null || west == null) { Debug.LogWarning("[ErrandRun] 找不到 Row_A / Row_B 的房子，跑腿任務做不了"); return; }
        pickupAt = FrontOf(east);
        dropoffAt = FrontOf(west);
        sender = MakeNpc("Errand_Sender", "Walker_Fisher", pickupAt, SenderName);
        receiver = MakeNpc("Errand_Receiver", "Walker_Servant", dropoffAt, ReceiverName);
        found = sender != null && receiver != null;
        if (!found) { Debug.LogWarning("[ErrandRun] 找不到村民的模型（Walker_Fisher / Walker_Servant），跑腿任務做不了"); return; }
        sender.linesProvider = SenderLines;
        sender.choiceProvider = SenderChoice;
        receiver.linesProvider = ReceiverLines;
        receiver.choiceProvider = ReceiverChoice;
    }

    void OnDestroy()
    {
        if (parcel != null) Destroy(parcel);
        if (parcelMat != null) Destroy(parcelMat);
        if (sender != null) Destroy(sender.gameObject);
        if (receiver != null) Destroy(receiver.gameObject);
    }

    // ---------------------------------------------------------------- 跑腿的男孩

    string[] Lines()
    {
        if (Has(Done)) return new[] { "謝謝你幫我跑腿！老闆還誇我動作快，嘿嘿。" };
        if (Has(Picked)) return new[] { $"包裹拿到了嗎？快送去海邊那排最西邊的房子，交給穿{ReceiverLook}衣服的人！" };
        if (Has(Accepted)) return new[] { $"包裹在東邊盡頭那間房子！中間那排、最東邊的那間，找門口穿{SenderLook}衣服的老闆。" };
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
                    $"太好了！東邊盡頭那間房子——中間那排、最東邊的那間，門口穿{SenderLook}衣服的老闆會把包裹交給你。",
                    $"送到海邊那排最西邊的房子，交給穿{ReceiverLook}衣服的人，他們會給你跑腿錢，有 {reward} 文喔！",
                };
            },
        };
    }

    // ---------------------------------------------------------------- 拿包裹（東邊的老闆）

    string[] SenderLines()
    {
        if (Has(Done)) return new[] { "包裹送到啦？謝謝你，下次有事再拜託你。" };
        if (Has(Picked)) return new[] { $"包裹就麻煩你了，送到海邊那排最西邊的房子，交給穿{ReceiverLook}衣服的那位。" };
        if (Has(Accepted)) return new[] { "喔！你是那孩子找來幫忙的？", "那孩子一天到晚跑來跑去，總算找到幫手了。" };
        return new[] { "這幾天貨多，忙都忙不過來。", "要是有人能幫忙送個貨就好了……" };
    }

    DialogueChoice SenderChoice()
    {
        if (!Has(Accepted) || Has(Picked)) return null;
        return new DialogueChoice
        {
            question = $"（{SenderName}把一個藍染布包著的包裹遞給你）",
            options = new[] { "收下包裹" },
            onPick = _ =>
            {
                StoryProgress.Set(Picked);
                if (parcel != null) parcel.SetActive(false);
                GameAudio.Play("SFX/RPG/handleSmallLeather", pickupAt, 0.8f, 1f, 0.05f, 0.3f);
                ItemToast.Show("跑腿的包裹", 0, $"送到海邊那排最西邊的房子，交給穿{ReceiverLook}衣服的人");
                AutoSave.Request("拿到跑腿的包裹");
                return new[] { $"送到海邊那排最西邊的房子，交給門口穿{ReceiverLook}衣服的那位就行了。路上小心！" };
            },
        };
    }

    // ---------------------------------------------------------------- 送包裹（西邊的海邊人家）

    string[] ReceiverLines()
    {
        if (Has(Done)) return new[] { "上次的包裹謝謝你啦，東西都好好的。" };
        if (Has(Picked)) return new[] { "哎呀，這包裹……是東邊那家託你送來的？" };
        return new[] { "海風真舒服。", "東邊那家說要送東西過來，怎麼還沒到呢……" };
    }

    DialogueChoice ReceiverChoice()
    {
        if (!Has(Picked) || Has(Done)) return null;
        return new DialogueChoice
        {
            question = $"要把包裹交給{ReceiverName}嗎？",
            options = new[] { "交出包裹" },
            onPick = _ =>
            {
                StoryProgress.Set(Done);
                VillageWallet.Add(VillageWallet.CoinKey, reward);
                GameAudio.Play("SFX/RPG/handleCoins", dropoffAt, 0.9f, 1f, 0.05f, 0.3f);
                ItemToast.Show("錢", reward, $"跑腿完成　持有 {VillageWallet.Coins} 文", "文");
                return new[]
                {
                    "從東邊盡頭一路走到這裡，辛苦你了。",
                    $"這是跑腿錢，{reward} 文，收下吧。",
                };
            },
        };
    }

    // ---------------------------------------------------------------- 包裹放在老闆腳邊

    void Update()
    {
        if (!found) return;
        if (player == null) { var hp = PlayerHealth.Find(); if (hp != null) player = hp.transform; }
        bool needParcel = Has(Accepted) && !Has(Picked);
        if (needParcel && parcel == null) parcel = MakeParcel(pickupAt + sender.transform.right * 0.75f);
        if (parcel != null && parcel.activeSelf != needParcel) parcel.SetActive(needParcel);
    }

    // ---------------------------------------------------------------- 房子、人、包裹

    /// <summary>
    /// 在房子門口生一個村民：複製場景裡某個村民的模型（衣服顏色跟著那個村民的材質），站著播待機（Animator 的 Speed 是 0），
    /// 加上碰撞和對話（MerchantNPC，不賣東西）。面向街上（房子正面朝 -Z）
    /// </summary>
    static MerchantNPC MakeNpc(string name, string source, Vector3 at, string speaker)
    {
        var src = GameObject.Find(source);
        var model = src != null ? src.transform.Find("Model") : null;
        if (model == null) return null;
        var go = new GameObject(name);
        go.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, 180f, 0f));
        go.transform.localScale = src.transform.lossyScale;
        var m = Instantiate(model.gameObject, go.transform, false);
        m.name = "Model";
        m.transform.SetLocalPositionAndRotation(model.localPosition, model.localRotation);
        m.transform.localScale = model.localScale;
        // 只留模型和 Animator：複製過來的村民行為（走路、拿東西）都拿掉
        foreach (var b in m.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(b);
        var anim = m.GetComponent<Animator>();
        if (anim != null) { anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }

        var capsule = go.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.34f;
        var npc = go.AddComponent<MerchantNPC>();
        npc.speakerName = speaker;
        npc.sellsGoods = false;
        npc.facePlayerOnTalk = true;
        npc.interactRange = 3f;
        npc.guideLines = new string[0];
        npc.repeatLines = new string[0];
        return npc;
    }

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

    // ---------------------------------------------------------------- 畫面：左邊的任務（跟祠堂的試煉一樣）、要找的人頭上的箭頭

    void OnGUI()
    {
        if (!found || !Active || player == null || GameUI.OverlayOpen || VitalsHud.Hidden) return;
        // GUI 的文字顏色是全域的：畫完再還原
        Color prevContent = GUI.contentColor, prevBackground = GUI.backgroundColor;
        GUI.contentColor = Color.white;
        GUI.backgroundColor = Color.white;
        bool picked = Has(Picked);
        var who = picked ? receiver : sender;
        float dist = Flat(who.transform.position - player.position).magnitude;
        DrawPanel(picked, dist);
        if (!StoryDialogue.Active && !who.InConversation) DrawArrow(who.transform, dist);
        GUI.contentColor = prevContent;
        GUI.backgroundColor = prevBackground;
        GUI.color = Color.white;
    }

    /// <summary>左上的任務框（樣式跟祠堂的試煉一樣）：祠堂的試煉也在畫的話排在它下面</summary>
    void DrawPanel(bool picked, float dist)
    {
        titleStyle ??= new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold, fontSize = 14 };
        lineStyle ??= new GUIStyle(GameUI.Small) { fontSize = 13, wordWrap = true };
        hintStyle ??= new GUIStyle(GameUI.Small) { fontSize = 12 };
        titleStyle.normal.textColor = lineStyle.normal.textColor = hintStyle.normal.textColor = Color.white;

        if (shrine == null) shrine = FindAnyObjectByType<ShrineTrial>();
        float y = 150f;
        if (shrine != null && shrine.isActiveAndEnabled && (shrine.Accepted || shrine.ReadingsDone > 0))
            y += (shrine.PortalOpened ? 50f : 132f) + 8f;

        var box = new Rect(16f, y, 230f, 88f);
        GameUI.Panel(box);
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 20f), "跑腿", titleStyle);
        string text = picked ? $"把包裹交給海邊那排最西邊的房子門口，穿{ReceiverLook}衣服的人"
                             : $"到東邊盡頭的房子（中間那排最東邊），找穿{SenderLook}衣服的老闆拿包裹";
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(box.x + 10f, box.y + 25f, box.width - 20f, 38f), text, lineStyle);
        GUI.color = GameUI.MutedColor;
        GUI.Label(new Rect(box.x + 10f, box.y + 64f, box.width - 20f, 18f), $"還有 {dist:F0} m", hintStyle);
        GUI.color = Color.white;
    }

    /// <summary>要找的人頭上一個上下浮動的箭頭（不寫字）</summary>
    void DrawArrow(Transform who, float dist)
    {
        var cam = Camera.main;
        if (cam == null || dist < 2.5f) return;
        Vector3 sp = cam.WorldToScreenPoint(who.position + Vector3.up * (2.7f + Mathf.Sin(Time.time * 2.4f) * 0.15f));
        if (sp.z <= 0f) return;
        markStyle ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        markStyle.normal.textColor = Color.white;
        markStyle.fontSize = Mathf.RoundToInt(Mathf.Lerp(34f, 20f, Mathf.Clamp01(dist / 120f)));
        var mr = new Rect(sp.x - 30f, Screen.height - sp.y - 22f, 60f, 44f);
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.Label(new Rect(mr.x + 2f, mr.y + 2f, mr.width, mr.height), "▼", markStyle);
        GUI.color = GameUI.Accent;
        GUI.Label(mr, "▼", markStyle);
    }
}
