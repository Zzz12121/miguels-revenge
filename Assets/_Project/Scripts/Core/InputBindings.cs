using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>可以在設定選單裡改按鍵的動作（移動、轉鏡頭、暫停、對話下一句固定不能改）</summary>
public enum GameAction
{
    Jump, Sprint, Roll, Attack, Block, Interact, Tether,
    Skill1, Skill2, Skill3, Skill4, HealPotion, StaminaPotion, Map
}

/// <summary>操作方式：自動（鍵盤滑鼠和手把都能用）/ 只用鍵盤滑鼠 / 只用手把</summary>
public enum InputScheme { Auto, KeyboardMouse, Gamepad }

/// <summary>
/// 全部動作的按鍵表：每個動作有一個鍵盤滑鼠按鍵、一個手把按鍵，玩家可以在暫停選單的「設定」裡改，存在 PlayerPrefs。
/// 按鍵用 Input System 的路徑字串記，例如 &lt;Keyboard&gt;/space、&lt;Mouse&gt;/leftButton、&lt;Gamepad&gt;/buttonSouth。
/// 各系統讀按鍵一律問這裡：InputBindings.Pressed(GameAction.Jump)
/// </summary>
public static class InputBindings
{
    public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));

    static readonly string[] DefaultKeyboard =
    {
        "<Keyboard>/space", "<Keyboard>/leftShift", "<Keyboard>/v", "<Mouse>/leftButton", "<Mouse>/rightButton",
        "<Keyboard>/e", "<Mouse>/middleButton",
        "<Keyboard>/1", "<Keyboard>/2", "<Keyboard>/3", "<Keyboard>/4", "<Keyboard>/q", "<Keyboard>/g", "<Keyboard>/m",
    };
    static readonly string[] DefaultPad =
    {
        "<Gamepad>/buttonSouth", "<Gamepad>/leftShoulder", "<Gamepad>/buttonEast", "<Gamepad>/buttonWest", "<Gamepad>/leftTrigger",
        "<Gamepad>/buttonNorth", "<Gamepad>/rightShoulder",
        "<Gamepad>/dpad/left", "<Gamepad>/dpad/up", "<Gamepad>/dpad/right", "<Gamepad>/dpad/down",
        "<Gamepad>/rightStickPress", "<Gamepad>/leftStickPress", "<Gamepad>/select",
    };

    public static string Label(GameAction a) => a switch
    {
        GameAction.Jump => "跳躍", GameAction.Sprint => "衝刺（按住）", GameAction.Roll => "翻滾",
        GameAction.Attack => "攻擊", GameAction.Block => "格擋 / 盾擊", GameAction.Interact => "互動",
        GameAction.Tether => "牽引技能", GameAction.Skill1 => "技能 1 虛空連斬", GameAction.Skill2 => "技能 2 貓躍雙斬",
        GameAction.Skill3 => "技能 3 月弧突進", GameAction.Skill4 => "技能 4 空中十字斬",
        GameAction.HealPotion => "治癒藥水", GameAction.StaminaPotion => "精力藥水", GameAction.Map => "小地圖",
        _ => a.ToString()
    };

    static string[] keyboard, pad;
    static InputScheme scheme;
    static float lookSensitivity = 1f;
    static bool invertY;
    static bool loaded;

    const string Prefs = "Controls.";

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        keyboard = (string[])DefaultKeyboard.Clone();
        pad = (string[])DefaultPad.Clone();
        for (int i = 0; i < All.Length; i++)
        {
            keyboard[i] = PlayerPrefs.GetString(Prefs + "kb." + All[i], keyboard[i]);
            pad[i] = PlayerPrefs.GetString(Prefs + "pad." + All[i], pad[i]);
        }
        scheme = (InputScheme)PlayerPrefs.GetInt(Prefs + "scheme", 0);
        lookSensitivity = PlayerPrefs.GetFloat(Prefs + "look", 1f);
        invertY = PlayerPrefs.GetInt(Prefs + "invertY", 0) == 1;
    }

    static void Save()
    {
        for (int i = 0; i < All.Length; i++)
        {
            PlayerPrefs.SetString(Prefs + "kb." + All[i], keyboard[i]);
            PlayerPrefs.SetString(Prefs + "pad." + All[i], pad[i]);
        }
        PlayerPrefs.SetInt(Prefs + "scheme", (int)scheme);
        PlayerPrefs.SetFloat(Prefs + "look", lookSensitivity);
        PlayerPrefs.SetInt(Prefs + "invertY", invertY ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ---------------- 設定 ----------------

    public static InputScheme Scheme { get { EnsureLoaded(); return scheme; } set { EnsureLoaded(); scheme = value; Save(); } }
    /// <summary>鏡頭靈敏度倍率（滑鼠和右搖桿都套用）</summary>
    public static float LookSensitivity { get { EnsureLoaded(); return lookSensitivity; } set { EnsureLoaded(); lookSensitivity = Mathf.Clamp(value, .25f, 3f); Save(); } }
    public static bool InvertY { get { EnsureLoaded(); return invertY; } set { EnsureLoaded(); invertY = value; Save(); } }

    public static bool KeyboardEnabled => Scheme != InputScheme.Gamepad;
    public static bool PadEnabled => Scheme != InputScheme.KeyboardMouse && Gamepad.current != null;

    public static string KeyboardPath(GameAction a) { EnsureLoaded(); return keyboard[(int)a]; }
    public static string PadPath(GameAction a) { EnsureLoaded(); return pad[(int)a]; }

    /// <summary>改按鍵；如果別的動作已經用了這顆鍵，兩個動作的按鍵互換</summary>
    public static void Rebind(GameAction a, bool forPad, string path)
    {
        EnsureLoaded();
        var table = forPad ? pad : keyboard;
        int i = (int)a;
        for (int j = 0; j < table.Length; j++)
            if (j != i && table[j] == path) table[j] = table[i];
        table[i] = path;
        Save();
    }

    public static void ResetDefaults(bool forPad)
    {
        EnsureLoaded();
        if (forPad) pad = (string[])DefaultPad.Clone();
        else keyboard = (string[])DefaultKeyboard.Clone();
        Save();
    }

    // ---------------- 讀按鍵 ----------------

    /// <summary>把路徑字串變成目前裝置上的按鍵（裝置沒插就是 null）</summary>
    public static ButtonControl Resolve(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int split = path.IndexOf('/');
        if (split < 0) return null;
        string device = path.Substring(0, split), control = path.Substring(split + 1);
        InputDevice d = device switch
        {
            "<Keyboard>" => Keyboard.current,
            "<Mouse>" => Mouse.current,
            "<Gamepad>" => Gamepad.current,
            _ => null
        };
        return d?.TryGetChildControl<ButtonControl>(control);
    }

    static ButtonControl KeyboardControl(GameAction a) => KeyboardEnabled ? Resolve(KeyboardPath(a)) : null;
    static ButtonControl PadControl(GameAction a) => PadEnabled ? Resolve(PadPath(a)) : null;

    /// <summary>這一幀剛按下（鍵盤滑鼠或手把任一個）</summary>
    public static bool Pressed(GameAction a) => KeyboardPressed(a) || PadPressed(a);
    public static bool Held(GameAction a) => (KeyboardControl(a)?.isPressed ?? false) || (PadControl(a)?.isPressed ?? false);
    public static bool Released(GameAction a) => (KeyboardControl(a)?.wasReleasedThisFrame ?? false) || (PadControl(a)?.wasReleasedThisFrame ?? false);
    public static bool KeyboardPressed(GameAction a) => KeyboardControl(a)?.wasPressedThisFrame ?? false;
    public static bool PadPressed(GameAction a) => PadControl(a)?.wasPressedThisFrame ?? false;
    public static bool PadHeld(GameAction a) => PadControl(a)?.isPressed ?? false;

    // ---------------- 顯示名稱 ----------------

    /// <summary>畫面上顯示的按鍵名稱（例如 Space、左鍵、A）。usePad = 顯示手把的</summary>
    public static string DisplayName(GameAction a, bool usePad) => PathName(usePad ? PadPath(a) : KeyboardPath(a));

    /// <summary>依照目前在用的裝置顯示（插著手把且沒有限定鍵盤 → 手把按鍵）</summary>
    public static string DisplayName(GameAction a) => DisplayName(a, PadEnabled && Scheme != InputScheme.KeyboardMouse && (Scheme == InputScheme.Gamepad || PadInput.UsingPadRecently));

    public static string PathName(string path)
    {
        if (string.IsNullOrEmpty(path)) return "—";
        int split = path.IndexOf('/');
        string device = split > 0 ? path.Substring(0, split) : "", c = split > 0 ? path.Substring(split + 1) : path;
        if (device == "<Mouse>")
            return c switch { "leftButton" => "滑鼠左鍵", "rightButton" => "滑鼠右鍵", "middleButton" => "滑鼠中鍵",
                              "backButton" => "滑鼠側鍵 1", "forwardButton" => "滑鼠側鍵 2", _ => c };
        if (device == "<Gamepad>")
            return c switch
            {
                "buttonSouth" => "A", "buttonEast" => "B", "buttonWest" => "X", "buttonNorth" => "Y",
                "leftShoulder" => "LB", "rightShoulder" => "RB", "leftTrigger" => "LT", "rightTrigger" => "RT",
                "leftStickPress" => "L3", "rightStickPress" => "R3", "start" => "Start", "select" => "Select",
                "dpad/up" => "十字 ↑", "dpad/down" => "十字 ↓", "dpad/left" => "十字 ←", "dpad/right" => "十字 →",
                _ => c
            };
        var key = Resolve(path) as KeyControl;
        if (key != null && !string.IsNullOrEmpty(key.displayName)) return key.displayName;
        return c.Length == 1 ? c.ToUpperInvariant() : char.ToUpperInvariant(c[0]) + c.Substring(1);
    }

    /// <summary>
    /// 等待改鍵時找「這一幀剛按下的鍵」，回傳通用路徑（找不到 = null）。
    /// forPad = 只找手把；否則只找鍵盤和滑鼠（Esc 保留給取消，不能綁）
    /// </summary>
    public static string ListenForPress(bool forPad)
    {
        if (forPad)
        {
            var p = Gamepad.current;
            if (p == null) return null;
            foreach (var c in p.allControls)
            {
                if (!(c is ButtonControl b) || b.synthetic || !b.wasPressedThisFrame) continue;
                if (b == p.startButton) continue; // Start 固定是暫停
                string rel = RelativePath(b, p);
                if (rel.StartsWith("leftStick/") || rel.StartsWith("rightStick/")) continue; // 搖桿推動不算按鍵
                return "<Gamepad>/" + rel;
            }
            return null;
        }
        var kb = Keyboard.current;
        if (kb != null)
            foreach (var k in kb.allKeys)
                if (k != null && k.wasPressedThisFrame && k.keyCode != Key.Escape) return "<Keyboard>/" + k.name;
        var m = Mouse.current;
        if (m != null)
            foreach (var b in new[] { m.leftButton, m.rightButton, m.middleButton, m.backButton, m.forwardButton })
                if (b != null && b.wasPressedThisFrame) return "<Mouse>/" + b.name;
        return null;
    }

    static string RelativePath(InputControl c, InputDevice device)
    {
        string full = c.path, prefix = device.path + "/";
        return full.StartsWith(prefix) ? full.Substring(prefix.Length) : c.name;
    }
}
