using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 測試模式快捷鍵（自動執行，不用在場景裡放東西；每個場景都能用）：
///   （兩個都要「連按兩下」才會作用，避免誤觸）
///   P P：鎖血 開 / 關。鎖住時被打還是有受擊反應、擊退，但血量不會往下掉，也不會被打死
///      （掉出地圖這種直接死亡不擋）
///   O O：補血，補最大生命的 50%
/// 只在 Unity 編輯器和 Development Build 裡有效，正式版不會有這些按鍵。
/// 只改 PlayerHealth 的血量（用它原本的 Heal / SetPracticeProtection），不動戰鬥、受傷流程。
/// </summary>
public sealed class PlayerTestMode : MonoBehaviour
{
    /// <summary>O 一次補多少（最大生命的比例）</summary>
    public static float HealFraction = 0.5f;
    /// <summary>兩下之間最多隔幾秒才算「連按兩下」</summary>
    public static float DoublePressWindow = 0.4f;

    public static bool HealthLocked { get; private set; }

    static PlayerTestMode instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; HealthLocked = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null || Application.isBatchMode || !Debug.isDebugBuild) return;
        var go = new GameObject("PlayerTestMode");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<PlayerTestMode>();
    }

    PlayerHealth player;
    float lockedHealth;
    float nextFind;
    string toast;
    float toastUntil;
    GUIStyle style;
    float lastP = float.NegativeInfinity, lastO = float.NegativeInfinity;

    void Update()
    {
        if (player == null && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 0.5f;
            player = PlayerHealth.Find();
            if (player != null && HealthLocked) Apply();   // 換場景 / 重生後換了新的玩家：繼續鎖
        }
        if (player == null) return;

        var kb = Keyboard.current;
        if (kb != null && !GameUI.OverlayOpen)
        {
            if (kb.pKey.wasPressedThisFrame && DoublePressed(ref lastP))
            {
                HealthLocked = !HealthLocked;
                Apply();
                Toast(HealthLocked ? "測試：鎖血 開" : "測試：鎖血 關");
            }
            if (kb.oKey.wasPressedThisFrame && DoublePressed(ref lastO) && !player.IsDead)
            {
                float before = player.Health;
                player.Heal(player.maxHealth * HealFraction);
                if (HealthLocked) lockedHealth = player.Health;
                Toast($"測試：補血 +{player.Health - before:0}（{player.Health:0}/{player.maxHealth:0}）");
            }
        }
    }

    /// <summary>這一下跟上一下隔得夠近 = 連按兩下（用掉之後要重新按兩下）</summary>
    static bool DoublePressed(ref float last)
    {
        float now = Time.unscaledTime;
        if (now - last <= DoublePressWindow) { last = float.NegativeInfinity; return true; }
        last = now;
        return false;
    }

    void LateUpdate()
    {
        if (!HealthLocked || player == null || player.IsDead) return;
        // 補血、喝藥水等等讓血變多：鎖在新的血量；被打掉的部分補回來
        if (player.Health > lockedHealth) lockedHealth = player.Health;
        else if (player.Health < lockedHealth) player.Heal(lockedHealth - player.Health);
    }

    void Apply()
    {
        if (player == null) return;
        player.SetPracticeProtection(this, HealthLocked);   // 被打最多剩 1 滴（同一幀內就補回去），不會被打死
        lockedHealth = player.Health;
    }

    void OnDisable()
    {
        if (player != null) player.SetPracticeProtection(this, false);
    }

    void Toast(string text)
    {
        toast = text;
        toastUntil = Time.unscaledTime + 1.5f;
    }

    void OnGUI()
    {
        if (player == null) return;
        style ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        float y = 104f;
        if (HealthLocked) { Shadowed(new Rect(20f, y, 400f, 22f), "測試模式：鎖血中（連按兩下 P 關閉、連按兩下 O 補血 50%）", new Color(1f, .85f, .3f)); y += 20f; }
        if (Time.unscaledTime < toastUntil) Shadowed(new Rect(20f, y, 400f, 22f), toast, new Color(.85f, 1f, .85f));
    }

    void Shadowed(Rect r, string text, Color c)
    {
        style.normal.textColor = new Color(0f, 0f, 0f, .8f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }
}
