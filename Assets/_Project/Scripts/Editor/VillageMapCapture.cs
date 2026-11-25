using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 拍漁村地圖的空拍圖（VillageMap 用）：選單 GeminiGrowUp/拍漁村地圖空拍圖，或批次模式
///   Unity.exe -batchmode -projectPath ... -executeMethod VillageMapCapture.Run
/// 打開 JapaneseVillage 進入 Play（村子裡有些東西要執行時才擺好，例如套件模型只留用得到的部分），等幾秒，
/// 找到每個地點（照物件名稱），每一區照地點的範圍算一個長方形，用正上方往下看的正交攝影機各拍一張，
/// 存成 Resources/VillageMap/Map_*.png 和 VillageMap.json（範圍、地點位置），拍完自動離開 Play（批次模式直接關掉 Unity）。
/// 村子的 Builder 重建過、地點換了位置，就再拍一次。
/// </summary>
[InitializeOnLoad]
public static class VillageMapCapture
{
    const string Flag = "VillageMapCapture.Pending";
    public const string ScenePath = "Assets/_Project/Scenes/JapaneseVillage.unity";
    public const string OutDir = "Assets/_Project/Resources/VillageMap";

    static VillageMapCapture()
    {
        EditorApplication.playModeStateChanged += s =>
        {
            if (s == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                SessionState.SetBool(Flag, false);
                new GameObject("VillageMapCaptureRunner").AddComponent<VillageMapCaptureRunner>();
            }
        };
    }

    [MenuItem("GeminiGrowUp/拍漁村地圖空拍圖")]
    public static void Run()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("[VillageMap] 先離開 Play 再拍"); return; }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        SessionState.SetBool(Flag, true);
        // 要留在漁村拍：這次進 Play 不要跳到主畫面
        PlayerPrefs.SetInt(MainMenu.SkipRedirectOnceKey, 1);
        PlayerPrefs.Save();
        EditorApplication.EnterPlaymode();
    }

    /// <summary>拍完：匯入圖片、設定成地圖用的貼圖（不壓縮成太小、不要 mipmap 模糊）</summary>
    public static void Finish(List<string> written)
    {
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () =>
        {
            AssetDatabase.Refresh();
            foreach (var path in written)
            {
                if (!path.EndsWith(".png")) continue;
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
            Debug.Log($"[VillageMap] 完成：{written.Count} 個檔案");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        };
    }
}

public class VillageMapCaptureRunner : MonoBehaviour
{
    // 地點：顯示的名字、場景裡的物件（null = 直接用 at 的位置）、屬於哪一區、固定位置（x, z；物件的支點在原點的用這個）
    static readonly (string label, string obj, string region, Vector2? at)[] Pois =
    {
        ("棧橋", null, "harbor", new Vector2(22f, -62f)),        // 棧橋中間（從海岸道路往南 44 m）
        ("老漁夫（釣魚）", "NPC_Fisherman", "harbor", null),
        ("行商的攤子", "MerchantStall", "harbor", null),
        ("補網的漁婦", "NPC_NetMender", "harbor", null),
        ("茶屋", "TeaBench_1", "town", null),
        ("鐵匠鋪", "Smithy_WeaponRack", "town", null),
        ("藥鋪", "NPC_Apothecary", "town", null),
        ("暫居地", "Residence_Step1", "town", null),
        ("女書生的長椅", "ScholarBench", "town", null),
        ("村長", "NPC_Elder", "chief", null),
        ("往道場的大門", "Portal_Dojo", "chief", null),
        ("石階", null, "forest", new Vector2(0f, 87f)),           // 石階 z 54 ~ 120 的中間（物件的支點在原點）
        ("雙尾虎出沒的空地", "TwoTailedTiger", "forest", null),
        ("雙尾虎的巢穴", "TigerDen", "forest", null),               // 竹林深處，打跑了逃回這裡
        ("砍竹人", "NPC_Woodcutter", "forest", null),
        ("祠堂", "Shrine", "summit", null),
        ("習武場", null, "summit", new Vector2(-30f, 165f)),       // 院子 z 134 ~ 186 的西半邊（物件的支點在原點）
        ("拉索深溝", "CourseStart", "summit", null),
    };

