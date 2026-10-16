using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 整個遊戲的關卡流程（跨場景存在，不會因為換場景被刪掉）：
///   - 主選單（MainMenu）按「開始遊戲」→ StartNewGame()，從第一關開始
///   - 記住關卡順序：星球 →（傳送門）→ 維京村莊 →（搭船）→ 地下城（Boss，最後一關）
///   - 傳送門 / 船抵達終點時呼叫 LoadNextLevel() 或 LoadLevel("場景名")
///   - 換場景時畫面淡出淡入，進入新關卡會顯示關卡名稱
///   - 玩家有 maxLives 條命：倒下時扣一條命並在檢查點復活，命用完 → Game Over 畫面
///   - 打倒最後一關的 Boss（或在最後一關呼叫 LoadNextLevel）→ 通關畫面
///   - Game Over / 通關畫面都有「重新開始」「回主選單」按鈕；遊戲中按 Esc 開暫停選單
///
/// 不用手動放進場景：按 Play 時會自動建立。想改關卡順序或標題，
/// 可以在任一場景放一個掛著 GameManager 的物件，那個物件的設定會優先使用。
///
/// 測試用快捷鍵（只在 Editor / Development Build 有效）：Shift + 1 / 2 / 3 直接跳到第 N 關。
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
    }

    public static GameManager Instance { get; private set; }

    [Tooltip("關卡順序，傳送門的「下一關」就是照這個順序")]
    public Level[] levels =
    {
        new Level { sceneName = "Planet",             title = "第一關　沙漠" },
        new Level { sceneName = "The_Viking_Village", title = "第二關　維京村莊" },
        new Level { sceneName = "Floor02",            title = "第三關　地下城" },
    };

    [Header("轉場")]
    public float fadeOutTime = 0.8f;
    public float fadeInTime = 0.8f;
    public Color fadeColor = Color.black;
    public bool showLevelTitle = true;

    [Header("主選單")]
    [Tooltip("主選單場景名稱（要放在 Build Settings 的第一個）")]
    public string mainMenuScene = "MainMenu";

    [Header("生命")]
    [Tooltip("玩家總共有幾條命，用完就 Game Over")]
    public int maxLives = 3;

    [Header("全破")]
    public string gameClearTitle = "ALL CLEAR";
    public string gameClearSubtitle = "感謝遊玩";

    [Header("測試")]
    public bool debugHotkeys = true;

    /// <summary>正在換場景（傳送門用來避免重複觸發）</summary>
    public bool IsLoading { get; private set; }
    /// <summary>目前場景在關卡順序中的位置，不在清單裡就是 -1</summary>
    public int CurrentLevelIndex => IndexOf(SceneManager.GetActiveScene().name);
    public Level CurrentLevel => CurrentLevelIndex >= 0 ? levels[CurrentLevelIndex] : null;

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
        // 直接從某一關按 Play 也顯示關卡標題
        ShowTitle();
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
        LoadLevel(levels[0].sceneName);
    }

    /// <summary>Game Over 後重新挑戰目前這一關（命補滿）</summary>
    public void RetryLevel()
    {
        if (IsLoading) return;
        Lives = maxLives;
        ReloadLevel();
    }

    public void GoToMainMenu()
    {
        if (IsLoading) return;
        LoadLevel(mainMenuScene);
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

    public void LoadLevel(string sceneName)
    {
        if (IsLoading) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"[GameManager] 場景「{sceneName}」不在 Build Settings 裡，請執行選單 GeminiGrowUp > 設定傳送門與關卡順序");
            return;
        }
        StartCoroutine(LoadRoutine(sceneName));
    }

    /// <summary>重新開始目前這一關</summary>
    public void ReloadLevel() => LoadLevel(SceneManager.GetActiveScene().name);

    IEnumerator LoadRoutine(string sceneName)
    {
        IsLoading = true;
        if (pendingEnding != null) StopCoroutine(pendingEnding);
        pendingEnding = null;
        yield return Fade(1f, fadeOutTime);

        CloseOverlay(false);
        IsGameOver = false;
        IsGameWon = false;
        Time.timeScale = 1f; // Boss 的慢動作之類的效果不要帶到下一關
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
        yield return null; // 等新場景的 Start 都跑完

        OnLevelLoaded?.Invoke(sceneName);
        ShowTitle();
        yield return Fade(0f, fadeInTime);
        IsLoading = false;
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
        if (CurrentLevelIndex >= 0 && !IsPaused && !IsGameOver && !IsGameWon) PlayTime += Time.deltaTime;
        if (IsLoading) return;
        var kb = Keyboard.current;
        if (kb == null) return;

        // Esc：暫停 / 繼續（別的選單把時間停住的時候不搶，例如能力植物三選一）
        if (kb.escapeKey.wasPressedThisFrame && CurrentLevelIndex >= 0 && !IsGameOver && !IsGameWon)
        {
            if (IsPaused) Resume();
            else if (Time.timeScale > 0f) Pause();
        }

        if (!debugHotkeys || !Debug.isDebugBuild || !kb.shiftKey.isPressed) return;
        if (kb.digit1Key.wasPressedThisFrame) LoadLevel(0);
        else if (kb.digit2Key.wasPressedThisFrame) LoadLevel(1);
        else if (kb.digit3Key.wasPressedThisFrame) LoadLevel(2);
    }

    // ================= 暫停 / Game Over / 通關畫面（uGUI） =================

    public void Pause()
    {
        if (IsOverlayOpen || IsLoading) return;
        OpenOverlay("PAUSED", new Color(0.85f, 0.9f, 1f), "遊戲暫停",
            ("繼續遊戲", Resume),
            ("重新開始本關", ReloadLevel),
            ("回主選單", GoToMainMenu));
        IsPaused = true; // OpenOverlay 會先把舊的狀態清掉，所以放在後面
    }

    public void Resume()
    {
        if (!IsPaused || IsLoading) return;
        CloseOverlay(true);
    }

    void ShowGameOver(string reason)
    {
        string sub = string.IsNullOrEmpty(reason) ? "生命已經用完了" : $"生命已經用完了（{reason}）";
        OpenOverlay("GAME OVER", new Color(1f, 0.35f, 0.3f), sub,
            ("重新挑戰本關", RetryLevel),
            ("從第一關重新開始", StartNewGame),
            ("回主選單", GoToMainMenu));
        GameAudio.StopMusic(1.5f);
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
        UIKit.Panel("Dim", root, new Color(0f, 0f, 0f, 0.7f)).raycastTarget = true;
        UIKit.Label("Title", root, title, 110, titleColor, new Vector2(0f, 220f), new Vector2(1600f, 150f),
                    TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Label("Subtitle", root, subtitle, 36, Color.white, new Vector2(0f, 90f), new Vector2(1400f, 120f));

        Button first = null;
        for (int i = 0; i < buttons.Length; i++)
        {
            var b = UIKit.Button("Button_" + i, root, buttons[i].label, new Vector2(0f, -40f - i * 110f),
                                 new Vector2(440f, 88f), buttons[i].action, 36);
            if (first == null) first = b;
        }
        if (first != null) first.Select(); // 可以直接按 Enter
    }

    void CloseOverlay(bool restoreGameplay)
    {
        if (overlay != null) Destroy(overlay.gameObject);
        overlay = null;
        IsPaused = false;
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
