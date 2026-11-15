using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 打擊的空氣波動：在命中點擴散一圈透明、會扭曲背後畫面的衝擊波（shader：Resources/ShockwaveDistortion）。
/// 用真實時間播放，所以頓幀（hitstop）時畫面停住、波動照樣往外擴，打擊會更有力。
/// ShockwaveFx.Spawn(位置, 半徑公尺, 持續秒數, 強度)；揮刀時 PlayerVisualAnimator 會沿著刀尖的軌跡呼叫（打沒打到都有）。
/// 渲染設定沒開 Opaque Texture（例如手機版設定）時不會產生，避免畫面出現灰色圓圈。
/// </summary>
public sealed class ShockwaveFx : MonoBehaviour
{
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int StrengthId = Shader.PropertyToID("_Strength");
    static readonly int FadeId = Shader.PropertyToID("_Fade");
    static readonly int WidthId = Shader.PropertyToID("_Width");

    static Material material;
    static Mesh quad;
    static bool? supported;
    static readonly Stack<ShockwaveFx> pool = new Stack<ShockwaveFx>();

    // 專案開了「進入 Play 不重新載入」（Enter Play Mode Options）：上一次 Play 建的材質、物件停止時都被刪了，
    // 但 static 變數還留著，下一次 Play 就會拿到已刪除的材質（畫面上變成粉紅色方塊）。每次 Play 開始時全部清掉重來。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        material = null;
        quad = null;
        supported = null;
        pool.Clear();
    }

    MeshRenderer meshRenderer;
    MaterialPropertyBlock block;
    float startTime, duration, strength, radius, delay;

    public static void Spawn(Vector3 position, float radius, float duration = .3f, float strength = .25f, float delay = 0f)
    {
        if (!Supported() || radius <= 0f || duration <= 0f) return;
        ShockwaveFx fx = null;
        while (fx == null && pool.Count > 0) fx = pool.Pop(); // 被刪掉的（換場景）跳過
        if (fx == null) fx = Create();
        if (fx == null) return;
        fx.transform.position = position;
        fx.radius = radius;
        fx.duration = duration;
        fx.strength = strength;
        fx.delay = delay;
        fx.startTime = Time.unscaledTime;
        fx.transform.localScale = Vector3.one * (radius * 2f);
        fx.gameObject.SetActive(true);
        fx.meshRenderer.enabled = delay <= 0f;
        fx.Apply(0f);
        fx.Face();
    }

    static bool Supported()
    {
        if (supported == true && material == null) supported = null; // 材質被刪了：重新建
        if (supported == null)
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            var shader = Shader.Find("GeminiGrowUp/ShockwaveDistortion");
            if (shader == null) shader = Resources.Load<Shader>("ShockwaveDistortion");
            supported = urp != null && urp.supportsCameraOpaqueTexture && shader != null && shader.isSupported;
            if (supported.Value) material = new Material(shader) { name = "Shockwave (runtime)" };
            else Debug.Log("[ShockwaveFx] 目前的渲染設定沒有開 Opaque Texture（或找不到 shader），空氣波動關閉。");
        }
        return supported.Value;
    }

    static ShockwaveFx Create()
    {
        if (quad == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        var go = new GameObject("Shockwave");
        DontDestroyOnLoad(go);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var fx = go.AddComponent<ShockwaveFx>();
        fx.meshRenderer = mr;
        fx.block = new MaterialPropertyBlock();
        return fx;
    }

    void LateUpdate()
    {
        float t = (Time.unscaledTime - startTime - delay) / duration;
        if (t < 0f) { meshRenderer.enabled = false; return; }
        meshRenderer.enabled = true;
        if (t >= 1f) { gameObject.SetActive(false); pool.Push(this); return; }
        Apply(t);
        Face();
    }

    void Apply(float t)
    {
        // 很快擴出去、越來越慢；越往外越淡、環也越寬
        float grow = 1f - Mathf.Pow(1f - t, 3f);
        block.SetFloat(ProgressId, Mathf.Lerp(.08f, .9f, grow));
        block.SetFloat(WidthId, Mathf.Lerp(.12f, .3f, grow));
        block.SetFloat(StrengthId, strength);
        block.SetFloat(FadeId, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / .08f)) * (1f - Mathf.SmoothStep(0f, 1f, t)));
        meshRenderer.SetPropertyBlock(block);
    }

    void Face()
    {
        var cam = Camera.main;
        if (cam == null) return;
        // 面向鏡頭（shader 用 ZTest Always：不會被打中的敵人身體切掉一半）
        transform.rotation = cam.transform.rotation;
    }
}
