#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 選單：GeminiGrowUp > 套用大競技場（半徑 36m）
///
/// 修改 BossRoom.prefab：
///   - 競技場半徑 18 → 36（地板重新產生，約 1200 塊不規則石板）
///   - 環形牆依新半徑重建（96 段），入口大門移到新的南側
///   - Boss 起始位置跟著放大
///   - Boss 技能數值套用放大版（光束寬度 ×2，拍地範圍 ×3，怒吼 80%；普攻距離不變）
/// 之後請執行「更新第二層」讓走廊接到新的入口。
///
/// 所有數值都是「直接設定成目標值」，重複執行不會越放越大。
/// </summary>
public static class ArenaResizer
{
    const float NewRadius = 36f;
    const int WallSegments = 96;
    const float WallHeight = 7f;
    const float WallThickness = 2f;

    [MenuItem("GeminiGrowUp/套用大競技場（半徑 36m）")]
    public static void Apply()
    {
        string path = Floor02Builder.FindBossRoomPrefab();
        if (path == null)
        {
            EditorUtility.DisplayDialog("找不到 BossRoom", "專案裡找不到名為 BossRoom 的 Prefab。", "OK");
            return;
        }
        if (!EditorUtility.DisplayDialog("套用大競技場",
                "會修改 BossRoom.prefab（地板、牆、大門、Boss 數值）。\n建議先備份或 commit。要繼續嗎？",
                "繼續", "取消"))
            return;

        var wallMat = BossArenaBuilder.GetOrCreateMaterial("M_ArenaWall", new Color(0.22f, 0.21f, 0.25f));
        var doorMat = BossArenaBuilder.GetOrCreateMaterial("M_BossDoor", new Color(0.12f, 0.1f, 0.14f));

        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            var floor = root.GetComponentInChildren<ArenaFloor>(true);
            var bc = root.GetComponentInChildren<BossController>(true);
            if (floor == null || bc == null)
            {
                Debug.LogError("[GeminiGrowUp] BossRoom 裡找不到 ArenaFloor 或 BossController，沒有修改。");
                return;
            }
            Vector3 center = floor.transform.position;

            // ---------- 地板 ----------
            floor.radius = NewRadius;
            floor.bigRockCount = 16;       // 面積變 4 倍，岩石多一點
            floor.bigRockSpacing = 9f;
            floor.smallRockCount = 20;
            floor.tileThickness = 2.5f;
            floor.keepFlatRadius = 4f;
            floor.riseWaveSpeed = 28f;     // 升起動畫的總時間維持差不多
            floor.Generate();

            // ---------- 環形牆 ----------
            var walls = Floor02Builder.FindDeep(root.transform, "Walls");
            if (walls == null)
            {
                walls = new GameObject("Walls").transform;
                walls.SetParent(floor.transform.parent != null ? floor.transform.parent : root.transform, false);
            }
            for (int i = walls.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(walls.GetChild(i).gameObject);

            float wallCenterR = NewRadius + WallThickness * 0.5f;
            float segWidth = 2f * Mathf.PI * (NewRadius + WallThickness) / WallSegments * 1.05f;
            for (int i = 0; i < WallSegments; i++)
            {
                float a = i * Mathf.PI * 2f / WallSegments;
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"Wall_{i:00}";
                seg.transform.SetParent(walls, false);
                seg.transform.position = center + dir * wallCenterR + Vector3.up * (WallHeight * 0.5f - 1f);
                seg.transform.rotation = Quaternion.LookRotation(dir);
                seg.transform.localScale = new Vector3(segWidth, WallHeight, WallThickness);
                seg.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
                seg.isStatic = true;
            }

            // ---------- 大門（移到新的南側入口） ----------
            Floor02Builder.BuildGate(root, center, NewRadius, bc, wallMat, doorMat);

            // ---------- Boss ----------
            bc.transform.SetPositionAndRotation(center + new Vector3(0f, 0f, 12f), Quaternion.Euler(0f, 180f, 0f));
            bc.autoStart = false;
            // 普攻（骨棒）的距離
            bc.clubRange = 6.5f;
            // 拍地：範圍 ×3，衝擊波速度 / 厚度 ×2（不然要等很久才擴散完）
            bc.slamRange = 48f;
            bc.slamApproachDistance = 18f;
            bc.slamWaveSpeed = 22f;
            bc.slamWaveWidth = 2.4f;
            // 怒吼：80% 完全隨機
            bc.collapseRatio = 0.8f;
            bc.playerCollapseRadius = 3.2f;
            bc.safeRadius = 7f;
            // 光束：原地發射、寬度 ×2（厚度不變，才跳得過）、會打碎岩石
            bc.beamDiameter = 2f;
            bc.beamMinAimDistance = 6f;
            bc.beamChargeTime = 2f;
            bc.beamBreaksPillars = true;
            bc.beamPillarBreakTime = 0.3f;

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // 不再自動重建 Floor02（會蓋掉 PlayerRig 和小怪），改用「更新第二層」把走廊接到新的入口
        Debug.Log("[GeminiGrowUp] BossRoom 已套用大競技場。如果競技場大小有變，請再執行「GeminiGrowUp → 更新第二層（寬走廊 + 小怪）」讓走廊接上新的入口。");
    }
}
#endif
