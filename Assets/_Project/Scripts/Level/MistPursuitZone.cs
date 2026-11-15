using UnityEngine;

/// <summary>
/// 吞噬的迷霧：玩家沿著 start → end 的方向往迷霧深處走（例如在孤島吊橋上往回逃），
///   - 霧越來越濃（場景霧密度、顏色）
///   - 每秒持續扣血，越深扣越多
///   - 移動越來越慢
///   - 走到 deathAt（0~1 的進度）之前一定會死
/// 退回霧外就全部恢復。所有座標都是這個物件的區域座標。
/// </summary>
public class MistPursuitZone : MonoBehaviour
{
    [Tooltip("迷霧開始（進度 0）與最深處（進度 1）")]
    public Vector3 start, end = Vector3.forward * 30f;
    [Tooltip("離中心線多遠還算在迷霧裡（吊橋寬度）")]
    public float halfWidth = 4f;

    [Header("傷害（每秒，依進度從 min 到 max）")]
    public float minDamagePerSecond = 4f;
    public float maxDamagePerSecond = 30f;
    [Tooltip("進度到這裡就直接死亡（一定走不到盡頭）")]
    [Range(0.3f, 1f)] public float deathAt = 0.85f;

    [Header("減速（移動速度倍率，依進度從 1 降到這個值）")]
    [Range(0.05f, 1f)] public float slowestMultiplier = 0.22f;

    [Header("霧")]
    public float maxFogDensity = 0.12f;
    public Color fogColor = new Color(0.78f, 0.8f, 0.8f);

    public string deathReason = "被迷霧吞噬";

    PlayerHealth player;
    ThirdPersonController motor;
    int lease;
    float damageCarry, fogK;
    bool baseCaptured;
    float baseDensity; Color baseColor; bool baseFog; FogMode baseMode;

    void OnEnable() => CaptureFog();

    void CaptureFog()
    {
        baseFog = RenderSettings.fog; baseMode = RenderSettings.fogMode;
        baseDensity = RenderSettings.fogDensity; baseColor = RenderSettings.fogColor;
        baseCaptured = true;
    }

    void OnDisable()
    {
        ReleaseSlow();
        if (baseCaptured)
        {
            RenderSettings.fog = baseFog; RenderSettings.fogMode = baseMode;
            RenderSettings.fogDensity = baseDensity; RenderSettings.fogColor = baseColor;
        }
    }

    void ReleaseSlow()
    {
        if (motor != null && lease != 0) motor.RemoveSpeedMultiplierLease(lease);
        lease = 0;
    }

    /// <summary>0 = 在霧外，1 = 最深處</summary>
    public float Progress(Vector3 world)
    {
        Vector3 a = transform.TransformPoint(start), b = transform.TransformPoint(end);
        Vector3 ab = b - a;
        var ab2 = new Vector2(ab.x, ab.z);
        if (ab2.sqrMagnitude < 0.01f) return 0f;
        var ap = new Vector2(world.x - a.x, world.z - a.z);
        float t = Vector2.Dot(ap, ab2) / ab2.sqrMagnitude;
        if (t <= 0f) return 0f;
        var closest = new Vector2(a.x, a.z) + ab2 * Mathf.Clamp01(t);
        if (Vector2.Distance(new Vector2(world.x, world.z), closest) > halfWidth) return 0f;
        return Mathf.Clamp01(t);
    }

    void Update()
    {
        if (player == null)
        {
            player = PlayerHealth.Find();
            if (player == null) return;
            motor = player.GetComponent<ThirdPersonController>();
        }
        // 每幀重新申請減速（其他系統重置移動時可能清掉所有倍率）
        ReleaseSlow();

        float u = player.IsDead ? 0f : Progress(player.transform.position);
        fogK = Mathf.MoveTowards(fogK, u, Time.deltaTime * 0.8f);
        ApplyFog(fogK);
        if (u <= 0f || player.IsDead) { damageCarry = 0f; return; }

        if (u >= deathAt) { player.Kill(deathReason); return; }

        // 減速：越深越慢
        if (motor != null)
            lease = motor.AddSpeedMultiplierLease(Mathf.Lerp(1f, slowestMultiplier, Mathf.SmoothStep(0f, 1f, u / deathAt)));

        // 持續扣血：累積到 1 點以上才扣，避免每幀都觸發受傷音效
        damageCarry += Mathf.Lerp(minDamagePerSecond, maxDamagePerSecond, u / deathAt) * Time.deltaTime;
        if (damageCarry >= 2f)
        {
            float dmg = damageCarry;
            damageCarry = 0f;
            player.TakeDamage(dmg, Vector3.zero, true, deathReason, false);
        }
    }

    void ApplyFog(float k)
    {
        if (!baseCaptured) CaptureFog();
        if (k <= 0.001f)
        {
            RenderSettings.fog = baseFog; RenderSettings.fogMode = baseMode;
            RenderSettings.fogDensity = baseDensity; RenderSettings.fogColor = baseColor;
            return;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        float from = baseFog && baseMode == FogMode.Exponential ? baseDensity : 0f;
        RenderSettings.fogDensity = Mathf.Lerp(from, maxFogDensity, Mathf.SmoothStep(0f, 1f, k));
        RenderSettings.fogColor = Color.Lerp(baseColor, fogColor, k);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 a = transform.TransformPoint(start), b = transform.TransformPoint(end);
        Gizmos.color = new Color(0.8f, 0.8f, 0.85f, 0.8f);
        Gizmos.DrawLine(a, b);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(Vector3.Lerp(a, b, deathAt), halfWidth);
    }
}
