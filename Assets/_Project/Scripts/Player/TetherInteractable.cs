using UnityEngine;

/// <summary>
/// 選配標記：覆寫雙極牽引技能對物件的自動判斷。
/// 未標記的固定 Collider 會被視為錨點；有 Rigidbody 或敵人控制器的物件會被視為可拉動。
/// </summary>
[DisallowMultipleComponent]
public sealed class TetherInteractable : MonoBehaviour
{
    public enum TetherMode
    {
        Auto,
        StaticAnchor,
        Pullable,
        FloatingTerrainChunk
    }

    [Tooltip("固定地形只能把玩家拉過去；浮空地塊可拉近後再次瞄準並投擲。")]
    public TetherMode mode = TetherMode.Auto;
    [Tooltip("玩家飛向表面時預留的距離。")]
    [Min(0f)] public float playerStopDistance = 1.05f;
    [Tooltip("浮空地塊投擲速度。")]
    [Min(1f)] public float throwSpeed = 24f;

    public bool IsFloatingChunk => mode == TetherMode.FloatingTerrainChunk;
}
