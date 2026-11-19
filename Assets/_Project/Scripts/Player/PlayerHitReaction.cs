using UnityEngine;

/// <summary>
/// 玩家受擊時要擺哪一種姿勢。攻擊方呼叫 PlayerHealth.TakeDamage(..., reaction: PlayerHitReaction.X) 指定；
/// 不指定（Auto）時由 PlayerHealth 依擊退力道自動判斷：往上的力道夠大 = 打飛，其餘 = 原本的受擊動畫。
/// </summary>
public enum PlayerHitReaction
{
    /// <summary>自動判斷（預設）</summary>
    Auto,
    /// <summary>原本的受擊動畫（Hit / hit_to_xxx）＋擊退</summary>
    Standard,
    /// <summary>被打飛：先蜷縮、在空中張開身體往後倒、摔到地上躺一下再爬起來</summary>
    KnockedFlying,
    /// <summary>吸到毒氣：雙手摀住口鼻、身體狂抖，原地不能動</summary>
    PoisonGas,
    /// <summary>定在原地：雙臂交叉擋在臉前的防禦姿勢，不會被推著滑走</summary>
    GuardBrace,
}
