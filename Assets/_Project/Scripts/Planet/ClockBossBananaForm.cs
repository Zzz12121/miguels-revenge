using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 時間吞噬者的二階段外型：震飛玩家後播 5 秒變身動畫，從原本的膠囊身體變成 Banana Man（顏色沿用膠囊的材質），
/// 帽子和臉上的時鐘會搬到香蕉人的頭上（時鐘的衝刺預兆脹縮照常），身上再多長幾個一樣的時鐘，變完說「這才是我的真身」。
/// 變身後的動作全部用程式擺骨頭（模型本身沒有動畫）：
///   - 平常：手垂下、身體上下呼吸
///   - 放任何時鐘陷阱：轉身、右手食指指向陷阱的位置
///   - 衝刺：身體往前傾、貼著地面連續跳躍前進（只是外觀，碰撞還是貼地）
/// 變身時環境光變暗、鏡頭先轉過去特寫（BossFocusCamera）；轉完一圈後右手摸下巴思考（左手托著右手手肘），
/// 再右手指向玩家、左手背到身後；最後推近到臉上的時鐘、時鐘變紅、同時說台詞，按下繼續後切回玩家、燈光恢復。
/// 腳底每幀貼齊地面。模型正面朝向自動判斷（用腳趾的方向）。
/// 只聽 ClockEnemyController 的事件，不改戰鬥邏輯；玩家死掉重來時變回原本的樣子。
/// </summary>
[RequireComponent(typeof(ClockEnemyController))]
public class ClockBossBananaForm : MonoBehaviour
{
    [Tooltip("二階段的模型（Banana Man.fbx）")]
    public GameObject bananaModel;
    [Tooltip("香蕉人站直的高度 (m)")]
    public float bodyHeight = 3.12f;
    [Tooltip("模型面向修正（模型正面不是朝 +Z 時調整）")]
    public float modelYaw = 0f;
    [Tooltip("帽子（留空 = 自動找名字有 Hat 的子物件）")]
    public Transform hat;
    [Tooltip("變身後碰撞體（受擊範圍）的粗細倍率；高度直接用 Body Height，腳底位置不變")]
    public float colliderRadiusScale = 1.3f;
    [Tooltip("身上多長的時鐘大小（跟臉上的時鐘比）")]
    public float bodyClockSize = 0.55f;
    [Tooltip("變身完說的台詞")]
    public string trueFormLine = "餘興時間即將結束，真實的時間將要轉動！";
    [Tooltip("帽子 / 時鐘搬到香蕉頭上後再乘的大小")]
    public float hatSize = 2f;
    public float clockSize = 1f;

    [Header("變身動畫")]
    [Tooltip("震飛玩家後多久開始變身 (秒)")]
    public float transformDelay = 0.15f;
    [Tooltip("舊身體蓄力的時間 (秒)，之後爆開換成香蕉人")]
    public float chargeTime = 1.8f;
    [Tooltip("香蕉人彈出來長大的時間 (秒)")]
    public float growTime = 0.9f;
    [Tooltip("轉完一圈後摸下巴思考的時間 (秒)")]
    public float thinkTime = 1.4f;
    [Tooltip("右手指向玩家、停多久才推近鏡頭 (秒)")]
    public float pointHoldTime = 0.8f;
    [Tooltip("變身時環境光 / 太陽光變暗到原本的幾倍")]
    [Range(0f, 1f)] public float dimTo = 0.3f;
    [Tooltip("燈光變暗 / 恢復的時間 (秒)")]
    public float dimTime = 0.6f;
    public Color transformColor = new Color(1f, 0.82f, 0.35f);

    [Header("動作")]
    [Tooltip("放陷阱時手指著的時間 (秒)")]
    public float pointTime = 0.9f;
    [Tooltip("衝刺時每一跳的高度 (m)")]
    public float dashLift = 0.9f;
    [Tooltip("衝刺時每一跳的時間 (秒)")]
    public float dashHopTime = 0.45f;
    [Tooltip("衝刺時身體往前傾的角度")]
    public float dashPitch = 35f;

    [Header("變身鏡頭")]
    public bool focusCamera = true;
    [Tooltip("鏡頭在 Boss 前方多遠、多高 (m)")]
    public float cameraDistance = 8f;
    public float cameraHeight = 2.2f;
    [Tooltip("鏡頭特寫的總時間 (秒)，含轉過去和轉回來")]
    public float cameraTime = 5.6f;
    [Tooltip("最後特寫臉上時鐘時，鏡頭離時鐘多遠 (m)")]
    public float closeUpDistance = 1.3f;
    [Tooltip("鏡頭推近、時鐘變紅的時間 (秒)")]
    public float closeUpTime = 0.8f;
    public Color clockRedColor = new Color(1f, 0.12f, 0.08f);

    ClockEnemyController controller;
    ClockEnemyHealth health;
    Transform clock;

    Transform pivot, model;
    readonly List<Renderer> oldRenderers = new List<Renderer>();
    Renderer[] newRenderers;
    float modelScale, baseHipsY;
    bool built, banana, transforming;

