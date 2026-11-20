using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 漁村的錢（文）：跟草藥一樣存在 PlayerInventory（資源名 Coin），所以會跟著換關卡、存檔。
/// 釣到的魚、買過的造型、穿哪一件也都存在 PlayerInventory。
/// </summary>
public static class VillageWallet
{
    public const string CoinKey = "Coin";
    public const string HerbKey = "Herb";

    static PlayerInventory cached;

    /// <summary>玩家的背包（沒有就加上）；場景裡沒有玩家回傳 null</summary>
    public static PlayerInventory Inventory
    {
        get
        {
            if (cached != null) return cached;
            var hp = PlayerHealth.Find();
            if (hp == null) return null;
            cached = hp.GetComponent<PlayerInventory>();
            if (cached == null) cached = hp.gameObject.AddComponent<PlayerInventory>();
            return cached;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => cached = null;

    public static int Coins => Get(CoinKey);
    public static int Get(string key) { var inv = Inventory; return inv != null ? inv.GetResource(key) : 0; }
    public static void Add(string key, int amount) { var inv = Inventory; if (inv != null && amount != 0) inv.AddResource(key, amount); }
    public static void Set(string key, int value) => Add(key, value - Get(key));
    /// <summary>付錢：不夠就不扣，回傳 false</summary>
    public static bool Spend(int coins) { var inv = Inventory; return inv != null && inv.ConsumeResource(CoinKey, coins); }
    public static string WalletText => $"錢：{Coins} 文";
}

/// <summary>
/// 畫面右上角的錢（只有在店裡、對話框商店打開時才畫）：一枚日本古代的小判金幣（直立的橢圓、暗黃色的金，
/// 外圈一道邊、表面一條條橫的槌紋，上下各一個淺淺的刻印，稍微斜放），旁邊是多少錢（只有數字）
/// </summary>
public static class CoinHud
{
    static Texture2D koban;
    static GUIStyle amountStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { amountStyle = null; } // 貼圖留著（不跟字型有關）

    public static void Draw(int coins)
    {
        if (Event.current.type != EventType.Repaint && Event.current.type != EventType.Layout) return;
        koban ??= MakeKoban();
        amountStyle ??= new GUIStyle(GameUI.Title) { fontSize = 20, alignment = TextAnchor.MiddleRight };
        const float coinW = 34f, coinH = 44f, margin = 22f;
        float sw = Screen.width;
        var coin = new Rect(sw - margin - coinW, margin, coinW, coinH);
        // 金幣稍微斜放（往右傾 18 度），後面一圈淡淡的影子
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(18f, coin.center);
        GUI.color = new Color(0f, 0f, 0f, 0.35f);
        GUI.DrawTexture(new Rect(coin.x + 2f, coin.y + 3f, coin.width, coin.height), koban);
        GUI.color = Color.white;
        GUI.DrawTexture(coin, koban);
        GUI.matrix = saved;
        // 數字：小一點、暗一點的金色（不寫單位）
        GameUI.Shadowed(new Rect(coin.x - 206f, coin.y, 198f, coinH), coins.ToString(), amountStyle, new Color(0.74f, 0.61f, 0.33f, 0.95f));
    }

    /// <summary>小判：稍微直立的橢圓（寬 : 高 = 0.77），暗黃色的金；邊緣一圈亮一點的邊，裡面一條條橫的槌紋，左上亮、右下暗</summary>
    static Texture2D MakeKoban()
    {
        const int w = 66, h = 86;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave, name = "Runtime_Koban" };
        Color baseGold = new Color(0.52f, 0.39f, 0.12f), light = new Color(0.7f, 0.56f, 0.24f), dark = new Color(0.28f, 0.19f, 0.05f); // 暗一點的金
        float a = w * 0.5f - 1.5f, b = h * 0.5f - 1.5f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f - w * 0.5f) / a, v = (y + 0.5f - h * 0.5f) / b;
                float d = Mathf.Sqrt(u * u + v * v);
                float alpha = Mathf.Clamp01((1f - d) * Mathf.Min(a, b) + 0.5f); // 邊緣抗鋸齒
                if (alpha <= 0f) { tex.SetPixel(x, y, new Color(0f, 0f, 0f, 0f)); continue; }
                // 左上亮、右下暗（y 往上是 +，貼圖的 y = 0 在下面）
                float shade = Mathf.Clamp01(0.5f + 0.35f * (-u + v) * 0.7f);
                Color c = Color.Lerp(dark, light, shade);
                c = Color.Lerp(c, baseGold, 0.45f);
                if (d > 0.84f) // 外圈的邊：一道亮、一道暗
                    c = d > 0.92f ? Color.Lerp(baseGold, light, 0.5f) : Color.Lerp(baseGold, dark, 0.55f);
                else
                {
                    // 橫的槌紋（茣蓙目）：每 4 px 一條暗線
                    if (y % 4 == 0) c = Color.Lerp(c, dark, 0.35f);
                    // 上下各一個淺淺的橢圓刻印
                    float s1 = Mathf.Sqrt(u * u / 0.09f + (v - 0.55f) * (v - 0.55f) / 0.02f), s2 = Mathf.Sqrt(u * u / 0.09f + (v + 0.55f) * (v + 0.55f) / 0.02f);
                    if (Mathf.Abs(s1 - 1f) < 0.25f || Mathf.Abs(s2 - 1f) < 0.25f) c = Color.Lerp(c, dark, 0.5f);
                }
                c.a = alpha;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }
}

