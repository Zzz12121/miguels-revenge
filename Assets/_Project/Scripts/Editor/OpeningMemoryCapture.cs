#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 拍開場的回憶圖（VillageOpening：開新遊戲時閃過以前的沙漠、維京村莊、地下城各一張）：
/// 選單 GeminiGrowUp/拍開場回憶圖（沙漠、維京村莊、地下城），或批次模式 -executeMethod OpeningMemoryCapture.Run。
/// 跟拍漁村空拍圖一樣進 Play 拍（有些東西執行時才擺好、才換上材質；不進 Play 拍會有洋紅色、缺東西）：
/// 打開沙漠進 Play，依序載入三關，每關等 3 秒讓場景擺好，從玩家出生點的背後上方往玩家面向的前方拍一張（玩家本身不拍），
/// 存成 Resources/Opening/Memory_*.png（1920×1080），拍完離開 Play、回到原本開著的場景。
/// 保險：拍照的那一下，URP 畫不出來的材質（舊管線的 shader、壞掉的 shader，畫面上是洋紅色）暫時換成 URP Lit（用原本的貼圖和顏色），
/// 拍完換回去，並在 Console 列出是哪些物件。
/// </summary>
[InitializeOnLoad]
public static class OpeningMemoryCapture
{
    const string Flag = "OpeningMemoryCapture.Pending";
    const string DoneFlag = "OpeningMemoryCapture.Done";
    const string RestoreKey = "OpeningMemoryCapture.Restore";
    public const string OutDir = "Assets/_Project/Resources/Opening";

    /// <summary>(場景名稱, 場景路徑, 存成的檔名, 顯示的名字)</summary>
    public static readonly (string scene, string path, string file, string label)[] Shots =
    {
        ("Planet", "Assets/_Project/Scenes/Planet.unity", "Memory_Desert", "沙漠"),
        ("The_Viking_Village", "Assets/_Project/Scenes/The_Viking_Village.unity", "Memory_Viking", "維京村莊"),
        ("Floor02", "Assets/_Project/Scenes/Floor02.unity", "Memory_Dungeon", "地下城"),
    };

    static OpeningMemoryCapture()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                new GameObject("OpeningMemoryCaptureRunner").AddComponent<OpeningMemoryCaptureRunner>();
            }
            else if (s == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(DoneFlag, false))
            {
                SessionState.SetBool(DoneFlag, false);
                string restore = SessionState.GetString(RestoreKey, "");
                SessionState.EraseString(RestoreKey);
                EditorApplication.delayCall += () => AfterCapture(restore);
            }
        };
    }

    [MenuItem("GeminiGrowUp/拍開場回憶圖（沙漠、維京村莊、地下城）")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[OpeningMemory] 先離開 Play 再拍"); return; }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        // 記下現在開著的場景，拍完回來
        var open = new List<string>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var sc = SceneManager.GetSceneAt(i);
            if (!string.IsNullOrEmpty(sc.path)) open.Add(sc.path);
        }
        SessionState.SetString(RestoreKey, string.Join("|", open));
        EditorSceneManager.OpenScene(Shots[0].path, OpenSceneMode.Single);
        SessionState.SetBool(Flag, true);
        SessionState.SetBool(DoneFlag, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>拍完（Runner 呼叫）：離開 Play，之後在 AfterCapture 匯入圖片、回到原本的場景</summary>
    public static void Finish(int count)
    {
        Debug.Log($"[OpeningMemory] 拍好 {count}/{Shots.Length} 張 → {OutDir}");
        EditorApplication.ExitPlaymode();
    }

    static void AfterCapture(string restore)
    {
        AssetDatabase.Refresh();
        foreach (var shot in Shots)
        {
            string path = $"{OutDir}/{shot.file}.png";
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default;
                ti.mipmapEnabled = false;
                ti.maxTextureSize = 2048;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(restore))
        {
            var paths = restore.Split('|');
            for (int i = 0; i < paths.Length; i++)
                if (File.Exists(paths[i])) EditorSceneManager.OpenScene(paths[i], i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive);
        }
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }
}

/// <summary>Play 裡依序載入三關、各拍一張（OpeningMemoryCapture 建立）</summary>
public class OpeningMemoryCaptureRunner : MonoBehaviour
{
    const int Width = 1920, Height = 1080;
    static readonly ShaderTagId LightMode = new ShaderTagId("LightMode");

    IEnumerator Start()
    {
        DontDestroyOnLoad(gameObject);
        Directory.CreateDirectory(OpeningMemoryCapture.OutDir);
        int count = 0;
        foreach (var (scene, _, file, label) in OpeningMemoryCapture.Shots)
        {
            if (SceneManager.GetActiveScene().name != scene)
            {
                if (!Application.CanStreamedLevelBeLoaded(scene)) { Debug.LogWarning($"[OpeningMemory] {label}：場景 {scene} 不在 Build Settings 裡，沒有拍"); continue; }
                SceneManager.LoadScene(scene);
                yield return null;
            }
            // 等場景擺好（執行時才放的東西、換上的材質、玩家落地）
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            if (SceneManager.GetActiveScene().name != scene) { Debug.LogWarning($"[OpeningMemory] {label}：現在的場景是 {SceneManager.GetActiveScene().name}，沒有拍"); continue; }
            Time.timeScale = 0f;
            bool ok = false;
            yield return Capture($"{OpeningMemoryCapture.OutDir}/{file}.png", label, r => ok = r);
            Time.timeScale = 1f;
            if (ok) count++;
        }
        OpeningMemoryCapture.Finish(count);
    }

