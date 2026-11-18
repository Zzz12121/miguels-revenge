using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把模型網格的一部分拿掉（例如村丁模型做在身上的武器、弓模型本身那條直的弓弦）：
/// Builder 先算好哪些頂點要拿掉（removedVertices，一個頂點一個 bit，Base64），
/// 開始時複製一份網格、把含有這些頂點的三角形刪掉，換到同一個物件的 SkinnedMeshRenderer / MeshFilter 上。
/// 也可以不拿掉、只讓一些頂點不要再跟著某幾根骨頭動（rebindVertices + detachBones，例如被手勾著走的褲子）：
/// 那幾根骨頭的權重拿掉、其餘的重新分配；全部都被拿掉的話改綁在 fallbackBone。
/// 也可以只留下網格裡的某一塊（keepIsland：一個模型裡擺了好幾樣東西時，例如一整組藥師道具，每樣各放一份只留自己那一塊）。
/// 同一個網格、同一份遮罩只複製一次（多個人共用）。網格要開 Read/Write（Builder 會設定）。
/// </summary>
[DefaultExecutionOrder(-100)]
public class StripMeshParts : MonoBehaviour
{
    [Tooltip("要拿掉的頂點（一個頂點一個 bit，Base64）")]
    [TextArea] public string removedVertices;
    [Tooltip("原本網格的頂點數（對不上就不動，避免模型換了以後刪錯）")]
    public int vertexCount;
    [Tooltip("三角形有幾個頂點在遮罩裡就刪掉（3 = 整個都在裡面才刪）")]
    [Range(1, 3)] public int minRemovedPerTriangle = 2;
    [Tooltip("直接拿掉的三角形（所有 submesh 依序編號）：例如手指和衣服黏在一起的那幾片")]
    public int[] removedTriangles;

    [Tooltip("只留下第幾塊（相連的三角形算一塊，依三角形數由多到少編號；-1 = 不用）")]
    public int keepIsland = -1;

    [Header("不拿掉，只換骨頭")]
    [Tooltip("要改權重的頂點（一個頂點一個 bit，Base64）")]
    [TextArea] public string rebindVertices;
    [Tooltip("這些頂點不要再跟著動的骨頭（SkinnedMeshRenderer.bones 的索引）")]
    public int[] detachBones;
    [Tooltip("權重全部被拿掉時改綁的骨頭（索引）：頂點低於 splitHeight（mesh 座標）用這個")]
    public int fallbackBone = -1;
    [Tooltip("權重全部被拿掉、頂點高於 splitHeight 時改綁的骨頭（-1 = 一樣用 fallbackBone）")]
    public int fallbackBoneHigh = -1;
    public float splitHeight = float.MaxValue;

    static readonly Dictionary<(Mesh, string, int), Mesh> cache = new Dictionary<(Mesh, string, int), Mesh>();

    void Awake()
    {
        var smr = GetComponent<SkinnedMeshRenderer>();
        var mf = smr == null ? GetComponent<MeshFilter>() : null;
        Mesh src = smr != null ? smr.sharedMesh : mf != null ? mf.sharedMesh : null;
        bool strip = !string.IsNullOrEmpty(removedVertices) || (removedTriangles != null && removedTriangles.Length > 0) || keepIsland >= 0, rebind = smr != null && !string.IsNullOrEmpty(rebindVertices) && detachBones != null && detachBones.Length > 0;
        if (src == null || (!strip && !rebind) || src.vertexCount != vertexCount) return;
        if (!src.isReadable) { Debug.LogWarning($"[StripMeshParts] {src.name} 沒有開 Read/Write，拿不掉"); return; }

        var key = (src, (removedVertices ?? "") + "|" + (rebindVertices ?? "") + "|" + (removedTriangles != null ? string.Join(",", removedTriangles) : "") + "|" + keepIsland, minRemovedPerTriangle);
        if (!cache.TryGetValue(key, out var mesh) || mesh == null)
        {
            if (keepIsland >= 0)
            {
                var islands = Islands(src);
                mesh = Instantiate(src);
                mesh.name = $"{src.name}_Piece{keepIsland}";
                mesh.subMeshCount = 1;
                mesh.SetTriangles(keepIsland < islands.Count ? islands[keepIsland] : new int[0], 0, true);
                cache[key] = mesh;
                if (smr != null) smr.sharedMesh = mesh; else mf.sharedMesh = mesh;
                return;
            }
            mesh = strip ? Strip(src, string.IsNullOrEmpty(removedVertices) ? null : System.Convert.FromBase64String(removedVertices), minRemovedPerTriangle, removedTriangles) : Instantiate(src);
            if (rebind) Rebind(mesh, System.Convert.FromBase64String(rebindVertices), detachBones, fallbackBone, fallbackBoneHigh, splitHeight);
            cache[key] = mesh;
        }
        if (smr != null) smr.sharedMesh = mesh;
        else mf.sharedMesh = mesh;
    }

