using UnityEngine;

/// <summary>標記可被 VFX 指定技選取的敵人或互動物件。</summary>
[DisallowMultipleComponent]
public sealed class VoidRendTargetable : MonoBehaviour
{
    [SerializeField] Transform aimPoint;
    [SerializeField] bool animateHitReaction = true;
    Animator cachedAnimator;
    int hitIndex;

    static readonly string[] HitStates =
    {
        "Hit", "hit_to_body_02", "hit_to_head", "hit_to_side", "hit_to_stomach"
    };

    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position + Vector3.up;

    void Awake() => cachedAnimator = GetComponentInChildren<Animator>(true);

    public void PlayHitReaction()
    {
        if (!animateHitReaction || cachedAnimator == null || !cachedAnimator.isActiveAndEnabled) return;
        string state = HitStates[hitIndex++ % HitStates.Length];
        cachedAnimator.CrossFadeInFixedTime(state, 0.06f, 0, 0f);
    }
}

/// <summary>只供技能靶場使用的往返移動目標。</summary>
public sealed class VoidRendMovingTarget : MonoBehaviour
{
    public Vector3 localOffset = new Vector3(3.5f, 0f, 0f);
    public float travelSeconds = 2.4f;
    Vector3 start;

    void Awake() => start = transform.position;

    void Update()
    {
        float duration = Mathf.Max(0.2f, travelSeconds);
        float t = Mathf.PingPong(Time.time / duration, 1f);
        transform.position = start + Vector3.Lerp(-localOffset, localOffset, Mathf.SmoothStep(0f, 1f, t));
    }
}
