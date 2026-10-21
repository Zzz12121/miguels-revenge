using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 劇情對話框：畫面下方的對話框（文字逐字出現），左邊是玩家的半身像，右邊是對方的半身像（正在說話的人亮、另一邊變暗）。
/// 播放期間遊戲暫停（Time.timeScale = 0）、玩家不能動，按空白鍵（或滑鼠左鍵）下一句，最後一句之後呼叫 onDone。
/// 用法：StoryDialogue.Play(lines, onDone)；每一句指定說話者名字、半身像要拍的物件、在左邊還是右邊。
/// 半身像由 DialoguePortraits 用一台隱藏的攝影機即時拍場景裡的角色（不需要另外準備圖片）。
/// </summary>
public class StoryDialogue : MonoBehaviour
{
    public struct Line
    {
        public string name;
        /// <summary>半身像要拍的角色（null = 不畫）</summary>
        public Transform who;
        /// <summary>true = 對方（右邊），false = 玩家（左邊）</summary>
        public bool right;
        public string text;
        public Color nameColor;
    }

    public static Line L(string name, Transform who, bool right, string text, Color color) =>
        new Line { name = name, who = who, right = right, text = text, nameColor = color };

    public float charsPerSecond = 32f;

    static StoryDialogue current;
    public static bool Active => current != null;

    List<Line> lines;
    System.Action onDone;
    int index;
    float lineStart;
    float prevTimeScale = 1f;
    ThirdPersonController controller;
    Transform leftWho, rightWho;

