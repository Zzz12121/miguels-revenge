using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 主畫面按「開始遊戲」之後、真正進到漁村之前的載入畫面：主畫面的舊宣紙底，下方一長條破布當進度條，進度的部分染成朱紅
/// （跟選項、暫停選單同一套風格，MenuSkin；圖載不到時是一片墨黑加一條細的朱紅進度條）。
/// 進度條偶爾會倒退一截（像載到一半又重來一點），停一下再繼續往前；跑到 100% 才切換到漁村。
///
/// 用法：MainMenu 在呼叫 GameManager.StartSlot 之前呼叫 StoryLoadingScreen.Begin(gm, 要去的場景)（新遊戲是漁村，讀檔是存檔那一關）。
/// 它透過 GameManager.HoldSceneActivation 讓漁村先在背景載好，等進度條跑完才切換；
/// 顯示的進度不會超過實際的載入進度（GameManager.LoadProgress），所以載得慢時也不會先跑到底再卡住。
/// 進到漁村之後跟著 GameManager 的淡入一起淡掉，然後自己刪掉（DontDestroyOnLoad，換場景時不會被刪）。
/// </summary>
public sealed class StoryLoadingScreen : MonoBehaviour
{
    const float MinTime = 2.6f;          // 沒有倒退的話，至少跑這麼久
    const float FadeIn = 0.3f, FadeOut = 0.6f;
    const float RetreatTime = 0.35f;     // 倒退那一下多快

    static readonly Color Back = new Color(0.035f, 0.03f, 0.028f);
    static readonly Color Fill = new Color(0.78f, 0.17f, 0.09f);
    static readonly Color Track = new Color(0.85f, 0.8f, 0.7f, 0.14f);

    struct Setback { public float at, amount; }

    GameManager gm;
    string targetScene;
    readonly List<Setback> setbacks = new List<Setback>();
    float startedAt, shown, ghost, ghostAlpha, flash;
    float retreatFrom, retreatTo, retreatT = -1f, stallUntil;
    float readyAt = -1f, leaveAt = -1f, retreated;
    float seed;
    GUIStyle label, percent;

    public static StoryLoadingScreen Begin(GameManager gm, string target = null)
    {
        if (gm == null || gm.IsLoading) return null;
        var go = new GameObject("StoryLoadingScreen");
        DontDestroyOnLoad(go);
        var s = go.AddComponent<StoryLoadingScreen>();
        s.gm = gm;
        s.targetScene = string.IsNullOrEmpty(target) ? gm.StoryStartScene : target;
        gm.HoldSceneActivation = s.Ready;
        return s;
    }

    void Awake()
    {
        startedAt = Time.unscaledTime;
        seed = Random.value * 100f;
        // 倒退：大多 1 次、有時 2 次、偶爾 3 次；每次退 6%~18%，偶爾在快滿的時候退一大截
        int n = Random.value < 0.5f ? 1 : Random.value < 0.8f ? 2 : 3;
        float lastAt = 0.1f;
        for (int i = 0; i < n; i++)
        {
            float at = Mathf.Max(lastAt + 0.15f, Random.Range(0.25f + i * 0.22f, 0.5f + i * 0.22f));
            if (at > 0.95f) break;
            float amount = Random.Range(0.06f, 0.18f);
            if (at > 0.82f && Random.value < 0.5f) amount = Random.Range(0.2f, 0.3f); // 差一點就好了，又退回去
            setbacks.Add(new Setback { at = at, amount = amount });
            lastAt = at;
        }
    }

    /// <summary>給 GameManager.HoldSceneActivation：進度條跑到底、停一下，就可以切換到漁村</summary>
    bool Ready() => this == null || (readyAt >= 0f && Time.unscaledTime - readyAt > 0.25f);

    void Update()
    {
        float now = Time.unscaledTime, dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f), e = now - startedAt;
        // 實際的載入進度：還在主畫面時看 GameManager；已經切到漁村了就是 100%
        bool arrived = SceneManager.GetActiveScene().name == targetScene;
        float real = arrived ? 1f : gm != null && gm.IsLoading ? gm.LoadProgress : 0f;

