using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if DOTWEEN
using DG.Tweening;
#endif

/// <summary>
/// 平滑補間的小工具（移動、轉向、受擊彈一下）。
/// 專案有裝 DOTween、Player Settings 的 Scripting Define Symbols 有 DOTWEEN 時用 DOTween；
/// 沒有的話用內建的協程補間，效果一樣，只是少了 DOTween 的效能與更多 Ease 曲線。
/// 呼叫端不用管底層是哪一個。
/// </summary>
public static class SmoothTween
{
    public enum Ease { Linear, OutCubic, InOutSine, OutBack, InCubic }

    /// <summary>受擊時模型彈一下（縮放），結束後回到原本大小</summary>
    public static void PunchScale(Transform t, float strength, float duration)
    {
        if (t == null) return;
#if DOTWEEN
        t.DOComplete();
        t.DOPunchScale(Vector3.one * strength, duration, 6, 0.6f);
#else
        Host.Run(t, "scale", PunchScaleRoutine(t, strength, duration));
#endif
    }

    /// <summary>移動到世界座標 target</summary>
    public static void Move(Transform t, Vector3 target, float duration, Ease ease = Ease.InOutSine, Action onComplete = null)
    {
        if (t == null) return;
#if DOTWEEN
        DOTween.Kill(MoveId(t));
        t.DOMove(target, duration).SetEase(ToDOTween(ease)).SetId(MoveId(t)).OnComplete(() => onComplete?.Invoke());
#else
        Host.Run(t, "move", MoveRoutine(t, target, duration, ease, onComplete));
#endif
    }

    /// <summary>轉向到 target</summary>
    public static void Rotate(Transform t, Quaternion target, float duration, Ease ease = Ease.OutCubic)
    {
        if (t == null) return;
#if DOTWEEN
        DOTween.Kill(RotId(t));
        t.DORotateQuaternion(target, duration).SetEase(ToDOTween(ease)).SetId(RotId(t));
#else
        Host.Run(t, "rot", RotateRoutine(t, target, duration, ease));
#endif
    }

    /// <summary>停掉這個物件所有的補間（例如死亡時）</summary>
    public static void Kill(Transform t)
    {
        if (t == null) return;
#if DOTWEEN
        DOTween.Kill(MoveId(t)); DOTween.Kill(RotId(t)); t.DOKill();
#else
        Host.StopAll(t);
#endif
    }

    public static float Evaluate(Ease ease, float x)
    {
        x = Mathf.Clamp01(x);
        switch (ease)
        {
            case Ease.OutCubic: return 1f - Mathf.Pow(1f - x, 3f);
            case Ease.InCubic: return x * x * x;
            case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * x) - 1f) * 0.5f;
            case Ease.OutBack: { const float c1 = 1.70158f, c3 = c1 + 1f; return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f); }
            default: return x;
        }
    }

#if DOTWEEN
    static string MoveId(Transform t) => "st_move_" + t.GetHashCode();
    static string RotId(Transform t) => "st_rot_" + t.GetHashCode();
    static DG.Tweening.Ease ToDOTween(Ease e)
    {
        switch (e)
        {
            case Ease.OutCubic: return DG.Tweening.Ease.OutCubic;
            case Ease.InCubic: return DG.Tweening.Ease.InCubic;
            case Ease.InOutSine: return DG.Tweening.Ease.InOutSine;
            case Ease.OutBack: return DG.Tweening.Ease.OutBack;
            default: return DG.Tweening.Ease.Linear;
        }
    }
#else
    static IEnumerator PunchScaleRoutine(Transform t, float strength, float duration)
    {
        Vector3 baseScale = Host.BaseScale(t);
        for (float e = 0f; e < duration && t != null; e += Time.deltaTime)
        {
            float k = e / duration;
            float wave = Mathf.Sin(k * Mathf.PI * 3f) * (1f - k);   // 彈三下、越來越小
            t.localScale = baseScale * (1f + strength * wave);
            yield return null;
        }
        if (t != null) t.localScale = baseScale;
        Host.ClearBaseScale(t);
    }

    static IEnumerator MoveRoutine(Transform t, Vector3 target, float duration, Ease ease, Action onComplete)
    {
        Vector3 from = t.position;
        for (float e = 0f; e < duration && t != null; e += Time.deltaTime)
        {
            t.position = Vector3.LerpUnclamped(from, target, Evaluate(ease, e / duration));
            yield return null;
        }
        if (t != null) t.position = target;
        onComplete?.Invoke();
    }

    static IEnumerator RotateRoutine(Transform t, Quaternion target, float duration, Ease ease)
    {
        Quaternion from = t.rotation;
        for (float e = 0f; e < duration && t != null; e += Time.deltaTime)
        {
            t.rotation = Quaternion.SlerpUnclamped(from, target, Evaluate(ease, e / duration));
            yield return null;
        }
        if (t != null) t.rotation = target;
    }

    /// <summary>沒有 DOTween 時跑協程用的隱形物件</summary>
    class Host : MonoBehaviour
    {
        static Host instance;
        readonly Dictionary<(Transform, string), Coroutine> running = new Dictionary<(Transform, string), Coroutine>();
        readonly Dictionary<Transform, Vector3> baseScales = new Dictionary<Transform, Vector3>();

        static Host I
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[SmoothTween]") { hideFlags = HideFlags.HideAndDontSave };
                    if (Application.isPlaying) DontDestroyOnLoad(go);
                    instance = go.AddComponent<Host>();
                }
                return instance;
            }
        }

        public static void Run(Transform t, string channel, IEnumerator routine)
        {
            var key = (t, channel);
            if (I.running.TryGetValue(key, out var c) && c != null) I.StopCoroutine(c);
            I.running[key] = I.StartCoroutine(routine);
        }

        public static void StopAll(Transform t)
        {
            if (instance == null) return;
            var id = t;
            foreach (var ch in new[] { "scale", "move", "rot" })
                if (instance.running.TryGetValue((id, ch), out var c) && c != null) instance.StopCoroutine(c);
            if (instance.baseScales.TryGetValue(id, out var s)) { t.localScale = s; instance.baseScales.Remove(id); }
        }

        // 連續被打時記住「原本」的大小，避免越彈越大或越小
        public static Vector3 BaseScale(Transform t)
        {
            var id = t;
            if (!I.baseScales.TryGetValue(id, out var s)) { s = t.localScale; I.baseScales[id] = s; }
            return s;
        }

        public static void ClearBaseScale(Transform t) { if (instance != null && t != null) instance.baseScales.Remove(t); }
    }
#endif
}
