using UnityEditor;
using UnityEngine;

/// <summary>
/// 主畫面（MainMenu）的編輯器工具：
///   - Resources/MainMenu 的圖（Art/MainMenu/make_menu_art.py 產生）匯入時自動設定：透明邊緣不出黑框、不要 mipmap、主角圖可以到 4096
///   - 選單 GeminiGrowUp/主畫面/直接測試漁村：打勾時從 JapaneseVillage 按 Play 不先跳到主畫面（只影響這台電腦的編輯器）
/// </summary>
public class MainMenuArtTools : AssetPostprocessor
{
    const string Folder = "Assets/_Project/Resources/MainMenu/";
    const string ToggleMenu = "GeminiGrowUp/主畫面/直接測試漁村（不先到主畫面）";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
    }

    [MenuItem(ToggleMenu)]
    static void ToggleSkip()
    {
        bool on = PlayerPrefs.GetInt(MainMenu.SkipVillageRedirectKey, 0) == 1;
        PlayerPrefs.SetInt(MainMenu.SkipVillageRedirectKey, on ? 0 : 1);
        PlayerPrefs.Save();
        Debug.Log(on ? "[MainMenu] 從漁村按 Play 會先到主畫面" : "[MainMenu] 從漁村按 Play 直接進村子（不先到主畫面）");
    }

    [MenuItem(ToggleMenu, true)]
    static bool ToggleSkipValidate()
    {
        Menu.SetChecked(ToggleMenu, PlayerPrefs.GetInt(MainMenu.SkipVillageRedirectKey, 0) == 1);
        return true;
    }
}
