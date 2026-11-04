using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 骷髏蝙蝠（flying bat 3d model，Tripo 綁骨、沒有動畫）：拍翅膀用程式轉翅膀骨頭，飛行用簡單的三種模式。
///   Orbit    繞著某個中心轉（護衛）
///   Dive     追著目標俯衝，碰到就呼叫 onHit，接著散開
///   Disperse 往外往上飛走、縮小、消失
///
/// 模型原本是直立 T-pose（脊椎朝上、翅膀往兩側），Awake 會自動把它轉成「頭朝前、肚子朝下」的飛行姿勢。
/// 由百足駭骨的蝙蝠技能生成，不需要手動放進場景。
/// </summary>
public class SkeletonBat : MonoBehaviour
{
    public enum Mode { Orbit, Dive, Disperse }

    [Header("拍翅")]
    public float flapFrequency = 9f;
    public float flapAngle = 38f;
    public float elbowAngle = 22f;

    [Header("飛行")]
    public float turnRate = 540f;
    public float wobble = 0.35f;

    [Header("骨頭（Tripo 骨架編號）")]
    public string rightWing = "bone_5";
    public string rightElbow = "bone_6";
    public string leftWing = "bone_14";
    public string leftElbow = "bone_15";

    public Mode CurrentMode { get; private set; } = Mode.Orbit;

    Transform model;
    Vector3 flapAxisLocal = Vector3.forward;   // 在 model 空間裡的脊椎方向，翅膀繞它上下拍
    readonly List<(Transform bone, Quaternion bind, float side, float amount)> wingBones = new();
    float flapPhase;
    float seed;

    // Orbit
    Transform orbitCenter;
    Vector3 orbitOffset;
    float orbitRadius, orbitHeight, orbitSpeed, orbitAngle;

    // Dive
    Transform diveTarget;
    Vector3 diveAim;
    float diveSpeed, hitDistance;
    Action<SkeletonBat> onHit;

    // Disperse
    float disperseStart;
    Vector3 disperseVelocity;
    Vector3 baseScale;

    void Awake()
    {
        seed = UnityEngine.Random.value * 100f;
        flapPhase = UnityEngine.Random.value * Mathf.PI * 2f;
        flapFrequency *= UnityEngine.Random.Range(0.85f, 1.15f);
        baseScale = transform.localScale;

        model = transform.childCount > 0 ? transform.GetChild(0) : transform;
        var bones = new Dictionary<string, Transform>();
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            int i = n.LastIndexOf(':');
            bones.TryAdd(i >= 0 ? n.Substring(i + 1) : n, t);
        }
        var anim = model.GetComponent<Animator>();
        if (anim != null) anim.enabled = false;

        // 把直立 T-pose 轉成飛行姿勢：脊椎（Root → Head_1）朝前，腳尖那一面朝下
        if (bones.TryGetValue("Root", out var root) && bones.TryGetValue("Head_1", out var headTip))
        {
            Vector3 spine = transform.InverseTransformDirection(headTip.position - root.position).normalized;
            Vector3 front = Vector3.forward;
            if (bones.TryGetValue("0_Left_Limb_3", out var toeA) && bones.TryGetValue("0_Left_Limb_4", out var toeB))
                front = transform.InverseTransformDirection(toeB.position - toeA.position);
            front = Vector3.ProjectOnPlane(front, spine);
            if (front.sqrMagnitude < 0.0001f) front = Vector3.forward;
            model.localRotation = Quaternion.Inverse(Quaternion.LookRotation(spine, -front.normalized)) * model.localRotation;
            flapAxisLocal = model.InverseTransformDirection(transform.TransformDirection(Vector3.forward));
        }

