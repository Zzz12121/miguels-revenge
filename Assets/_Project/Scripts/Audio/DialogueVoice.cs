using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// 對話的英文配音：字幕維持中文，同一句話播一段英文語音。
///
/// 語音檔放在 Resources/Audio/Voice/，對照表是 Resources/Audio/Voice/voice_index.txt，
/// 一行一句：「中文台詞的 key ⇥ 語音檔名 ⇥ 英文（給人看的，程式不用）」。
/// key = 台詞去掉空白、開頭的 @（玩家說的話），數字換成 #（「還差 3 個」和「還差 2 個」用同一段語音）。
/// key 裡的 * 代表「這裡的字會變」（例如兵器名稱、按鍵名稱），比對時 * 可以對到任何字。
/// 對照表裡沒有的台詞就不出聲（例如之後新加、還沒產生語音的台詞）。
/// </summary>
public static class DialogueVoice
{
    /// <summary>語音音量</summary>
    public static float Volume = 1f;

    static Dictionary<string, string> index;
    static List<KeyValuePair<string[], string>> patterns; // 有 * 的 key：拆成幾段
    static AudioSource current;
    static AudioClip currentClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { index = null; patterns = null; current = null; currentClip = null; }

    /// <summary>台詞 → 對照表的 key</summary>
    public static string Key(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";
        var sb = new StringBuilder(line.Length);
        bool lastDigit = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (i == 0 && c == '@') continue;
            if (char.IsWhiteSpace(c)) continue;
            bool digit = c >= '0' && c <= '9';
            if (digit) { if (!lastDigit) sb.Append('#'); }
            else sb.Append(c);
            lastDigit = digit;
        }
        return sb.ToString();
    }

    static void Load()
    {
        if (index != null) return;
        index = new Dictionary<string, string>();
        patterns = new List<KeyValuePair<string[], string>>();
        var text = Resources.Load<TextAsset>("Audio/Voice/voice_index");
        if (text == null) return;
        foreach (var raw in text.text.Split('\n'))
        {
            var row = raw.TrimEnd('\r');
            if (row.Length == 0 || row[0] == '#') continue;
            var cols = row.Split('\t');
            if (cols.Length < 2) continue;
            if (cols[0].IndexOf('*') >= 0) patterns.Add(new KeyValuePair<string[], string>(cols[0].Split('*'), cols[1]));
            else index[cols[0]] = cols[1];
        }
    }

    /// <summary>這句台詞有沒有配音</summary>
    public static bool Has(string line) => Find(line) != null;

    /// <summary>找語音檔名：先找完全一樣的，再找有 * 的</summary>
    static string Find(string line)
    {
        Load();
        string key = Key(line);
        if (key.Length == 0) return null;
        if (index.TryGetValue(key, out var name)) return name;
        foreach (var p in patterns)
            if (Matches(key, p.Key)) return p.Value;
        return null;
    }

    static bool Matches(string key, string[] parts)
    {
        const System.StringComparison O = System.StringComparison.Ordinal;
        if (!key.StartsWith(parts[0], O) || !key.EndsWith(parts[parts.Length - 1], O)) return false;
        int at = parts[0].Length, end = key.Length - parts[parts.Length - 1].Length;
        if (end < at) return false;
        for (int i = 1; i < parts.Length - 1; i++)
        {
            int k = key.IndexOf(parts[i], at, end - at, O);
            if (k < 0) return false;
            at = k + parts[i].Length;
        }
        return true;
    }

    /// <summary>
    /// 唸出這句台詞的英文配音（先停掉上一句）。沒有配音回傳 false。
    /// 位置只用來做一點點左右聲道（大部分是 2D），讓對話不會因為鏡頭遠近忽大忽小。
    /// </summary>
    public static bool Speak(string line, Vector3 at)
    {
        Stop();
        string clipName = Find(line);
        if (clipName == null) return false;
        var clip = GameAudio.Clip("Voice/" + clipName);
        if (clip == null) return false;
        current = GameAudio.PlayAt(clip, at, Volume, 1f, 0.15f, 60f);
        currentClip = clip;
        return current != null;
    }

    /// <summary>停掉正在唸的配音（對話結束、跳到下一句時）</summary>
    public static void Stop()
    {
        if (current != null && current.clip == currentClip && current.isPlaying) current.Stop();
        current = null;
        currentClip = null;
    }

    /// <summary>還在唸</summary>
    public static bool Speaking => current != null && current.clip == currentClip && current.isPlaying;
}
