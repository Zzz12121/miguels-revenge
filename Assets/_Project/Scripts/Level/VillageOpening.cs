using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 開新遊戲第一次進漁村的開場（GameManager.StartStoryGame 設 Pending，不用在場景裡放東西）：
///   1. 漁村的景：漁港、市街、鳥居和石階、村長宅院，四個慢慢移動的空景鏡頭（上下黑邊，鏡頭之間淡入淡出）
///   2. 閃過以前的回憶：沙漠、維京村莊、地下城各一張，白光一閃就過去
///      （圖在 Resources/Opening/Memory_*.png，編輯器選單 GeminiGrowUp/拍開場回憶圖 產生；沒拍過的話是褪色的紙上寫地名）
///   3. 轉到主角身上：站在行商面前，下面的字幕「我到底發生甚麼事了……甚麼記憶都沒有……這裡又是哪裡？」
///   4. 鏡頭飛回玩家背後，交還操作，顯示「漁村」
/// 主角一開始就被放到行商面前（面對他）。按 Space / Enter / 手把 A 跳過。播完記下 StoryProgress.openingSeen 並存檔。
/// </summary>
public class VillageOpening : MonoBehaviour
{
    /// <summary>下一次進漁村要播開場（開新遊戲時設，播了就清掉）</summary>
    public static bool Pending;
    /// <summary>開場正在播</summary>
    public static bool Active { get; private set; }

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { Pending = false; Active = false; hooked = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (hooked) return;
        hooked = true;
        SceneManager.sceneLoaded += (s, m) =>
        {
            if (!Pending || s.name != VillageReturn.VillageScene) return;
            Pending = false;
            new GameObject("VillageOpening").AddComponent<VillageOpening>();
        };
    }

    static readonly string[] Lines = { "我到底發生甚麼事了……", "甚麼記憶都沒有……", "這裡又是哪裡？" };
    static readonly (string file, string place)[] Memories =
    {
        ("Opening/Memory_Desert", "沙　漠"), ("Opening/Memory_Viking", "維京村莊"), ("Opening/Memory_Dungeon", "地下城"),
    };

    const float ShotSeconds = 2.9f, ShotFade = 0.45f;

    GameManager gm;
    bool titleWas = true;
    Transform playerTf, merchant;
    ThirdPersonController lockedCtrl;
    ThirdPersonCamera tpc;
    CinemachineCamera vcam;
    Vector3 camPos;
    Quaternion camRot;

    // 畫面上的東西（OnGUI）
    float black = 1f, white, letterbox, memoryAlpha;
    Texture2D memoryTex;
    string memoryPlace;
    Vector2 memoryShake;
    float memoryZoom = 1f;
    string subtitle;
    float subtitleReveal, subtitleAlpha;
    bool skippable, finished, pushing;
    GUIStyle subStyle, placeStyle;
    static Texture2D pixel;

    void Awake()
    {
        Active = true;
        gm = GameManager.Instance;
        // 「漁村」的標題等開場播完再顯示
        if (gm != null) { titleWas = gm.showLevelTitle; gm.showLevelTitle = false; }
    }

    void OnDestroy()
    {
        Active = false;
        if (gm != null) gm.showLevelTitle = titleWas;
    }

