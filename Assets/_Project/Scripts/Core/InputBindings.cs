using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>可以在設定選單裡改按鍵的動作（移動、轉鏡頭、暫停、對話下一句固定不能改）</summary>
public enum GameAction
{
    Jump, Sprint, Roll, Attack, Block, Interact, Tether,
    Skill1, Skill2, Skill3, Skill4, HealPotion, StaminaPotion, Map,
    SwitchWeapon, BatPowder,
    BowAim, AimConfirm   // 新的加在最後面：PlayerPrefs 用名字存，但預設表用順序對應
}

/// <summary>手把配置：PS4（△ 跳、× 翻滾、R2 衝刺…）或 Xbox（原本的 A 跳、B 翻滾、LB 衝刺…）。兩套各自記住改過的按鍵</summary>
public enum PadLayout { PS4, Xbox }

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
        "<Keyboard>/tab", "<Keyboard>/f",
        "<Mouse>/rightButton", "",   // 拉弓瞄準：跟格擋一樣是右鍵（拿弓時才有作用）；瞄準確認：鍵盤固定用滑鼠左鍵
    };
    // 手把預設（括號內是 PS4 的按鍵）：
    //   跳躍 Y（△）、衝刺 RT（R2 按住）、翻滾 A（×）、攻擊 X（□）、格擋 / 盾反 LB（L1）、互動 B（○）、牽引 RB（R1）
    //   技能 1 = 十字 ↑（先選技能 1，再按住 L1 蓄力、R1 發射）；技能 2~4 手把預設不綁（可以在設定裡自己綁）
    //   切換武器 十字 ↓、撒石粉 B（○，跟互動共用）、拉弓瞄準 LT（L2）、瞄準確認 / 射箭 RT（R2）
    static string[] DefaultPad => layout == PadLayout.Xbox ? DefaultPadXbox : DefaultPadPS4;

    // Xbox 配置（原本的手把預設）：A 跳躍、LB 衝刺、B 翻滾、X 攻擊、LT 格擋、Y 互動、RB 牽引、十字 ← ↑ → ↓ 技能 1~4、
    //   R3 治癒藥水、L3 精力藥水、Select 小地圖、RT 切換武器、撒石粉不綁、LT 拉弓瞄準（跟格擋同一顆）、X 瞄準確認 / 射箭（跟攻擊同一顆）
    static readonly string[] DefaultPadXbox =
    {
        "<Gamepad>/buttonSouth", "<Gamepad>/leftShoulder", "<Gamepad>/buttonEast", "<Gamepad>/buttonWest", "<Gamepad>/leftTrigger",
        "<Gamepad>/buttonNorth", "<Gamepad>/rightShoulder",
        "<Gamepad>/dpad/left", "<Gamepad>/dpad/up", "<Gamepad>/dpad/right", "<Gamepad>/dpad/down",
        "<Gamepad>/rightStickPress", "<Gamepad>/leftStickPress", "<Gamepad>/select",
        "<Gamepad>/rightTrigger", "",
        "<Gamepad>/leftTrigger", "<Gamepad>/buttonWest",
    };

    static readonly string[] DefaultPadPS4 =
    {
        "<Gamepad>/buttonNorth", "<Gamepad>/rightTrigger", "<Gamepad>/buttonSouth", "<Gamepad>/buttonWest", "<Gamepad>/leftShoulder",
        "<Gamepad>/buttonEast", "<Gamepad>/rightShoulder",
        "<Gamepad>/dpad/up", "", "", "",
        "<Gamepad>/rightStickPress", "<Gamepad>/leftStickPress", "<Gamepad>/select",
        "<Gamepad>/dpad/down", "<Gamepad>/buttonEast",   // 撒石粉 = ○（跟互動同一顆：旁邊有能互動的東西時先互動）
        "<Gamepad>/leftTrigger", "<Gamepad>/rightTrigger",
    };

    public static string Label(GameAction a) => a switch
    {
        GameAction.Jump => "跳躍", GameAction.Sprint => "衝刺（按住）", GameAction.Roll => "翻滾",
        GameAction.Attack => "攻擊", GameAction.Block => "格擋 / 盾反", GameAction.Interact => "互動",
        GameAction.Tether => "牽引技能", GameAction.Skill1 => "技能 1 虛空連斬", GameAction.Skill2 => "技能 2 貓躍雙斬",
        GameAction.Skill3 => "技能 3 月弧突進", GameAction.Skill4 => "技能 4 空中十字斬",
        GameAction.HealPotion => "治癒藥水", GameAction.StaminaPotion => "精力藥水", GameAction.Map => "小地圖",
        GameAction.SwitchWeapon => "切換武器（刀 / 弓）", GameAction.BatPowder => "撒石粉（擾亂蝙蝠）",
        GameAction.BowAim => "拉弓瞄準（按住）", GameAction.AimConfirm => "瞄準確認 / 射箭",
        _ => a.ToString()
    };

    static string[] keyboard, pad;
    static InputScheme scheme;
    static float lookSensitivity = 1f;
    static bool invertY;
    static bool rumble = true;
    static PadLayout layout = PadLayout.PS4;
    static bool loaded;

    const string Prefs = "Controls.";

    static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        layout = (PadLayout)PlayerPrefs.GetInt(Prefs + "padLayout", (int)PadLayout.PS4);
        keyboard = (string[])DefaultKeyboard.Clone();
        pad = (string[])DefaultPad.Clone();
        for (int i = 0; i < All.Length; i++)
        {
            keyboard[i] = PlayerPrefs.GetString(Prefs + "kb." + All[i], keyboard[i]);
            pad[i] = PlayerPrefs.GetString(PadKey + All[i], pad[i]);
        }
        // 手把配置整個換過一次（第 3 版：△ 跳、× 翻滾、○ 互動、L1 盾反、L2 拉弓、R2 衝刺 / 確認…）：
        // 存檔裡的舊手把按鍵全部換成新預設（鍵盤的不動）
        if (PlayerPrefs.GetInt(Prefs + "padVersion", 1) < 3)
        {
            layout = PadLayout.PS4;
            PlayerPrefs.SetInt(Prefs + "padLayout", (int)layout);
            pad = (string[])DefaultPad.Clone();
            for (int i = 0; i < All.Length; i++) PlayerPrefs.SetString(Prefs + "pad." + All[i], pad[i]);
            PlayerPrefs.SetInt(Prefs + "padVersion", 4);
            PlayerPrefs.Save();
        }
        // 第 4 版：撒石粉預設改成 ○（原本手把沒綁）
        if (PlayerPrefs.GetInt(Prefs + "padVersion", 1) < 4)
        {
            int powder = (int)GameAction.BatPowder;
            if (string.IsNullOrEmpty(pad[powder])) { pad[powder] = DefaultPad[powder]; PlayerPrefs.SetString(Prefs + "pad." + GameAction.BatPowder, pad[powder]); }
            PlayerPrefs.SetInt(Prefs + "padVersion", 4);
            PlayerPrefs.Save();
        }
        scheme = (InputScheme)PlayerPrefs.GetInt(Prefs + "scheme", 0);
        lookSensitivity = PlayerPrefs.GetFloat(Prefs + "look", 1f);
        invertY = PlayerPrefs.GetInt(Prefs + "invertY", 0) == 1;
        rumble = PlayerPrefs.GetInt(Prefs + "rumble", 1) == 1;
    }

    static void Save()
    {
        for (int i = 0; i < All.Length; i++)
        {
            PlayerPrefs.SetString(Prefs + "kb." + All[i], keyboard[i]);
            PlayerPrefs.SetString(PadKey + All[i], pad[i]);
        }
        PlayerPrefs.SetInt(Prefs + "padLayout", (int)layout);
        PlayerPrefs.SetInt(Prefs + "scheme", (int)scheme);
        PlayerPrefs.SetFloat(Prefs + "look", lookSensitivity);
        PlayerPrefs.SetInt(Prefs + "invertY", invertY ? 1 : 0);
        PlayerPrefs.SetInt(Prefs + "rumble", rumble ? 1 : 0);
        PlayerPrefs.Save();
    }

    // ---------------- 設定 ----------------

    public static InputScheme Scheme { get { EnsureLoaded(); return scheme; } set { EnsureLoaded(); scheme = value; Save(); } }
    /// <summary>鏡頭靈敏度倍率（滑鼠和右搖桿都套用）</summary>
    public static float LookSensitivity { get { EnsureLoaded(); return lookSensitivity; } set { EnsureLoaded(); lookSensitivity = Mathf.Clamp(value, .25f, 3f); Save(); } }
    public static bool InvertY { get { EnsureLoaded(); return invertY; } set { EnsureLoaded(); invertY = value; Save(); } }
    /// <summary>手把震動（打中敵人、被打到時）</summary>
    public static bool RumbleEnabled { get { EnsureLoaded(); return rumble; } set { EnsureLoaded(); rumble = value; Save(); } }

    /// <summary>手把配置（PS4 / Xbox）。切換時換成那一套的按鍵（各自記住改過的鍵），按鍵名稱也跟著顯示 ×○□△ 或 ABXY</summary>
    public static PadLayout Layout
    {
        get { EnsureLoaded(); return layout; }
        set
        {
            EnsureLoaded();
            if (value == layout) return;
            Save();   // 先把目前這一套存起來
            layout = value;
            pad = (string[])DefaultPad.Clone();
            for (int i = 0; i < All.Length; i++) pad[i] = PlayerPrefs.GetString(PadKey + All[i], pad[i]);
            PadShouldersReserved = false;
            Save();
        }
    }

    /// <summary>PS4 的按鍵存在 "pad."（沿用原本的存檔），Xbox 存在 "padXbox."</summary>
    static string PadKey => Prefs + (layout == PadLayout.Xbox ? "padXbox." : "pad.");

    /// <summary>手把某顆鍵在目前配置下的名稱，例如 PadName("leftShoulder") = L1 / LB</summary>
    public static string PadName(string control) => PathName("<Gamepad>/" + control);

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
    static ButtonControl PadControl(GameAction a)
    {
        if (!PadEnabled) return null;
        string path = PadPath(a);
        // 技能 1 虛空連斬選好之後，L1 / R1 是它的蓄力 / 發射鍵：綁在 L1 / R1 上的其他動作（盾反、牽引…）暫時不算
        // L1 + R1 一起按住 = 選技能 1：這時候也不算盾反、牽引（不然先按的那顆會順便觸發）
        if ((PadShouldersReserved || PadShouldersChorded) && (path == "<Gamepad>/leftShoulder" || path == "<Gamepad>/rightShoulder")) return null;
        return Resolve(path);
    }

    /// <summary>手把的 L1 / R1 正被技能 1 拿去瞄準 / 發射（VoidRendSkillDemo 設定）</summary>
    public static bool PadShouldersReserved { get; set; }

    /// <summary>手把 L1 和 R1 同時按住（選技能 1 的組合鍵）</summary>
    public static bool PadShouldersChorded
    {
        get
        {
            var pad = PadEnabled ? Gamepad.current : null;
            return pad != null && pad.leftShoulder.isPressed && pad.rightShoulder.isPressed;
        }
    }

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
        if (device == "<Gamepad>" && Layout == PadLayout.PS4)
            return c switch
            {
                "buttonSouth" => "×", "buttonEast" => "○", "buttonWest" => "□", "buttonNorth" => "△",
                "leftShoulder" => "L1", "rightShoulder" => "R1", "leftTrigger" => "L2", "rightTrigger" => "R2",
                "leftStickPress" => "L3", "rightStickPress" => "R3", "start" => "OPTIONS", "select" => "SHARE",
                "dpad/up" => "十字 ↑", "dpad/down" => "十字 ↓", "dpad/left" => "十字 ←", "dpad/right" => "十字 →",
                _ => c
            };
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
