using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 強敵擊敗之後的「Congratulations!!」勝利畫面，出現後一直停在這個畫面，按確認鍵才關掉。
///   構圖：暗藍色的背景（看得到後面的世界和玩家）、幾道金色光柱、畫面下方一整排藍色火焰、往上飄的藍白光點，
///         中間白色粗體大字「Congratulations!!」砸下來，底下一條白色橫條從中間往兩邊展開（中間一個菱形缺口），
///         再下面一行小字「擊敗 〇〇」。全部用程式產生的貼圖和 UGUI 做，不需要任何美術檔案。
///   時間：Boss 倒下後 deathDelay 秒出現 → 1.2 秒後世界暫停（停在這個畫面）→ 2 秒後出現「按 × 繼續」。
///         全部用真實時間（unscaled），暫停也照樣在動：火焰會一直燒、光點一直飄、光柱慢慢明暗。
///   關掉：手把 ×（確認）、Enter / Space、滑鼠左鍵。關掉之後時間恢復，遊戲照原本的流程繼續（例如最後一關的通關畫面）。
///
/// 不用在場景裡放東西：自動監看場景裡所有 BossHealth（強敵的血量），哪一隻倒下就播。
/// 播的時候會關掉血條上原本的「強敵已擊敗」小字，也會收掉同時出現的過關大標題（VictoryBanner）。
/// </summary>
public sealed class VictoryCongratulations : MonoBehaviour
{
    /// <summary>勝利畫面正在顯示（GameUI.OverlayOpen 會算進去：這時玩家不能移動、攻擊、互動）</summary>
    public static bool Showing { get; private set; }

    const float DeathDelay = 1.8f;      // Boss 倒地動畫先播一下
    const float FreezeAfter = 1.2f;     // 出現後多久把世界暫停
    const float HintAfter = 2f;         // 多久之後出現「按 × 繼續」

    static readonly Color Navy = new Color(.02f, .035f, .09f, 1f);
    static readonly Color FlameBlue = new Color(.35f, .72f, 1f, 1f);
    static readonly Color FlameCore = new Color(.82f, .95f, 1f, 1f);
    static readonly Color ShaftGold = new Color(1f, .78f, .25f, 1f);

    // ---------------- 自動監看 Boss ----------------

    static VictoryCongratulations watcher;
    readonly HashSet<BossHealth> watched = new HashSet<BossHealth>();
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { watcher = null; Showing = false; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (watcher != null || Application.isBatchMode) return;
        var go = new GameObject("VictoryCongratulations");
        go.hideFlags = HideFlags.HideInHierarchy;
        DontDestroyOnLoad(go);
        watcher = go.AddComponent<VictoryCongratulations>();
        SceneManager.sceneLoaded += (s, m) => { if (watcher != null) watcher.nextScan = 0f; };
    }

    void Update()
    {
        if (isScreen) return;   // 勝利畫面本身也是這個元件：只有監看的那一個要找 Boss
        // Boss 有些是演出中途才生出來的：每秒重新找一次
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f;
        watched.RemoveWhere(h => h == null);
        foreach (var h in FindObjectsByType<BossHealth>(FindObjectsInactive.Include))
        {
            if (!watched.Add(h)) continue;
            var health = h;
            health.OnDied += () => { if (watcher != null) watcher.StartCoroutine(watcher.ShowAfterDeath(health)); };
        }
    }

    IEnumerator ShowAfterDeath(BossHealth health)
    {
        if (Showing) yield break;
        // These two encounters now own a continuous portal / credits sequence. A modal
        // victory screen would freeze the portal before the player could reach it.
        if(CentipedeCredits.HandlesEndingFor(health))yield break;
        // 百足駭骨三階段的分身也有 BossHealth（整隻複製出來的）：分身倒下不算贏，要等本體倒下
        if (health != null && health.TryGetComponent(out SkeletonCentipedeBossAI centipede) && centipede.IsClone) yield break;
        if (health != null) health.ShowDefeatLabel = false;   // 換成這個畫面，血條上的小字不用了
        yield return new WaitForSecondsRealtime(DeathDelay);
        if (Showing) yield break;
        Show(health != null ? health.bossName : "");
    }

