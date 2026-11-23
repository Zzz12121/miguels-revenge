using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 書法系統（遊墨行者 / 墨相）、半血逃走、雙極能量牽引的音效，集中在這裡（只加聲音，不改任何招式的時間或判定）。
/// 大部分招式透過兩個共用的入口自動有聲音：
///   CalligraphyVfxLibrary.Play → OnVfx（依特效種類：飛墨、近戰筆刷、墨光、墨浪、落印、墨龍、爆炸…）
///   CalligraphyInkField.Burst  → OnBurst（墨水炸開的潑濺聲，越大團越大聲）
/// 其他時間點（墨光發射、落印砸地、墨龍俯衝、變身、逃走、牽引）由各腳本呼叫下面的方法。
/// 音效檔：Resources/Audio/SFX/Ink/（100 Retro Magic Sound Effects 的水 / 雷 / 吸收 / 光，剪短、淡出、轉單聲道）
///         以及專案原本就有的 SFX/Gen、SFX/Magic、SFX/RPG、dragon_roar。
/// </summary>
public static class CalligraphyAudio
{
    const string Ink = "SFX/Ink/";
    static readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

    /// <summary>同一種聲音在 gap 秒內只播一次（一次生出很多特效 / 墨水時不會疊得很大聲）</summary>
    static AudioSource Play(string key, string clip, Vector3 at, float volume, float pitch, float gap = .06f,
                            float jitter = .05f, float spatial = 1f, float maxDistance = 55f)
    {
        float now = Time.unscaledTime;
        if (lastPlayed.TryGetValue(key, out float t) && now - t < gap) return null;
        lastPlayed[key] = now;
        return GameAudio.Play(clip, at, volume, pitch, jitter, spatial, maxDistance);
    }

    // ---------------- 共用入口 ----------------

    public static void OnVfx(CalligraphyVfxLibrary lib, GameObject prefab, Vector3 at, float scale)
    {
        if (lib == null || prefab == null) return;
        if (prefab == lib.projectile)
        {
            // 飛墨一筆：筆尖甩出一道墨
            Play("flick", Ink + "ink_flick", at, .55f, 1.15f, .05f, .08f);
        }
        else if (prefab == lib.stab || prefab == lib.cLight || prefab == lib.cHeavy || prefab == lib.circleLight || prefab == lib.circleHeavy)
        {
            // 近戰筆刷：低沉的揮動風壓 + 甩墨
            bool heavy = prefab == lib.cHeavy || prefab == lib.circleHeavy;
            Play("brushSwing", "SFX/Gen/swing_whoosh_" + Random.Range(1, 4), at, heavy ? .85f : .7f, heavy ? .62f : .72f, .05f);
            Play("brushInk", Ink + "ink_flick", at, .4f, .85f, .05f, .08f);
        }
        else if (prefab == lib.impact)
        {
            // 玩家被墨打中（命中特效）：墨水濺開（玩家本身的受擊聲由 PlayerHealth 播）
            Play("hitInk", Ink + "ink_splash_s", at, .55f, 1.1f, .08f, .08f);
        }
        else if (prefab == lib.beam)
        {
            // 交叉運筆・墨光：先聚墨蓄力（約 3 秒後由 BeamFire 發射）
            Play("beamCharge", Ink + "ink_gather", at, .75f, 1.05f, .3f, 0f);
        }
        else if (prefab == lib.smoke)
        {
            // 天降墨龍：腳下墨煙升起
            Play("dragonSmoke", "SFX/Magic/dark_aura", at, .7f, .8f, .5f);
            Play("dragonGather", Ink + "ink_gather", at, .6f, .8f, .5f, 0f);
        }
        else if (prefab == lib.bigExplosion)
        {
            // 墨龍砸地爆炸
            Play("bigBoom", "SFX/Magic/void_explosion", at, 1f, .85f, .3f);
            Play("bigSlam", "SFX/Magic/earth_slam", at, .8f, .8f, .3f);
            Play("bigInk", Ink + "ink_splash_l", at, .8f, .8f, .3f);
        }
        else if (prefab == lib.frontal || prefab == lib.frontalPulses)
        {
            // 連環墨浪 / 墨相砸地的浪：墨水像浪一樣推出去
            bool light = prefab == lib.frontalPulses;
            Play("wave", Ink + "ink_wave", at, light ? .6f : .85f, light ? 1.15f : .85f, .2f);
            if (!light) Play("waveRumble", Ink + "ink_rumble", at, .5f, 1.1f, .3f, .05f);
        }
        else if (prefab == lib.rune)
        {
            // 點墨落印：地上出現印記（預警）
            Play("runeWarn", Ink + "ink_rune", at, .5f, 1.05f, .12f);
        }
        else if (prefab == lib.verticalExplosion)
        {
            // 遊墨行者倒下：整個人化成墨爆開
            Play("deathBurst", "SFX/Magic/void_burst", at, .9f, .8f, 1f);
            Play("deathBoom", "SFX/Magic/void_explosion", at, .8f, .7f, 1f);
            Play("deathInk", Ink + "ink_splash_l", at, .8f, .7f, 1f);
        }
        else if (prefab == lib.pulse)
        {
            // 畫中世界：腳下的墨圈脈動
            Play("pulse", Ink + "ink_rune", at, .45f, .8f, .3f, .03f, .5f);
        }
    }