    IEnumerator Capture(string path, string label, System.Action<bool> done)
    {
        var player = GameObject.FindWithTag("Player");
        var main = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        Transform p = player != null ? player.transform : null;
        Vector3 eye, look;
        if (p != null)
        {
            Vector3 fwd = p.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f && main != null) { fwd = main.transform.forward; fwd.y = 0f; }
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 head = p.position + Vector3.up * 1.6f;
            // 往後退 4.5 m、抬高 1.4 m；後面有牆（地下城）就退到牆前面
            float back = 4.5f;
            foreach (var hit in Physics.SphereCastAll(head, 0.3f, -fwd, back, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.collider.transform.IsChildOf(p.root) && hit.distance > 0f) back = Mathf.Min(back, Mathf.Max(0.4f, hit.distance - 0.3f));
            eye = head - fwd * back + Vector3.up * 1.4f;
            look = head + fwd * 22f - Vector3.up * 1.2f;
        }
        else if (main != null)
        {
            eye = main.transform.position;
            look = eye + main.transform.forward * 10f;
        }
        else
        {
            Debug.LogWarning($"[OpeningMemory] {label}：找不到玩家也找不到攝影機，沒有拍");
            done(false);
            yield break;
        }

        // 玩家不拍；URP 畫不出來的材質（洋紅色）暫時換成 URP Lit
        var hidden = new List<Renderer>();
        if (p != null)
            foreach (var r in p.root.GetComponentsInChildren<Renderer>(true))
                if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }
        var swapped = new List<(Renderer r, Material[] mats)>();
        var made = new Dictionary<Material, Material>();
        var extra = new List<Material>();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var names = new List<string>();
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
        {
            if (r.forceRenderingOff || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var mats = r.sharedMaterials;
            bool any = false;
            foreach (var m in mats) if (Pink(m)) { any = true; break; }
            if (!any) continue;
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer || lit == null)
            {
                r.forceRenderingOff = true;   // 特效畫不出來的話乾脆不拍
                hidden.Add(r);
            }
            else
            {
                swapped.Add((r, mats));
                var fixedMats = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (!Pink(m)) { fixedMats[i] = m; continue; }
                    Material f;
                    if (m == null) { f = Fallback(null, lit); extra.Add(f); }   // 沒有材質：每次做一個灰的
                    else if (!made.TryGetValue(m, out f)) { f = Fallback(m, lit); made[m] = f; }
                    fixedMats[i] = f;
                }
                r.sharedMaterials = fixedMats;
            }
            if (names.Count < 20) names.Add(r.name);
        }
        if (names.Count > 0) Debug.Log($"[OpeningMemory] {label}：這些物件的材質 URP 畫不出來（洋紅色），拍照時暫時換成 URP Lit / 不拍：{string.Join("、", names)}");
        // GPU Resident Drawer 一幀才吃到一次 Renderer 的改動
        yield return null;
        yield return null;

        var go = new GameObject("OpeningMemoryCamera");
        var cam = go.AddComponent<Camera>();
        if (main != null) cam.CopyFrom(main);
        cam.enabled = false;
        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1500f);
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        bool ok = false;
        try
        {
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[OpeningMemory] 拍好 {label}：鏡頭 {eye} → {path}");
            ok = true;
        }
        finally
        {
            cam.targetTexture = null;
            foreach (var (r, mats) in swapped) if (r != null) r.sharedMaterials = mats;
            foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
            foreach (var m in made.Values) Destroy(m);
            foreach (var m in extra) Destroy(m);
            Destroy(go);
            Destroy(rt);
            Destroy(tex);
        }
        done(ok);
    }

    /// <summary>URP 畫不出來的材質：沒有材質、shader 壞掉，或 shader 裡沒有 URP 會畫的 pass（舊管線的 Standard 之類）</summary>
    static bool Pink(Material m)
    {
        if (m == null) return true;
        var sh = m.shader;
        if (sh == null || !sh.isSupported || sh.name == "Hidden/InternalErrorShader") return true;
        for (int i = 0; i < sh.passCount; i++)
        {
            string mode = sh.FindPassTagValue(i, LightMode).name;
            if (string.IsNullOrEmpty(mode) || mode == "UniversalForward" || mode == "UniversalForwardOnly" || mode == "SRPDefaultUnlit" || mode == "Universal2D")
                return false;
        }
        return true;
    }

    /// <summary>用原本的貼圖、顏色做一個 URP Lit 的替身</summary>
    static Material Fallback(Material src, Shader lit)
    {
        var m = new Material(lit) { name = (src != null ? src.name : "Missing") + " (OpeningMemory)" };
        Color c = new Color(0.55f, 0.52f, 0.48f);
        if (src != null)
        {
            Texture tex = src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (src.HasProperty("_Color")) c = src.GetColor("_Color");
            else if (src.HasProperty("_BaseColor")) c = src.GetColor("_BaseColor");
            else if (tex != null) c = Color.white;
        }
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", 0.2f);
        return m;
    }
}
#endif
