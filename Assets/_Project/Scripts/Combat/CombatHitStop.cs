using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>One bounded owner for confirmed-impact time dilation. Other clocks take priority.</summary>
[DefaultExecutionOrder(-10000)]
public sealed class CombatHitStop : MonoBehaviour
{
    static CombatHitStop instance;
    float startedAt, endsAt, nextAllowedAt;
    float appliedScale;
    bool ownsTime;

    public static bool Active => instance && instance.ownsTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public static void Request(float seconds, float scale = .05f, float cooldown = .12f)
    {
        if (seconds <= 0f || Time.timeScale <= 0f || GameUI.OverlayOpen) return;
        if (!instance)
        {
            var host = new GameObject("Combat impact clock");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<CombatHitStop>();
        }
        instance.Begin(Mathf.Clamp(seconds, 0f, .12f), Mathf.Clamp(scale, .01f, 1f), cooldown);
    }

    void Begin(float seconds, float scale, float cooldown)
    {
        float now = Time.unscaledTime;
        if (ownsTime && Mathf.Approximately(Time.timeScale, appliedScale))
        {
            // Upgrade simultaneous contacts without extending a rapid multi-hit indefinitely.
            endsAt = Mathf.Min(startedAt + .12f, Mathf.Max(endsAt, now + seconds));
            return;
        }
        if (now < nextAllowedAt || !Mathf.Approximately(Time.timeScale, 1f)) return;
        startedAt = now;
        endsAt = now + seconds;
        nextAllowedAt = endsAt + Mathf.Max(0f, cooldown);
        appliedScale = scale;
        ownsTime = true;
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
        if (Time.unscaledTime >= endsAt) Cancel();
    }

    void OnEnable() => SceneManager.activeSceneChanged += SceneChanged;
    void OnDisable()
    {
        SceneManager.activeSceneChanged -= SceneChanged;
        Cancel();
    }
    void SceneChanged(Scene oldScene, Scene newScene) => Cancel();
}
