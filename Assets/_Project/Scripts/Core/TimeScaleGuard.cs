using UnityEngine;

/// <summary>
/// 時間加速保護（自動執行，不用在場景裡放東西）：
/// 遊戲裡只有「頓幀之後的抽刀加速」會把時間調到比 1 快（1.7 倍、0.07 秒）。
/// 好幾個系統（頓幀、慢動作、大招、技能瞄準）都會「記下現在的時間倍率、結束時還回去」，
/// 剛好在抽刀加速那一下記到 1.7，還回去之後整個遊戲就一直是 1.7 倍（幻影分身突進兩刀同時砍中時特別容易發生）。
/// 這裡每幀檢查：時間倍率比 1 快、而且持續超過 maxFastSeconds 秒（真實時間），就改回 1 並在 Console 留一行警告。
/// </summary>
public sealed class TimeScaleGuard : MonoBehaviour
{
    /// <summary>比 1 快的時間倍率最多允許維持幾秒（真實時間）</summary>
    public static float MaxFastSeconds = 0.5f;

    static TimeScaleGuard instance;
    float fastSince = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("TimeScaleGuard");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<TimeScaleGuard>();
    }

    void Update()
    {
        if (Time.timeScale <= 1.01f) { fastSince = -1f; return; }
        if (fastSince < 0f) { fastSince = Time.unscaledTime; return; }
        if (Time.unscaledTime - fastSince < MaxFastSeconds) return;
        Debug.LogWarning($"[TimeScaleGuard] 時間倍率卡在 {Time.timeScale:0.00} 倍超過 {MaxFastSeconds} 秒，已改回 1");
        Time.timeScale = 1f;
        fastSince = -1f;
    }
}
