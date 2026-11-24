using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技能條（遊戲平衡）：技能要靠普攻累積，不能一直放。
///   - 普攻打中一下 +1/HitsToFill（預設 10 下集滿；一刀砍中好幾隻每隻都算）。技能自己造成的傷害不算。
///   - 集滿才能放技能（虛空連斬、技能 2~4），放出去就整條用掉；
///     × □ × 的滑剷繞背斬、R2 + △ □ 的跳躍劈擊是一般招式：不需要技能條，打中也跟普攻一樣累積；
///     沒集滿時按技能：技能條外框閃紅、輕震一下，技能不會出來。蓄力 / 瞄準到一半取消不會扣。
///   - 牽引（拉索）不受限制：它也是過關卡用的移動手段。靶場（VFX_VoidRend_Bocchi）也不受限制，方便測特效。
/// 畫在白色精力條正下方（跟 VitalsHud 同一套日式斜角外框）：深藍底、藍白色的能量往右流，
/// 十格刻度；集滿時整條發亮、一閃一閃，右端冒出小火花。
/// 不用在場景裡放東西：遊戲開始自動建立，換場景不會消失，技能條的值也會保留。
/// </summary>
public sealed class SkillGauge : MonoBehaviour
{
    public const int HitsToFill = 10;

    /// <summary>0 ~ 1</summary>
    public static float Value { get; private set; }
    public static bool Ready => Value >= .999f;

    static SkillGauge instance;
    static float deniedAt = float.NegativeInfinity, filledAt = float.NegativeInfinity, gainedAt = float.NegativeInfinity;

    // 版面：VitalsHud 的精力條是 (20, 38, 176 x 8)，這條放在它下面
    const float X = 20f, Y = 54f, W = 156f, H = 6f, Slant = 3f;

    static readonly Color Frame = new Color(0.45f, 0.29f, 0.16f, 0.95f);
    static readonly Color FrameInner = new Color(0.2f, 0.12f, 0.07f, 0.95f);
    static readonly Color Empty = new Color(0.05f, 0.07f, 0.12f, 0.92f);
    static readonly Color Energy = new Color(0.35f, 0.7f, 1f);
    static readonly Color EnergyBright = new Color(0.85f, 0.97f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { instance = null; Value = 0f; deniedAt = filledAt = gainedAt = float.NegativeInfinity; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null || Application.isBatchMode) return;
        var go = new GameObject("SkillGauge");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<SkillGauge>();
    }

    // ---------------- 給技能用 ----------------

    /// <summary>這個技能現在能不能放（不受限制的場景永遠可以）。不行的話順便提示玩家（外框閃紅、輕震）</summary>
    public static bool TryBegin(Component caster)
    {
        if (Exempt(caster) || Ready) return true;
        Deny();
        return false;
    }

    /// <summary>技能真的放出去了：整條用掉</summary>
    public static void Consume(Component caster)
    {
        if (Exempt(caster)) return;
        Value = 0f;
    }

    public static void Deny()
    {
        if (Time.unscaledTime - deniedAt > .25f) PadRumble.Pulse(.15f, .25f, .08f);
        deniedAt = Time.unscaledTime;
    }

    /// <summary>靶場（特效測試場景）不受技能條限制</summary>
    static bool Exempt(Component caster) => caster != null && RangeInputSettings.IsRange(caster);

    static void Gain(float amount)
    {
        if (amount <= 0f || Ready) return;
        Value = Mathf.Clamp01(Value + amount);
        gainedAt = Time.unscaledTime;
        if (Ready) { filledAt = Time.unscaledTime; PadRumble.Pulse(.3f, .5f, .18f); }
    }

    // ---------------- 普攻累積 ----------------

    PlayerHealth health;
    PlayerCombat combat;
    float nextFind;
    Material mat;
    float shown;

