#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 村長家中軸的通道：從大門進主殿、穿過內殿，從後門出去直直走到後院大門（JapaneseVillageBuilder.BuildChiefCompound 呼叫）。
///   主殿後牆（Prefab 空間 z = 10）正中間開一個門，寬度和內殿入口（RearChamberPortalHeader，寬 6）一樣：
///   中柱（含柱礎、斗栱）拿掉，兩旁兩格紙門裁短到門邊，原本中柱兩邊的門框移到門邊，門頂補一根橫楣（內殿入口那根複製過來），
///   最上面那根門楣原本在中柱斷開，拉到中間接起來；後緣的緣側補一組和正面一樣的石階（正面石階前後鏡射）下到後院。
/// 主殿（隊友的 PF_MainDojo_Complete，最終決戰場景也用）的外觀是 Structure/BakedVisuals 裡按材質合併好的大 Mesh，
/// 沒辦法只關掉一片牆，所以複製一份 Mesh 來改（Prefab 和原本的 Mesh 都不動，新的存進村莊的 JV_Meshes.asset）。
/// 碰撞：中柱關掉、兩格紙門的碰撞框縮到門邊、後面的石階另外放一組。
/// 座標都是 Prefab 空間（主殿原點、+Z = 後面；×ChiefHouseScale 才是場景裡的公尺，村莊裡 +Z 轉成 +X = 東邊）。
/// </summary>
public static class ChiefHouseRearPassage
{
    const float WallZ = 10f;                  // 後牆
    const float BayHalf = 8.5f;               // 中柱兩邊各一格，到 ±8.5 的柱子為止
    const float DoorHalf = 3f;                // 門洞半寬 = 內殿入口
    const float StileHalf = 0.055f;           // 門框（DoorStile）半寬
    const float BandY0 = 1.1f, BandY1 = 5.95f, BandZ = 0.35f;   // 後牆這一層：地板到大樑底下（中柱頂 5.9）
    const float TopRailY = 5.66f;             // 最上面那根門楣（DoorRail y 5.72）以上：留著，拉到中間接起來
    const float Eps = 0.012f;

    struct Box
    {
        public Vector3 c, h;
        public bool Contains(Vector3 p) => Mathf.Abs(p.x - c.x) <= h.x + Eps && Mathf.Abs(p.y - c.y) <= h.y + Eps && Mathf.Abs(p.z - c.z) <= h.z + Eps;
    }

    /// <summary>單位方塊（或碰撞框）在 Prefab 空間的外框</summary>
    static Box BoxOf(Matrix4x4 toPrefab, Vector3 center, Vector3 size)
    {
        var m = toPrefab;
        return new Box
        {
            c = m.MultiplyPoint3x4(center),
            h = 0.5f * new Vector3(Mathf.Abs(m.m00) * size.x + Mathf.Abs(m.m01) * size.y + Mathf.Abs(m.m02) * size.z,
                                   Mathf.Abs(m.m10) * size.x + Mathf.Abs(m.m11) * size.y + Mathf.Abs(m.m12) * size.z,
                                   Mathf.Abs(m.m20) * size.x + Mathf.Abs(m.m21) * size.y + Mathf.Abs(m.m22) * size.z),
        };
    }

