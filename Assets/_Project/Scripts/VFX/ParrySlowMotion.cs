using System.Collections;
using UnityEngine;

/// <summary>
/// 格擋成功的慢動作：時間變成 scale 倍（預設 0.8）維持 seconds 秒（真實時間），最後一小段平滑回到 1。
///   ParrySlowMotion.Play(0.8f, 0.7f);
/// 跟專案裡其他改時間的東西（頓幀、技能慢動作、暫停選單）共用 Time.timeScale 的規矩：
///   - 只有時間是 1 的時候才開始（先等格擋的頓幀 0.06 秒結束，最多等 0.25 秒）
///   - 中途被暫停（timeScale = 0）就跟著停，繼續遊戲後接著跑完，不會卡在 0.8
///   - 時間被別人改成其他數值（例如技能慢動作）就交給它，不再動
/// 自動建立一個 DontDestroyOnLoad 的物件來跑，不用在場景裡放東西。
/// </summary>
public static class ParrySlowMotion
{
    /// <summary>慢動作中（給特效 / 音效判斷用）</summary>
    public static bool Active { get; private set; }

    static Host host;

    /// <summary>End this transient clock before another system captures a restore value.</summary>
    public static void Cancel()
    {
        if (host != null) host.Cancel();
        else Active = false;
    }

    public static void Play(float scale = 0.8f, float seconds = 0.7f)
    {
        if (scale <= 0f || scale >= 1f || seconds <= 0f) return;
        if (host == null)
        {
            var go = new GameObject("ParrySlowMotion");
            Object.DontDestroyOnLoad(go);
            host = go.AddComponent<Host>();
        }
        host.Begin(scale, seconds);
    }

    sealed class Host : MonoBehaviour
    {
        Coroutine running;
        float current = -1f;   // 目前由這裡設定的 timeScale（-1 = 沒有）

        public void Begin(float scale, float seconds)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Run(scale, seconds));
        }

        IEnumerator Run(float scale, float seconds)
        {
            // 連續格擋：上一次的慢動作還在 → 直接延長
            bool continuing = current > 0f && Mathf.Approximately(Time.timeScale, current);
            if (!continuing)
            {
                current = -1f;
                // 先讓格擋的頓幀（ShieldParryVFX，0.06 秒真實時間）播完，再開始慢動作
                float hold = 0f;
                while (hold < ShieldParryVFX.HitStopSeconds + 0.01f) { hold += Time.unscaledDeltaTime; yield return null; }
                // 等頓幀結束（時間回到 1）
                float wait = 0f;
                while (!Mathf.Approximately(Time.timeScale, 1f) && wait < 0.25f)
                {
                    if (Time.timeScale > 0f) wait += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (!Mathf.Approximately(Time.timeScale, 1f)) { running = null; yield break; }
            }

            Active = true;
            Set(scale);
            float ease = Mathf.Min(0.2f, seconds * 0.35f);   // 最後這段慢慢回到 1
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                if (Time.timeScale <= 0f) continue;                        // 暫停中：不算時間
                if (!Mathf.Approximately(Time.timeScale, current)) break;   // 被別人改走了
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01((t - (seconds - ease)) / ease);
                Set(Mathf.Lerp(scale, 1f, k * k * (3f - 2f * k)));
            }
            if (current > 0f && Mathf.Approximately(Time.timeScale, current)) Time.timeScale = 1f;
            current = -1f;
            Active = false;
            running = null;
        }

        void Set(float scale)
        {
            Time.timeScale = scale;
            current = scale;
        }

        public void Cancel()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            if (current > 0f && Mathf.Approximately(Time.timeScale, current)) Time.timeScale = 1f;
            current = -1f;
            Active = false;
        }

        void OnDisable() => Cancel();
    }
}
