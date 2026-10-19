using UnityEngine;

/// <summary>
/// 在所有一般相機腳本的 LateUpdate 之後再次套用技能鏡頭，並在真正渲染前再確認一次。
/// 這可避免正式關卡與靶場的 ThirdPersonCamera 把 Z 技能運鏡覆寫掉。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(32000)]
public sealed class VoidRendCinematicCameraDriver : MonoBehaviour
{
    VoidRendSkillDemo owner;

    public void Claim(VoidRendSkillDemo candidate)
    {
        if (candidate != null) owner = candidate;
    }

    public void Release(VoidRendSkillDemo candidate)
    {
        if (owner == candidate) owner = null;
    }

    void LateUpdate() => owner?.ApplyQueuedCinematicCameraPose();
    void OnPreCull() => owner?.ApplyQueuedCinematicCameraPose();
}
