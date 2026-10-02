using UnityEngine;

/// <summary>
/// 可以被玩家攻擊的東西（Boss、小怪、可破壞物件…）都實作這個介面。
/// 玩家的攻擊只認這個介面，所以之後新增敵人不用改玩家的程式。
/// </summary>
public interface IDamageable
{
    /// <summary>受到傷害。hitPoint 是打中的位置（可用來生特效）</summary>
    void TakeDamage(float amount, Vector3 hitPoint);
    bool IsDead { get; }
}
