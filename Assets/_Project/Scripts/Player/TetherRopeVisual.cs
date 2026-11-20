using UnityEngine;

/// <summary>
/// 拉索的外觀：一條麻繩，前端綁著三爪的鐵鉤爪（EnergyTetherSkill 在 Rope 外觀時用）。
/// EnergyTetherSkill 每幀告訴它鉤爪在哪裡、繩子鬆多少（Show），用完 Hide。
/// 在所有姿勢（動畫、PlayerLoadout、SwingBodyAccent）擺完之後才畫：右手臂伸向鉤爪，繩子從右手掌拉出去。
/// 鉤爪、繩子都是程式做的，不需要任何資源。
/// 外觀（Skins）：預設是麻繩鐵鉤；在行商那裡用錢買的鉤索造型由 PlayerCosmetics 呼叫 SetSkin 換上。
/// </summary>
[DefaultExecutionOrder(32200)]
[DisallowMultipleComponent]
public sealed class TetherRopeVisual : MonoBehaviour
{
    [Min(.005f)] public float ropeWidth = .035f;
    [Range(8, 64)] public int ropeSegments = 28;
    [Tooltip("鉤爪的大小（1 = 全長約 0.3 m）")]
    [Min(.1f)] public float hookScale = 1f;
    [Tooltip("右手臂伸向鉤爪的程度（0 = 不動手臂）")]
    [Range(0f, 1f)] public float armReach = .85f;

    /// <summary>鉤索的造型：繩子的顏色（亮、暗兩色的三股紋）和鉤爪的金屬</summary>
    public readonly struct Skin
    {
        public readonly string name;
        public readonly Color hemp, dark, metal;
        public readonly float metallic, smoothness;
        public Skin(string name, Color hemp, Color dark, Color metal, float metallic, float smoothness)
        { this.name = name; this.hemp = hemp; this.dark = dark; this.metal = metal; this.metallic = metallic; this.smoothness = smoothness; }
    }

    public static readonly Skin[] Skins =
    {
        new Skin("麻繩鐵鉤", new Color(.62f, .48f, .3f), new Color(.3f, .21f, .12f), new Color(.2f, .2f, .22f), .85f, .45f),
        new Skin("朱繩金鉤", new Color(.78f, .17f, .1f), new Color(.34f, .05f, .04f), new Color(.86f, .64f, .24f), 1f, .72f),
        new Skin("墨繩銀鉤", new Color(.2f, .2f, .23f), new Color(.04f, .04f, .05f), new Color(.8f, .82f, .86f), 1f, .82f),
        new Skin("青藤竹鉤", new Color(.42f, .6f, .26f), new Color(.15f, .26f, .09f), new Color(.66f, .6f, .32f), 0f, .35f),
    };

    int skin;

    /// <summary>換鉤索的造型（Skins 的第幾個）</summary>
    public void SetSkin(int index)
    {
        index = Mathf.Clamp(index, 0, Skins.Length - 1);
        if (index == skin && ropeTexture != null) return;
        skin = index;
        if (ropeMaterial == null) return; // 還沒 Build：Build 時照 skin 做
        if (ropeTexture != null) Destroy(ropeTexture);
        ropeTexture = MakeRopeTexture(Skins[skin]);
        ropeMaterial.mainTexture = ropeTexture;
        PaintIron();
    }

    public int CurrentSkin => skin;

    LineRenderer rope;
    Transform hook;
    Material ropeMaterial, ironMaterial;
    Texture2D ropeTexture;
    Animator animator;
    Transform upperArm, foreArm, hand, middleFinger;

    bool active;
    Vector3 hookPoint, hookDir = Vector3.forward;
    float slack, armWeight;

    /// <summary>鉤爪的尖端在 point、朝 direction 飛（或插在那裡）；slack = 繩子鬆的程度（0 = 拉直、1 = 剛丟出去、甩動得最厲害）</summary>
    public void Show(Vector3 point, Vector3 direction, float ropeSlack)
    {
        active = true;
        hookPoint = point;
        if (direction.sqrMagnitude > 1e-6f) hookDir = direction.normalized;
        slack = Mathf.Clamp01(ropeSlack);
    }

    public void Hide()
    {
        active = false;
        if (rope != null) rope.enabled = false;
        if (hook != null) hook.gameObject.SetActive(false);
    }

