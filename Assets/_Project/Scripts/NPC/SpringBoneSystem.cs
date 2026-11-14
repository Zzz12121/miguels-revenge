using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 彈簧骨：讓頭髮、袖子、長袍下襬跟著身體的動作晃動（慣性、重力、彈回原位），不會穿進腿和身體。
/// 骨頭是 Blender 加的（Art/DojoMaster/dojomaster_build.py），名字是 SB_ 開頭，一串一串的：
///   SB_HairBack / SB_HairL / SB_HairR（頭髮）、SB_SleeveL / SB_SleeveR（外袍的寬袖子）、SB_Skirt±角度（長袍 / 裙子下襬，繞腰 8 串）
/// 玩家（貓戰士）的骨頭由選單 GeminiGrowUp > 玩家 > 加上頭髮與裙子的彈簧骨 加到 PF_PlayerCatWarrior_Visual。
/// 掛在角色的根物件上（腳底），遊戲開始時自動找到所有 SB_ 骨頭和身體的碰撞範圍（大腿、小腿、腰、胸、頭、地板）。
/// 每一種的手感（硬度、阻力、重力）可以在 Settings 裡調。
/// </summary>
[DefaultExecutionOrder(100)] // 在動畫、出招的骨架疊加之後才算
[DisallowMultipleComponent]
public sealed class SpringBoneSystem : MonoBehaviour
{
    [System.Serializable]
    public class ChainSettings
    {
        [Tooltip("骨頭名字開頭（例如 SB_Hair）")]
        public string namePrefix;
        [Tooltip("硬度：越大越快彈回原本的形狀")]
        [Range(0f, 4f)] public float stiffness = 1f;
        [Tooltip("阻力：越大晃得越少、越快停下來")]
        [Range(0f, 1f)] public float drag = 0.4f;
        [Tooltip("重力：越大越往下垂")]
        [Range(0f, 2f)] public float gravity = 0.3f;
        [Tooltip("碰撞時這串骨頭的粗細（跟身高的比例）")]
        [Range(0f, 0.1f)] public float thickness = 0.02f;
        public ChainSettings() { }
        public ChainSettings(string prefix, float stiffness, float drag, float gravity, float thickness)
        { namePrefix = prefix; this.stiffness = stiffness; this.drag = drag; this.gravity = gravity; this.thickness = thickness; }
    }

    public ChainSettings[] settings =
    {
        new ChainSettings("SB_Hair", 1.4f, 0.45f, 0.25f, 0.015f),
        new ChainSettings("SB_Sleeve", 1.0f, 0.4f, 0.45f, 0.02f),
        new ChainSettings("SB_Skirt", 0.8f, 0.35f, 0.5f, 0.03f),
    };
    [Tooltip("不會穿進腿、身體、地板")]
    public bool useColliders = true;
    [Tooltip("身體碰撞範圍的大小倍率")]
    [Range(0.5f, 2f)] public float colliderScale = 1f;
    [Tooltip("一幀內移動超過這個距離（公尺）就當作瞬移：直接重設，不會甩一大下")]
    public float teleportDistance = 1.2f;
    [Tooltip("模型原本（身高約 1.0）髖部到頭的距離：用來換算碰撞範圍的大小（老師父 0.34、貓戰士 0.277）")]
    public float hipsToHead = 0.34f;

    class Joint
    {
        public Transform bone, tail;
        public Quaternion localRest;
        public Vector3 axis;
        public float length, hit;
        public Vector3 current, previous;
        public ChainSettings s;
    }
    struct Capsule { public Transform a, b; public float radius; }

    readonly List<Joint> joints = new List<Joint>();
    readonly List<Capsule> capsules = new List<Capsule>();
    readonly List<Transform> legBones = new List<Transform>();
    float unit = 1f;           // 模型的 1 單位是幾公尺（模型身高約 1.0 單位）
    Vector3 lastRoot;
    bool built;

    void Start() => Build();