    void Awake()
    {
        mat = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void OnDestroy() { Unhook(); if (mat != null) Destroy(mat); }

    void Update()
    {
        if (health == null && Time.unscaledTime >= nextFind) { nextFind = Time.unscaledTime + 1f; Hook(); }
        shown = Mathf.MoveTowards(shown, Value, Time.unscaledDeltaTime * (Value < shown ? 3f : 1.6f));
    }

    void Hook()
    {
        Unhook();
        health = PlayerHealth.Find();
        if (health == null) return;
        combat = health.GetComponent<PlayerCombat>();
        if (combat != null) combat.OnDealtHit += OnDealtHit;
    }

    void Unhook()
    {
        if (combat != null) combat.OnDealtHit -= OnDealtHit;
        combat = null; health = null;
    }

    void OnDealtHit(float damage, IDamageable target)
    {
        if (PlayerCombat.ReportingSkillHit) return;   // 技能打中的不算
        if (target != null && (target.IsDead && damage <= 0f)) return;
        Gain(1f / HitsToFill);
    }

    // ---------------- 畫面 ----------------

    void OnGUI()
    {
        if (health == null || health.IsDead || GameUI.OverlayOpen || VitalsHud.Hidden) return;
        if (Event.current.type != EventType.Repaint) return;
        if (RangeInputSettings.IsRange(health)) return;   // 靶場不顯示
        mat.SetPass(0);
        GL.PushMatrix();
        GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f);
        GL.Begin(GL.TRIANGLES);
        Draw(Time.unscaledTime);
        GL.End();
        GL.PopMatrix();
    }

    void Draw(float t)
    {
        float denied = Mathf.Clamp01(1f - (t - deniedAt) / .6f);
        float flashRed = denied * Mathf.Abs(Mathf.Sin(t * 25f));
        bool ready = Ready;
        float pulse = ready ? .5f + .5f * Mathf.Sin(t * 6f) : 0f;
        Color frame = Color.Lerp(Frame, new Color(1f, .25f, .2f, 1f), flashRed);
        if (ready) frame = Color.Lerp(frame, new Color(.6f, .85f, 1f, 1f), .35f + .35f * pulse);
        Para(X, Y, W, H, Slant, 2.5f, frame);
        Para(X, Y, W, H, Slant, 1.2f, FrameInner);
        Para(X, Y, W, H, Slant, 0f, Empty);

        // 能量：切成小段，亮度隨時間往右流動
        float level = Mathf.Clamp01(shown);
        if (level > .002f)
        {
            float end = W * level;
            int cols = Mathf.Max(2, Mathf.CeilToInt(end / 4f));
            float justGained = Mathf.Clamp01(1f - (t - gainedAt) / .35f);
            for (int c = 0; c < cols; c++)
            {
                float u0 = (float)c / cols, u1 = (float)(c + 1) / cols;
                float x0 = end * u0, x1 = end * u1;
                float wave = .5f + .5f * Mathf.Sin(x0 * .09f - t * 7f);
                Color col = Color.Lerp(Energy, EnergyBright, .25f * wave + .5f * pulse + .4f * justGained * Mathf.Clamp01(1f - (end - x0) / 30f));
                col.a = ready ? .95f : .8f;
                Quad(new Vector2(X + Slant + x0, Y), new Vector2(X + Slant + x1, Y), new Vector2(X + x1, Y + H), new Vector2(X + x0, Y + H), col);
            }
            // 上緣的亮線
            Para(X + Slant * .2f, Y, end, 1.2f, Slant * .2f, 0f, new Color(1f, 1f, 1f, .18f + .25f * pulse));
        }

        // 十格刻度
        for (int i = 1; i < HitsToFill; i++)
        {
            float x = W * i / HitsToFill;
            Quad(new Vector2(X + Slant + x, Y), new Vector2(X + Slant + x + 1f, Y), new Vector2(X + x + 1f, Y + H), new Vector2(X + x, Y + H), new Color(0f, 0f, 0f, .45f));
        }

        // 集滿：右端冒小火花，剛滿的那一下整條閃白
        if (ready)
        {
            for (int i = 0; i < 3; i++)
            {
                float ph = t * 3f + i * 2.1f;
                Vector2 c = new Vector2(X + Slant * .5f + W + 3f + 3f * Mathf.Sin(ph), Y + H * .5f + 3f * Mathf.Sin(ph * 1.6f));
                Blob(c, 3f + 1.2f * Mathf.Sin(ph * 1.3f), new Color(.75f, .92f, 1f, .5f));
            }
            float flash = Mathf.Clamp01(1f - (t - filledAt) / .45f);
            if (flash > 0f) Para(X, Y, W, H, Slant, 1f, new Color(1f, 1f, 1f, .6f * flash));
        }
    }

