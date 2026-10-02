#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 音效的自動匯入設定（放進 Assets/_Project/Resources/Audio 就會套用）：
///   - Music/：串流播放（不會一次載入整首到記憶體）、Vorbis 壓縮（20MB 的 wav 會變成 2~3MB）
///   - SFX/：載入時解壓縮（播放時不卡）、Vorbis 壓縮、轉單聲道（3D 音效才有方向感）
/// </summary>
public class AudioImportSettings : AssetPostprocessor
{
    const string AudioDir = "Assets/_Project/Resources/Audio";

    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(AudioDir)) return;
        var imp = (AudioImporter)assetImporter;
        bool music = assetPath.Contains("/Music/");

        var s = imp.defaultSampleSettings;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        if (music)
        {
            s.loadType = AudioClipLoadType.Streaming;
            s.quality = 0.6f;
        }
        else
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.quality = 0.7f;
        }
        imp.defaultSampleSettings = s;
        imp.forceToMono = !music;
        imp.loadInBackground = music;
    }
}
#endif
