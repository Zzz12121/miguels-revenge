using System.Collections.Generic;
using UnityEngine;

/// <summary>實際播放音效的物件（自動建立，不用手動放）</summary>
public class GameAudioHost : MonoBehaviour
{
    readonly List<AudioSource> pool = new List<AudioSource>();
    int next;
    AudioSource music;
    float musicTarget, musicFadeSpeed;
    bool stopWhenSilent;

    public void Init(int size)
    {
        for (int i = 0; i < size; i++)
        {
            var go = new GameObject("Sfx_" + i);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            pool.Add(s);
        }
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.loop = true;
        music.spatialBlend = 0f;
        music.volume = 0f;
        music.priority = 0;
    }

    /// <summary>找一個空著的 AudioSource；都在用就搶最舊的那個</summary>
    public AudioSource GetSource()
    {
        for (int i = 0; i < pool.Count; i++)
        {
            int k = (next + i) % pool.Count;
            if (pool[k] != null && !pool[k].isPlaying)
            {
                next = (k + 1) % pool.Count;
                return pool[k];
            }
        }
        var s = pool[next];
        next = (next + 1) % pool.Count;
        s.Stop();
        return s;
    }

    public void PlayMusic(AudioClip clip, float volume, float fadeIn)
    {
        if (music.clip != clip || !music.isPlaying)
        {
            music.clip = clip;
            music.volume = 0f;
            music.Play();
        }
        musicTarget = volume;
        musicFadeSpeed = volume / Mathf.Max(fadeIn, 0.01f);
        stopWhenSilent = false;
    }

    public void StopMusic(float fadeOut)
    {
        if (!music.isPlaying) return;
        musicTarget = 0f;
        musicFadeSpeed = Mathf.Max(music.volume, 0.01f) / Mathf.Max(fadeOut, 0.01f);
        stopWhenSilent = true;
    }

    void Update()
    {
        if (music == null || !music.isPlaying) return;
        float target = musicTarget * GameAudio.MusicVolume;
        music.volume = Mathf.MoveTowards(music.volume, target, musicFadeSpeed * Time.unscaledDeltaTime);
        if (stopWhenSilent && music.volume <= 0.0001f)
        {
            music.Stop();
            stopWhenSilent = false;
        }
    }
}
