using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在匯入模型沒有尾巴骨的情況下，執行時只替尾巴頂點建立額外骨鏈，
/// 再於 Animator 完成姿勢後疊加波浪與細微不規則擺動。
/// 不讀取輸入，也不改玩家移動、碰撞或技能邏輯。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(10000)]
public sealed class PlayerTailAnimator : MonoBehaviour
{
    [Header("尾巴動態")]
    [SerializeField, Range(3, 16)] int tailBoneCount = 10;
    [Tooltip("每節尾巴左右擺動的最大角度。骨鏈會累積成柔和曲線。")]
    [SerializeField, Range(0f, 30f)] float swayPerJoint = 18f;
    [Tooltip("尾巴根部的左右擺動倍率；根部先對準角色正後方，再以此倍率明顯左右甩動。")]
    [SerializeField, Range(0.5f, 3f)] float rootSwayMultiplier = 3f;
    [Tooltip("每節尾巴上下起伏的最大角度。")]
    [SerializeField, Range(0f, 12f)] float liftPerJoint = 7f;
    [SerializeField, Range(0.2f, 8f)] float swaySpeed = 2.55f;
    [SerializeField, Range(0f, 2f)] float phaseDelay = 0.38f;
    [Tooltip("打破完全規則的正弦波，讓尾巴有細微自主動作。")]
    [SerializeField, Range(0f, 1f)] float irregularity = 0.42f;
    [Tooltip("角色移動越快，尾巴會稍微更活躍。")]
    [SerializeField, Range(0f, 1.5f)] float movementResponse = 0.45f;

    [Header("貓科慣性與待機")]
    [SerializeField, Range(0f, 90f)] float turnCounterSwing = 55f;
    [SerializeField, Range(0f, 60f)] float idleTipCurl = 32f;
    [SerializeField, Range(0f, 40f)] float idleTipFlick = 22f;
    [SerializeField, Range(.2f, 1.5f)] float idleTempo = .55f;
    [SerializeField, Range(0f, 40f)] float runningVerticalSwing = 18f;
    [SerializeField, Range(0f, 35f)] float idleGroundPat = 24f;

    [Header("此模型的尾巴頂點範圍")]
    [SerializeField] string sourceBoneName = "Spine";
    [SerializeField] Vector3 tailBoundsMin = new Vector3(-0.35f, 0.27f, -0.16f);
    [SerializeField] Vector3 tailBoundsMax = new Vector3(-0.02f, 0.58f, -0.05f);

    SkinnedMeshRenderer skinnedRenderer;
    Mesh runtimeMesh;
    Transform[] tailBones;
    Quaternion[] restLocalRotations;
    Vector3[] restSegmentDirections;
    Vector3 previousWorldPosition;
    float smoothedMovement;
    float noiseSeed;
    float motionPhase;
    Vector3 restRootLocalPosition;
    Vector3 previousForward;
    float turnSwing, turnVelocity, tailTime, nextFlick, flickStart = -10f;
    float flickSign = 1f;
    bool patSequence;
    System.Random tailRandom;

    void OnEnable()
    {
        previousWorldPosition = transform.position;
        previousForward = transform.forward;
        turnSwing = turnVelocity = smoothedMovement = 0f;
        tailRandom = new System.Random(Guid.NewGuid().GetHashCode());
        nextFlick = tailTime + 2f + (float)tailRandom.NextDouble() * 3f;
        flickStart = -10f;
        patSequence = false;
    }

    void Awake()
    {
        noiseSeed = (gameObject.name.GetHashCode() & 1023) * 0.00614f;
        TryBuildTailRig();
        previousWorldPosition = transform.position;
    }

