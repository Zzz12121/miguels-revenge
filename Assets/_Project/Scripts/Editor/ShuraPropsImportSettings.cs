#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Models/ShuraProps 資料夾的匯入設定（修羅場的日式道具，由 Tripo 模型拆分、減面而來）：
///   - 模型：不自動產生材質（統一用 M_ShuraProps 這個 URP 材質）、不需要動畫
///   - 貼圖：上限 2048；名稱含 Normal 的當作法線貼圖
/// </summary>
public class ShuraPropsImportSettings : AssetPostprocessor
{
    const string Folder = "Assets/_Project/Models/ShuraProps/";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.animationType = ModelImporterAnimationType.None;
        mi.importCameras = false;
        mi.importLights = false;
        mi.importNormals = ModelImporterNormals.Import;
        mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Low;
        mi.isReadable = false;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        ti.maxTextureSize = 2048;
        ti.mipmapEnabled = true;
        if (assetPath.Contains("Normal"))
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.sRGBTexture = false;
        }
    }
}
#endif