    /// <summary>直接播勝利畫面（測試、或其他劇情也想用時）</summary>
    public static void Show(string defeatedName)
    {
        if (Showing) return;
        var go = new GameObject("Congratulations Screen");
        go.AddComponent<VictoryCongratulations>().Build(defeatedName);
    }

    // ---------------- 畫面本體 ----------------

    bool isScreen;
    Canvas canvas;
    CanvasGroup group;
    Image dim, flash;
    RectTransform title, barLeft, barRight, diamond;
    Text titleText, subtitle, hint;
    readonly List<Image> shafts = new List<Image>();
    readonly List<(RectTransform rt, Image img, float phase, float baseH, float speed)> flames = new List<(RectTransform, Image, float, float, float)>();
    readonly List<(RectTransform rt, Image img, Vector2 vel, float life, float age)> embers = new List<(RectTransform, Image, Vector2, float, float)>();
    float shownAt, savedTimeScale = 1f;
    bool frozen, closing;
    float nextEmber;
    static Sprite softCircle, shaftSprite, vignetteSprite, diamondSprite;

    void Build(string defeatedName)
    {
        isScreen = true;
        Showing = true;
        shownAt = Time.unscaledTime;
        VictoryBanner.HideCurrent();
        MakeSprites();

        canvas = UIKit.CreateCanvas("Congratulations Canvas", 900, transform);
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true;
        var root = canvas.transform;

        // 暗藍色背景（半透明：看得到後面的世界）+ 四周壓暗
        dim = UIKit.Panel("Dim", root, new Color(Navy.r, Navy.g, Navy.b, 0f));
        var vignette = UIKit.Panel("Vignette", root, new Color(0f, 0f, 0f, .85f));
        vignette.sprite = vignetteSprite;

        // 金色光柱：四道，從上往下打
        float[] xs = { -620f, -170f, 290f, 700f };
        for (int i = 0; i < xs.Length; i++)
        {
            var s = UIKit.Box("Shaft" + i, root, new Vector2(xs[i], 60f), new Vector2(230f, 1250f), new Color(ShaftGold.r, ShaftGold.g, ShaftGold.b, 0f));
            s.sprite = shaftSprite;
            s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (i - 1.5f) * 2.5f);
            shafts.Add(s);
        }

        // 藍色火焰帶：畫面下方一整排，大小不一的柔光團（每個自己忽大忽小）
        for (int i = 0; i < 34; i++)
        {
            float x = Mathf.Lerp(-1050f, 1050f, i / 33f) + Random.Range(-30f, 30f);
            float h = Random.Range(160f, 340f);
            var f = UIKit.Box("Flame" + i, root, new Vector2(x, -470f + h * .3f), new Vector2(Random.Range(150f, 260f), h), new Color(FlameBlue.r, FlameBlue.g, FlameBlue.b, 0f));
            f.sprite = softCircle;
            f.rectTransform.pivot = new Vector2(.5f, 0f);
            f.rectTransform.anchoredPosition = new Vector2(x, -560f);
            flames.Add((f.rectTransform, f, Random.value * 10f, h, Random.Range(5f, 9f)));
        }
        // 火焰的白色亮芯（比較小、比較亮）
        for (int i = 0; i < 18; i++)
        {
            float x = Mathf.Lerp(-980f, 980f, i / 17f) + Random.Range(-40f, 40f);
            float h = Random.Range(90f, 170f);
            var f = UIKit.Box("FlameCore" + i, root, Vector2.zero, new Vector2(Random.Range(90f, 150f), h), new Color(FlameCore.r, FlameCore.g, FlameCore.b, 0f));
            f.sprite = softCircle;
            f.rectTransform.pivot = new Vector2(.5f, 0f);
            f.rectTransform.anchoredPosition = new Vector2(x, -560f);
            flames.Add((f.rectTransform, f, Random.value * 10f, h, Random.Range(8f, 12f)));
        }

        // 標題
        var font = TitleFont();
        titleText = UIKit.Label("Title", root, "Congratulations!!", 150, Color.white, new Vector2(0f, 250f), new Vector2(1800f, 220f), TextAnchor.MiddleCenter, FontStyle.Bold);
        if (font != null) titleText.font = font;
        var shadow = titleText.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(.1f, .3f, .7f, .8f); shadow.effectDistance = new Vector2(0f, -6f);
        var glow = titleText.gameObject.AddComponent<Outline>();
        glow.effectColor = new Color(.55f, .8f, 1f, .35f); glow.effectDistance = new Vector2(3f, 3f);
        title = titleText.rectTransform;