/// <summary>釣得到的魚：越大的魚上鉤時要按的數字越多，賣得也越貴</summary>
public static class FishCatalog
{
    public sealed class Fish
    {
        public readonly string id, name;
        public readonly int price, digits;
        /// <summary>按完全部數字的時間（秒）</summary>
        public readonly float seconds;
        /// <summary>上鉤的機率權重</summary>
        public readonly float weight;
        public readonly Color color;
        /// <summary>魚的身長（m，拉上來時飛過來的那條魚）</summary>
        public readonly float length;
        public Fish(string id, string name, int price, int digits, float seconds, float weight, Color color, float length)
        { this.id = id; this.name = name; this.price = price; this.digits = digits; this.seconds = seconds; this.weight = weight; this.color = color; this.length = length; }
    }

    public static readonly Fish[] All =
    {
        new Fish("Fish_Aji", "竹莢魚", 6, 5, 3.2f, 40f, new Color(.62f, .7f, .74f), .22f),
        new Fish("Fish_Saba", "鯖魚", 10, 6, 3.6f, 28f, new Color(.32f, .5f, .52f), .3f),
        new Fish("Fish_Suzuki", "鱸魚", 16, 7, 4f, 18f, new Color(.7f, .72f, .7f), .42f),
        new Fish("Fish_Tai", "真鯛", 28, 8, 4.4f, 10f, new Color(.86f, .45f, .45f), .4f),
        new Fish("Fish_Kinme", "金目鯛", 60, 10, 5f, 4f, new Color(.9f, .2f, .12f), .38f),
    };

    public static Fish Roll()
    {
        float total = 0f;
        foreach (var f in All) total += f.weight;
        float x = Random.value * total;
        foreach (var f in All) { x -= f.weight; if (x <= 0f) return f; }
        return All[0];
    }

    /// <summary>身上一共幾條魚</summary>
    public static int CountAll()
    {
        int n = 0;
        foreach (var f in All) n += VillageWallet.Get(f.id);
        return n;
    }
}

/// <summary>行商賣的造型（PlayerCosmetics 照這張表做出來）</summary>
public static class Wardrobe
{
    public enum Slot { Hat, Scarf, Hook }

    public sealed class Item
    {
        public readonly Slot slot;
        public readonly int index;
        public readonly string name, desc;
        public readonly int price;
        public Item(Slot slot, int index, string name, string desc, int price) { this.slot = slot; this.index = index; this.name = name; this.desc = desc; this.price = price; }
    }

