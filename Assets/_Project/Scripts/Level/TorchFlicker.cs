using UnityEngine;

/// <summary>
/// 火把：燈光亮度和火焰大小會隨機忽明忽暗。
/// 放在火把物件上，會自動找子物件裡的 Light 和名為 "Flame" 的火焰。
/// </summary>
public class TorchFlicker : MonoBehaviour
{
    [Tooltip("閃爍的幅度（0 = 不閃）")]
    [Range(0f, 1f)] public float amount = 0.3f;
    [Tooltip("閃爍的速度")]
    public float speed = 6f;

    /// <summary>整體亮度倍率（0 = 完全熄滅）。火把被打掉、慢慢熄滅時由 KnockableTorch 控制</summary>
    [HideInInspector] public float dim = 1f;

    Light torchLight;
    Transform flame;
    float baseIntensity;
    Vector3 baseFlameScale;
    float seed;

    void Awake()
    {
        torchLight = GetComponentInChildren<Light>();
        if (torchLight != null) baseIntensity = torchLight.intensity;
        flame = transform.Find("Flame");
        if (flame != null) baseFlameScale = flame.localScale;
        seed = Random.value * 100f;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(seed, Time.time * speed);   // 0~1
        float k = (1f - amount + amount * 2f * n) * dim;         // 約 (1 - amount) ~ (1 + amount)
        if (torchLight != null) torchLight.intensity = baseIntensity * k;
        if (flame != null)
        {
            float stretch = Mathf.PerlinNoise(seed + 7f, Time.time * speed * 1.3f);
            flame.localScale = new Vector3(baseFlameScale.x * k, baseFlameScale.y * (0.8f + 0.4f * stretch) * dim, baseFlameScale.z * k);
        }
    }
}
