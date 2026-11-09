using UnityEngine;

/// <summary>
/// 修羅場天色：百足駭骨進入三階段時，夜空在 transitionTime 秒內轉成暗紅色
/// （天空、月亮、雲、星星、霧、環境光、月光一起變）；戰鬥重來或 Boss 被擊敗時轉回夜晚。
///
/// 夜晚的數值在 Awake 時從目前的 Skybox 材質和 RenderSettings 讀取，所以場景原本的設定就是「夜晚」；
/// 這裡只存暗紅色那一組。執行時會複製一份 Skybox 材質來改，不會動到材質檔。
/// </summary>
public class ShuraSkyPhase : MonoBehaviour
{
    public SkeletonCentipedeBossAI boss;
    [Tooltip("月光（方向光）")]
    public Light moonLight;
    public float transitionTime = 3f;

    [Header("暗紅天色")]
    public Color zenith = new Color(0.03f, 0.003f, 0.005f);
    public Color horizon = new Color(0.3f, 0.03f, 0.025f);
    public Color below = new Color(0.04f, 0.008f, 0.008f);
    [ColorUsage(false, true)] public Color moon = new Color(2.4f, 0.35f, 0.2f);
    [ColorUsage(false, true)] public Color halo = new Color(0.9f, 0.1f, 0.05f);
    public float haloIntensity = 0.9f;
    public Color cloud = new Color(0.05f, 0.008f, 0.008f);
    [ColorUsage(false, true)] public Color stars = new Color(0.5f, 0.25f, 0.25f);
    public Color fog = new Color(0.12f, 0.02f, 0.02f);
    public Color ambientSky = new Color(0.28f, 0.06f, 0.05f);
    public Color ambientEquator = new Color(0.15f, 0.03f, 0.03f);
    public Color ambientGround = new Color(0.04f, 0.01f, 0.01f);
    public Color moonLightColor = new Color(1f, 0.35f, 0.28f);
    public float moonLightIntensity = 0.6f;

    static readonly int ZenithId = Shader.PropertyToID("_ZenithColor");
    static readonly int HorizonId = Shader.PropertyToID("_HorizonColor");
    static readonly int GroundId = Shader.PropertyToID("_GroundColor");
    static readonly int MoonId = Shader.PropertyToID("_MoonColor");
    static readonly int HaloId = Shader.PropertyToID("_HaloColor");
    static readonly int HaloIntensityId = Shader.PropertyToID("_HaloIntensity");
    static readonly int CloudId = Shader.PropertyToID("_CloudColor");
    static readonly int StarId = Shader.PropertyToID("_StarColor");

    struct Palette
    {
        public Color zenith, horizon, below, moon, halo, cloud, stars, fog, ambSky, ambEq, ambGround, light;
        public float haloIntensity, lightIntensity;
    }

    Material sky;
    Palette night, blood;
    float blend, targetBlend;
    float nextGiUpdate;

    void Awake()
    {
        if (RenderSettings.skybox != null)
        {
            sky = new Material(RenderSettings.skybox) { name = RenderSettings.skybox.name + " (Runtime)" };
            RenderSettings.skybox = sky;
        }
        night = Capture();
        blood = new Palette
        {
            zenith = zenith, horizon = horizon, below = below, moon = moon, halo = halo, cloud = cloud, stars = stars,
            fog = fog, ambSky = ambientSky, ambEq = ambientEquator, ambGround = ambientGround,
            light = moonLightColor, haloIntensity = haloIntensity, lightIntensity = moonLightIntensity,
        };
    }

    void OnEnable()
    {
        if (boss == null) return;
        boss.OnPhase3Started += ToBlood;
        boss.OnFightReset += ToNight;
        boss.OnDefeated += ToNight;
    }

    void OnDisable()
    {
        if (boss == null) return;
        boss.OnPhase3Started -= ToBlood;
        boss.OnFightReset -= ToNight;
        boss.OnDefeated -= ToNight;
    }

    void OnDestroy()
    {
        if (sky != null) Destroy(sky);
    }

    public void ToBlood() => targetBlend = 1f;
    public void ToNight() => targetBlend = 0f;

    void Update()
    {
        if (Mathf.Approximately(blend, targetBlend)) return;
        blend = Mathf.MoveTowards(blend, targetBlend, Time.deltaTime / Mathf.Max(0.01f, transitionTime));
        Apply(Mathf.SmoothStep(0f, 1f, blend));
    }

    Palette Capture()
    {
        var p = new Palette
        {
            fog = RenderSettings.fogColor,
            ambSky = RenderSettings.ambientSkyColor,
            ambEq = RenderSettings.ambientEquatorColor,
            ambGround = RenderSettings.ambientGroundColor,
            light = moonLight != null ? moonLight.color : Color.white,
            lightIntensity = moonLight != null ? moonLight.intensity : 1f,
        };
        if (sky != null)
        {
            p.zenith = sky.GetColor(ZenithId);
            p.horizon = sky.GetColor(HorizonId);
            p.below = sky.GetColor(GroundId);
            p.moon = sky.GetColor(MoonId);
            p.halo = sky.GetColor(HaloId);
            p.haloIntensity = sky.GetFloat(HaloIntensityId);
            p.cloud = sky.GetColor(CloudId);
            p.stars = sky.GetColor(StarId);
        }
        return p;
    }

    void Apply(float t)
    {
        if (sky != null)
        {
            sky.SetColor(ZenithId, Color.Lerp(night.zenith, blood.zenith, t));
            sky.SetColor(HorizonId, Color.Lerp(night.horizon, blood.horizon, t));
            sky.SetColor(GroundId, Color.Lerp(night.below, blood.below, t));
            sky.SetColor(MoonId, Color.Lerp(night.moon, blood.moon, t));
            sky.SetColor(HaloId, Color.Lerp(night.halo, blood.halo, t));
            sky.SetFloat(HaloIntensityId, Mathf.Lerp(night.haloIntensity, blood.haloIntensity, t));
            sky.SetColor(CloudId, Color.Lerp(night.cloud, blood.cloud, t));
            sky.SetColor(StarId, Color.Lerp(night.stars, blood.stars, t));
        }
        RenderSettings.fogColor = Color.Lerp(night.fog, blood.fog, t);
        RenderSettings.ambientSkyColor = Color.Lerp(night.ambSky, blood.ambSky, t);
        RenderSettings.ambientEquatorColor = Color.Lerp(night.ambEq, blood.ambEq, t);
        RenderSettings.ambientGroundColor = Color.Lerp(night.ambGround, blood.ambGround, t);
        if (moonLight != null)
        {
            moonLight.color = Color.Lerp(night.light, blood.light, t);
            moonLight.intensity = Mathf.Lerp(night.lightIntensity, blood.lightIntensity, t);
        }

        // 環境光改顏色後要重新計算（不用每一幀都算）
        if (Time.unscaledTime >= nextGiUpdate || Mathf.Approximately(blend, targetBlend))
        {
            DynamicGI.UpdateEnvironment();
            nextGiUpdate = Time.unscaledTime + 0.1f;
        }
    }
}