    public static readonly Item[] Items =
    {
        new Item(Slot.Hat, 1, "斗笠", "跟老漁夫一樣的草編斗笠，帽沿毛毛的", 60),
        new Item(Slot.Hat, 2, "竹笠", "跟巡邏的村丁一樣的竹編笠，綁著一圈圈竹篾", 90),
        new Item(Slot.Hat, 3, "烏帽子", "高高的黑漆烏帽子，京城來的貨", 120),
        new Item(Slot.Scarf, 1, "朱紅圍巾", "素面的朱紅棉布圍巾", 50),
        new Item(Slot.Scarf, 2, "藍染圍巾", "素面的藍染棉布圍巾", 70),
        new Item(Slot.Scarf, 3, "雪白長圍巾", "素面的白絹長圍巾，巾尾很長、跑起來會飄", 100),
        new Item(Slot.Hook, 1, TetherRopeVisual.Skins[1].name, "朱紅的絲繩、黃銅的鉤爪", 80),
        new Item(Slot.Hook, 2, TetherRopeVisual.Skins[2].name, "墨黑的繩子、亮銀的鉤爪", 100),
        new Item(Slot.Hook, 3, TetherRopeVisual.Skins[3].name, "青藤搓的繩子、竹子削的鉤爪", 130),
    };

    /// <summary>每一類「不穿 / 原本的」那一項（index 0，不用錢）</summary>
    public static string NoneName(Slot s) => s == Slot.Hat ? "不戴帽子" : s == Slot.Scarf ? "不圍圍巾" : TetherRopeVisual.Skins[0].name + "（原本的）";
    public static string SlotName(Slot s) => s == Slot.Hat ? "帽子" : s == Slot.Scarf ? "圍巾" : "鉤索造型";

    public static string WearKey(Slot s) => "Wear_" + s;
    public static string OwnKey(Slot s, int index) => $"Own_{s}_{index}";
    public static int Wearing(Slot s) => VillageWallet.Get(WearKey(s));
    public static bool Owns(Slot s, int index) => index == 0 || VillageWallet.Get(OwnKey(s, index)) > 0;
    public static void Wear(Slot s, int index) => VillageWallet.Set(WearKey(s), index);
    public static void Unlock(Slot s, int index) => VillageWallet.Set(OwnKey(s, index), 1);

    public static Item Find(Slot s, int index)
    {
        foreach (var it in Items) if (it.slot == s && it.index == index) return it;
        return null;
    }

    public static string NameOf(Slot s, int index) => index == 0 ? NoneName(s) : Find(s, index)?.name ?? NoneName(s);

    public static List<Item> InSlot(Slot s)
    {
        var list = new List<Item>();
        foreach (var it in Items) if (it.slot == s) list.Add(it);
        return list;
    }
}

// ---------------------------------------------------------------- 三家店

/// <summary>岸邊補網的年輕漁夫：照市價收魚（一次賣掉一種，或全部賣掉）</summary>
public sealed class FishBuyerShop : IDialogueShop
{
    readonly List<FishCatalog.Fish> stock = new List<FishCatalog.Fish>();

    void Refresh()
    {
        stock.Clear();
        foreach (var f in FishCatalog.All) if (VillageWallet.Get(f.id) > 0) stock.Add(f);
    }

    public string HintLabel => "賣魚";
    public int Wallet => VillageWallet.Coins;
    public string LeaveLabel => "離開";
    public int Count { get { Refresh(); return stock.Count == 0 ? 0 : stock.Count + 1; } }

    public string Open()
    {
        Refresh();
        return stock.Count == 0
            ? "你身上沒有魚啊。去棧橋末端跟老漁夫一起釣吧，釣到了拿來，我照市價收。"
            : "喔？釣到魚了！讓我看看……嗯，挺新鮮的。要賣哪些？";
    }

    public ShopEntry Entry(int index)
    {
        Refresh();
        if (index == 0)
        {
            int total = 0, n = 0;
            foreach (var f in stock) { int c = VillageWallet.Get(f.id); n += c; total += c * f.price; }
            return new ShopEntry($"全部賣掉（{n} 條）", $"{total} 文", true);
        }
        var fish = stock[index - 1];
        int count = VillageWallet.Get(fish.id);
        return new ShopEntry($"{fish.name} ×{count}", $"每條 {fish.price} 文　共 {fish.price * count} 文", true);
    }

