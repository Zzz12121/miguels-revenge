#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;

/// <summary>Offline repair of the two imported characters; no per-frame skinning changes.</summary>
public static class GarmentSkinningRepair
{
    public const string ElderMesh = "Assets/_Project/Models/Boss/CalligraphyElderBoss_Rigged_Source/CalligraphyElder_GarmentFixed.asset";
    public const string PlayerMesh = "Assets/_Project/Models/PlayerCharacter/PlayerCatWarrior_Rigged/PlayerCatWarrior_TailRigged_GarmentFixed.asset";
    const string ElderPrefab = "Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Presentation.prefab";
    const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PF_PlayerCatWarrior_Visual.prefab";

    [MenuItem("VFX/Characters/Repair Garment Skin Weights")]
    public static void RunMenu() => Debug.Log(RepairAll());

    public static string RepairAll()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play before repairing assets.");
        string report = Repair(ElderPrefab, ElderMesh, false) + "\n" + Repair(PlayerPrefab, PlayerMesh, true);
        AssetDatabase.SaveAssets();
        return report;
    }

    static bool Arm(string n) => n.Contains("Arm") || n.Contains("Hand") || n.Contains("Shoulder") ||
        n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Pinky") || n.Contains("Thumb");
    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        var d = b - a;
        return Vector3.Distance(p, a + d * Mathf.Clamp01(Vector3.Dot(p - a, d) / Mathf.Max(d.sqrMagnitude, 1e-8f)));
    }
    static int[] Indices(BoneWeight w) => new[] { w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3 };
    static float[] Weights(BoneWeight w) => new[] { w.weight0, w.weight1, w.weight2, w.weight3 };
    static BoneWeight Pack(Dictionary<int, float> weights)
    {
        var pairs = weights.Where(p => p.Value > 0).OrderByDescending(p => p.Value).Take(4).ToArray();
        float sum = pairs.Sum(p => p.Value);
        var ids = new int[4]; var values = new float[4];
        for (int i = 0; i < pairs.Length; i++) { ids[i] = pairs[i].Key; values[i] = pairs[i].Value / sum; }
        return new BoneWeight { boneIndex0 = ids[0], boneIndex1 = ids[1], boneIndex2 = ids[2], boneIndex3 = ids[3],
            weight0 = values[0], weight1 = values[1], weight2 = values[2], weight3 = values[3] };
    }

    static string Repair(string prefabPath, string output, bool player)
    {
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            var skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
            // Always start from the source, so rebuilding is deterministic and never compounds smoothing.
            var source = player ? AssetDatabase.LoadAssetAtPath<Mesh>("Assets/_Project/Models/PlayerCharacter/PlayerCatWarrior_Rigged/PlayerCatWarrior_TailRigged.asset") :
                AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Models/Boss/CalligraphyElderBoss_Rigged_Source/tripo_convert_5d8fea80-b25a-423b-937a-9c8937deeba0.fbx").OfType<Mesh>().OrderByDescending(m => m.vertexCount).First();
            var names = skin.bones.Select(b => b.name).ToArray();
            int Find(string n) { int i = Array.IndexOf(names, n); if (i < 0) throw new Exception("Missing bone: " + n); return i; }
            int hip = Find("Hips"), leftLeg = Find("Left_UpperLeg"), rightLeg = Find("Right_UpperLeg");
            var basis = root.transform.worldToLocalMatrix * skin.transform.localToWorldMatrix;
            var positions = source.bindposes.Select(b => basis.MultiplyPoint3x4(b.inverse.MultiplyPoint3x4(Vector3.zero))).ToArray();
            var arm = names.Select(Arm).ToArray();
            var vertices = source.vertices; var weights = source.boneWeights;
            if (positions.Length != skin.bones.Length) throw new Exception("Source bone layout differs from prefab.");
            float height = positions[Find("Head")].y - positions[hip].y;
            // The skirt starts at the pelvis. Sleeve/hand capsules protect real limb vertices,
            // including fingers below the waist in the imported A-pose.
            float protect = player ? .065f : .13f;
            int changed = 0, protectedTail = 0;
            for (int v = 0; v < vertices.Length; v++)
            {
                var ids = Indices(weights[v]); var values = Weights(weights[v]);
                if (Enumerable.Range(0, 4).Any(k => values[k] > 0 && names[ids[k]].StartsWith("Tail_"))) { protectedTail++; continue; }
                float armWeight = Enumerable.Range(0, 4).Where(k => arm[ids[k]]).Sum(k => values[k]);
                if (armWeight < .00001f) continue;
                Vector3 p = basis.MultiplyPoint3x4(vertices[v]);
                float waistMask = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(positions[hip].y + height * .12f, positions[hip].y - height * .12f, p.y));
                if (waistMask <= 0) continue;
                float limbDistance = float.MaxValue;
                for (int b = 0; b < names.Length; b++)
                {
                    if (!arm[b]) continue;
                    int parent = Array.IndexOf(skin.bones, skin.bones[b].parent);
                    if (parent >= 0 && arm[parent]) limbDistance = Mathf.Min(limbDistance, SegmentDistance(p, positions[b], positions[parent]));
                }
                float mask = waistMask * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(protect, protect * 1.5f, limbDistance));
                if (mask < .00001f) continue;
                var result = new Dictionary<int, float>();
                void Add(int id, float w) { if (!result.ContainsKey(id)) result[id] = 0; result[id] += w; }
                float remainingBody = 0;
                for (int k = 0; k < 4; k++)
                {
                    Add(ids[k], values[k] * (arm[ids[k]] ? 1 - mask : 1));
                    if (!arm[ids[k]]) remainingBody += values[k];
                }
                float transfer = armWeight * mask;
                if (remainingBody > .15f)
                {
                    for (int k = 0; k < 4; k++) if (!arm[ids[k]]) Add(ids[k], transfer * values[k] / remainingBody);
                }
                else
                {
                    // Fully misbound inner robe panels have no reliable body donor. Anchor to
                    // pelvis/thighs, not feet, so the long coat doesn't inherit ankle rotations.
                    float legBlend = .45f * Mathf.SmoothStep(0, 1, (positions[hip].y - p.y) / (height * .65f));
                    float side = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(positions[leftLeg].x, positions[rightLeg].x, p.x));
                    Add(hip, transfer * (1 - legBlend));
                    Add(leftLeg, transfer * legBlend * (1 - side));
                    Add(rightLeg, transfer * legBlend * side);
                }
                weights[v] = Pack(result); changed++;
            }
            var copy = Object.Instantiate(source); copy.name = System.IO.Path.GetFileNameWithoutExtension(output); copy.boneWeights = weights;
            var asset = AssetDatabase.LoadAssetAtPath<Mesh>(output);
            if (asset) { EditorUtility.CopySerialized(copy, asset); Object.DestroyImmediate(copy); EditorUtility.SetDirty(asset); }
            else { asset = copy; AssetDatabase.CreateAsset(asset, output); }
            skin.sharedMesh = asset;
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return $"{prefabPath}: repaired {changed} vertices; {protectedTail} tail vertices untouched; {weights.Length} total vertices.";
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
