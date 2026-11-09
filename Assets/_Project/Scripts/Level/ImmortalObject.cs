using UnityEngine;

/// <summary>
/// 標記「不可破壞」的物件（掛在碰撞體本身或它的父物件上，例如競技場的環形牆）。
/// 玩家的攻擊掃到它時，ImmortalObjectFeedback 會在打中的位置跳出紅色六角標籤。
/// 這不是 IDamageable：打牆不算「造成傷害」，吸血 / 深淵復仇等能力不會被觸發。
/// </summary>
public class ImmortalObject : MonoBehaviour
{
    [Tooltip("留空 = 用 ImmortalObjectFeedback 的預設文字")]
    public string labelOverride = "";
}
