using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 日式村莊的主線：祠堂 → 習武場 → 和僧侶過招 → 古畫與詩 → 村長家後院通往道場的門。
/// 1. 跟祠堂裡的師父對話，選擇進入習武場，後門就會打開
/// 2. 打過所有木樁人偶（打一下就會變成木頭人推人）、用拉索盪過深溝碰到所有木條（習武場裡不會被打倒，掉進溝裡回起點）
/// 3. 習武場完成後，僧侶從祠堂後門走進習武場；跟他對話開始過招（MonkSparring），來回 5 回合
/// 4. 看完祠堂地上的四幅古畫和一首詩（順序不限，也可以先看；混在裡面的村民的書不算）
/// 5. 全部完成後跟僧侶對話，選擇開門，村長家後院的門就會變成傳送門
/// 進度只存在這個場景裡，重新載入場景會從頭開始。
/// </summary>
public class ShrineTrial : MonoBehaviour
{
    [Header("人物")]
    public MerchantNPC master;
    public MerchantNPC monk;
    [Tooltip("村長：第一次對話講村子的往事和玩家能做的事，之後依進度提示下一步")]
    public MerchantNPC elder;
    [Tooltip("僧侶的過招（習武場完成後出現）")]
    public MonkSparring monkSpar;

    [Header("機關")]
    public SlidingDoor backDoor;
    [Tooltip("村長家後院通往道場的大門（僧侶開鎖之後要玩家自己推開）")]
    public DojoGate finalGate;
    public TrainingDummy[] dummies;
    public TetherTrainingBeam[] beams;
    public TetherCourse course;
    [Tooltip("要看完的古畫和詩")]
    public SteleInscription[] readings;

    [Tooltip("習武場的範圍（x, z）：玩家在裡面時血量最低留 1，不會被木樁人偶打倒")]
    public Rect yardArea;

    public bool Accepted { get; private set; }
    public bool PortalOpened { get; private set; }

    public int DummiesCleared { get { int n = 0; foreach (var d in dummies) if (d.Cleared) n++; return n; } }
    public int BeamsTouched { get { int n = 0; foreach (var b in beams) if (b.Touched) n++; return n; } }
    public int ReadingsDone { get { int n = 0; foreach (var s in readings) if (s.HasRead) n++; return n; } }

    public bool TrainingDone => DummiesCleared >= dummies.Length && BeamsTouched >= beams.Length;
    public bool SparDone => monkSpar == null || monkSpar.Done;
    public int SparRounds => monkSpar != null ? monkSpar.Rounds : 0;
    public int SparTotal => monkSpar != null ? monkSpar.roundsRequired : 0;
    public bool ReadingDone => ReadingsDone >= readings.Length;
    public bool AllDone => TrainingDone && SparDone && ReadingDone;

    PlayerHealth player;
    bool metMaster, metElder, elderJustTold, protectionOn, announcedTraining, announcedReading, announcedSpar, announcedAll, sparPending;
    GUIStyle titleStyle, lineStyle, hintStyle;

    void Start()
    {
        player = PlayerHealth.Find();
        master.linesProvider = MasterLines;
        master.choiceProvider = MasterChoice;
        monk.linesProvider = MonkLines;
        monk.choiceProvider = MonkChoice;
        if (elder != null)
        {
            elder.linesProvider = ElderLines;
            elder.choiceProvider = ElderChoice;
        }
        if (course != null) course.markersAllowed = () => Accepted;

        // 隱形牆放在 Ignore Raycast 層。鏡頭退到圍牆外時，拉索的瞄準線不能先打到牆，否則會鎖到空氣
        var tether = player != null ? player.GetComponent<EnergyTetherSkill>() : null;
        if (tether != null) tether.aimMask &= ~(1 << 2);
    }

    void OnDisable()
    {
        if (player != null) player.SetPracticeProtection(this, false);
    }

