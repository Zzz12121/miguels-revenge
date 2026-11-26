using System.Collections;
using UnityEngine;

/// <summary>
/// 盾反（用盾牌把攻擊彈開）的視覺回饋：
///   1. 空氣震盪：兩圈會扭曲背後畫面的衝擊波（ShockwaveFx，一大一小錯開）
///   2. 黃橘色閃光：盾牌前面一團瞬間炸亮又收掉的光（加亮粒子）＋一盞橘色點光源照亮周圍
///   3. 火花：往攻擊來的方向噴出的黃橘色細長火花
///   4. 頓幀 0.06 秒（真實時間）＋鏡頭震動，讓「彈開」有重量
/// ShieldParryVFX.Spawn(盾牌上的位置, 攻擊來的方向)。全部用程式產生，播完自己刪掉。
/// </summary>
public static class ShieldParryVFX
{
    public static Color FlashCore = new Color(1f, 0.92f, 0.55f, 1f);   // 中心偏黃白
    public static Color FlashEdge = new Color(1f, 0.55f, 0.12f, 1f);   // 外圈橘
    public static float HitStopSeconds = 0.06f;

    static Material additive;
    static Material Additive
    {
        get
        {
            if (additive == null) additive = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), true);
            return additive;
        }
    }

    public static void Spawn(Vector3 point, Vector3 attackFrom)
    {
        if (attackFrom.sqrMagnitude < 0.0001f) attackFrom = Vector3.forward;
        attackFrom.Normalize();

        // 1. 空氣震盪：大圈 + 稍後的小圈
        ShockwaveFx.Spawn(point, 2.8f, 0.38f, 0.38f);
        ShockwaveFx.Spawn(point, 1.5f, 0.26f, 0.3f, 0.05f);

        var root = new GameObject("VFX_ShieldParry");
        root.transform.position = point;
        Flash(root.transform);
        Sparks(root.transform, attackFrom);
        var host = root.AddComponent<ParryVFXHost>();
        host.Begin(point);

        ThirdPersonCamera.ShakeMain(0.22f, 0.16f);
        Object.Destroy(root, 1.2f);
    }

    static ParticleSystem NewSystem(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false; main.playOnAwake = false; main.duration = 0.3f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;   // 頓幀時也照樣播
        var em = ps.emission; em.rateOverTime = 0f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Additive;
        return ps;
    }

    /// <summary>黃橘色閃光：一顆大的黃白核心 + 幾顆橘色光暈，瞬間放大後收掉</summary>
    static void Flash(Transform parent)
    {
        var ps = NewSystem("Flash", parent);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.16f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
        main.startColor = new ParticleSystem.MinMaxGradient(FlashCore, FlashEdge);
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 4) });
        var shape = ps.shape; shape.enabled = false;
        var size = ps.sizeOverLifetime; size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.25f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(FlashCore, 0f), new GradientColorKey(FlashEdge, 0.5f), new GradientColorKey(FlashEdge, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        ps.Play();
    }

    /// <summary>往攻擊來的方向（敵人那一側）噴出的細長火花</summary>
    static void Sparks(Transform parent, Vector3 attackFrom)
    {
        var ps = NewSystem("Sparks", parent);
        ps.transform.rotation = Quaternion.LookRotation(attackFrom);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.38f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f, 13f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(FlashCore, FlashEdge);
        main.gravityModifier = 0.9f;
        var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
        var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 55f; shape.radius = 0.2f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(FlashCore, 0f), new GradientColorKey(FlashEdge, 0.6f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f; r.lengthScale = 1.5f;
        ps.Play();
    }

    /// <summary>點光源閃一下 + 頓幀（用真實時間跑）</summary>
    sealed class ParryVFXHost : MonoBehaviour
    {
        public void Begin(Vector3 point) => StartCoroutine(Run(point));

        IEnumerator Run(Vector3 point)
        {
            var lightGo = new GameObject("FlashLight");
            lightGo.transform.SetParent(transform, false);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point; l.color = FlashEdge; l.range = 6f; l.intensity = 0f; l.shadows = LightShadows.None;

            // 頓幀：只在正常速度時做，結束時只還原我們自己設的值（不干擾對話暫停等其他系統）
            CombatHitStop.Request(HitStopSeconds, .05f, .08f);

            const float flashTime = 0.2f;
            for (float t = 0f; t < flashTime; t += Time.unscaledDeltaTime)
            {
                float k = t / flashTime;
                l.intensity = 9f * (k < 0.15f ? k / 0.15f : 1f - SmoothTween.Evaluate(SmoothTween.Ease.OutCubic, (k - 0.15f) / 0.85f));
                yield return null;
            }
            l.intensity = 0f;
        }

        void OnDestroy()
        {
            // 物件提早被刪掉（換場景）：別把遊戲留在慢動作
            if (Mathf.Approximately(Time.timeScale, 0.05f)) Time.timeScale = 1f;
        }
    }
}
