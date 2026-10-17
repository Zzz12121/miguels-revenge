using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 動態小地圖（右上角圓形）：
///   - 一台俯視的正交攝影機跟著玩家，畫到 RenderTexture 上
///   - 地圖跟著鏡頭方向旋轉（畫面上方 = 你正在看的方向），中間的箭頭是玩家面向
///   - 紅點 = 敵人（死掉就消失），藍點 = 傳送門，黃點 = 檢查點；傳送門在範圍外時會貼在地圖邊緣指方向
///   - 只畫玩家頭頂 ceilingClearance 公尺以下的東西，地下城的天花板、房子屋頂不會擋住地圖
///   - 按 M 顯示 / 隱藏
///
/// 不用手動放進場景：GameManager.levels 裡的關卡載入時會自動建立。
/// </summary>
public class Minimap : MonoBehaviour
{
    [Header("地圖")]
    [Tooltip("地圖半徑代表幾公尺")]
    public float worldRadius = 45f;
    [Tooltip("地圖在畫面上的直徑（以 1920x1080 為基準）")]
    public float uiSize = 280f;
    [Tooltip("攝影機在玩家上方幾公尺")]
    public float cameraHeight = 80f;
    [Tooltip("玩家頭頂幾公尺以上的東西不畫（天花板、屋頂）")]
    public float ceilingClearance = 5f;
    [Tooltip("往下畫多深 (m)")]
    public float depth = 80f;
    [Tooltip("地圖跟著鏡頭轉（關掉 = 永遠北方朝上）")]
    public bool rotateWithCamera = true;
    [Tooltip("每幾個畫面更新一次地圖畫面（越大越省效能）")]
    [Range(1, 6)] public int renderEveryNFrames = 2;
    public Key toggleKey = Key.M;

    [Header("標記顏色")]
    public Color enemyColor = new Color(1f, 0.25f, 0.2f);
    public Color portalColor = new Color(0.4f, 0.75f, 1f);
    public Color checkpointColor = new Color(1f, 0.85f, 0.3f);

    enum Kind { Enemy, Portal, Checkpoint }

    class Marker
    {
        public Transform target;
        public Kind kind;
        public System.Func<bool> hidden;
        public RectTransform ui;
    }