    void Update()
    {
        if (player == null) return;

        // 習武場裡不會被打倒（離開就恢復）
        var p = player.transform.position;
        bool inside = yardArea.Contains(new Vector2(p.x, p.z));
        if (inside != protectionOn)
        {
            protectionOn = inside;
            player.SetPracticeProtection(this, inside);
        }

        if (TrainingDone && !announcedTraining)
        {
            announcedTraining = true;
            if (monkSpar != null) monkSpar.Appear(); // 僧侶從祠堂後門走進習武場
            Announce();
        }
        // 對話選了「過招」：等對話框關掉再開始
        if (sparPending && !monk.InConversation)
        {
            sparPending = false;
            monkSpar.BeginSpar();
        }
        if (monkSpar != null && monkSpar.Done && !announcedSpar)
        {
            announcedSpar = true;
            Announce();
        }
        if (ReadingDone && !announcedReading)
        {
            announcedReading = true;
            Announce();
        }
        if (AllDone && !announcedAll)
        {
            announcedAll = true;
            Announce();
        }
    }

    /// <summary>完成一個階段：只播音效（不在畫面中間跳大字），進度看左邊的面板</summary>
    void Announce()
    {
        GameAudio.Play("SFX/RPG/metalLatch", Vector3.zero, 0.8f, 0.8f, 0f, 0f);
    }

    /// <summary>還差什麼（對話裡用）</summary>
    string Remaining()
    {
        var parts = new List<string>();
        if (DummiesCleared < dummies.Length) parts.Add($"木樁人偶還差 {dummies.Length - DummiesCleared} 個");
        if (BeamsTouched < beams.Length) parts.Add($"懸空木條還差 {beams.Length - BeamsTouched} 根");
        if (!SparDone) parts.Add("還沒跟僧侶過招");
        if (!ReadingDone) parts.Add($"古畫和詩還有 {readings.Length - ReadingsDone} 份沒看");
        return string.Join("、", parts);
    }

    // ---------------- 村長 ----------------

    /// <summary>村子的往事（玄清道人的故事）</summary>
    static readonly string[] ElderStory =
    {
        "四十年前，村子外海住著一條海龍。每到秋天就掀起大浪，出海的船一艘接一艘沉下去，沒有人敢再出港。",
        "那年來了一位雲遊的道人，叫做玄清。他一個人站上浪頭，跟海龍鬥了一整夜，一劍把牠逼回了深海。",
        "村人擺酒謝他，他喝得大醉，笑說這裡的魚好吃、酒也好喝，乾脆在此終老。從那以後，村子過了好多年太平日子。",
        "直到有一夜，後山的地裂開了，一條百足巨蟲破土而出。道人跟牠鬥到天快亮，終究……還是倒下了。",
        "更怪的是，天還沒亮，一群穿黑衣的人趁亂闖進村子，抬走了道人的遺體，往東邊去了。從此再也沒有人見過他們。",
        "我們在山上蓋了祠堂供奉道人。他留下的四幅畫和一首詩，就收在祠堂裡；想知道得更清楚，就上去看看吧。",
    };

    /// <summary>玩家在村裡能做的事</summary>
    string[] ElderTodo() => new[]
    {
        "你在村裡能做的事不少：主街上的行商收草藥，後山竹林石階兩旁長著草藥，靠近就能採，拿去跟他換藥水。",
        "沿著主街往北，穿過鳥居、爬上石階，就是山上的祠堂。守祠的師父會帶你進後山的習武場——用拉索盪過深溝，跟會動的木樁人偶過招。",
        "石階上那位僧侶，身手可不簡單。等你把習武場走完，他自然會來試試你。",
        "還有我這宅院後面那扇老門，從我爺爺那一代就鎖著了，鑰匙在僧侶身上。聽說門的另一邊……就是那群黑衣人的道場。",
    };

    string[] ElderLines()
    {
        if (!metElder)
        {
            metElder = true;
            elderJustTold = true; // 這次對話已經講完整段往事，結尾不用再問要不要重聽
            var lines = new List<string> { "歡迎來到寒舍，我是這座漁村的村長。看你的樣子，是從海的那邊來的吧？先坐，聽我這老頭子說說村子的往事。" };
            lines.AddRange(ElderStory);
            lines.AddRange(ElderTodo());
            lines.Add("想再聽一次往事，隨時來找我。");
            return lines.ToArray();
        }
        // 之後：依進度告訴玩家下一步
        if (PortalOpened)
            return new[] { "後院的門鎖已經開了。門很重，用力推開它。", "……替我們把道人帶回家吧。" };
        if (AllDone)
            return new[] { "畫看完了、僧侶也認可你了？那就去習武場找他，請他打開我家後院的那扇門。" };
        if (!Accepted)
            return new[]
            {
                "還沒上山的話，就沿著主街往北，穿過鳥居爬上石階，去祠堂找守祠的師父。",
                "路上累了，就採點草藥去跟行商換藥水。",
            };
        if (!TrainingDone)
        {
            var left = new List<string>();
            if (DummiesCleared < dummies.Length) left.Add($"木樁人偶還差 {dummies.Length - DummiesCleared} 個");
            if (BeamsTouched < beams.Length) left.Add($"懸空木條還差 {beams.Length - BeamsTouched} 根");
            return new[] { $"習武場練得怎麼樣了？{string.Join("、", left)}。", "掉進溝裡也別灰心，道人當年也是這樣練過來的。" };
        }
        if (!SparDone)
            return new[] { "僧侶在習武場等你過招。他會輕功，還會點穴，被點中了可是一陣子動不了，小心點。" };
        return new[] { $"祠堂裡道人的畫和詩，還有 {readings.Length - ReadingsDone} 份沒看。看完再去找僧侶吧。" };
    }