        if (retreatT >= 0f)
        {
            // 倒退中：很快退回一截，然後停一下
            retreatT += dt / RetreatTime;
            float k = Mathf.Clamp01(retreatT);
            shown = Mathf.Lerp(retreatFrom, retreatTo, 1f - (1f - k) * (1f - k));
            if (k >= 1f)
            {
                retreatT = -1f;
                stallUntil = now + Random.Range(0.35f, 0.7f);
            }
        }
        else if (now >= stallUntil)
        {
            // 往前：快慢不一（一陣快一陣慢），不超過時間和實際載入進度允許的
            // 時間上限要把倒退掉的補回來，不然倒退之後會被時間卡住
            float allowed = Mathf.Min(e / MinTime + retreated, 0.12f + 0.88f * real);
            if (real < 1f) allowed = Mathf.Min(allowed, 0.95f);
            float speed = 0.42f * (0.3f + 1.4f * Mathf.PerlinNoise(seed, e * 1.3f));
            shown = Mathf.MoveTowards(shown, Mathf.Min(1f, allowed), speed * dt);
            if (setbacks.Count > 0 && shown >= setbacks[0].at)
            {
                retreatFrom = shown;
                retreatTo = Mathf.Max(0.02f, shown - setbacks[0].amount);
                retreated += retreatFrom - retreatTo;
                ghost = shown;
                ghostAlpha = 1f;
                flash = 1f;
                retreatT = 0f;
                setbacks.RemoveAt(0);
                GameAudio.Play("SFX/RPG/cloth2", Vector3.zero, 0.35f, 0.7f, 0f, 0f);
            }
        }
        ghostAlpha = Mathf.MoveTowards(ghostAlpha, 0f, dt * 1.2f);
        flash = Mathf.MoveTowards(flash, 0f, dt * 2.5f);