    public string Select(int index)
    {
        Refresh();
        if (stock.Count == 0) return null;
        int earned = 0, n = 0;
        string last = null;
        var sell = index == 0 ? new List<FishCatalog.Fish>(stock) : new List<FishCatalog.Fish> { stock[Mathf.Clamp(index - 1, 0, stock.Count - 1)] };
        foreach (var f in sell)
        {
            int c = VillageWallet.Get(f.id);
            if (c <= 0) continue;
            VillageWallet.Set(f.id, 0);
            earned += c * f.price;
            n += c;
            last = f.name;
        }
        if (earned <= 0) return null;
        VillageWallet.Add(VillageWallet.CoinKey, earned);
        var p = PlayerHealth.Find();
        GameAudio.Play("SFX/RPG/handleCoins", p != null ? p.transform.position : Vector3.zero, 0.9f, 1f, 0.05f, 0.3f);
        VictoryBanner.ShowSmall($"錢 +{earned} 文", $"持有 {VillageWallet.Coins} 文", GameUI.Accent);
        Refresh();
        string more = stock.Count > 0 ? "還有別的要賣嗎？" : "這些錢拿去村裡逛逛吧——行商那裡有外地的貨，茶屋的糰子也不錯。";
        return index == 0 || n > 1
            ? $"一共 {n} 條，算你 {earned} 文。{more}"
            : $"{last}一條，{earned} 文。{more}";
    }

    public string Back() => null;
}

/// <summary>茶屋老闆娘：甜點（吃了馬上有暫時的增益）和藥草</summary>
public sealed class TeaHouseShop : IDialogueShop
{
    public const int DangoPrice = 30, DaifukuPrice = 20, HerbPrice = 8;
    public const float ShieldRatio = 0.2f, StaminaBonus = 0.3f;

    public string HintLabel => "看看甜點";
    public int Wallet => VillageWallet.Coins;
    public string LeaveLabel => "離開";
    public int Count => 3;

    public string Open() => "要不要來點甜的？剛做好的糰子和大福，吃了身子會輕快些。藥草也有一些，是我上山採的。";

    public ShopEntry Entry(int index)
    {
        int coins = VillageWallet.Coins;
        switch (index)
        {
            case 0: return new ShopEntry($"三色糰子　護盾：先擋下最大生命 {ShieldRatio * 100f:0}% 的傷害", $"{DangoPrice} 文", coins >= DangoPrice);
            case 1: return new ShopEntry($"豆大福　額外精力 {StaminaBonus * 100f:0}%（用完為止）", $"{DaifukuPrice} 文", coins >= DaifukuPrice);
            default: return new ShopEntry($"藥草 ×1　拿去藥鋪換藥水（身上 {VillageWallet.Get(VillageWallet.HerbKey)}）", $"{HerbPrice} 文", coins >= HerbPrice);
        }
    }

    public string Select(int index)
    {
        int price = index == 0 ? DangoPrice : index == 1 ? DaifukuPrice : HerbPrice;
        var hp = PlayerHealth.Find();
        Vector3 at = hp != null ? hp.transform.position : Vector3.zero;
        if (VillageWallet.Coins < price)
        {
            GameAudio.Play("SFX/RPG/metalClick", at, 0.6f, 0.7f, 0f, 0.3f);
            return $"哎呀，錢不太夠呢，還差 {price - VillageWallet.Coins} 文。去棧橋那邊釣幾條魚，拿給岸邊的年輕漁夫換錢吧。";
        }
        switch (index)
        {
            case 0:
                if (hp == null || !SweetShield.Grant(hp.maxHealth * ShieldRatio)) return "糰子的力氣還在你身上呢，吃太多可會撐著的。";
                VillageWallet.Spend(price);
                ShieldFlash.Play(hp.transform, false);
                Gulp(at);
                return "三色糰子，請慢用。……吃完身上暖暖的吧？這股力氣會先幫你擋下一些傷，擋完或倒下就沒了。";
            case 1:
                if (!PlayerStamina.AddBonus(StaminaBonus)) return "大福的力氣還沒用完呢，先去跑一跑再來吧。";
                VillageWallet.Spend(price);
                Gulp(at);
                return "豆大福，紅豆是自家熬的。精力條後面多出來的那一截先用，用完就沒了，不會自己回來喔。";
            default:
                VillageWallet.Spend(price);
                VillageWallet.Add(VillageWallet.HerbKey, 1);
                GameAudio.Play("SFX/RPG/handleSmallLeather", at, 0.8f, 1.1f, 0.05f, 0.3f);
                return $"藥草一株，拿好。湊夠了就拿去藥鋪給藥師換藥水。（身上的藥草：{VillageWallet.Get(VillageWallet.HerbKey)}）";
        }
    }