        // 白色橫條：左右兩段從中間往外長出來，中間一個菱形
        barLeft = UIKit.Box("BarLeft", root, new Vector2(-12f, 95f), new Vector2(0f, 46f), new Color(1f, 1f, 1f, .95f)).rectTransform;
        barLeft.pivot = new Vector2(1f, .5f);
        barRight = UIKit.Box("BarRight", root, new Vector2(12f, 95f), new Vector2(0f, 46f), new Color(1f, 1f, 1f, .95f)).rectTransform;
        barRight.pivot = new Vector2(0f, .5f);
        var d = UIKit.Box("Diamond", root, new Vector2(0f, 95f), new Vector2(34f, 34f), new Color(Navy.r, Navy.g, Navy.b, 1f));
        d.sprite = diamondSprite;
        diamond = d.rectTransform;
        diamond.localScale = Vector3.zero;

        subtitle = UIKit.Label("Subtitle", root, string.IsNullOrEmpty(defeatedName) ? "強敵已擊敗" : $"擊敗　{defeatedName}", 40,
                               new Color(.85f, .93f, 1f, 0f), new Vector2(0f, 20f), new Vector2(1400f, 60f), TextAnchor.MiddleCenter, FontStyle.Bold);
        hint = UIKit.Label("Hint", root, "", 30, new Color(1f, 1f, 1f, 0f), new Vector2(0f, -330f), new Vector2(1200f, 50f));

        // 一開始的白色閃光
        flash = UIKit.Panel("Flash", root, new Color(1f, 1f, 1f, 0f));
        flash.raycastTarget = false;

