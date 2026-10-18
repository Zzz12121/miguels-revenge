using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 主選單（MainMenu 場景）：
///   - 遊戲標題
///   - 用 GUI Text 說明遊戲規則和操作
///   - 有存檔時多一個「繼續遊戲」按鈕（GameManager.LoadSavedGame），Enter 預設繼續
///   - 「開始遊戲」按鈕進入遊玩模式（GameManager.StartNewGame）、「結束遊戲」按鈕關閉遊戲
///
/// 畫面全部用程式碼產生，場景裡只要有一個掛著這個元件的物件就好；
/// 就算忘了放，只要場景名稱是 GameManager.mainMenuScene 也會自動建立。
/// </summary>
public class MainMenu : MonoBehaviour
{
    public string gameTitle = "MIGUEL'S REVENGE";
    public string subtitle = "3D 異次元競技場";

    [TextArea(6, 20)]
    public string rulesText =
        "<b><color=#FFD36B>遊戲規則</color></b>\n" +
        "消滅每一關的敵人，就會被傳送到下一關，共有三個關卡：\n" +
        "　<b>第一關　沙漠星球</b>：打倒時鐘敵人，牠倒下的地方會出現傳送門\n" +
        "　<b>第二關　維京村莊</b>：擊敗村莊守衛，搭上長船駛向終點\n" +
        "　　　　　　　　　　　潛入水中會消耗<color=#7FD4FF>氧氣</color>，氧氣用完會開始扣血\n" +
        "　<b>第三關　地下城</b>：打倒最終 Boss 就通關！\n\n" +
        "玩家共有 <color=#FF8080>3 條命</color>，倒下後會在檢查點復活，命用完就 Game Over。\n" +
        "受傷後可以撿<color=#FF6060>紅十字補血道具</color>，或靠檢查點和篝火回復血量。";

    [TextArea(6, 20)]
    public string controlsText =
        "<b><color=#FFD36B>操作方式</color></b>\n" +
        "W A S D　　移動\n" +
        "左 Shift　　衝刺\n" +
        "空白鍵　　　跳躍\n" +
        "滑鼠　　　　轉動鏡頭\n" +
        "滾輪　　　　鏡頭遠近\n" +
        "滑鼠左鍵　　近戰連擊\n" +
        "滑鼠右鍵　　舉盾衝撞（正面傷害減少）\n" +
        "右鍵按住　　瞄準（在船上用弓箭時）\n" +
        "E　　　　　互動 / 傳送門 / 上船\n" +
        "M　　　　　顯示 / 隱藏小地圖\n" +
        "Esc　　　　暫停選單（存檔 / 讀檔）";

    public Color backgroundColor = new Color(0.05f, 0.06f, 0.1f);

    Canvas canvas;
    Button startButton;
    Button continueButton;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string menuScene = GameManager.Instance != null ? GameManager.Instance.mainMenuScene : "MainMenu";
        if (scene.name != menuScene || FindAnyObjectByType<MainMenu>() != null) return;
        new GameObject("MainMenu").AddComponent<MainMenu>();
    }

    void Start()
    {
        Time.timeScale = 1f;
        UIKit.ShowCursor();
        EnsureCamera();
        Build();
        // 主選單的背景音樂（GameManager 決定播哪一首）
        if (GameManager.Instance != null) GameManager.Instance.PlaySceneMusic();
    }

    void EnsureCamera()
    {
        if (Camera.main != null) return;
        var go = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        go.tag = "MainCamera";
        var cam = go.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    void Build()
    {
        canvas = UIKit.CreateCanvas("MainMenuCanvas", 10, transform);
        var root = canvas.transform;

        UIKit.Panel("Background", root, backgroundColor);
        // 上下兩條金色細線當裝飾
        UIKit.Box("LineTop", root, new Vector2(0f, 330f), new Vector2(1500f, 3f), new Color(1f, 0.82f, 0.42f, 0.6f));
        UIKit.Box("LineBottom", root, new Vector2(0f, -330f), new Vector2(1500f, 3f), new Color(1f, 0.82f, 0.42f, 0.6f));

        UIKit.Label("Title", root, gameTitle, 96, new Color(1f, 0.82f, 0.42f), new Vector2(0f, 420f),
                    new Vector2(1600f, 130f), TextAnchor.MiddleCenter, FontStyle.Bold);
        UIKit.Label("Subtitle", root, subtitle, 40, new Color(0.8f, 0.86f, 1f), new Vector2(0f, 350f),
                    new Vector2(1200f, 60f));

        // 規則（左）與操作（右）
        UIKit.Box("RulesBox", root, new Vector2(-260f, 20f), new Vector2(980f, 560f), new Color(0f, 0f, 0f, 0.35f));
        UIKit.Label("Rules", root, rulesText, 28, Color.white, new Vector2(-260f, 20f), new Vector2(920f, 520f),
                    TextAnchor.UpperLeft).lineSpacing = 1.15f;

        UIKit.Box("ControlsBox", root, new Vector2(520f, 20f), new Vector2(500f, 560f), new Color(0f, 0f, 0f, 0.35f));
        UIKit.Label("Controls", root, controlsText, 28, Color.white, new Vector2(520f, 20f), new Vector2(440f, 520f),
                    TextAnchor.UpperLeft).lineSpacing = 1.15f;

        string save = GameManager.Instance != null ? GameManager.Instance.DescribeSave() : null;
        if (save != null)
        {
            continueButton = UIKit.Button("ContinueButton", root, "繼續遊戲", new Vector2(-360f, -420f),
                                          new Vector2(320f, 90f), OnContinueClicked, 40);
            startButton = UIKit.Button("StartButton", root, "開始新遊戲", new Vector2(0f, -420f), new Vector2(320f, 90f),
                                       OnStartClicked, 40);
            UIKit.Button("QuitButton", root, "結束遊戲", new Vector2(360f, -420f), new Vector2(320f, 90f), OnQuitClicked, 40);
            UIKit.Label("Hint", root, $"存檔：{save}　　Enter 繼續遊戲", 22, new Color(1f, 1f, 1f, 0.55f),
                        new Vector2(0f, -500f), new Vector2(1200f, 40f));
        }
        else
        {
            startButton = UIKit.Button("StartButton", root, "開始遊戲", new Vector2(-180f, -420f), new Vector2(320f, 90f),
                                       OnStartClicked, 40);
            UIKit.Button("QuitButton", root, "結束遊戲", new Vector2(180f, -420f), new Vector2(320f, 90f), OnQuitClicked, 40);
            UIKit.Label("Hint", root, "Enter 也可以開始遊戲", 22, new Color(1f, 1f, 1f, 0.5f), new Vector2(0f, -500f),
                        new Vector2(800f, 40f));
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
        {
            if (continueButton != null) OnContinueClicked();
            else OnStartClicked();
        }
    }

    void OnContinueClicked()
    {
        var gm = GameManager.Instance;
        if (gm == null || gm.IsLoading) return;
        GameAudio.Play("SFX/RPG/metalClick", Vector3.zero, 1f, 1f, 0f, 0f);
        if (gm.LoadSavedGame())
        {
            if (continueButton != null) continueButton.interactable = false;
            if (startButton != null) startButton.interactable = false;
        }
    }

    void OnStartClicked()
    {
        if (GameManager.Instance == null || GameManager.Instance.IsLoading) return;
        if (startButton != null) startButton.interactable = false;
        if (continueButton != null) continueButton.interactable = false;
        GameAudio.Play("SFX/RPG/metalClick", Vector3.zero, 1f, 1f, 0f, 0f);
        GameManager.Instance.StartNewGame();
    }

    void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