    /// <summary>繩子從哪裡拉出去（右手掌；找不到手就用胸前）</summary>
    public Vector3 HandPoint
    {
        get
        {
            ResolveBones();
            if (hand != null) return middleFinger != null ? Vector3.Lerp(hand.position, middleFinger.position, .6f) : hand.position;
            return transform.position + Vector3.up * 1.25f + transform.right * .25f;
        }
    }

    void Awake()
    {
        Build();
        Hide();
    }

    void ResolveBones()
    {
        if (animator != null && hand != null) return;
        animator = GetComponentInChildren<Animator>(true);
        if (animator == null || !animator.isHuman) return;
        upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        foreArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        middleFinger = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
    }

    void LateUpdate()
    {
        armWeight = Mathf.MoveTowards(armWeight, active ? 1f : 0f, Time.unscaledDeltaTime * (active ? 9f : 5f));
        if (armWeight > .001f) PoseArm(armWeight * armReach);
        if (!active) return;

        Vector3 tail = hookPoint - hookDir * (.24f * hookScale); // 繩子綁在鉤爪尾端的環上
        hook.gameObject.SetActive(true);
        hook.SetPositionAndRotation(hookPoint, Quaternion.LookRotation(hookDir, Mathf.Abs(hookDir.y) > .95f ? transform.forward : Vector3.up));
        hook.localScale = Vector3.one * hookScale;
        DrawRope(HandPoint, tail);
    }

