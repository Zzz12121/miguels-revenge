using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全遊戲共用的音效 / 音樂播放器（不用放進場景，第一次播放時會自動建立）。
///
/// 音效檔放在 Assets/_Project/Resources/Audio/ 底下，用路徑名稱載入，例如：
///   GameAudio.Play("SFX/rock_hit", 位置);
///   GameAudio.PlayMusic(GameAudio.Clip("Music/boss_fight_music"), 0.5f, 1.5f);
///
/// 各元件（Boss、玩家、小怪、大門…）上也有 AudioClip 欄位，拖別的音效進去就會改用那個。
/// </summary>
public static class GameAudio
{
    /// <summary>全部音效的總音量</summary>
    public static float SfxVolume = 1f;
    /// <summary>音樂的總音量</summary>
    public static float MusicVolume = 1f;

    const int PoolSize = 32;

    static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
    static readonly HashSet<string> missing = new HashSet<string>();
    static GameAudioHost host;

    // ---------------- 載入 ----------------

    /// <summary>載入 Resources/Audio/ 底下的音效（例如 "SFX/rock_hit"），找不到回傳 null（只警告一次）</summary>
    public static AudioClip Clip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (cache.TryGetValue(name, out var c) && c != null) return c;
        c = Resources.Load<AudioClip>("Audio/" + name);
        if (c != null) cache[name] = c;
        else if (missing.Add(name)) Debug.LogWarning($"[GameAudio] 找不到音效 Resources/Audio/{name}");
        return c;
    }

    /// <summary>從幾個名稱裡隨機挑一個</summary>
    public static AudioClip RandomClip(params string[] names)
    {
        if (names == null || names.Length == 0) return null;
        return Clip(names[Random.Range(0, names.Length)]);
    }

    /// <summary>欄位有指定音效就用欄位的，沒有就用預設名稱</summary>
    public static AudioClip Pick(AudioClip overrideClip, string fallback) => overrideClip != null ? overrideClip : Clip(fallback);

    public static AudioClip Pick(AudioClip[] overrides, params string[] fallbacks)
    {
        if (overrides != null && overrides.Length > 0)
        {
            var c = overrides[Random.Range(0, overrides.Length)];
            if (c != null) return c;
        }
        return RandomClip(fallbacks);
    }

    // ---------------- 音效 ----------------

    /// <summary>
    /// 在某個位置播放音效。
    /// spatial：0 = 不管距離都一樣大聲（像 UI），1 = 完全 3D（離越遠越小聲）。
    /// maxDistance：超過這個距離就聽不到（3D 音效才有用）。
    /// </summary>
    public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f,
                                     float spatial = 1f, float maxDistance = 40f)
    {
        if (clip == null || volume <= 0f) return null;
        var src = Host().GetSource();
        src.transform.position = position;
        src.clip = clip;
        src.volume = Mathf.Clamp01(volume * SfxVolume);
        src.pitch = pitch;
        src.spatialBlend = spatial;
        src.minDistance = Mathf.Min(3f, maxDistance * 0.2f);
        src.maxDistance = maxDistance;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.loop = false;
        src.Play();
        return src;
    }

    /// <summary>用名稱播放，pitchJitter = 音高隨機浮動的幅度（避免同一個音效聽起來很機械）</summary>
    public static AudioSource Play(string name, Vector3 position, float volume = 1f, float pitch = 1f,
                                   float pitchJitter = 0.05f, float spatial = 1f, float maxDistance = 40f)
    {
        return PlayAt(Clip(name), position, volume, pitch * (1f + Random.Range(-pitchJitter, pitchJitter)), spatial, maxDistance);
    }

    // ---------------- 音樂 ----------------

    /// <summary>播放背景音樂（循環），fadeIn 秒淡入。正在播同一首就不會重頭開始</summary>
    public static void PlayMusic(AudioClip clip, float volume = 0.5f, float fadeIn = 1f)
    {
        if (clip == null) return;
        Host().PlayMusic(clip, volume, fadeIn);
    }

    /// <summary>停止背景音樂，fadeOut 秒淡出</summary>
    public static void StopMusic(float fadeOut = 1f)
    {
        if (host != null) host.StopMusic(fadeOut);
    }

    static GameAudioHost Host()
    {
        if (host == null)
        {
            var go = new GameObject("GameAudio");
            Object.DontDestroyOnLoad(go);
            host = go.AddComponent<GameAudioHost>();
            host.Init(PoolSize);
        }
        return host;
    }
}
