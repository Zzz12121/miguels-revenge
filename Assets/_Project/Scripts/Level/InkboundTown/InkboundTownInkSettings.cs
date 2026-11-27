using UnityEngine;

/// <summary>Scene-local visual timing. Does not change gameplay or resident animation time.</summary>
[DisallowMultipleComponent]
public sealed class InkboundTownInkSettings : MonoBehaviour
{
    [Header("散墨／回流播放速度")]
    [Tooltip("1 = 原速；0.5 = 半速、歷時兩倍；0.25 = 四倍慢動作。只影響散墨，不影響玩家、居民動畫或戰鬥。")]
    [Range(.05f,2f)] public float inkPlaybackSpeed=.5f;
}
