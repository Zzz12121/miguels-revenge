using UnityEngine;

/// <summary>
/// 量「目前姿勢」的蒙皮網格在世界座標的外框（腳底、頭頂、身體中心）。
/// SkinnedMeshRenderer.BakeMesh 烘出來的頂點要怎麼轉到世界座標，跟模型階層裡的縮放有關
/// （Blender / Tripo 匯出的骨架常常帶 0.01 或 100 的縮放），猜錯就會差 100 倍。
/// 這裡把幾種可能都算一遍，挑跟骨頭位置最吻合的那一個，所以不管模型怎麼匯出都量得對。
/// </summary>
public static class SkinnedMeshBounds
{
    /// <param name="ignoreBonePrefix">主要跟著這種名字開頭的骨頭動的頂點不算（例如 "SB_"：會晃動的頭髮 / 衣服）</param>
    public static bool TryGet(GameObject root, out Bounds result, string ignoreBonePrefix = null)
    {
        result = default;
        bool any = false;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (!TryGet(smr, out var b, ignoreBonePrefix)) continue;
            if (!any) { result = b; any = true; }
            else result.Encapsulate(b);
        }
        return any;
    }

    public static bool TryGet(SkinnedMeshRenderer smr, out Bounds best, string ignoreBonePrefix = null)
    {
        best = default;
        if (smr == null || smr.sharedMesh == null) return false;

        // 參考：骨頭在世界座標的範圍（骨頭一定在身體裡面，網格外框會比它大一點點）
        Bounds bones = default;
        int boneCount = 0;
        foreach (var b in smr.bones)
        {
            if (b == null) continue;
            if (boneCount++ == 0) bones = new Bounds(b.position, Vector3.zero);
            else bones.Encapsulate(b.position);
        }

        bool[] skip = null;
        if (!string.IsNullOrEmpty(ignoreBonePrefix))
        {
            var bonesArr = smr.bones;
            var weights = smr.sharedMesh.boneWeights;
            if (weights != null && weights.Length == smr.sharedMesh.vertexCount)
            {
                skip = new bool[weights.Length];
                for (int i = 0; i < weights.Length; i++)
                {
                    int bi = weights[i].boneIndex0; // 權重最大的那根骨頭
                    skip[i] = bi >= 0 && bi < bonesArr.Length && bonesArr[bi] != null && bonesArr[bi].name.StartsWith(ignoreBonePrefix);
                }
            }
        }

        var mesh = new Mesh();
        float bestScore = float.PositiveInfinity;
        bool found = false;
        try
        {
            var noScale = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            foreach (bool useScale in new[] { true, false })
            {
                smr.BakeMesh(mesh, useScale);
                var verts = mesh.vertices;
                if (verts.Length == 0) continue;
                foreach (var m in new[] { noScale, smr.transform.localToWorldMatrix })
                {
                    var b = Measure(verts, m, skip);
                    float score = boneCount < 2 ? (found ? float.PositiveInfinity : 0f) : Score(b, bones);
                    if (score < bestScore) { bestScore = score; best = b; found = true; }
                }
            }
        }
        finally
        {
            if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
        }
        return found;
    }

    static Bounds Measure(Vector3[] verts, Matrix4x4 m, bool[] skip)
    {
        Bounds b = default; bool any = false;
        for (int i = 0; i < verts.Length; i++)
        {
            if (skip != null && i < skip.Length && skip[i]) continue;
            var p = m.MultiplyPoint3x4(verts[i]);
            if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
        }
        return any ? b : new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
    }

    /// <summary>越小越好：大小跟骨頭範圍差幾倍（取 log）＋ 中心偏多遠</summary>
    static float Score(Bounds mesh, Bounds bones)
    {
        float boneSize = Mathf.Max(bones.size.x, bones.size.y, bones.size.z, 0.001f);
        float meshSize = Mathf.Max(mesh.size.x, mesh.size.y, mesh.size.z, 0.001f);
        float sizeError = Mathf.Abs(Mathf.Log(meshSize / (boneSize * 1.15f)));
        float centerError = Vector3.Distance(mesh.center, bones.center) / boneSize;
        return sizeError + centerError;
    }
}
