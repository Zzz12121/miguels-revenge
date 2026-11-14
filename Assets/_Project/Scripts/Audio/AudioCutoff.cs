using UnityEngine;

/// <summary>
/// 把一個正在播放的音效截短：maxSeconds 後在 fadeSeconds 內淡出並停止。
/// 用法：AudioCutoff.PlayShort("SFX/rock_hit", 位置, 音量, 音高, 最長秒數)
/// （專案裡有些碎裂音檔長達 4~6 秒，破碎時只要前段的衝擊聲）
/// </summary>
public class AudioCutoff : MonoBehaviour
{
    AudioSource source;
    AudioClip clip;
    float stopAt, fade, startVolume;

    public static AudioSource PlayShort(string clip, Vector3 position, float volume, float pitch, float maxSeconds,
                                        float fadeSeconds = 0.15f, float pitchJitter = 0.06f, float maxDistance = 45f)
    {
        var src = GameAudio.Play(clip, position, volume, pitch, pitchJitter, 1f, maxDistance);
        if (src == null || maxSeconds <= 0f) return src;
        var cut = src.gameObject.AddComponent<AudioCutoff>();
        cut.source = src;
        cut.clip = src.clip;
        cut.fade = Mathf.Max(0.01f, fadeSeconds);
        cut.stopAt = Time.unscaledTime + maxSeconds;
        cut.startVolume = src.volume;
        return src;
    }

    void Update()
    {
        // GameAudio 的音源是共用池：播完或被拿去播別的音效就不再插手
        if (source == null || !source.isPlaying || source.clip != clip) { Destroy(this); return; }
        float t = Time.unscaledTime - stopAt;
        if (t < 0f) return;
        float k = 1f - t / fade;
        if (k <= 0f)
        {
            source.Stop();
            Destroy(this);
            return;
        }
        source.volume = startVolume * k;
    }
}
