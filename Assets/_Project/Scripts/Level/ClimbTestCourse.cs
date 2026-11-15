using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 攀爬測試台：一排高度不同的石台 / 石牆，可以在任何場景（例如 MovementTest）測攀爬動畫。
/// 選單 GeminiGrowUp > 測試 > 在目前場景建立攀爬測試台 會把它放在玩家前面。
/// 石頭在按 Play 的時候才產生（跟書法 Boss 大招的浮石用同一套：CalligraphyInkPlatform + CalligraphyPlatformClimb），
/// 編輯模式下用 Gizmo 畫出石頭的位置和形狀。
///
/// 怎麼爬：跑向石頭、跳起來，往下掉的時候「面向石頭、按住 W」就會抓邊爬上去
/// （邊緣要比腳高 0.5～2.5 公尺、腰在邊緣下面、離邊緣 1.35 公尺內）。
/// </summary>
[DisallowMultipleComponent]
public sealed class ClimbTestCourse : MonoBehaviour
{
    public enum Shape { Wall, InkRock }

    [System.Serializable]
    public class Block
    {
        public string label = "1.6m";
        public Shape shape = Shape.Wall;
        [Tooltip("在測試台底下的位置（x 左右、z 前後）")]
        public Vector2 position;
        [Tooltip("頂面離地多高（公尺）")]
        [Min(0.3f)] public float height = 1.6f;
        [Tooltip("石牆：寬、深；石頭：半徑用 x")]
        public Vector2 size = new Vector2(2.4f, 2.4f);
        [Tooltip("石頭的形狀亂數（換數字就換形狀）")]
        public int seed = 3;
        [Tooltip("繞 y 軸轉幾度")]
        public float yaw;
    }

    public Block[] blocks =
    {
        new Block { label = "1.0m 牆", shape = Shape.Wall, position = new Vector2(-9.6f, 0), height = 1.0f },
        new Block { label = "1.6m 牆", shape = Shape.Wall, position = new Vector2(-6.4f, 0), height = 1.6f },
        new Block { label = "2.2m 牆", shape = Shape.Wall, position = new Vector2(-3.2f, 0), height = 2.2f },
        new Block { label = "2.8m 牆（要跳）", shape = Shape.Wall, position = new Vector2(0f, 0), height = 2.8f },
        new Block { label = "1.8m 石", shape = Shape.InkRock, position = new Vector2(3.2f, 0), height = 1.8f, size = new Vector2(1.5f, 0), seed = 3 },
        new Block { label = "2.6m 石", shape = Shape.InkRock, position = new Vector2(6.4f, 0), height = 2.6f, size = new Vector2(1.7f, 0), seed = 7, yaw = 40 },
        new Block { label = "3.4m 石（要跳）", shape = Shape.InkRock, position = new Vector2(9.6f, 0), height = 3.4f, size = new Vector2(1.9f, 0), seed = 11, yaw = 90 },
    };

    [Tooltip("攀爬的手感（跟書法 Boss 大招的設定一樣）")]
    public CalligraphyBossCombat.AvatarClimbSettings climb = new CalligraphyBossCombat.AvatarClimbSettings();
    [Tooltip("石頭的材質（空的就用灰色的預設材質）")]
    public Material material;
    [Tooltip("靠近時在畫面上顯示操作說明")]
    public bool showHint = true;

    readonly List<CalligraphyInkPlatform> platforms = new List<CalligraphyInkPlatform>();
    CalligraphyPlatformClimb climber;
    PlayerHealth player;
    Material runtimeMaterial;

    void Start() => Build();

    void Update()
    {
        if (player == null) Build(); // 玩家晚一點才生成的場景
    }

    /// <summary>場景裡的玩家：有 PlayerHealth 的優先，沒有就找 ThirdPersonController（MovementTest 的玩家沒有血量元件）</summary>
    public static Transform FindPlayerBody()
    {
        var h = PlayerHealth.Find();
        if (h != null) return h.transform;
        var c = FindAnyObjectByType<ThirdPersonController>();
        return c != null ? c.transform : null;
    }

    /// <summary>所有石頭在測試台底下佔的範圍（用來找空地）</summary>
    public Bounds LocalFootprint()
    {
        var b = new Bounds(Vector3.zero, Vector3.zero);
        bool any = false;
        foreach (var k in blocks)
        {
            if (k == null) continue;
            float r = k.shape == Shape.Wall ? 0.75f * Mathf.Max(k.size.x, k.size.y) : Mathf.Max(0.6f, k.size.x) * 1.15f;
            var e = new Bounds(new Vector3(k.position.x, k.height * 0.5f, k.position.y), new Vector3(2 * r, k.height, 2 * r));
            if (!any) { b = e; any = true; } else b.Encapsulate(e);
        }
        return b;
    }

    void Build()
    {
        if (player == null)
        {
            player = PlayerHealth.Find();
            if (player == null)
            {
                // 攀爬程式要用到 PlayerHealth（死掉時取消攀爬）：測試場景的玩家沒有的話，遊玩時幫它加一個（不會存進場景）
                var body = FindPlayerBody();
                if (body == null) return;
                player = body.gameObject.AddComponent<PlayerHealth>();
                Debug.Log("[攀爬測試台] 玩家沒有 PlayerHealth，遊玩時先幫它加上（只在這次 Play 有效）。");
            }
        }
        if (platforms.Count == 0)
        {
            for (int i = 0; i < blocks.Length; i++)
                if (blocks[i] != null) platforms.Add(CreateBlock(blocks[i], i));
        }
        if (climber == null) climber = gameObject.AddComponent<CalligraphyPlatformClimb>();
        climber.Initialize(player, platforms, climb);
    }