    DialogueChoice ElderChoice()
    {
        // MerchantNPC 先拿台詞再拿選項：第一次對話講完往事就不問了
        if (!metElder || elderJustTold) { elderJustTold = false; return null; }
        return new DialogueChoice
        {
            question = "要再聽一次村子的往事嗎？",
            options = new[] { "請村長再說一次", "不用了" },
            onPick = i => i == 0 ? ElderStory : new[] { "好，有事再來找我。" },
        };
    }

    // ---------------- 祠堂的師父 ----------------

    string[] MasterLines()
    {
        if (!Accepted)
        {
            if (metMaster) return new[] { "想好了嗎？" };
            metMaster = true;
            return new[]
            {
                "這座祠堂供奉的是玄清道人。地上散著他留下的四幅古畫和一首詩，混在村民寄放的書裡，你可以撿起來看看。",
                "我身後的門通往後山的習武場，是道人當年親手開闢的：東邊有一道深溝，只能靠拉索抓著懸空的木條盪過去；西邊的木樁人偶，打一下就會活過來跟你過招。",
            };
        }
        if (AllDone)
            return new[]
            {
                "木人、橫木、古畫，你都看過了，連僧侶都認可了你。",
                "去找僧侶吧，他就在習武場裡。村長家後院那扇門通往黑衣人的道場，鑰匙在他那裡。",
            };
        if (TrainingDone && !SparDone)
            return new[] { "僧侶在習武場裡等你。他可不像木頭人那樣只會推人，小心點。" };
        return new[] { $"{Remaining()}。別急，慢慢來。" };
    }

    DialogueChoice MasterChoice()
    {
        if (Accepted) return null;
        return new DialogueChoice
        {
            question = "要進入習武場嗎？",
            options = new[] { "進入習武場", "先不用" },
            onPick = i =>
            {
                if (i != 0) return new[] { "也好。想好了再來找我。" };
                Accepted = true;
                backDoor.Open();
                return new[]
                {
                    $"後門開了。練拉索：按 {InputBindings.DisplayName(GameAction.Tether)} 瞄準，點左鍵鎖定木條，再把滾輪往上滾，就能把自己拉過去。掉進溝裡就從起點重來。",
                    "木頭人會用雙手推你，看到它把雙手往後收，就是要推了，趕快舉盾或翻滾。在院子裡你不會被打倒，儘管放手練。",
                };
            },
        };
    }

    // ---------------- 石階上的僧侶 ----------------

    string[] MonkLines()
    {
        if (PortalOpened) return new[] { "門鎖已經解開了，就在村長家的後院，推開它就能過去。此去保重，阿彌陀佛。" };
        if (monkSpar != null && monkSpar.Appeared && !monkSpar.Done)
            return new[]
            {
                "木人、橫木都難不倒你。不過木頭人不會還手——讓貧僧親自試試你的身手。",
                "看到我抬手就舉盾或翻滾，等我出完招露出空檔再還手。沒有空檔就打過來，我可是會擋的。",
                "還有，小心我的點穴：被點中了，可是會有一陣子動彈不得。",
            };
        if (monkSpar != null && monkSpar.Done && !ReadingDone)
            return new[]
            {
                "好身手。不過光有身手還不夠，你得知道自己要面對的是什麼。",
                $"祠堂地上的古畫和詩還有 {readings.Length - ReadingsDone} 份沒看，看完再來找我。",
            };
        if (AllDone)
            return new[]
            {
                "身手過了貧僧這關，畫和詩也都看過了。你知道道人最後怎麼了吧。",
                "那群黑衣人把道人的遺骨帶進了他們的道場。村長家後院有一扇老門通往那裡，鑰匙一直在我身上。",
            };
        if (!Accepted && ReadingsDone == 0)
            return new[]
            {
                "石階一共九十多階，每走一階，心就靜一分。",
                "山頂的祠堂供奉著玄清道人，裡面的師父會告訴你該做什麼。",
            };
        return new[] { $"村長家後院那扇門由我看管，不過現在還不能為你打開：{Remaining()}。" };
    }

