using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 村子這條主線的進度，跨場景保留（只存在記憶體）：從道場被抬回村子時，村長家後院的大門不用重新解鎖。
/// </summary>
public static class VillageProgress
{
    /// <summary>村長家後院通往道場的大門已經解鎖（僧侶開鎖，或女書生偷偷打開）</summary>
    public static bool GateUnlocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => GateUnlocked = false;
}

/// <summary>
/// 倒下之後被抬回村子（不用在場景裡放東西，自動執行）：
///   - 在道場、霧海孤島被打倒：畫面暗下來，「村民把你抬回了村子」，載入漁村，在暫居地（Residence）醒來
///   - 在村子裡倒下：照原本的重生流程，在暫居地醒來
///   - 這條主線不會 Game Over：每次倒下都把命補滿（GameManager.RestoreLives）
/// 醒來後米格爾自言自語一句，村長、僧侶頭上出現指引標記。之後跟村長、僧侶、守祠的師父、女鐵匠、藥師、行商、
/// 小孩、茶屋老闆娘、女書生說話時，他們會先安慰、鼓勵他繼續努力（每個人每次倒下講一次，講完恢復平常的對話）。
/// 第一次倒下和之後再倒下講的話不一樣；村長會提到上一次輸給誰（CombatLog 記下的對手）。
/// </summary>
public class VillageReturn : MonoBehaviour
{
    public const string VillageScene = "JapaneseVillage";
    static readonly string[] CarryBackFrom = { "DojoArena_ArchitecturePreview", "ForestIsland" };

