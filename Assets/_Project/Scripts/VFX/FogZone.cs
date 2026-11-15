using UnityEngine;

/// <summary>
/// 大霧區：玩家越靠近 start→end 這條線段，場景的霧越濃、顏色越接近 fogColor。
/// 離開範圍後恢復場景原本的霧。用在道場後山「接近吊橋時大霧瀰漫」，之後再切換到 ForestIsland。
/// 天空盒不吃霧，所以通常搭配大片霧粒子一起用。
/// </summary>
public class FogZone : MonoBehaviour
{
    [Tooltip("霧最濃的線段起點／終點（區域座標）")]
    public Vector3 start, end = Vector3.forward * 10f;
    [Tooltip("距離線段多近時霧達到最濃")]
    public float innerRadius = 8f;
    [Tooltip("距離線段多遠開始起霧")]
    public float outerRadius = 45f;
    [Tooltip("最濃時的霧密度（Exponential）")]
    public float density = 0.07f;
    public Color fogColor = new Color(0.78f, 0.81f, 0.82f);
    [Tooltip("霧變濃的反應速度（避免一步一跳）")]
    public float smoothing = 3f;

    bool baseFog;
    FogMode baseMode;
    float baseDensity;
    Color baseColor;
    float k;
    Transform player;

    void OnEnable()
    {
        baseFog = RenderSettings.fog;
        baseMode = RenderSettings.fogMode;
        baseDensity = RenderSettings.fogDensity;
        baseColor = RenderSettings.fogColor;
    }

    void OnDisable()
    {
        RenderSettings.fog = baseFog;
        RenderSettings.fogMode = baseMode;
        RenderSettings.fogDensity = baseDensity;
        RenderSettings.fogColor = baseColor;
    }

    void Update()
    {
        if (player == null)
        {
            var ph = PlayerHealth.Find();
            if (ph != null) player = ph.transform;
            else if (Camera.main != null) player = Camera.main.transform;
            if (player == null) return;
        }
        Vector3 a = transform.TransformPoint(start), b = transform.TransformPoint(end);
        Vector3 p = player.position;
        Vector3 ab = b - a;
        float t = ab.sqrMagnitude > 0.001f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
        float d = Vector3.Distance(p, a + ab * t);
        float target = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(innerRadius, outerRadius, d));
        k = Mathf.MoveTowards(k, target, smoothing * Time.deltaTime);

        if (k <= 0.001f)
        {
            RenderSettings.fog = baseFog;
            RenderSettings.fogMode = baseMode;
            RenderSettings.fogDensity = baseDensity;
            RenderSettings.fogColor = baseColor;
            return;
        }
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        float from = baseFog && baseMode == FogMode.Exponential ? baseDensity : 0f;
        // 密度用平方曲線：外圍只是淡淡起霧，靠近吊橋才急遽變濃
        RenderSettings.fogDensity = Mathf.Lerp(from, density, k * k);
        RenderSettings.fogColor = Color.Lerp(baseColor, fogColor, k);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.8f, 0.85f, 0.9f, 0.6f);
        Vector3 a = transform.TransformPoint(start), b = transform.TransformPoint(end);
        Gizmos.DrawLine(a, b);
        Gizmos.DrawWireSphere(a, innerRadius); Gizmos.DrawWireSphere(b, innerRadius);
        Gizmos.color = new Color(0.8f, 0.85f, 0.9f, 0.2f);
        Gizmos.DrawWireSphere(a, outerRadius); Gizmos.DrawWireSphere(b, outerRadius);
    }
}
