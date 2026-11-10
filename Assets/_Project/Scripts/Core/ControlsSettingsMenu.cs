using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 暫停選單的「設定」畫面：選操作方式（自動 / 鍵盤滑鼠 / 手把）、鏡頭靈敏度、上下反轉，
/// 以及每個動作的按鍵（鍵盤滑鼠、手把各一份，點一下再按新的鍵）。設定存在 PlayerPrefs，下次開遊戲還在。
/// 滑鼠點、鍵盤方向鍵 + Enter、手把十字鍵 / 左搖桿 + A 都能操作；Esc / 手把 B 返回。
/// 用法：ControlsSettingsMenu.Open(parent, onClose)
/// </summary>
public sealed class ControlsSettingsMenu : MonoBehaviour
{
    public static bool IsOpen => instance != null;
    /// <summary>這一幀剛關掉（同一下 Esc 不要再被暫停選單當成「繼續遊戲」）</summary>
    public static bool ClosedThisFrame => closedFrame == Time.frameCount;
    static ControlsSettingsMenu instance;
    static int closedFrame = -1;

    Action onClose;
    bool padTab;
    Canvas canvas;
    Transform root;

    // 等待改鍵
    bool listening;
    GameAction listenAction;
    float listenStarted;
    const float ListenIgnore = .2f, ListenTimeout = 6f;

    readonly Dictionary<GameAction, Text> bindingTexts = new Dictionary<GameAction, Text>();
    readonly Dictionary<GameAction, Button> bindingButtons = new Dictionary<GameAction, Button>();
    readonly List<(InputScheme scheme, Button button)> schemeButtons = new List<(InputScheme, Button)>();
    Button keyboardTab, padTabButton, invertButton;
    Text lookValue, statusText;

    static readonly Color Selected = new Color(0.85f, 0.62f, 0.2f, 1f);
    static readonly Color Normal = new Color(0.16f, 0.2f, 0.28f, 0.92f);