    public void Build()
    {
        joints.Clear(); capsules.Clear(); legBones.Clear();
        var all = GetComponentsInChildren<Transform>(true);
        var map = new Dictionary<string, Transform>();
        foreach (var t in all) if (!map.ContainsKey(t.name)) map[t.name] = t;

        var hips = Get(map, "Hips"); var head = Get(map, "Head");
        if (hips != null && head != null) unit = Mathf.Max(0.01f, Vector3.Distance(hips.position, head.position) / Mathf.Max(0.05f, hipsToHead));

        // 每一串從根往末端排（子骨頭要在父骨頭算完之後才算）
        foreach (var t in all)
        {
            if (!IsSpring(t) || IsSpring(t.parent)) continue;
            for (var b = t; b != null && IsSpring(b); b = SpringChild(b))
            {
                var tail = SpringChild(b);
                if (tail == null) break;
                var s = SettingsFor(b.name);
                if (s == null) break;
                joints.Add(new Joint
                {
                    bone = b, tail = tail, s = s,
                    localRest = b.localRotation,
                    axis = tail.localPosition.normalized,
                    length = Vector3.Distance(b.position, tail.position),
                    hit = s.thickness * unit,
                    current = tail.position, previous = tail.position,
                });
            }
        }

        // 身體的碰撞範圍（膠囊：兩端點 + 半徑）
        void Cap(string a, string b, float r)
        {
            var ta = Get(map, a); var tb = b != null ? Get(map, b) : ta;
            if (ta != null && tb != null) capsules.Add(new Capsule { a = ta, b = tb, radius = r * unit * colliderScale });
        }
        Cap("Hips", null, 0.09f);
        Cap("Spine", "UpperChest", 0.085f);
        Cap("Head", null, 0.07f);
        Cap("Left_UpperLeg", "Left_LowerLeg", 0.06f);
        Cap("Right_UpperLeg", "Right_LowerLeg", 0.06f);
        Cap("Left_LowerLeg", "Left_Foot", 0.048f);
        Cap("Right_LowerLeg", "Right_Foot", 0.048f);
        foreach (var n in new[] { "Left_LowerLeg", "Right_LowerLeg", "Left_Foot", "Right_Foot", "Left_Toes", "Right_Toes" })
        { var t = Get(map, n); if (t != null) legBones.Add(t); }
        lastRoot = transform.position;
        built = true;
    }

    void LateUpdate()
    {
        if (!built) return;
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f) return;
        if ((transform.position - lastRoot).sqrMagnitude > teleportDistance * teleportDistance) ResetPose();
        lastRoot = transform.position;
        // 地板：站著時是腳底（根物件）；跪坐時根物件會比坐墊高，改用小腿 / 腳最低的那根骨頭往下一點
        float floor = transform.position.y;
        foreach (var t in legBones) floor = Mathf.Min(floor, t.position.y - 0.035f * unit);

        foreach (var j in joints)
        {
            Quaternion restRot = j.bone.parent.rotation * j.localRest;
            Vector3 restDir = restRot * j.axis;
            Vector3 origin = j.bone.position;
            Vector3 next = j.current + (j.current - j.previous) * (1f - j.s.drag)
                         + restDir * (j.s.stiffness * dt)
                         + Vector3.down * (j.s.gravity * dt);
            next = origin + (next - origin).normalized * j.length;

            if (useColliders)
            {
                foreach (var c in capsules)
                {
                    Vector3 p = Closest(c.a.position, c.b.position, next);
                    float r = c.radius + j.hit;
                    Vector3 d = next - p;
                    if (d.sqrMagnitude < r * r)
                    {
                        next = p + (d.sqrMagnitude > 1e-8f ? d.normalized : restDir) * r;
                        next = origin + (next - origin).normalized * j.length;
                    }
                }
                if (next.y < floor + j.hit)
                {
                    next.y = floor + j.hit;
                    next = origin + (next - origin).normalized * j.length;
                }
            }

            j.previous = j.current;
            j.current = next;
            j.bone.rotation = Quaternion.FromToRotation(restDir, next - origin) * restRot;
        }
    }

    /// <summary>全部回到原本的形狀（瞬移、重生時用）</summary>
    public void ResetPose()
    {
        foreach (var j in joints)
        {
            j.bone.localRotation = j.localRest;
            j.current = j.previous = j.tail.position;
        }
    }

    static Vector3 Closest(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float l = ab.sqrMagnitude;
        if (l < 1e-8f) return a;
        return a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / l);
    }

    ChainSettings SettingsFor(string boneName)
    {
        foreach (var s in settings) if (s != null && !string.IsNullOrEmpty(s.namePrefix) && boneName.StartsWith(s.namePrefix)) return s;
        return null;
    }

    static bool IsSpring(Transform t) => t != null && t.name.StartsWith("SB_");

    static Transform SpringChild(Transform t)
    {
        foreach (Transform c in t) if (IsSpring(c)) return c;
        return null;
    }

    static Transform Get(Dictionary<string, Transform> map, string name) => map.TryGetValue(name, out var t) ? t : null;
}
