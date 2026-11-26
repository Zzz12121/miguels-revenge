using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One bounded owner for confirmed-impact time dilation. Other clocks take priority.
/// 刀砍進血肉時多兩段（Request 的 enter / exit）：砍進去先慢下來（BladeEnterScale）→ 卡住（scale）→ 抽出來再慢一段（BladeExitScale），
/// 砍進去、抽出來各比平常多花約 0.05 秒。三段都由這裡管，不會跟別的時間控制互搶。
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class CombatHitStop : MonoBehaviour
{
    // 砍進去：實際 0.07 秒只走 0.02 秒；抽出來：實際 0.1 秒只走 0.05 秒
    public const float BladeEnterSeconds = .07f, BladeEnterScale = .28f;
    public const float BladeExitSeconds = .1f, BladeExitScale = .5f;

    static CombatHitStop instance;
    float startedAt, endsAt, nextAllowedAt;
    float appliedScale;
    bool ownsTime;
    // 0 = 砍進去、1 = 卡住、2 = 抽出來
    int phase;
    float holdSeconds, holdScale, exitSeconds;

    public static bool Active => instance && instance.ownsTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    /// <param name="enter">先慢下來多久（真實秒數，BladeEnterScale）；0 = 直接卡住</param>
    /// <param name="exit">卡住之後再慢多久（真實秒數，BladeExitScale）；0 = 直接回到正常</param>
    public static void Request(float seconds, float scale = .05f, float cooldown = .12f, float enter = 0f, float exit = 0f)
    {
        if (seconds <= 0f || Time.timeScale <= 0f || GameUI.OverlayOpen) return;
        if (!instance)
        {
            var host = new GameObject("Combat impact clock");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CombatHitStop>();
        }
        instance.Begin(Mathf.Clamp(seconds, 0f, .12f), Mathf.Clamp(scale, .01f, 1f), cooldown, Mathf.Clamp(enter, 0f, .1f), Mathf.Clamp(exit, 0f, .15f));
    }

    void Begin(float seconds, float scale, float cooldown, float enter, float exit)
    {
        float now = Time.unscaledTime;
        if (ownsTime && Mathf.Approximately(Time.timeScale, appliedScale))
        {
            // Upgrade simultaneous contacts without extending a rapid multi-hit indefinitely.
            if (phase == 0) holdSeconds = Mathf.Max(holdSeconds, seconds);
            else if (phase == 1) endsAt = Mathf.Min(startedAt + .12f, Mathf.Max(endsAt, now + seconds));
            return;
        }
        if (now < nextAllowedAt || !Mathf.Approximately(Time.timeScale, 1f)) return;
        holdSeconds = seconds;
        holdScale = scale;
        exitSeconds = exit;
        nextAllowedAt = now + enter + seconds + exit + Mathf.Max(0f, cooldown);
        ownsTime = true;
        if (enter > 0f) EnterPhase(0, now, enter, BladeEnterScale);
        else EnterPhase(1, now, seconds, scale);
    }

    void EnterPhase(int p, float now, float duration, float scale)
    {
        phase = p;
        startedAt = now;
        endsAt = now + duration;
        appliedScale = scale;
        Time.timeScale = appliedScale;
    }

    /// <summary>Call before a menu or cinematic saves its previous time scale.</summary>
    public static float CaptureTimeScale()
    {
        Cancel();
        ParrySlowMotion.Cancel();
        return Time.timeScale;
    }

    public static void Cancel()
    {
        if (!instance || !instance.ownsTime) return;
        if (Mathf.Approximately(Time.timeScale, instance.appliedScale)) Time.timeScale = 1f;
        instance.ownsTime = false;
    }

    void Update()
    {
        if (!ownsTime) return;
        if (!Mathf.Approximately(Time.timeScale, appliedScale))
        {
            // A pause, targeting mode or cinematic has taken ownership.
            ownsTime = false;
            return;
        }
        float now = Time.unscaledTime;
        if (now < endsAt) return;
        if (phase == 0) EnterPhase(1, now, holdSeconds, holdScale);                 // 砍進去 → 卡住
        else if (phase == 1 && exitSeconds > 0f) EnterPhase(2, now, exitSeconds, BladeExitScale); // 卡住 → 抽出來
        else Cancel();
    }

    void OnEnable() => SceneManager.activeSceneChanged += SceneChanged;
    void OnDisable()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        Cancel();
    }
    void SceneChanged(Scene oldScene, Scene newScene) => Cancel();
}
