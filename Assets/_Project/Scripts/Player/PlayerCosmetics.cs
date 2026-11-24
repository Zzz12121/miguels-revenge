using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家的造型（在漁村的行商那裡用錢買，Wardrobe 是商品表）：
///   - 帽子：斗笠（跟老漁夫一樣的草編斗笠、帽沿毛毛的）、竹笠（跟巡邏的村丁一樣：一根根竹篾往外放射、綁著幾圈、捲邊）、
///     烏帽子（高高的黑漆帽）——掛在頭骨上，跟著頭轉
///   - 圍巾（素色、沒有花紋）：朱紅、藍染、雪白長圍巾——在脖子上繞三圈，左後方打個結、垂下兩條有厚度的巾尾，
///     走路、跑步時往後飄（每幀算，不用物理）
///   - 鉤索：換 TetherRopeVisual 的繩子顏色和鉤爪金屬（TetherRopeVisual.Skins）
/// 穿哪一件存在 PlayerInventory（Wardrobe.WearKey），所以會跟著換關卡、存檔。
/// 全部是程式做的網格，大小照玩家模型量（換裝時烘一次目前姿勢的網格）。
/// 換上的那一下要站在地上（腳平放才量得準），在空中就等落地再穿。
/// 遊戲開始自動掛到玩家身上（VillageEconomy）。
/// </summary>
[DefaultExecutionOrder(32050)]
[DisallowMultipleComponent]
public sealed class PlayerCosmetics : MonoBehaviour
{
    Animator animator;
    ThirdPersonController controller;
    PlayerHealth health;
    TetherRopeVisual rope;

    int builtHat = -1, builtScarf = -1, startFrame;
    readonly List<GameObject> hatParts = new List<GameObject>(), scarfParts = new List<GameObject>();
    readonly List<Object> owned = new List<Object>(); // 自己做的網格、材質、貼圖：換掉時刪
    readonly List<Object> scarfOwned = new List<Object>();

    void Start()
    {
        controller = GetComponent<ThirdPersonController>();
        health = GetComponent<PlayerHealth>();
        startFrame = Time.frameCount;
    }

