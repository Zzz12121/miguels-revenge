using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單 GeminiGrowUp > 道場 > 建立開場演出（推門 → 老師父走出來）（也會把玩家起點移到大門外）
/// 在目前開著的道場場景（DojoArena_ArchitecturePreview）裡：
///   1. 找到（或放進）老師父 PF_DojoMaster，讓他坐在離大門最近的茶几坐墊上
///   2. 建立 DojoOpeningCutscene 物件：坐墊位置、老師父走出來的路線、各個鏡頭
/// 路線和鏡頭都是場景裡的物件，可以直接拖動調整（調鏡頭：Scene 視窗轉到想要的畫面 → 選鏡頭物件 → Ctrl+Shift+F）。
/// 可以 Ctrl+Z 復原；建好之後記得存場景。
/// </summary>
public static class DojoOpeningCutsceneInstaller
{
    const string MasterPrefabPath = "Assets/_Project/Prefabs/NPC/PF_DojoMaster.prefab";
    // 道場主屋（MainDojo）茶室那張茶几的位置：路線和鏡頭是照這裡的牆和門洞排好的
    static readonly Vector3 MainTeaTable = new Vector3(14f, 1.24f, 90f);
    static readonly Vector3 PreferredCushion = new Vector3(13.14f, 1.34f, 91.65f);

    [MenuItem("GeminiGrowUp/道場/建立開場演出（推門 → 老師父走出來）")]
    static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var gate = Object.FindAnyObjectByType<DojoCourtyardGate>(FindObjectsInactive.Include);
        if (gate == null)
        {
            EditorUtility.DisplayDialog("道場開場演出", "目前的場景裡找不到大門（DojoCourtyardGate）。\n請先打開 DojoArena_ArchitecturePreview 場景。", "OK");
            return;
        }

