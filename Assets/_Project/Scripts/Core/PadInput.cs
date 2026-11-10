using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// 遊戲手把的固定操作（不能改鍵的部分）和瞄準準心。可以改鍵的動作（跳躍、攻擊、互動……）在 InputBindings。
///
///   左搖桿 移動          右搖桿 轉鏡頭（技能瞄準時改成移動準心）
///   A / ×  對話下一句、選單確認      B / ○  取消瞄準、離開商店
///   Start  暫停          十字鍵 ← ↑ → = 商店 / 能力植物的選項 1 2 3
/// 預設可改鍵的手把按鍵：A 跳躍、B 翻滾、X 攻擊、Y 互動、LB 衝刺、RB 牽引、LT 格擋、
///   十字鍵 ← ↑ → ↓ 技能 1~4、R3 治癒藥水、L3 精力藥水、Select 小地圖
/// 設定選單選「只用鍵盤滑鼠」時，這裡全部回傳沒按。
/// </summary>
public static class PadInput
{
    public static Gamepad Pad => InputBindings.PadEnabled ? Gamepad.current : null;
    /// <summary>有插手把、而且設定允許用手把</summary>
    public static bool Connected => Pad != null;

    const float StickDeadZone = .18f;

    static bool Down(ButtonControl b) => b != null && b.wasPressedThisFrame;

    // ---------------- 移動 / 鏡頭 ----------------

    /// <summary>左搖桿（已扣掉死區，長度 0~1）</summary>
    public static Vector2 Move => Stick(Pad?.leftStick.ReadValue() ?? Vector2.zero);
    /// <summary>右搖桿（已扣掉死區，長度 0~1）</summary>
    public static Vector2 Look => Stick(Pad?.rightStick.ReadValue() ?? Vector2.zero);

    static Vector2 Stick(Vector2 v)
    {
        float m = v.magnitude;
        if (m < StickDeadZone) return Vector2.zero;
        return v / m * Mathf.Clamp01((m - StickDeadZone) / (1f - StickDeadZone));
    }

    // ---------------- 固定按鍵 ----------------

    public static bool PausePressed => Down(Pad?.startButton);
    /// <summary>對話 / 卷軸 / 選單：下一句、確認</summary>
    public static bool ConfirmPressed => Down(Pad?.buttonSouth);
    /// <summary>取消瞄準、離開選單</summary>
    public static bool CancelPressed => Down(Pad?.buttonEast);

    /// <summary>十字鍵 ← ↑ → ↓ 對應 1 2 3 4（商店、能力植物的選項）。沒按回傳 0</summary>
    public static int NumberPressed
    {
        get
        {
            var p = Pad;
            if (p == null) return 0;
            if (p.dpad.left.wasPressedThisFrame) return 1;
            if (p.dpad.up.wasPressedThisFrame) return 2;
            if (p.dpad.right.wasPressedThisFrame) return 3;
            if (p.dpad.down.wasPressedThisFrame) return 4;
            return 0;
        }
    }

    /// <summary>這一幀有沒有用手把（按鍵或推搖桿）—— 用來在手把操作時自動鎖定滑鼠游標</summary>
    public static bool AnyPressed
    {
        get
        {
            var p = Pad;
            if (p == null) return false;
            if (p.buttonSouth.wasPressedThisFrame || p.buttonEast.wasPressedThisFrame || p.buttonWest.wasPressedThisFrame ||
                p.buttonNorth.wasPressedThisFrame || p.leftShoulder.wasPressedThisFrame || p.rightShoulder.wasPressedThisFrame ||
                p.leftTrigger.wasPressedThisFrame || p.rightTrigger.wasPressedThisFrame) return true;
            return Move.sqrMagnitude > 0f || Look.sqrMagnitude > 0f;
        }
    }

    // ---------------- 最近在用哪一種裝置 ----------------

    static int trackFrame = -1;
    static bool usingPad;

    /// <summary>最近一次操作是用手把（不是鍵盤滑鼠）：瞄準準心、畫面上的按鍵提示都照手把顯示</summary>
    public static bool UsingPadRecently { get { Track(); return usingPad; } }