    /// <summary>墨水炸開（strength：0.5 小團、1 普通、3 以上很大團）</summary>
    public static void OnBurst(Vector3 at, float strength)
    {
        if (strength >= 2f) Play("burstL", Ink + "ink_splash_l", at, Mathf.Clamp(.45f + strength * .1f, .5f, 1f), Mathf.Lerp(1f, .75f, (strength - 2f) / 4f), .15f);
        else Play("burstS", Ink + "ink_splash_s", at, Mathf.Clamp(.25f + strength * .3f, .3f, .7f), 1.25f - strength * .2f, .07f, .08f);
    }

    // ---------------- 書法招式 ----------------

    /// <summary>墨光真正射出去（聚墨結束）</summary>
    public static void BeamFire(Vector3 at)
    {
        Play("beamFire", Ink + "ink_beam", at, .75f, .9f, .5f, 0f);
        Play("beamWash", Ink + "ink_splash_l", at, .55f, .9f, .5f);
    }

    /// <summary>落印砸到地上</summary>
    public static void RuneImpact(Vector3 at)
    {
        Play("runeHit", "SFX/Gen/hit_heavy_" + Random.Range(1, 3), at, .7f, .8f, .05f);
        Play("runeInk", Ink + "ink_splash_s", at, .6f, .85f, .05f);
    }

    /// <summary>躍筆重砸：起跳 / 砸地</summary>
    public static void LeapJump(Vector3 at) => Play("leap", "SFX/Gen/jump_whoosh_" + Random.Range(1, 3), at, .8f, .7f, .2f);
    public static void LeapImpact(Vector3 at)
    {
        Play("leapSlam", "SFX/Magic/earth_slam", at, .95f, .9f, .2f);
        Play("leapHit", "SFX/Gen/hit_heavy_2", at, .8f, .75f, .2f);
    }

    /// <summary>墨龍開始俯衝（giant = 天降巨龍）</summary>
    public static void DragonDive(Vector3 at, bool giant)
    {
        Play("dragonRoar", "SFX/dragon_roar", at, giant ? .8f : .5f, giant ? .75f : 1f, .4f, .05f, giant ? .6f : 1f, 90f);
        Play("dragonWind", "SFX/Magic/dash_burst", at, giant ? .7f : .45f, .7f, .2f);
    }

    /// <summary>閃避時留下墨分身、本體往後滑開</summary>
    public static void Evade(Vector3 at)
    {
        Play("evade", "SFX/Magic/wind_short", at, .6f, .9f, .15f);
        Play("evadeClone", "SFX/Magic/clone_appear", at, .45f, 1.1f, .15f);
    }

    /// <summary>瞬移到玩家背後（出發 / 出現）</summary>
    public static void Teleport(Vector3 at) => Play("teleport", "SFX/Magic/wind_short", at, .55f, 1.15f, .1f);

    // ---------------- 墨相鎮海（大招） ----------------

    public static void AvatarRoar(Vector3 at)
    {
        Play("avatarRoar", Ink + "ink_rumble", at, 1f, .8f, 1f, 0f, .6f, 90f);
        Play("avatarPower", "SFX/Magic/power_up", at, .8f, .7f, 1f, 0f, .6f, 90f);
        Play("avatarDark", "SFX/Magic/dark_aura", at, .7f, .7f, 1f, 0f, .6f, 90f);
    }
    public static void AvatarSweep(Vector3 at)
    {
        Play("avatarSweep", "SFX/Magic/sword_spin_swell", at, .7f, 1.3f, .5f, 0f, .6f, 90f);
        Play("avatarSweepWind", "SFX/Gen/swing_whoosh_3", at, 1f, .5f, .5f, 0f, .6f, 90f);
    }
    public static void AvatarSlam(Vector3 at)
    {
        Play("avatarSlam", "SFX/Magic/void_explosion", at, 1f, .75f, .5f, 0f, .7f, 90f);
        Play("avatarSlamEarth", "SFX/Magic/earth_slam", at, 1f, .7f, .5f, 0f, .7f, 90f);
    }
    public static void AvatarRise(Vector3 at) => Play("avatarRise", Ink + "ink_rumble", at, .7f, 1.2f, .5f, 0f, .5f, 90f);
    public static void AvatarDissolve(Vector3 at) => Play("avatarFade", Ink + "ink_gather", at, .6f, .7f, 1f, 0f, .5f, 90f);
    public static void Pillar(Vector3 at) => Play("pillar", Ink + "ink_splash_s", at, .45f, .7f, .15f, .1f);

