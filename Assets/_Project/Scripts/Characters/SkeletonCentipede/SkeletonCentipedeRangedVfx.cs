using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 百足駭骨遠程攻擊的特效：
///   RangedCharge            → 嘴前聚出幾顆能量彈，跟著頭移動、慢慢變大
///   AttackImpact("Ranged")  → 發射口閃光，能量彈扇形射向 driver.RangedTargetPoint（沒有就往正前方）
///                             launchInterval > 0 時一顆一顆依序射出，每顆射出時重新讀瞄準點
///   能量彈撞到東西或飛到最遠距離 → 爆炸特效，並發出 ProjectileHit 事件
///
/// 這裡只有畫面和碰撞位置，不扣血。隊友要做傷害時訂閱 ProjectileHit 就好（參數：爆炸位置、撞到的 Collider，可能是 null）。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeRangedVfx : MonoBehaviour
{
    [Header("能量彈")]
    public Material glowMaterial;
    public Material trailMaterial;
    [ColorUsage(true, true)] public Color coreColor = new Color(4f, 0.35f, 0.25f, 1f);
    public int projectileCount = 3;
    [Tooltip("扇形總角度")]
    public float spreadAngle = 24f;
    public float projectileSpeed = 22f;
    public float maxDistance = 45f;
    [Tooltip("拋物線最高點（公尺）")]
    public float arcHeight = 1.5f;
    [Tooltip("能量彈大小（頭到尾身長的倍數）")]
    public float projectileScale = 0.06f;
    public float chargeTime = 0.55f;

    [Header("特效")]
    public GameObject muzzlePrefab;
    public GameObject explosionPrefab;
    public float muzzleScale = 1.2f;
    public float explosionScale = 1.5f;
    public float effectLifetime = 4f;

    [Header("骨頭（Tripo 骨架編號）")]
    public string headBone = "Head_0";
    public string[] fangBones = { "bone_81", "bone_82" };
    public string tailEndBone = "bone_7";

    /// <summary>能量彈爆炸：位置、撞到的 Collider（飛到最遠距離自爆時是 null）。</summary>
    public event Action<Vector3, Collider> ProjectileHit;

    [Header("追蹤（Boss AI 設定；沒有目標就直線飛向發射時的瞄準點）")]
    [Tooltip("能量彈飛行中落點會慢慢修正到這個目標")]
    public Transform homingTarget;
    public Vector3 homingOffset = new Vector3(0f, 1f, 0f);
    [Tooltip("落點每秒最多移動幾公尺（0 = 不追蹤）")]
    public float homingRate = 0f;
    [Tooltip("飛到全程的多少比例之後就不再修正（留給玩家最後閃避的空間）")]
    [Range(0f, 1f)] public float homingUntil = 0.75f;
    [Tooltip("每顆能量彈之間的發射間隔（秒）。0 = 全部同時射出；大於玩家受傷無敵時間，後面的能量彈才打得到")]
    public float launchInterval = 0f;

    SkeletonCentipedeAnimationDriver driver;
    Transform head;
    readonly List<Transform> fangs = new();
    Transform mouth;
    float bodyLength = 1f;
    readonly List<Projectile> charging = new();
    float chargeStart;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        var bones = new Dictionary<string, Transform>();
        foreach (var t in GetComponentsInChildren<Transform>(true)) bones.TryAdd(StripPrefix(t.name), t);

        if (!bones.TryGetValue(headBone, out head))
        {
            Debug.LogWarning($"{name}: 找不到頭部骨頭 {headBone}，遠程特效停用。", this);
            enabled = false;
            return;
        }
        foreach (var f in fangBones)
            if (bones.TryGetValue(f, out var fang)) fangs.Add(fang);
        if (bones.TryGetValue(tailEndBone, out var tailEnd)) bodyLength = Vector3.Distance(head.position, tailEnd.position);

        mouth = new GameObject("RangedMouth").transform;
        mouth.SetParent(head, false);
        UpdateMouth();
    }

    void OnEnable()
    {
        if (driver == null) return;
        driver.RangedCharge += OnCharge;
        driver.AttackImpact += OnImpact;
    }

    void OnDisable()
    {
        if (driver == null) return;
        driver.RangedCharge -= OnCharge;
        driver.AttackImpact -= OnImpact;
        foreach (var p in charging) if (p != null) Destroy(p.gameObject);
        charging.Clear();
    }

    void LateUpdate()
    {
        if (charging.Count == 0) return;
        UpdateMouth();
        float grow = Mathf.Clamp01((Time.time - chargeStart) / chargeTime);
        for (int i = 0; i < charging.Count; i++)
        {
            if (charging[i] == null) continue;
            // 幾顆能量彈在嘴前繞小圈
            float angle = Time.time * 540f + i * 360f / charging.Count;
            Vector3 orbit = Quaternion.AngleAxis(angle, transform.forward) * transform.up * (bodyLength * 0.04f * grow);
            charging[i].transform.position = mouth.position + orbit;
            charging[i].SetSize(ProjectileSize * Mathf.Lerp(0.2f, 1f, grow));
        }
    }

    float ProjectileSize => bodyLength * projectileScale;

    void UpdateMouth()
    {
        // 嘴巴 = 兩根大顎中間再往前一點
        Vector3 p = head.position;
        if (fangs.Count > 0)
        {
            p = Vector3.zero;
            foreach (var f in fangs) p += f.position;
            p /= fangs.Count;
        }
        mouth.position = p + transform.forward * bodyLength * 0.05f;
    }

    void OnCharge()
    {
        foreach (var p in charging) if (p != null) Destroy(p.gameObject);
        charging.Clear();
        chargeStart = Time.time;
        UpdateMouth();
        for (int i = 0; i < projectileCount; i++)
            charging.Add(Projectile.Create(mouth.position, ProjectileSize * 0.2f, glowMaterial, trailMaterial, coreColor));
    }

    void OnImpact(string attack)
    {
        if (attack != "Ranged") return;
        UpdateMouth();

        Vector3 target = driver.RangedTargetPoint ?? mouth.position + transform.forward * maxDistance * 0.6f;
        Vector3 toTarget = target - mouth.position;
        Spawn(muzzlePrefab, mouth.position, Quaternion.LookRotation(toTarget.sqrMagnitude > 0.01f ? toTarget : transform.forward), bodyLength * 0.15f * muzzleScale);

        if (charging.Count == 0) OnCharge(); // 沒經過聚能（例如動畫被切掉一半）也照樣射
        if (volley != null) StopCoroutine(volley);
        volley = StartCoroutine(LaunchVolley());
    }

    Coroutine volley;

    /// <summary>
    /// 扇形依序射出（launchInterval = 0 就同一幀全部射出）。每一顆射出時都重新讀 RangedTargetPoint，
    /// 所以前一顆把玩家擊退之後，後面的仍會瞄準玩家當下的位置。還沒射出的能量彈留在嘴前繞圈。
    /// </summary>
    IEnumerator LaunchVolley()
    {
        int n = charging.Count;
        for (int i = 0; i < n; i++)
        {
            if (charging.Count == 0) yield break;
            var p = charging[0];
            charging.RemoveAt(0);
            if (p != null) LaunchOne(p, i, n);
            if (launchInterval > 0f && i < n - 1) yield return new WaitForSeconds(launchInterval);
        }
        volley = null;
    }

    void LaunchOne(Projectile p, int i, int n)
    {
        UpdateMouth();
        Vector3 target = driver.RangedTargetPoint ?? mouth.position + transform.forward * maxDistance * 0.6f;
        Vector3 toTarget = target - mouth.position;
        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        if (flat.sqrMagnitude < 0.01f) flat = transform.forward;

        float yaw = n > 1 ? Mathf.Lerp(-spreadAngle * 0.5f, spreadAngle * 0.5f, i / (float)(n - 1)) : 0f;
        Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * flat.normalized;
        float distance = Mathf.Min(flat.magnitude, maxDistance);
        Vector3 end = mouth.position + dir * distance;
        end.y = target.y;
        p.SetSize(ProjectileSize);
        p.Launch(mouth.position, end, projectileSpeed, arcHeight, maxDistance, transform, OnProjectileExplode);
        if (homingTarget != null && homingRate > 0f) p.SetHoming(homingTarget, homingOffset, homingRate, homingUntil);
    }

    void OnProjectileExplode(Vector3 position, Collider hit)
    {
        Spawn(explosionPrefab, position, Quaternion.identity, bodyLength * 0.15f * explosionScale);
        ProjectileHit?.Invoke(position, hit);
    }

    void Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return;
        var fx = Instantiate(prefab, position, rotation);
        fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, effectLifetime);
    }

    static string StripPrefix(string boneName)
    {
        int i = boneName.LastIndexOf(':');
        return i >= 0 ? boneName.Substring(i + 1) : boneName;
    }

    /// <summary>單顆能量彈：發光核心（粒子）+ 拖尾，沿拋物線飛，用射線檢查撞擊。</summary>
    class Projectile : MonoBehaviour
    {
        ParticleSystem core;
        TrailRenderer trail;
        Vector3 start, end;
        float speed, arc, traveled, total, maxDistance;
        Transform owner;
        Action<Vector3, Collider> onExplode;
        Transform homing;
        Vector3 homingOffset;
        float homingRate, homingUntil;

        public void SetHoming(Transform target, Vector3 offset, float rate, float until)
        {
            homing = target;
            homingOffset = offset;
            homingRate = rate;
            homingUntil = until;
        }
        bool flying;

        public static Projectile Create(Vector3 position, float size, Material glow, Material trailMat, Color color)
        {
            var go = new GameObject("SkeletonCentipede_EnergyOrb");
            go.transform.position = position;
            var p = go.AddComponent<Projectile>();

            p.core = go.AddComponent<ParticleSystem>();
            p.core.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.core.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.18f;
            main.startSpeed = 0f;
            main.startColor = color;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 64;
            var emission = p.core.emission;
            emission.rateOverTime = 90f;
            var shape = p.core.shape;
            shape.enabled = false;
            var sizeOverLife = p.core.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f)));
            var colorOverLife = p.core.colorOverLifetime;
            colorOverLife.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = glow;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            p.trail = go.AddComponent<TrailRenderer>();
            p.trail.sharedMaterial = trailMat;
            p.trail.time = 0.25f;
            p.trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            var trailColor = new Gradient();
            trailColor.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.75f), 0f), new GradientColorKey(new Color(0.8f, 0.05f, 0.03f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0f, 1f) });
            p.trail.colorGradient = trailColor;
            p.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            p.trail.emitting = false;

            p.SetSize(size);
            p.core.Play();
            return p;
        }

        public void SetSize(float size)
        {
            var main = core.main;
            main.startSize = size;
            trail.widthMultiplier = size * 0.6f;
        }

        public void Launch(Vector3 from, Vector3 to, float speed, float arc, float maxDistance, Transform owner, Action<Vector3, Collider> onExplode)
        {
            start = from;
            end = to;
            this.speed = speed;
            this.arc = arc;
            this.maxDistance = maxDistance;
            this.owner = owner;
            this.onExplode = onExplode;
            total = Mathf.Max(0.1f, Vector3.Distance(from, to));
            traveled = 0f;
            trail.Clear();
            trail.emitting = true;
            flying = true;
        }

        void Update()
        {
            if (!flying) return;
            Vector3 prev = transform.position;
            traveled += speed * Time.deltaTime;
            if (homing != null && traveled / total < homingUntil)
            {
                // 落點慢慢往目標修正（保留一點偏差，玩家翻滾 / 衝刺還是躲得掉）
                end = Vector3.MoveTowards(end, homing.position + homingOffset, homingRate * Time.deltaTime);
                total = Mathf.Max(0.1f, Vector3.Distance(start, end));
            }
            float u = traveled / total;
            Vector3 next = Vector3.Lerp(start, end, u) + Vector3.up * (4f * arc * u * (1f - u));
            // 到了目標點還沒撞到東西就繼續直線往前，直到最遠距離
            if (u > 1f) next = end + (end - start).normalized * (traveled - total) + Vector3.down * (traveled - total) * 0.15f;

            Vector3 step = next - prev;
            if (step.sqrMagnitude > 0f)
            {
                var hits = Physics.RaycastAll(prev, step.normalized, step.magnitude, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var hit in hits)
                {
                    if (owner != null && hit.collider.transform.IsChildOf(owner)) continue;
                    Explode(hit.point, hit.collider);
                    return;
                }
            }
            transform.position = next;
            if (traveled > maxDistance) Explode(next, null);
        }

        void Explode(Vector3 at, Collider hit)
        {
            flying = false;
            onExplode?.Invoke(at, hit);
            core.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            trail.emitting = false;
            Destroy(gameObject, 0.4f);
        }
    }
}