    static void Para(float x, float y, float w, float h, float slant, float grow, Color c)
    {
        float k = slant / Mathf.Max(0.01f, h), g = grow;
        Vector2 tl = new Vector2(x + k * (h + g) - g, y - g), tr = new Vector2(x + w + k * (h + g) + g, y - g);
        Vector2 br = new Vector2(x + w - k * g + g, y + h + g), bl = new Vector2(x - k * g - g, y + h + g);
        Quad(tl, tr, br, bl, c);
    }

    static void Blob(Vector2 c, float r, Color col)
    {
        const int n = 12;
        Color edge = new Color(col.r, col.g, col.b, 0f);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            GL.Color(col); GL.Vertex3(c.x, c.y, 0f);
            GL.Color(edge); GL.Vertex3(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r, 0f);
            GL.Color(edge); GL.Vertex3(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r, 0f);
        }
    }

    static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        GL.Color(col);
        GL.Vertex3(a.x, a.y, 0f); GL.Vertex3(b.x, b.y, 0f); GL.Vertex3(c.x, c.y, 0f);
        GL.Vertex3(a.x, a.y, 0f); GL.Vertex3(c.x, c.y, 0f); GL.Vertex3(d.x, d.y, 0f);
    }
}

/// <summary>
/// 技能對特定強敵的傷害上限：一次技能（一次施放）對百足駭骨最多打掉牠 MaxFraction（30%）的最大血量，
/// 剩下的要靠普攻。技能每次對敵人造成傷害都經過 Deal()；普攻不經過這裡。
/// </summary>
public static class SkillDamageCap
{
    /// <summary>一次技能最多打掉最大血量的多少</summary>
    public const float MaxFraction = .3f;

    static readonly Dictionary<Object, Dictionary<BossHealth, float>> dealt = new Dictionary<Object, Dictionary<BossHealth, float>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => dealt.Clear();

    /// <summary>新的一次施放開始（caster = 放技能的元件）：重新計算這次打掉多少</summary>
    public static void BeginCast(Object caster)
    {
        if (caster != null) dealt[caster] = new Dictionary<BossHealth, float>();
    }

    /// <summary>
    /// 技能造成傷害：對有上限的強敵，這次施放累計不能超過最大血量的 30%（超過的部分不算，打滿了就完全不扣）。
    /// 回傳真正造成的傷害（0 = 沒打）。
    /// </summary>
    public static float Deal(Object caster, IDamageable target, float amount, Vector3 point)
    {
        if (target == null || amount <= 0f || target.IsDead) return 0f;
        var boss = Capped(target);
        if (boss != null && caster != null)
        {
            if (!dealt.TryGetValue(caster, out var perBoss)) dealt[caster] = perBoss = new Dictionary<BossHealth, float>();
            perBoss.TryGetValue(boss, out float done);
            float room = boss.maxHealth * MaxFraction - done;
            if (room <= .01f) { StunIfCentipede(target); return 0f; }   // 打滿 30% 不扣血，但還是會僵持
            amount = Mathf.Min(amount, room);
            perBoss[boss] = done + amount;
        }
        target.TakeDamage(amount, point);
        StunIfCentipede(target);
        return amount;
    }

    /// <summary>大招打中百足駭骨（本體或分身）：僵持 StunSeconds 秒，期間不能動、不能放技能，蝙蝠也不攻擊</summary>
    public const float StunSeconds = 1f;

    static void StunIfCentipede(IDamageable target)
    {
        if (!(target is Component c) || c == null) return;
        var ai = c.GetComponentInParent<SkeletonCentipedeBossAI>();
        if (ai != null) ai.SkillStun(StunSeconds);
    }

    /// <summary>這個目標是不是有技能傷害上限的強敵（目前：百足駭骨本體；分身不算）</summary>
    static BossHealth Capped(IDamageable target)
    {
        if (!(target is Component c) || c == null) return null;
        var boss = c as BossHealth ?? c.GetComponentInParent<BossHealth>();
        if (boss == null) return null;
        // 三階段的分身不設上限（技能可以整條打掉），只有本體有 30% 上限
        return boss.TryGetComponent(out SkeletonCentipedeBossAI ai) && !ai.IsClone ? boss : null;
    }
}