        AddWing(bones, rightWing, 1f, 1f);
        AddWing(bones, rightElbow, 1f, elbowAngle / Mathf.Max(1f, flapAngle));
        AddWing(bones, leftWing, -1f, 1f);
        AddWing(bones, leftElbow, -1f, elbowAngle / Mathf.Max(1f, flapAngle));
    }

    void AddWing(Dictionary<string, Transform> bones, string boneName, float side, float amount)
    {
        if (bones.TryGetValue(boneName, out var t)) wingBones.Add((t, t.localRotation, side, amount));
    }

    // ───────── 模式 ─────────

    public void StartOrbit(Transform center, float radius, float height, float degreesPerSecond, float startAngle)
    {
        CurrentMode = Mode.Orbit;
        orbitCenter = center;
        orbitRadius = radius;
        orbitHeight = height;
        orbitSpeed = degreesPerSecond;
        orbitAngle = startAngle;
        orbitOffset = Vector3.zero;
    }

    public void StartDive(Transform target, Vector3 aimOffset, float speed, float hitDist, Action<SkeletonBat> hit)
    {
        CurrentMode = Mode.Dive;
        diveTarget = target;
        diveAim = aimOffset;
        diveSpeed = speed;
        hitDistance = hitDist;
        onHit = hit;
    }

    public void Disperse(Vector3 awayFrom)
    {
        if (CurrentMode == Mode.Disperse) return;
        CurrentMode = Mode.Disperse;
        disperseStart = Time.time;
        baseScale = transform.localScale;
        Vector3 away = transform.position - awayFrom;
        away.y = 0f;
        if (away.sqrMagnitude < 0.01f) away = UnityEngine.Random.insideUnitSphere;
        disperseVelocity = (away.normalized * UnityEngine.Random.Range(6f, 10f) + Vector3.up * UnityEngine.Random.Range(5f, 9f));
        Destroy(gameObject, 1.6f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 wob = new Vector3(Mathf.PerlinNoise(seed, Time.time * 1.7f) - 0.5f, Mathf.PerlinNoise(seed + 7f, Time.time * 2.3f) - 0.5f, 0f) * (wobble * 2f);

        switch (CurrentMode)
        {
            case Mode.Orbit:
            {
                if (orbitCenter == null) { Disperse(transform.position); break; }
                orbitAngle += orbitSpeed * dt;
                float rad = orbitAngle * Mathf.Deg2Rad;
                Vector3 center = orbitCenter.position + Vector3.up * orbitHeight;
                Vector3 target = center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius
                                 + Vector3.up * (Mathf.Sin(Time.time * 2.1f + seed) * orbitRadius * 0.12f) + wob;
                Vector3 vel = (target - transform.position) / Mathf.Max(dt, 0.0001f);
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-10f * dt));
                Steer(vel, 25f);
                break;
            }
            case Mode.Dive:
            {
                if (diveTarget == null) { Disperse(transform.position); break; }
                Vector3 aim = diveTarget.position + diveAim;
                Vector3 to = aim - transform.position;
                Steer(to, 0f);
                transform.position += transform.forward * diveSpeed * dt + wob * dt * 3f;
                if (to.magnitude <= hitDistance)
                {
                    var cb = onHit;
                    onHit = null;
                    cb?.Invoke(this);
                    Disperse(aim);
                }
                break;
            }
            case Mode.Disperse:
            {
                float u = (Time.time - disperseStart) / 1.4f;
                disperseVelocity += Vector3.up * 4f * dt;
                transform.position += disperseVelocity * dt;
                Steer(disperseVelocity, 0f);
                transform.localScale = baseScale * Mathf.Clamp01(1f - u * u);
                break;
            }
        }
    }

    void Steer(Vector3 direction, float bank)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        var want = Quaternion.LookRotation(direction.normalized, Vector3.up);
        if (bank != 0f) want *= Quaternion.AngleAxis(Mathf.Clamp(Vector3.SignedAngle(transform.forward, direction, Vector3.up), -bank, bank), Vector3.forward);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnRate * Time.deltaTime);
    }

    void LateUpdate()
    {
        // 翅膀：繞脊椎上下拍，往下拍比往上抬快一點（比較有力）
        flapPhase += flapFrequency * Mathf.PI * 2f * Time.deltaTime * (CurrentMode == Mode.Dive ? 1.3f : 1f);
        float s = Mathf.Sin(flapPhase);
        float wave = s > 0f ? s : s * 0.75f;
        foreach (var (bone, bind, side, amount) in wingBones)
        {
            float lag = amount < 1f ? Mathf.Sin(flapPhase - 0.9f) : wave;
            bone.localRotation = bind;
            Vector3 axis = bone.parent != null ? bone.parent.InverseTransformDirection(model.TransformDirection(flapAxisLocal)) : flapAxisLocal;
            bone.localRotation = Quaternion.AngleAxis(side * flapAngle * amount * lag, axis) * bind;
        }
    }

    /// <summary>生成一隻蝙蝠（prefab 根物件要有 SkeletonBat；模型放在第一個子物件）。</summary>
    public static SkeletonBat Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float scale)
    {
        if (prefab == null) return null;
        var go = Instantiate(prefab, position, rotation);
        go.transform.localScale = Vector3.one * scale;
        var bat = go.GetComponent<SkeletonBat>();
        if (bat == null) bat = go.AddComponent<SkeletonBat>();
        return bat;
    }
}
