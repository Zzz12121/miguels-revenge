using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 選單 GeminiGrowUp > 測試 > 在目前場景建立攀爬測試台：
/// 在目前打開的場景（建議 MovementTest）玩家附近找一塊空地，放一個 ClimbTestCourse（一排不同高度的石牆和石頭）。
/// 按 Play 之後石頭才會長出來；編輯模式下用半透明的方塊 / 石頭形狀顯示位置（Scene 視窗的 Gizmos 要開著），可以直接拖動整個測試台。
/// 場景已經有測試台時，可以選擇「重設並移到玩家附近的空地」。
/// </summary>
public static class ClimbTestCourseInstaller
{
    [MenuItem("GeminiGrowUp/測試/在目前場景建立攀爬測試台")]
    static void Create()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("攀爬測試台", "請先停止 Play。", "OK"); return; }
        var scene = EditorSceneManager.GetActiveScene();
        var existing = Object.FindObjectsByType<ClimbTestCourse>(FindObjectsSortMode.None);
        if (existing.Length > 0)
        {
            Selection.activeGameObject = existing[0].gameObject;
            int choice = EditorUtility.DisplayDialogComplex("攀爬測試台",
                "這個場景已經有攀爬測試台了。", "重設並移到玩家附近的空地", "取消", "再建一個");
            if (choice == 1) return;
            if (choice == 0) foreach (var e in existing) Undo.DestroyObjectImmediate(e.gameObject);
        }

        var go = new GameObject("ClimbTestCourse");
        Undo.RegisterCreatedObjectUndo(go, "建立攀爬測試台");
        var course = go.AddComponent<ClimbTestCourse>();

        var player = ClimbTestCourse.FindPlayerBody();
        bool clear = false;
        if (player != null) clear = PlaceNear(go.transform, course.LocalFootprint(), player);
        else
        {
            var view = SceneView.lastActiveSceneView;
            Vector3 p = view != null ? view.pivot : Vector3.zero;
            if (Physics.Raycast(p + Vector3.up * 20f, Vector3.down, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)) p = hit.point;
            go.transform.position = p;
        }
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        bool hasClimb = false;
        if (player != null)
        {
            var anim = player.GetComponentInChildren<Animator>(true);
            if (anim != null && anim.runtimeAnimatorController is UnityEditor.Animations.AnimatorController ac)
                hasClimb = ac.layers.Any(l => l.stateMachine.states.Any(s => s.state.name == "Climb"));
        }

        EditorUtility.DisplayDialog("攀爬測試台",
            "已經在「" + scene.name + "」建立攀爬測試台（記得存檔）。\n\n" +
            "左邊 4 面石牆：1.0 / 1.6 / 2.2 / 2.8 公尺\n右邊 3 顆石頭：1.8 / 2.6 / 3.4 公尺（斜的石壁）\n" +
            "編輯模式下是半透明的預覽（Scene 視窗上方的 Gizmos 要開著），按 Play 才會變成真的石頭。\n\n" +
            "怎麼爬：跑向石頭跳起來，往下掉的時候面向石頭、按住 W。\n" +
            "（邊緣要比腳高 0.5～2.5 公尺：2.8m 以上的要先跳起來才抓得到）\n\n" +
            "高度、位置、形狀在 ClimbTestCourse 的 Blocks 裡改；攀爬手感在 Climb 裡改。" +
            (player != null && !clear ? "\n\n⚠ 玩家附近找不到夠大的空地，先放在玩家前方，可能會跟其他東西重疊，請自己拖到空的地方。" : "") +
            (player != null && !hasClimb ? "\n\n⚠ 玩家的 Animator 還沒有 Climb 狀態：先執行 GeminiGrowUp > 安裝 玩家跳躍・下落・攀爬動畫。" : "") +
            (player == null ? "\n\n⚠ 這個場景找不到玩家，測試台放在 Scene 視窗中間。" : ""),
            "OK");
    }

    /// <summary>在玩家前後左右、由近到遠找一塊放得下整排石頭的空地；石頭排成一排、正面對著玩家</summary>
    static bool PlaceNear(Transform course, Bounds footprint, Transform player)
    {
        Physics.SyncTransforms();
        Vector3 fwd = Vector3.ProjectOnPlane(player.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3[] dirs = { fwd, -fwd, right, -right };
        float[] dists = { 5f, 7f, 9f, 11f, 13f, 16f, 20f };
        Vector3 groundUnderPlayer = Ground(player.position + Vector3.up) ?? player.position;
        foreach (float d in dists)
            foreach (var dir in dirs)
            {
                // 測試台的 -z（石頭正面）朝向玩家
                Quaternion rot = Quaternion.LookRotation(dir);
                float back = footprint.extents.z + 0.5f;
                Vector3 center = groundUnderPlayer + dir * (d + back);
                Vector3? ground = Ground(center);
                if (ground == null || Mathf.Abs(ground.Value.y - groundUnderPlayer.y) > 0.6f) continue;
                Vector3 origin = ground.Value - dir * back;
                Vector3 boxCenter = origin + rot * new Vector3(footprint.center.x, 0, footprint.center.z) + Vector3.up * 2.3f;
                Vector3 half = new Vector3(footprint.extents.x + 1f, 2f, footprint.extents.z + 1f);
                bool blocked = Physics.OverlapBox(boxCenter, half, rot, ~0, QueryTriggerInteraction.Ignore)
                    .Any(c => !c.transform.IsChildOf(player) && !c.transform.IsChildOf(course));
                if (blocked) continue;
                course.SetPositionAndRotation(origin, rot);
                return true;
            }
        course.SetPositionAndRotation(groundUnderPlayer + fwd * 6f, Quaternion.LookRotation(fwd));
        return false;
    }

    static Vector3? Ground(Vector3 p)
    {
        foreach (var h in Physics.RaycastAll(p + Vector3.up * 20f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (h.normal.y > 0.7f && h.collider.GetComponentInParent<CharacterController>() == null) return h.point;
        return null;
    }
}