    Material Mat()
    {
        if (material != null) return material;
        if (runtimeMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            runtimeMaterial = new Material(shader) { name = "ClimbTest_Rock", color = new Color(0.42f, 0.42f, 0.45f) };
        }
        return runtimeMaterial;
    }

    CalligraphyInkPlatform CreateBlock(Block b, int index)
    {
        // 平台物件的原點 = 頂面（CalligraphyInkPlatform 用 transform.position.y 當頂面）
        Vector3 top = transform.TransformPoint(new Vector3(b.position.x, b.height, b.position.y));
        var go = new GameObject("ClimbTest_" + index + "_" + b.label);
        go.transform.SetParent(transform, false);
        go.transform.SetPositionAndRotation(top, transform.rotation * Quaternion.Euler(0f, b.yaw, 0f));

        Vector3[] rim;
        float depth = b.height + 0.6f; // 往下埋進地面一點
        float radius;
        if (b.shape == Shape.Wall)
        {
            float hx = b.size.x * 0.5f, hz = b.size.y * 0.5f;
            radius = Mathf.Max(hx, hz);
            // 逆時針（從上往下看是順著 x→z），CalligraphyInkPlatform.TryLedge 才算得出往內的方向
            rim = new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) };
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Mesh";
            cube.transform.SetParent(go.transform, false);
            cube.transform.localPosition = new Vector3(0, -depth * 0.5f, 0);
            cube.transform.localScale = new Vector3(b.size.x, depth, b.size.y);
            cube.GetComponent<Renderer>().sharedMaterial = Mat();
        }
        else
        {
            radius = Mathf.Max(0.6f, b.size.x);
            var mesh = CalligraphyRockGeometry.Create(radius, depth, b.seed, 0.35f, 0.3f, out rim);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat();
            var col = go.AddComponent<MeshCollider>();
            col.sharedMesh = mesh;
            col.convex = true;
        }

        var platform = go.AddComponent<CalligraphyInkPlatform>();
        platform.Initialize(top.y, radius, 1f, player); // 目標高度 = 現在的高度：不會升降
        platform.ConfigureShape(rim, depth, 0f);
        return platform;
    }

    void OnGUI()
    {
        if (!showHint || player == null || GameUI.OverlayOpen) return;
        if ((player.transform.position - transform.position).sqrMagnitude > 18f * 18f) return;
        string state = climber != null && climber.Climbing ? "攀爬中…" : $"已爬上 {(climber != null ? climber.CompletedClimbs : 0)} 次";
        GameUI.Shadowed(new Rect(0, 70, Screen.width, 26), "攀爬測試：朝石頭跳起來，往下掉的時候面向石頭、按住 W　｜　" + state, GameUI.Center, Color.white);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null) Destroy(runtimeMaterial);
    }

#if UNITY_EDITOR
    // ---- 編輯模式下畫出石頭（不用按 Play 也看得到放在哪） ----
    readonly Dictionary<int, Mesh> previewMeshes = new Dictionary<int, Mesh>();

    void OnDrawGizmos()
    {
        if (Application.isPlaying || blocks == null) return;
        for (int i = 0; i < blocks.Length; i++)
        {
            var b = blocks[i];
            if (b == null) continue;
            Vector3 top = transform.TransformPoint(new Vector3(b.position.x, b.height, b.position.y));
            Quaternion rot = transform.rotation * Quaternion.Euler(0f, b.yaw, 0f);
            float depth = b.height + 0.6f;
            Gizmos.color = b.shape == Shape.Wall ? new Color(0.55f, 0.6f, 0.7f, 0.85f) : new Color(0.45f, 0.45f, 0.5f, 0.85f);
            if (b.shape == Shape.Wall)
            {
                Gizmos.matrix = Matrix4x4.TRS(top + rot * new Vector3(0, -depth * 0.5f, 0), rot, Vector3.one);
                Gizmos.DrawCube(Vector3.zero, new Vector3(b.size.x, depth, b.size.y));
            }
            else
            {
                int key = b.seed * 1000 + Mathf.RoundToInt(b.size.x * 100) + Mathf.RoundToInt(depth * 10) * 100000;
                if (!previewMeshes.TryGetValue(key, out var mesh) || mesh == null)
                {
                    mesh = CalligraphyRockGeometry.Create(Mathf.Max(0.6f, b.size.x), depth, b.seed, 0.35f, 0.3f, out _);
                    mesh.hideFlags = HideFlags.HideAndDontSave;
                    previewMeshes[key] = mesh;
                }
                Gizmos.matrix = Matrix4x4.TRS(top, rot, Vector3.one);
                Gizmos.DrawMesh(mesh);
            }
            Gizmos.matrix = Matrix4x4.identity;
            UnityEditor.Handles.Label(top + Vector3.up * 0.4f, b.label);
        }
    }

    void OnDisable()
    {
        foreach (var m in previewMeshes.Values) if (m != null) DestroyImmediate(m);
        previewMeshes.Clear();
    }
#endif
}