    IEnumerator Start()
    {
        // 等 GameManager 換完場景（淡入的時候我們這邊還是黑的）
        while (gm != null && gm.IsLoading) yield return null;
        yield return null;
        Setup();
        skippable = true;

        // 1. 漁村的景
        float gc = Ground(133f, 6f);
        var shots = new (Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float fov)[]
        {
            // 漁港：從海上往西北看棧橋、沙灘和三排房子
            (new Vector3(62f, 13f, -96f), new Vector3(54f, 11f, -88f), new Vector3(20f, 2.5f, -58f), new Vector3(15f, 4f, -48f), 50f),
            // 市街：主街南端往北，看得到鳥居和後山
            (At(2.5f, -34f, 4.2f), At(1.5f, -22f, 4f), At(0f, 40f, 4f), At(0f, 46f, 5f), 45f),
            // 鳥居和石階：從鳥居前往上看石階
            (At(7f, 44f, 2.6f), At(4.5f, 48f, 3f), At(0f, 62f, 4f), At(0f, 80f, 5f), 50f),
            // 村長宅院：從宅院門前的路上方往東看
            (new Vector3(90f, gc + 15f, -16f), new Vector3(94f, gc + 14f, -9f), new Vector3(130f, gc + 4f, 4f), new Vector3(133f, gc + 5f, 6f), 48f),
        };
        foreach (var s in shots) yield return Shot(s.from, s.to, s.lookFrom, s.lookTo, s.fov);

        // 2. 閃過以前的回憶
        yield return Wait(0.3f);
        foreach (var (file, place) in Memories) yield return Memory(file, place);

        // 3. 轉到主角身上：白光褪掉，站在行商面前
        CutToPlayer();
        yield return FadeWhite(0f, 0.7f);
        foreach (var line in Lines) yield return Say(line);
        yield return Wait(0.4f);
        subtitle = null;
        pushing = false;

        // 4. 鏡頭飛回玩家背後
        skippable = false;
        yield return FlyToPlayer();
        Finish();
    }

    // ---------------------------------------------------------------- 準備 / 收尾

    void Setup()
    {
        var hp = PlayerHealth.Find();
        playerTf = hp != null ? hp.transform : null;
        var m = GameObject.Find("NPC_Merchant");
        merchant = m != null ? m.transform : null;
        PlaceInFrontOfMerchant();
        lockedCtrl = playerTf != null ? playerTf.GetComponent<ThirdPersonController>() : null;
        if (lockedCtrl != null)
        {
            lockedCtrl.LockActions();
            lockedCtrl.inputEnabled = false;
            lockedCtrl.StopHorizontalMotion();
        }
        VitalsHud.Hidden = true;
        InteractPrompt.Block(this);
        tpc = FindAnyObjectByType<ThirdPersonCamera>();
        vcam = new GameObject("CM VillageOpening").AddComponent<CinemachineCamera>();
        if (tpc != null && tpc.cinemachineCamera != null) vcam.Lens = tpc.cinemachineCamera.Lens;
        vcam.Priority = 1000; // 蓋過玩家的鏡頭；刪掉之後回到玩家鏡頭
        vcam.Prioritize();
        letterbox = 1f;
    }