    Transform player;
    Camera mapCam;
    RenderTexture rt;
    Canvas canvas;
    RectTransform mapRoot, markerRoot, playerArrow;
    readonly List<Marker> markers = new List<Marker>();
    float nextScan;
    bool visible = true;
    static Sprite circleSprite, arrowSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        var gm = GameManager.Instance;
        bool isLevel = gm != null ? gm.IndexOf(scene.name) >= 0 : scene.name != "MainMenu";
        if (!isLevel || FindAnyObjectByType<Minimap>() != null) return;
        new GameObject("Minimap").AddComponent<Minimap>();
    }

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null)
        {
            Destroy(gameObject);
            return;
        }
        player = p.transform;
        BuildCamera();
        BuildUI();
        ScanMarkers();
    }

    void OnDestroy()
    {
        if (mapCam != null) mapCam.targetTexture = null;
        if (rt != null) rt.Release();
    }

    // ---------------------------------------------------------------- 建立

    void BuildCamera()
    {
        rt = new RenderTexture(384, 384, 16) { name = "MinimapRT" };
        var go = new GameObject("MinimapCamera");
        go.transform.SetParent(transform, false);
        mapCam = go.AddComponent<Camera>();
        mapCam.orthographic = true;
        mapCam.orthographicSize = worldRadius;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color(0.08f, 0.1f, 0.14f);
        mapCam.targetTexture = rt;
        mapCam.nearClipPlane = Mathf.Max(0.3f, cameraHeight - ceilingClearance);
        mapCam.farClipPlane = cameraHeight + depth;
        mapCam.allowHDR = false;
        mapCam.allowMSAA = false;
        mapCam.useOcclusionCulling = false;

        // 小地圖不需要陰影和後製，省效能
        var data = mapCam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderShadows = false;
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
        }
    }

    void BuildUI()
    {
        EnsureSprites();
        canvas = UIKit.CreateCanvas("MinimapCanvas", 5, transform);

        // 右上角
        var frame = UIKit.Rect("Frame", canvas.transform, Vector2.one, Vector2.one,
                               new Vector2(-uiSize * 0.5f - 30f, -uiSize * 0.5f - 30f), Vector2.one * (uiSize + 10f));
        var frameImg = frame.gameObject.AddComponent<Image>();
        frameImg.sprite = circleSprite;
        frameImg.color = new Color(1f, 0.82f, 0.42f, 0.85f);
        frameImg.raycastTarget = false;
        mapRoot = frame;

        // 圓形遮罩裡放地圖畫面
        var mask = UIKit.Rect("Mask", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * uiSize);
        var maskImg = mask.gameObject.AddComponent<Image>();
        maskImg.sprite = circleSprite;
        maskImg.raycastTarget = false;
        mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;

        var map = UIKit.Rect("Map", mask, Vector2.zero, Vector2.one);
        var raw = map.gameObject.AddComponent<RawImage>();
        raw.texture = rt;
        raw.raycastTarget = false;

        markerRoot = UIKit.Rect("Markers", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * uiSize);

        playerArrow = UIKit.Rect("Player", frame, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
        var arrowImg = playerArrow.gameObject.AddComponent<Image>();
        arrowImg.sprite = arrowSprite;
        arrowImg.color = Color.white;
        arrowImg.raycastTarget = false;
        playerArrow.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);

        var label = UIKit.Label("Hint", frame, $"{toggleKey} 小地圖", 18, new Color(1f, 1f, 1f, 0.6f),
                                new Vector2(0f, -uiSize * 0.5f - 22f), new Vector2(200f, 30f));
        label.raycastTarget = false;
    }

    static void EnsureSprites()
    {
        if (circleSprite != null) return;

        const int n = 128;
        var circle = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "MinimapCircle", wrapMode = TextureWrapMode.Clamp };
        float r = n * 0.5f;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                circle.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(r - d)));
            }
        circle.Apply();
        circleSprite = Sprite.Create(circle, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));

        const int m = 64;
        var arrow = new Texture2D(m, m, TextureFormat.RGBA32, false) { name = "MinimapArrow", wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < m; y++)
            for (int x = 0; x < m; x++)
            {
                // 往上指的三角形，底部中間凹進去一點
                float v = y / (float)(m - 1);
                float half = (1f - v) * 0.5f;
                float u = Mathf.Abs(x / (float)(m - 1) - 0.5f);
                bool inside = u <= half && !(v < 0.25f && u < (0.25f - v) * 0.8f);
                arrow.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        arrow.Apply();
        arrowSprite = Sprite.Create(arrow, new Rect(0, 0, m, m), new Vector2(0.5f, 0.5f));
    }

    // ---------------------------------------------------------------- 標記

    void ScanMarkers()
    {
        nextScan = Time.unscaledTime + 0.5f;
        markers.RemoveAll(mk =>
        {
            if (mk.target != null) return false;
            if (mk.ui != null) Destroy(mk.ui.gameObject);
            return true;
        });

        foreach (var e in FindObjectsByType<EnemyHealth>()) Track(e.transform, Kind.Enemy, () => e == null || e.IsDead);
        foreach (var e in FindObjectsByType<ClockEnemyHealth>()) Track(e.transform, Kind.Enemy, () => e == null || e.IsDead);
        foreach (var e in FindObjectsByType<BossHealth>()) Track(e.transform, Kind.Enemy, () => e == null || e.IsDead || !e.gameObject.activeInHierarchy);
        foreach (var e in FindObjectsByType<EnemyBoat>()) Track(e.transform, Kind.Enemy, () => e == null || e.IsDead);
        foreach (var p in FindObjectsByType<Portal>()) Track(p.transform, Kind.Portal, () => p == null || !p.IsOpen);
        foreach (var c in FindObjectsByType<Checkpoint>()) Track(c.transform, Kind.Checkpoint, () => c == null);
    }

    void Track(Transform t, Kind kind, System.Func<bool> hidden)
    {
        foreach (var mk in markers)
            if (mk.target == t) return;

        float size = kind == Kind.Enemy ? 10f : 14f;
        var ui = UIKit.Rect(kind.ToString(), markerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size);
        var img = ui.gameObject.AddComponent<Image>();
        img.sprite = circleSprite;
        img.color = kind == Kind.Enemy ? enemyColor : kind == Kind.Portal ? portalColor : checkpointColor;
        img.raycastTarget = false;
        ui.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
        markers.Add(new Marker { target = t, kind = kind, hidden = hidden, ui = ui });
    }

    // ---------------------------------------------------------------- 每幀更新

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[toggleKey].wasPressedThisFrame && Time.timeScale > 0f)
        {
            visible = !visible;
            if (canvas != null) canvas.enabled = visible;
        }
        if (Time.unscaledTime >= nextScan) ScanMarkers();
    }

    void LateUpdate()
    {
        if (player == null || mapCam == null) return;

        // 地圖畫面：關掉小地圖時不畫；其他時候每 N 個畫面畫一次（RenderTexture 會留著上一張）
        mapCam.enabled = visible && Time.frameCount % renderEveryNFrames == 0;

        float yaw = 0f;
        if (rotateWithCamera && Camera.main != null) yaw = Camera.main.transform.eulerAngles.y;
        Vector3 center = player.position;
        mapCam.transform.SetPositionAndRotation(center + Vector3.up * cameraHeight, Quaternion.Euler(90f, yaw, 0f));

        if (!visible) return;

        playerArrow.localRotation = Quaternion.Euler(0f, 0f, -(player.eulerAngles.y - yaw));

        float radiusPx = uiSize * 0.5f;
        float scale = radiusPx / worldRadius;
        Quaternion toMap = Quaternion.Euler(0f, -yaw, 0f);
        foreach (var mk in markers)
        {
            if (mk.target == null || mk.ui == null) continue;
            bool hide = mk.hidden();
            Vector3 local = toMap * (mk.target.position - center);
            Vector2 pos = new Vector2(local.x, local.z) * scale;
            float edge = radiusPx - 8f;
            if (pos.magnitude > edge)
            {
                // 傳送門在範圍外：貼在邊緣指方向；其他標記直接不顯示
                if (mk.kind == Kind.Portal) pos = pos.normalized * edge;
                else hide = true;
            }
            if (mk.ui.gameObject.activeSelf == hide) mk.ui.gameObject.SetActive(!hide);
            mk.ui.anchoredPosition = pos;
        }
    }
}
