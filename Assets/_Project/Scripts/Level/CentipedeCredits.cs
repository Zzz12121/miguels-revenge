using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 百足倒下後開啟前往河鎮的符陣；工作人員名單移至河鎮老者真正死亡後（自動掛上）：
///   河鎮 InkboundTownEncounter 的 BossHealth.OnDied → 等死亡演出 → 畫面暗下來 →
///   舊宣紙上由下往上捲過「Miguel's Revenge／製作團隊（GitHub repo 的帳號）／感謝遊玩」→ 回到漁村（GameManager.LoadLevel，帶著玩家的狀態），
///   百足擊敗紀錄由 ForestInkboundPortal 在開啟傳送流程時寫入。
/// 名單捲了 2 秒以後按 Space / Enter / 手把 A 可以直接跳到最後。百足是隊友的 Boss，這裡只訂閱它的事件，不改它。
/// </summary>
public class CentipedeCredits : MonoBehaviour
{
    public const string Scene = "ForestIsland";
    public static bool HandlesEndingFor(BossHealth health)
    {
        if(!health)return false;
        if(health.gameObject.scene.name==Scene&&health.TryGetComponent<SkeletonCentipedeBossAI>(out var centipede))return !centipede.IsClone;
        if(health.gameObject.scene.name==InkboundTownJourney.Scene)
        {
            var encounter=FindAnyObjectByType<InkboundTownEncounter>();
            return encounter&&encounter.Health==health;
        }
        return false;
    }

    /// <summary>GitHub repo（Zzz12121/miguels-revenge）的成員帳號</summary>
    static readonly string[] Team = { "LinLeo109", "Zzz12121", "isc777", "kevin2-111110" };
    const string Repo = "github.com/Zzz12121/miguels-revenge";

    const float AfterDefeat = 6f;      // 河鎮老者死亡演出結束後再進片尾
    const float ScrollSeconds = 16f;   // 名單從下面捲到停住
    const float HoldSeconds = 2.5f;    // 最後「感謝遊玩」停在中間