        var old = Object.FindAnyObjectByType<DojoOpeningCutscene>(FindObjectsInactive.Include);
        if (old != null)
        {
            if (!EditorUtility.DisplayDialog("道場開場演出", "場景裡已經有開場演出了。\n要刪掉重建嗎？（你調整過的鏡頭、路線、對話都會回到預設）", "重建", "取消")) return;
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        // ---- 老師父 ----
        var master = FindMaster();
        if (master == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("道場開場演出", "找不到老師父的 Prefab：\n" + MasterPrefabPath + "\n\n請先執行「GeminiGrowUp > 道場 > 建立老師父 Prefab」。", "OK");
                return;
            }
            master = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, scene)).transform;
            Undo.RegisterCreatedObjectUndo(master.gameObject, "放置老師父");
        }

        // ---- 離大門最近的茶几、坐墊 ----
        Vector3 gatePos = gate.transform.position;
        var table = AllInScene("TeaTableAndCushions").OrderBy(t => FlatDistance(t.position, gatePos)).FirstOrDefault();
        if (table == null)
        {
            EditorUtility.DisplayDialog("道場開場演出", "場景裡找不到茶几（TeaTableAndCushions）。", "OK");
            return;
        }
        bool preset = FlatDistance(table.position, MainTeaTable) < 1.5f;
        var cushions = table.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("FloorCushion")).ToList();
        Transform cushion = preset
            ? cushions.OrderBy(c => FlatDistance(c.position, PreferredCushion)).FirstOrDefault()
            : cushions.OrderByDescending(c => FlatDistance(c.position, gatePos)).FirstOrDefault();
        Vector3 tableCenter = TableCenter(table);
        Vector3 seat = cushion != null ? cushion.position : tableCenter - FlatDir(tableCenter - gatePos) * 1.6f;
        seat.y = cushion != null ? Top(cushion) : table.position.y;
        Vector3 face = DominantAxis(table, tableCenter - seat);

        // ---- 演出物件 ----
        var root = new GameObject("DojoOpeningCutscene");
        SceneManager_Move(root, scene);
        Undo.RegisterCreatedObjectUndo(root, "建立道場開場演出");
        var cut = root.AddComponent<DojoOpeningCutscene>();
        cut.gate = gate;
        cut.master = master;
        cut.holdTeaCup = true;

        var sit = new GameObject("SitPoint").transform;
        sit.SetParent(root.transform, false);
        sit.SetPositionAndRotation(seat, Quaternion.LookRotation(face));
        cut.sitPoint = sit;

        var pathRoot = new GameObject("Path").transform;
        pathRoot.SetParent(root.transform, false);
        var shotRoot = new GameObject("Shots").transform;
        shotRoot.SetParent(root.transform, false);

        if (preset) BuildMainDojoPreset(cut, pathRoot, shotRoot);
        else BuildGeneric(cut, pathRoot, shotRoot, seat, face, gate);

        // 會面點：庭院中間，藍色箭頭指向主屋（老師父停在箭頭那側，玩家停在另一側）
        Vector3 inward = GateInward(gate, tableCenter);
        var meet = new GameObject("MeetPoint").transform;
        meet.SetParent(root.transform, false);
        Vector3 meetPos = preset ? new Vector3(0f, 0f, 36f) : gatePos + inward * 20f;
        meetPos.y = gatePos.y;
        meet.SetPositionAndRotation(meetPos, Quaternion.LookRotation(inward));
        cut.meetPoint = meet;

        // 編輯器裡也先把老師父放到坐墊上（實際坐姿在遊戲開始時對齊）
        Undo.RecordObject(master, "放置老師父");
        master.SetPositionAndRotation(seat, Quaternion.LookRotation(face));

        // 玩家起點移到大門外（面向大門），一開始走過去按 E 就能觸發
        string playerNote = MovePlayerOutsideGate(gate, inward);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        EditorUtility.DisplayDialog("道場開場演出",
            $"已建立開場演出：\n茶几：{PathOf(table)}\n路線 {cut.path.Count} 點、鏡頭 {cut.shots.Count} 個。\n\n" +
            (preset ? "" : "⚠ 這張茶几不是預設的主屋茶室，只建了簡單的路線和鏡頭，請自己在場景裡調整。\n\n") +
            playerNote + "\n\n按 Play → 走到大門按 E 推門就會開始。\n對話內容在 DojoOpeningCutscene 的 Lines 裡改。\n記得 Ctrl+S 存場景。", "OK");
    }

    // ---------------- 主屋茶室的預設路線、鏡頭 ----------------
    // 茶室在主屋東側（x 11～17），出口在 x=11、z 83～86 的門洞；大廳正門在 z=78 中間；正門台階在 z 72.6～75；
    // 庭院石板路沿著 x=0 一路通到大門（z=5）。

    static void BuildMainDojoPreset(DojoOpeningCutscene cut, Transform pathRoot, Transform shotRoot)
    {
        Point(cut, pathRoot, new Vector3(11.9f, 1.2f, 91.0f));  // 0    茶几旁站起來
        Point(cut, pathRoot, new Vector3(11.9f, 1.2f, 84.6f));  // 6.4  茶室門洞前
        Point(cut, pathRoot, new Vector3(9.5f, 1.2f, 84.5f));   // 8.8  走出茶室
        Point(cut, pathRoot, new Vector3(1.2f, 1.2f, 79.6f));   // 18.4 大廳、正門內
        Point(cut, pathRoot, new Vector3(0f, 1.2f, 76.6f));     // 21.7 緣側
        Point(cut, pathRoot, new Vector3(0f, 0f, 72.3f));       // 26.0 台階下（再來沿石板路走到會面點，老師父停在 z≈38）

        var s = Shot(cut, shotRoot, "Shot_01_推門特寫", DojoCutsceneShot.Kind.PlayerCloseUp, Vector3.zero, Vector3.forward, 36f);
        s.masterWalks = false; s.dollySpeed = 0f;
        // 喝茶：從茶室東側、跟桌面差不多高往西拍，茶壺在畫面中間、老師父在右邊，後面看得到門洞外的櫻花（Kevin 選的畫面）
        s = Shot(cut, shotRoot, "Shot_02_老師父喝茶", DojoCutsceneShot.Kind.Fixed, new Vector3(16.7f, 2.2f, 90.0f), new Vector3(12.0f, 2.0f, 90.0f), 50f);
        s.masterWalks = false; s.dollySpeed = 0.12f; s.dollyMinDistance = 1.8f;
        s = Shot(cut, shotRoot, "Shot_03_走出茶室", DojoCutsceneShot.Kind.Fixed, new Vector3(16.4f, 2.4f, 86.6f), new Vector3(10.8f, 1.8f, 87.3f), 40f);
        s.startMeters = 2.0f; s.dollySpeed = 0.3f;
        // 主屋正面：台階前低角度往上拍屋簷和正門（Kevin 選的畫面），老師父從正門走出來、走下台階
        s = Shot(cut, shotRoot, "Shot_04_走下台階", DojoCutsceneShot.Kind.Fixed, new Vector3(2.0f, 1.2f, 68.5f), new Vector3(3.0f, 5.0f, 78.0f), 55f);
        s.startMeters = 21.4f; s.dollySpeed = 0.3f;
        s = Shot(cut, shotRoot, "Shot_05_庭院石板路", DojoCutsceneShot.Kind.Fixed, new Vector3(2.4f, 1.1f, 49.5f), new Vector3(0f, 1.5f, 55.5f), 40f);
        s.startMeters = 42.5f; s.dollySpeed = 0.3f;
        Shot(cut, shotRoot, "Shot_06_高空俯視會面", DojoCutsceneShot.Kind.Meeting, Vector3.zero, Vector3.forward, 40f);
    }

    // ---------------- 其他茶几：簡單版 ----------------

    static void BuildGeneric(DojoOpeningCutscene cut, Transform pathRoot, Transform shotRoot, Vector3 seat, Vector3 face, DojoCourtyardGate gate)
    {
        Vector3 inward = -FlatDir(seat - gate.transform.position);
        Vector3 side = Vector3.Cross(Vector3.up, face);
        Point(cut, pathRoot, seat + side * 0.9f);

        var s = Shot(cut, shotRoot, "Shot_01_推門特寫", DojoCutsceneShot.Kind.PlayerCloseUp, Vector3.zero, Vector3.forward, 36f);
        s.masterWalks = false; s.dollySpeed = 0f;
        Vector3 cam = seat + face * 3.2f + side * 1.2f + Vector3.up * 1.1f;
        s = Shot(cut, shotRoot, "Shot_02_老師父喝茶", DojoCutsceneShot.Kind.Fixed, cam, seat + Vector3.up * 0.8f, 38f);
        s.masterWalks = false;
        Shot(cut, shotRoot, "Shot_03_高空俯視會面", DojoCutsceneShot.Kind.Meeting, Vector3.zero, Vector3.forward, 40f);
    }

    // ---------------- 小工具 ----------------

    [MenuItem("GeminiGrowUp/道場/把玩家起點移到大門外")]
    static void MovePlayerMenu()
    {
        var gate = Object.FindAnyObjectByType<DojoCourtyardGate>(FindObjectsInactive.Include);
        if (gate == null) { EditorUtility.DisplayDialog("道場", "目前的場景裡找不到大門（DojoCourtyardGate）。", "OK"); return; }
        var table = AllInScene("TeaTableAndCushions").OrderBy(t => FlatDistance(t.position, gate.transform.position)).FirstOrDefault();
        Vector3 inward = GateInward(gate, table != null ? table.position : gate.transform.position + gate.transform.forward);
        string note = MovePlayerOutsideGate(gate, inward);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorUtility.DisplayDialog("道場", note + "\n\n記得 Ctrl+S 存場景。", "OK");
    }

    /// <summary>大門朝向庭院（茶几那一側）的水平方向</summary>
    static Vector3 GateInward(DojoCourtyardGate gate, Vector3 towards)
    {
        Vector3 f = FlatDir(gate.transform.forward);
        return Vector3.Dot(f, towards - gate.transform.position) >= 0f ? f : -f;
    }

    /// <summary>把 PlayerRig 整組移到大門外 4 公尺、對準門縫，玩家面向大門</summary>
    static string MovePlayerOutsideGate(DojoCourtyardGate gate, Vector3 inward)
    {
        var ph = Object.FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        if (ph == null) return "⚠ 場景裡找不到玩家（PlayerHealth），玩家起點沒有移動。";
        var rigGo = PrefabUtility.GetOutermostPrefabInstanceRoot(ph.gameObject);
        Transform rig = rigGo != null ? rigGo.transform : ph.transform.root;
        var cc = ph.GetComponent<CharacterController>();
        float feetOffset = cc != null ? (cc.center.y - cc.height * 0.5f) * ph.transform.lossyScale.y : 0f;
        Vector3 want = gate.transform.position - inward * 4f;
        Vector3 delta = want - ph.transform.position;
        delta.y = gate.transform.position.y + 0.05f - (ph.transform.position.y + feetOffset);
        Undo.RecordObject(rig, "玩家移到大門外");
        rig.position += delta;
        Undo.RecordObject(ph.transform, "玩家面向大門");
        ph.transform.rotation = Quaternion.LookRotation(inward);
        return $"玩家起點已移到大門外：{PathOf(rig)}";
    }

    static void Point(DojoOpeningCutscene cut, Transform parent, Vector3 pos)
    {
        var t = new GameObject("P" + cut.path.Count.ToString("00")).transform;
        t.SetParent(parent, false);
        t.position = pos;
        cut.path.Add(t);
    }

    static DojoCutsceneShot Shot(DojoOpeningCutscene cut, Transform parent, string name, DojoCutsceneShot.Kind kind, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        if (kind == DojoCutsceneShot.Kind.Fixed) go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        var s = go.AddComponent<DojoCutsceneShot>();
        s.kind = kind;
        s.fov = fov;
        cut.shots.Add(s);
        return s;
    }

    static Transform FindMaster()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MasterPrefabPath);
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var src = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject);
            if (prefab != null && src == prefab && PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) == t.gameObject) return t;
            if (t.name.StartsWith("PF_DojoMaster")) return t;
        }
        return null;
    }

    static IEnumerable<Transform> AllInScene(string name) =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name == name);

    static void SceneManager_Move(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    static float FlatDistance(Vector3 a, Vector3 b) { a.y = b.y = 0f; return Vector3.Distance(a, b); }

    static Vector3 FlatDir(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.forward; }

    static Vector3 TableCenter(Transform table)
    {
        var top = table.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Table") && t != table);
        return top != null ? top.position : table.position;
    }

    /// <summary>坐墊表面的高度（模型外框的頂端）</summary>
    static float Top(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return t.position.y + 0.08f;
        return rs.Max(r => r.bounds.max.y);
    }

    /// <summary>面向茶几：取茶几自己的前後 / 左右軸裡最接近的那一個（坐得正正的）</summary>
    static Vector3 DominantAxis(Transform table, Vector3 dir)
    {
        dir.y = 0f;
        Vector3 f = FlatDir(table.forward), r = FlatDir(table.right);
        float df = Vector3.Dot(dir, f), dr = Vector3.Dot(dir, r);
        Vector3 axis = Mathf.Abs(df) >= Mathf.Abs(dr) ? f * Mathf.Sign(df) : r * Mathf.Sign(dr);
        return axis.sqrMagnitude > 0.0001f ? axis : Vector3.forward;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        for (; t != null; t = t.parent) parts.Insert(0, t.name);
        return string.Join("/", parts);
    }
}