    static void Gulp(Vector3 at)
    {
        GameAudio.Play("SFX/RPG/handleCoins", at, 0.8f, 1.05f, 0.05f, 0.3f);
        GameAudio.Play("SFX/Gen/drink_gulp_1", at, 0.6f, 1.2f, 0.05f, 0.3f);
    }

    public string Back() => null;
}

/// <summary>行商：外地來的造型（帽子、鞋子、鉤索），先選一類再選商品；買過的可以隨時換回來</summary>
public sealed class CosmeticShop : IDialogueShop
{
    int page = -1; // -1 = 選類別，0 ~ 2 = Wardrobe.Slot
    static readonly Wardrobe.Slot[] Slots = { Wardrobe.Slot.Hat, Wardrobe.Slot.Scarf, Wardrobe.Slot.Hook };

    public string HintLabel => "看看貨";
    public int Wallet => VillageWallet.Coins;
    public string LeaveLabel => page < 0 ? "離開" : "返回";
    public int Count => page < 0 ? Slots.Length : Wardrobe.InSlot(Slots[page]).Count + 1;

    public string Open()
    {
        page = -1;
        return "對了，我這次帶了些外地的好貨——帽子、圍巾，還有給你那條鉤索換的新繩子。用錢買，買過的隨時可以換回來。";
    }

    public ShopEntry Entry(int index)
    {
        if (page < 0)
        {
            var s = Slots[index];
            return new ShopEntry(Wardrobe.SlotName(s), $"現在：{Wardrobe.NameOf(s, Wardrobe.Wearing(s))}", true);
        }
        var slot = Slots[page];
        int wearing = Wardrobe.Wearing(slot);
        if (index == 0) return new ShopEntry(Wardrobe.NoneName(slot), wearing == 0 ? "穿戴中" : "換回來", true);
        var it = Wardrobe.InSlot(slot)[index - 1];
        bool own = Wardrobe.Owns(slot, it.index);
        string detail = wearing == it.index ? "穿戴中" : own ? "已擁有　換上" : $"{it.price} 文";
        return new ShopEntry($"{it.name}　{it.desc}", detail, own || VillageWallet.Coins >= it.price);
    }

    public string Select(int index)
    {
        if (page < 0)
        {
            page = index;
            var s = Slots[page];
            return s == Wardrobe.Slot.Hat ? "帽子在這邊。戴上看看？" : s == Wardrobe.Slot.Scarf ? "圍巾在這邊，都是素面的好布。海風大，脖子可得圍暖一點。" : "鉤索的繩子和鉤爪。換了顏色，甩出去也比較氣派。";
        }
        var slot = Slots[page];
        int wearing = Wardrobe.Wearing(slot);
        var p = PlayerHealth.Find();
        Vector3 at = p != null ? p.transform.position : Vector3.zero;
        if (index == 0)
        {
            if (wearing == 0) return "你現在就是這樣啊。";
            Wardrobe.Wear(slot, 0);
            GameAudio.Play("SFX/RPG/cloth2", at, 0.7f, 1f, 0.05f, 0.3f);
            return slot == Wardrobe.Slot.Hook ? "換回原本的麻繩了。樸素也不錯。" : "換回原本的樣子了。";
        }
        var it = Wardrobe.InSlot(slot)[index - 1];
        if (wearing == it.index) return $"{it.name}已經穿戴在身上囉。";
        if (!Wardrobe.Owns(slot, it.index))
        {
            if (!VillageWallet.Spend(it.price))
            {
                GameAudio.Play("SFX/RPG/metalClick", at, 0.6f, 0.7f, 0f, 0.3f);
                return $"這個要 {it.price} 文，你還差 {it.price - VillageWallet.Coins} 文。聽說棧橋那邊的魚很好釣，岸邊的年輕漁夫會收。";
            }
            Wardrobe.Unlock(slot, it.index);
            GameAudio.Play("SFX/RPG/handleCoins", at, 0.9f, 1f, 0.05f, 0.3f);
            Wardrobe.Wear(slot, it.index);
            return slot == Wardrobe.Slot.Hook ? $"成交！{it.name}，下次甩鉤索的時候就看得到了。" : $"成交！{it.name}，很適合你。";
        }
        Wardrobe.Wear(slot, it.index);
        GameAudio.Play("SFX/RPG/cloth2", at, 0.7f, 1f, 0.05f, 0.3f);
        return $"換上{it.name}了。";
    }