    static bool hooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => hooked = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!hooked)
        {
            hooked = true;
            SceneManager.sceneLoaded += (s, m) => Attach(s);
        }
        Attach(SceneManager.GetActiveScene());
    }

    static void Attach(Scene s)
    {
        if ((s.name != Scene && s.name != InkboundTownJourney.Scene) || FindAnyObjectByType<CentipedeCredits>() != null) return;
        var go = new GameObject("CentipedeCredits");
        SceneManager.MoveGameObjectToScene(go, s);
        go.AddComponent<CentipedeCredits>();
    }

    SkeletonCentipedeBossAI boss;
    BossHealth townHealth;
    bool lockedInput;
    bool started, rolling, leaving;
    float black, scroll, textAlpha, rollStart;
    ThirdPersonController lockedCtrl;
    GUIStyle titleStyle, headStyle, nameStyle, smallStyle;
    static Texture2D pixel;

    void Update()
    {
        if (!started && gameObject.scene.name == InkboundTownJourney.Scene && townHealth == null)
        {
            var encounter=FindAnyObjectByType<InkboundTownEncounter>();
            if(encounter && encounter.Health)
            {
                townHealth=encounter.Health;townHealth.OnDied+=OnTownDefeated;
                if(townHealth.IsDead)OnTownDefeated();
            }
        }
        if (!started && gameObject.scene.name == Scene && boss == null)
        {
            foreach (var b in FindObjectsByType<SkeletonCentipedeBossAI>(FindObjectsInactive.Exclude))
                if (!b.IsClone) { boss = b; boss.OnDefeated += OnDefeated; break; }
        }
        if (rolling && !leaving && Time.unscaledTime - rollStart > 2f && Time.timeScale > 0f && !GameUI.OverlayOpen)
        {
            var kb = Keyboard.current;
            if ((kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) || PadInput.ConfirmPressed)
                scroll = Mathf.Max(scroll, ScrollSeconds);
        }
    }

    void OnDestroy()
    {
        if (boss != null) boss.OnDefeated -= OnDefeated;
        if(townHealth)townHealth.OnDied-=OnTownDefeated;
        if(lockedCtrl){lockedCtrl.UnlockActions();lockedCtrl.inputEnabled=lockedInput;}
        if (rolling) { VitalsHud.Hidden = false; InteractPrompt.Unblock(this); }
    }

    void OnDefeated()
    {
        if (started) return;
        started = true;
        ForestInkboundPortal.BeginAfterDefeat(boss);
    }

    void OnTownDefeated()
    {
        if(started)return;
        started=true;
        StartCoroutine(Roll());
    }

    IEnumerator Roll()
    {
        for (float t = 0f; t < AfterDefeat; t += Time.deltaTime) yield return null;

        // 玩家停下來、HUD 收起來，畫面暗下來
        var hp = PlayerHealth.Find();
        lockedCtrl = hp != null ? hp.GetComponent<ThirdPersonController>() : null;
        if (lockedCtrl != null)
        {
            lockedInput=lockedCtrl.inputEnabled;
            lockedCtrl.LockActions();
            lockedCtrl.inputEnabled = false;
            lockedCtrl.StopHorizontalMotion();
        }
        rolling = true;
        VitalsHud.Hidden = true;
        InteractPrompt.Block(this);
        var music = GameAudio.Clip("Music/bgm_menu");
        if (music != null) GameAudio.PlayMusic(music, 0.4f, 2.5f);
        for (float t = 0f; t < 1.6f; t += Time.deltaTime) { black = t / 1.6f; yield return null; }
        black = 1f;

        // 名單：舊宣紙上由下往上捲
        rollStart = Time.unscaledTime;
        for (float t = 0f; t < 0.8f; t += Time.deltaTime) { textAlpha = t / 0.8f; yield return null; }
        textAlpha = 1f;
        while (scroll < ScrollSeconds) { scroll += Time.deltaTime; yield return null; }
        scroll = ScrollSeconds;
        for (float t = 0f; t < HoldSeconds; t += Time.deltaTime) yield return null;
        for (float t = 0f; t < 1.2f; t += Time.deltaTime) { textAlpha = 1f - t / 1.2f; yield return null; }
        textAlpha = 0f;

        // 回到漁村
        GameManager.NextArrivalEvent = "擊敗畫中河鎮的老者，回到漁村";
        leaving = true;
        if (lockedCtrl != null) { lockedCtrl.UnlockActions(); lockedCtrl.inputEnabled = lockedInput; lockedCtrl = null; }
        var gm = GameManager.Instance;
        if (gm != null) gm.LoadLevel(VillageReturn.VillageScene);
        else SceneManager.LoadScene(VillageReturn.VillageScene);
    }

    // ---------------------------------------------------------------- 畫面

    void OnGUI()
    {
        if (black <= 0f || GameUI.OverlayOpen) return;
        GUI.depth = -999; // GameManager 換場景的淡出（-1000）蓋在上面
        if (pixel == null) { pixel = new Texture2D(1, 1); pixel.SetPixel(0, 0, Color.white); pixel.Apply(); }
        float w = Screen.width, h = Screen.height;

        // 底：舊宣紙（主畫面那張），沒有的話是黑的
        GUI.color = new Color(0f, 0f, 0f, black);
        GUI.DrawTexture(new Rect(0f, 0f, w, h), pixel);
        var paper = MenuSkin.Paper;
        bool onPaper = paper != null;
        if (onPaper)
        {
            GUI.color = new Color(0.92f, 0.88f, 0.8f, black);
            GUI.DrawTexture(new Rect(0f, 0f, w, h), paper, ScaleMode.ScaleAndCrop);
        }
        if (textAlpha <= 0f) { GUI.color = Color.white; return; }

        EnsureStyles(h);
        Color ink = onPaper ? new Color(0.17f, 0.11f, 0.065f, textAlpha) : new Color(1f, 0.96f, 0.88f, textAlpha);
        Color red = onPaper ? new Color(0.6f, 0.14f, 0.08f, textAlpha) : new Color(1f, 0.55f, 0.45f, textAlpha);
        Color soft = new Color(ink.r, ink.g, ink.b, 0.65f * textAlpha);

        // 一行一行排：(文字, 樣式, 顏色, 這一行佔多高（畫面高的比例）)
        var lines = new System.Collections.Generic.List<(string text, GUIStyle style, Color color, float height)>
        {
            ("Miguel's Revenge", titleStyle, ink, 0.1f),
            ("神使之復仇", headStyle, red, 0.12f),
            ("製作團隊", headStyle, red, 0.08f),
        };
        foreach (var n in Team) lines.Add((n, nameStyle, ink, 0.065f));
        lines.Add(("", nameStyle, ink, 0.06f));
        lines.Add((Repo, smallStyle, soft, 0.12f));
        lines.Add(("感謝遊玩", titleStyle, ink, 0.1f));

        float total = 0f;
        foreach (var l in lines) total += l.height * h;
        // 從畫面下緣外面開始往上捲，捲完時最後一行（感謝遊玩）停在畫面中間
        float lastCenter = total - lines[lines.Count - 1].height * h * 0.5f;
        float startY = h + h * 0.05f, endY = h * 0.5f - lastCenter;
        float u = Mathf.Clamp01(scroll / ScrollSeconds);
        float y = Mathf.Lerp(startY, endY, u);
        foreach (var l in lines)
        {
            float lh = l.height * h;
            if (!string.IsNullOrEmpty(l.text) && y + lh > 0f && y < h)
            {
                l.style.normal.textColor = l.color;
                GUI.color = Color.white;
                GUI.Label(new Rect(0f, y, w, lh), l.text, l.style);
            }
            y += lh;
        }

        if (rolling && !leaving && Time.unscaledTime - rollStart > 2f && scroll < ScrollSeconds)
        {
            const string action = "跳過";
            var style = GameUI.SmallRight;
            float tw = style.CalcSize(new GUIContent(action)).x;
            float x = w - 32f - tw, yy = h - 44f;
            GUI.color = new Color(1f, 1f, 1f, 0.6f * textAlpha);
            GUI.Label(new Rect(x, yy, tw, 24f), action, style);
            GameUI.Key(new Rect(x - 64f, yy, 58f, 24f), "Space", 0.6f * textAlpha);
        }
        GUI.color = Color.white;
    }

    void EnsureStyles(float h)
    {
        if (titleStyle == null)
        {
            GUIStyle Make(FontStyle fs)
            {
                var s = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = fs, richText = false, wordWrap = false };
                if (MenuSkin.Font != null) s.font = MenuSkin.Font;
                return s;
            }
            titleStyle = Make(FontStyle.Bold);
            headStyle = Make(FontStyle.Bold);
            nameStyle = Make(FontStyle.Normal);
            smallStyle = Make(FontStyle.Normal);
        }
        titleStyle.fontSize = Mathf.RoundToInt(h * 0.065f);
        headStyle.fontSize = Mathf.RoundToInt(h * 0.042f);
        nameStyle.fontSize = Mathf.RoundToInt(h * 0.04f);
        smallStyle.fontSize = Mathf.RoundToInt(h * 0.024f);
    }
}
