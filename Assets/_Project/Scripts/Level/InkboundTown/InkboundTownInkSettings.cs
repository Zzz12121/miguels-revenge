using UnityEngine;

/// <summary>Scene-local visual timing. Does not change gameplay or resident animation time.</summary>
[DisallowMultipleComponent]
public sealed class InkboundTownInkSettings : MonoBehaviour
{
    [Header("散墨／回流播放速度")]
    [Tooltip("1 = 原速；0.5 = 半速、歷時兩倍；0.25 = 四倍慢動作。只影響散墨，不影響玩家、居民動畫或戰鬥。")]
    [Range(.05f,2f)] public float inkPlaybackSpeed=.5f;
    [Header("穿透物件：老者命中黑墨")]
    public bool useProjectileHitEffect=true;
    [Tooltip("飛墨一筆／連筆四式命中的黑墨絲材質。")]
    public Material passThroughInkMaterial;
    [Tooltip("沿用飛墨一筆／連筆四式命中特效的大小倍率。")]
    [Range(.2f,2f)] public float passThroughEffectScale=1;
    [Tooltip("黑墨播放完畢後，物件由下而上聚回的時間；再除以 Ink Playback Speed。")]
    [Range(.1f,3f)] public float reassembleSeconds=.65f;
    [Range(1,12)] public int maximumPassThroughEffects=8;

    public InkboundPassThroughImpact ImpactPlayer
    {
        get
        {
            var player=GetComponent<InkboundPassThroughImpact>();
            return player?player:gameObject.AddComponent<InkboundPassThroughImpact>();
        }
    }
}