    static VillageReturn host;
    /// <summary>這次開遊戲以來倒下幾次（村子這條主線）</summary>
    public static int Defeats { get; private set; }
    static bool wakePending, comfortPending;
    static readonly HashSet<string> comforted = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        host = null;
        Defeats = 0;
        wakePending = comfortPending = false;
        comforted.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        MerchantNPC.LinesOverride = ComfortFor;
        if (host != null) return;
        host = new GameObject("VillageReturn").AddComponent<VillageReturn>();
        DontDestroyOnLoad(host.gameObject);
    }

    static bool InStoryScene(string scene) => scene == VillageScene || System.Array.IndexOf(CarryBackFrom, scene) >= 0;

    PlayerHealth player;
    float overlay, overlayTarget;
    string overlayTitle, overlaySub;
    bool carrying;

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
    void Start() => Hook();

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        Hook();
        if (s.name == VillageScene && wakePending) StartCoroutine(WakeInVillage());
    }

    void Hook()
    {
        if (player != null) { player.OnDied -= OnPlayerDied; player.OnRespawned -= OnPlayerRespawned; }
        player = PlayerHealth.Find();
        if (player == null) return;
        player.OnDied += OnPlayerDied;
        player.OnRespawned += OnPlayerRespawned;
    }

    void OnPlayerDied()
    {
        string scene = SceneManager.GetActiveScene().name;
        if (!InStoryScene(scene)) return;
        Defeats++;
        comfortPending = true;
        comforted.Clear();
        // PlayerHealth 在 OnDied 之後才扣命：先補滿，扣完還有命，就不會 Game Over
        GameManager.Instance?.RestoreLives();
        if (scene == VillageScene) return; // 村子裡：PlayerHealth 自己會在暫居地（respawnPoint）重生
        if (!Application.CanStreamedLevelBeLoaded(VillageScene))
        {
            Debug.LogWarning($"[VillageReturn] {VillageScene} 不在 Build Settings 裡，沒辦法抬回村子，照原本的方式在這裡重生");
            return;
        }
        // 不要在原地重生（PlayerHealth 在 OnDied 之後才讀 respawnDelay）：載入村子之前都不會復活
        player.respawnDelay = 60f;
        StartCoroutine(CarryBack());
    }

    void OnPlayerRespawned()
    {
        // 村子裡倒下、在暫居地醒來
        if (SceneManager.GetActiveScene().name == VillageScene && comfortPending && !carrying) StartCoroutine(AfterWake(false));
    }

    IEnumerator CarryBack()
    {
        carrying = true;
        wakePending = true;
        yield return new WaitForSecondsRealtime(1.3f);
        overlayTitle = "你倒下了……";
        overlaySub = "村民把你抬回了村子";
        overlayTarget = 1f;
        while (overlay < 0.999f) yield return null;
        yield return new WaitForSecondsRealtime(0.6f);
        var gm = GameManager.Instance;
        if (gm != null) gm.LoadLevel(VillageScene);
        else SceneManager.LoadScene(VillageScene);
    }

    IEnumerator WakeInVillage()
    {
        wakePending = false;
        overlay = overlayTarget = 1f;
        yield return null;
        yield return null; // 等新場景的 Start 跑完（暫居地設好 wakePoint）
        var p = PlayerHealth.Find();
        var home = FindFirstObjectByType<Residence>();
        if (p != null && home != null && home.wakePoint != null) Residence.MovePlayer(p, home.wakePoint);
        GameManager.Instance?.RestoreLives();
        yield return new WaitForSecondsRealtime(0.9f);
        while (GameManager.Instance != null && GameManager.Instance.IsLoading) yield return null;
        overlayTitle = overlaySub = null;
        overlayTarget = 0f;
        while (overlay > 0.05f) yield return null;
        carrying = false;
        yield return AfterWake(true);
    }

    /// <summary>醒來：米格爾自言自語，村長、僧侶頭上出現指引標記</summary>
    IEnumerator AfterWake(bool carried)
    {
        yield return new WaitForSecondsRealtime(0.4f);
        var p = PlayerHealth.Find();
        if (p == null) yield break;
        string[] lines = Defeats <= 1
            ? new[] { "……這裡是，暫居地？", "是村民把我抬回來的吧。……去跟村長他們說一聲。" }
            : new[] { "……又被抬回來了。", "再來一次。這次一定要贏。" };
        var story = new List<StoryDialogue.Line>();
        foreach (var l in lines) story.Add(StoryDialogue.L("米格爾", p.transform, false, l, PlanetStory.PlayerColor));
        StoryDialogue.Play(story);
        foreach (var npc in FindObjectsByType<MerchantNPC>(FindObjectsSortMode.None))
            if (npc.speakerName == "村長" || npc.speakerName == "僧侶") npc.ShowGuideMarker();
    }

    void Update()
    {
        overlay = Mathf.MoveTowards(overlay, overlayTarget, Time.unscaledDeltaTime * 1.6f);
    }

    GUIStyle titleStyle, subStyle;

    void OnGUI()
    {
        if (overlay <= 0.001f) return;
        GUI.depth = -1300; // 蓋在 GameManager 的淡入淡出（-1000）、死亡畫面上面
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, overlay);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        if (!string.IsNullOrEmpty(overlayTitle))
        {
            titleStyle ??= new GUIStyle(GameUI.Title) { fontSize = 40 };
            subStyle ??= new GUIStyle(GameUI.Center) { fontSize = 24, fontStyle = FontStyle.Normal };
            float a = Mathf.Clamp01((overlay - 0.5f) * 2f);
            GameUI.Shadowed(new Rect(0f, Screen.height * 0.42f, Screen.width, 50f), overlayTitle, titleStyle, new Color(1f, 0.85f, 0.8f, a));
            GameUI.Shadowed(new Rect(0f, Screen.height * 0.42f + 56f, Screen.width, 34f), overlaySub, subStyle, new Color(0.85f, 0.82f, 0.75f, a));
        }
        GUI.color = prev;
    }

    // ---------------- 村民的鼓勵 ----------------

    // {opp} = 上一次輸給的對手。@ 開頭 = 米格爾說的。first = 第一次倒下，again = 之後再倒下
    static readonly Dictionary<string, (string[] first, string[] again)> Comfort = new Dictionary<string, (string[], string[])>
    {
        ["村長"] = (new[]
        {
            "醒啦？村裡的人發現你倒在地上，七手八腳把你抬了回來。",
            "輸給{opp}不丟人。玄清道人當年，也不是一夜之間就成了能斬龍的人。",
            "@我還差得遠。",
            "差得遠，就一步一步走近。吃飽、睡好，再去一次。",
        }, new[]
        {
            "又被抬回來啦。……別低頭，年輕人。",
            "每倒下一次，就多看清楚對手一分。下一次，一定能走得更遠。",
            "@我會再去的。",
        }),
        ["僧侶"] = (new[]
        {
            "阿彌陀佛。施主身上的傷，貧僧看得出來，是一場硬仗。",
            "勝負只在一念之間。心不亂，手就不亂。再試一次吧。",
            "@多謝大師。",
        }, new[]
        {
            "跌倒的次數，比不上站起來的次數。去吧，貧僧等你的好消息。",
            "@這次一定不讓大師失望。",
        }),
        ["守祠的師父"] = (new[]
        {
            "聽說你被抬回來了。習武場的門一直開著，想練就上來。",
            "把木人的推掌看熟，對上真正的敵人時，身體自然就會動。",
        }, new[]
        {
            "又輸了？那就再練。道人留下的習武場，就是為了這種時候。",
        }),
        ["女鐵匠"] = (new[]
        {
            "被打回來了？先讓我看看你的兵器……刃口還好。人沒事就好。",
            "打不贏，就換個打法。想換兵器，架子上隨你挑。",
            "@謝了，我再想想。",
        }, new[]
        {
            "又來了？兵器我幫你磨好了，這次別再讓它沾地。",
        }),
        ["藥師"] = (new[]
        {
            "哎呀，傷得不輕。下次藥水記得帶夠，別逞強。",
            "草藥拿來，我隨時幫你煎。活著回來，比什麼都重要。",
        }, new[]
        {
            "又是你……傷口我看了，沒有大礙。去吧，別忘了帶藥。",
        }),
        ["行商"] = (new[]
        {
            "旅人，聽說你吃了敗仗？做生意也一樣，虧了一次，下次就知道怎麼賺回來。",
        }, new[]
        {
            "又見面了，旅人。輸幾次不要緊，只要人還在，就還有翻本的機會。",
        }),
        ["村裡的小孩"] = (new[]
        {
            "你回來了！大家說你被壞人打倒了……你下次一定會贏的，對不對？",
            "@對，下次一定贏。",
        }, new[]
        {
            "你又被抬回來了……不過我知道，你一定會再站起來的！",
        }),
        ["茶屋老闆娘"] = (new[] { "先喝杯熱茶，暖暖身子。人回來了就好。" }, new[] { "茶我一直溫著呢。慢慢來，不急。" }),
        ["女書生"] = (new[]
        {
            "我把你的事記下來了：倒下又站起來的人，才寫得進傳說裡。",
        }, new[]
        {
            "這一頁我又多寫了一行。故事還沒寫完，主角可不能放棄。",
        }),
    };

    /// <summary>MerchantNPC 開始對話時問：這個人有沒有要先講的鼓勵（沒有 = null，照平常的對話）</summary>
    static string[] ComfortFor(MerchantNPC npc)
    {
        if (!comfortPending || npc == null || SceneManager.GetActiveScene().name != VillageScene) return null;
        if (comforted.Contains(npc.speakerName) || !Comfort.TryGetValue(npc.speakerName, out var set)) return null;
        comforted.Add(npc.speakerName);
        var src = Defeats <= 1 ? set.first : set.again;
        var lines = new string[src.Length];
        string opp = CombatLog.LastDefeatOpponent;
        for (int i = 0; i < src.Length; i++) lines[i] = src[i].Replace("{opp}", opp);
        return lines;
    }

    /// <summary>所有鼓勵的台詞（配音表用）</summary>
    public static IEnumerable<string> AllComfortLines()
    {
        foreach (var kv in Comfort)
        {
            foreach (var l in kv.Value.first) yield return l;
            foreach (var l in kv.Value.again) yield return l;
        }
    }
}
