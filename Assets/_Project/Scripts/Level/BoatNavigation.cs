using UnityEngine;

/// <summary>
/// 船隻共用的航行工具：探測水深（不能開上陸地）、船身隨海浪起伏。玩家的 VikingBoat 和敵艦 EnemyBoat 都用這裡。
/// </summary>
public static class BoatNavigation
{
    static readonly RaycastHit[] groundHits = new RaycastHit[16];

    /// <summary>某一點底下最高的地面（陸地、海底、碼頭）；忽略 self 這艘船、所有船（玩家船、敵艦）和角色</summary>
    public static float GroundHeight(Vector3 p, Transform self, float waterY)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, waterY + 30f, p.z), Vector3.down, groundHits, 60f,
                                        ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            var col = groundHits[i].collider;
            if (self != null && col.transform.IsChildOf(self)) continue;
            if (col is CharacterController) continue;
            if (col.GetComponentInParent<EnemyBoat>() != null || col.GetComponentInParent<VikingBoat>() != null) continue;
            if (groundHits[i].point.y > best) best = groundHits[i].point.y;
        }
        return best;
    }

    /// <summary>
    /// 船往 move 移動會不會開上陸地：在前進方向的船頭中間和左右兩側往下探測地面，
    /// 新位置的地面比「海平面 - minDepth」高、而且比現在更淺，就不能走。
    /// 「比現在更淺」才擋：船停在碼頭邊（本來就很淺）時，還是可以往外海開走
    /// </summary>
    public static bool WouldRunAground(Transform boat, Vector3 move, float halfLength, float halfWidth,
                                       float waterY, float minDepth)
    {
        Vector3 dir = new Vector3(move.x, 0f, move.z);
        if (dir.sqrMagnitude < 1e-8f) return false;
        dir.Normalize();
        float limit = waterY - minDepth;
        Vector3 side = Vector3.Cross(Vector3.up, dir);
        Vector3 pos = boat.position;
        Vector3[] offsets =
        {
            dir * halfLength,
            dir * halfLength * 0.7f + side * halfWidth,
            dir * halfLength * 0.7f - side * halfWidth,
        };
        foreach (var o in offsets)
        {
            float now = GroundHeight(pos + o, boat, waterY);
            float next = GroundHeight(pos + move + o, boat, waterY);
            if (next > limit && next > now + 0.01f) return true;
        }
        return false;
    }

    /// <summary>這個位置夠深、放得下一艘船（船身前後左右都在水裡）</summary>
    public static bool IsOpenWater(Vector3 pos, float halfLength, float halfWidth, float waterY, float minDepth)
    {
        float limit = waterY - minDepth;
        Vector3[] offsets =
        {
            Vector3.zero,
            Vector3.forward * halfLength, Vector3.back * halfLength,
            Vector3.right * halfLength, Vector3.left * halfLength,
        };
        foreach (var o in offsets)
            if (GroundHeight(pos + o, null, waterY) > limit) return false;
        return true;
    }

    /// <summary>海面在 p 的高度：量這裡的水深，讓淺水區的浪和畫面一樣變小（SeaWaves）</summary>
    static float Surface(Vector3 p, Transform boat, float waterY)
    {
        float ground = GroundHeight(p, boat, waterY);
        float depth = float.IsNegativeInfinity(ground) ? 1000f : Mathf.Max(0f, waterY - ground);
        return SeaWaves.HeightAt(p, waterY, depth);
    }

    /// <summary>
    /// 船身隨海浪起伏：在船頭、船尾、左舷、右舷取海面高度，算出船的高度、前後俯仰、左右翻滾，
    /// 再平滑地靠過去（smooth 越大越跟手）。forward = 視覺船首方向
    /// </summary>
    public static void FloatOnWaves(Transform boat, float yaw, Vector3 forward, float halfLength, float halfWidth,
                                    float waterY, float draft, float extraPitch, float tiltScale, float smooth,
                                    ref Vector3 smoothedPose)
    {
        Vector3 p = boat.position;
        Vector3 fwd = new Vector3(forward.x, 0f, forward.z).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        float bow = Surface(p + fwd * halfLength, boat, waterY);
        float stern = Surface(p - fwd * halfLength, boat, waterY);
        float port = Surface(p - right * halfWidth, boat, waterY);
        float starboard = Surface(p + right * halfWidth, boat, waterY);
        float centre = Surface(p, boat, waterY);

        // 船身是硬的，浪是彎的：用船頭船尾和兩舷的平均當高度，船身中段才不會浮出水面
        float height = (bow + stern + port + starboard + centre * 2f) / 6f + draft;
        // 視覺船首往上翹 = 繞船身右方向轉負角
        float pitch = -Mathf.Atan2(bow - stern, halfLength * 2f) * Mathf.Rad2Deg * tiltScale + extraPitch;
        float roll = Mathf.Atan2(starboard - port, halfWidth * 2f) * Mathf.Rad2Deg * tiltScale;

        var target = new Vector3(height, pitch, roll);
        float k = 1f - Mathf.Exp(-smooth * Time.deltaTime);
        smoothedPose = Vector3.Lerp(smoothedPose, target, k);

        p.y = smoothedPose.x;
        boat.position = p;
        // 先轉船頭朝向，再沿船首 / 右方向傾斜（模型軸向和船首不同也正確）
        boat.rotation = Quaternion.AngleAxis(smoothedPose.y, right) * Quaternion.AngleAxis(smoothedPose.z, fwd)
                        * Quaternion.Euler(0f, yaw, 0f);
    }
}