    public static Mesh Strip(Mesh src, byte[] bits, int minRemoved, int[] removedTris = null)
    {
        bool Removed(int v) => bits != null && (bits[v >> 3] & (1 << (v & 7))) != 0;
        var dropTris = removedTris != null ? new HashSet<int>(removedTris) : null;
        var mesh = Instantiate(src);
        mesh.name = src.name + "_Stripped";
        int triIndex = 0;
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            var tri = src.GetTriangles(sm);
            var keep = new List<int>(tri.Length);
            for (int i = 0; i < tri.Length; i += 3, triIndex++)
            {
                if (dropTris != null && dropTris.Contains(triIndex)) continue;
                int n = (Removed(tri[i]) ? 1 : 0) + (Removed(tri[i + 1]) ? 1 : 0) + (Removed(tri[i + 2]) ? 1 : 0);
                if (n >= minRemoved) continue;
                keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]);
            }
            mesh.SetTriangles(keep, sm, false);
        }
        return mesh;
    }

    /// <summary>遮罩裡的頂點：detach 裡的骨頭權重拿掉、其餘的重新分配（加起來 = 1）；全部都拿掉了就綁在 fallback</summary>
    public static void Rebind(Mesh mesh, byte[] bits, int[] detach, int fallback, int fallbackHigh = -1, float splitHeight = float.MaxValue)
    {
        var set = new HashSet<int>(detach);
        var weights = mesh.boneWeights;
        var verts = mesh.vertices;
        for (int v = 0; v < weights.Length; v++)
        {
            if ((bits[v >> 3] & (1 << (v & 7))) == 0) continue;
            var w = weights[v];
            var list = new List<(int bone, float weight)>(4);
            void Add(int b, float x) { if (x > 0f && !set.Contains(b)) list.Add((b, x)); }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            float sum = 0f;
            foreach (var e in list) sum += e.weight;
            var nw = new BoneWeight();
            if (sum <= 1e-4f) { nw.boneIndex0 = Mathf.Max(0, verts[v].y > splitHeight && fallbackHigh >= 0 ? fallbackHigh : fallback); nw.weight0 = 1f; }
            else
            {
                list.Sort((a, b) => b.weight.CompareTo(a.weight));
                if (list.Count > 0) { nw.boneIndex0 = list[0].bone; nw.weight0 = list[0].weight / sum; }
                if (list.Count > 1) { nw.boneIndex1 = list[1].bone; nw.weight1 = list[1].weight / sum; }
                if (list.Count > 2) { nw.boneIndex2 = list[2].bone; nw.weight2 = list[2].weight / sum; }
            }
            weights[v] = nw;
        }
        mesh.boneWeights = weights;
    }

    static readonly Dictionary<Mesh, List<int[]>> islandCache = new Dictionary<Mesh, List<int[]>>();

    /// <summary>網格裡相連（共用位置）的三角形一塊一塊分開，依三角形數由多到少排；每塊是頂點索引（每 3 個一個三角形）</summary>
    public static List<int[]> Islands(Mesh mesh)
    {
        if (islandCache.TryGetValue(mesh, out var cached)) return cached;
        var v = mesh.vertices;
        var tri = mesh.triangles;
        var key = new Dictionary<Vector3Int, int>();
        var weld = new int[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            var q = Vector3Int.RoundToInt(v[i] * 5000f);
            if (!key.TryGetValue(q, out int id)) { id = key.Count; key[q] = id; }
            weld[i] = id;
        }
        var parent = new int[key.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < tri.Length; t += 3)
        {
            int a = Find(weld[tri[t]]);
            int b = Find(weld[tri[t + 1]]); if (b != a) parent[b] = a;
            int c = Find(weld[tri[t + 2]]); if (c != a) parent[c] = a;
        }
        var groups = new Dictionary<int, List<int>>();
        for (int t = 0; t < tri.Length; t += 3)
        {
            int r = Find(weld[tri[t]]);
            if (!groups.TryGetValue(r, out var l)) groups[r] = l = new List<int>();
            l.Add(tri[t]); l.Add(tri[t + 1]); l.Add(tri[t + 2]);
        }
        var list = new List<int[]>();
        foreach (var g in groups.Values) list.Add(g.ToArray());
        list.Sort((a, b) => b.Length.CompareTo(a.Length));
        islandCache[mesh] = list;
        return list;
    }

    /// <summary>頂點遮罩 → Base64（Builder 用）</summary>
    public static string Encode(bool[] removed)
    {
        var bits = new byte[(removed.Length + 7) / 8];
        for (int i = 0; i < removed.Length; i++) if (removed[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
        return System.Convert.ToBase64String(bits);
    }
}