    static void Track()
    {
        if (trackFrame == Time.frameCount) return;
        trackFrame = Time.frameCount;
        if (InputBindings.Scheme == InputScheme.Gamepad) { usingPad = Pad != null; }
        else if (Pad == null) usingPad = false;
        else
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if ((mouse != null && (mouse.delta.ReadValue().sqrMagnitude > 4f || mouse.leftButton.wasPressedThisFrame))
                || (kb != null && kb.anyKey.wasPressedThisFrame))
                usingPad = false;
            if (AnyPressed || Down(Pad.startButton) || Pad.dpad.ReadValue().sqrMagnitude > 0f) usingPad = true;
        }
        UpdatePointer();
    }

    // ---------------- 瞄準用的虛擬準心 ----------------
    // 技能 / 牽引瞄準原本用滑鼠指著目標；用手把時改成右搖桿推動一個畫面上的準心。

    const float PointerSpeed = 1.1f;   // 每秒移動幾個「畫面高度」
    static Vector2 virtualPointer;
    static Vector2 virtualDelta;

    /// <summary>瞄準準心跟著右搖桿（最近用的是手把）</summary>
    public static bool UsingPadPointer => UsingPadRecently;

    /// <summary>開始瞄準時把準心放回畫面正中央</summary>
    public static void CenterPointer()
    {
        virtualPointer = new Vector2(Screen.width * .5f, Screen.height * .5f);
        virtualDelta = Vector2.zero;
    }

    static void UpdatePointer()
    {
        virtualDelta = Look * (PointerSpeed * InputBindings.LookSensitivity * Screen.height * Time.unscaledDeltaTime);
        virtualPointer += virtualDelta;
        virtualPointer.x = Mathf.Clamp(virtualPointer.x, 0f, Screen.width);
        virtualPointer.y = Mathf.Clamp(virtualPointer.y, 0f, Screen.height);
    }

    static Mouse PointerMouse => InputBindings.KeyboardEnabled ? Mouse.current : null;

    /// <summary>瞄準準心的畫面座標：用手把 = 虛擬準心，用滑鼠 = 滑鼠位置</summary>
    public static Vector2 PointerPosition
    {
        get
        {
            Track();
            if (usingPad || PointerMouse == null) return virtualPointer;
            return PointerMouse.position.ReadValue();
        }
    }

    /// <summary>準心這一幀移動多少（像素）</summary>
    public static Vector2 PointerDelta
    {
        get
        {
            Track();
            if (usingPad || PointerMouse == null) return virtualDelta;
            return PointerMouse.delta.ReadValue();
        }
    }

    /// <summary>瞄準的確認鍵：滑鼠左鍵 / 手把的「攻擊」鍵（預設 X）</summary>
    public static bool PointerPressed => (PointerMouse?.leftButton.wasPressedThisFrame ?? false) || InputBindings.PadPressed(GameAction.Attack);
    public static bool PointerHeld => (PointerMouse?.leftButton.isPressed ?? false) || InputBindings.PadHeld(GameAction.Attack);
    public static bool PointerReleased => (PointerMouse?.leftButton.wasReleasedThisFrame ?? false)
        || (Pad != null && (InputBindings.Resolve(InputBindings.PadPath(GameAction.Attack))?.wasReleasedThisFrame ?? false));

    /// <summary>畫出手把準心（瞄準中、用手把時呼叫，放在 OnGUI 裡）</summary>
    public static void DrawPointer()
    {
        if (!UsingPadPointer) return;
        Vector2 p = virtualPointer;
        float y = Screen.height - p.y; // GUI 座標 y 朝下
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, .6f);
        GUI.DrawTexture(new Rect(p.x - 13f, y - 2f, 26f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(p.x - 2f, y - 13f, 4f, 26f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(p.x - 11f, y - 1f, 22f, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(p.x - 1f, y - 11f, 2f, 22f), Texture2D.whiteTexture);
        GUI.color = old;
    }
}