    void LateUpdate()
    {
        if (tailBones == null || restLocalRotations == null) return;

        if (Time.deltaTime <= 0f) return;
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 movement = transform.position - previousWorldPosition;
        movement.y = 0f;
        previousWorldPosition = transform.position;
        float normalizedMovement = Mathf.Clamp01(movement.magnitude / deltaTime / 4.5f);
        smoothedMovement = Mathf.Lerp(smoothedMovement, normalizedMovement, 1f - Mathf.Exp(-7f * deltaTime));

        float yawRate = Vector3.SignedAngle(previousForward, transform.forward, Vector3.up) / deltaTime;
        previousForward = transform.forward;
        // 正轉向的反向力矩：尾巴本來朝後，因此同號 yaw 會甩到轉彎外側。
        float turnTarget = Mathf.Clamp(yawRate / 180f, -1f, 1f) * turnCounterSwing * smoothedMovement;
        if (movement.sqrMagnitude > 9f) turnTarget = 0f; // 閃現不當作跑步轉彎。
        float remaining = Mathf.Min(deltaTime, 0.1f);
        while (remaining > 0f)
        {
            float step = Mathf.Min(remaining, 1f / 120f);
            turnVelocity += ((turnTarget - turnSwing) * 55f - turnVelocity * 8f) * step;
            turnSwing = Mathf.Clamp(turnSwing + turnVelocity * step, -80f, 80f);
            remaining -= step;
        }
        tailTime += Time.deltaTime;
        if (tailTime >= nextFlick)
        {
            flickStart = tailTime;
            flickSign = tailRandom.NextDouble() < .5 ? -1f : 1f;
            patSequence = tailRandom.NextDouble() < .6;
            nextFlick = tailTime + 2.5f + (float)tailRandom.NextDouble() * 4f;
        }

        float activity = 1f + smoothedMovement * movementResponse;
        float tempo = Mathf.Lerp(idleTempo, 1.15f, smoothedMovement);
        tempo *= Mathf.Lerp(.7f, 1.3f, Mathf.PerlinNoise(noiseSeed, tailTime * .12f));
        motionPhase += Time.deltaTime * swaySpeed * tempo;
        float clock = motionPhase;
        CenterTailRoot();
        for (int i = 0; i < tailBones.Length; i++)
        {
            float along = i / Mathf.Max(1f, tailBones.Length - 1f);
            float tipWeight = Mathf.Lerp(0.85f, 1f, along);
            // 延遲與噪聲按整條尾巴的位置計算，增加骨節不會增加彎折頻率。
            float phase = clock - along * 4f * phaseDelay;
            // 正弦維持左右往返，低頻噪聲只改變細部姿態。
            float noise = (Mathf.PerlinNoise(noiseSeed + along * .35f, tailTime * .21f) - .5f) * 2f;
            float secondary = Mathf.Sin(phase * 0.47f + 1.9f) * 0.2f;
            float sway = (Mathf.Sin(phase) + secondary + noise * irregularity) * swayPerJoint * tipWeight * activity;
            float lift = (Mathf.Sin(phase * 0.73f + 0.85f) + noise * irregularity * 0.45f)
                * liftPerJoint * tipWeight * activity;
            float tip = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.45f, 1f, along));
            float age = tailTime - flickStart - along * .12f;
            float flick = age > 0f && age < 2f ? Mathf.Sin(age * 7f) * Mathf.Exp(-age * 2.4f) : 0f;
            float curl = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(clock * .63f - along)), 3f);
            // 朝後的向量繞角色 right 正轉才是向上，尾尖從中後段漸進捲起。
            float curlShape = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, 1f, along));
            lift += curlShape * curl * idleTipCurl * 1.65f * (1f - smoothedMovement * .65f);
            lift += tip * flick * idleTipFlick * .5f * (1f - smoothedMovement);
            // 每次偶發動作約兩秒，三次輕拍，前後淡入淡出避免突然折角。
            if (patSequence && age > 0f && age < 2.1f)
            {
                float envelope = Mathf.SmoothStep(0f, 1f, age / .25f)
                    * Mathf.SmoothStep(0f, 1f, (2.1f - age) / .45f);
                float pat = Mathf.Pow(.5f - .5f * Mathf.Cos(age * Mathf.PI * 2f / .7f), 2f);
                lift -= (idleGroundPat * pat + curlShape * curl * idleTipCurl)
                    * envelope * Mathf.Lerp(.3f, 1f, tip) * (1f - smoothedMovement);
            }
            lift += (Mathf.Sin(clock * 1.45f - along * 2.2f) * .75f
                + Mathf.Sin(clock * .71f - along * 1.4f + 1.2f) * .25f)
                * runningVerticalSwing * smoothedMovement * Mathf.Lerp(.45f, 1f, along);
            lift += Mathf.Sin(phase + .8f) * Mathf.Abs(turnSwing) * .15f * along;
            if (i < tailBones.Length - 1)
            {
                float segmentSway = sway * Mathf.Lerp(rootSwayMultiplier, 2.2f, along)
                    * Mathf.Lerp(.55f, 1f, smoothedMovement);
                segmentSway += turnSwing * Mathf.Lerp(1f, .65f, along)
                    - turnVelocity * .025f * along + flick * flickSign * idleTipFlick * tip;
                segmentSway = Mathf.Clamp(segmentSway, -80f, 80f);
                PointSegmentBehindCharacter(i, segmentSway, lift);
                continue;
            }

            tailBones[i].localRotation = restLocalRotations[i] * Quaternion.Euler(0f, sway, lift);
        }
    }

    void CenterTailRoot()
    {
        Transform root = tailBones[0];
        root.localPosition = restRootLocalPosition;
        // 保持實際腰部接縫的位置，不能把根骨平移到角色中央而拉扯接縫。
    }

    void PointSegmentBehindCharacter(int index, float sway, float lift)
    {
        Transform bone = tailBones[index];
        Transform parent = bone.parent;
        if (parent == null || restSegmentDirections == null
            || index >= restSegmentDirections.Length
            || restSegmentDirections[index].sqrMagnitude < 0.0001f)
        {
            bone.localRotation = restLocalRotations[index];
            return;
        }

        // 由角色本身的 forward/up/right 算目標方向，而不是猜匯入模型的骨頭局部軸。
        // 先固定以角色正後方為中心，再加入左右甩動與少量上下起伏。
        Vector3 restingDirection = (-transform.forward - transform.up * Mathf.Lerp(1.15f, .38f, smoothedMovement)).normalized;
        Vector3 desiredDirection = Quaternion.AngleAxis(sway, transform.up) * restingDirection;
        desiredDirection = Quaternion.AngleAxis(lift, transform.right) * desiredDirection;

        Quaternion restWorldRotation = parent.rotation * restLocalRotations[index];
        Vector3 restDirection = restWorldRotation * restSegmentDirections[index];
        bone.rotation = Quaternion.FromToRotation(restDirection, desiredDirection.normalized) * restWorldRotation;
    }

    void OnDisable()
    {
        if (tailBones == null || restLocalRotations == null) return;
        for (int i = 0; i < tailBones.Length; i++)
            if (tailBones[i] != null) tailBones[i].localRotation = restLocalRotations[i];
        if (tailBones[0] != null) tailBones[0].localPosition = restRootLocalPosition;
    }

    void OnDestroy()
    {
        if (runtimeMesh != null) Destroy(runtimeMesh);
    }

    void TryBuildTailRig()
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        if (animator == null) return;

        SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        int largestVertexCount = 0;
        foreach (SkinnedMeshRenderer candidate in renderers)
        {
            if (candidate == null || candidate.sharedMesh == null) continue;
            if (candidate.sharedMesh.vertexCount <= largestVertexCount) continue;
            largestVertexCount = candidate.sharedMesh.vertexCount;
            skinnedRenderer = candidate;
        }
        if (skinnedRenderer == null || skinnedRenderer.sharedMesh == null) return;

        // 正式 Prefab 已經有 Tail 骨鏈與重新配置過的蒙皮權重時，
        // 直接驅動持久骨架；不再每次進入 Play Mode 才臨時複製 Mesh。
        if (TryUsePersistentTailRig()) return;

        Transform sourceBone = FindBone(skinnedRenderer.bones, sourceBoneName);
        if (sourceBone == null) return;
        int sourceBoneIndex = Array.IndexOf(skinnedRenderer.bones, sourceBone);
        if (sourceBoneIndex < 0) return;

        Mesh sourceMesh = skinnedRenderer.sharedMesh;
        Vector3[] vertices = sourceMesh.vertices;
        BoneWeight[] weights = sourceMesh.boneWeights;
        if (weights == null || weights.Length != vertices.Length) return;

        List<int> tailVertices = new List<int>(512);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = vertices[i];
            if (!InsideTailBounds(point) || DominantBone(weights[i]) != sourceBoneIndex) continue;
            tailVertices.Add(i);
        }
        // 防止模型更換或匯入設定改變時誤抓一小撮衣服頂點。
        if (tailVertices.Count < 120) return;

        runtimeMesh = Instantiate(sourceMesh);
        runtimeMesh.name = sourceMesh.name + "_ProceduralTail";
        runtimeMesh.MarkDynamic();

        Vector3[] centers = CalculateTailCenters(vertices, tailVertices);
        tailBones = CreateTailBones(sourceBone, centers);
        restLocalRotations = new Quaternion[tailBones.Length];
        for (int i = 0; i < tailBones.Length; i++) restLocalRotations[i] = tailBones[i].localRotation;
        CacheSegmentDirections();

        Transform[] originalBones = skinnedRenderer.bones;
        Transform[] expandedBones = new Transform[originalBones.Length + tailBones.Length];
        Array.Copy(originalBones, expandedBones, originalBones.Length);
        Array.Copy(tailBones, 0, expandedBones, originalBones.Length, tailBones.Length);

        Matrix4x4[] originalBindposes = runtimeMesh.bindposes;
        Matrix4x4[] expandedBindposes = new Matrix4x4[expandedBones.Length];
        Array.Copy(originalBindposes, expandedBindposes, originalBindposes.Length);
        for (int i = 0; i < tailBones.Length; i++)
            expandedBindposes[originalBones.Length + i] = tailBones[i].worldToLocalMatrix * skinnedRenderer.transform.localToWorldMatrix;

        for (int i = 0; i < tailVertices.Count; i++)
        {
            int vertexIndex = tailVertices[i];
            float progress = TailProgress(vertices[vertexIndex]);
            float scaled = progress * (tailBones.Length - 1);
            int first = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, tailBones.Length - 1);
            int second = Mathf.Min(first + 1, tailBones.Length - 1);
            float blend = scaled - first;
            float sourceRetention = 1f - Mathf.SmoothStep(0f, 1f, progress / 0.18f);
            float tailInfluence = 1f - sourceRetention;
            weights[vertexIndex] = new BoneWeight
            {
                boneIndex0 = originalBones.Length + first,
                weight0 = (1f - blend) * tailInfluence,
                boneIndex1 = originalBones.Length + second,
                weight1 = blend * tailInfluence,
                boneIndex2 = sourceBoneIndex,
                weight2 = sourceRetention
            };
        }

        runtimeMesh.bindposes = expandedBindposes;
        runtimeMesh.boneWeights = weights;
        skinnedRenderer.bones = expandedBones;
        skinnedRenderer.sharedMesh = runtimeMesh;
    }

    bool TryUsePersistentTailRig()
    {
        List<Transform> found = new List<Transform>(tailBoneCount);
        for (int i = 1; i <= tailBoneCount; i++)
        {
            Transform bone = FindBone(skinnedRenderer.bones, $"Tail_{i:00}");
            if (bone == null) break;
            found.Add(bone);
        }
        if (found.Count < 3) return false;

        tailBones = found.ToArray();
        restLocalRotations = new Quaternion[tailBones.Length];
        for (int i = 0; i < tailBones.Length; i++)
            restLocalRotations[i] = tailBones[i].localRotation;
        CacheSegmentDirections();
        return true;
    }

    void CacheSegmentDirections()
    {
        restRootLocalPosition = tailBones[0].localPosition;
        restSegmentDirections = new Vector3[Mathf.Max(0, tailBones.Length - 1)];
        for (int i = 0; i < restSegmentDirections.Length; i++)
        {
            Vector3 worldDirection = tailBones[i + 1].position - tailBones[i].position;
            restSegmentDirections[i] = worldDirection.sqrMagnitude > 0.0001f
                ? tailBones[i].InverseTransformDirection(worldDirection.normalized)
                : Vector3.left;
        }
    }

    Vector3[] CalculateTailCenters(Vector3[] vertices, List<int> tailVertices)
    {
        Vector3[] sums = new Vector3[tailBoneCount];
        int[] counts = new int[tailBoneCount];
        foreach (int vertexIndex in tailVertices)
        {
            int section = Mathf.Clamp(Mathf.RoundToInt(TailProgress(vertices[vertexIndex]) * (tailBoneCount - 1)), 0, tailBoneCount - 1);
            sums[section] += vertices[vertexIndex];
            counts[section]++;
        }

        Vector3 root = new Vector3(tailBoundsMax.x, (tailBoundsMin.y + tailBoundsMax.y) * 0.5f, (tailBoundsMin.z + tailBoundsMax.z) * 0.5f);
        Vector3 tip = new Vector3(tailBoundsMin.x, root.y, root.z);
        Vector3[] centers = new Vector3[tailBoneCount];
        for (int i = 0; i < centers.Length; i++)
        {
            float progress = i / Mathf.Max(1f, centers.Length - 1f);
            centers[i] = counts[i] > 0 ? sums[i] / counts[i] : Vector3.Lerp(root, tip, progress);
        }
        return centers;
    }

    Transform[] CreateTailBones(Transform sourceBone, Vector3[] localCenters)
    {
        Transform[] bones = new Transform[localCenters.Length];
        Transform parent = sourceBone;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = new GameObject($"ProceduralTail_{i + 1:00}").transform;
            bone.SetParent(parent, false);
            bone.position = skinnedRenderer.transform.TransformPoint(localCenters[i]);
            bone.rotation = skinnedRenderer.transform.rotation;
            bones[i] = bone;
            parent = bone;
        }
        return bones;
    }

    bool InsideTailBounds(Vector3 point)
    {
        return point.x >= tailBoundsMin.x && point.x <= tailBoundsMax.x
            && point.y >= tailBoundsMin.y && point.y <= tailBoundsMax.y
            && point.z >= tailBoundsMin.z && point.z <= tailBoundsMax.z;
    }

    float TailProgress(Vector3 point)
    {
        return Mathf.InverseLerp(tailBoundsMax.x, tailBoundsMin.x, point.x);
    }

    static int DominantBone(BoneWeight weight)
    {
        int index = weight.boneIndex0;
        float highest = weight.weight0;
        if (weight.weight1 > highest) { highest = weight.weight1; index = weight.boneIndex1; }
        if (weight.weight2 > highest) { highest = weight.weight2; index = weight.boneIndex2; }
        if (weight.weight3 > highest) index = weight.boneIndex3;
        return index;
    }

    static Transform FindBone(Transform[] bones, string boneName)
    {
        if (bones == null) return null;
        foreach (Transform bone in bones)
            if (bone != null && string.Equals(bone.name, boneName, StringComparison.OrdinalIgnoreCase)) return bone;
        return null;
    }
}
