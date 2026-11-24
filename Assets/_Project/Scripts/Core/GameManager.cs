using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 整個遊戲的關卡流程（跨場景存在，不會因為換場景被刪掉）：
///   - 主選單（MainMenu）按「開始遊戲」→ StartStoryGame()，從漁村開始（StartNewGame() 從第一關沙漠開始，Game Over / 通關的重新開始用）
///   - 記住關卡順序：星球 →（傳送門）→ 維京村莊 →（搭船）→ 地下城（Boss，最後一關）
///   - 另一條主線（storyLevels）：漁村 ↔ 道場。跟上面一樣可以暫停（存檔 / 讀檔 / 設定 / 重新開始本關）、
///     換場景時帶著玩家的狀態（血量、背包、錢、造型）、進場景時自動存檔；但不算進「下一關」的順序、沒有關卡標題和轉場動畫
///   - 傳送門 / 船抵達終點時呼叫 LoadNextLevel() 或 LoadLevel("場景名")
///   - 換場景時畫面淡出淡入，進入新關卡會顯示關卡名稱
///   - 換到下一關時玩家的血量、能力升級、背包會帶過去（PlayerState）；重新開始本關會回到進關時的狀態
///   - 存檔（SaveSystem）：進入每一關自動存檔，暫停選單可以手動存檔 / 讀檔，主選單可以「繼續遊戲」
///   - 玩家有 maxLives 條命：倒下時扣一條命並在這一關最近碰過的檢查點復活；命用完 → Game Over 畫面幾秒後自動從第一關起點重來（命和時間重算）
///   - 打倒最後一關的 Boss（或在最後一關呼叫 LoadNextLevel）→ 通關畫面
///   - 通關畫面有「重新開始」「回主選單」按鈕；遊戲中按 Esc 開暫停選單
///
/// 不用手動放進場景：按 Play 時會自動建立。想改關卡順序或標題，
/// 可以在任一場景放一個掛著 GameManager 的物件，那個物件的設定會優先使用。
/// </summary>
public class GameManager : MonoBehaviour
{
    [System.Serializable]
    public class Level
    {
        [Tooltip("場景名稱（要加進 Build Settings，選單 GeminiGrowUp > 設定傳送門與關卡順序 會自動加）")]
        public string sceneName;
        [Tooltip("進入關卡時顯示的標題")]
        public string title;
        [Tooltip("這一關的背景音樂（Resources/Audio 底下的路徑，例如 Music/bgm_planet）。Boss 戰會換成 Boss 的音樂")]
        public string music;
    }

    public static GameManager Instance { get; private set; }

    [Tooltip("關卡順序，傳送門的「下一關」就是照這個順序")]
    public Level[] levels =
    {
        new Level { sceneName = "Planet",             title = "第一關　沙漠",     music = "Music/bgm_planet" },
        new Level { sceneName = "The_Viking_Village", title = "第二關　維京村莊", music = "Music/bgm_viking" },
        new Level { sceneName = "Floor02",            title = "第三關　地下城",   music = "Music/bgm_dungeon" },
    };

    [Tooltip("漁村這條主線的場景（不在上面的順序裡）：一樣可以暫停、存讀檔，換場景時帶著玩家的狀態")]
    public Level[] storyLevels =
    {
        new Level { sceneName = "JapaneseVillage",               title = "漁村" },
        new Level { sceneName = "DojoArena_ArchitecturePreview", title = "道場" },
    };

    [Header("背景音樂")]
    [Tooltip("主選單的背景音樂（Resources/Audio 底下的路徑）")]
    public string mainMenuMusic = "Music/bgm_menu";
    [Range(0f, 1f)] public float musicVolume = 0.35f;
    public float musicFadeIn = 1.5f;

    [Header("轉場")]
    public float fadeOutTime = 0.8f;
    public float fadeInTime = 0.8f;
    public Color fadeColor = Color.black;
    [Tooltip("往下一關前進時（第一關 → 第二關、第二關 → 第三關）播放轉場動畫，最少播幾秒")]
    public float transitionMinTime = 3.2f;
    LevelTransition transition;
    public bool showLevelTitle = true;

    [Header("主選單")]
    [Tooltip("主選單場景名稱（要放在 Build Settings 的第一個）")]
    public string mainMenuScene = "MainMenu";