    /// <summary>播放一段劇情對話（已經有對話在播的話排在它後面）</summary>
    public static void Play(IList<Line> lines, System.Action onDone = null)
    {
        if (lines == null || lines.Count == 0) { onDone?.Invoke(); return; }
        if (current != null)
        {
            // 前一段播完再接著播
            var prev = current.onDone;
            var copy = new List<Line>(lines);
            current.onDone = () => { prev?.Invoke(); Play(copy, onDone); };
            return;
        }
        var go = new GameObject("StoryDialogue");
        current = go.AddComponent<StoryDialogue>();
        current.Begin(new List<Line>(lines), onDone);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => current = null;

    void Begin(List<Line> list, System.Action done)
    {
        lines = list;
        onDone = done;
        index = 0;
        lineStart = Time.unscaledTime;
        prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;

        var hp = PlayerHealth.Find();
        if (hp != null)
        {
            controller = hp.GetComponent<ThirdPersonController>();
            leftWho = hp.transform;
        }
        if (controller != null)
        {
            controller.LockActions();
            controller.inputEnabled = false;
        }
        UpdateSpeakers();
        GameAudio.Play("SFX/RPG/bookOpen", Vector3.zero, 0.6f, 1.1f, 0f, 0f);
    }

    void UpdateSpeakers()
    {
        var line = lines[index];
        if (line.right) rightWho = line.who;
        else if (line.who != null) leftWho = line.who;
        // 還沒輪到右邊說話時，先找這段對話裡第一個右邊的人
        if (rightWho == null)
            foreach (var l in lines) if (l.right && l.who != null) { rightWho = l.who; break; }
    }

    int VisibleChars() => Mathf.FloorToInt((Time.unscaledTime - lineStart) * charsPerSecond);

    void Update()
    {
        if (GameUI.OverlayOpen) return;   // 暫停選單開著時不動
        Time.timeScale = 0f;              // 暫停選單關掉時會把時間恢復，這裡再停一次

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        bool next = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                 || (mouse != null && mouse.leftButton.wasPressedThisFrame);
        if (!next) return;

        if (VisibleChars() < lines[index].text.Length)
        {
            lineStart = -9999f; // 文字還在跑：先整句顯示
            return;
        }
        index++;
        lineStart = Time.unscaledTime;
        GameAudio.Play("SFX/RPG/bookFlip1", Vector3.zero, 0.5f, 1f, 0.1f, 0f);
        if (index >= lines.Count) Finish();
        else UpdateSpeakers();
    }

    void Finish()
    {
        current = null;
        Time.timeScale = prevTimeScale;
        if (controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            var hp = controller.GetComponent<PlayerHealth>();
            if (hp == null || !hp.IsDead) controller.inputEnabled = true;
        }
        Destroy(gameObject);
        onDone?.Invoke();
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }

    [System.NonSerialized] GUIStyle nameStyle;

    void OnGUI()
    {
        if (GameUI.OverlayOpen || index >= lines.Count) return;
        GUI.depth = -1200;
        var line = lines[index];

        float w = Mathf.Min(900f, Screen.width - 80f), h = 170f;
        var box = new Rect((Screen.width - w) * 0.5f, Screen.height - h - 40f, w, h);
        DrawPortraits(box, leftWho, rightWho, !line.right, line.right);

        GameUI.Panel(box);
        nameStyle ??= new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold };
        nameStyle.alignment = line.right ? TextAnchor.UpperRight : TextAnchor.UpperLeft;
        GUI.color = line.nameColor.a > 0f ? line.nameColor : GameUI.Accent;
        GUI.Label(new Rect(box.x + 24f, box.y + 12f, box.width - 48f, 30f), line.name, nameStyle);

        int n = Mathf.Clamp(VisibleChars(), 0, line.text.Length);
        GUI.color = GameUI.TextColor;
        GUI.Label(new Rect(box.x + 24f, box.y + 46f, box.width - 48f, 80f), line.text.Substring(0, n), GameUI.Body);

        bool done = n >= line.text.Length;
        float a = done ? 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f) : 0.4f;
        var right = GameUI.SmallRight;
        string action = index == lines.Count - 1 ? "結束" : "繼續";
        float actionW = right.CalcSize(new GUIContent(action)).x;
        float y = box.yMax - 34f, x = box.xMax - 24f - actionW;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.Label(new Rect(x, y, actionW, 24f), action, right);
        GameUI.Key(new Rect(x - 64f, y, 58f, 24f), "Space", a);
        GUI.color = Color.white;
    }

    /// <summary>
    /// 對話框兩側的半身像：左邊 left（玩家）、右邊 right（對方），正在說話的那邊亮、另一邊變暗。
    /// 商人的對話框也用這個。
    /// </summary>
    public static void DrawPortraits(Rect box, Transform left, Transform right, bool leftSpeaking, bool rightSpeaking)
    {
        // 站在對話框的上緣（左上角 / 右上角）：下半部藏在對話框後面，只露出上半身。背景透明
        // （呼叫的地方要先畫半身像、再畫對話框，對話框才會蓋住下半部）
        const float pw = 240f, ph = 300f;
        float y = box.y - ph * 0.5f;
        if (left != null) DialoguePortraits.Draw(new Rect(box.x + 8f, y, pw, ph), 0, left, leftSpeaking);
        if (right != null) DialoguePortraits.Draw(new Rect(box.xMax - pw - 8f, y, pw, ph), 1, right, rightSpeaking);
    }
}

/// <summary>
/// 對話用的半身像：每一邊一台隱藏的攝影機（背景透明），每幀把指定角色單獨拍進 RenderTexture（暫時把角色換到第 31 層，只拍那一層）。
/// OnGUI 裡呼叫 Draw 就好：畫上一幀拍好的圖，並登記下一幀要拍誰；一陣子沒被要求就不再拍。
/// </summary>
public class DialoguePortraits : MonoBehaviour
{
    const int PortraitLayer = 31;
    /// <summary>每一邊的放大倍率：左邊（玩家）大一點、右邊（商人等）小一點</summary>
    static readonly float[] SlotZoom = { 1.2f, 0.85f };
    const int Width = 288, Height = 352;

    class Slot
    {
        public Transform target;
        public float lastUse = float.NegativeInfinity;
        public RenderTexture rt;
        public Camera cam;
        public float zoom = 1f;
    }

    static DialoguePortraits instance;
    readonly Slot[] slots = { new Slot { zoom = SlotZoom[0] }, new Slot { zoom = SlotZoom[1] } };
    readonly List<Renderer> renderers = new List<Renderer>();
    readonly Dictionary<GameObject, int> savedLayers = new Dictionary<GameObject, int>();