    DialogueChoice MonkChoice()
    {
        if (monkSpar != null && monkSpar.Arrived && !monkSpar.Done && !monkSpar.Fighting)
            return new DialogueChoice
            {
                question = "要跟僧侶過招嗎？",
                options = new[] { "請大師指教", "再等等" },
                onPick = i =>
                {
                    if (i != 0) return new[] { "不急，準備好了再來。" };
                    sparPending = true;
                    return new[] { $"好，來回 {monkSpar.roundsRequired} 招。出招吧！" };
                },
            };
        if (!AllDone || PortalOpened) return null;
        return new DialogueChoice
        {
            question = "要打開村長家後院通往道場的門嗎？",
            options = new[] { "請大師開門", "再等等" },
            onPick = i =>
            {
                if (i != 0) return new[] { "準備好了再來，貧僧就在這裡等你。" };
                PortalOpened = true;
                finalGate.Unlock();
                return new[] { "門鎖已經解開了，就在村長家的後院。門很重，用力推開它，去把道人的遺骨帶回來吧。阿彌陀佛。" };
            },
        };
    }

    // ---------------- 進度 ----------------

    void OnGUI()
    {
        if (GameUI.OverlayOpen) return;
        if (!Accepted && ReadingsDone == 0) return;

        // 小小的一塊：標題、每項一行、最下面一行提示
        titleStyle ??= new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold, fontSize = 14 };
        lineStyle ??= new GUIStyle(GameUI.Small) { fontSize = 13 };
        hintStyle ??= new GUIStyle(GameUI.Small) { fontSize = 12, wordWrap = true };

        var box = new Rect(16f, 150f, 230f, PortalOpened ? 50f : 132f);
        GameUI.Panel(box);
        GUI.color = GameUI.Accent;
        GUI.Label(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 20f), "祠堂的試煉", titleStyle);

        if (PortalOpened)
        {
            GUI.color = GameUI.TextColor;
            GUI.Label(new Rect(box.x + 10f, box.y + 25f, box.width - 20f, 20f), "推開村長家後院的大門", lineStyle);
            GUI.color = Color.white;
            return;
        }

        float y = box.y + 25f;
        void Line(string text, int done, int total)
        {
            GUI.color = done >= total ? GameUI.Accent : GameUI.TextColor;
            GUI.Label(new Rect(box.x + 10f, y, box.width - 20f, 18f), $"{text}　{done} / {total}", lineStyle);
            y += 17f;
        }
        Line("木樁人偶", DummiesCleared, dummies.Length);
        Line("懸空木條", BeamsTouched, beams.Length);
        if (monkSpar != null) Line("與僧侶過招", SparRounds, SparTotal);
        Line("古畫與詩", ReadingsDone, readings.Length);

        string hint = AllDone ? "跟習武場裡的僧侶對話"
            : !Accepted ? "跟祠堂裡的師父對話，才能進入習武場"
            : monkSpar != null && monkSpar.Fighting ? "僧侶抬手就舉盾或翻滾，等他出完招的空檔再還手"
            : TrainingDone && !SparDone ? "僧侶在習武場裡等你，跟他對話開始過招"
            : TrainingDone ? "看完祠堂地上的古畫和詩"
            : $"拉索：{InputBindings.DisplayName(GameAction.Tether)} 瞄準 → 左鍵鎖定 → 滾輪往上，一路盪過深溝";
        GUI.color = AllDone ? GameUI.Accent : GameUI.MutedColor;
        GUI.Label(new Rect(box.x + 10f, y + 1f, box.width - 20f, 34f), hint, hintStyle);
        GUI.color = Color.white;
    }
}
