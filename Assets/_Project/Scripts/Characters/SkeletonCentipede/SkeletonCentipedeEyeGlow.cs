using UnityEngine;

/// <summary>
/// 百骨駭足的眼睛紅光（衝刺的前搖）：Flash(秒數) → 頭上兩顆眼睛瞬間亮起強烈紅光、照亮周圍，時間到熄掉。
/// 眼睛的位置跟著頭骨（名字結尾是 Head_0 的骨頭；找不到就用 Boss 前方）每幀更新，光暈永遠面向鏡頭。
/// 模型沒有眼睛骨頭，位置用「頭骨 + Boss 的前 / 上 / 左右偏移」估計：看起來沒對準就在 Inspector 調 eyeForward / eyeUp / eyeSpacing。
/// 由 SkeletonCentipedeBossAI 自動加上。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkeletonCentipedeEyeGlow : MonoBehaviour
{
    [Header("眼睛位置（相對頭骨，乘上 Boss 的縮放）")]
    public float eyeForward = 0.55f;
    public float eyeUp = 0.25f;
    [Tooltip("兩眼之間的距離的一半")]
    public float eyeSpacing = 0.32f;

    [Header("外觀")]
    public Color glowColor = new Color(1f, 0.08f, 0.04f, 1f);
    [Tooltip("光暈亮度（HDR 倍率，越大 Bloom 越強）")]
    public float glowIntensity = 6f;
    [Tooltip("每顆眼睛光暈的大小")]
    public float glowSize = 1.3f;
    [Tooltip("點光源最亮時的強度 / 範圍")]
    public float lightIntensity = 22f, lightRange = 10f;

    Transform head;
    Transform[] eyes;
    MeshRenderer[] eyeRenderers;
    Light glowLight;
    MaterialPropertyBlock mpb;
    float flashStart = -10f, flashDuration;
    static Material glowMat;
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public bool Flashing => Time.time - flashStart < flashDuration;

    void Awake()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name.EndsWith("Head_0")) { head = t; break; }

        if (glowMat == null) glowMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);
        mpb = new MaterialPropertyBlock();
        eyes = new Transform[2];
        eyeRenderers = new MeshRenderer[2];
        for (int i = 0; i < 2; i++)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.name = i == 0 ? "EyeGlow_L" : "EyeGlow_R";
            q.transform.SetParent(transform, false);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = glowMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.enabled = false;
            eyes[i] = q.transform;
            eyeRenderers[i] = r;
        }
        var lg = new GameObject("EyeGlowLight");
        lg.transform.SetParent(transform, false);
        glowLight = lg.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = glowColor;
        glowLight.shadows = LightShadows.None;
        glowLight.intensity = 0f;
        glowLight.enabled = false;
    }

    /// <summary>眼睛亮起強烈紅光 seconds 秒</summary>
    public void Flash(float seconds)
    {
        flashStart = Time.time;
        flashDuration = Mathf.Max(0.05f, seconds);
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.8f, 0.55f, 0.05f, 1f, 45f);
    }

    void LateUpdate()
    {
        float t = Time.time - flashStart;
        bool on = t < flashDuration + 0.08f;
        for (int i = 0; i < 2; i++) eyeRenderers[i].enabled = on;
        glowLight.enabled = on;
        if (!on) return;

        // 亮度：瞬間亮起（前 25%），撐住，最後一點點時間快速熄掉
        float k = t / flashDuration;
        float a = k < 0.25f ? k / 0.25f : k <= 1f ? 1f : 1f - (t - flashDuration) / 0.08f;
        a = Mathf.Clamp01(a);
        float flicker = 1f + 0.12f * Mathf.Sin(Time.time * 70f);

        float s = transform.lossyScale.x;
        Vector3 basePos = (head != null ? head.position : transform.position + Vector3.up * 2f * s)
                          + transform.forward * eyeForward * s + Vector3.up * eyeUp * s;
        var cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        Color c = glowColor * (glowIntensity * a * flicker);
        c.a = a;
        mpb.SetColor(BaseColor, c);
        for (int i = 0; i < 2; i++)
        {
            eyes[i].position = basePos + transform.right * (i == 0 ? -eyeSpacing : eyeSpacing) * s;
            eyes[i].rotation = face;
            eyes[i].localScale = Vector3.one * glowSize * (0.7f + 0.3f * a);
            eyeRenderers[i].SetPropertyBlock(mpb);
        }
        glowLight.transform.position = basePos + transform.forward * 0.4f * s;
        glowLight.range = lightRange * s;
        glowLight.intensity = lightIntensity * a * flicker;
    }
}