    [Header("生命")]
    [Tooltip("玩家總共有幾條命，用完就 Game Over")]
    public int maxLives = 3;
    [Tooltip("命用完後 GAME OVER 畫面停留幾秒，然後自動從第一關起點重來")]
    public float gameOverRestartDelay = 3f;

    [Header("全破")]
    public string gameClearTitle = "ALL CLEAR";
    public string gameClearSubtitle = "感謝遊玩";

    [Header("存檔")]
    [Tooltip("進入每一關時自動存檔")]
    public bool autoSaveOnLevelStart = true;

    /// <summary>正在換場景（傳送門用來避免重複觸發）</summary>
    public bool IsLoading { get; private set; }
    /// <summary>換場景的載入進度 0~1（LoadSceneAsync 的 progress / 0.9）。主畫面「開始遊戲」的進度條（StoryLoadingScreen）用</summary>
    public float LoadProgress { get; private set; }
    /// <summary>
    /// 不是 null 時：下一次一般的換場景（不是傳送門轉場）先在背景載好，等它回傳 true 才切換到新場景，用完就清掉。
    /// 主畫面的進度條用它讓「開始遊戲」的進度條跑完才進漁村
    /// </summary>
    public System.Func<bool> HoldSceneActivation;
    /// <summary>目前場景在關卡順序中的位置，不在清單裡就是 -1</summary>
    public int CurrentLevelIndex => IndexOf(SceneManager.GetActiveScene().name);
    public Level CurrentLevel => CurrentLevelIndex >= 0 ? levels[CurrentLevelIndex] : null;
    /// <summary>目前場景是可以玩的關卡（上面的關卡順序，或漁村這條主線）：可以暫停、存讀檔</summary>
    public bool InPlayableLevel => FindLevel(SceneManager.GetActiveScene().name) != null;

    /// <summary>在兩條路線裡找這個場景（關卡順序先找，再找漁村主線）；都沒有回傳 null</summary>
    public Level FindLevel(string sceneName)
    {
        int i = IndexOf(sceneName);
        if (i >= 0) return levels[i];
        if (storyLevels != null)
            foreach (var l in storyLevels)
                if (l != null && l.sceneName == sceneName) return l;
        return null;
    }

