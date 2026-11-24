using UnityEngine;

/// <summary>
/// 兩個一般招式的冷卻顯示（自動執行，不用在場景裡放東西），畫在技能條下面：
///   × □ ×（ShieldSlideStrike，「盾」）、R2 + △ □（LeapCleaveStrike，「劈」）
/// 每招一個圓形圖示（Resources/UI/Move_ShieldSlide、Move_LeapCleave）：
/// 冷卻中還沒冷卻完的部分蓋一層暗色，暗色從 12 點鐘方向順時針被掃掉，中間顯示剩幾秒；
/// 轉完一圈 = 可以用了，圖示和外圈亮起來閃一下。圓的右邊寫目前配置的按鍵（手把 / 鍵盤自動切換）。
/// 圖示載不到的話退回原本的純色圓 +「盾」「劈」字。
/// 拿長槍時（SpearArts，一個武器兩招）：「盾」「劈」換成長槍的兩招——R2 + □（破陣突刺，「刺」）、△ □（飛身貫地，「貫」），圖示 Move_SpearCharge / Move_SpearPlunge。
/// </summary>
public sealed class MoveCooldownHud : MonoBehaviour
{
    // 版面：SkillGauge 在 (20, 54, 156 x 6)，這排放在它下面
    const float Top = 64f, R = 18f, Left = 20f, Spacing = 140f;

    static readonly Color Disc = new Color(0.05f, 0.07f, 0.12f, 0.88f);
    static readonly Color Sweep = new Color(0.55f, 0.78f, 1f, 0.55f);
    static readonly Color ReadyFill = new Color(0.35f, 0.6f, 0.95f, 0.55f);
    static readonly Color Rim = new Color(0.45f, 0.29f, 0.16f, 0.95f);
    static readonly Color RimReady = new Color(0.75f, 0.92f, 1f, 1f);
    static readonly Color Shade = new Color(0.02f, 0.03f, 0.06f, 0.72f);   // 冷卻中蓋在圖示上的暗色
    static readonly string[] IconPaths = { "UI/Move_ShieldSlide", "UI/Move_LeapCleave", "UI/Move_SpearCharge", "UI/Move_SpearPlunge" };
    // 每一格用哪張圖示：平常（盾、劈）/ 拿長槍（刺、貫）
    static readonly int[] NormalIcons = { 0, 1 }, SpearIcons = { 2, 3 };

