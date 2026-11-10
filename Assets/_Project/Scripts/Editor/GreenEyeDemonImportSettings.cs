#if UNITY_EDITOR
using UnityEditor;

/// <summary>Models/GreenEyeDemon 的匯入設定：模型用 Humanoid（才能套 Mixamo 動畫）、不自動產生材質；貼圖上限 2048。</summary>
public class GreenEyeDemonImportSettings : AssetPostprocessor
{
    const string Folder = "Assets/_Project/Models/GreenEyeDemon/";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.importCameras = false;
        mi.importLights = false;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 2048;
        if (assetPath.Contains("Normal")) ti.textureType = TextureImporterType.NormalMap;
        if (assetPath.Contains("Normal") || assetPath.Contains("MetallicSmoothness")) ti.sRGBTexture = false;
    }
}
#endif