    public static void Open(Transform parent, Action onClose)
    {
        if (instance != null) return;
        var go = new GameObject("ControlsSettingsMenu");
        if (parent != null) go.transform.SetParent(parent, false);
        instance = go.AddComponent<ControlsSettingsMenu>();
        instance.onClose = onClose;
        instance.padTab = PadInput.UsingPadRecently;
        instance.Build();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Close()
    {
        closedFrame = Time.frameCount;
        Destroy(gameObject);
        instance = null;
        onClose?.Invoke();
    }

    // ---------------- 介面 ----------------

    void Build()
    {
        if (canvas != null) Destroy(canvas.gameObject);
        bindingTexts.Clear();
        bindingButtons.Clear();
        schemeButtons.Clear();

        canvas = UIKit.CreateCanvas("ControlsSettingsCanvas", 110, transform);
        root = canvas.transform;
        UIKit.Panel("Dim", root, new Color(0f, 0f, 0f, 0.82f)).raycastTarget = true;
        UIKit.Label("Title", root, "設定", 64, new Color(0.85f, 0.9f, 1f), new Vector2(0f, 425f), new Vector2(800f, 90f),
                    TextAnchor.MiddleCenter, FontStyle.Bold);

        // 操作方式
        UIKit.Label("SchemeLabel", root, "操作方式", 30, Color.white, new Vector2(-560f, 345f), new Vector2(240f, 60f), TextAnchor.MiddleRight);
        AddScheme(InputScheme.Auto, "自動（兩種都能用）", new Vector2(-250f, 345f), 340f);
        AddScheme(InputScheme.KeyboardMouse, "只用鍵盤滑鼠", new Vector2(80f, 345f), 280f);
        AddScheme(InputScheme.Gamepad, "只用手把", new Vector2(360f, 345f), 240f);

        // 鏡頭
        UIKit.Label("LookLabel", root, "鏡頭靈敏度", 30, Color.white, new Vector2(-560f, 278f), new Vector2(240f, 60f), TextAnchor.MiddleRight);
        UIKit.Button("LookDown", root, "－", new Vector2(-380f, 278f), new Vector2(70f, 56f), () => ChangeLook(-.1f), 34);
        lookValue = UIKit.Label("LookValue", root, "", 30, Color.white, new Vector2(-280f, 278f), new Vector2(120f, 56f));
        UIKit.Button("LookUp", root, "＋", new Vector2(-180f, 278f), new Vector2(70f, 56f), () => ChangeLook(.1f), 34);
        invertButton = UIKit.Button("Invert", root, "", new Vector2(120f, 278f), new Vector2(360f, 56f), ToggleInvert, 28);

        // 分頁
        keyboardTab = UIKit.Button("TabKeyboard", root, "鍵盤滑鼠按鍵", new Vector2(-160f, 205f), new Vector2(300f, 60f), () => SetTab(false), 30);
        padTabButton = UIKit.Button("TabPad", root, "手把按鍵", new Vector2(160f, 205f), new Vector2(300f, 60f), () => SetTab(true), 30);

        // 按鍵列表：兩欄
        var actions = InputBindings.All;
        int perColumn = Mathf.CeilToInt(actions.Length / 2f);
        for (int i = 0; i < actions.Length; i++)
        {
            var a = actions[i];
            int col = i / perColumn, row = i % perColumn;
            float x = col == 0 ? -430f : 430f, y = 135f - row * 58f;
            UIKit.Label("Label_" + a, root, InputBindings.Label(a), 26, new Color(0.9f, 0.92f, 1f), new Vector2(x - 150f, y),
                        new Vector2(320f, 50f), TextAnchor.MiddleRight);
            var b = UIKit.Button("Bind_" + a, root, "", new Vector2(x + 140f, y), new Vector2(240f, 50f), () => BeginListen(a), 26);
            bindingButtons[a] = b;
            bindingTexts[a] = b.GetComponentInChildren<Text>();
        }

        statusText = UIKit.Label("Status", root, "", 26, new Color(1f, 0.85f, 0.5f), new Vector2(0f, -270f), new Vector2(1400f, 44f));
        UIKit.Label("Hint", root, "點一下按鍵後按下新的鍵即可更換；按到別的動作已經在用的鍵，兩個會互換。移動、轉鏡頭、暫停（Esc / Start）固定不能改",
                    20, new Color(1f, 1f, 1f, 0.6f), new Vector2(0f, -310f), new Vector2(1500f, 36f));
        UIKit.Button("Reset", root, "恢復預設按鍵", new Vector2(-180f, -375f), new Vector2(320f, 62f), ResetCurrent, 30);
        var back = UIKit.Button("Back", root, "返回", new Vector2(180f, -375f), new Vector2(320f, 62f), Close, 30);

        Refresh();
        EventSystem.current?.SetSelectedGameObject(back.gameObject);
    }

    void AddScheme(InputScheme scheme, string label, Vector2 pos, float width)
    {
        var b = UIKit.Button("Scheme_" + scheme, root, label, pos, new Vector2(width, 56f), () =>
        {
            InputBindings.Scheme = scheme;
            if (scheme == InputScheme.Gamepad) padTab = true;
            else if (scheme == InputScheme.KeyboardMouse) padTab = false;
            Refresh();
            if (scheme == InputScheme.Gamepad && Gamepad.current == null)
                statusText.text = "沒有偵測到手把：插上手把之前，只有 Esc 可以用（回到這裡改回「自動」）";
        }, 26);
        schemeButtons.Add((scheme, b));
    }

    void Refresh()
    {
        foreach (var (scheme, button) in schemeButtons) Tint(button, InputBindings.Scheme == scheme);
        Tint(keyboardTab, !padTab);
        Tint(padTabButton, padTab);
        lookValue.text = $"{InputBindings.LookSensitivity:0.0}x";
        invertButton.GetComponentInChildren<Text>().text = "鏡頭上下反轉：" + (InputBindings.InvertY ? "開" : "關");
        foreach (var a in InputBindings.All)
            bindingTexts[a].text = listening && a == listenAction ? "按下新按鍵…" : InputBindings.DisplayName(a, padTab);
        if (!listening && statusText != null && !statusText.text.StartsWith("沒有偵測到"))
            statusText.text = padTab && Gamepad.current == null ? "目前沒有偵測到手把（還是可以先設定）" : "";
    }

    static void Tint(Button b, bool on)
    {
        var c = b.colors;
        c.normalColor = on ? Selected : Normal;
        b.colors = c;
    }

    void SetTab(bool pad)
    {
        padTab = pad;
        listening = false;
        Refresh();
    }

    void ChangeLook(float delta)
    {
        InputBindings.LookSensitivity = Mathf.Round((InputBindings.LookSensitivity + delta) * 10f) / 10f;
        Refresh();
    }

    void ToggleInvert()
    {
        InputBindings.InvertY = !InputBindings.InvertY;
        Refresh();
    }

    void ResetCurrent()
    {
        InputBindings.ResetDefaults(padTab);
        listening = false;
        statusText.text = padTab ? "手把按鍵已恢復預設" : "鍵盤滑鼠按鍵已恢復預設";
        Refresh();
    }

    void BeginListen(GameAction a)
    {
        listening = true;
        listenAction = a;
        listenStarted = Time.unscaledTime;
        statusText.text = $"「{InputBindings.Label(a)}」：請按下新的{(padTab ? "手把按鍵" : "鍵盤按鍵或滑鼠鍵")}（Esc 取消）";
        // 等待中不要讓 UI 導覽把按鍵吃掉（例如手把 A / 方向鍵）
        EventSystem.current?.SetSelectedGameObject(null);
        Refresh();
    }

    void EndListen(string message)
    {
        listening = false;
        statusText.text = message;
        Refresh();
        if (bindingButtons.TryGetValue(listenAction, out var b)) EventSystem.current?.SetSelectedGameObject(b.gameObject);
    }

    // ---------------- 輸入 ----------------

    void Update()
    {
        var kb = Keyboard.current;
        bool escape = kb != null && kb.escapeKey.wasPressedThisFrame;

        if (listening)
        {
            if (escape) { EndListen("已取消"); return; }
            if (Time.unscaledTime - listenStarted > ListenTimeout) { EndListen("太久沒有按鍵，已取消"); return; }
            if (Time.unscaledTime - listenStarted < ListenIgnore) return; // 不要把點下按鈕的那一下當成新按鍵
            string path = InputBindings.ListenForPress(padTab);
            if (path != null)
            {
                InputBindings.Rebind(listenAction, padTab, path);
                EndListen($"「{InputBindings.Label(listenAction)}」改成 {InputBindings.PathName(path)}");
            }
            return;
        }

        // 返回：Esc / 手把 B / Start
        var pad = Gamepad.current;
        if (escape || (pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame)))
        {
            Close();
            return;
        }

        // 手把 LB / RB 切換分頁
        if (pad != null && (pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame))
            SetTab(!padTab);

        // 選取的東西被點掉了（例如點了空白處）：用手把 / 方向鍵時自動選回「返回」
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject == null &&
            (PadInput.AnyPressed || (kb != null && (kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame))))
        {
            var back = root.Find("Back");
            if (back != null) EventSystem.current.SetSelectedGameObject(back.gameObject);
        }
    }
}