    // 帽子 / 時鐘搬家：裝在 mount 底下，mount 每幀跟著頭骨走（時鐘自己的 localScale 不動，預兆脹縮才不會壞）
    class Attachment
    {
        public Transform item, mount, parent;
        public Vector3 localPos, localScale;
        public Quaternion localRot;
        public Transform bone; public bool owned; // owned = 自己複製出來的（變回去時直接刪）
        public Vector3 headPos; public Quaternion headRot; // mount 相對骨頭
        public float size;
    }
    Attachment hatAt, clockAt;
    readonly List<Attachment> bodyClocks = new List<Attachment>();

    // 原本的碰撞體大小（變回去時還原）
    struct Capsule { public float height, radius; public Vector3 center; }
    Capsule ccOld, colOld;
    CapsuleCollider bodyCollider;

    // 骨頭
    Transform hips, spine2, spine3, head, headEnd;
    readonly Transform[] arm = new Transform[2], fore = new Transform[2], hand = new Transform[2], fingerTip = new Transform[2];
    readonly List<Transform[]> rightFingers = new List<Transform[]>();
    Transform[] rightIndex;
    readonly Dictionary<Transform, Quaternion> rest = new Dictionary<Transform, Quaternion>();

    // 動作狀態
    Vector3 pointTarget; float pointUntil = -1f, pointW;
    float dashStart = -1f, dashTime;
    int lastHop;
    float groundOffset;
    static readonly RaycastHit[] groundHits = new RaycastHit[8];
    float thinkW, backW;
    bool thinkGoal, backGoal, forcePoint;
    PlayerHealth player;

    void Awake()
    {
        controller = GetComponent<ClockEnemyController>();
        health = GetComponent<ClockEnemyHealth>();
    }

    void OnEnable()
    {
        controller.PhaseTwoBurst += OnPhaseTwo;
        controller.TrapCast += OnTrapCast;
        controller.DashStarted += OnDashStarted;
        controller.FightReset += Revert;
    }

    void OnDisable()
    {
        controller.PhaseTwoBurst -= OnPhaseTwo;
        controller.TrapCast -= OnTrapCast;
        controller.DashStarted -= OnDashStarted;
        controller.FightReset -= Revert;
    }

    void Start()
    {
        clock = controller.faceClock != null ? controller.faceClock : transform.Find("Clock");
        if (hat == null)
            foreach (Transform c in transform)
                if (c.name.Contains("Hat")) { hat = c; break; }
        Build();
    }

    // ---------------- 建立模型 ----------------

    void Build()
    {
        if (bananaModel == null) return;

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            if ((hat != null && r.transform.IsChildOf(hat)) || (clock != null && r.transform.IsChildOf(clock))) continue;
            oldRenderers.Add(r);
        }

        var cc = GetComponent<CharacterController>();
        float feetY = cc.center.y - cc.height * 0.5f;

