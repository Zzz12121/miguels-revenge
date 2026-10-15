using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 整個遊戲的關卡流程（跨場景存在，不會因為換場景被刪掉）：
///   - 記住關卡順序：星球 →（傳送門）→ 維京村莊 →（搭船）→ 地下城（Boss，最後一關）
///   - 傳送門 / 船抵達終點時呼叫 LoadNextLevel() 或 LoadLevel("場景名")
///   - 換場景時畫面淡出淡入，進入新關卡會顯示關卡名稱
///   - 最後一關打完會顯示「全破」然後回到第一關
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
        new Level { sceneName = "Planet",             title = "第一關　星球" },
        new Level { sceneName = "The_Viking_Village", title = "第二關　維京村莊" },
        new Level { sceneName = "Floor02",            title = "第三關　地下城" },
    };

    [Header("轉場")]
    public float fadeOutTime = 0.8f;
    public float fadeInTime = 0.8f;
    public Color fadeColor = Color.black;
    public bool showLevelTitle = true;

    [Header("全破")]
    public string gameClearTitle = "ALL CLEAR";
    public string gameClearSubtitle = "感謝遊玩";
    [Tooltip("全破畫面停留幾秒後回到第一關")]
    public float gameClearDelay = 6f;

    [Header("測試")]
    public bool debugHotkeys = true;

    /// <summary>正在換場景（傳送門用來避免重複觸發）</summary>
    public bool IsLoading { get; private set; }
    /// <summary>目前場景在關卡順序中的位置，不在清單裡就是 -1</summary>
    public int CurrentLevelIndex => IndexOf(SceneManager.GetActiveScene().name);
    public Level CurrentLevel => CurrentLevelIndex >= 0 ? levels[CurrentLevelIndex] : null;

    /// <summary>新關卡載入完成（參數是場景名稱）</summary>
    public event System.Action<string> OnLevelLoaded;

    float fadeAlpha;

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

    /// <summary>前往下一關；已經是最後一關就是全破，回到第一關</summary>
    public void LoadNextLevel()
    {
        if (IsLoading || levels.Length == 0) return;
        int next = CurrentLevelIndex + 1;
        if (next >= levels.Length) StartCoroutine(GameClearRoutine());
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
        yield return Fade(1f, fadeOutTime);

        Time.timeScale = 1f; // Boss 的慢動作之類的效果不要帶到下一關
        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;
        yield return null; // 等新場景的 Start 都跑完

        OnLevelLoaded?.Invoke(sceneName);
        ShowTitle();
        yield return Fade(0f, fadeInTime);
        IsLoading = false;
    }

    IEnumerator GameClearRoutine()
    {
        IsLoading = true;
        VictoryBanner.Show(gameClearTitle, gameClearSubtitle);
        yield return new WaitForSecondsRealtime(gameClearDelay);
        IsLoading = false;
        if (levels.Length > 0) LoadLevel(levels[0].sceneName);
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
        if (!debugHotkeys || !Debug.isDebugBuild || IsLoading) return;
        var kb = Keyboard.current;
        if (kb == null || !kb.shiftKey.isPressed) return;
        if (kb.digit1Key.wasPressedThisFrame) LoadLevel(0);
        else if (kb.digit2Key.wasPressedThisFrame) LoadLevel(1);
        else if (kb.digit3Key.wasPressedThisFrame) LoadLevel(2);
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