    /// <summary>開後門、補石階。</summary>
    /// <returns>後面石階最下面一階外緣的中點（世界座標）；主殿的結構跟預期的不一樣時回傳 null（什麼都不改）</returns>
    public static Vector3? Open(GameObject house, Transform parent, System.Func<Mesh, Mesh> saveMesh)
    {
        var inst = house.transform;
        var toPrefab = inst.worldToLocalMatrix;

        // 要複製的東西：中柱兩邊的門框、內殿入口的橫楣、正面石階
        var stiles = new List<Box>();
        var steps = new List<Box>();
        Box? header = null;
        float railBottom = 5.675f;
        foreach (var t in inst.GetComponentsInChildren<Transform>(true))
        {
            if (t.childCount > 0) continue;
            var b = BoxOf(toPrefab * t.localToWorldMatrix, Vector3.zero, Vector3.one);
            bool rear = Mathf.Abs(b.c.z - WallZ) < 0.3f;
            if (t.name == "DoorStile" && rear && Mathf.Abs(b.c.x) < 0.5f) stiles.Add(b);
            else if (t.name == "DoorRail" && rear && Mathf.Abs(b.c.x) < BayHalf && b.c.y > 5f) railBottom = b.c.y - b.h.y;
            else if (t.name == "RearChamberPortalHeader") header = b;
            else if (t.name.StartsWith("Step_") && t.parent != null && t.parent.name == "FrontStoneStairs") steps.Add(b);
        }
        if (stiles.Count != 2 || header == null || steps.Count == 0)
        {
            Debug.LogWarning($"[JapaneseVillage] 村長家的主殿結構跟預期的不一樣（門框 {stiles.Count}、橫楣 {header != null}、石階 {steps.Count}），沒有開後門");
            return null;
        }

        int removed = 0, clipped = 0, copied = 0, meshes = 0;
        foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
        {
            // 只改主結構（Structure/BakedVisuals）：天花板、家具的合併 Mesh 不動
            var bp = mf.transform.parent;
            if (bp == null || bp.name != "BakedVisuals" || bp.parent == null || bp.parent.name != "Structure") continue;
            var src = mf.sharedMesh;
            if (src == null || !src.isReadable) continue;

            var e = new MeshEdit(src, toPrefab * mf.transform.localToWorldMatrix);
            // 先複製（用原本的位置），再切後牆；複製出來的不會再被切到
            foreach (var s in stiles)
                e.CopyInside(new[] { s }, new Vector3(Mathf.Sign(s.c.x) * (DoorHalf + StileHalf) - s.c.x, 0f, 0f), false);
            var hb = header.Value;
            e.CopyInside(new[] { hb }, new Vector3(0f, railBottom - hb.h.y - hb.c.y, WallZ - hb.c.z), false);
            e.CopyInside(steps, Vector3.zero, true);
            e.CutRearWall();
            if (e.removed + e.clipped + e.copied == 0) continue;

            removed += e.removed; clipped += e.clipped; copied += e.copied; meshes++;
            mf.sharedMesh = saveMesh(e.Build(src.name + "_RearPassage"));
            PrefabUtility.RecordPrefabInstancePropertyModifications(mf);
        }

        // 碰撞：中柱關掉；兩格紙門（紙、下面的木板）的碰撞框縮到門邊
        foreach (var col in inst.GetComponentsInChildren<BoxCollider>(true))
        {
            var t = col.transform;
            var m = toPrefab * t.localToWorldMatrix;
            var b = BoxOf(m, col.center, col.size);
            if (Mathf.Abs(b.c.z - WallZ) > 0.3f || Mathf.Abs(b.c.x) > BayHalf) continue;
            if (t.name == "CedarColumn" && Mathf.Abs(b.c.x) < 0.3f)
            {
                col.enabled = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(col);
            }
            else if ((t.name == "PaperPanel" || t.name == "WoodenLowerPanel") && b.c.x - b.h.x < DoorHalf && b.c.x + b.h.x > -DoorHalf)
            {
                float side = Mathf.Sign(b.c.x);
                float xa = side > 0f ? DoorHalf : b.c.x - b.h.x, xb = side > 0f ? b.c.x + b.h.x : -DoorHalf;
                var inv = m.inverse;   // 後牆的牆板沒有旋轉：本地 x 軸 = Prefab 的 x 軸
                var p0 = inv.MultiplyPoint3x4(new Vector3(xa, b.c.y, b.c.z));
                var p1 = inv.MultiplyPoint3x4(new Vector3(xb, b.c.y, b.c.z));
                col.center = new Vector3((p0.x + p1.x) * 0.5f, col.center.y, col.center.z);
                col.size = new Vector3(Mathf.Abs(p1.x - p0.x), col.size.y, col.size.z);
                PrefabUtility.RecordPrefabInstancePropertyModifications(col);
            }
        }

        // 後面的石階碰撞：正面石階（Step_1～6）前後鏡射，放在和主殿同樣位置、旋轉、縮放的空物件底下
        var stairs = new GameObject("ChiefHouse_RearStairs").transform;
        stairs.SetParent(parent, false);
        stairs.SetPositionAndRotation(inst.position, inst.rotation);
        Vector3 hs = inst.lossyScale, ps = parent.lossyScale;
        stairs.localScale = new Vector3(hs.x / ps.x, hs.y / ps.y, hs.z / ps.z);
        float footZ = 0f;
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            var go = new GameObject($"RearStep_{i + 1}");
            go.transform.SetParent(stairs, false);
            go.transform.localPosition = new Vector3(s.c.x, s.c.y, -s.c.z);
            go.AddComponent<BoxCollider>().size = s.h * 2f;
            go.isStatic = true;
            footZ = Mathf.Max(footZ, -s.c.z + s.h.z);
        }