    /// <summary>剩下幾條命（包含現在這條）</summary>
    public int Lives { get; private set; }
    /// <summary>這一輪遊戲經過的時間（暫停不算）</summary>
    public float PlayTime { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsGameOver { get; private set; }
    public bool IsGameWon { get; private set; }
    /// <summary>暫停 / Game Over / 通關畫面開著</summary>
    public bool IsOverlayOpen => overlay != null;
    public bool InMainMenu => SceneManager.GetActiveScene().name == mainMenuScene;

    /// <summary>新關卡載入完成（參數是場景名稱）</summary>
    public event System.Action<string> OnLevelLoaded;

    float fadeAlpha;
    /// <summary>進入目前這一關時玩家的狀態（換關卡時帶過去、重新開始本關時還原）</summary>
    PlayerState levelEntryState;
    Canvas overlay;
    float overlayPrevTimeScale = 1f;
    ThirdPersonCamera disabledCamera;
    Coroutine pendingEnding;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Instance == null) new GameObject("GameManager").AddComponent<GameManager>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
        Lives = maxLives;
    }

    void Start()
    {
        // 直接從某一關按 Play 也顯示關卡標題、播音樂，並記下進關時的狀態（重新開始本關用）
        ShowTitle();
        PlaySceneMusic();
        if (InPlayableLevel) levelEntryState = PlayerState.Capture(PlayerState.FindPlayer());
        if (SceneManager.GetActiveScene().name == StoryStartScene) storyStartState = levelEntryState;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public int IndexOf(string sceneName)
    {
        for (int i = 0; i < levels.Length; i++)
            if (levels[i].sceneName == sceneName) return i;
        return -1;
    }

    // ================= 遊戲流程：開始 / 重新開始 / 主選單 =================

    /// <summary>開新遊戲（主選單的「開始遊戲」、Game Over / 通關的「重新開始」）：命和時間重設，從第一關開始</summary>
    public void StartNewGame()
    {
        if (IsLoading || levels.Length == 0) return;
        Lives = maxLives;
        PlayTime = 0f;
        levelEntryState = null; // 新遊戲：用場景裡玩家原本的數值
        LoadScene(levels[0].sceneName, false);
    }

    // ================= 存檔 / 讀檔 =================

    /// <summary>存檔：目前這一關 + 命 + 時間 + 玩家現在的狀態</summary>
    public bool SaveGame()
    {
        var level = FindLevel(SceneManager.GetActiveScene().name);
        if (level == null) return false;
        var state = PlayerState.Capture(PlayerState.FindPlayer()) ?? levelEntryState;
        return SaveSystem.Save(new SaveData
        {
            sceneName = level.sceneName,
            lives = Mathf.Max(1, Lives),
            playTime = PlayTime,
            player = state,
        });
    }

    /// <summary>讀檔：回到存檔那一關的起點，帶著存檔時的命、時間和玩家狀態</summary>
    public bool LoadSavedGame()
    {
        if (IsLoading) return false;
        var data = SaveSystem.Load();
        if (data == null || FindLevel(data.sceneName) == null) return false;
        Lives = Mathf.Max(1, data.lives);
        PlayTime = data.playTime;
        levelEntryState = data.player;
        LoadScene(data.sceneName, false);
        return true;
    }

    /// <summary>給主選單顯示用：「第二關　維京村莊　2026-10-17 20:30」；沒有存檔回傳 null</summary>
    public string DescribeSave()
    {
        var data = SaveSystem.Load();
        if (data == null) return null;
        var level = FindLevel(data.sceneName);
        string title = level != null ? level.title : data.sceneName;
        return $"{title}　{data.savedAt}";
    }

    /// <summary>Game Over 後重新挑戰目前這一關（命補滿）</summary>
    public void RetryLevel()
    {
        if (IsLoading) return;
        Lives = maxLives;
        ReloadLevel();
    }

    /// <summary>命補滿（不換場景）。漁村 → 道場這條主線倒下時用，不會 Game Over（VillageReturn）</summary>
    public void RestoreLives() => Lives = maxLives;
    /// <summary>直接設定剩幾條命（VillageReturn：最後一條命用完時先設成上限 + 1，扣完剛好補滿、不會 Game Over）</summary>
    public void SetLives(int n) => Lives = Mathf.Clamp(n, 0, maxLives + 1);

    public void GoToMainMenu()
    {
        if (IsLoading) return;
        LoadScene(mainMenuScene, false);
    }

    /// <summary>
    /// 玩家倒下時由 PlayerHealth 呼叫：扣一條命。
    /// 回傳 true = 還有命，可以復活；false = 命用完了，已經排好 Game Over 畫面
    /// </summary>
    public bool OnPlayerDied(string reason, float gameOverDelay)
    {
        if (IsGameOver || IsGameWon) return false;
        Lives = Mathf.Max(0, Lives - 1);
        if (Lives > 0) return true;
        IsGameOver = true;
        pendingEnding = StartCoroutine(EndingRoutine(gameOverDelay, () => ShowGameOver(reason)));
        return false;
    }

    /// <summary>通關（打倒最後一關的 Boss 時呼叫），delay 秒後顯示通關畫面</summary>
    public void TriggerGameWin(float delay = 0f)
    {
        if (IsGameOver || IsGameWon || IsLoading) return;
        IsGameWon = true;
        pendingEnding = StartCoroutine(EndingRoutine(delay, ShowGameWin));
    }

    /// <summary>目前是不是最後一關（BossController 用來判斷打倒 Boss 是不是通關）</summary>
    public bool IsFinalLevel => levels.Length > 0 && CurrentLevelIndex == levels.Length - 1;

    IEnumerator EndingRoutine(float delay, System.Action show)
    {
        if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
        pendingEnding = null;
        show();
    }

    /// <summary>前往下一關；已經是最後一關就是通關</summary>
    public void LoadNextLevel()
    {
        if (IsLoading || levels.Length == 0) return;
        int next = CurrentLevelIndex + 1;
        if (next >= levels.Length) TriggerGameWin();
        else LoadLevel(levels[next].sceneName);
    }

    public void LoadLevel(int index)
    {
        if (index < 0 || index >= levels.Length) return;
        LoadLevel(levels[index].sceneName);
    }

    /// <summary>前往指定關卡，玩家的血量 / 能力 / 背包會帶過去（傳送門、船、測試快捷鍵用）</summary>
    public void LoadLevel(string sceneName) => LoadScene(sceneName, true);

    /// <summary>重新開始目前這一關（玩家回到進關時的狀態）</summary>
    public void ReloadLevel() => LoadScene(SceneManager.GetActiveScene().name, false);

    /// <param name="carryPlayer">true = 把現在玩家的狀態帶到新關卡；false = 沿用 levelEntryState</param>
    void LoadScene(string sceneName, bool carryPlayer)
    {
        if (IsLoading) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[GameManager] 場景「{sceneName}」不在 Build Settings 裡，請執行選單 GeminiGrowUp > 設定關卡流程");
            return;
        }
        StartCoroutine(LoadRoutine(sceneName, carryPlayer));
    }

    IEnumerator LoadRoutine(string sceneName, bool carryPlayer)
    {
        IsLoading = true;
        LoadProgress = 0f;
        if (pendingEnding != null) StopCoroutine(pendingEnding);
        pendingEnding = null;

        // 從一關走到另一關：記下玩家現在的血量 / 能力 / 背包，等一下套到新場景的玩家身上
        if (carryPlayer && InPlayableLevel && sceneName != SceneManager.GetActiveScene().name)
            levelEntryState = PlayerState.Capture(PlayerState.FindPlayer()) ?? levelEntryState;

        // 往後面的關卡前進（帶著玩家狀態）：載入時播轉場動畫，不是一片黑
        int targetIndex = IndexOf(sceneName);
        bool travel = carryPlayer && CurrentLevelIndex >= 0 && targetIndex > CurrentLevelIndex;

        float shownAt = Time.unscaledTime;
        if (travel)
        {
            // 一進傳送門就直接蓋上轉場畫面（不先黑一下），蓋滿之後底下再換成黑幕
            if (transition == null) transition = gameObject.AddComponent<LevelTransition>();
            int fromIndex = CurrentLevelIndex;
            transition.Begin(levels[fromIndex].title, levels[targetIndex].title, fromIndex, targetIndex);
            while (!transition.FullyShown) yield return null;
            fadeAlpha = 1f;
        }
        else yield return Fade(1f, fadeOutTime);

        CloseOverlay(false);
        IsGameOver = false;
        IsGameWon = false;
        Time.timeScale = 1f; // Boss 的慢動作之類的效果不要帶到下一關
        var op = SceneManager.LoadSceneAsync(sceneName);
        var hold = HoldSceneActivation;
        HoldSceneActivation = null;
        if (!travel && hold != null)
        {
            // 主畫面的進度條：背景載好，而且進度條跑完了才切換
            op.allowSceneActivation = false;
            while (op.progress < 0.9f || !hold())
            {
                LoadProgress = op.progress / 0.9f;
                yield return null;
            }
            LoadProgress = 1f;
            op.allowSceneActivation = true;
        }
        if (travel)
        {
            op.allowSceneActivation = false; // 先在背景載入，動畫至少播 transitionMinTime 秒
            while (op.progress < 0.9f || Time.unscaledTime - shownAt < transitionMinTime)
            {
                // 進度條跟著載入進度，但不會比最短播放時間跑得快
                transition.Progress = Mathf.Min(op.progress / 0.9f, (Time.unscaledTime - shownAt) / transitionMinTime);
                yield return null;
            }
            transition.Progress = 1f;
            op.allowSceneActivation = true;
        }
        while (!op.isDone)
        {
            LoadProgress = Mathf.Max(LoadProgress, Mathf.Min(1f, op.progress / 0.9f));
            yield return null;
        }
        LoadProgress = 1f;
        yield return null; // 等新場景的 Start 都跑完

        if (FindLevel(sceneName) != null)
        {
            var player = PlayerState.FindPlayer();
            if (levelEntryState != null) levelEntryState.Apply(player);
            else levelEntryState = PlayerState.Capture(player);
            if (sceneName == StoryStartScene) storyStartState = PlayerState.Capture(player);
            if (autoSaveOnLevelStart) SaveGame();
        }

        if (travel && transition != null) yield return transition.End();

        OnLevelLoaded?.Invoke(sceneName);
        ShowTitle();
        PlaySceneMusic();
        yield return Fade(0f, fadeInTime);
        IsLoading = false;
    }

    /// <summary>播目前場景的背景音樂：主選單 / 各關卡各自一首（Boss 戰開始時 BossController 會換成 Boss 音樂）</summary>
    public void PlaySceneMusic()
    {
        string path = InMainMenu ? mainMenuMusic : CurrentLevel?.music;
        var clip = GameAudio.Clip(path);
        if (clip != null) GameAudio.PlayMusic(clip, musicVolume, musicFadeIn);
        else GameAudio.StopMusic(0.5f);
    }

    void ShowTitle()
    {
        var level = CurrentLevel;
        if (showLevelTitle && level != null && !string.IsNullOrEmpty(level.title))
            VictoryBanner.ShowSmall(level.title, "", new Color(0.85f, 0.9f, 1f));
    }

    IEnumerator Fade(float target, float duration)
    {
        float from = fadeAlpha, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            fadeAlpha = Mathf.Lerp(from, target, t / duration);
            yield return null;
        }
        fadeAlpha = target;
    }

    void Update()
    {
        if (InPlayableLevel && !IsPaused && !IsGameOver && !IsGameWon) PlayTime += Time.deltaTime;
        if (IsLoading) return;
        var kb = Keyboard.current;
        bool pausePressed = (kb != null && kb.escapeKey.wasPressedThisFrame) || PadInput.PausePressed; // 手把 Start

        // 設定畫面開著時 Esc / Start 由設定畫面自己處理（返回暫停選單）
        if (ControlsSettingsMenu.IsOpen || ControlsSettingsMenu.ClosedThisFrame) return;

        // Esc：暫停 / 繼續（別的選單把時間停住的時候不搶，例如能力植物三選一）
        if (pausePressed && InPlayableLevel && !IsGameOver && !IsGameWon)
        {
            if (SkillAimCancellation.CancelForEscape()) return;
            if (IsPaused) Resume();
            else if (Time.timeScale > 0f) Pause();
        }
    }

    // ================= 暫停 / Game Over / 通關畫面（uGUI） =================

    /// <summary>目前在漁村這條主線（漁村、道場）</summary>
    public bool InStoryLevel => IndexOf(SceneManager.GetActiveScene().name) < 0 && InPlayableLevel;
    /// <summary>漁村主線的起點（storyLevels 的第一個）</summary>
    public string StoryStartScene => storyLevels != null && storyLevels.Length > 0 ? storyLevels[0].sceneName : null;
    /// <summary>最近一次進漁村時玩家的狀態（漁村主線的「從漁村重新開始」用）</summary>
    PlayerState storyStartState;

    /// <summary>主選單的「開始遊戲」：命和時間重設，從漁村開始（用場景裡玩家原本的數值）</summary>
    public void StartStoryGame()
    {
        if (IsLoading || string.IsNullOrEmpty(StoryStartScene)) return;
        Lives = maxLives;
        PlayTime = 0f;
        levelEntryState = null;
        storyStartState = null;
        LoadScene(StoryStartScene, false);
    }

    /// <summary>漁村主線的「重新開始」：不管在漁村還是道場，都回到漁村，帶著最近一次進漁村時的狀態</summary>
    public void RestartStory()
    {
        if (IsLoading || string.IsNullOrEmpty(StoryStartScene)) return;
        levelEntryState = storyStartState;
        LoadScene(StoryStartScene, false);
    }

    public void Pause()
    {
        if (IsOverlayOpen || IsLoading) return;
        if (InStoryLevel)
        {
            // 漁村主線：掛軸風格的暫停選單；重新開始 = 回到漁村
            UIKit.Washi = true;
            OpenOverlay("暫　停", new Color(0.17f, 0.11f, 0.065f), FindLevel(SceneManager.GetActiveScene().name)?.title ?? "",
                ("繼續遊戲", Resume),
                ("存檔", OnSaveClicked),
                ("讀取存檔", OnLoadClicked),
                ("設定", OpenSettings),
                ("從漁村重新開始", RestartStory),
                ("回主選單", GoToMainMenu));
            IsPaused = true;
            return;
        }
        UIKit.Washi = false;
        OpenOverlay("PAUSED", new Color(0.85f, 0.9f, 1f), "遊戲暫停",
            ("繼續遊戲", Resume),
            ("存檔", OnSaveClicked),
            ("讀取存檔", OnLoadClicked),
            ("設定", OpenSettings),
            ("重新開始本關", ReloadLevel),
            ("回主選單", GoToMainMenu));
        IsPaused = true; // OpenOverlay 會先把舊的狀態清掉，所以放在後面
    }

    void OnSaveClicked()
    {
        bool ok = SaveGame();
        VictoryBanner.ShowSmall(ok ? "已存檔" : "存檔失敗", ok ? "讀檔時會從這一關的起點開始" : "",
                                ok ? new Color(0.6f, 1f, 0.7f) : new Color(1f, 0.5f, 0.45f));
    }

    /// <summary>暫停選單的「設定」：操作方式、鏡頭靈敏度、自訂按鍵。開著的時候先把暫停選單藏起來，返回時再顯示</summary>
    void OpenSettings()
    {
        if (overlay == null || ControlsSettingsMenu.IsOpen) return;
        var pauseMenu = overlay;
        pauseMenu.gameObject.SetActive(false);
        ControlsSettingsMenu.Open(transform, () =>
        {
            if (pauseMenu == null || overlay != pauseMenu) return;
            pauseMenu.gameObject.SetActive(true);
            var settingsButton = pauseMenu.transform.Find("Button_3");
            if (settingsButton != null) UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(settingsButton.gameObject);
        });
    }

    void OnLoadClicked()
    {
        if (!LoadSavedGame())
            VictoryBanner.ShowSmall("沒有存檔", "", new Color(1f, 0.5f, 0.45f));
    }

    public void Resume()
    {
        if (!IsPaused || IsLoading) return;
        CloseOverlay(true);
    }

    /// <summary>命用完：顯示 GAME OVER 幾秒，接著自動回到最初的出生點（第一關起點）重來，命和遊玩時間都重算</summary>
    void ShowGameOver(string reason)
    {
        string sub = (string.IsNullOrEmpty(reason) ? "生命已經用完了" : $"生命已經用完了（{reason}）") + "\n即將回到最初的出生點重新開始";
        OpenOverlay("GAME OVER", new Color(1f, 0.35f, 0.3f), sub);
        GameAudio.StopMusic(1.5f);
        pendingEnding = StartCoroutine(EndingRoutine(gameOverRestartDelay, StartNewGame));
    }

    void ShowGameWin()
    {
        int t = Mathf.FloorToInt(PlayTime);
        string sub = $"{gameClearSubtitle}\n通關時間 {t / 60:00}:{t % 60:00}　剩餘生命 {Lives}";
        OpenOverlay(gameClearTitle, new Color(1f, 0.82f, 0.42f), sub,
            ("重新開始", StartNewGame),
            ("回主選單", GoToMainMenu));
    }

    void OpenOverlay(string title, Color titleColor, string subtitle,
                     params (string label, UnityEngine.Events.UnityAction action)[] buttons)
    {
        CloseOverlay(false);
        SkillAimCancellation.Cancel(); // Restore targeting's clock BEFORE pause captures it.
        overlayPrevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        // 鏡頭在游標沒鎖住時按左鍵會重新鎖住游標，選單開著時先停用（跟能力植物的做法一樣）
        var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null && cam.enabled)
        {
            cam.enabled = false;
            disabledCamera = cam;
        }
        UIKit.ShowCursor();

        overlay = UIKit.CreateCanvas("GameFlowOverlay", 100, transform);
        var root = overlay.transform;
        // 主畫面風格（MenuSkin）：所有選單（暫停、Game Over、通關）都寫在一張直的破皮紙上，按鈕是破布（選到時被風吹）
        bool skin = MenuSkin.Available;
        UIKit.Panel("Dim", root, skin ? MenuSkin.Dim : UIKit.Washi ? new Color(0.05f, 0.03f, 0.02f, 0.6f) : new Color(0f, 0f, 0f, 0.7f)).raycastTarget = true;
        Text skinTitle = null;
        // 皮紙風格的排法：排得緊一點，整張紙（6 個按鈕時約 710 高）在比較扁的畫面（看得到的高度約 950）也放得下
        const float SkinTitleY = 300f, SkinStartY = 150f, SkinStep = 74f, SkinButtonH = 62f;
        if (skin)
        {
            float top = SkinTitleY + 90f;
            float bottom = buttons.Length == 0 ? 40f : SkinStartY - (buttons.Length - 1) * SkinStep - SkinButtonH * 0.5f - 70f;
            MenuSkin.TallParchment(root, new Vector2(0f, (top + bottom) * 0.5f), new Vector2(660f, top - bottom));
            MenuSkin.TitleStroke(root, new Vector2(0f, SkinTitleY - 2f), new Vector2(400f, 100f));
            skinTitle = UIKit.Label("Title", root, title, 80, titleColor, new Vector2(0f, SkinTitleY), new Vector2(600f, 104f), TextAnchor.MiddleCenter, FontStyle.Bold);
            if (UIKit.Washi) UIKit.Seal(root, "休", new Vector2(180f, SkinTitleY + 20f), 42f);
            UIKit.Label("Subtitle", root, subtitle, 26, Color.white, new Vector2(0f, buttons.Length == 0 ? 170f : SkinTitleY - 70f),
                        new Vector2(580f, buttons.Length == 0 ? 120f : 40f));
        }
        else if (UIKit.Washi)
        {
            // 漁村風格：一幅掛軸，標題是墨色的大字、旁邊蓋一個朱印，下面寫在哪裡
            float bottom = -((buttons.Length > 3 ? 0f : -40f) - (buttons.Length - 1) * (buttons.Length > 5 ? 88f : buttons.Length > 3 ? 98f : 110f)) + 70f;
            float top = 330f;
            UIKit.HangingScroll(root, new Vector2(0f, (top - bottom) * 0.5f), new Vector2(620f, top + bottom));
            UIKit.Label("Title", root, title, 88, titleColor, new Vector2(0f, 192f), new Vector2(600f, 110f), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Seal(root, "休", new Vector2(172f, 210f), 44f);
            UIKit.Label("Subtitle", root, subtitle, 30, new Color(0.42f, 0.31f, 0.2f), new Vector2(0f, 112f), new Vector2(560f, 46f));
        }
        else
        {
            UIKit.Label("Title", root, title, 110, titleColor, new Vector2(0f, 220f), new Vector2(1600f, 150f),
                        TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Label("Subtitle", root, subtitle, 36, Color.white, new Vector2(0f, 90f), new Vector2(1400f, 120f));
        }

        Button first = null;
        // 按鈕多的時候（暫停選單）往上挪、間距縮小，才不會超出畫面
        float startY = skin ? SkinStartY : buttons.Length > 3 ? 0f : -40f;
        float step = skin ? SkinStep : buttons.Length > 5 ? 88f : buttons.Length > 3 ? 98f : 110f;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = UIKit.Button("Button_" + i, root, buttons[i].label, new Vector2(0f, startY - i * step),
                                 skin ? new Vector2(400f, SkinButtonH) : UIKit.Washi ? new Vector2(420f, 72f) : new Vector2(440f, 84f), buttons[i].action,
                                 skin ? 30 : UIKit.Washi ? 32 : 36);
            if (first == null) first = b;
        }
        if (skin)
        {
            MenuSkin.Apply(root);
            // 標題：Game Over 這種紅色的標題寫成朱紅，其他都是墨色
            if (skinTitle != null && titleColor.r > titleColor.g * 1.8f) skinTitle.color = MenuSkin.Cinnabar;
            var sub = root.Find("Subtitle")?.GetComponent<Text>();
            if (sub != null) sub.color = new Color(0.42f, 0.31f, 0.2f);
        }
        if (first != null) first.Select(); // 可以直接按 Enter
    }

    void CloseOverlay(bool restoreGameplay)
    {
        if (overlay != null) Destroy(overlay.gameObject);
        overlay = null;
        IsPaused = false;
        UIKit.Washi = false;
        if (restoreGameplay)
        {
            Time.timeScale = overlayPrevTimeScale;
            if (disabledCamera != null) disabledCamera.enabled = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        disabledCamera = null; // 換場景時舊的鏡頭會跟著場景一起被刪掉
    }

    void OnGUI()
    {
        if (fadeAlpha <= 0f) return;
        GUI.depth = -1000; // 蓋在所有 HUD 上面
        GUI.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, fadeAlpha);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
