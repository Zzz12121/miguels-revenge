using UnityEngine;

/// <summary>
/// 不讓玩家站在百足駭骨身上：百足駭骨用一個大方塊碰撞（擋住玩家不能穿過去），
/// 方塊頂端離地 3 公尺多，玩家跳上去 / 招式落下來就會站在上面。
/// 玩家的腳在方塊頂端、而且在方塊範圍內時，把玩家往最近的邊推出去，掉回地面。
/// 由 SkeletonCentipedeBossAI 自動加上（分身複製時會一起帶著），不用手動放。
/// 不改玩家的移動腳本，只在玩家的 CharacterController 開著時用 Move 推一下。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkeletonCentipedeNoStand : MonoBehaviour
{
    [Tooltip("推下去的速度 (m/s)")]
    public float pushSpeed = 9f;
    [Tooltip("腳在方塊頂端往下多少以內都算「站在上面」(m)")]
    public float topTolerance = 0.6f;

    BoxCollider box;
    CharacterController player;
    float nextFind;

    void Awake() => box = GetComponent<BoxCollider>();

    void LateUpdate()
    {
        if (box == null || !box.enabled) return;
        if (player == null)
        {
            if (Time.unscaledTime < nextFind) return;
            nextFind = Time.unscaledTime + 1f;
            var hp = PlayerHealth.Find();
            if (hp != null) player = hp.GetComponent<CharacterController>();
            if (player == null) return;
        }
        if (!player.enabled || !player.gameObject.activeInHierarchy) return;

        Bounds world = box.bounds;
        float feet = player.bounds.min.y;
        if (feet < world.max.y - topTolerance) return;          // 腳在方塊頂端底下：在旁邊，不是站在上面
        if (feet > world.max.y + 0.5f) return;                  // 還在空中（跳躍中），落下來再說

        // 玩家在方塊自己座標裡的位置（方塊會跟著百足駭骨轉）
        Transform t = box.transform;
        Vector3 local = t.InverseTransformPoint(player.transform.position) - box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 scale = t.lossyScale;
        float padX = player.radius / Mathf.Max(0.0001f, Mathf.Abs(scale.x));
        float padZ = player.radius / Mathf.Max(0.0001f, Mathf.Abs(scale.z));
        float outX = half.x + padX - Mathf.Abs(local.x);        // 往 x 方向還要走多遠才離開
        float outZ = half.z + padZ - Mathf.Abs(local.z);
        if (outX <= 0f || outZ <= 0f) return;                   // 不在方塊上方

        // 往最近的邊推（世界座標的距離來比）
        float worldX = outX * Mathf.Abs(scale.x), worldZ = outZ * Mathf.Abs(scale.z);
        Vector3 dir = worldX < worldZ
            ? t.right * (local.x >= 0f ? 1f : -1f)
            : t.forward * (local.z >= 0f ? 1f : -1f);
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        float step = Mathf.Min(pushSpeed * Time.deltaTime, Mathf.Min(worldX, worldZ) + 0.05f);
        player.Move(dir.normalized * step);
    }
}
