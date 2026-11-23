using UnityEngine;

/// <summary>
/// 看動畫裡腳的高度判斷「腳踩到地」的那一刻，讓腳步聲跟動畫對上（不是每走幾公尺播一次）。
/// 用法：new FootPlantDetector(root, 左腳骨頭, 右腳骨頭)；每幀（動畫算完之後，LateUpdate 最準）呼叫 Step，
/// 回傳 true 時 at = 踩下去那隻腳的位置。開始走路時呼叫 Reset。
/// 每隻腳各自記最近的最低 / 最高點（慢慢遺忘），腳抬到這個範圍的 45% 以上算「抬起」，
/// 抬起之後再降到 18% 以下就算「踩地」。所以不用知道模型多大、步伐多高。
/// </summary>
public sealed class FootPlantDetector
{
    sealed class Foot
    {
        public Transform bone;
        public float low = float.MaxValue, high = float.MinValue;
        public bool lifted;
    }

    readonly Transform root;
    readonly Foot left, right;
    public bool Valid => root != null && left.bone != null && right.bone != null;

    public FootPlantDetector(Transform root, Transform leftFoot, Transform rightFoot)
    {
        this.root = root;
        left = new Foot { bone = leftFoot };
        right = new Foot { bone = rightFoot };
    }

    /// <summary>在 root 底下用名字找左右腳（找不到就用 Humanoid 骨架）</summary>
    public static FootPlantDetector Find(Transform root, Animator animator = null)
    {
        if (root == null) return null;
        Transform l = Deep(root, "Left_Foot") ?? Deep(root, "LeftFoot") ?? Deep(root, "mixamorig:LeftFoot");
        Transform r = Deep(root, "Right_Foot") ?? Deep(root, "RightFoot") ?? Deep(root, "mixamorig:RightFoot");
        if ((l == null || r == null) && animator != null && animator.isHuman)
        {
            l = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
            r = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        }
        var d = new FootPlantDetector(root, l, r);
        return d.Valid ? d : null;
    }

    public void Reset()
    {
        foreach (var f in new[] { left, right }) { f.low = float.MaxValue; f.high = float.MinValue; f.lifted = false; }
    }

    public bool Step(float dt, out Vector3 at)
    {
        at = default;
        if (!Valid) return false;
        bool planted = Check(left, dt, ref at);
        planted |= Check(right, dt, ref at);
        return planted;
    }

    bool Check(Foot f, float dt, ref Vector3 at)
    {
        float h = f.bone.position.y - root.position.y;
        // 最低 / 最高點慢慢往目前高度靠（走上台階、換動作後會重新適應）
        float forget = dt * 0.15f;
        f.low = f.low == float.MaxValue ? h : Mathf.Min(h, f.low + forget);
        f.high = f.high == float.MinValue ? h : Mathf.Max(h, f.high - forget);
        float range = f.high - f.low;
        if (range < 0.02f) return false; // 腳幾乎沒抬：還不能判斷
        if (!f.lifted)
        {
            if (h > f.low + range * 0.45f) f.lifted = true;
            return false;
        }
        if (h > f.low + range * 0.18f) return false;
        f.lifted = false;
        at = f.bone.position;
        return true;
    }

    static Transform Deep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = Deep(c, name); if (r != null) return r; }
        return null;
    }
}