    /// <summary>右手臂伸向鉤爪：先把手肘拉直，再把整隻手臂轉向鉤爪（收起來之後朝最後的位置慢慢放下）</summary>
    void PoseArm(float w)
    {
        ResolveBones();
        if (upperArm == null || foreArm == null || hand == null) return;
        Vector3 target = hookPoint;
        Vector3 bend = hand.position - foreArm.position, straight = foreArm.position - upperArm.position;
        if (bend.sqrMagnitude > 1e-6f && straight.sqrMagnitude > 1e-6f)
            foreArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(bend, straight), w * .8f) * foreArm.rotation;
        Vector3 cur = hand.position - upperArm.position, want = target - upperArm.position;
        if (cur.sqrMagnitude > 1e-6f && want.sqrMagnitude > 1e-6f)
            upperArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want), w) * upperArm.rotation;
    }

    void DrawRope(Vector3 from, Vector3 to)
    {
        rope.enabled = true;
        int n = Mathf.Max(8, ropeSegments);
        rope.positionCount = n;
        Vector3 d = to - from;
        float length = d.magnitude;
        Vector3 axis = length > 1e-4f ? d / length : transform.forward;
        Vector3 side = Vector3.Cross(axis, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = transform.right;
        side.Normalize();
        // 剛丟出去的時候繩子是鬆的：往下垂、左右甩；拉直之後只剩一點點下垂
        float sag = Mathf.Min(1.6f, length * (.015f + .12f * slack));
        float wave = Mathf.Min(.5f, length * .05f) * slack;
        float time = Time.unscaledTime;
        for (int i = 0; i < n; i++)
        {
            float u = i / (n - 1f);
            float env = Mathf.Sin(u * Mathf.PI);
            Vector3 p = from + d * u
                        - Vector3.up * (sag * env)
                        + side * (Mathf.Sin(u * Mathf.PI * 3f - time * 18f) * wave * env);
            rope.SetPosition(i, p);
        }
    }

    // ---------------------------------------------------------------- 建立外觀

    void Build()
    {
        Shader sprite = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        ropeTexture = MakeRopeTexture(Skins[skin]);
        if (sprite != null)
        {
            ropeMaterial = new Material(sprite) { name = "Runtime_TetherRope", mainTexture = ropeTexture };
        }

        var ropeGo = new GameObject("TetherRope_Line");
        ropeGo.transform.SetParent(transform, false);
        rope = ropeGo.AddComponent<LineRenderer>();
        rope.useWorldSpace = true;
        rope.widthMultiplier = ropeWidth;
        rope.numCapVertices = 2;
        rope.numCornerVertices = 2;
        rope.textureMode = LineTextureMode.Tile;
        rope.textureScale = new Vector2(1f / (ropeWidth * 2.2f), 1f); // 沿著繩子每公尺重複幾次：一段麻繩紋約 2 倍繩寬
        rope.alignment = LineAlignment.View;
        rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rope.sharedMaterial = ropeMaterial;
        rope.startColor = rope.endColor = Color.white;

        Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? sprite;
        if (lit != null)
        {
            ironMaterial = new Material(lit) { name = "Runtime_TetherHookIron" };
            PaintIron();
        }
        hook = BuildHook().transform;
        hook.SetParent(transform, false);
    }

    void PaintIron()
    {
        if (ironMaterial == null) return;
        var sk = Skins[skin];
        if (ironMaterial.HasProperty("_BaseColor")) ironMaterial.SetColor("_BaseColor", sk.metal);
        if (ironMaterial.HasProperty("_Color")) ironMaterial.SetColor("_Color", sk.metal);
        if (ironMaterial.HasProperty("_Metallic")) ironMaterial.SetFloat("_Metallic", sk.metallic);
        if (ironMaterial.HasProperty("_Smoothness")) ironMaterial.SetFloat("_Smoothness", sk.smoothness);
    }

    /// <summary>
    /// 三爪鉤爪（+Z = 尖端朝前）：一根鐵桿，尾端一個環（綁繩子），前端三支往後彎的爪子
    /// </summary>
    GameObject BuildHook()
    {
        var root = new GameObject("TetherRope_Hook");
        void Part(PrimitiveType type, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            DestroyImmediate(go.GetComponent<Collider>()); // 不能擋到角色、也不能被瞄準打到
            go.transform.SetParent(root.transform, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = ironMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Quaternion alongZ = Quaternion.Euler(90f, 0f, 0f); // Cylinder 原本沿 Y
        // 鐵桿：從尾端 (-0.24) 到前端 (-0.02)
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, -.13f), alongZ, new Vector3(.028f, .11f, .028f));
        // 尾端的環（兩個交叉的扁圓柱當成環）
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0f, -.25f), Quaternion.Euler(0f, 0f, 90f), new Vector3(.05f, .006f, .05f));
        Part(PrimitiveType.Sphere, new Vector3(0f, 0f, -.02f), Quaternion.identity, Vector3.one * .045f);
        // 三支爪：從前端往外伸、再往後彎，爪尖稍微往內勾
        for (int k = 0; k < 3; k++)
        {
            Quaternion around = Quaternion.AngleAxis(k * 120f, Vector3.forward);
            Vector3 a = new Vector3(0f, 0f, -.02f), b = around * new Vector3(0f, .075f, -.05f), c = around * new Vector3(0f, .07f, -.13f);
            Segment(a, b, .02f);
            Segment(b, c, .017f);
        }
        void Segment(Vector3 from, Vector3 to, float thick)
        {
            Vector3 mid = (from + to) * .5f, dir = to - from;
            Part(PrimitiveType.Cylinder, mid, Quaternion.FromToRotation(Vector3.up, dir), new Vector3(thick, dir.magnitude * .5f, thick));
        }
        return root;
    }

    /// <summary>麻繩的紋路：斜斜的三股繩絞在一起，兩側暗一點（圓的感覺）。u = 沿著繩子、v = 繩子的寬</summary>
    static Texture2D MakeRopeTexture(Skin sk)
    {
        const int w = 32, h = 16;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
        Color hemp = sk.hemp, dark = sk.dark;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (h - 1f);
                float strand = Mathf.Repeat(u * 3f + v * 1.5f, 1f);            // 三股斜紋
                float groove = Mathf.SmoothStep(0f, .18f, strand) * Mathf.SmoothStep(1f, .82f, strand);
                float round = Mathf.Sin(v * Mathf.PI);                         // 兩側暗
                float fiber = .92f + .08f * Mathf.PerlinNoise(x * .7f, y * 1.9f);
                Color c = Color.Lerp(dark, hemp, (.35f + .65f * groove) * (.45f + .55f * round)) * fiber;
                c.a = 1f;
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        return tex;
    }

    void OnDestroy()
    {
        if (ropeMaterial != null) Destroy(ropeMaterial);
        if (ironMaterial != null) Destroy(ironMaterial);
        if (ropeTexture != null) Destroy(ropeTexture);
    }
}