        if (readyAt < 0f && shown >= 0.999f && setbacks.Count == 0 && retreatT < 0f) readyAt = now;
        // 進到漁村、新場景開始淡入：一起淡掉
        if (arrived && leaveAt < 0f) leaveAt = now;
        if (leaveAt >= 0f && now - leaveAt > FadeOut) Destroy(gameObject);
        // 保險：GameManager 不見了、或沒有在載入（開始遊戲被擋下來）
        if (gm == null || (!gm.IsLoading && !arrived && e > 1.5f))
        {
            if (gm != null && gm.HoldSceneActivation == (System.Func<bool>)Ready) gm.HoldSceneActivation = null;
            Destroy(gameObject);
        }
    }

    void OnGUI()
    {
        float now = Time.unscaledTime;
        float a = Mathf.Clamp01((now - startedAt) / FadeIn);
        if (leaveAt >= 0f) a *= 1f - Mathf.Clamp01((now - leaveAt) / FadeOut);
        if (a <= 0f) return;
        GUI.depth = -1002; // 蓋在 GameManager 的黑幕（-1000）和關卡轉場（-1001）上面
        float w = Screen.width, h = Screen.height, u = h / 1080f;
        var cloth = MenuSkin.LoadingCloth;
        var paper = MenuSkin.Paper;
        if (cloth != null && paper != null) DrawCloth(a, now, w, h, u, cloth, paper);
        else DrawPlain(a, now, w, h, u);
        GUI.color = Color.white;
    }

    /// <summary>
    /// 主畫面風格：舊宣紙底，下方一長條破布當進度條，進度的部分染成朱紅（前端的紅暈開一點）；
    /// 倒退時退掉的那一截留下淡淡的紅（染過又褪掉），字是墨色的標楷體
    /// </summary>
    void DrawCloth(float a, float now, float w, float h, float u, Texture2D cloth, Texture2D paper)
    {
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), paper, ScaleMode.ScaleAndCrop);

        float barW = Mathf.Min(1000f * u, w * 0.7f), barH = barW * cloth.height / cloth.width;
        var bar = new Rect((w - barW) * 0.5f, h - 150f * u, barW, barH);
        GUI.color = new Color(1f, 1f, 1f, a);
        GUI.DrawTexture(bar, cloth);
        Color dye = new Color(0.8f, 0.2f, 0.12f);
        if (ghostAlpha > 0f && ghost > shown)
        {
            GUI.color = new Color(dye.r, dye.g, dye.b, 0.45f * ghostAlpha * a);
            float x0 = Mathf.Clamp01(shown), x1 = Mathf.Clamp01(ghost);
            GUI.DrawTextureWithTexCoords(new Rect(bar.x + bar.width * x0, bar.y, bar.width * (x1 - x0), bar.height), cloth, new Rect(x0, 0f, x1 - x0, 1f));
        }
        float p = Mathf.Clamp01(shown);
        GUI.color = new Color(Mathf.Lerp(dye.r, 1f, flash * 0.25f), Mathf.Lerp(dye.g, 0.5f, flash * 0.25f), dye.b, a);
        GUI.DrawTextureWithTexCoords(new Rect(bar.x, bar.y, bar.width * p, bar.height), cloth, new Rect(0f, 0f, p, 1f));
        // 染料往前暈開一點（前端一團淡紅）
        if (p > 0.005f)
        {
            float bleed = barH * 1.3f;
            GUI.color = new Color(dye.r, dye.g, dye.b, 0.35f * a);
            GUI.DrawTexture(new Rect(bar.x + bar.width * p - bleed * 0.5f, bar.y + barH * 0.5f - bleed * 0.5f, bleed, bleed), DotTexture());
        }

        label ??= new GUIStyle(GameUI.Small) { alignment = TextAnchor.LowerLeft };
        percent ??= new GUIStyle(GameUI.SmallRight) { alignment = TextAnchor.LowerRight };
        label.font = percent.font = MenuSkin.Font;
        label.fontSize = percent.fontSize = Mathf.RoundToInt(30f * u);
        int dots = Mathf.FloorToInt(now * 2.5f) % 4;
        var textRect = new Rect(bar.x + 8f * u, bar.y - 50f * u, bar.width - 16f * u, 44f * u);
        Color ink = MenuSkin.Ink;
        InkText(textRect, "載入中" + new string('．', dots), label, new Color(ink.r, ink.g, ink.b, 0.9f * a), u);
        Color pc = Color.Lerp(ink, MenuSkin.Cinnabar, flash);
        InkText(textRect, Mathf.FloorToInt(p * 100f) + "%", percent, new Color(pc.r, pc.g, pc.b, 0.9f * a), u);
    }

    /// <summary>墨色的字（底下一層淡淡的紙色，像墨滲進紙裡）</summary>
    static void InkText(Rect r, string text, GUIStyle style, Color c, float u)
    {
        style.normal.textColor = new Color(1f, 0.95f, 0.85f, 0.35f * c.a);
        GUI.Label(new Rect(r.x + 1.5f * u, r.y + 1.5f * u, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }

    static Texture2D dot;
    /// <summary>中間實、邊緣淡掉的圓（染料暈開）</summary>
    static Texture2D DotTexture()
    {
        if (dot != null) return dot;
        const int S = 64;
        dot = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = new Vector2(x - S * 0.5f + 0.5f, y - S * 0.5f + 0.5f).magnitude / (S * 0.5f);
                float k = Mathf.Clamp01(1f - d);
                px[y * S + x] = new Color32(255, 255, 255, (byte)(k * k * 255));
            }
        dot.SetPixels32(px);
        dot.Apply();
        return dot;
    }

    /// <summary>圖載不到時：一片墨黑，下方一條細細的朱紅進度條</summary>
    void DrawPlain(float a, float now, float w, float h, float u)
    {
        GUI.color = new Color(Back.r, Back.g, Back.b, a);
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

        float barW = Mathf.Min(760f * u, w * 0.6f), barH = Mathf.Max(3f, 7f * u);
        var bar = new Rect((w - barW) * 0.5f, h - 130f * u, barW, barH);
        GUI.color = new Color(Track.r, Track.g, Track.b, Track.a * a);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        // 剛倒退掉的那一截：淡淡的殘影，看得出是退回去
        if (ghostAlpha > 0f && ghost > shown)
        {
            GUI.color = new Color(Fill.r, Fill.g, Fill.b, 0.4f * ghostAlpha * a);
            GUI.DrawTexture(new Rect(bar.x + bar.width * shown, bar.y, bar.width * (ghost - shown), bar.height), Texture2D.whiteTexture);
        }
        var fill = new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(shown), bar.height);
        GUI.color = new Color(Mathf.Lerp(Fill.r, 1f, flash * 0.3f), Mathf.Lerp(Fill.g, 0.9f, flash * 0.3f), Fill.b, a);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        // 前端一點亮光
        float glowW = 18f * u;
        GUI.color = new Color(1f, 0.55f, 0.35f, 0.5f * a);
        GUI.DrawTexture(new Rect(fill.xMax - glowW * 0.5f, bar.y - barH * 0.6f, glowW, barH * 2.2f), Texture2D.whiteTexture);

        label ??= new GUIStyle(GameUI.Small) { alignment = TextAnchor.LowerLeft };
        percent ??= new GUIStyle(GameUI.SmallRight) { alignment = TextAnchor.LowerRight };
        label.fontSize = percent.fontSize = Mathf.RoundToInt(22f * u);
        int dots = Mathf.FloorToInt(now * 2.5f) % 4;
        var textRect = new Rect(bar.x, bar.y - 44f * u, bar.width, 36f * u);
        GameUI.Shadowed(textRect, "載入中" + new string('．', dots), label, new Color(0.92f, 0.88f, 0.8f, 0.85f * a));
        Color pc = Color.Lerp(new Color(0.92f, 0.88f, 0.8f), new Color(1f, 0.45f, 0.35f), flash);
        GameUI.Shadowed(textRect, Mathf.FloorToInt(Mathf.Clamp01(shown) * 100f) + "%", percent, new Color(pc.r, pc.g, pc.b, 0.85f * a));
        GUI.color = Color.white;
    }
}