    static MoveCooldownHud instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null || Application.isBatchMode) return;
        var go = new GameObject("MoveCooldownHud");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<MoveCooldownHud>();
    }

    PlayerHealth health;
    ShieldSlideStrike slide;
    LeapCleaveStrike leap;
    SpearArts arts;
    bool SpearMode => arts != null && arts.Active;
    float nextFind;
    Material mat;
    readonly float[] readyAt = { float.NegativeInfinity, float.NegativeInfinity };
    readonly bool[] wasCooling = new bool[2];
    GUIStyle center, label;
    Texture2D[] icons;

    void Awake()
    {
        mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void Start()
    {
        icons = new Texture2D[IconPaths.Length];
        for (int i = 0; i < IconPaths.Length; i++) icons[i] = Resources.Load<Texture2D>(IconPaths[i]);
    }

    void OnDestroy() { if (mat != null) Destroy(mat); }

    Texture2D Icon(int i) => icons != null && i < icons.Length ? icons[i] : null;

    void Update()
    {
        if ((health == null || slide == null || leap == null) && Time.unscaledTime >= nextFind)
        {
            nextFind = Time.unscaledTime + 1f;
            health = PlayerHealth.Find();
            if (health != null)
            {
                slide = health.GetComponentInChildren<ShieldSlideStrike>();
                leap = health.GetComponentInChildren<LeapCleaveStrike>();
            }
        }
        if (health != null && arts == null) arts = health.GetComponentInChildren<SpearArts>();
        if (SpearMode)
        {
            Track(0, arts.ChargeRemaining);
            Track(1, arts.PlungeRemaining);
        }
        else
        {
            Track(0, slide != null ? slide.CooldownRemaining : 0f);
            Track(1, leap != null ? leap.CooldownRemaining : 0f);
        }
    }

    /// <summary>冷卻剛轉完一圈的那一刻記下來（外圈閃一下）</summary>
    void Track(int i, float remaining)
    {
        bool cooling = remaining > 0f;
        if (wasCooling[i] && !cooling) readyAt[i] = Time.unscaledTime;
        wasCooling[i] = cooling;
    }

    void OnGUI()
    {
        if (health == null || health.IsDead || GameUI.OverlayOpen || VitalsHud.Hidden) return;
        if (slide == null && leap == null) return;
        if (RangeInputSettings.IsRange(health)) return;   // 靶場不顯示（跟技能條一樣）

        string N(GameAction a) => InputBindings.DisplayName(a);
        bool spear = SpearMode;
        int[] iconIdx = spear ? SpearIcons : NormalIcons;
        var moves = spear
            ? new[]
            {
                (true, arts.ChargeRemaining, arts.chargeCooldown, "刺", $"{N(GameAction.Sprint)} + {N(GameAction.Attack)}"),
                (true, arts.PlungeRemaining, arts.plungeCooldown, "貫", $"{N(GameAction.Jump)} {N(GameAction.Attack)}"),
            }
            : new[]
            {
                (slide != null, slide != null ? slide.CooldownRemaining : 0f, slide != null ? slide.cooldown : 1f, "盾", $"{N(GameAction.Roll)} {N(GameAction.Attack)} {N(GameAction.Roll)}"),
                (leap != null, leap != null ? leap.CooldownRemaining : 0f, leap != null ? leap.cooldown : 1f, "劈", $"{N(GameAction.Sprint)} + {N(GameAction.Jump)} {N(GameAction.Attack)}"),
            };

        float t = Time.unscaledTime;
        if (Event.current.type == EventType.Repaint)
        {
            // 1. 外圈（+ 沒有圖示時的純色底）
            BeginGL();
            for (int i = 0; i < moves.Length; i++)
            {
                var (has, remaining, total, _, _) = moves[i];
                if (!has) continue;
                bool cooling = remaining > 0f;
                float flash = cooling ? 0f : Mathf.Clamp01(1f - (t - readyAt[i]) / 0.4f);
                Vector2 c = Center(i);
                Ring(c, R + 0.5f, R + 2.5f, cooling ? Rim : Color.Lerp(RimReady, Color.white, flash));
                if (Icon(iconIdx[i]) == null) DrawDial(c, Progress(remaining, total), cooling, t - readyAt[i]);
            }
            EndGL();

            // 2. 圖示
            for (int i = 0; i < moves.Length; i++)
            {
                if (!moves[i].Item1 || Icon(iconIdx[i]) == null) continue;
                Vector2 c = Center(i);
                Color old = GUI.color;
                GUI.color = moves[i].Item2 > 0f ? new Color(0.8f, 0.8f, 0.85f, 1f) : Color.white;
                GUI.DrawTexture(new Rect(c.x - R, c.y - R, R * 2f, R * 2f), Icon(iconIdx[i]), ScaleMode.StretchToFill, true);
                GUI.color = old;
            }

            // 3. 冷卻中：還沒冷卻完的部分蓋暗色（從指針順時針到 12 點鐘）＋指針；剛好的那一刻整顆閃白
            BeginGL();
            for (int i = 0; i < moves.Length; i++)
            {
                var (has, remaining, total, _, _) = moves[i];
                if (!has || Icon(iconIdx[i]) == null) continue;
                Vector2 c = Center(i);
                if (remaining > 0f)
                {
                    float p = Progress(remaining, total);
                    if (p < 0.999f) Sector(c, R, p, 1f, Shade);
                    float a = -Mathf.PI * 0.5f + p * Mathf.PI * 2f;
                    Line(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R, 1.2f, new Color(0.9f, 0.97f, 1f, 0.9f));
                }
                else
                {
                    float flash = Mathf.Clamp01(1f - (t - readyAt[i]) / 0.4f);
                    if (flash > 0f) Sector(c, R, 0f, 1f, new Color(1f, 1f, 1f, 0.55f * flash));
                }
            }
            EndGL();
        }

        center ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 14, fontStyle = FontStyle.Bold };
        label ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 12 };
        for (int i = 0; i < moves.Length; i++)
        {
            var (has, remaining, _, glyph, keys) = moves[i];
            if (!has) continue;
            Vector2 c = Center(i);
            bool cooling = remaining > 0f;
            center.normal.textColor = cooling ? new Color(1f, 1f, 1f, 0.95f) : new Color(0.9f, 0.97f, 1f, 1f);
            string mid = cooling ? remaining.ToString("0.0") : Icon(iconIdx[i]) == null ? glyph : null;   // 有圖示：好了就不寫字
            if (mid != null) Shadowed(new Rect(c.x - R, c.y - R, R * 2f, R * 2f), mid, center);
            label.normal.textColor = cooling ? new Color(1f, 1f, 1f, 0.45f) : new Color(0.85f, 0.93f, 1f, 0.9f);
            Shadowed(new Rect(c.x + R + 6f, c.y - 10f, Spacing - R * 2f - 10f, 20f), keys, label);
        }
    }

    static float Progress(float remaining, float total) => Mathf.Clamp01(1f - remaining / Mathf.Max(0.01f, total));

    void BeginGL()
    {
        mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
        GL.Begin(GL.TRIANGLES);
    }

    static void EndGL()
    {
        GL.End();
        GL.PopMatrix();
    }

    static Vector2 Center(int i) => new Vector2(Left + R + i * Spacing, Top + R);

    static void Shadowed(Rect r, string text, GUIStyle style)
    {
        Color c = style.normal.textColor;
        style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.8f);
        GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, style);
        style.normal.textColor = c;
        GUI.Label(r, text, style);
    }

    /// <summary>progress：0 = 剛開始冷卻，1 = 好了。亮的扇形從 12 點鐘順時針長到 progress</summary>
    static void DrawDial(Vector2 c, float progress, bool cooling, float sinceReady)
    {
        float flash = cooling ? 0f : Mathf.Clamp01(1f - sinceReady / 0.4f);
        // 外圈在 OnGUI 裡畫（有沒有圖示都一樣）
        // 底
        Sector(c, R, 0f, 1f, Disc);
        if (cooling)
        {
            // 已經過去的部分：亮的扇形，順時針轉
            if (progress > 0.001f) Sector(c, R, 0f, progress, Sweep);
            // 扇形前緣的指針線
            float a = -Mathf.PI * 0.5f + progress * Mathf.PI * 2f;
            Line(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R, 1.2f, new Color(0.9f, 0.97f, 1f, 0.9f));
        }
        else
        {
            Sector(c, R, 0f, 1f, Color.Lerp(ReadyFill, new Color(1f, 1f, 1f, 0.8f), flash));
        }
    }

    /// <summary>從 12 點鐘方向順時針，畫 from ~ to（0~1 圈）的扇形（螢幕座標 y 往下，角度增加 = 順時針）</summary>
    static void Sector(Vector2 c, float r, float from, float to, Color col)
    {
        int n = Mathf.Max(2, Mathf.CeilToInt((to - from) * 48f));
        GL.Color(col);
        for (int i = 0; i < n; i++)
        {
            float a0 = -Mathf.PI * 0.5f + Mathf.Lerp(from, to, (float)i / n) * Mathf.PI * 2f;
            float a1 = -Mathf.PI * 0.5f + Mathf.Lerp(from, to, (float)(i + 1) / n) * Mathf.PI * 2f;
            GL.Vertex3(c.x, c.y, 0f);
            GL.Vertex3(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r, 0f);
            GL.Vertex3(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r, 0f);
        }
    }

    static void Ring(Vector2 c, float r0, float r1, Color col)
    {
        const int n = 48;
        GL.Color(col);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector2 i0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r0, i1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r0;
            Vector2 o0 = c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r1, o1 = c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r1;
            GL.Vertex3(i0.x, i0.y, 0f); GL.Vertex3(o0.x, o0.y, 0f); GL.Vertex3(o1.x, o1.y, 0f);
            GL.Vertex3(i0.x, i0.y, 0f); GL.Vertex3(o1.x, o1.y, 0f); GL.Vertex3(i1.x, i1.y, 0f);
        }
    }

    static void Line(Vector2 a, Vector2 b, float w, Color col)
    {
        Vector2 n = (b - a).normalized;
        n = new Vector2(-n.y, n.x) * w;
        GL.Color(col);
        GL.Vertex3(a.x + n.x, a.y + n.y, 0f); GL.Vertex3(b.x + n.x, b.y + n.y, 0f); GL.Vertex3(b.x - n.x, b.y - n.y, 0f);
        GL.Vertex3(a.x + n.x, a.y + n.y, 0f); GL.Vertex3(b.x - n.x, b.y - n.y, 0f); GL.Vertex3(a.x - n.x, a.y - n.y, 0f);
    }
}