    public string Back()
    {
        if (page < 0) return null;
        page = -1;
        return "還想看看什麼？";
    }
}

/// <summary>
/// 漁村的經濟（不用在場景裡放東西，自動執行）：
///   - 每個場景的玩家都掛上 PlayerCosmetics（造型）
///   - 漁村（JapaneseVillage）：老漁夫旁邊的棧橋末端可以釣魚（FishingSpot），老漁夫教怎麼釣；
///     岸邊補網的年輕漁夫收魚換錢（FishBuyerShop）、茶屋老闆娘賣甜點和藥草（TeaHouseShop）、行商賣造型（CosmeticShop）
/// </summary>
public sealed class VillageEconomy : MonoBehaviour
{
    static VillageEconomy host;
    static bool fishermanTaught;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { host = null; fishermanTaught = false; rackWood = rackRope = rackFish = null; cylinderMesh = sphereMesh = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("VillageEconomy").AddComponent<VillageEconomy>();
        DontDestroyOnLoad(host.gameObject);
    }

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    void Start() => Install(SceneManager.GetActiveScene());

    /// <summary>測試用：第一次進漁村時給 100 文（一個存檔只給一次；改成 0 就不給）</summary>
    public const int StarterCoins = 100;
    const string StarterKey = "CoinStarterGiven";

    /// <summary>等兩幀再給：換場景時 GameManager 會把上一關的背包整個套到新的玩家身上，先給會被蓋掉</summary>
    System.Collections.IEnumerator GiveStarterCoins()
    {
        yield return null;
        yield return null;
        if (StarterCoins <= 0 || VillageWallet.Inventory == null || VillageWallet.Get(StarterKey) > 0) yield break;
        VillageWallet.Set(StarterKey, 1);
        VillageWallet.Add(VillageWallet.CoinKey, StarterCoins);
        Debug.Log($"[VillageEconomy] 測試用：給了 {StarterCoins} 文");
    }
    void OnSceneLoaded(Scene s, LoadSceneMode m) => Install(s);

    void Install(Scene scene)
    {
        var hp = PlayerHealth.Find();
        if (hp != null && hp.GetComponent<PlayerCosmetics>() == null) hp.gameObject.AddComponent<PlayerCosmetics>();
        SweetShield.HookDeath(); // 換了場景：新的玩家倒下時護盾也要消失
        if (scene.name != VillageReturn.VillageScene) return;
        StartCoroutine(GiveStarterCoins());

        MerchantNPC fisherman = null;
        foreach (var npc in FindObjectsByType<MerchantNPC>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (npc.gameObject.scene != scene) continue;
            switch (npc.gameObject.name)
            {
                case "NPC_Fisherman": fisherman = npc; break;
                case "NPC_NetMender": npc.customShop = new FishBuyerShop(); AddFishRacks(npc.transform, scene); break;
                case "NPC_TeaHost": npc.customShop = new TeaHouseShop(); break;
                case "NPC_Merchant": npc.customShop = new CosmeticShop(); break;
            }
        }
        if (fisherman != null) InstallFishing(fisherman);
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if ((t.name == "TeaBench_1" || t.name == "TeaBench_2") && t.GetComponent<BoxCollider>() != null && t.GetComponent<BenchSeat>() == null)
                    t.gameObject.AddComponent<BenchSeat>();
        else Debug.LogWarning("[VillageEconomy] 漁村裡找不到 NPC_Fisherman，沒有釣魚的地方");
    }