        var foot = inst.TransformPoint(new Vector3(0f, 0f, footZ));
        Debug.Log($"[JapaneseVillage] 村長家後門：門寬 {DoorHalf * 2f * inst.lossyScale.x:F1} m，改了 {meshes} 個合併 Mesh（拿掉 {removed}、裁短 {clipped}、補上 {copied} 個三角形），後面石階到 {foot}");
        return foot;
    }

    /// <summary>複製一份合併 Mesh 來改：頂點都換算到 Prefab 空間判斷</summary>
    sealed class MeshEdit
    {
        readonly Mesh src;
        readonly Matrix4x4 toPrefab, toMesh;
        readonly int origCount;
        readonly List<Vector3> pos = new(), nrm = new(), p = new();
        readonly List<Vector4> tan = new();
        readonly List<Color> col = new();
        readonly List<Vector4>[] uv = new List<Vector4>[8];
        readonly int[] uvDim = new int[8];
        readonly List<int[]> subs = new();
        readonly List<List<int>> extra = new();
        public int removed, clipped, copied;

        public MeshEdit(Mesh m, Matrix4x4 meshToPrefab)
        {
            src = m;
            toPrefab = meshToPrefab;
            toMesh = meshToPrefab.inverse;
            m.GetVertices(pos);
            m.GetNormals(nrm);
            m.GetTangents(tan);
            m.GetColors(col);
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = VertexAttribute.TexCoord0 + ch;
                if (!m.HasVertexAttribute(attr)) continue;
                uv[ch] = new List<Vector4>();
                m.GetUVs(ch, uv[ch]);
                uvDim[ch] = m.GetVertexAttributeDimension(attr);
            }
            foreach (var v in pos) p.Add(toPrefab.MultiplyPoint3x4(v));
            origCount = pos.Count;
            for (int s = 0; s < m.subMeshCount; s++)
            {
                subs.Add(m.GetTriangles(s));
                extra.Add(new List<int>());
            }
        }

        /// <summary>三個點都在某個 box 裡的三角形複製一份：平移 offset；mirrorZ = 前後鏡射（繞序反過來）</summary>
        public void CopyInside(IList<Box> boxes, Vector3 offset, bool mirrorZ)
        {
            for (int s = 0; s < subs.Count; s++)
            {
                var tris = subs[s];
                var map = new Dictionary<int, int>();
                int Map(int v) => map.TryGetValue(v, out int w) ? w : map[v] = Dup(v, offset, mirrorZ);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    bool inside = false;
                    foreach (var box in boxes)
                        if (box.Contains(p[a]) && box.Contains(p[b]) && box.Contains(p[c])) { inside = true; break; }
                    if (!inside) continue;
                    int na = Map(a), nb = Map(b), nc = Map(c);
                    if (mirrorZ) extra[s].AddRange(new[] { na, nc, nb });
                    else extra[s].AddRange(new[] { na, nb, nc });
                    copied++;
                }
            }
        }

        int Dup(int i, Vector3 offset, bool mirrorZ)
        {
            var q = p[i];
            if (mirrorZ) q.z = -q.z;
            q += offset;
            p.Add(q);
            pos.Add(toMesh.MultiplyPoint3x4(q));
            if (nrm.Count > i)
            {
                var n = toPrefab.MultiplyVector(nrm[i]);
                if (mirrorZ) n.z = -n.z;
                nrm.Add(toMesh.MultiplyVector(n).normalized);
            }
            if (tan.Count > i)
            {
                var t4 = tan[i];
                var t = toPrefab.MultiplyVector(new Vector3(t4.x, t4.y, t4.z));
                if (mirrorZ) t.z = -t.z;
                t = toMesh.MultiplyVector(t).normalized;
                tan.Add(new Vector4(t.x, t.y, t.z, mirrorZ ? -t4.w : t4.w));
            }
            if (col.Count > i) col.Add(col[i]);
            for (int ch = 0; ch < 8; ch++)
                if (uv[ch] != null) uv[ch].Add(uv[ch][i]);
            return p.Count - 1;
        }

        static bool InBand(Vector3 v) => Mathf.Abs(v.z - WallZ) < BandZ && v.y > BandY0 && v.y < BandY1;

        /// <summary>
        /// 後牆中間開門：門洞裡的（中柱、柱礎、斗栱、門框、直格條）拿掉；橫跨門洞的（紙、木板、橫格條、門檻）裁到門框中線；
        /// 最上面的門楣（兩格原本在中柱斷開）拉到中間接起來。整條橫跨好幾格的（大樑、天花板）不動。
        /// </summary>
        public void CutRearWall()
        {
            float cut = DoorHalf + StileHalf, core = DoorHalf + 2f * StileHalf + 0.005f;
            var moved = new HashSet<int>();
            for (int s = 0; s < subs.Count; s++)
            {
                var tris = subs[s];
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                    Vector3 pa = p[a], pb = p[b], pc = p[c];
                    if (InBand(pa) && InBand(pb) && InBand(pc))
                    {
                        float x0 = Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x)), x1 = Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x));
                        bool local = x0 >= -BayHalf - 0.1f && x1 <= BayHalf + 0.1f;
                        if (local && x0 >= -core && x1 <= core) { removed++; continue; }
                        if (local && Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y)) >= TopRailY)
                        {
                            foreach (int v in new[] { a, b, c })
                                if (Mathf.Abs(p[v].x) < 0.3f && moved.Add(v)) MoveX(v, 0f);
                        }
                        else if (local && x0 < cut && x1 > -cut && !(x0 < -cut && x1 > cut))
                        {
                            float side = Mathf.Sign(pa.x + pb.x + pc.x);
                            foreach (int v in new[] { a, b, c })
                                if (p[v].x * side < cut && moved.Add(v)) MoveX(v, side * cut);
                            clipped++;
                        }
                    }
                    keep.Add(a); keep.Add(b); keep.Add(c);
                }
                subs[s] = keep.ToArray();
            }
        }

        /// <summary>把頂點沿 x 移到 x（Prefab 空間），貼圖座標跟著同一面另一頭的頂點內插，紋理才不會被壓扁</summary>
        void MoveX(int v, float x)
        {
            var q = p[v];
            int partner = -1;
            float best = float.MaxValue;
            var n = nrm.Count > v ? nrm[v] : Vector3.zero;
            for (int i = 0; i < origCount; i++)
            {
                var o = p[i];
                if (i == v || Mathf.Abs(o.y - q.y) > 0.002f || Mathf.Abs(o.z - q.z) > 0.002f) continue;
                if (Mathf.Sign(o.x) != Mathf.Sign(q.x) || Mathf.Abs(o.x) < Mathf.Abs(q.x) + 0.5f) continue;
                if (nrm.Count > i && Vector3.Dot(nrm[i], n) < 0.99f) continue;
                float d = Mathf.Abs(o.x - q.x);
                if (d < best) { best = d; partner = i; }
            }
            if (partner >= 0)
            {
                float t = (x - q.x) / (p[partner].x - q.x);
                for (int ch = 0; ch < 8; ch++)
                    if (uv[ch] != null) uv[ch][v] = Vector4.LerpUnclamped(uv[ch][v], uv[ch][partner], t);
            }
            q.x = x;
            p[v] = q;
            pos[v] = toMesh.MultiplyPoint3x4(q);
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.indexFormat = pos.Count > 65535 ? IndexFormat.UInt32 : src.indexFormat;
            m.SetVertices(pos);
            if (nrm.Count == pos.Count) m.SetNormals(nrm);
            if (tan.Count == pos.Count) m.SetTangents(tan);
            if (col.Count == pos.Count) m.SetColors(col);
            for (int ch = 0; ch < 8; ch++)
            {
                if (uv[ch] == null) continue;
                if (uvDim[ch] <= 2) m.SetUVs(ch, uv[ch].ConvertAll(u => new Vector2(u.x, u.y)));
                else if (uvDim[ch] == 3) m.SetUVs(ch, uv[ch].ConvertAll(u => new Vector3(u.x, u.y, u.z)));
                else m.SetUVs(ch, uv[ch]);
            }
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++)
            {
                var all = new List<int>(subs[s]);
                all.AddRange(extra[s]);
                m.SetTriangles(all, s, false);
            }
            m.RecalculateBounds();
            return m;
        }
    }
}
#endif