    // 每一區：id、名字、說明、最少拍多寬（公尺）
    static readonly (string id, string name, string desc, float minW)[] Regions =
    {
        ("harbor", "漁港", "村子南邊的海岸。棧橋末端有老漁夫在釣魚，可以跟他學釣魚、把魚賣掉換錢；行商的攤子在海岸道路旁，賣帽子、圍巾和鉤索的造型。", 120f),
        ("town", "市街", "三排房子和中間的主街。茶屋（甜點、長椅）、鐵匠鋪（換武器、用虎皮虎牙升級）、藥鋪（草藥換藥水）都在這裡，北邊那排主街西側是你暫住的老房子。", 150f),
        ("chief", "村長宅院", "村子東邊盡頭、全村最大的宅院。村長住在主殿裡；後院東牆的大門通往道場。", 90f),
        ("forest", "竹林與石階", "村子北邊往山上的石階，兩旁是竹林。石階西側的空地常有一頭雙尾虎走動，牠的巢穴在竹林更深處；打跑牠會留下虎皮和虎牙。", 130f),
        ("summit", "山頂祠堂", "石階頂端的祠堂，供奉著玄清道人；後面是習武場，東半邊有一道只能用拉索盪過去的深溝。", 170f),
    };

    float shadowDistance = -1f;
    bool fog;

    // 改的是專案的 URP 設定檔（Play 裡改了也會留下來）：不管怎麼結束都改回去
    void OnDestroy() => RestoreSettings();

    void RestoreSettings()
    {
        var urp = UniversalRenderPipeline.asset;
        if (urp != null && shadowDistance >= 0f) urp.shadowDistance = shadowDistance;
        shadowDistance = -1f;
    }

    IEnumerator Start()
    {
        var written = new List<string>();
        var urp = UniversalRenderPipeline.asset;
        fog = RenderSettings.fog;
        // 等村子擺好（套件模型、村民的位置）
        for (float t = 0f; t < 4f; t += Time.unscaledDeltaTime) yield return null;
        // 不在漁村（例如被帶到主畫面）：什麼都不寫，免得把原本的圖蓋成一片海
        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.path != VillageMapCapture.ScenePath)
        {
            Debug.LogError($"[VillageMap] 現在的場景是 {active.name}，不是漁村，沒有拍（原本的圖沒動）");
            VillageMapCapture.Finish(written);
            yield break;
        }
        Time.timeScale = 0f;

        // 找地點
        var found = new List<VillageMap.Poi>();
        foreach (var (label, obj, region, at) in Pois)
        {
            Vector3 p;
            if (at.HasValue) p = new Vector3(at.Value.x, 0f, at.Value.y);
            else
            {
                var go = GameObject.Find(obj);
                if (go == null) { Debug.LogWarning($"[VillageMap] 找不到 {obj}（{label}）"); continue; }
                p = go.transform.position;
            }
            Debug.Log($"[VillageMap] {label} = {obj ?? "固定位置"} ({p.x:F1}, {p.z:F1})");
            found.Add(new VillageMap.Poi { label = label, region = region, x = p.x, z = p.z });
        }

        // 每一區的範圍：區裡地點的外框再往外留一點，長寬比 16:10；全村 = 全部地點的外框
        var data = new VillageMap.Data { regions = new VillageMap.Region[Regions.Length], pois = found.ToArray() };
        for (int i = 0; i < Regions.Length; i++)
        {
            var (id, name, desc, minW) = Regions[i];
            var pts = found.FindAll(p => p.region == id);
            data.regions[i] = Clamp(Frame(id, name, desc, pts, 22f, 1.6f, minW));
        }
        data.overall = Clamp(Frame("all", "全村", "整個漁村：南邊是海和漁港，中間是市街，東邊是村長宅院，北邊的石階穿過竹林通到山頂的祠堂。", found, 30f, 1.25f, 120f));

