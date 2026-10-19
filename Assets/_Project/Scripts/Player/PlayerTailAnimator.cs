using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 在匯入模型沒有尾巴骨的情況下，執行時只替尾巴頂點建立額外骨鏈，
/// 再於 Animator 完成姿勢後疊加波浪與細微不規則擺動。
/// 不讀取輸入，也不改玩家移動、碰撞或技能邏輯。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerTailAnimator : MonoBehaviour
{
    [Header("尾巴動態")]
    [SerializeField, Range(3, 7)] int tailBoneCount = 5;
    [Tooltip("每節尾巴左右擺動的最大角度。骨鏈會累積成柔和曲線。")]
    [SerializeField, Range(0f, 12f)] float swayPerJoint = 8.5f;
    [Tooltip("每節尾巴上下起伏的最大角度。")]
    [SerializeField, Range(0f, 8f)] float liftPerJoint = 4f;
    [SerializeField, Range(0.2f, 8f)] float swaySpeed = 2.35f;
    [SerializeField, Range(0f, 2f)] float phaseDelay = 0.62f;
    [Tooltip("打破完全規則的正弦波，讓尾巴有細微自主動作。")]
    [SerializeField, Range(0f, 1f)] float irregularity = 0.55f;
    [Tooltip("角色移動越快，尾巴會稍微更活躍。")]
    [SerializeField, Range(0f, 1f)] float movementResponse = 0.65f;

    [Header("此模型的尾巴頂點範圍")]
    [SerializeField] string sourceBoneName = "Spine";
    [SerializeField] Vector3 tailBoundsMin = new Vector3(-0.35f, 0.27f, -0.16f);
    [SerializeField] Vector3 tailBoundsMax = new Vector3(-0.07f, 0.57f, -0.05f);

    SkinnedMeshRenderer skinnedRenderer;
    Mesh runtimeMesh;
    Transform[] tailBones;
    Quaternion[] restLocalRotations;
    Vector3 previousWorldPosition;
    float smoothedMovement;
    float noiseSeed;

    void Awake()
    {
        noiseSeed = Mathf.Abs(gameObject.name.GetHashCode() * 0.00137f) + 3.7f;
        TryBuildTailRig();
        previousWorldPosition = transform.position;
    }

    void LateUpdate()
    {
        if (tailBones == null || restLocalRotations == null) return;

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 movement = transform.position - previousWorldPosition;
        movement.y = 0f;
        previousWorldPosition = transform.position;
        float normalizedMovement = Mathf.Clamp01(movement.magnitude / deltaTime / 4.5f);
        smoothedMovement = Mathf.Lerp(smoothedMovement, normalizedMovement, 1f - Mathf.Exp(-7f * deltaTime));

        float activity = 1f + smoothedMovement * movementResponse;
        float clock = Time.time * swaySpeed * (1f + smoothedMovement * 0.32f);
        for (int i = 0; i < tailBones.Length; i++)
        {
            float tipWeight = Mathf.Lerp(0.56f, 1f, i / Mathf.Max(1f, tailBones.Length - 1f));
            float phase = clock - i * phaseDelay;
            float noise = (Mathf.PerlinNoise(noiseSeed + i * 0.41f, Time.time * 0.47f) - 0.5f) * 2f;
            float secondary = Mathf.Sin(phase * 0.47f + 1.9f) * 0.24f;
            float sway = (Mathf.Sin(phase) + secondary + noise * irregularity) * swayPerJoint * tipWeight * activity;
            float lift = (Mathf.Sin(phase * 0.73f + 0.85f) + noise * irregularity * 0.45f)
                * liftPerJoint * tipWeight * activity;
            tailBones[i].localRotation = restLocalRotations[i] * Quaternion.Euler(0f, sway, lift);
        }
    }

    void OnDisable()
    {
        if (tailBones == null || restLocalRotations == null) return;
        for (int i = 0; i < tailBones.Length; i++)
            if (tailBones[i] != null) tailBones[i].localRotation = restLocalRotations[i];
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
            float sourceRetention = 1f - Mathf.SmoothStep(0f, 0.18f, progress);
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
