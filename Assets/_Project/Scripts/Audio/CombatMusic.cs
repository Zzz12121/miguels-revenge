using UnityEngine;

/// <summary>
/// 一般戰鬥的音樂（不用在場景裡放東西，自動執行，每一關都有）：
///   - 跟敵人打起來（CombatLog.InFight：打中敵人或被敵人打到）就換成緊湊的戰鬥曲（Music/bgm_battle_clock），
///     打完（對手都倒下，或 7 秒沒再交手）過 1.5 秒換回這一關的背景音樂
///   - 練習（木人、跟僧侶過招）不換；Boss 戰自己有音樂，已經在播別的戰鬥曲 / Boss 曲時不搶
/// </summary>
public sealed class CombatMusic : MonoBehaviour
{
    public const string TrackPath = "Music/bgm_battle_clock";
    const float Volume = 0.55f, EndDelay = 1.5f;

    static CombatMusic host;
    bool ours;
    float quietSince = -1f;
    AudioClip track;
    PlayerHealth player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        var go = new GameObject("CombatMusic");
        DontDestroyOnLoad(go);
        host = go.AddComponent<CombatMusic>();
        go.AddComponent<LowHealthHeartbeat>();
    }

    void Update()
    {
        if (player == null && Time.frameCount % 30 == 0) player = PlayerHealth.Find();
        var hp = player;
        bool fighting = hp != null && !hp.IsDead && CombatLog.InFight;
        var current = GameAudio.CurrentMusic;

        if (ours)
        {
            // 別人（Boss）換了音樂：交給他，不再管
            if (current != null && track != null && current != track) { ours = false; return; }
            if (fighting) { quietSince = -1f; return; }
            if (quietSince < 0f) quietSince = Time.unscaledTime;
            if (Time.unscaledTime - quietSince < EndDelay) return;
            ours = false;
            quietSince = -1f;
            BattleMusic.End();
            return;
        }

        if (!fighting || BattleMusic.Playing) return;
        // 已經在播戰鬥曲 / Boss 曲（名字有 battle、boss、fight）：不搶
        if (current != null)
        {
            string n = current.name.ToLowerInvariant();
            if (n.Contains("battle") || n.Contains("boss") || n.Contains("fight")) return;
        }
        track ??= GameAudio.Clip(TrackPath);
        if (track == null) return;
        BattleMusic.Begin(track, Volume, 1f, TrackPath);
        ours = true;
        quietSince = -1f;
    }
}

/// <summary>
/// 血量低於 20% 時聽得到自己的心跳（不用在場景裡放東西）：「咚、咚」一下一下，血越少跳得越快、越大聲；
/// 補血到 20% 以上或倒下就停。心跳聲是程式合成的（兩下很低的悶響）
/// </summary>
public sealed class LowHealthHeartbeat : MonoBehaviour
{
    public const float Threshold = 0.2f;
    static AudioClip beat;
    float nextBeat;
    PlayerHealth player;

    void Update()
    {
        if (player == null && Time.frameCount % 30 == 0) player = PlayerHealth.Find(); // 換場景後重新找（不要每幀找）
        var hp = player;
        if (hp == null || hp.IsDead || hp.maxHealth <= 0f || GameUI.OverlayOpen) return;
        float ratio = hp.Health / hp.maxHealth;
        if (ratio >= Threshold || ratio <= 0f) { nextBeat = 0f; return; }
        if (Time.unscaledTime < nextBeat) return;
        float danger = 1f - ratio / Threshold; // 0 = 剛好 20%、1 = 快沒血了
        beat ??= MakeBeat();
        GameAudio.PlayAt(beat, hp.transform.position, Mathf.Lerp(0.55f, 0.9f, danger), 1f, 0f, 50f);
        nextBeat = Time.unscaledTime + Mathf.Lerp(0.95f, 0.55f, danger);
    }

    /// <summary>心跳：「咚」（55 Hz）、0.16 秒後一下比較輕的「咚」（48 Hz），都很快衰減，加一點點悶悶的雜音</summary>
    static AudioClip MakeBeat()
    {
        const int rate = 22050;
        int n = (int)(rate * 0.5f);
        var data = new float[n];
        var rng = new System.Random(3);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate, v = 0f;
            foreach (var (t0, f, a) in new[] { (0f, 55f, 1f), (0.16f, 48f, 0.7f) })
            {
                float bt = t - t0;
                if (bt < 0f || bt > 0.25f) continue;
                float env = Mathf.Min(1f, bt / 0.008f) * Mathf.Exp(-bt * 22f);
                float pitch = f * (1f + 0.6f * Mathf.Exp(-bt * 40f)); // 一開始音高高一點，像「咚」
                v += Mathf.Sin(2f * Mathf.PI * pitch * bt) * env * a;
            }
            lp += ((float)(rng.NextDouble() * 2.0 - 1.0) - lp) * 0.05f;
            data[i] = Mathf.Clamp(v * 0.9f + lp * 0.15f * Mathf.Exp(-t * 6f), -1f, 1f);
        }
        var clip = AudioClip.Create("Heartbeat", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
