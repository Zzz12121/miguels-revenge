using UnityEngine;

/// <summary>
/// 戰鬥音樂：開戰時換成戰鬥曲，打完 / 玩家死掉重來時換回這一關的背景音樂（GameManager.PlaySceneMusic）。
/// 用法：BattleMusic.Begin(battleMusic, volume);  BattleMusic.End();
/// clip 不填 = Resources/Audio 底下的 fallback 路徑（預設 Music/boss_fight_music）
/// </summary>
public static class BattleMusic
{
    public const string DefaultPath = "Music/boss_fight_music";

    public static bool Playing { get; private set; }

    public static void Begin(AudioClip clip, float volume = 0.5f, float fadeIn = 1.5f, string fallback = DefaultPath)
    {
        var c = GameAudio.Pick(clip, fallback);
        if (c == null) return;
        GameAudio.PlayMusic(c, volume, fadeIn);
        Playing = true;
    }

    public static void End()
    {
        if (!Playing) return;
        Playing = false;
        if (GameManager.Instance != null) GameManager.Instance.PlaySceneMusic();
        else GameAudio.StopMusic(1.5f);
    }
}
