#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Kenney 素材的自動匯入設定（放進對應資料夾就會自動套用，不用手動調）：
///
/// Retro Fantasy Kit（Assets/_Project/ThirdParty/Kenney/RetroFantasyKit）
///   - 模型原本 1 格 = 1m，玩家有 2m 高，放大成 1 格 = 4m（ModelScale）
///   - 貼圖用 Point 過濾，保留像素風的銳利感
///   - 自動產生 Mesh Collider，玩家可以直接踩上去 / 被牆擋住
///
/// Fantasy UI Borders（Assets/_Project/ThirdParty/Kenney/FantasyUIBorders）
///   - 轉成 Sprite，並設定九宮格（9-slice）邊界，邊框拉多大都不會變形
///   - 原圖是白色，在 Image 上改 Color 就能染成任何顏色
///
/// 改了這裡的數值後，對資料夾按右鍵 → Reimport 才會重新套用。
/// </summary>
public class KenneyImportSettings : AssetPostprocessor
{
    const string KitDir = "Assets/_Project/ThirdParty/Kenney/RetroFantasyKit";
    const string UiDir = "Assets/_Project/ThirdParty/Kenney/FantasyUIBorders";
    const float ModelScale = 4f;

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(KitDir)) return;
        var imp = (ModelImporter)assetImporter;
        imp.globalScale = ModelScale;
        imp.useFileScale = true;
        imp.bakeAxisConversion = true;
        imp.addCollider = true;                 // Mesh Collider
        imp.importAnimation = false;
        imp.animationType = ModelImporterAnimationType.None;
        imp.importCameras = false;
        imp.importLights = false;
    }

    void OnPreprocessTexture()
    {
        var imp = (TextureImporter)assetImporter;
        if (assetPath.StartsWith(KitDir))
        {
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
        }
        else if (assetPath.StartsWith(UiDir))
        {
            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.filterMode = FilterMode.Point;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.spritePixelsPerUnit = 100f;
            // 九宮格：四個角大約佔 1/3（48px 的圖 = 16px、96px 的圖 = 32px）。分隔線不切。
            int w = PngWidth(assetPath);
            if (!assetPath.Contains("/Divider/") && w > 0)
            {
                float b = Mathf.Round(w / 3f);
                imp.spriteBorder = new Vector4(b, b, b, b);
            }
        }
    }

    /// <summary>直接讀 PNG 檔頭拿寬度（匯入前還沒有 Texture 可以問）</summary>
    static int PngWidth(string path)
    {
        try
        {
            using (var f = System.IO.File.OpenRead(path))
            {
                var b = new byte[24];
                if (f.Read(b, 0, 24) < 24) return 0;
                return (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            }
        }
        catch { return 0; }
    }
}
#endif
