using UnityEngine;

/// <summary>標記可被 VFX 指定技選取的敵人或互動物件。</summary>
[DisallowMultipleComponent]
public sealed class VoidRendTargetable : MonoBehaviour
{
    [SerializeField] Transform aimPoint;
    [SerializeField] bool animateHitReaction = true;
    [Tooltip("只有單一粗略碰撞器時，依模型 Bounds 建立頭、胸、手、腿的瞄準用 Trigger。")]
    [SerializeField] bool generateFallbackAimZones = true;
    Animator cachedAnimator;
    int hitIndex;

    static readonly string[] HitStates =
    {
        "Hit", "hit_to_body_02", "hit_to_head", "hit_to_side", "hit_to_stomach"
    };

    public Vector3 AimPosition => aimPoint != null ? aimPoint.position : transform.position + Vector3.up;

    void Awake()
    {
        cachedAnimator = GetComponentInChildren<Animator>(true);
        if (generateFallbackAimZones) EnsureFallbackAimZones();
    }

    void EnsureFallbackAimZones()
    {
        Collider[] existing = GetComponentsInChildren<Collider>(true);
        int useful = 0;
        foreach (Collider collider in existing)
            if (collider != null && collider.enabled && !collider.isTrigger) useful++;
        if (useful > 1 || transform.Find("VoidRendAimZones") != null) return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
            if (!found) { bounds = renderer.bounds; found = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!found || bounds.size.y < .2f) return;

        Transform zones = new GameObject("VoidRendAimZones").transform;
        zones.SetParent(transform, false);
        float height = bounds.size.y;
        float width = Mathf.Max(bounds.size.x, bounds.size.z);
        Vector3 bottom = bounds.center - Vector3.up * bounds.extents.y;

        AddSphereZone(zones, "Head", bottom + Vector3.up * (height * .88f), Mathf.Clamp(width * .16f, .12f, height * .13f));
        AddBoxZone(zones, "Torso", bottom + Vector3.up * (height * .58f), new Vector3(width * .55f, height * .34f, width * .42f));
        AddSphereZone(zones, "LeftHand", bounds.center - transform.right * (bounds.extents.x * .82f) + Vector3.up * (height * .03f), Mathf.Clamp(width * .1f, .09f, .22f));
        AddSphereZone(zones, "RightHand", bounds.center + transform.right * (bounds.extents.x * .82f) + Vector3.up * (height * .03f), Mathf.Clamp(width * .1f, .09f, .22f));
        AddCapsuleZone(zones, "LeftLeg", bottom - transform.right * (width * .16f) + Vector3.up * (height * .22f), height * .36f, width * .12f);
        AddCapsuleZone(zones, "RightLeg", bottom + transform.right * (width * .16f) + Vector3.up * (height * .22f), height * .36f, width * .12f);
    }

    void AddSphereZone(Transform parent, string label, Vector3 worldPosition, float worldRadius)
    {
        GameObject zone = CreateZone(parent, label, worldPosition);
        SphereCollider collider = zone.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = WorldToLocalDistance(zone.transform, worldRadius);
    }

    void AddCapsuleZone(Transform parent, string label, Vector3 worldPosition, float worldHeight, float worldRadius)
    {
        GameObject zone = CreateZone(parent, label, worldPosition);
        CapsuleCollider collider = zone.AddComponent<CapsuleCollider>();
        collider.isTrigger = true;
        collider.direction = 1;
        collider.radius = WorldToLocalDistance(zone.transform, worldRadius);
        collider.height = Mathf.Max(collider.radius * 2f, WorldToLocalDistance(zone.transform, worldHeight));
    }

    void AddBoxZone(Transform parent, string label, Vector3 worldPosition, Vector3 worldSize)
    {
        GameObject zone = CreateZone(parent, label, worldPosition);
        BoxCollider collider = zone.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        Vector3 scale = zone.transform.lossyScale;
        collider.size = new Vector3(worldSize.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)), worldSize.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), worldSize.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
    }

    GameObject CreateZone(Transform parent, string label, Vector3 worldPosition)
    {
        GameObject zone = new GameObject("Aim_" + label);
        zone.layer = gameObject.layer;
        zone.transform.SetParent(parent, true);
        zone.transform.position = worldPosition;
        zone.transform.rotation = transform.rotation;
        return zone;
    }

    static float WorldToLocalDistance(Transform target, float distance)
    {
        Vector3 scale = target.lossyScale;
        return distance / Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
    }

    public void PlayHitReaction()
    {
        if (!animateHitReaction || cachedAnimator == null || !cachedAnimator.isActiveAndEnabled) return;
        for (int i = 0; i < HitStates.Length; i++)
        {
            string state = HitStates[hitIndex++ % HitStates.Length];
            if (!cachedAnimator.HasState(0, Animator.StringToHash(state))) continue;
            cachedAnimator.CrossFadeInFixedTime(state, 0.06f, 0, 0f);
            break;
        }
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