    /// <summary>主角出生在行商面前（面對他）。找不到行商就留在場景原本的出生點</summary>
    void PlaceInFrontOfMerchant()
    {
        if (playerTf == null || merchant == null) return;
        Vector3 fwd = Flat(merchant.forward);
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.back;
        fwd.Normalize();
        Vector3 p = merchant.position + fwd * 2.3f;
        p.y = Ground(p.x, p.z) + 0.05f;
        // 地上有東西（橋、平台）就站在上面（行商自己、玩家自己不算）
        foreach (var hit in Physics.RaycastAll(new Vector3(p.x, p.y + 2.5f, p.z), Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            var t = hit.collider.transform;
            if (t.IsChildOf(playerTf) || t.IsChildOf(merchant)) continue;
            p.y = Mathf.Max(p.y, hit.point.y + 0.05f);
        }
        var cc = playerTf.GetComponent<CharacterController>();
        bool ccOn = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        playerTf.SetPositionAndRotation(p, Quaternion.LookRotation(-fwd));
        if (cc != null) cc.enabled = ccOn;
        Physics.SyncTransforms();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        if (vcam != null) Destroy(vcam.gameObject);
        vcam = null;
        if (tpc != null && playerTf != null)
        {
            tpc.yaw = playerTf.eulerAngles.y;
            tpc.pitch = 12f;
            tpc.SnapToTarget();
        }
        if (lockedCtrl != null)
        {
            lockedCtrl.UnlockActions();
            lockedCtrl.SuppressJump(0.4f); // 跳過用的 Space 不要變成跳
            var hp = lockedCtrl.GetComponent<PlayerHealth>();
            if (hp == null || !hp.IsDead) lockedCtrl.inputEnabled = true;
            lockedCtrl = null;
        }
        VitalsHud.Hidden = false;
        InteractPrompt.Unblock(this);
        black = white = memoryAlpha = letterbox = 0f;
        subtitle = null;
        skippable = false;
        if (gm != null)
        {
            gm.showLevelTitle = titleWas;
            var level = gm.FindLevel(SceneManager.GetActiveScene().name);
            if (titleWas && level != null && !string.IsNullOrEmpty(level.title)) VictoryBanner.ShowSmall(level.title, "", new Color(0.85f, 0.9f, 1f));
        }
        StoryProgress.Current.openingSeen = true;
        if (gm != null) gm.SaveGame("在行商面前醒來");
        Destroy(gameObject, 0.1f);
    }

    void Update()
    {
        if (!skippable || finished || GameUI.OverlayOpen || Time.timeScale <= 0f) return;
        var kb = Keyboard.current;
        bool skip = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || PadInput.ConfirmPressed;
        if (!skip) return;
        StopAllCoroutines();
        if (playerTf == null) Setup();
        Finish();
    }

    // ---------------------------------------------------------------- 鏡頭

    IEnumerator Shot(Vector3 from, Vector3 to, Vector3 lookFrom, Vector3 lookTo, float fov)
    {
        from = Free(from);
        to = Free(to);
        SetLens(fov);
        for (float t = 0f; t < ShotSeconds; t += Time.deltaTime)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / ShotSeconds);
            Vector3 p = Vector3.Lerp(from, to, u), l = Vector3.Lerp(lookFrom, lookTo, u);
            Place(p, Quaternion.LookRotation(l - p), t == 0f);
            black = Mathf.Max(1f - t / ShotFade, Mathf.Clamp01((t - (ShotSeconds - ShotFade)) / ShotFade));
            yield return null;
        }
        black = 1f;
    }

    /// <summary>主角的臉：斜前方、頭的高度，慢慢推近</summary>
    void CutToPlayer()
    {
        black = 0f;
        memoryAlpha = 0f;
        if (playerTf == null || vcam == null) return;
        Vector3 face = Face(playerTf, 1.55f);
        Vector3 fwd = Flat(playerTf.forward).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        // 主角面對行商：鏡頭在主角的斜前方（行商的肩膀旁邊），被東西擋住就換另一邊
        Vector3 a = face + fwd * 2.1f + right * 1.1f + Vector3.up * 0.15f, b = face + fwd * 2.1f - right * 1.1f + Vector3.up * 0.15f;
        Vector3 cam = Clear(face, a) >= Clear(face, b) ? a : b;
        SetLens(38f);
        Place(cam, Quaternion.LookRotation(face - cam), true);
        pushing = true;
        StartCoroutine(PushIn(face));
    }

    IEnumerator PushIn(Vector3 face)
    {
        while (pushing && vcam != null)
        {
            Vector3 to = face - camPos;
            if (to.magnitude > 1.3f) camPos += to.normalized * 0.12f * Time.deltaTime;
            Place(camPos, Quaternion.LookRotation(face - camPos), false);
            yield return null;
        }
    }

    /// <summary>鏡頭從主角的臉順順地飛到玩家背後（終點每幀跟著玩家鏡頭的實際位置），接上去不會跳</summary>
    IEnumerator FlyToPlayer()
    {
        if (vcam == null || tpc == null || tpc.cinemachineCamera == null || playerTf == null) yield break;
        tpc.yaw = playerTf.eulerAngles.y;
        tpc.pitch = 12f;
        tpc.SnapToTarget();
        var playerCam = tpc.cinemachineCamera;
        var oldStandby = playerCam.StandbyUpdate;
        playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
        Vector3 fromPos = camPos;
        Quaternion fromRot = camRot;
        float fromFov = vcam.Lens.FieldOfView, toFov = playerCam.Lens.FieldOfView;
        const float seconds = 1.5f;
        yield return null;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            var state = playerCam.State;
            float u = Mathf.Clamp01(t / seconds);
            float e = u * u * u * (u * (u * 6f - 15f) + 10f);
            camPos = Vector3.Lerp(fromPos, state.GetFinalPosition(), e) + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.5f);
            camRot = Quaternion.Slerp(fromRot, state.GetFinalOrientation(), e);
            vcam.transform.SetPositionAndRotation(camPos, camRot);
            var lens = vcam.Lens;
            lens.FieldOfView = Mathf.Lerp(fromFov, toFov, e);
            vcam.Lens = lens;
            letterbox = 1f - e;
            yield return null;
        }
        playerCam.StandbyUpdate = oldStandby;
    }

    void Place(Vector3 p, Quaternion r, bool cut)
    {
        camPos = p;
        camRot = r;
        if (vcam == null) return;
        vcam.transform.SetPositionAndRotation(p, r);
        if (cut) vcam.PreviousStateIsValid = false;
    }

    void SetLens(float fov)
    {
        if (vcam == null) return;
        var lens = vcam.Lens;
        lens.FieldOfView = fov;
        vcam.Lens = lens;
    }

    // ---------------------------------------------------------------- 回憶、字幕

    /// <summary>一張回憶：白光一閃、圖褪色地晃一下，又被白光蓋掉</summary>
    IEnumerator Memory(string file, string place)
    {
        memoryTex = Resources.Load<Texture2D>(file);
        memoryPlace = place;
        GameAudio.Play("SFX/Gen/swing_whoosh_2", Vector3.zero, 0.55f, 0.55f, 0.05f, 0f);
        GameAudio.Play("SFX/Gen/hit_impact_1", Vector3.zero, 0.35f, 0.45f, 0f, 0f);
        black = 0f;
        white = 1f;
        memoryAlpha = 1f;
        const float hold = 0.75f;
        for (float t = 0f; t < hold; t += Time.deltaTime)
        {
            float u = t / hold;
            white = Mathf.Max(Mathf.Clamp01(1f - t / 0.18f), Mathf.Clamp01((u - 0.82f) / 0.18f));
            memoryZoom = Mathf.Lerp(1.08f, 1f, u);
            float k = Mathf.Clamp01(1f - u * 1.5f) * 9f;
            memoryShake = new Vector2(Random.Range(-k, k), Random.Range(-k, k));
            yield return null;
        }
        white = 1f;
        memoryAlpha = 0f;
        yield return Wait(0.06f);
    }

    IEnumerator Say(string line)
    {
        subtitle = line;
        subtitleReveal = 0f;
        float seconds = 0.9f + line.Length * 0.11f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            subtitleReveal = Mathf.Clamp01(t / (line.Length * 0.07f + 0.01f));
            subtitleAlpha = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((seconds - t) / 0.3f);
            yield return null;
        }
        subtitleAlpha = 0f;
        yield return Wait(0.15f);
    }

    IEnumerator FadeWhite(float to, float seconds)
    {
        float from = white;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            white = Mathf.Lerp(from, to, t / seconds);
            yield return null;
        }
        white = to;
    }

    static IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
    }

    // ---------------------------------------------------------------- 工具

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    /// <summary>地形的高度（不算房子、樹）</summary>
    static float Ground(float x, float z)
    {
        var terrain = Terrain.activeTerrain;
        return terrain != null ? terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y : 0f;
    }

    /// <summary>地面（x, z）往上 h 公尺</summary>
    static Vector3 At(float x, float z, float h) => new Vector3(x, Ground(x, z) + h, z);

    /// <summary>鏡頭位置卡在東西裡面（房子、樹）的話往上挪到空的地方</summary>
    static Vector3 Free(Vector3 p)
    {
        for (int i = 0; i < 24 && Physics.CheckSphere(p, 0.5f, ~0, QueryTriggerInteraction.Ignore); i++) p.y += 1f;
        return p;
    }

    /// <summary>from → to 中間有沒有東西擋著（玩家、行商不算）：1 = 沒擋</summary>
    float Clear(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude, nearest = len;
        if (len < 0.01f) return 1f;
        foreach (var h in Physics.RaycastAll(from, d / len, len, ~0, QueryTriggerInteraction.Ignore))
        {
            var t = h.collider.transform;
            if ((playerTf != null && t.IsChildOf(playerTf)) || (merchant != null && t.IsChildOf(merchant))) continue;
            nearest = Mathf.Min(nearest, h.distance);
        }
        return nearest / len;
    }

    /// <summary>臉的位置（頭骨往上一點）；找不到頭就用身高的比例</summary>
    static Vector3 Face(Transform who, float fallbackHeight)
    {
        var a = who.GetComponentInChildren<Animator>();
        var head = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return who.position + Vector3.up * fallbackHeight * who.lossyScale.y;
        return head.position + Vector3.up * 0.07f;
    }

    // ---------------------------------------------------------------- 畫面

    void OnGUI()
    {
        if (finished || GameUI.OverlayOpen) return;
        GUI.depth = -999; // GameManager 的淡入淡出（-1000）蓋在上面
        if (pixel == null) { pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
        float w = Screen.width, h = Screen.height;

        if (memoryAlpha > 0f)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);
            if (memoryTex != null)
            {
                // 褪色的舊照片：偏暖、有點暗，放大一點點再慢慢縮回來、晃一下
                float sw = w * memoryZoom, sh = h * memoryZoom;
                GUI.color = new Color(1f, 0.9f, 0.74f, memoryAlpha);
                GUI.DrawTexture(new Rect((w - sw) * 0.5f + memoryShake.x, (h - sh) * 0.5f + memoryShake.y, sw, sh), memoryTex, ScaleMode.ScaleAndCrop);
                GUI.color = new Color(0.25f, 0.15f, 0.05f, 0.25f * memoryAlpha);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);
            }
            else
            {
                // 還沒拍回憶圖：褪色的紙上寫地名
                var paper = MenuSkin.Paper;
                GUI.color = new Color(0.75f, 0.66f, 0.52f, memoryAlpha);
                if (paper != null) GUI.DrawTexture(new Rect(memoryShake.x, memoryShake.y, w, h), paper, ScaleMode.ScaleAndCrop);
                else GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);
                if (placeStyle == null)
                {
                    placeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                    if (MenuSkin.Font != null) placeStyle.font = MenuSkin.Font;
                }
                placeStyle.fontSize = Mathf.RoundToInt(h * 0.11f);
                placeStyle.normal.textColor = new Color(0.17f, 0.11f, 0.065f, 0.75f * memoryAlpha);
                GUI.color = Color.white;
                GUI.Label(new Rect(memoryShake.x, memoryShake.y, w, h), memoryPlace, placeStyle);
            }
        }

        if (black > 0f) { GUI.color = new Color(0f, 0f, 0f, black); GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel); }
        if (white > 0f) { GUI.color = new Color(1f, 0.98f, 0.94f, white); GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel); }

        // 上下黑邊
        float bar = h * 0.085f * Mathf.SmoothStep(0f, 1f, letterbox);
        if (bar > 0.5f)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, w, bar), pixel);
            GUI.DrawTexture(new Rect(0f, h - bar, w, bar), pixel);
        }

        // 字幕：黑邊上面一點，一個字一個字出來。淺色的字（米白，外面一圈淡淡的深色邊）
        if (!string.IsNullOrEmpty(subtitle) && subtitleAlpha > 0f)
        {
            GUI.color = Color.white;   // 上面畫黑邊時 GUI.color 是黑的，不重設的話字會被染黑
            if (subStyle == null)
            {
                subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = false };
                if (MenuSkin.Font != null) subStyle.font = MenuSkin.Font;
            }
            subStyle.fontSize = Mathf.RoundToInt(h * 0.052f);
            int n = Mathf.Clamp(Mathf.CeilToInt(subtitle.Length * subtitleReveal), 0, subtitle.Length);
            string shown = subtitle.Substring(0, n);
            var r = new Rect(0f, h - bar - h * 0.12f, w, h * 0.09f);
            subStyle.normal.textColor = new Color(0.08f, 0.05f, 0.03f, 0.55f * subtitleAlpha);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx * 2f, r.y + dy * 2f, r.width, r.height), shown, subStyle);
            subStyle.normal.textColor = new Color(1f, 0.96f, 0.86f, subtitleAlpha);
            GUI.Label(r, shown, subStyle);
        }

        if (skippable && letterbox > 0.5f)
        {
            const string action = "跳過";
            var style = GameUI.SmallRight;
            float tw = style.CalcSize(new GUIContent(action)).x;
            float x = w - 32f - tw, y = h - bar * 0.5f - 12f;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(x, y, tw, 24f), action, style);
            GameUI.Key(new Rect(x - 64f, y, 58f, 24f), "Space", 0.75f);
        }
        GUI.color = Color.white;
    }
}
