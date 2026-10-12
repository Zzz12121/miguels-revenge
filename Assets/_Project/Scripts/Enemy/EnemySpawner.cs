using UnityEngine;

/// <summary>
/// 生小怪的共用工具：在指定位置的地面生出一隻小怪，播「從地底鑽出來」的動畫，然後開始追玩家。
/// 地城房間（DungeonRoom）和 Boss 的召喚技能都用這個。
/// </summary>
public static class EnemySpawner
{
    /// <summary>
    /// groundPos：要出現的位置（地面上）。home：玩家死掉時小怪走回去的位置，同時讓小怪進入「不管多遠都追」的模式。
    /// 回傳生出來的小怪；prefab 上沒有 EnemyAI 就回傳 null。
    /// </summary>
    public static EnemyAI SpawnFromGround(GameObject prefab, Vector3 groundPos, Vector3 home, Transform parent, Transform lookAt)
    {
        if (prefab == null) return null;

        Vector3 look = lookAt != null ? lookAt.position - groundPos : Vector3.forward;
        look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

        var go = Object.Instantiate(prefab, groundPos + Vector3.up * 1.05f, rot, parent);
        go.name = prefab.name;
        go.SetActive(true); // 範本如果是隱藏的小怪，複製出來也是隱藏的
        var ai = go.GetComponent<EnemyAI>();
        if (ai == null) { Object.Destroy(go); return null; }

        ai.SetArenaMode(home);
        var visual = go.GetComponent<EnemyVisual>();
        if (visual != null && visual.HasAnimator)
        {
            // 有模型：貼到地面上，播從地底爬出來的動畫
            ai.BeginSpawnAt(groundPos + Vector3.up, visual.spawnDuration);
        }
        else
        {
            // 沒有模型（膠囊）：直接開始追，冒出來時小跳一下
            ai.ForceChase();
            var ctrl = go.GetComponent<ThirdPersonController>();
            if (ctrl != null) ctrl.AddImpulse(Vector3.up * 5f);
        }
        GameAudio.Play("SFX/RPG/cloth3", groundPos, 0.8f, 0.6f, 0.1f, 1f, 45f);
        return ai;
    }
}