    /// <summary>
    /// 收魚的年輕漁夫（沙灘上補網）四周多擺五座魚乾架：跟場景裡原本的魚乾架同一種樣子（兩邊各一對斜撐的木腳、上面橫竹竿），
    /// 掛兩排魚乾；擺在他的左右和身後（離海邊、村民走的海岸路都有距離），貼著沙灘的高度，落點在水裡就不擺。
    /// 原本的魚乾架是靜態合併的網格不能複製，這裡用程式做（每座合成一個網格：木頭、繩子、魚乾三種材質）
    /// </summary>
    static void AddFishRacks(Transform mender, Scene scene)
    {
        var spots = new[]
        {
            (dx: -5.5f, dz: 2.4f, yaw: 20f, w: 3f),
            (dx: -9.5f, dz: 0.4f, yaw: 12f, w: 2.6f),
            (dx: 5f, dz: 1.4f, yaw: -10f, w: 3f),
            (dx: 5.5f, dz: -3.1f, yaw: 6f, w: 2.7f),
            (dx: -4f, dz: 5.9f, yaw: 0f, w: 3.3f),
        };
        var parent = new GameObject("FishRacks_Extra");
        SceneManager.MoveGameObjectToScene(parent, scene);
        int placed = 0, seed = 17;
        foreach (var sp in spots)
        {
            Vector3 p = mender.position + new Vector3(sp.dx, 0f, sp.dz);
            if (!Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y < 0.15f) continue; // 落在水裡
            var rack = BuildFishRack(sp.w, 1.75f, seed++);
            rack.transform.SetParent(parent.transform, false);
            rack.transform.SetPositionAndRotation(hit.point, Quaternion.Euler(0f, sp.yaw, 0f));
            placed++;
        }
        Debug.Log($"[VillageEconomy] 年輕漁夫旁邊多擺了 {placed} 座魚乾架");
    }

    static Material rackWood, rackRope, rackFish;
    static Mesh cylinderMesh, sphereMesh;

    static Material RackMat(Color c, float smooth)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { name = "Runtime_FishRack" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return mesh;
    }

