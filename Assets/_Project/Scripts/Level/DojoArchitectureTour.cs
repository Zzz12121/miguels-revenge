using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Free-flight inspection camera for the standalone dojo architecture preview.
/// Attached only to its preview Camera; no player/combat/camera gameplay wiring.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class DojoArchitectureTour : MonoBehaviour
{
    [Header("Preview controls")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 8f;
    [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
    [SerializeField, Min(1f)] private float fastMultiplier = 3f;
    [SerializeField, Range(0.05f, 1f)] private float precisionMultiplier = 0.25f;
    [SerializeField] private bool startAtEntrance = true;
    [SerializeField] private bool showControls = true;
    [SerializeField] private Transform architectureRoot;

    private float yaw;
    private float pitch;
    private bool looking;
    private bool waitForRightRelease;
    private bool cutaway;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private GameObject[] roofGroups;
    private bool[] roofStates;
    private GUIStyle textStyle;
    private Font controlsFont;

    public float MoveSpeed => moveSpeed;
    public bool IsCutaway => cutaway;
    public bool IsLooking => looking;

    private void OnEnable()
    {
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        looking = false;
        waitForRightRelease = false;
        if (architectureRoot == null)
        {
            GameObject arena = GameObject.Find("FinalDojoArena");
            if (arena != null) architectureRoot = arena.transform.Find("Architecture");
        }
        CacheRoofs();
        if (startAtEntrance) Visit(2);
        else SyncAngles();
    }

    private void CacheRoofs()
    {
        var groups = new System.Collections.Generic.List<GameObject>();
        if (architectureRoot != null)
        {
            foreach (Transform child in architectureRoot.GetComponentsInChildren<Transform>(true))
                if (child.name == "RemovableRoofs" || child.name == "RemovableCeiling")
                    groups.Add(child.gameObject);
        }
        roofGroups = groups.ToArray();
        roofStates = new bool[roofGroups.Length];
        for (int i = 0; i < roofGroups.Length; i++) roofStates[i] = roofGroups[i].activeSelf;
        cutaway = false;
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            ReleaseLook();
            waitForRightRelease = true;
        }
        if (!mouse.rightButton.isPressed)
        {
            ReleaseLook();
            waitForRightRelease = false;
        }

        bool justCaptured = false;
        if (mouse.rightButton.isPressed && !looking && !waitForRightRelease)
        {
            SyncAngles();
            looking = true;
            justCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (looking && !justCaptured) RotateView(mouse.delta.ReadValue());

        Vector3 input = new Vector3(
            (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f),
            (keyboard.eKey.isPressed ? 1f : 0f) - (keyboard.qKey.isPressed ? 1f : 0f),
            (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f));
        MoveView(input, Time.unscaledDeltaTime,
            keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed,
            keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed);

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            moveSpeed = Mathf.Clamp(moveSpeed * Mathf.Pow(1.2f, scroll / 120f), 1f, 40f);

        if (keyboard.f1Key.wasPressedThisFrame) Visit(1);
        if (keyboard.f2Key.wasPressedThisFrame) Visit(2);
        if (keyboard.f3Key.wasPressedThisFrame) Visit(3);
        if (keyboard.f4Key.wasPressedThisFrame) Visit(4);
        if (keyboard.hKey.wasPressedThisFrame) SetCutaway(!cutaway);
        if (keyboard.tabKey.wasPressedThisFrame) showControls = !showControls;
    }

    // Public inspection primitives also allow deterministic validation without
    // injecting input into the user's physical keyboard or gameplay controls.
    public void MoveView(Vector3 input, float deltaSeconds, bool fast, bool precise)
    {
        input = Vector3.ClampMagnitude(input, 1f);
        Vector3 direction = transform.right * input.x + Vector3.up * input.y + transform.forward * input.z;
        direction = Vector3.ClampMagnitude(direction, 1f);
        float speed = moveSpeed * (fast ? fastMultiplier : 1f) * (precise ? precisionMultiplier : 1f);
        transform.position += direction * speed * Mathf.Clamp(deltaSeconds, 0f, 0.1f);
    }

    public void RotateView(Vector2 pixels)
    {
        yaw += pixels.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - pixels.y * mouseSensitivity, -85f, 85f);
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    public void Visit(int view)
    {
        Vector3 position;
        Vector3 target;
        switch (view)
        {
            case 1: position = new Vector3(64f, 32f, 38f); target = new Vector3(0f, 5.5f, 87f); break;
            case 3: position = new Vector3(0f, 3.0f, 82f); target = new Vector3(0f, 3.0f, 96f); break;
            case 4: position = new Vector3(13.1f, 2.8f, 86.5f); target = new Vector3(14f, 1.8f, 90f); break;
            default: position = new Vector3(0f, 2.7f, 74f); target = new Vector3(0f, 3.0f, 88f); break;
        }
        // Building bookmarks follow the architecture's placement if the whole
        // compound is moved/rotated later; all positions are compound-local.
        if (architectureRoot != null)
        {
            position = architectureRoot.TransformPoint(position);
            target = architectureRoot.TransformPoint(target);
        }
        transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position, Vector3.up));
        SyncAngles();
    }

    public void SetCutaway(bool enabled)
    {
        if (roofGroups == null) return;
        cutaway = enabled;
        for (int i = 0; i < roofGroups.Length; i++)
            if (roofGroups[i] != null) roofGroups[i].SetActive(enabled ? false : roofStates[i]);
    }

    private void SyncAngles()
    {
        yaw = transform.eulerAngles.y;
        pitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
    }

    private void ReleaseLook()
    {
        if (!looking) return;
        looking = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) return;
        ReleaseLook();
        waitForRightRelease = true;
    }

    private void OnDisable()
    {
        SetCutaway(false);
        looking = false;
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
        if (controlsFont != null) Destroy(controlsFont);
        controlsFont = null;
        textStyle = null;
    }

    private void OnGUI()
    {
        if (!showControls) return;
        if (textStyle == null)
        {
            controlsFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft JhengHei", "Noto Sans CJK TC", "Arial" }, 15);
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, font = controlsFont, wordWrap = true };
            textStyle.normal.textColor = Color.white;
        }
        float width = Mathf.Min(820f, Screen.width - 24f);
        GUI.Box(new Rect(12f, 12f, width, 122f), GUIContent.none);
        GUI.Label(new Rect(24f, 19f, width - 24f, 25f), "道場自由參觀（可穿牆）  |  請先點一下 Game 畫面", textStyle);
        GUI.Label(new Rect(24f, 45f, width - 24f, 25f), "按住滑鼠右鍵轉視角 · WASD 移動 · Q/E 下降/上升 · Shift 加速 · Ctrl 慢速", textStyle);
        GUI.Label(new Rect(24f, 70f, width - 24f, 25f), "F1 總覽 · F2 入口 · F3 大廳 · F4 茶室 · H 隱藏屋頂/天花板 · Esc 放開滑鼠", textStyle);
        GUI.Label(new Rect(24f, 96f, width - 24f, 25f), "滾輪調速度：" + moveSpeed.ToString("F1") + " m/s  |  Tab 隱藏操作提示", textStyle);
    }
}