    // 非玩家角色拍「替身」：把角色（或指定的 Prefab）複製一份放到地圖下方很遠的地方，只留模型。
    // 不受場景裡本尊的腳本、LOD、光影或其他物件影響
    static readonly Dictionary<Transform, GameObject> prefabOverrides = new Dictionary<Transform, GameObject>();
    readonly Dictionary<Transform, GameObject> standIns = new Dictionary<Transform, GameObject>();
    Transform stage;
    int stageCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        instance = null;
        prefabOverrides.Clear();
    }

    /// <summary>指定 target 的半身像改拍這個 Prefab（例如商人用專門的模型）</summary>
    public static void UsePrefab(Transform target, GameObject prefab)
    {
        if (target == null) return;
        if (prefab != null) prefabOverrides[target] = prefab;
        else prefabOverrides.Remove(target);
        if (instance != null && instance.standIns.TryGetValue(target, out var old))
        {
            if (old != null) Destroy(old);
            instance.standIns.Remove(target);
        }
    }

    /// <summary>玩家（人形骨架、會動）直接拍本人；其他角色拍替身</summary>
    Transform Subject(Transform target)
    {
        if (prefabOverrides.ContainsKey(target)) return StandIn(target);
        var anim = target.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman) return target;
        return StandIn(target);
    }

    Transform StandIn(Transform target)
    {
        if (standIns.TryGetValue(target, out var existing) && existing != null) return existing.transform;

        if (stage == null)
        {
            var holder = new GameObject("PortraitStage");
            holder.transform.SetParent(transform, false);
            stage = holder.transform;
        }
        // 先複製到一個關閉的物件底下：腳本的 Awake / OnEnable 不會執行
        var hidden = new GameObject("PortraitStageHidden");
        hidden.SetActive(false);
        prefabOverrides.TryGetValue(target, out var prefab);
        var src = prefab != null ? prefab : target.gameObject;
        var copy = Instantiate(src, hidden.transform);
        copy.name = "Portrait_" + target.name;

        // 只留模型：腳本、碰撞、物理、特效、燈光都拿掉（有依賴關係，腳本先拿）
        // 倒著拿：被別人 RequireComponent 的腳本通常排在前面
        var scripts = copy.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = scripts.Length - 1; i >= 0; i--) DestroyImmediate(scripts[i]);
        foreach (var c in copy.GetComponentsInChildren<Animator>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<CharacterController>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<ParticleSystem>(true)) DestroyImmediate(c.gameObject);
        foreach (var c in copy.GetComponentsInChildren<LineRenderer>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<TrailRenderer>(true)) DestroyImmediate(c);
        foreach (var c in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(c);
        foreach (var t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PortraitLayer;

        // 每個替身隔開 200 公尺，放在地圖下方
        copy.transform.SetParent(stage, false);
        copy.transform.position = new Vector3(stageCount++ * 200f, -5000f, 0f);
        copy.transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        if (prefab == null) copy.transform.localScale = target.lossyScale;
        Destroy(hidden);
        standIns[target] = copy;
        return copy.transform;
    }

    static DialoguePortraits Instance
    {
        get
        {
            if (instance == null)
            {
                var go = new GameObject("DialoguePortraits");
                DontDestroyOnLoad(go);
                instance = go.AddComponent<DialoguePortraits>();
            }
            return instance;
        }
    }

    /// <summary>在 OnGUI 裡呼叫：畫出 slot（0 = 左、1 = 右）的半身像</summary>
    public static void Draw(Rect r, int slot, Transform target, bool speaking)
    {
        if (Event.current.type != EventType.Repaint) return;
        var inst = Instance;
        var s = inst.slots[slot];
        s.target = target; // 下一幀的 LateUpdate 拍（不能在 OnGUI 裡渲染）
        s.lastUse = Time.unscaledTime;

        // 背景透明：只畫角色。沒在說話的那邊變暗
        if (s.rt != null)
        {
            GUI.color = speaking ? Color.white : new Color(0.45f, 0.45f, 0.48f, 0.9f);
            GUI.DrawTexture(r, s.rt, ScaleMode.ScaleAndCrop, true);
        }
        GUI.color = Color.white;
    }

    void LateUpdate()
    {
        // 本尊已經不在了（換場景、沙蟲沉下去）：替身也刪掉
        if (standIns.Count > 0)
        {
            List<Transform> gone = null;
            foreach (var kv in standIns) if (kv.Key == null) (gone ??= new List<Transform>()).Add(kv.Key);
            if (gone != null)
                foreach (var k in gone) { if (standIns[k] != null) Destroy(standIns[k]); standIns.Remove(k); }
        }

        foreach (var s in slots)
            if (s.target != null && Time.unscaledTime - s.lastUse < 0.3f) RenderSlot(s);
    }

    /// <summary>每一邊各自一台攝影機（同一台在同一幀拍兩次，URP 可能會把兩張畫進同一個目標）</summary>
    Camera EnsureCamera(Slot s)
    {
        if (s.cam != null) return s.cam;
        var go = new GameObject("PortraitCamera");
        go.transform.SetParent(transform, false);
        var cam = go.AddComponent<Camera>();
        cam.enabled = false;               // 只手動 Render
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // 透明背景
        cam.cullingMask = 1 << PortraitLayer;
        cam.fieldOfView = 28f;
        cam.nearClipPlane = 0.05f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        var data = cam.GetUniversalAdditionalCameraData();
        if (data != null)
        {
            data.renderPostProcessing = false; // 後處理會把透明度蓋掉
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;
            data.requiresDepthTexture = false;
            data.requiresColorTexture = false;
        }
        s.cam = cam;
        return cam;
    }

    void RenderSlot(Slot s)
    {
        if (s.target == null) return;
        var cam = EnsureCamera(s);
        if (s.rt == null)
        {
            s.rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "DialoguePortrait" };
            s.rt.Create();
        }

        Transform subject = Subject(s.target);
        renderers.Clear();
        foreach (var r in subject.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            renderers.Add(r);
        }
        if (renderers.Count == 0) return;

        Bounds b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        float height = Mathf.Max(0.5f, b.size.y);

        // 頭的位置：人形骨架用頭骨，其他用外框頂端
        Vector3 head = new Vector3(b.center.x, b.max.y - height * 0.12f, b.center.z);
        var anim = subject.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman)
        {
            var bone = anim.GetBoneTransform(HumanBodyBones.Head);
            if (bone != null) { head = bone.position; height = Mathf.Max(0.5f, head.y - b.min.y) * 1.12f; }
        }

        // 畫面高度 ≈ 整個人（頭上留一點空間）；下半部會被對話框蓋住，看起來就是上半身
        float frame = Mathf.Clamp(height * 0.95f / Mathf.Max(0.1f, s.zoom), 0.5f, 20f);
        float top = head.y + height * 0.12f;
        Vector3 focus = new Vector3(head.x, top - frame * 0.5f, head.z);
        Vector3 fwd = subject.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd = Quaternion.Euler(0f, 20f, 0f) * fwd.normalized; // 稍微斜一點的 3/4 角度
        float dist = frame * 0.5f / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
        cam.transform.position = focus + fwd * dist + Vector3.up * frame * 0.05f;
        cam.transform.LookAt(focus);
        cam.farClipPlane = dist + height * 3f;
        cam.targetTexture = s.rt;

        // 同一個物件可能有好幾個 Renderer：每個物件只記一次原本的層
        savedLayers.Clear();
        foreach (var r in renderers)
            if (!savedLayers.ContainsKey(r.gameObject)) savedLayers.Add(r.gameObject, r.gameObject.layer);
        foreach (var go in savedLayers.Keys) go.layer = PortraitLayer;
        try { cam.Render(); }
        finally
        {
            foreach (var kv in savedLayers) if (kv.Key != null) kv.Key.layer = kv.Value;
        }
    }

    void OnDestroy()
    {
        foreach (var s in slots)
        {
            if (s.cam != null) s.cam.targetTexture = null;
            if (s.rt != null) s.rt.Release();
        }
    }
}
