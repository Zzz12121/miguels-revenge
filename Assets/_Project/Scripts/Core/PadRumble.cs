using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 手把震動（PS4 / Xbox 都適用）：打中敵人、被打到、用盾擋下攻擊時震一下。
/// 不用在場景裡放東西：第一次載入場景就自動開始，每個場景自己找玩家（PlayerHealth / PlayerCombat）掛上事件。
///   - 打中敵人：PlayerCombat.OnDealtHit（近戰、盾擊、箭）；技能的傷害由技能自己呼叫 Hit()
///   - 被打到：PlayerHealth.OnDamaged（越痛震越大）
///   - 擋下：PlayerCombat.OnBlockedHit（破防震比較大）
/// 暫停（timeScale = 0）、視窗失去焦點、設定裡關掉震動、沒在用手把時都不震；結束時一定把馬達歸零，不會一直震。
/// 震動用 unscaled time，技能慢動作中長度不變。
///
/// PS4 手把用藍牙連線時：Unity 內建的 DualShock 4 震動只會送 USB 格式的指令（report 0x05），藍牙連線的手把收不到、不會震。
/// 這裡自己送藍牙格式的指令（report 0x11 + CRC32，DualShock4Bluetooth），USB 連線、Xbox 手把照樣用內建的 SetMotorSpeeds。
/// </summary>
public sealed class PadRumble : MonoBehaviour
{
    static PadRumble host;

    // 兩顆馬達：low = 左邊的低頻（重、沉），high = 右邊的高頻（細、脆）
    float low, high, until, startedAt, length;
    float lastHitPulse = float.NegativeInfinity;
    Gamepad rumbling;   // 正在被這裡震的手把（null = 沒在震；閒置時不要一直去歸零，免得蓋掉別的震動）
    PlayerHealth player;
    PlayerCombat combat;
    float nextFind;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null || Application.isBatchMode) return;
        var go = new GameObject("PadRumble");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        host = go.AddComponent<PadRumble>();
    }

    // ---------------- 給其他系統呼叫 ----------------

    /// <summary>震一下：low / high = 兩顆馬達的強度（0~1），seconds = 多久（會慢慢減弱）</summary>
    public static void Pulse(float lowMotor, float highMotor, float seconds)
    {
        if (host == null) return;
        host.Add(lowMotor, highMotor, seconds);
    }

    /// <summary>打中敵人（技能連續打很多下時，間隔太短的會合併，不會變成一直震）</summary>
    public static void Hit(float strength = 1f)
    {
        if (host == null) return;
        if (Time.unscaledTime - host.lastHitPulse < .045f) return;
        host.lastHitPulse = Time.unscaledTime;
        strength = Mathf.Clamp(strength, .3f, 1.6f);
        host.Add(.22f * strength, .42f * strength, .07f + .03f * strength);
    }

    // ---------------- 內部 ----------------

    void Add(float lowMotor, float highMotor, float seconds)
    {
        if (!Allowed()) return;
        float now = Time.unscaledTime;
        // 正在震的時候：取比較強的那個，長度延到比較晚結束的那個
        float remain = Mathf.Max(0f, until - now);
        float fade = length > 0f ? remain / length : 0f;
        low = Mathf.Max(low * fade, Mathf.Clamp01(lowMotor));
        high = Mathf.Max(high * fade, Mathf.Clamp01(highMotor));
        length = Mathf.Max(remain, seconds);
        startedAt = now;
        until = now + length;
        // 馬上開始震（不等下一幀 Update）：打中的那一幀如果卡了一下（第一次載入血花特效之類），震動才不會整個被吃掉
        var pad = PadInput.Pad;
        if (pad != null)
        {
            if (rumbling != null && rumbling != pad) Motors(rumbling, 0f, 0f);
            rumbling = pad;
            Motors(pad, low, high, true);
        }
    }

    static bool Allowed() => InputBindings.RumbleEnabled && PadInput.Connected && Time.timeScale > 0f && Application.isFocused;

    void OnEnable() { SceneManager.sceneLoaded += OnSceneLoaded; }
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; Unhook(); Stop(); }
    void OnApplicationFocus(bool focus) { if (!focus) Stop(); }
    void OnApplicationQuit() => Stop();
    void OnSceneLoaded(Scene s, LoadSceneMode m) { Stop(); Unhook(); nextFind = 0f; }

    void Update()
    {
        if (player == null && Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1f; Hook(); }

        var pad = PadInput.Pad;
        float now = Time.unscaledTime;
        if (pad == null || now >= until || !Allowed()) { if (rumbling != null) Stop(); return; }
        if (pad != rumbling) { if (rumbling != null) Motors(rumbling, 0f, 0f, true); rumbling = pad; }   // 換了手把：舊的停掉
        // 越後面越弱（平方衰減：一開始很實、尾巴很快收掉）
        float t = Mathf.Clamp01((now - startedAt) / Mathf.Max(.001f, length));
        float k = (1f - t) * (1f - t);
        Motors(pad, low * k, high * k);
    }

    void Stop()
    {
        low = high = 0f; until = 0f; length = 0f;
        if (rumbling != null) Motors(rumbling, 0f, 0f, true);
        rumbling = null;
    }

    void Hook()
    {
        Unhook();
        player = PlayerHealth.Find();
        if (player == null) return;
        combat = player.GetComponent<PlayerCombat>();
        player.OnDamaged += OnPlayerDamaged;
        if (combat != null) { combat.OnDealtHit += OnDealtHit; combat.OnBlockedHit += OnBlockedHit; }
    }

    void Unhook()
    {
        if (player != null) player.OnDamaged -= OnPlayerDamaged;
        if (combat != null) { combat.OnDealtHit -= OnDealtHit; combat.OnBlockedHit -= OnBlockedHit; }
        player = null; combat = null;
    }

    void OnDealtHit(float damage, IDamageable target) => Hit(damage >= 40f ? 1.4f : 1f);

    void OnPlayerDamaged(float amount)
    {
        if (amount < .05f) return;   // 動作預覽之類的 0.01 傷害不震
        // 扣掉最大血量的比例越多震越大：小傷 ~0.45、扣三成以上就是全力
        float max = player != null ? Mathf.Max(1f, player.maxHealth) : 100f;
        float s = Mathf.Lerp(.45f, 1f, Mathf.Clamp01(amount / max / .3f));
        Add(.75f * s, .45f * s, .18f + .14f * s);
    }

    void OnBlockedHit(bool broke) => Add(broke ? .7f : .35f, broke ? .5f : .25f, broke ? .3f : .1f);

    // ---------------- 送到馬達 ----------------

    float sentLow = -1f, sentHigh = -1f, sentAt;

    /// <summary>設定兩顆馬達。變化很小時不重送（藍牙頻寬有限），force = 一定送</summary>
    void Motors(Gamepad pad, float lowMotor, float highMotor, bool force = false)
    {
        if (pad == null) return;
        bool zero = lowMotor <= .001f && highMotor <= .001f;
        if (!force && !zero && Mathf.Abs(lowMotor - sentLow) < .04f && Mathf.Abs(highMotor - sentHigh) < .04f && Time.unscaledTime - sentAt < .25f) return;
        if (!force && zero && sentLow == 0f && sentHigh == 0f) return;
        sentLow = zero ? 0f : lowMotor; sentHigh = zero ? 0f : highMotor; sentAt = Time.unscaledTime;
        if (DualShock4Bluetooth.Is(pad)) DualShock4Bluetooth.SetMotors(pad, sentLow, sentHigh);
        else if (zero) pad.ResetHaptics();
        else pad.SetMotorSpeeds(sentLow, sentHigh);
    }
}