        title.localScale = Vector3.one * 1.4f;
        titleText.color = new Color(1f, 1f, 1f, 0f);
        GameAudio.Play("SFX/victory_stinger", Vector3.zero, 1f, 1f, 0f, 0f);
        PadRumble.Pulse(.6f, .4f, .5f);
    }

    void LateUpdate()
    {
        if (!isScreen) return;
        float t = Time.unscaledTime - shownAt;
        float dt = Time.unscaledDeltaTime;

        // 背景 / 光柱
        float fade = closing ? 0f : 1f;
        dim.color = new Color(Navy.r, Navy.g, Navy.b, Mathf.Lerp(dim.color.a, .82f * fade, 1f - Mathf.Exp(-6f * dt)));
        for (int i = 0; i < shafts.Count; i++)
        {
            float a = Mathf.Clamp01((t - .2f - i * .08f) / .8f) * (.3f + .1f * Mathf.Sin(t * 1.3f + i * 1.7f));
            var c = shafts[i].color; c.a = a; shafts[i].color = c;
        }

        // 火焰：每團自己忽大忽小、左右晃
        float flameIn = Mathf.Clamp01((t - .1f) / .6f);
        foreach (var f in flames)
        {
            float n = Mathf.PerlinNoise(f.phase, t * f.speed * .25f);
            float h = f.baseH * (.6f + .8f * n) * flameIn;
            f.rt.sizeDelta = new Vector2(f.rt.sizeDelta.x, h);
            f.rt.localRotation = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t * .9f, f.phase) - .5f) * 14f);
            var c = f.img.color; c.a = (.45f + .35f * n) * flameIn; f.img.color = c;
        }

        // 往上飄的光點
        if (t >= nextEmber && !closing)
        {
            nextEmber = t + .035f;
            var e = UIKit.Box("Ember", canvas.transform, new Vector2(Random.Range(-1000f, 1000f), Random.Range(-540f, -380f)),
                              Vector2.one * Random.Range(4f, 11f), Random.value < .3f ? Color.white : FlameBlue);
            e.sprite = softCircle; e.raycastTarget = false;
            e.rectTransform.SetSiblingIndex(flames.Count + shafts.Count + 2);
            embers.Add((e.rectTransform, e, new Vector2(Random.Range(-25f, 25f), Random.Range(90f, 260f)), Random.Range(1.8f, 3.6f), 0f));
        }
        for (int i = embers.Count - 1; i >= 0; i--)
        {
            var e = embers[i];
            e.age += dt;
            if (e.age >= e.life || e.rt == null) { if (e.rt != null) Destroy(e.rt.gameObject); embers.RemoveAt(i); continue; }
            e.rt.anchoredPosition += e.vel * dt + new Vector2(Mathf.Sin(e.age * 3f + i) * 12f * dt, 0f);
            float k = e.age / e.life;
            var c = e.img.color; c.a = Mathf.Sin(k * Mathf.PI) * (.6f + .4f * Mathf.Sin(e.age * 14f)); e.img.color = c;
            embers[i] = e;
        }

        // 白色閃光
        flash.color = new Color(1f, 1f, 1f, t < .25f ? 0f : Mathf.Clamp01(.55f - (t - .25f) * 2.2f));

        // 標題砸下來（稍微回彈）
        float ut = Mathf.Clamp01((t - .25f) / .35f);
        float back = 1f + 2.2f * Mathf.Pow(ut - 1f, 3f) + 1.2f * Mathf.Pow(ut - 1f, 2f);   // ease out back
        title.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, back);
        titleText.color = new Color(1f, 1f, 1f, ut);

        // 橫條從中間往兩邊長出來
        float ub = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - .5f) / .45f));
        float half = 690f * ub;
        barLeft.sizeDelta = new Vector2(half, 46f);
        barRight.sizeDelta = new Vector2(half, 46f);
        diamond.localScale = Vector3.one * Mathf.Clamp01((t - .55f) / .2f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);

        float us = Mathf.Clamp01((t - .85f) / .4f);
        subtitle.color = new Color(subtitle.color.r, subtitle.color.g, subtitle.color.b, us);

        // 停在這個畫面：世界暫停
        if (!frozen && !closing && t >= FreezeAfter)
        {
            frozen = true;
            savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }

        // 提示 + 關閉
        if (t >= HintAfter && !closing)
        {
            bool pad = PadInput.UsingPadRecently;
            hint.text = pad ? $"按 {InputBindings.PathName("<Gamepad>/buttonSouth")} 繼續" : "按 Enter 繼續";
            hint.color = new Color(1f, 1f, 1f, .45f + .35f * Mathf.Sin((t - HintAfter) * 3f));
            var kb = Keyboard.current; var mouse = Mouse.current;
            if (PadInput.ConfirmPressed || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                Close();
        }

        if (closing)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, dt / .45f);
            if (group.alpha <= 0f) Destroy(gameObject);
        }
    }

    void Close()
    {
        if (!isScreen || closing) return;
        closing = true;
        hint.text = "";
        if (frozen) { Time.timeScale = savedTimeScale; frozen = false; }
    }

    void OnDestroy()
    {
        if (!isScreen) return;
        if (frozen) Time.timeScale = savedTimeScale;
        Showing = false;
    }

    // ---------------- 程式產生的貼圖 ----------------

    /// <summary>英文標題用比較窄、比較方正的粗體字（電腦上有哪個用哪個；都沒有就用預設字型）</summary>
    static Font TitleFont()
    {
        try { return Font.CreateDynamicFontFromOSFont(new[] { "DIN Condensed", "Bahnschrift SemiBold Condensed", "Bahnschrift", "Impact", "Arial Narrow", "Arial Black" }, 150); }
        catch { return null; }
    }

    static void MakeSprites()
    {
        if (softCircle != null) return;
        softCircle = MakeSprite(64, 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f)) * 2f;
            return Mathf.Pow(Mathf.Clamp01(1f - d), 1.8f);
        });
        // 光柱：左右邊緣柔和、上面亮下面淡
        shaftSprite = MakeSprite(32, 128, (u, v) =>
        {
            float side = 1f - Mathf.Abs(u - .5f) * 2f;
            return Mathf.Pow(Mathf.Clamp01(side), 1.5f) * Mathf.Lerp(.15f, 1f, v);
        });
        // 四周壓暗：中間透明、角落黑
        vignetteSprite = MakeSprite(128, 128, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .55f)) * 1.45f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - .35f) / .65f));
        });
        diamondSprite = MakeSprite(8, 8, (u, v) => 1f);
    }

    static Sprite MakeSprite(int w, int h, System.Func<float, float, float> alpha)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = new Color(1f, 1f, 1f, alpha((x + .5f) / w, (y + .5f) / h));
        tex.SetPixels32(px); tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(.5f, .5f));
    }
}