        // 拍照：不畫玩家、不要霧；陰影照得到整張圖
        var hp = PlayerHealth.Find();
        if (hp != null) foreach (var r in hp.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = true;
        RenderSettings.fog = false;
        if (urp != null) { shadowDistance = urp.shadowDistance; urp.shadowDistance = 400f; }
        var camGo = new GameObject("VillageMapCamera");
        var cam = camGo.AddComponent<Camera>();
        if (Camera.main != null) cam.CopyFrom(Camera.main);
        cam.orthographic = true;
        cam.nearClipPlane = 1f;
        cam.farClipPlane = 400f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.16f, 0.32f, 0.42f); // 地圖外面當成海
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        cam.enabled = false;
        // 海：水面的著色器從正上方的正交鏡頭拍不出來（只拍到沙灘），拍照時在海面的高度鋪一片藍色的平面，拍完拿掉
        var water = GameObject.Find("Water");
        float seaY = water != null ? water.transform.position.y : 0f;
        var sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
        sea.name = "VillageMapSea";
        Destroy(sea.GetComponent<Collider>());
        sea.transform.position = new Vector3(0f, seaY + 0.01f, 0f);
        sea.transform.localScale = Vector3.one * 120f; // 1200 m
        var seaMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        seaMat.SetColor("_BaseColor", new Color(0.13f, 0.33f, 0.42f));
        seaMat.SetFloat("_Smoothness", 0.8f);
        sea.GetComponent<Renderer>().sharedMaterial = seaMat;
        // 讓 GPU Resident Drawer 先吃到上面的改動
        yield return null;
        yield return null;

        Directory.CreateDirectory(VillageMapCapture.OutDir);
        var all = new List<VillageMap.Region>(data.regions) { data.overall };
        foreach (var r in all)
        {
            r.image = "Map_" + r.id;
            float w = r.x1 - r.x0, h = r.z1 - r.z0;
            int px = w >= h ? 2048 : Mathf.RoundToInt(2048 * w / h), py = w >= h ? Mathf.RoundToInt(2048 * h / w) : 2048;
            cam.transform.SetPositionAndRotation(new Vector3((r.x0 + r.x1) * 0.5f, 200f, (r.z0 + r.z1) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
            cam.orthographicSize = h * 0.5f;
            cam.aspect = w / h;
            var rt = new RenderTexture(px, py, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();
            var tex = new Texture2D(px, py, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, px, py), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            string path = $"{VillageMapCapture.OutDir}/{r.image}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            written.Add(path);
            Destroy(rt);
            Destroy(tex);
            Debug.Log($"[VillageMap] 拍好 {r.name}：x {r.x0:F0}~{r.x1:F0}、z {r.z0:F0}~{r.z1:F0}（{px}×{py}）→ {path}");
            yield return null;
        }
        string json = $"{VillageMapCapture.OutDir}/VillageMap.json";
        File.WriteAllText(json, JsonUtility.ToJson(data, true));
        written.Add(json);

        Destroy(sea);
        RestoreSettings();
        RenderSettings.fog = fog;
        Time.timeScale = 1f;
        VillageMapCapture.Finish(written);
    }

    /// <summary>框不要超出地形（外面拍到的是背景色）：先往裡移，比地形還大就縮成地形的大小（長寬比照舊）</summary>
    static VillageMap.Region Clamp(VillageMap.Region r)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return r;
        Vector3 o = terrain.transform.position, size = terrain.terrainData.size;
        float w = r.x1 - r.x0, h = r.z1 - r.z0, aspect = w / h;
        float k = Mathf.Min(1f, size.x / w, size.z / h);
        w *= k; h *= k;
        float cx = Mathf.Clamp((r.x0 + r.x1) * 0.5f, o.x + w * 0.5f, o.x + size.x - w * 0.5f);
        float cz = Mathf.Clamp((r.z0 + r.z1) * 0.5f, o.z + h * 0.5f, o.z + size.z - h * 0.5f);
        r.x0 = cx - w * 0.5f; r.x1 = cx + w * 0.5f; r.z0 = cz - h * 0.5f; r.z1 = cz + h * 0.5f;
        return r;
    }

    /// <summary>一群地點的外框，往外留 pad 公尺，再補成 aspect（寬 / 高）、至少 minW 公尺寬</summary>
    static VillageMap.Region Frame(string id, string name, string desc, List<VillageMap.Poi> pts, float pad, float aspect, float minW)
    {
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (var p in pts) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
        if (pts.Count == 0) { x0 = -50f; x1 = 50f; z0 = -50f; z1 = 50f; }
        x0 -= pad; x1 += pad; z0 -= pad; z1 += pad;
        float w = Mathf.Max(minW, x1 - x0), h = z1 - z0;
        if (w / h < aspect) w = h * aspect; else h = w / aspect;
        float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
        return new VillageMap.Region { id = id, name = name, desc = desc, x0 = cx - w * 0.5f, x1 = cx + w * 0.5f, z0 = cz - h * 0.5f, z1 = cz + h * 0.5f };
    }
}
