using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 換場景時的畫面：黑底上輪播幾張圖（村長家後院大門用四幅古畫），每張慢慢放大、交叉淡入淡出。
/// 蓋滿畫面之後才開始載入（startLoad），新場景載好、而且每張圖都輪過一次以後才淡出；
/// 新場景載好但還在播圖的這段時間先暫停遊戲，玩家不會在看不到的時候被打。
/// 跨場景保留（DontDestroyOnLoad），播完自己刪掉。
/// </summary>
public class PaintingSlideshow : MonoBehaviour
{
    const float FadeIn = 0.6f, PerSlide = 1.8f, CrossFade = 0.5f, FadeOut = 0.8f, LoadTimeout = 4f;

    Texture2D[] slides;
    Action startLoad, onAbort;
    float t, fadeOutStart = -1f;
    bool loadStarted, sceneChanged, froze;
    Scene startScene;

    public static bool Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Active = false;

    public static void Play(Texture2D[] slides, Action startLoad, Action onAbort = null)
    {
        var go = new GameObject("PaintingSlideshow");
        DontDestroyOnLoad(go);
        var s = go.AddComponent<PaintingSlideshow>();
        s.slides = slides != null && slides.Length > 0 ? slides : null;
        s.startLoad = startLoad;
        s.onAbort = onAbort;
        s.startScene = SceneManager.GetActiveScene();
        Active = true;
    }

    void OnEnable() => SceneManager.activeSceneChanged += OnSceneChanged;
    void OnDisable() => SceneManager.activeSceneChanged -= OnSceneChanged;

    void OnSceneChanged(Scene from, Scene to)
    {
        if (to != startScene) sceneChanged = true;
    }

    int Count => slides != null ? slides.Length : 0;

    void Update()
    {
        t += Time.unscaledDeltaTime;
        if (!loadStarted && t >= FadeIn)
        {
            loadStarted = true;
            startLoad?.Invoke();
        }

        var gm = GameManager.Instance;
        bool loading = gm != null && gm.IsLoading;
        if (fadeOutStart < 0f)
        {
            // 新場景載好了（GameManager 也淡入完成），但圖還沒輪完：先暫停
            if (sceneChanged && !loading && !froze)
            {
                froze = true;
                Time.timeScale = 0f;
            }
            bool shownAll = t >= FadeIn + PerSlide * Mathf.Max(1, Count);
            if (sceneChanged && !loading && shownAll) BeginFadeOut();
            // 一直沒有開始換場景：載入失敗，收起來
            else if (!sceneChanged && !loading && t > FadeIn + LoadTimeout)
            {
                onAbort?.Invoke();
                BeginFadeOut();
            }
        }
        else if (t - fadeOutStart >= FadeOut)
        {
            Active = false;
            Destroy(gameObject);
        }
    }

    void BeginFadeOut()
    {
        fadeOutStart = t;
        if (froze) Time.timeScale = 1f;
        froze = false;
    }

    void OnDestroy()
    {
        if (froze) Time.timeScale = 1f;
        Active = false;
    }

    void OnGUI()
    {
        GUI.depth = -1500; // 蓋在 GameManager 的黑幕（-1000）和所有畫面上面
        float alpha = fadeOutStart < 0f ? Mathf.Clamp01(t / FadeIn) : 1f - Mathf.Clamp01((t - fadeOutStart) / FadeOut);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

        if (Count > 0)
        {
            float s = Mathf.Max(0f, t - FadeIn * 0.5f);
            int index = Mathf.FloorToInt(s / PerSlide);
            float local = s - index * PerSlide;
            // 這張完整顯示；換下一張前的 CrossFade 秒和下一張交疊
            DrawSlide(index % Count, local, alpha * Mathf.Clamp01(local / CrossFade + (index == 0 ? 1f : 0f)));
            if (local > PerSlide - CrossFade)
                DrawSlide((index + 1) % Count, local - PerSlide, alpha * Mathf.Clamp01((local - (PerSlide - CrossFade)) / CrossFade));
        }
        GUI.color = prev;
    }

    void DrawSlide(int i, float local, float a)
    {
        var tex = slides[i];
        if (tex == null || a <= 0f) return;
        float aspect = tex.width / (float)Mathf.Max(1, tex.height);
        float maxW = Screen.width * 0.8f, maxH = Screen.height * 0.8f;
        float w = Mathf.Min(maxW, maxH * aspect), h = w / aspect;
        float zoom = 1f + 0.05f * Mathf.Clamp01((local + CrossFade) / (PerSlide + CrossFade)); // 慢慢放大一點點
        w *= zoom;
        h *= zoom;
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), tex, ScaleMode.StretchToFill, true);
    }
}
