using UnityEngine;

/// <summary>
/// 登山步道的安全範圍（道場東牆外的整座後山）。
/// 離開步道不會馬上死：要「掉下山谷」——腳比最近的步道 / 山頂平台 / 吊橋低超過 fallDepth 才死亡，
/// 玩家會先看到自己往霧裡掉一段。沒有掉下去、但在山坡上走得太遠（maxWanderDistance）也算迷失。
/// 死亡走 PlayerHealth 原本的死亡與重生流程。所有座標都是這個物件的區域座標（生成器會填好）。
/// </summary>
public class TrailBoundsKillZone : MonoBehaviour
{
    [Header("作用範圍：x 大於這個值才檢查（道場院子裡不檢查）")]
    public float activeMinX = 48.6f;

    [Header("步道中心線（含路面高度）")]
    public Vector3[] pathPoints;
    [Tooltip("離步道中心線多遠以內算在步道上（在步道上永遠不會死）")]
    public float allowedHalfWidth = 2.9f;

    [Header("墜落判定")]
    [Tooltip("離開步道後，腳比最近的安全處低超過這個高度才死亡（墜入山谷的感覺）")]
    public float fallDepth = 7f;
    [Tooltip("沒有掉下去、但離所有安全處超過這個水平距離，也算迷失在山霧中")]
    public float maxWanderDistance = 22f;

    [Header("其他安全區")]
    public Vector3 plateauCenter;
    public float plateauRadius = 8f;
    public Vector3 bridgeStart, bridgeEnd;
    public float bridgeHalfWidth = 1.6f;

    public string fallReason = "墜入山谷";
    public string lostReason = "迷失在山霧中";

    PlayerHealth player;

    void Update()
    {
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }
        if (player.IsDead) return;
        Vector3 p = transform.InverseTransformPoint(player.transform.position);
        if (p.x < activeMinX) return;
        float feet = player.FeetY - transform.position.y;

        // 找最近的安全處：步道 / 平台 / 吊橋，取水平距離最近的那個，以及它的高度
        float best = float.MaxValue, refY = 0f, safeRadius = allowedHalfWidth;
        Nearest(PathDistance(p, out float py), py, allowedHalfWidth, ref best, ref refY, ref safeRadius);
        float dpl = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(plateauCenter.x, plateauCenter.z)) - plateauRadius;
        Nearest(Mathf.Max(0f, dpl), plateauCenter.y, 0.6f, ref best, ref refY, ref safeRadius);
        Nearest(BridgeDistance(p, out float by), by, bridgeHalfWidth + 0.4f, ref best, ref refY, ref safeRadius);

        if (best <= safeRadius) return;                                   // 還在步道上
        if (feet < refY - fallDepth) { player.Kill(fallReason); return; } // 掉下山谷
        if (best > maxWanderDistance) player.Kill(lostReason);           // 走太遠
    }

    static void Nearest(float d, float y, float radius, ref float best, ref float refY, ref float safeRadius)
    {
        if (d - radius >= best - safeRadius) return;
        best = d; refY = y; safeRadius = radius;
    }

    float PathDistance(Vector3 p, out float y)
    {
        y = 0f;
        if (pathPoints == null || pathPoints.Length < 2) return float.MaxValue;
        float best = float.MaxValue;
        var q = new Vector2(p.x, p.z);
        for (int i = 0; i < pathPoints.Length - 1; i++)
        {
            var a = new Vector2(pathPoints[i].x, pathPoints[i].z);
            var b = new Vector2(pathPoints[i + 1].x, pathPoints[i + 1].z);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            float d = Vector2.Distance(q, a + ab * t);
            if (d < best) { best = d; y = Mathf.Lerp(pathPoints[i].y, pathPoints[i + 1].y, t); }
        }
        return best;
    }

    float BridgeDistance(Vector3 p, out float y)
    {
        Vector3 ab = bridgeEnd - bridgeStart;
        y = bridgeStart.y;
        if (ab.sqrMagnitude < 0.01f) return float.MaxValue;
        var ab2 = new Vector2(ab.x, ab.z);
        float t = Mathf.Clamp01(Vector2.Dot(new Vector2(p.x - bridgeStart.x, p.z - bridgeStart.z), ab2) / ab2.sqrMagnitude);
        Vector3 c = bridgeStart + ab * t;
        // 吊橋中段會下垂約 4m：參考高度跟著往下
        y = c.y - 4f * 4f * t * (1f - t);
        return new Vector2(p.x - c.x, p.z - c.z).magnitude;
    }

    void OnDrawGizmosSelected()
    {
        if (pathPoints == null) return;
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
        for (int i = 0; i < pathPoints.Length - 1; i++)
            Gizmos.DrawLine(transform.TransformPoint(pathPoints[i]), transform.TransformPoint(pathPoints[i + 1]));
        Gizmos.DrawWireSphere(transform.TransformPoint(plateauCenter), plateauRadius);
        Gizmos.DrawLine(transform.TransformPoint(bridgeStart), transform.TransformPoint(bridgeEnd));
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.3f);
        Gizmos.DrawLine(transform.TransformPoint(plateauCenter), transform.TransformPoint(plateauCenter + Vector3.down * fallDepth));
    }
}