        pivot = new GameObject("BananaForm").transform;
        pivot.SetParent(transform, false);
        model = Instantiate(bananaModel, pivot).transform;
        model.name = "Banana Man";
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.Euler(0f, modelYaw, 0f);
        model.localScale = Vector3.one;
        foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false; // 骨頭由這支程式擺
        foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);

        newRenderers = model.GetComponentsInChildren<Renderer>(true);
        foreach (var r in newRenderers)
        {
            r.sharedMaterials = Recolor(ToURP(r.sharedMaterials));
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        FindBones();

        // 模型正面朝向：腳趾指的方向就是前面，朝後的話轉 180°
        var foot = Bone("Left Foot"); var toes = Bone("Left Toes");
        if (foot != null && toes != null)
        {
            Vector3 toe = pivot.InverseTransformDirection(toes.position - foot.position);
            if (toe.z < 0f) model.localRotation = Quaternion.Euler(0f, modelYaw + 180f, 0f);
        }

        // 縮放到指定身高，腳底對齊 CharacterController 底部，旋轉中心放在骨盆
        var b = LocalBounds();
        modelScale = b.size.y > 0.01f ? bodyHeight / b.size.y : 1f;
        model.localScale = Vector3.one * modelScale;
        float hipsH = hips != null ? (pivot.InverseTransformPoint(hips.position).y - b.min.y * modelScale) : bodyHeight * 0.5f;
        baseHipsY = feetY + hipsH;
        pivot.localPosition = new Vector3(0f, baseHipsY, 0f);
        model.localPosition = new Vector3(0f, -b.min.y * modelScale - hipsH, 0f);

        foreach (var t in model.GetComponentsInChildren<Transform>(true)) rest[t] = t.localRotation;
        health?.AddRenderers(newRenderers); // 被打會閃白、死掉會一起消失
        pivot.gameObject.SetActive(false);
        built = true;
    }

    Bounds LocalBounds()
    {
        var b = new Bounds(); bool any = false;
        foreach (var r in newRenderers)
        {
            Bounds wb = r.bounds;
            if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var m = new Mesh(); smr.BakeMesh(m, true);
                foreach (var v in m.vertices)
                {
                    var p = pivot.InverseTransformPoint(smr.transform.TransformPoint(v));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
                Destroy(m);
                continue;
            }
            var lp = pivot.InverseTransformPoint(wb.min); var hp = pivot.InverseTransformPoint(wb.max);
            if (!any) { b = new Bounds(lp, Vector3.zero); any = true; }
            b.Encapsulate(lp); b.Encapsulate(hp);
        }
        return b;
    }

    /// <summary>換成原本膠囊的顏色：身體直接用膠囊的材質，關節用同一個材質再暗一點</summary>
    Material[] Recolor(Material[] mats)
    {
        var capsule = GetComponent<Renderer>();
        var src = capsule != null ? capsule.sharedMaterial : null;
        if (src == null) return mats;
        var res = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            bool joint = mats[i] != null && mats[i].name.Contains("Joint");
            if (!joint) { res[i] = src; continue; }
            if (jointMat == null)
            {
                jointMat = new Material(src) { name = src.name + " (Joints)" };
                foreach (var id in new[] { "_BaseColor", "_Color" })
                    if (jointMat.HasProperty(id))
                    {
                        var c = jointMat.GetColor(id);
                        jointMat.SetColor(id, new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.45f, c.a));
                    }
            }
            res[i] = jointMat;
        }
        return res;
    }
    Material jointMat;

    void OnDestroy()
    {
        RestoreLighting();
        if (jointMat != null) Destroy(jointMat);
        foreach (var m in redMats) if (m != null) Destroy(m);
    }

    static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

    /// <summary>素材包的材質是內建 Standard（URP 下會變粉紅），換成同貼圖、同顏色的 URP Lit</summary>
    static Material[] ToURP(Material[] mats)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null) return mats;
        var res = new Material[mats.Length];
        for (int i = 0; i < mats.Length; i++)
        {
            var src = mats[i];
            if (src == null || src.shader == null || src.shader.name.StartsWith("Universal")) { res[i] = src; continue; }
            if (!converted.TryGetValue(src, out var m) || m == null)
            {
                m = new Material(lit) { name = src.name + " (URP)" };
                if (src.HasProperty("_MainTex") && src.mainTexture != null) m.SetTexture("_BaseMap", src.mainTexture);
                if (src.HasProperty("_Color")) m.SetColor("_BaseColor", src.color);
                m.SetFloat("_Smoothness", 0.35f);
                converted[src] = m;
            }
            res[i] = m;
        }
        return res;
    }

    Transform Bone(string n)
    {
        foreach (var t in model.GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
        return null;
    }

    void FindBones()
    {
        hips = Bone("Hips"); spine2 = Bone("Spine 2"); spine3 = Bone("Spine 3");
        head = Bone("Head"); headEnd = Bone("Head_end");
        string[] side = { "Left", "Right" };
        for (int s = 0; s < 2; s++)
        {
            arm[s] = Bone(side[s] + " Arm"); fore[s] = Bone(side[s] + " Forearm"); hand[s] = Bone(side[s] + " Hand");
            fingerTip[s] = Bone(side[s] + " Hand Middle 1");
        }
        foreach (var f in new[] { "Index", "Middle", "Ring", "Pinky" })
        {
            var chain = new[] { Bone("Right Hand " + f + " 1"), Bone("Right Hand " + f + " 2"), Bone("Right Hand " + f + " 3"), Bone("Right Hand " + f + " 3_end") };
            if (f == "Index") rightIndex = chain; else rightFingers.Add(chain);
        }
    }

    // ---------------- 帽子 / 時鐘 ----------------

    Attachment Attach(Transform item, Vector3 worldPos, Quaternion worldRot, float size, Transform bone = null)
    {
        if (bone == null) bone = head;
        if (item == null || bone == null) return null;
        var a = new Attachment
        {
            item = item, parent = item.parent,
            localPos = item.localPosition, localRot = item.localRotation, localScale = item.localScale, size = size,
        };
        a.mount = new GameObject(item.name + " Mount").transform;
        a.mount.SetParent(transform, false);
        a.mount.SetPositionAndRotation(worldPos, worldRot);
        a.mount.localScale = Vector3.one * size;
        item.SetParent(a.mount, false);
        item.localPosition = Vector3.zero;
        item.localRotation = Quaternion.identity;
        a.bone = bone;
        a.headPos = bone.InverseTransformPoint(worldPos);
        a.headRot = Quaternion.Inverse(bone.rotation) * worldRot;
        return a;
    }

    void Detach(Attachment a)
    {
        if (a == null || a.item == null) return;
        if (a.owned) { if (a.mount != null) Destroy(a.mount.gameObject); return; }
        a.item.SetParent(a.parent, false);
        a.item.localPosition = a.localPos; a.item.localRotation = a.localRot; a.item.localScale = a.localScale;
        if (a.mount != null) Destroy(a.mount.gameObject);
    }

    void Follow(Attachment a)
    {
        if (a == null || a.mount == null) return;
        a.mount.SetPositionAndRotation(a.bone.TransformPoint(a.headPos), a.bone.rotation * a.headRot);
        a.mount.localScale = Vector3.one * a.size * (model.localScale.x / modelScale); // 變身長大時跟著一起長
    }

    /// <summary>在站直的姿勢下量頭的位置：頭頂、臉的正前方、頭的寬度</summary>
    void AttachToHead()
    {
        ResetPose();
        Vector3 top = headEnd != null ? headEnd.position : head.position + pivot.up * 0.3f;
        float lo = pivot.InverseTransformPoint(head.position).y, hi = pivot.InverseTransformPoint(top).y;
        float front = 0.25f * bodyHeight * 0.1f, width = 0.5f;
        float faceY = Mathf.Lerp(lo, hi, 0.45f);
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null)
        {
            var m = new Mesh(); smr.BakeMesh(m, true);
            float maxZ = float.NegativeInfinity, minX = float.PositiveInfinity, maxX = float.NegativeInfinity, topY = hi;
            foreach (var v in m.vertices)
            {
                var p = pivot.InverseTransformPoint(smr.transform.TransformPoint(v));
                if (p.y < lo) continue;
                topY = Mathf.Max(topY, p.y);
                if (Mathf.Abs(p.y - faceY) < (hi - lo) * 0.25f && Mathf.Abs(p.x) < 0.15f * bodyHeight) maxZ = Mathf.Max(maxZ, p.z);
                minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x);
            }
            Destroy(m);
            if (!float.IsNegativeInfinity(maxZ)) front = maxZ;
            if (maxX > minX) width = maxX - minX;
            top = pivot.TransformPoint(new Vector3(0f, topY, pivot.InverseTransformPoint(top).z));
        }

        var cc = GetComponent<CharacterController>();
        float fit = width / Mathf.Max(0.1f, cc.radius * 2f); // 原本的帽子是戴在膠囊上的

        if (hat != null)
            hatAt = Attach(hat, top - pivot.up * 0.2f * fit * hatSize, transform.rotation * hat.localRotation, fit * hatSize); // 原本帽子戴在膠囊頂端往下 0.2 m
        if (clock != null)
        {
            // 原本時鐘貼在膠囊的 +X 面，轉 -90° 讓它貼在臉的正面 (+Z)
            Vector3 face = pivot.TransformPoint(new Vector3(0f, faceY, front + 0.02f));
            clockAt = Attach(clock, face, transform.rotation * Quaternion.Euler(0f, -90f, 0f) * clock.localRotation, fit * clockSize);
        }
        AddBodyClocks(fit * clockSize * bodyClockSize);
        Debug.Log($"[BananaForm] 頭頂 {top.y - transform.position.y:F2} m｜帽子 {(hat ? hat.position.y - transform.position.y : float.NaN):F2} m｜" +
                  $"時鐘 {(clock ? clock.position.y - transform.position.y : float.NaN):F2} m（相對 Boss 中心）｜頭寬 {width:F2} m｜縮放 {fit:F2}");
    }

    /// <summary>身上多長幾個跟臉上一樣的時鐘：胸口、肚子、背後、左前臂、右大腿（貼在模型表面）</summary>
    void AddBodyClocks(float size)
    {
        if (clock == null) return;
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
        var verts = new List<Vector3>();
        if (smr != null)
        {
            var m = new Mesh(); smr.BakeMesh(m, true);
            foreach (var v in m.vertices) verts.Add(pivot.InverseTransformPoint(smr.transform.TransformPoint(v)));
            Destroy(m);
        }
        var spots = new (Transform bone, Vector3 dir)[]
        {
            (spine3, new Vector3(0.25f, 0f, 1f)),
            (Bone("Spine 1"), new Vector3(-0.45f, -0.1f, 1f)),
            (spine2, new Vector3(0f, 0.1f, -1f)),
            (fore[0], new Vector3(0f, 0f, 1f)),
            (Bone("Right Thigh"), new Vector3(0.6f, 0f, 1f)),
        };
        // 臉上的時鐘先暫時關掉再複製，複製出來的才不會跑時鐘陷阱的程式
        bool wasActive = clock.gameObject.activeSelf;
        clock.gameObject.SetActive(false);
        foreach (var (bone, d) in spots)
        {
            if (bone == null) continue;
            Vector3 dir = d.normalized, at = pivot.InverseTransformPoint(bone.position);
            float reach = 0.13f * bodyHeight, best = float.NegativeInfinity;
            Vector3 surface = at + dir * 0.1f * bodyHeight;
            foreach (var p in verts)
            {
                if ((p - at).sqrMagnitude > reach * reach) continue;
                float s = Vector3.Dot(p - at, dir);
                if (s > best) { best = s; surface = p; }
            }
            var copy = Instantiate(clock.gameObject).transform;
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var col in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);
            copy.name = "Body Clock";
            copy.gameObject.SetActive(true);
            Vector3 worldDir = pivot.TransformDirection(dir);
            var a = Attach(copy, pivot.TransformPoint(surface) + worldDir * 0.02f,
                Quaternion.LookRotation(worldDir, pivot.up), size, bone);
            if (a == null) { Destroy(copy.gameObject); continue; }
            a.owned = true;
            bodyClocks.Add(a);
            health?.AddRenderers(copy.GetComponentsInChildren<Renderer>(true));
        }
        clock.gameObject.SetActive(wasActive);
    }

    // ---------------- 事件 ----------------

    void OnPhaseTwo()
    {
        if (!built || banana || transforming || health.IsDead) return;
        StartCoroutine(TransformRoutine());
    }

    void OnTrapCast(Vector3 at)
    {
        pointTarget = at;
        pointUntil = Time.time + pointTime;
    }

    void OnDashStarted(Vector3 dir)
    {
        dashStart = Time.time;
        dashTime = Mathf.Max(0.2f, controller.dashDuration);
        lastHop = 0;
        if (banana && !transforming)
        {
            CombatVFX.Ring(Feet(), transformColor, 2.5f);
            GameAudio.Play("SFX/rock_hit", transform.position, 0.6f, 1.4f, 0.05f, 1f, 40f);
        }
    }

    /// <summary>玩家死掉、戰鬥重來：變回原本的樣子</summary>
    void Revert()
    {
        StopAllCoroutines();
        transforming = false;
        banana = false;
        dashStart = -1f; pointUntil = -1f;
        if (BossFocusCamera.Active) BossFocusCamera.Stop(true);
        RestoreClockColor();
        thinkGoal = backGoal = forcePoint = false; thinkW = backW = 0f;
        RestoreLighting();
        Detach(hatAt); Detach(clockAt); hatAt = clockAt = null;
        foreach (var a in bodyClocks) Detach(a);
        bodyClocks.Clear();
        if (pivot != null) pivot.gameObject.SetActive(false);
        foreach (var r in oldRenderers) if (r != null) r.enabled = true;
        ResizeColliders(false);
    }

    /// <summary>變身後把 CharacterController 和身體的 CapsuleCollider 放大到香蕉人的大小（腳底不動）</summary>
    void ResizeColliders(bool big)
    {
        var cc = GetComponent<CharacterController>();
        if (bodyCollider == null) bodyCollider = GetComponent<CapsuleCollider>();
        if (big)
        {
            ccOld = new Capsule { height = cc.height, radius = cc.radius, center = cc.center };
            float feet = cc.center.y - cc.height * 0.5f;
            float r = cc.radius * colliderRadiusScale, h = Mathf.Max(bodyHeight, r * 2f);
            cc.radius = r; cc.height = h; cc.center = new Vector3(cc.center.x, feet + h * 0.5f, cc.center.z);
            if (bodyCollider != null)
            {
                colOld = new Capsule { height = bodyCollider.height, radius = bodyCollider.radius, center = bodyCollider.center };
                float cf = bodyCollider.center.y - bodyCollider.height * 0.5f;
                float cr = bodyCollider.radius * colliderRadiusScale, ch = Mathf.Max(bodyHeight, cr * 2f);
                bodyCollider.radius = cr; bodyCollider.height = ch;
                bodyCollider.center = new Vector3(bodyCollider.center.x, cf + ch * 0.5f, bodyCollider.center.z);
            }
            resized = true;
        }
        else if (resized)
        {
            cc.height = ccOld.height; cc.radius = ccOld.radius; cc.center = ccOld.center;
            if (bodyCollider != null) { bodyCollider.height = colOld.height; bodyCollider.radius = colOld.radius; bodyCollider.center = colOld.center; }
            resized = false;
        }
    }
    bool resized;

    /// <summary>腳底要往上 / 下移多少才剛好踩在地上（CharacterController 的外皮會讓身體浮一點）</summary>
    float GroundOffset()
    {
        Vector3 feet = Feet();
        int n = Physics.RaycastNonAlloc(feet + Vector3.up * 1.5f, Vector3.down, groundHits, 3f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var c = groundHits[i].collider;
            if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlayerHealth>() != null) continue;
            best = Mathf.Max(best, groundHits[i].point.y);
        }
        return float.IsNegativeInfinity(best) ? 0f : Mathf.Clamp(best - (feet.y - 0.05f), -1.2f, 1.2f);
    }

    Vector3 Feet()
    {
        var cc = GetComponent<CharacterController>();
        return transform.position + Vector3.up * (cc.center.y - cc.height * 0.5f + 0.05f);
    }

    // ---------------- 變身 ----------------

    IEnumerator TransformRoutine()
    {
        transforming = true;
        StartDim(true);
        if (focusCamera) BossFocusCamera.Play(transform, float.PositiveInfinity, cameraDistance, cameraHeight, bodyHeight * 0.3f);
        yield return new WaitForSeconds(transformDelay + 0.3f); // 等鏡頭轉過去

        // 1. 蓄力：時鐘越轉越快、帽子越跳越高、金色光點和光環越來越密
        GameAudio.Play("SFX/RPG/metalClick", transform.position, 1f, 0.6f, 0f, 1f, 45f);
        float nextRing = 0f;
        Vector3 hatPos = hat != null ? hat.localPosition : Vector3.zero;
        Quaternion clockRot = clock != null ? clock.localRotation : Quaternion.identity;
        float t = 0f, spin = 0f, nextFx = 0f;
        while (t < chargeTime)
        {
            t += Time.deltaTime;
            float k = t / chargeTime;
            spin += Time.deltaTime * Mathf.Lerp(360f, 2160f, k);
            if (clock != null) clock.localRotation = clockRot * Quaternion.Euler(0f, 0f, spin);
            if (hat != null) hat.localPosition = hatPos + Vector3.up * Mathf.Abs(Mathf.Sin(t * 30f)) * 0.15f * k;
            if (t >= nextFx)
            {
                nextFx = t + Mathf.Lerp(0.18f, 0.05f, k);
                CombatVFX.Impact(transform.position + Random.insideUnitSphere * 1.2f, transformColor);
            }
            if (t >= nextRing)
            {
                nextRing = t + Mathf.Lerp(0.6f, 0.2f, k);
                CombatVFX.Ring(Feet(), transformColor, Mathf.Lerp(4f, 1.5f, k)); // 光環越縮越小，像在往身體收
                GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.5f, Mathf.Lerp(0.7f, 1.6f, k), 0f, 1f, 45f);
            }
            yield return null;
        }
        if (clock != null) clock.localRotation = clockRot;
        if (hat != null) hat.localPosition = hatPos;

        // 2. 爆開：舊身體消失，香蕉人出現
        CombatVFX.Ring(Feet(), transformColor, 6f);
        CombatVFX.Impact(transform.position + Vector3.up * 0.5f, Color.white);
        CombatVFX.Impact(transform.position + Vector3.up * 1.2f, transformColor);
        GameAudio.Play("SFX/rock_hit", transform.position, 1f, 0.8f, 0.05f, 1f, 50f);
        foreach (var r in oldRenderers) if (r != null) r.enabled = false;
        pivot.gameObject.SetActive(true);
        AttachToHead();
        ResizeColliders(true);
        banana = true;

        // 3. 彈出來長大、轉一圈落定
        t = 0f;
        float grow = growTime;
        while (t < grow)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / grow);
            float s = Mathf.LerpUnclamped(0.25f, 1f, BackOut(k));
            model.localScale = Vector3.one * modelScale * s;
            pivot.localRotation = Quaternion.Euler(0f, (1f - k) * (1f - k) * 540f, 0f);
            yield return null;
        }
        model.localScale = Vector3.one * modelScale;
        pivot.localRotation = Quaternion.identity;

        // 4. 右手摸下巴思考、左手托著右手手肘
        thinkGoal = true;
        yield return new WaitForSeconds(thinkTime);

        // 5. 右手指向玩家、左手背到身後
        if (player == null) player = PlayerHealth.Find();
        thinkGoal = false; backGoal = true; forcePoint = true;
        GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.8f, 1.3f, 0f, 1f, 45f);
        yield return new WaitForSeconds(pointHoldTime);

        // 6. 鏡頭推近到臉上的時鐘、時鐘變紅，台詞同時出來（對話框會暫停遊戲，這段用不受暫停影響的時間）
        if (focusCamera && clockAt != null) BossFocusCamera.ZoomTo(clockAt.mount, closeUpDistance, closeUpTime);
        bool done = string.IsNullOrEmpty(trueFormLine) || health.IsDead;
        if (!done)
            StoryDialogue.Play(new[] { StoryDialogue.L(health.enemyName, transform, true, trueFormLine, PlanetStory.BossColor) }, () => done = true);
        float red = 0f;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 1f, 0.6f, 0f, 1f, 50f);
        while (!done || red < 1f)
        {
            red = Mathf.Min(1f, red + Time.unscaledDeltaTime / Mathf.Max(0.01f, closeUpTime));
            SetClockRed(red);
            yield return null;
        }

        // 7. 切回玩家、燈光恢復、手放下
        if (focusCamera) BossFocusCamera.Stop(true);
        backGoal = false; forcePoint = false;
        StartDim(false);
        transforming = false;
    }

    // ---------------- 臉上時鐘變紅 ----------------

    Renderer[] redRenderers;
    Material[][] clockOriginal;
    readonly List<Material> redMats = new List<Material>();
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color"), EmissionId = Shader.PropertyToID("_EmissionColor");

    /// <summary>k = 0~1：把臉上時鐘的材質換成會漸漸變紅、發紅光的複本（不用 PropertyBlock，受擊閃白才不會把紅色洗掉）</summary>
    void SetClockRed(float k)
    {
        if (clock == null) return;
        if (redRenderers == null)
        {
            redRenderers = clock.GetComponentsInChildren<Renderer>(true);
            clockOriginal = new Material[redRenderers.Length][];
            for (int i = 0; i < redRenderers.Length; i++)
            {
                clockOriginal[i] = redRenderers[i].sharedMaterials;
                var mats = new Material[clockOriginal[i].Length];
                for (int j = 0; j < mats.Length; j++)
                {
                    if (clockOriginal[i][j] == null) continue;
                    mats[j] = new Material(clockOriginal[i][j]);
                    mats[j].EnableKeyword("_EMISSION");
                    redMats.Add(mats[j]);
                }
                redRenderers[i].sharedMaterials = mats;
            }
        }
        for (int i = 0; i < redRenderers.Length; i++)
        {
            var mats = redRenderers[i].sharedMaterials;
            for (int j = 0; j < mats.Length; j++)
            {
                var m = mats[j]; var src = clockOriginal[i][j];
                if (m == null || src == null) continue;
                if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.Lerp(src.GetColor(BaseColorId), clockRedColor, k));
                if (m.HasProperty(ColorId)) m.SetColor(ColorId, Color.Lerp(src.GetColor(ColorId), clockRedColor, k));
                if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, clockRedColor * (1.5f * k));
            }
        }
    }

    void RestoreClockColor()
    {
        if (redRenderers != null)
            for (int i = 0; i < redRenderers.Length; i++)
                if (redRenderers[i] != null) redRenderers[i].sharedMaterials = clockOriginal[i];
        foreach (var m in redMats) if (m != null) Destroy(m);
        redMats.Clear();
        redRenderers = null; clockOriginal = null;
    }

    static float BackOut(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // ---------------- 環境光變暗 ----------------

    bool dimSaved;
    float savedAmbientIntensity, savedReflection;
    Color savedAmbient, savedSky, savedEquator, savedGround;
    Light sun; float savedSun;
    Coroutine dimRoutine;

    void StartDim(bool dark)
    {
        if (dark && !dimSaved)
        {
            savedAmbientIntensity = RenderSettings.ambientIntensity;
            savedReflection = RenderSettings.reflectionIntensity;
            savedAmbient = RenderSettings.ambientLight;
            savedSky = RenderSettings.ambientSkyColor; savedEquator = RenderSettings.ambientEquatorColor; savedGround = RenderSettings.ambientGroundColor;
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; }
            if (sun != null) savedSun = sun.intensity;
            dimSaved = true;
        }
        if (!dimSaved) return;
        if (dimRoutine != null) StopCoroutine(dimRoutine);
        dimRoutine = StartCoroutine(Dim(dark ? dimTo : 1f));
    }

    float dimLevel = 1f;
    IEnumerator Dim(float to)
    {
        float from = dimLevel;
        for (float t = 0f; t < dimTime; t += Time.unscaledDeltaTime)
        {
            ApplyDim(Mathf.Lerp(from, to, t / Mathf.Max(0.01f, dimTime)));
            yield return null;
        }
        ApplyDim(to);
        if (to >= 1f) RestoreLighting();
        dimRoutine = null;
    }

    void ApplyDim(float k)
    {
        dimLevel = k;
        RenderSettings.ambientIntensity = savedAmbientIntensity * k;
        RenderSettings.reflectionIntensity = savedReflection * k;
        RenderSettings.ambientLight = savedAmbient * k;
        RenderSettings.ambientSkyColor = savedSky * k; RenderSettings.ambientEquatorColor = savedEquator * k; RenderSettings.ambientGroundColor = savedGround * k;
        if (sun != null) sun.intensity = savedSun * k;
    }

    /// <summary>燈光恢復成變身前的樣子（玩家死掉重來、Boss 被刪掉時也會呼叫）</summary>
    void RestoreLighting()
    {
        if (!dimSaved) return;
        if (dimRoutine != null) { StopCoroutine(dimRoutine); dimRoutine = null; }
        ApplyDim(1f);
        dimSaved = false;
    }

    // ---------------- 每幀擺姿勢 ----------------

    void ResetPose()
    {
        foreach (var kv in rest) if (kv.Key != null) kv.Key.localRotation = kv.Value;
    }

    /// <summary>把骨頭轉到讓「骨頭 → 下一節」指向 dir（世界座標），w = 混合比例</summary>
    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir), w) * bone.rotation;
    }

    void LateUpdate()
    {
        if (!banana || pivot == null) return;
        bool dead = health != null && health.IsDead;
        float time = Time.time;

        // 衝刺：往前傾、貼地連跳
        float dashW = 0f, lift = 0f;
        if (dashStart >= 0f && !transforming && !dead)
        {
            float u = (time - dashStart) / dashTime;
            if (u >= 1f)
            {
                dashStart = -1f;
                CombatVFX.Ring(Feet(), transformColor, 3f);
            }
            else
            {
                dashW = Mathf.Clamp01(u / 0.12f) * Mathf.Clamp01((1f - u) / 0.18f);
                dashW = dashW * dashW * (3f - 2f * dashW);
                int hops = Mathf.Max(1, Mathf.RoundToInt(dashTime / Mathf.Max(0.1f, dashHopTime)));
                float h = u * hops;
                lift = Mathf.Sin((h - Mathf.Floor(h)) * Mathf.PI) * dashLift;
                if ((int)h != lastHop) // 每次落地揚起一圈沙
                {
                    lastHop = (int)h;
                    CombatVFX.Ring(Feet(), transformColor, 1.6f);
                }
            }
        }
        if (!dead) groundOffset = Mathf.MoveTowards(groundOffset, GroundOffset(), Time.deltaTime * 6f);
        float breathe = dead || transforming ? 0f : Mathf.Sin(time * 2.4f) * 0.03f;
        pivot.localPosition = new Vector3(0f, baseHipsY + groundOffset + lift + breathe, 0f);
        if (!transforming) pivot.localRotation = Quaternion.Euler(dashPitch * dashW, 0f, 0f);

        ResetPose();
        Vector3 up = pivot.up, fwd = pivot.forward, right = pivot.right;

        // 手指的混合
        float pointGoal = ((time < pointUntil && !transforming) || forcePoint) && !dead && dashW < 0.5f ? 1f : 0f;
        pointW = Mathf.MoveTowards(pointW, pointGoal, Time.deltaTime * 6f);
        thinkW = Mathf.MoveTowards(thinkW, thinkGoal && !dead ? 1f : 0f, Time.deltaTime * 3f);
        backW = Mathf.MoveTowards(backW, backGoal && !dead ? 1f : 0f, Time.deltaTime * 4f);
        if (forcePoint && player != null) pointTarget = player.transform.position + Vector3.up * 1.2f;
        Vector3 toTarget = pointTarget - (arm[1] != null ? arm[1].position : transform.position);

        // 身體轉向指著的方向
        if (pointW > 0f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(toTarget, up);
            float yaw = Mathf.Clamp(Vector3.SignedAngle(Vector3.ProjectOnPlane(fwd, up), flat, up), -70f, 70f) * pointW;
            if (spine2 != null) spine2.rotation = Quaternion.AngleAxis(yaw * 0.5f, up) * spine2.rotation;
            if (spine3 != null) spine3.rotation = Quaternion.AngleAxis(yaw * 0.5f, up) * spine3.rotation;
        }

        // 手：平常垂下（搖一點），舉手，衝刺時右手往前、左手貼身
        float sway = dead ? 0f : Mathf.Sin(time * 2.4f) * 0.08f;
        for (int s = 0; s < 2; s++)
        {
            float sx = s == 0 ? -1f : 1f;
            Vector3 idleArm = (right * sx * 0.3f - up + fwd * (0.05f + sway * sx)).normalized;
            Vector3 idleFore = (right * sx * 0.15f - up + fwd * 0.35f).normalized;
            Aim(arm[s], fore[s], idleArm, 1f);
            Aim(fore[s], hand[s], idleFore, 1f);

            if (dashW > 0f)
            {
                Vector3 d = (-fwd - up * 0.35f + right * sx * 0.25f).normalized; // 雙手往後擺
                Aim(arm[s], fore[s], d, dashW);
                Aim(fore[s], hand[s], d, dashW);
                Aim(hand[s], fingerTip[s], d, dashW);
            }
        }

        // 思考：右手上臂垂在身前、前臂垂直往上（手肘直角），手摸下巴；左前臂橫在肚子前托著右手手肘
        if (thinkW > 0f)
        {
            float k = thinkW * thinkW * (3f - 2f * thinkW);
            Aim(arm[1], fore[1], (-up + fwd * 0.45f + right * 0.05f).normalized, k);
            Vector3 chin = head != null ? head.position + fwd * 0.06f * bodyHeight - up * 0.02f * bodyHeight : fore[1].position + up;
            if (fore[1] != null && hand[1] != null) Aim(fore[1], hand[1], (chin - fore[1].position).normalized, k);
            if (hand[1] != null && fingerTip[1] != null) Aim(hand[1], fingerTip[1], (up - fwd * 0.3f).normalized, k);
            Aim(arm[0], fore[0], (-up + fwd * 0.45f - right * 0.1f).normalized, k);
            if (fore[0] != null && fore[1] != null) Aim(fore[0], hand[0], (fore[1].position - fwd * 0.02f * bodyHeight - fore[0].position).normalized, k);
            if (head != null && headEnd != null) Aim(head, headEnd, (up + fwd * 0.15f + right * 0.12f).normalized, k * 0.8f); // 歪頭
        }

        // 左手背到身後：上臂往後下、前臂橫在背後
        if (backW > 0f)
        {
            float k = backW * backW * (3f - 2f * backW);
            Aim(arm[0], fore[0], (-up - fwd * 0.5f - right * 0.1f).normalized, k);
            Aim(fore[0], hand[0], (right - fwd * 0.35f - up * 0.1f).normalized, k);
            Aim(hand[0], fingerTip[0], (right - fwd * 0.2f).normalized, k);
        }

        // 右手指向陷阱：整隻手臂伸直、食指伸出、其他手指握起來
        if (pointW > 0f && toTarget.sqrMagnitude > 0.01f)
        {
            Vector3 dir = (toTarget.normalized + up * 0.15f).normalized;
            Aim(arm[1], fore[1], dir, pointW);
            Aim(fore[1], hand[1], dir, pointW);
            Aim(hand[1], fingerTip[1], dir, pointW);
            if (rightIndex != null)
                for (int i = 0; i < 3; i++) Aim(rightIndex[i], rightIndex[i + 1], dir, pointW);
            Vector3 axis = Vector3.Cross(dir, -up);
            if (axis.sqrMagnitude < 1e-4f) axis = right;
            axis.Normalize();
            foreach (var f in rightFingers)
                for (int i = 0; i < 3; i++)
                    Aim(f[i], f[i + 1], Quaternion.AngleAxis(60f * (i + 1), axis) * dir, pointW);
            if (head != null && headEnd != null) Aim(head, headEnd, (up + toTarget.normalized * 0.35f).normalized, pointW * 0.6f);
        }

        Follow(hatAt);
        Follow(clockAt);
        foreach (var a in bodyClocks) Follow(a);
    }
}
