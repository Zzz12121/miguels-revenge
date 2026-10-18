#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 打包（Windows exe + Mac app）
///
/// 用 Build Settings 裡的場景（主選單 + 三關）打包到專案根目錄的 Builds 資料夾：
///   Builds/Windows/MiguelsRevenge.exe
///   Builds/Mac/MiguelsRevenge.app
/// Builds 資料夾在 .gitignore 裡，不會被 commit。也可以用指令列：-executeMethod BuildScript.BuildAll
/// </summary>
public static class BuildScript
{
    const string ProductName = "MiguelsRevenge";

    [MenuItem("GeminiGrowUp/打包（Windows exe + Mac app）")]
    public static void BuildAll()
    {
        BuildWindows();
        BuildMac();
    }

    public static void BuildWindows() =>
        Build(BuildTarget.StandaloneWindows64, $"Builds/Windows/{ProductName}.exe");

    public static void BuildMac() =>
        Build(BuildTarget.StandaloneOSX, $"Builds/Mac/{ProductName}.app");

    static void Build(BuildTarget target, string path)
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
        {
            Debug.LogError("[Build] Build Settings 裡沒有場景，請先執行 GeminiGrowUp > 設定關卡流程");
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = target,
            options = BuildOptions.None,
        });
        var s = report.summary;
        Debug.Log($"[Build] {target}：{s.result}，{s.totalErrors} 個錯誤，大小 {s.totalSize / (1024f * 1024f):0.0} MB，" +
                  $"花了 {s.totalTime.TotalMinutes:0.0} 分鐘 → {path}");
        if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
    }
}
#endif