    // ---------------- 畫中世界 ----------------

    public static void PaintingEnter(Vector3 at)
    {
        Play("paintIn", Ink + "ink_gather", at, .8f, .75f, 1f, 0f, .3f);
        Play("paintWash", Ink + "ink_splash_l", at, .6f, .7f, 1f, 0f, .3f);
    }
    public static void PaintingExit(Vector3 at) => Play("paintOut", Ink + "ink_shimmer", at, .55f, .9f, 1f, 0f, .3f);

    // ---------------- 變身（喝完酒變成遊墨行者） ----------------

    public static void FlaskThrow(Vector3 at) => Play("flask", "SFX/Gen/swing_whoosh_2", at, .6f, 1.1f, .3f);
    public static void FlaskLand(Vector3 at) => Play("flaskLand", Ink + "ink_splash_s", at, .5f, 1.3f, .3f);
    public static void BrushReveal(Vector3 at, float seconds)
    {
        // 墨從胸口流到手上、組成巨筆（ink_gather 長 2.8 秒，配合組成的時間調音高）
        Play("brushGather", Ink + "ink_gather", at, .75f, Mathf.Clamp(2.8f / Mathf.Max(.5f, seconds), .7f, 1.5f), 1f, 0f);
    }
    public static void BrushComplete(Vector3 at) => Play("brushDone", Ink + "ink_shimmer", at, .65f, 1f, 1f, 0f);

    // ---------------- 半血逃走 ----------------

    public static void EscapeTwist(Vector3 at)
    {
        Play("escTwist", Ink + "ink_gather", at, .8f, 1.6f, 1f, 0f, .6f, 70f);
        Play("escDark", "SFX/Magic/dark_aura", at, .6f, 1f, 1f, 0f, .6f, 70f);
    }
    public static void EscapeBurst(Vector3 at)
    {
        Play("escBurst", Ink + "ink_splash_l", at, .8f, 1f, 1f, 0f, .6f, 70f);
        Play("escWind", "SFX/Magic/dash_burst", at, .6f, .9f, 1f, 0f, .6f, 70f);
    }

    /// <summary>掛在會移動的物件上、一直循環的聲音（物件刪掉或 StopLoop 時停）</summary>
    public static AudioSource AttachLoop(GameObject host, string clip, float volume, float pitch, float spatial = 1f, float maxDistance = 60f)
    {
        if (host == null) return null;
        var c = GameAudio.Clip(clip);
        if (c == null) return null;
        var src = host.AddComponent<AudioSource>();
        src.clip = c; src.loop = true; src.playOnAwake = false;
        src.volume = Mathf.Clamp01(volume * GameAudio.SfxVolume); src.pitch = pitch;
        src.spatialBlend = spatial; src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = Mathf.Min(4f, maxDistance * .2f); src.maxDistance = maxDistance;
        src.Play();
        return src;
    }

    // ---------------- 雙極能量牽引（玩家技能） ----------------

    public static void TetherAim(Vector3 at) => Play("tAim", Ink + "tether_slow", at, .55f, 1f, .3f, 0f, 0f);
    public static void TetherLock(Vector3 at) => Play("tLock", Ink + "tether_zap", at, .5f, 1.25f, .15f, .05f, 0f);
    public static void TetherPull(Vector3 at)
    {
        Play("tPull", Ink + "tether_zap", at, .7f, .95f, .1f, .05f, .3f);
        Play("tPullWind", "SFX/Magic/dash_burst", at, .5f, 1.1f, .1f, .05f, .3f);
    }
    public static void TetherThrow(Vector3 at)
    {
        Play("tThrow", Ink + "tether_throw", at, .75f, 1f, .1f, .05f, .3f);
        Play("tThrowWind", "SFX/Gen/swing_whoosh_3", at, .7f, .8f, .1f, .05f, .3f);
    }
    public static void TetherLand(Vector3 at) => Play("tLand", "SFX/Gen/jump_land_heavy", at, .55f, .9f, .1f);
}