    /// <summary>一座魚乾架（原點 = 地面中央，+X 是橫竿方向）：寬 width、高 height，上下兩排魚乾</summary>
    static GameObject BuildFishRack(float width, float height, int seed)
    {
        rackWood ??= RackMat(new Color(.33f, .23f, .14f), .15f);
        rackRope ??= RackMat(new Color(.62f, .52f, .36f), .05f);
        rackFish ??= RackMat(new Color(.55f, .5f, .42f), .55f); // 曬乾的魚：灰褐色、帶點油光
        if (cylinderMesh == null) cylinderMesh = PrimitiveMesh(PrimitiveType.Cylinder);
        if (sphereMesh == null) sphereMesh = PrimitiveMesh(PrimitiveType.Sphere);
        var wood = new List<CombineInstance>();
        var rope = new List<CombineInstance>();
        var fish = new List<CombineInstance>();
        void Rod(List<CombineInstance> into, Vector3 a, Vector3 b, float thick)
        {
            Vector3 d = b - a;
            into.Add(new CombineInstance
            {
                mesh = cylinderMesh,
                transform = Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d), new Vector3(thick, d.magnitude * 0.5f, thick)),
            });
        }
        // 兩邊各一對斜撐的木腳（下面插進沙裡）、頂上一根橫竿、中間再一根（第二排）
        float hw = width * 0.5f, low = height * 0.58f;
        foreach (float x in new[] { -hw, hw })
        {
            Rod(wood, new Vector3(x, -0.3f, -0.45f), new Vector3(x, height + 0.05f, 0f), 0.085f);
            Rod(wood, new Vector3(x, -0.3f, 0.45f), new Vector3(x, height + 0.05f, 0f), 0.085f);
        }
        Rod(wood, new Vector3(-hw - 0.15f, height, 0f), new Vector3(hw + 0.15f, height, 0f), 0.06f);
        Rod(wood, new Vector3(-hw - 0.1f, low, -0.2f), new Vector3(hw + 0.1f, low, -0.2f), 0.05f);
        Rod(wood, new Vector3(-hw - 0.1f, low, 0.2f), new Vector3(hw + 0.1f, low, 0.2f), 0.05f);
        // 魚乾：上排一條條吊著，下排兩根竿子上各一排（長短、角度隨機一點）
        var rng = new System.Random(seed);
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        void Fish(Vector3 hang, float len, float tilt)
        {
            Vector3 top = hang + Vector3.down * 0.12f;
            Rod(rope, hang, top, 0.012f);
            var rot = Quaternion.Euler(tilt, R(-12f, 12f), R(-6f, 6f));
            fish.Add(new CombineInstance
            {
                mesh = sphereMesh,
                transform = Matrix4x4.TRS(top + rot * (Vector3.down * len * 0.5f), rot, new Vector3(0.085f, len, 0.032f)),
            });
        }
        int n = Mathf.RoundToInt(width / 0.27f);
        for (int i = 0; i < n; i++)
        {
            float x = -hw + 0.2f + (width - 0.4f) * i / Mathf.Max(1, n - 1);
            Fish(new Vector3(x, height, 0f), R(0.32f, 0.44f), R(-4f, 4f));
            if (i % 2 == 0) Fish(new Vector3(x + 0.12f, low, -0.2f), R(0.26f, 0.36f), R(-6f, 6f));
            else Fish(new Vector3(x - 0.05f, low, 0.2f), R(0.26f, 0.36f), R(-6f, 6f));
        }
        var mesh = new Mesh { name = "Runtime_FishRack", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        var parts = new CombineInstance[3];
        for (int k = 0; k < 3; k++)
        {
            var sub = new Mesh();
            sub.CombineMeshes((k == 0 ? wood : k == 1 ? rope : fish).ToArray(), true, true);
            parts[k] = new CombineInstance { mesh = sub, transform = Matrix4x4.identity };
        }
        mesh.CombineMeshes(parts, false, false);
        foreach (var c in parts) Destroy(c.mesh);
        var go = new GameObject("FishRack_Extra");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = new[] { rackWood, rackRope, rackFish };
        return go;
    }

    static void InstallFishing(MerchantNPC fisherman)
    {
        if (FindAnyObjectByType<FishingSpot>() != null) return;
        // 老漁夫坐在棧橋末端、面向海：釣魚的位子在他左手邊（棧橋中間那一側）一點二公尺
        Transform f = fisherman.transform;
        Vector3 fwd = Vector3.ProjectOnPlane(f.forward, Vector3.up).normalized;
        Vector3 spot = f.position + f.right * 1.2f - fwd * 0.35f;
        if (Physics.Raycast(spot + Vector3.up * 2f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) spot.y = hit.point.y;
        var go = new GameObject("FishingSpot");
        go.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(fwd));
        var fs = go.AddComponent<FishingSpot>();
        fs.fisherman = fisherman;

        string[] guide = fisherman.guideLines, repeat = fisherman.repeatLines;
        string key = InteractPrompt.KeyName;
        fisherman.linesProvider = () =>
        {
            if (!fishermanTaught)
            {
                fishermanTaught = true;
                var lines = new List<string>(guide ?? new string[0])
                {
                    "……你也想釣釣看？我旁邊還空著。想釣就跟我說一聲，或是站到我旁邊的魚簍那裡按 " + key + "。",
                    "浮標一沉下去就是魚咬鉤了。這時候照著順序按下出現的數字，手要快、不能按錯，不然魚就跑了。",
                    "越大的魚越難纏，要按的數字越多。釣上來的魚拿去岸邊，給補網的那個年輕人，他照市價收。",
                    "@好，我試試看。",
                };
                return lines.ToArray();
            }
            int fish = FishCatalog.CountAll();
            if (fish > 0) return new[] { $"你的魚簍裡有 {fish} 條了，不錯嘛。", "拿去岸邊給補網的年輕人吧，他會用錢跟你換。" };
            return repeat != null && repeat.Length > 0 ? repeat : new[] { "今天的魚不太咬鉤啊。" };
        };
        // 講完話問要不要一起釣：選了就關掉對話框、走到位子上直接下竿
        fisherman.choiceProvider = () => new DialogueChoice
        {
            question = "怎麼樣，要不要在我旁邊釣一會兒？",
            options = new[] { "一起釣魚", "先不用" },
            onPick = i =>
            {
                if (i == 0) { fs.BeginAfterDialogue(); return null; }
                return new[] { "嗯，想釣的時候再來。魚可不等人喔。" };
            },
        };
    }
}