    void LateUpdate()
    {
        if (Time.frameCount < startFrame + 3) return; // 等動畫跑過幾幀（剛載入時骨架可能還是綁定姿勢）
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman) { animator = null; return; }
        }
        if (rope == null) rope = GetComponentInChildren<TetherRopeVisual>();
        if (rope != null && rope.CurrentSkin != Wardrobe.Wearing(Wardrobe.Slot.Hook)) rope.SetSkin(Wardrobe.Wearing(Wardrobe.Slot.Hook));

        UpdateTails();
        int hat = Wardrobe.Wearing(Wardrobe.Slot.Hat), scarf = Wardrobe.Wearing(Wardrobe.Slot.Scarf);
        if (hat == builtHat && scarf == builtScarf) return;
        // 量骨架要站穩：在空中、翻滾、死掉的時候先不換
        if (controller != null && (!controller.IsGrounded || controller.IsRolling)) return;
        if (health != null && health.IsDead) return;
        if (hat != builtHat) { BuildHat(hat); builtHat = hat; }
        if (scarf != builtScarf) { BuildScarf(scarf); builtScarf = scarf; }
    }

    void OnDestroy()
    {
        Clear(hatParts, owned);
        Clear(scarfParts, scarfOwned);
        tails.Clear();
    }

    static void Clear(List<GameObject> parts, List<Object> assets)
    {
        foreach (var p in parts) if (p != null) Destroy(p);
        parts.Clear();
        foreach (var a in assets) if (a != null) Destroy(a);
        assets.Clear();
    }

    // ---------------------------------------------------------------- 帽子

    void BuildHat(int index)
    {
        Clear(hatParts, owned);
        if (index <= 0) return;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;
        // 頭的方向：Mixamo 的 HeadTop_End；沒有就用身體的上方
        Transform topBone = FindChild(head, "HeadTop");
        Vector3 up = topBone != null ? (topBone.position - head.position).normalized : transform.up;
        float h = topBone != null ? Vector3.Distance(topBone.position, head.position) : 0.17f * Mathf.Max(0.5f, animator.humanScale);
        // 量模型真正的頭（頭髮、頭盔都算）：最高點、上半部最寬的半徑。帽子照這個大小做，才不會被原本的模型穿出來
        var pts = MeasureHead(head, up);
        float top, widest = 0f;
        if (pts.Count > 0)
        {
            top = float.NegativeInfinity;
            foreach (var q in pts) top = Mathf.Max(top, q.y);
            foreach (var q in pts) if (q.y > top * 0.35f) widest = Mathf.Max(widest, q.x);
        }
        else
        {
            // 量不到網格：照貓戰士的比例估（頭骨離地高度的 14% 是耳朵尖、8.75% 是頭最寬的地方）
            float fromGround = head.position.y - (health != null ? health.FeetY : transform.position.y);
            top = Mathf.Max(h, 0.14f * fromGround);
            widest = 0.0875f * fromGround;
        }
        // 帽子用的頭半徑：比量到的再大一點（頭髮的彈簧骨會晃，動起來才不會穿出來）
        float r = Mathf.Max(0.55f * h, widest * 1.15f);
        Debug.Log($"[PlayerCosmetics] 帽子 {index}：量到頭上 {pts.Count} 個頂點，頭頂 {top:0.000} m、最寬半徑 {widest:0.000} m → 帽子半徑 {r:0.000} m");
        // 斗笠（老漁夫的）：比較尖的圓錐；竹笠（村丁的）：比較扁、比較寬
        Vector2[] kasa = { new Vector2(0f, .42f), new Vector2(.1f, .4f), new Vector2(1.2f, -.1f), new Vector2(2.35f, -.62f), new Vector2(2.42f, -.68f) };
        Vector2[] jingasa = { new Vector2(0f, .3f), new Vector2(.15f, .28f), new Vector2(1.3f, -.05f), new Vector2(2.5f, -.42f), new Vector2(2.54f, -.46f) };
        // 斗笠、陣笠是圓錐：整頂往上（或往下）挪到帽子內側剛好碰不到頭上任何一點
        float lift = index == 1 ? FitLift(kasa, pts, top, r) : index == 2 ? FitLift(jingasa, pts, top, r) : Clearance;
        Vector3 crown = head.position + up * (top + lift);
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
        var root = new GameObject("Cosmetic_Hat");
        root.transform.SetPositionAndRotation(crown, Quaternion.LookRotation(fwd, up));
        hatParts.Add(root);

        switch (index)
        {
            case 1: // 斗笠（跟老漁夫一樣）：草編的圓錐，一根根稻草往外放射、幾圈深一點的編痕，帽沿參差不齊（毛毛的）
            {
                var straw = Lit(Color.white, 0f, .15f, KasaStrawTexture());
                Lathe(root.transform, "Kasa", straw, r, 48, true, 0.06f, kasa);
                Lathe(root.transform, "KasaKnob", straw, r, 16, false, 0f,
                    new Vector2(0f, .5f), new Vector2(.1f, .47f), new Vector2(.14f, .4f));
                // 內側的頭圈（從帽子底下看得到，戴得住的感覺）
                Lathe(root.transform, "KasaRing", Lit(new Color(.35f, .24f, .12f), 0f, .2f), r, 24, true, 0f,
                    new Vector2(.95f, -.1f), new Vector2(.95f, -.24f));
                break;
            }
            case 2: // 竹笠（跟巡邏的村丁一樣）：灰黃的竹篾一根根往外放射，中間綁兩圈、帽沿捲一圈粗邊
            {
                var bamboo = Lit(Color.white, 0f, .25f, BambooWeaveTexture());
                var binding = Lit(new Color(.42f, .36f, .27f), 0f, .2f);
                Lathe(root.transform, "Takegasa", bamboo, r, 48, true, 0f, jingasa);
                Lathe(root.transform, "TakegasaKnob", binding, r, 16, false, 0f,
                    new Vector2(0f, .37f), new Vector2(.14f, .34f), new Vector2(.2f, .28f));
                // 捲邊：沿著帽沿一圈小圓管
                var rim = new Vector2[9];
                for (int k = 0; k < rim.Length; k++)
                {
                    float a = k * Mathf.PI * 2f / (rim.Length - 1);
                    rim[k] = new Vector2(2.5f + Mathf.Cos(a) * .06f, -.44f + Mathf.Sin(a) * .06f);
                }
                Lathe(root.transform, "TakegasaRim", binding, r, 48, true, 0f, rim);
                break;
            }
            case 3: // 烏帽子：高高的黑漆帽，往後斜一點，頂端圓圓的；前面一道摺痕
            {
                var lacquer = Lit(new Color(.06f, .055f, .06f), .1f, .6f);
                var cap = Lathe(root.transform, "Eboshi", lacquer, r, 32, true, 0f,
                    new Vector2(1.12f, -1.05f), new Vector2(1.1f, 0f), new Vector2(.98f, .75f), new Vector2(.8f, 1.1f), new Vector2(.46f, 1.3f), new Vector2(0f, 1.36f));
                cap.transform.localRotation = Quaternion.Euler(-6f, 0f, 0f);
                cap.transform.localScale = new Vector3(1f, 1f, 1.06f); // 前後長一點（頭的形狀）；寬度不縮，才包得住頭
                var crease = Box(cap.transform, "EboshiCrease", Lit(new Color(.02f, .02f, .02f), 0f, .4f),
                                 new Vector3(0f, .55f * r, 1.1f * r), Quaternion.Euler(-8f, 0f, 0f), new Vector3(.9f * r, .05f * r, .05f * r));
                crease.transform.localPosition = new Vector3(0f, .55f * r, 1.1f * r);
                crease.transform.localScale = new Vector3(.9f * r, .05f * r, .05f * r / 1.06f);
                break;
            }
        }
        Attach(root.transform, head);
    }

    /// <summary>掛到骨頭上（保持現在的世界位置），圖層跟模型一樣（半身像、鏡頭碰撞才會當成身體的一部分）</summary>
    static void Attach(Transform part, Transform bone)
    {
        part.SetParent(bone, true);
        foreach (var t in part.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = bone.gameObject.layer;
    }

    /// <summary>模型目前姿勢的所有頂點（世界座標）和各自權重最大的骨頭（讀不到權重時骨頭是 null）。每次換裝烘一次</summary>
    List<(Vector3 p, Transform bone)> BakeAll()
    {
        var all = new List<(Vector3 p, Transform bone)>();
        var baked = new Mesh();
        foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (smr.sharedMesh == null || !smr.enabled) continue;
            Vector3[] verts;
            BoneWeight[] weights = null;
            try
            {
                smr.BakeMesh(baked, true);
                verts = baked.vertices;
                if (smr.sharedMesh.isReadable) weights = smr.sharedMesh.boneWeights;
            }
            catch { continue; }
            var bones = smr.bones;
            var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            bool useWeights = weights != null && weights.Length == verts.Length;
            for (int i = 0; i < verts.Length; i++)
            {
                Transform bone = null;
                if (useWeights && weights[i].weight0 >= 0.5f)
                {
                    int bi = weights[i].boneIndex0;
                    if (bi >= 0 && bi < bones.Length) bone = bones[bi];
                }
                all.Add((m.MultiplyPoint3x4(verts[i]), bone));
            }
        }
        Destroy(baked);
        return all;
    }

    /// <summary>權重最大的骨頭是 bone（children = 或它底下的骨頭）的頂點</summary>
    static List<Vector3> VertsOf(List<(Vector3 p, Transform bone)> all, Transform bone, bool children)
    {
        var list = new List<Vector3>();
        foreach (var v in all)
            if (v.bone != null && (v.bone == bone || (children && v.bone.IsChildOf(bone)))) list.Add(v.p);
        return list;
    }

    /// <summary>
    /// 頭上每一個頂點（頭髮、頭盔都包含）：x = 離頭的軸線多遠，y = 沿著頭的上方、比頭骨高多少（公尺）。
    /// 讀不到權重時改用頭骨上方、附近的頂點
    /// </summary>
    List<Vector2> MeasureHead(Transform head, Vector3 up)
    {
        var all = BakeAll();
        var pts = new List<Vector2>();
        Vector2 Rel(Vector3 w)
        {
            Vector3 rel = w - head.position;
            float y = Vector3.Dot(rel, up);
            return new Vector2((rel - up * y).magnitude, y);
        }
        foreach (var w in VertsOf(all, head, true)) pts.Add(Rel(w));
        if (pts.Count == 0)
            foreach (var v in all)
            {
                var q = Rel(v.p);
                if (q.y > 0f && q.x < 0.3f && (v.p - head.position).magnitude < 0.45f) pts.Add(q);
            }
        return pts;
    }

    /// <summary>帽子內側離頭（頭髮、耳朵）至少留多少 (m)</summary>
    const float Clearance = 0.01f;

    /// <summary>圓錐形的帽子（輪廓由內往外）要往上挪多少，帽子內側才不會碰到頭上任何一點（留 Clearance）</summary>
    static float FitLift(Vector2[] profile, List<Vector2> pts, float top, float r)
    {
        if (pts.Count == 0) return Clearance;
        float need = float.NegativeInfinity, maxX = profile[profile.Length - 1].x;
        foreach (var q in pts)
        {
            float x = q.x / r;
            if (x > maxX) continue;
            float py = profile[0].y;
            for (int k = 1; k < profile.Length; k++)
                if (x <= profile[k].x) { py = Mathf.Lerp(profile[k - 1].y, profile[k].y, Mathf.InverseLerp(profile[k - 1].x, profile[k].x, x)); break; }
            need = Mathf.Max(need, q.y - (top + py * r));
        }
        if (float.IsNegativeInfinity(need)) return 0f;
        return Mathf.Max(-0.3f * r, need + Clearance);
    }

    // ---------------------------------------------------------------- 圍巾

    /// <summary>一條巾尾：一串點（第 0 點綁在結上），每幀往下垂、往後飄，畫成一條兩面都看得到的布條</summary>
    sealed class Tail
    {
        public Transform knot;
        public Vector3 offset;          // 結上的位置（結的座標）
        public Vector3[] points;
        public float segment, width, thickness, phase;
        public Mesh mesh;
        public Vector3[] verts;
        public Vector3[] shown;         // FollowKnot 用：這一幀畫出來的點（不動模擬用的 points）
    }

    readonly List<Tail> tails = new List<Tail>();
    const int TailSides = 8; // 巾尾的切面：扁的橢圓（有厚度，不是一片紙）
    Vector3 lastRootPos;
    Vector3 bodyVelocity;

    /// <summary>
    /// 圍巾：在脖子根部（脖子骨往下一點、前面再低一點）繞一圈布，圈的形狀照量到的脖子 / 衣領輪廓（每 15 度一格，取最外面的點），
    /// 量不到就用貓戰士的比例。左後方打一個結，垂下兩條巾尾。圈掛在胸口的骨頭上（頭轉的時候圍巾不跟著轉）
    /// </summary>
    void BuildScarf(int index)
    {
        Clear(scarfParts, scarfOwned);
        tails.Clear();
        if (index <= 0) return;
        var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (neck == null || head == null) return;
        Transform chest = neck.parent != null ? neck.parent : neck;
        // 圈是水平的（脖子骨本身往前傾約 24 度，照它的方向圈會前低後高、掉到肩膀上）
        Vector3 axis = Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, axis).normalized;
        Vector3 right = Vector3.Cross(axis, fwd);
        // 尺寸換算：貓戰士的頭骨離地 1.53 m（其他模型照比例）
        float sc = Mathf.Max(0.3f, (head.position.y - (health != null ? health.FeetY : transform.position.y)) / 1.534f);

        // 脖子的半徑（貓戰士量的：前 4.8、兩側 5、後面 5.5 cm，後面是頭髮的根部）；圍在脖子骨往上 1 cm 的地方，前面低 1 cm
        const int S = 24;
        var ring = new float[S];
        for (int k = 0; k < S; k++)
        {
            float c = Mathf.Cos(k * Mathf.PI * 2f / S);
            ring[k] = sc * (c > 0f ? Mathf.Lerp(0.05f, 0.048f, c) : Mathf.Lerp(0.05f, 0.055f, -c));
        }
        float t = 0.022f * sc, hw = 0.022f * sc; // 每一圈布捲起來的厚度、寬度（各一半）
        Vector3 Center(int k)
        {
            float th = k * Mathf.PI * 2f / S;
            Vector3 d = fwd * Mathf.Cos(th) + right * Mathf.Sin(th);
            return neck.position + axis * (0.01f * sc - 0.01f * sc * Mathf.Max(0f, Mathf.Cos(th))) + d * (ring[k] + t * 0.9f);
        }

        Color color = index == 1 ? new Color(.62f, .1f, .07f) : index == 2 ? new Color(.1f, .16f, .32f) : new Color(.92f, .91f, .88f);
        var cloth = Lit(color, 0f, .12f, null, scarfOwned);
        if (cloth.HasProperty("_Cull")) cloth.SetFloat("_Cull", 0f);

        // 在脖子上繞三圈：下面那圈大一點、垂在衣領上，上面那圈貼著脖子；每圈高低起伏不同（看得出是一圈圈交疊繞上去的）
        const int C = 8, Loops = 3;
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var tris = new List<int>();
        var root = new GameObject("Cosmetic_Scarf");
        root.transform.SetPositionAndRotation(neck.position, Quaternion.LookRotation(fwd, axis));
        for (int loop = 0; loop < Loops; loop++)
        {
            int baseIndex = verts.Count;
            float lift = (loop - 1) * hw * 1.2f, grow = (loop == 0 ? 0.012f : loop == 1 ? 0.004f : 0f) * sc;
            float wobble = 0.009f * sc, phase = loop * 2.1f, thick = t * (loop == 1 ? 1.1f : 0.95f);
            for (int k = 0; k <= S; k++)
            {
                int kk = k % S;
                float th = kk * Mathf.PI * 2f / S;
                Vector3 d = fwd * Mathf.Cos(th) + right * Mathf.Sin(th);
                Vector3 c = Center(kk) + axis * (lift + Mathf.Sin(th + phase) * wobble) + d * grow;
                for (int q = 0; q < C; q++)
                {
                    float ph = q * Mathf.PI * 2f / C;
                    Vector3 n = d * Mathf.Cos(ph) + axis * Mathf.Sin(ph);
                    Vector3 w = c + d * (thick * Mathf.Cos(ph)) + axis * (hw * Mathf.Sin(ph));
                    verts.Add(root.transform.InverseTransformPoint(w));
                    norms.Add(root.transform.InverseTransformDirection(n));
                }
            }
            for (int k = 0; k < S; k++)
                for (int q = 0; q < C; q++)
                {
                    int a = baseIndex + k * C + q, b = baseIndex + k * C + (q + 1) % C, c2 = baseIndex + (k + 1) * C + q, d2 = baseIndex + (k + 1) * C + (q + 1) % C;
                    tris.Add(a); tris.Add(b); tris.Add(c2);
                    tris.Add(b); tris.Add(d2); tris.Add(c2);
                }
        }
        var mesh = new Mesh { name = "Runtime_ScarfRing" };
        mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0); mesh.RecalculateBounds();
        scarfOwned.Add(mesh);
        root.AddComponent<MeshFilter>().sharedMesh = mesh;
        root.AddComponent<MeshRenderer>().sharedMaterial = cloth;
        scarfParts.Add(root);

        // 結：左後方（從正面算 -120 度）一顆扁扁的布團
        int knotK = Mathf.RoundToInt(Mathf.Repeat(-120f / 360f, 1f) * S) % S;
        float knotTh = knotK * Mathf.PI * 2f / S;
        Vector3 knotDir = fwd * Mathf.Cos(knotTh) + right * Mathf.Sin(knotTh);
        Vector3 knotPos = Center(knotK) + knotDir * (t * 1.6f + 0.012f * sc);
        var knot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(knot.GetComponent<Collider>());
        knot.name = "Cosmetic_ScarfKnot";
        knot.transform.SetPositionAndRotation(knotPos, Quaternion.LookRotation(knotDir, axis));
        knot.transform.localScale = new Vector3(hw * 3.4f, hw * 3f, t * 2.6f);
        knot.GetComponent<Renderer>().sharedMaterial = cloth;
        Attach(knot.transform, chest);
        Attach(root.transform, chest);

        // 兩條巾尾：從結往下垂（雪白長圍巾比較長）
        float length = (index == 3 ? 0.9f : 0.48f) * sc;
        for (int i = 0; i < 2; i++)
        {
            var tail = new Tail
            {
                knot = knot.transform,
                offset = new Vector3(i == 0 ? -0.25f : 0.25f, -0.2f, 0.3f), // 結的座標（大小 = 結的縮放）
                points = new Vector3[8],
                segment = length * (i == 0 ? 1f : 0.82f) / 7f,
                width = 0.095f * sc,
                thickness = 0.042f * sc,
                phase = i * 1.7f,
            };
            Vector3 start = knot.transform.TransformPoint(tail.offset);
            for (int k = 0; k < tail.points.Length; k++) tail.points[k] = start + Vector3.down * tail.segment * k;
            tail.mesh = new Mesh { name = "Runtime_ScarfTail" };
            tail.mesh.MarkDynamic();
            tail.verts = new Vector3[tail.points.Length * TailSides];
            var tt = new List<int>();
            for (int k = 0; k < tail.points.Length - 1; k++)
                for (int q = 0; q < TailSides; q++)
                {
                    int a = k * TailSides + q, b = k * TailSides + (q + 1) % TailSides, c2 = a + TailSides, d2 = b + TailSides;
                    tt.Add(a); tt.Add(b); tt.Add(c2);
                    tt.Add(b); tt.Add(d2); tt.Add(c2);
                }
            tail.mesh.vertices = tail.verts;
            tail.mesh.SetTriangles(tt, 0);
            scarfOwned.Add(tail.mesh);
            var go = new GameObject("Cosmetic_ScarfTail");
            go.AddComponent<MeshFilter>().sharedMesh = tail.mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = cloth;
            go.layer = chest.gameObject.layer;
            scarfParts.Add(go);
            tails.Add(tail);
        }
        lastRootPos = transform.position;
        UpdateTails();
    }

    /// <summary>巾尾：每一節往「下 + 往後 + 往外」拉，身體在動的時候再往反方向飄、上下擺動；長度固定</summary>
    void UpdateTails()
    {
        if (tails.Count == 0) return;
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        Vector3 v = (transform.position - lastRootPos) / dt;
        lastRootPos = transform.position;
        if (v.sqrMagnitude > 400f) v = Vector3.zero; // 傳送
        bodyVelocity = Vector3.Lerp(bodyVelocity, v, 1f - Mathf.Exp(-dt * 6f));
        float speed = new Vector2(bodyVelocity.x, bodyVelocity.z).magnitude;
        float time = Time.time;
        foreach (var tail in tails)
        {
            if (tail.knot == null) continue;
            Vector3 back = -transform.forward, outward = -transform.right;
            var pts = tail.points;
            pts[0] = tail.knot.TransformPoint(tail.offset);
            for (int k = 1; k < pts.Length; k++)
            {
                float u = k / (pts.Length - 1f);
                Vector3 wind = -bodyVelocity * 0.12f;
                Vector3 flutter = (outward * Mathf.Sin(time * 7f + tail.phase + u * 4f) + Vector3.up * Mathf.Sin(time * 9f + tail.phase + u * 5f)) * (0.15f + 0.25f * Mathf.Clamp01(speed / 4f)) * u;
                Vector3 dir = (Vector3.down * 1f + back * 0.45f + outward * 0.3f + wind + flutter).normalized;
                Vector3 want = pts[k - 1] + dir * tail.segment;
                pts[k] = Vector3.Lerp(pts[k], want, 1f - Mathf.Exp(-dt * 14f));
                pts[k] = pts[k - 1] + (pts[k] - pts[k - 1]).normalized * tail.segment;
            }
            BuildTailMesh(tail, pts);
        }
    }

    /// <summary>照一串點把巾尾的網格做出來</summary>
    void BuildTailMesh(Tail tail, Vector3[] pts)
    {
        Vector3 back = -transform.forward, outward = -transform.right;
        for (int k = 0; k < pts.Length; k++)
        {
            Vector3 seg = (k < pts.Length - 1 ? pts[k + 1] - pts[k] : pts[k] - pts[k - 1]).normalized;
            Vector3 side = Vector3.Cross(seg, outward + back * 0.5f).normalized;
            Vector3 face = Vector3.Cross(side, seg).normalized;
            float u = k / (pts.Length - 1f);
            // 根部比較寬（從結裡鬆開）、末端收窄一點；最後一圈縮小把尾端收起來
            float halfW = tail.width * 0.5f * Mathf.Lerp(1.15f, 0.8f, u) * (k == pts.Length - 1 ? 0.6f : 1f);
            float halfT = tail.thickness * 0.5f * Mathf.Lerp(1.2f, 0.7f, u) * (k == pts.Length - 1 ? 0.6f : 1f);
            for (int q = 0; q < TailSides; q++)
            {
                float ph = q * Mathf.PI * 2f / TailSides;
                tail.verts[k * TailSides + q] = pts[k] + side * (Mathf.Cos(ph) * halfW) + face * (Mathf.Sin(ph) * halfT);
            }
        }
        tail.mesh.vertices = tail.verts;
        tail.mesh.RecalculateNormals();
        tail.mesh.RecalculateBounds();
    }

    /// <summary>
    /// 給在 LateUpdate 裡把身體挪位置的姿勢用（BenchSeat 坐下：骨盆、脖子整個往下往後移）：
    /// 巾尾是在這之前照站著的脖子算的（往後外斜飄），坐著的時候改成從結沿著背往下垂，碰到 keepAbove（椅子）就平鋪在椅面上往後，
    /// 超過椅子後緣再往下垂。drape = 0 → 照原本模擬的形狀搬到結的位置，1 → 完全垂下（坐下 / 起身時漸變）。
    /// 只改這一幀畫出來的網格，模擬還是照原本的
    /// </summary>
    public void FollowKnot(Bounds? keepAbove = null, float drape = 1f)
    {
        Vector3 back = -transform.forward;
        back.y = 0f;
        back = back.sqrMagnitude > 1e-6f ? back.normalized : Vector3.back;
        Vector3 hang = (Vector3.down + back * 0.2f - transform.right * 0.04f).normalized;
        foreach (var tail in tails)
        {
            if (tail.knot == null) continue;
            var pts = tail.points;
            if (tail.shown == null || tail.shown.Length != pts.Length) tail.shown = new Vector3[pts.Length];
            Vector3 start = tail.knot.TransformPoint(tail.offset);
            Vector3 delta = start - pts[0];
            Vector3 prev = start;
            tail.shown[0] = start;
            for (int k = 1; k < pts.Length; k++)
            {
                // 垂下的形狀：每節照 hang 往下；落在椅面範圍裡、低於椅面的話，平鋪在椅面上往後
                Vector3 q = prev + hang * tail.segment;
                if (keepAbove.HasValue)
                {
                    var b = keepAbove.Value;
                    float top = b.max.y + tail.thickness * 0.5f;
                    if (q.x > b.min.x && q.x < b.max.x && q.z > b.min.z && q.z < b.max.z && q.y < top)
                    {
                        float dy = Mathf.Max(0f, prev.y - top);
                        float hz = Mathf.Sqrt(Mathf.Max(0f, tail.segment * tail.segment - dy * dy));
                        q = new Vector3(prev.x, top, prev.z) + back * hz;
                    }
                }
                prev = q;
                Vector3 moved = pts[k] + delta;
                if (keepAbove.HasValue)
                {
                    var b = keepAbove.Value;
                    float top = b.max.y + tail.thickness * 0.5f;
                    if (moved.x > b.min.x && moved.x < b.max.x && moved.z > b.min.z && moved.z < b.max.z && moved.y < top) moved.y = top;
                }
                tail.shown[k] = Vector3.Lerp(moved, q, Mathf.Clamp01(drape));
            }
            BuildTailMesh(tail, tail.shown);
        }
    }

    // ---------------------------------------------------------------- 網格、材質

    static Transform FindChild(Transform t, string contains)
    {
        foreach (Transform c in t)
        {
            if (c.name.Contains(contains)) return c;
            var deep = FindChild(c, contains);
            if (deep != null) return deep;
        }
        return null;
    }

    Material Lit(Color c, float metallic, float smoothness, Texture2D tex = null, List<Object> into = null)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh) { name = "Runtime_Cosmetic" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tex != null ? Color.white : c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", tex != null ? Color.white : c);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (tex != null)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            (into ?? owned).Add(tex);
        }
        (into ?? owned).Add(m);
        return m;
    }

    /// <summary>
    /// 老漁夫那種草編斗笠的貼圖（u = 繞一圈的方向，一張重複 6 次；v = 帽頂 → 帽沿）：
    /// 淺褐色的稻草一根根往外放射（粗細、深淺不一），三圈深一點的編痕，帽沿那一段比較暗、比較亂
    /// </summary>
    static Texture2D KasaStrawTexture()
    {
        const int w = 96, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave, name = "Runtime_KasaStraw" };
        Color light = new Color(.74f, .62f, .42f), dark = new Color(.42f, .33f, .2f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = y / (h - 1f);
                float fiber = 0.5f + 0.5f * Mathf.Sin(x * 1.31f + Mathf.PerlinNoise(x * .2f, y * .03f) * 3f);
                float n = Mathf.PerlinNoise(x * .45f, y * .12f);
                float k = 0.45f + 0.35f * fiber + 0.2f * n;
                foreach (float ring in new[] { .3f, .55f, .8f })
                    k -= 0.35f * Mathf.Clamp01(1f - Mathf.Abs(v - ring) * 70f);
                if (v > .9f) k -= 0.2f * Mathf.PerlinNoise(x * .9f, y * .9f);
                Color c = Color.Lerp(dark, light, Mathf.Clamp01(k));
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 村丁那種竹笠的貼圖：灰黃的竹篾一根根往外放射（一張 6 根、繞一圈 36 根，竹篾之間一條暗縫），
    /// 帽頂到帽沿的 1/3、2/3 各綁一圈深色的竹篾（一格一格的綁痕）
    /// </summary>
    static Texture2D BambooWeaveTexture()
    {
        const int w = 96, h = 128;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave, name = "Runtime_BambooWeave" };
        Color light = new Color(.76f, .7f, .58f), dark = new Color(.38f, .33f, .25f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = y / (h - 1f);
                float s = Mathf.Repeat(x / (w / 6f), 1f);                 // 一根竹篾裡的位置
                float strip = Mathf.SmoothStep(0f, .12f, s) * Mathf.SmoothStep(1f, .88f, s);
                float k = 0.35f + 0.55f * strip + 0.1f * Mathf.PerlinNoise(x * .3f, y * .05f);
                foreach (float ring in new[] { .36f, .68f })
                    if (Mathf.Abs(v - ring) < .025f) k = 0.25f + 0.25f * (Mathf.Repeat(x / 4f, 1f) < .5f ? 1f : 0f); // 綁痕
                Color c = Color.Lerp(dark, light, Mathf.Clamp01(k));
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    /// <summary>稻草編的紋路：直條（沿 u）粗細不一，偶爾一條深色的</summary>
    static Texture2D StrawTexture(Color light, Color dark)
    {
        const int w = 64, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave, name = "Runtime_Straw" };
        for (int x = 0; x < w; x++)
        {
            float stripe = 0.5f + 0.5f * Mathf.Sin(x * 1.9f) * Mathf.Sin(x * 0.37f + 1f);
            bool darkLine = x % 11 == 0;
            for (int y = 0; y < h; y++)
            {
                float n = Mathf.PerlinNoise(x * 0.4f, y * 0.9f);
                Color c = Color.Lerp(dark, light, darkLine ? 0.2f : 0.55f + 0.35f * stripe + 0.1f * n);
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return tex;
    }

    GameObject Box(Transform parent, string name, Material mat, Vector3 localPos, Quaternion localRot, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(localPos, localRot);
        go.transform.localScale = size;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        return go;
    }

    /// <summary>
    /// 繞 Y 軸轉一圈的網格（帽子）：profile 是 (半徑, 高度)，單位是頭的半徑 r（帽子座標：原點 = 頭頂、+Y 朝上、+Z 朝前）。
    /// twoSided = 內側也畫（帽沿底下看得到）
    /// </summary>
    GameObject Lathe(Transform parent, string name, Material mat, float r, int segments, bool twoSided, float fray, params Vector2[] profile)
    {
        int rings = profile.Length, cols = segments + 1;
        // v = 沿著輪廓走了多遠（0 = 帽頂、1 = 帽沿），貼圖的一圈圈編痕才會是等距的
        var arc = new float[rings];
        for (int j = 1; j < rings; j++) arc[j] = arc[j - 1] + Vector2.Distance(profile[j], profile[j - 1]);
        float arcLen = Mathf.Max(1e-4f, arc[rings - 1]);
        var rng = new System.Random(name.GetHashCode());
        var frayR = new float[cols];
        var frayY = new float[cols];
        for (int i = 0; i < segments; i++) { frayR[i] = (float)rng.NextDouble() * fray; frayY[i] = (float)rng.NextDouble() * fray * 0.8f; }
        frayR[segments] = frayR[0]; frayY[segments] = frayY[0];
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        // 每個輪廓點的法線（輪廓的切線轉 90 度）
        var n2 = new Vector2[rings];
        for (int j = 0; j < rings; j++)
        {
            Vector2 prev = profile[Mathf.Max(0, j - 1)], next = profile[Mathf.Min(rings - 1, j + 1)];
            Vector2 tan = (next - prev).normalized;
            n2[j] = new Vector2(-tan.y, tan.x); // 跟三角形的正面同一邊（profile 由上往下時朝外、由下往上時朝內，另一面由 twoSided 補）
        }
        for (int pass = 0; pass < (twoSided ? 2 : 1); pass++)
        {
            int baseIndex = verts.Count;
            float flip = pass == 0 ? 1f : -1f;
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < cols; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                    // 最外面兩圈：每一格的長短、高低不一樣（帽沿毛毛的）
                    float edge = j >= rings - 2 ? (j == rings - 1 ? 1f : 0.5f) : 0f;
                    float px = profile[j].x * (1f + frayR[i] * edge), py = profile[j].y - frayY[i] * edge;
                    verts.Add(new Vector3(px * sn, py, px * cs) * r);
                    normals.Add(new Vector3(n2[j].x * sn, n2[j].y, n2[j].x * cs).normalized * flip);
                    uvs.Add(new Vector2((float)i / segments * 6f, arc[j] / arcLen));
                }
            for (int j = 0; j < rings - 1; j++)
                for (int i = 0; i < segments; i++)
                {
                    int a = baseIndex + j * cols + i, b = a + 1, c = a + cols, d = c + 1;
                    if (pass == 0) { tris.Add(a); tris.Add(c); tris.Add(b); tris.Add(b); tris.Add(c); tris.Add(d); }
                    else { tris.Add(a); tris.Add(b); tris.Add(c); tris.Add(b); tris.Add(d); tris.Add(c); }
                }
        }
        var mesh = new Mesh { name = "Runtime_" + name };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        owned.Add(mesh);
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        return go;
    }
}
