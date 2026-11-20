using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 日式的血量 / 精力條（取代 PlayerHealth、PlayerStamina 原本的簡易長方形）：
///   - 外形：頭尾斜切的平行四邊形，深色底、咖啡色外框；血條右邊幾顆血滴是剩下的命（GameManager.Lives），
///     少一條命那顆血滴破掉、濺出小血點，變成空的殼
///   - 血量像液體：數值用彈簧追（會晃一下再停）、液面的邊緣是波浪，扣血時波浪變大、濺出幾滴血，
///     失去的那段先留著暗紅色、慢慢流掉；補血時液面湧上來、冒小氣泡；裡面有緩緩流動的光澤
///   - 茶屋甜點：護盾（SweetShield）是血條上一層淡金色的釉，從左邊算起、長度 = 護盾 / 最大生命，擋下傷害時閃一下；
///     額外精力（PlayerStamina.Bonus01）是精力條右邊多出來的一截金色的霧，用掉就縮回去
///   - 精力像氣體：半透明的霧，濃淡隨時間飄移、尾端是散開的煙；用掉的那段化成一團團煙往上飄散，
///     平常液面上也會偶爾冒出細細的煙；精力不夠時外框閃紅、精力低時顏色偏橘並微微閃爍
/// 用 GL 在 OnGUI 的 Repaint 畫多邊形（斜角、波浪、圓點、血滴），不畫文字。
/// 遊戲開始自動建立（RuntimeInitializeOnLoadMethod），換場景不會消失；場景裡沒有玩家就不畫。
/// </summary>
public class VitalsHud : MonoBehaviour
{
    /// <summary>過場演出時藏起來（例如藥師送藥）</summary>
    public static bool Hidden;

    // ---- 版面（像素，螢幕左上角起算，跟原本的血條同一個位置） ----
    const float HpX = 20f, HpY = 18f, HpW = 196f, HpH = 14f, HpSlant = 6f;
    const float StX = 20f, StY = 38f, StW = 176f, StH = 8f, StSlant = 4f;

    // ---- 顏色 ----
    static readonly Color Frame = new Color(0.45f, 0.29f, 0.16f, 0.95f);        // 外框：咖啡色
    static readonly Color FrameInner = new Color(0.2f, 0.12f, 0.07f, 0.95f);    // 外框內側：深咖啡
    static readonly Color HpEmpty = new Color(0.16f, 0.05f, 0.05f, 0.95f);
    static readonly Color HpDeep = new Color(0.33f, 0.07f, 0.07f);              // 深紅（液體底部）
    static readonly Color HpBright = new Color(0.56f, 0.14f, 0.12f);            // 暗朱紅（液面）：亮度、對比都壓低
    static readonly Color HpTrail = new Color(0.36f, 0.13f, 0.11f, 0.8f);       // 剛失去的那段
    static readonly Color HpHeal = new Color(0.72f, 0.4f, 0.32f);
    static readonly Color DropTop = new Color(0.45f, 0.1f, 0.09f), DropBottom = new Color(0.28f, 0.05f, 0.05f); // 命的血滴（暗）
    static readonly Color StEmpty = new Color(0.07f, 0.08f, 0.09f, 0.9f);
    static readonly Color StMist = new Color(0.86f, 0.93f, 0.91f);              // 白綠的霧
    static readonly Color StLow = new Color(1f, 0.62f, 0.38f);

    static VitalsHud instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("VitalsHud");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<VitalsHud>();
    }

    PlayerHealth health;
    PlayerStamina stamina;
    static FieldInfo deniedField;
    Material mat;

    // 血量：液體
    float hpShown = 1f, hpVel, hpTrail = 1f, hpTrailHold, hpWave, hpHealGlow, lastHp = -1f;
    // 精力：氣體
    float stShown = 1f, lastSt = -1f, wispTimer;
    // 命（血滴）：上一次的數量、每顆剛破掉多久（< 0 = 沒在破）
    int lastLives = -1;
    readonly float[] dropBreak = new float[8];
    const float DropX = HpX + HpW + HpSlant + 10f, DropGap = 9.5f, DropR = 3.1f, DropTilt = 28f;

    struct Particle
    {
        public Vector2 pos, vel;
        public float age, life, r0, r1;
        public Color color;
        public bool gas;   // true = 煙（變大、變淡、往上飄），false = 血滴（受重力、變小）
        public bool clip;  // 只畫在血條裡面（補血的氣泡）
    }
    readonly List<Particle> particles = new List<Particle>();

    void Awake()
    {
        var shader = Shader.Find("Hidden/Internal-Colored");
        mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        mat.SetInt("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    void OnDestroy()
    {
        if (instance == this) { instance = null; PlayerHealth.SimpleBarHidden = false; }
        if (mat != null) Destroy(mat);
    }

    bool FindPlayer()
    {
        if (health == null)
        {
            health = PlayerHealth.Find();
            stamina = health != null ? health.GetComponent<PlayerStamina>() : null;
            lastHp = -1f; lastSt = -1f;
        }
        else if (stamina == null) stamina = health.GetComponent<PlayerStamina>();
        PlayerHealth.SimpleBarHidden = health != null;
        return health != null;
    }

    // ---------------------------------------------------------------- 狀態

    void Update()
    {
        if (!FindPlayer()) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float t = Time.unscaledTime;

        // 血量：彈簧追目標（稍微超過再回來，像液體晃一下）；扣血時失去的那段停一下再流掉
        float hp = Mathf.Clamp01(health.Health / Mathf.Max(1f, health.maxHealth));
        if (lastHp < 0f) { lastHp = hp; hpShown = hp; hpTrail = hp; }
        float d = hp - lastHp;
        if (Mathf.Abs(d) > 0.0005f)
        {
            hpWave = Mathf.Min(1f, hpWave + Mathf.Abs(d) * 6f + 0.25f);
            if (d < 0f)
            {
                hpTrailHold = 0.45f;
                hpTrail = Mathf.Max(hpTrail, lastHp);
                SpawnDroplets(lastHp, Mathf.Clamp(Mathf.RoundToInt(-d * 45f), 2, 9));
            }
            else
            {
                hpHealGlow = 1f;
                SpawnBubbles(hp, Mathf.Clamp(Mathf.RoundToInt(d * 30f), 2, 7));
            }
            lastHp = hp;
        }
        hpVel += ((hp - hpShown) * 70f - hpVel * 9f) * dt;
        hpShown += hpVel * dt;
        if (hpTrailHold > 0f) hpTrailHold -= dt;
        else hpTrail = Mathf.MoveTowards(hpTrail, hpShown, dt * (0.12f + (hpTrail - hpShown) * 2.2f));
        if (hpTrail < hpShown) hpTrail = hpShown;
        hpWave = Mathf.MoveTowards(hpWave, 0f, dt * 0.9f) * Mathf.Exp(-dt * 1.2f);
        hpHealGlow = Mathf.MoveTowards(hpHealGlow, 0f, dt * 1.4f);

        // 精力：平滑追目標；用掉的那段化成煙飄走
        if (stamina != null)
        {
            float st = stamina.Stamina01;
            if (lastSt < 0f) { lastSt = st; stShown = st; }
            if (st < lastSt - 0.004f) SpawnVapor(st, lastSt);
            lastSt = st;
            stShown = Mathf.Lerp(stShown, st, 1f - Mathf.Exp(-dt * 9f));
            // 平常也偶爾從霧裡冒出細細的煙
            wispTimer -= dt;
            if (wispTimer <= 0f && stShown > 0.03f)
            {
                wispTimer = Random.Range(0.18f, 0.45f);
                float x = StX + StSlant + Random.Range(0.05f, 1f) * StW * stShown;
                particles.Add(new Particle
                {
                    pos = new Vector2(x, StY + Random.Range(0f, 3f)), vel = new Vector2(Random.Range(4f, 12f), Random.Range(-16f, -8f)),
                    life = Random.Range(0.9f, 1.5f), r0 = 1.2f, r1 = Random.Range(3f, 5f), color = MistColor(t) * new Color(1f, 1f, 1f, 0.22f), gas = true,
                });
            }
        }

        // 少了一條命：那顆血滴破掉、濺出小血點
        var gm = GameManager.Instance;
        int lives = gm != null ? gm.Lives : -1;
        if (lastLives < 0) lastLives = lives;
        if (lives >= 0 && lives < lastLives)
            for (int i = lives; i < lastLives && i < dropBreak.Length; i++)
            {
                dropBreak[i] = 0.001f;
                Vector2 c = DropCenter(i);
                for (int k = 0; k < 6; k++)
                    particles.Add(new Particle
                    {
                        pos = c + new Vector2(Random.Range(-2f, 2f), Random.Range(-2f, 2f)),
                        vel = new Vector2(Random.Range(-60f, 60f), Random.Range(-110f, -30f)),
                        life = Random.Range(0.5f, 0.8f), r0 = Random.Range(1.2f, 2.2f), r1 = 0.6f,
                        color = Color.Lerp(HpDeep, HpBright, Random.value),
                    });
            }
        if (lives > lastLives) for (int i = 0; i < dropBreak.Length; i++) dropBreak[i] = 0f;
        lastLives = lives;
        for (int i = 0; i < dropBreak.Length; i++) if (dropBreak[i] > 0f) dropBreak[i] = Mathf.Min(1f, dropBreak[i] + dt * 3f);

        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            p.age += dt;
            if (p.age >= p.life) { particles.RemoveAt(i); continue; }
            if (p.gas)
            {
                p.vel *= Mathf.Exp(-dt * 1.2f);
                p.vel.y -= 6f * dt;                                   // 往上飄
                p.vel.x += Mathf.Sin(t * 2f + p.pos.y * 0.2f) * 10f * dt; // 左右飄忽
            }
            else if (p.clip) p.vel.y -= 25f * dt;                    // 氣泡往上浮
            else p.vel.y += 420f * dt;                                // 血滴往下掉
            p.pos += p.vel * dt;
            particles[i] = p;
        }
    }

    Color MistColor(float t)
    {
        bool low = stamina != null && stamina.Stamina01 < 0.2f;
        if (!low) return StMist;
        return Color.Lerp(StMist, StLow, 0.6f + 0.25f * Mathf.Sin(t * 9f));
    }

    void SpawnDroplets(float from, int n)
    {
        float edge = HpX + HpSlant * 0.5f + HpW * from;
        for (int i = 0; i < n; i++)
            particles.Add(new Particle
            {
                pos = new Vector2(edge + Random.Range(-3f, 3f), HpY + Random.Range(2f, HpH - 2f)),
                vel = new Vector2(Random.Range(-30f, 90f), Random.Range(-150f, -50f)),
                life = Random.Range(0.55f, 0.9f), r0 = Random.Range(1.5f, 2.8f), r1 = 0.7f,
                color = Color.Lerp(HpDeep, HpBright, Random.value),
            });
    }

    void SpawnBubbles(float to, int n)
    {
        float edge = HpX + HpSlant * 0.5f + HpW * to;
        for (int i = 0; i < n; i++)
            particles.Add(new Particle
            {
                pos = new Vector2(edge - Random.Range(4f, 40f), HpY + HpH - Random.Range(1f, 4f)),
                vel = new Vector2(Random.Range(-6f, 6f), Random.Range(-14f, -6f)),
                life = Random.Range(0.6f, 1.1f), r0 = Random.Range(1.2f, 2.2f), r1 = 1f,
                color = new Color(0.85f, 0.6f, 0.5f, 0.5f), clip = true,
            });
    }

    void SpawnVapor(float to, float from)
    {
        float x0 = StX + StSlant * 0.5f + StW * to, x1 = StX + StSlant * 0.5f + StW * from;
        int n = Mathf.Clamp(Mathf.RoundToInt((from - to) * 60f), 1, 10);
        Color c = MistColor(Time.unscaledTime);
        for (int i = 0; i < n; i++)
            particles.Add(new Particle
            {
                pos = new Vector2(Random.Range(x0, Mathf.Max(x0 + 2f, x1)), StY + Random.Range(1f, StH - 1f)),
                vel = new Vector2(Random.Range(6f, 30f), Random.Range(-38f, -16f)),
                life = Random.Range(0.9f, 1.6f), r0 = Random.Range(2f, 3f), r1 = Random.Range(7f, 11f),
                color = new Color(c.r, c.g, c.b, Random.Range(0.3f, 0.45f)), gas = true,
            });
    }

    // ---------------------------------------------------------------- 畫

    void OnGUI()
    {
        if (health == null || GameUI.OverlayOpen || Hidden) return;
        bool showStamina = stamina != null && !health.IsDead;
        if (Event.current.type == EventType.Repaint)
        {
            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, Screen.width, Screen.height, 0f); // 跟 GUI 一樣：左上角是原點、y 往下
            DrawGL(Time.unscaledTime, showStamina);
            GL.PopMatrix();
        }
    }

    /// <summary>所有的形狀（GL，像素座標、y 往下）。測試時也可以畫到 RenderTexture 上</summary>
    public void DrawGL(float t, bool showStamina)
    {
        GL.Begin(GL.TRIANGLES);
        DrawHealth(t);
        DrawShield(t);
        DrawLives(t);
        if (showStamina) { DrawStamina(t); DrawBonusStamina(t); }
        DrawParticles(showStamina);
        GL.End();
    }

    // ---- 血量 ----

    void DrawHealth(float t)
    {
        // 黑漆外框 + 金邊（都是同樣斜角的平行四邊形，往外擴）
        Para(HpX, HpY, HpW, HpH, HpSlant, 3f, Frame);
        Para(HpX, HpY, HpW, HpH, HpSlant, 1.5f, FrameInner);
        Para(HpX, HpY, HpW, HpH, HpSlant, 0f, HpEmpty);
        // 竹節：四分之一、一半、四分之三的地方淡淡的刻線
        for (int i = 1; i < 4; i++)
        {
            float x = HpX + HpW * i / 4f;
            Quad(new Vector2(x + HpSlant, HpY), new Vector2(x + HpSlant + 1f, HpY), new Vector2(x + 1f, HpY + HpH), new Vector2(x, HpY + HpH), new Color(0.35f, 0.12f, 0.1f, 0.9f));
        }
        // 剛失去的那段（暗紅）
        float trail = Mathf.Clamp01(hpTrail), shown = Mathf.Clamp(hpShown, 0f, 1.02f);
        if (trail > shown + 0.002f) LiquidFill(Mathf.Clamp01(shown), trail, t, true);
        if (shown > 0.001f) LiquidFill(0f, Mathf.Min(1f, shown), t, false);
        // 金邊內側的細線（玻璃管的反光）
        float hs = HpSlant * 1.2f / HpH;
        Para(HpX + HpSlant - hs, HpY, HpW, 1.2f, hs, 0f, new Color(1f, 0.9f, 0.75f, 0.08f));
    }

    /// <summary>
    /// 液體：從 a 到 b（0~1）。切成橫的幾條，每條的右端是波浪（越靠上越亮）；
    /// 直的也切成一小段一小段，顏色隨時間緩緩流動（光澤往右流）。trail = 剛失去的暗紅色那段（邊緣不晃）
    /// </summary>
    void LiquidFill(float a, float b, float t, bool trail)
    {
        const int rows = 7;
        float wave = trail ? 0f : hpWave;
        for (int r = 0; r < rows; r++)
        {
            float v0 = (float)r / rows, v1 = (float)(r + 1) / rows;
            float y0 = HpY + v0 * HpH, y1 = HpY + v1 * HpH;
            // 這一列的左右端（斜角：上面往右偏 slant）
            float sl0 = HpSlant * (1f - v0), sl1 = HpSlant * (1f - v1);
            float xa0 = HpX + sl0 + HpW * a, xa1 = HpX + sl1 + HpW * a;
            float xb0 = HpX + sl0 + HpW * b + Edge(y0, t, wave, b), xb1 = HpX + sl1 + HpW * b + Edge(y1, t, wave, b);
            if (b >= 0.999f) { xb0 = HpX + sl0 + HpW; xb1 = HpX + sl1 + HpW; }
            if (a > 0.001f) { xa0 += Edge(y0, t, 0f, a) * 0.3f; xa1 += Edge(y1, t, 0f, a) * 0.3f; }
            xb0 = Mathf.Max(xb0, xa0); xb1 = Mathf.Max(xb1, xa1);

            if (trail)
            {
                Color c = HpTrail * new Color(1f, 1f, 1f, 0.75f + 0.25f * (1f - v0));
                Quad(new Vector2(xa0, y0), new Vector2(xb0, y0), new Vector2(xb1, y1), new Vector2(xa1, y1), c);
                continue;
            }
            // 直的切成幾段，每段的顏色：上亮下暗 + 往右流動的光澤 + 補血時發亮
            int cols = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(xb0 - xa0, xb1 - xa1) / 7f));
            for (int c = 0; c < cols; c++)
            {
                float u0 = (float)c / cols, u1 = (float)(c + 1) / cols;
                Vector2 p00 = new Vector2(Mathf.Lerp(xa0, xb0, u0), y0), p10 = new Vector2(Mathf.Lerp(xa0, xb0, u1), y0);
                Vector2 p01 = new Vector2(Mathf.Lerp(xa1, xb1, u0), y1), p11 = new Vector2(Mathf.Lerp(xa1, xb1, u1), y1);
                Quad4(p00, p10, p11, p01, LiquidColor(p00, v0, t), LiquidColor(p10, v0, t), LiquidColor(p11, v1, t), LiquidColor(p01, v1, t));
            }
            // 液面（右端）一條亮線
            Quad(new Vector2(xb0 - 1.6f, y0), new Vector2(xb0, y0), new Vector2(xb1, y1), new Vector2(xb1 - 1.6f, y1), new Color(0.85f, 0.5f, 0.42f, 0.28f + 0.25f * wave));
        }
    }

    /// <summary>液面邊緣的位移：受傷 / 補血時的大波浪（wave 0~1）+ 平常很小的起伏</summary>
    static float Edge(float y, float t, float wave, float level)
    {
        float big = wave * 5f * Mathf.Sin(y * 0.55f + t * 15f) * (0.6f + 0.4f * Mathf.Sin(t * 5.3f + level * 9f));
        float idle = 0.9f * Mathf.Sin(y * 0.42f + t * 3.1f) + 0.5f * Mathf.Sin(y * 0.9f - t * 4.7f);
        return big + idle;
    }

    Color LiquidColor(Vector2 p, float v, float t)
    {
        Color c = Color.Lerp(HpBright, HpDeep, Mathf.SmoothStep(0f, 1f, v));
        float flow = Mathf.Sin(p.x * 0.045f - t * 2.1f + p.y * 0.25f) * 0.5f + 0.5f;
        float sheen = Mathf.Pow(flow, 6f) * (1f - v) * 0.14f;
        c = Color.Lerp(c, new Color(0.8f, 0.5f, 0.45f), sheen);
        if (hpHealGlow > 0f) c = Color.Lerp(c, HpHeal, hpHealGlow * 0.3f);
        bool low = health != null && health.Health / Mathf.Max(1f, health.maxHealth) < 0.25f;
        if (low) c *= 0.85f + 0.15f * Mathf.Sin(t * 6f); // 血少時一明一暗（心跳）
        c.a = 1f;
        return c;
    }

    // ---- 茶屋甜點：護盾、額外精力 ----

    static readonly Color ShieldGold = new Color(1f, 0.84f, 0.48f);

    /// <summary>護盾：血條上一層淡金色（左邊起，長度 = 護盾 / 最大生命），上緣一條亮線、光澤往右流；擋下傷害時亮一下</summary>
    void DrawShield(float t)
    {
        if (health == null || SweetShield.Amount <= 0f) return;
        float len = Mathf.Clamp01(SweetShield.Amount / Mathf.Max(1f, health.maxHealth));
        float flash = 1f - Mathf.Clamp01((Time.unscaledTime - SweetShield.LastHitTime) / 0.4f);
        int cols = Mathf.Max(2, Mathf.CeilToInt(HpW * len / 6f));
        for (int c = 0; c < cols; c++)
        {
            float a = len * c / cols, b = len * (c + 1) / cols;
            float sheen = Mathf.Pow(Mathf.Sin((a * HpW) * 0.05f - t * 2.4f) * 0.5f + 0.5f, 4f);
            Color col = new Color(ShieldGold.r, ShieldGold.g, ShieldGold.b, 0.32f + 0.18f * sheen + 0.4f * flash);
            ParaPart(HpX, HpY, HpW, HpH, HpSlant, a, b, col);
        }
        float ts = HpSlant * 2f / HpH; // 上緣 2 px 的亮線（斜率跟血條一樣）
        ParaPart(HpX + HpSlant - ts, HpY, HpW, 2f, ts, 0f, len, new Color(1f, 0.95f, 0.8f, 0.7f + 0.3f * flash));
        // 右端一條亮邊
        ParaPart(HpX, HpY, HpW, HpH, HpSlant, Mathf.Max(0f, len - 1.5f / HpW), len, new Color(1f, 0.93f, 0.7f, 0.85f));
    }

    /// <summary>額外精力：精力條右邊多出來的一截（外框 + 金色的霧）</summary>
    void DrawBonusStamina(float t)
    {
        float bonus = stamina != null ? stamina.Bonus01 : 0f;
        if (bonus <= 0.002f) return;
        float x = StX + StW + 3f, w = StW * bonus;
        Para(x, StY, w, StH, StSlant, 2.5f, Frame);
        Para(x, StY, w, StH, StSlant, 1.2f, FrameInner);
        int cols = Mathf.Max(2, Mathf.CeilToInt(w / 6f));
        for (int c = 0; c < cols; c++)
        {
            float a = (float)c / cols, b = (float)(c + 1) / cols;
            float n = Mathf.PerlinNoise((x + w * a) * 0.05f - t * 0.7f, t * 0.4f);
            ParaPart(x, StY, w, StH, StSlant, a, b, new Color(ShieldGold.r, ShieldGold.g, ShieldGold.b, 0.55f + 0.35f * n));
        }
    }

    /// <summary>斜角平行四邊形的其中一段（a ~ b，0~1，沿著長邊）</summary>
    static void ParaPart(float x, float y, float w, float h, float slant, float a, float b, Color c)
    {
        Quad(new Vector2(x + slant + w * a, y), new Vector2(x + slant + w * b, y), new Vector2(x + w * b, y + h), new Vector2(x + w * a, y + h), c);
    }

    // ---- 命（血滴） ----

    Vector2 DropCenter(int i) => new Vector2(DropX + i * DropGap, HpY + HpH * 0.5f + 1.5f);

    /// <summary>
    /// 血條右邊一排血滴：還在的命是紅色、上面一點高光，輕輕地一顆接一顆起伏；
    /// 用掉的命是咖啡色的空殼（剛用掉時先脹大、變淡再縮回空殼）
    /// </summary>
    void DrawLives(float t)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        int max = Mathf.Clamp(gm.maxLives, 1, dropBreak.Length), lives = Mathf.Clamp(gm.Lives, 0, max);
        for (int i = 0; i < max; i++)
        {
            Vector2 c = DropCenter(i);
            bool full = i < lives;
            float br = dropBreak[i];
            if (full)
            {
                c.y += Mathf.Sin(t * 2.2f - i * 0.9f) * 0.6f; // 輕輕地起伏，一顆接一顆
                Drop(c, DropR + 1.1f, FrameInner);
                Drop(c, DropR, DropTop, DropBottom);
                Dot(c + new Vector2(-1.1f, -0.7f), 0.75f, new Color(0.9f, 0.6f, 0.55f, 0.45f));
            }
            else
            {
                Drop(c, DropR + 1.1f, Frame);
                Drop(c, DropR, new Color(0.15f, 0.09f, 0.06f, 0.95f));
                if (br > 0f && br < 1f) // 剛破掉：紅色的血滴脹大、變淡
                {
                    Color fade = DropTop;
                    fade.a = 1f - br;
                    Drop(c, DropR * (1f + 0.6f * br), fade, new Color(DropBottom.r, DropBottom.g, DropBottom.b, 1f - br));
                }
            }
        }
    }

    /// <summary>血滴：下面是圓、上面收成尖角（c = 圓心，r = 半徑），整顆往右斜 DropTilt 度；top / bottom 是上下的顏色</summary>
    static void Drop(Vector2 c, float r, Color col) => Drop(c, r, col, col);

    static void Drop(Vector2 c, float r, Color top, Color bottom)
    {
        float tilt = DropTilt * Mathf.Deg2Rad, cs = Mathf.Cos(tilt), sn = Mathf.Sin(tilt);
        Vector2 R(Vector2 p) { Vector2 d = p - c; return c + new Vector2(d.x * cs - d.y * sn, d.x * sn + d.y * cs); }
        Vector2 tip = R(c + new Vector2(0f, -r * 2.1f));
        const int n = 14;
        // 從尖角往下兩條切線碰到圓的地方（尖端在圓心上方 2.1r，切點在正上方往兩邊約 61 度）
        float a0 = -Mathf.PI * 0.5f - 1.07f, a1 = -Mathf.PI * 0.5f + 1.07f; // acos(1 / 2.1)
        // 圓的部分：從 a1 繞一大圈到 a0 + 2π
        float span = (a0 + Mathf.PI * 2f) - a1;
        for (int i = 0; i < n; i++)
        {
            float u0 = a1 + span * i / n, u1 = a1 + span * (i + 1) / n;
            Vector2 p0 = c + new Vector2(Mathf.Cos(u0), Mathf.Sin(u0)) * r, p1 = c + new Vector2(Mathf.Cos(u1), Mathf.Sin(u1)) * r;
            Color c0 = Color.Lerp(top, bottom, Mathf.InverseLerp(-r, r, p0.y - c.y)), c1 = Color.Lerp(top, bottom, Mathf.InverseLerp(-r, r, p1.y - c.y));
            p0 = R(p0); p1 = R(p1);
            GL.Color(Color.Lerp(top, bottom, 0.5f)); GL.Vertex3(c.x, c.y, 0f);
            GL.Color(c0); GL.Vertex3(p0.x, p0.y, 0f);
            GL.Color(c1); GL.Vertex3(p1.x, p1.y, 0f);
        }
        // 上面的尖角：尖端、兩個切點、圓心
        Vector2 q0 = R(c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r), q1 = R(c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r);
        GL.Color(top); GL.Vertex3(tip.x, tip.y, 0f);
        GL.Color(Color.Lerp(top, bottom, 0.2f)); GL.Vertex3(q1.x, q1.y, 0f);
        GL.Color(Color.Lerp(top, bottom, 0.2f)); GL.Vertex3(q0.x, q0.y, 0f);
        GL.Color(Color.Lerp(top, bottom, 0.2f)); GL.Vertex3(q0.x, q0.y, 0f);
        GL.Color(Color.Lerp(top, bottom, 0.2f)); GL.Vertex3(q1.x, q1.y, 0f);
        GL.Color(Color.Lerp(top, bottom, 0.5f)); GL.Vertex3(c.x, c.y, 0f);
    }

    // ---- 精力 ----

    void DrawStamina(float t)
    {
        float denied = DeniedFlash();
        Color frame = Color.Lerp(Frame, new Color(1f, 0.25f, 0.2f, 1f), denied * Mathf.Abs(Mathf.Sin(t * 25f)));
        Para(StX, StY, StW, StH, StSlant, 2.5f, frame);
        Para(StX, StY, StW, StH, StSlant, 1.2f, FrameInner);
        Para(StX, StY, StW, StH, StSlant, 0f, StEmpty);
        float level = Mathf.Clamp01(stShown);
        if (level < 0.002f) return;
        // 霧：切成小格，每格的濃淡用 Perlin noise 隨時間飄移；尾端 22 px 漸淡、而且被雜訊吃掉一點（煙散開）
        const int rows = 4;
        Color mist = MistColor(t);
        float end = StW * level;
        int cols = Mathf.Max(2, Mathf.CeilToInt(end / 6f));
        for (int r = 0; r < rows; r++)
        {
            float v0 = (float)r / rows, v1 = (float)(r + 1) / rows;
            float y0 = StY + v0 * StH, y1 = StY + v1 * StH;
            float sl0 = StSlant * (1f - v0), sl1 = StSlant * (1f - v1);
            for (int c = 0; c < cols; c++)
            {
                float u0 = (float)c / cols, u1 = (float)(c + 1) / cols;
                Vector2 p00 = new Vector2(StX + sl0 + end * u0, y0), p10 = new Vector2(StX + sl0 + end * u1, y0);
                Vector2 p01 = new Vector2(StX + sl1 + end * u0, y1), p11 = new Vector2(StX + sl1 + end * u1, y1);
                Quad4(p00, p10, p11, p01, MistAt(p00, v0, end, mist, t), MistAt(p10, v0, end, mist, t), MistAt(p11, v1, end, mist, t), MistAt(p01, v1, end, mist, t));
            }
        }
        // 尾端外面再飄一點點散開的煙
        for (int i = 0; i < 3; i++)
        {
            float ph = t * 0.8f + i * 2.1f;
            Vector2 c = new Vector2(StX + StSlant * 0.5f + end + 3f + 4f * Mathf.Sin(ph), StY + StH * 0.5f + 2.5f * Mathf.Sin(ph * 1.7f));
            Blob(c, 3.5f + 1.5f * Mathf.Sin(ph * 1.3f), new Color(mist.r, mist.g, mist.b, 0.16f));
        }
    }

    Color MistAt(Vector2 p, float v, float end, Color mist, float t)
    {
        float n = Mathf.PerlinNoise(p.x * 0.045f - t * 0.7f, p.y * 0.18f + t * 0.35f);
        float n2 = Mathf.PerlinNoise(p.x * 0.11f + t * 0.4f, p.y * 0.3f - t * 0.9f);
        float a = 0.42f + 0.38f * n + 0.15f * n2;
        float edgeV = 1f - Mathf.Abs(v - 0.5f) * 1.1f;                       // 上下邊淡一點（像一管氣）
        float fromEnd = end - (p.x - StX - StSlant * (1f - v));
        float tail = Mathf.Clamp01((fromEnd + (n2 - 0.5f) * 10f) / 22f);    // 尾端散開
        Color c = mist;
        c.a = Mathf.Clamp01(a * edgeV * Mathf.SmoothStep(0f, 1f, tail));
        return c;
    }

    float DeniedFlash()
    {
        if (stamina == null) return 0f;
        deniedField ??= typeof(PlayerStamina).GetField("deniedTime", BindingFlags.NonPublic | BindingFlags.Instance);
        if (deniedField == null) return 0f;
        float since = Time.time - (float)deniedField.GetValue(stamina);
        return 1f - Mathf.Clamp01(since / 0.5f);
    }

    // ---- 粒子 ----

    void DrawParticles(bool showStamina)
    {
        foreach (var p in particles)
        {
            float k = p.age / p.life;
            if (p.gas)
            {
                if (!showStamina) continue;
                float r = Mathf.Lerp(p.r0, p.r1, Mathf.Sqrt(k));
                Color c = p.color;
                c.a *= Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, k * 6f)) * (1f - k);
                Blob(p.pos, r, c);
            }
            else
            {
                if (p.clip && (p.pos.y < HpY + 1.5f || p.pos.y > HpY + HpH)) continue;
                Color c = p.color;
                c.a *= 1f - k * k;
                Dot(p.pos, Mathf.Lerp(p.r0, p.r1, k), c);
            }
        }
    }

    // ---------------------------------------------------------------- 形狀

    /// <summary>平行四邊形：左上 (x + slant, y)、右上 (x + slant + w, y)、右下 (x + w, y + h)、左下 (x, y + h)，往外擴 grow</summary>
    static void Para(float x, float y, float w, float h, float slant, float grow, Color c)
    {
        // 斜邊的斜率不變：斜邊每往上 1 px 往右 k px；外擴時上下各多 grow、斜邊再往外推 grow
        float k = slant / Mathf.Max(0.01f, h), g = grow;
        Vector2 tl = new Vector2(x + k * (h + g) - g, y - g), tr = new Vector2(x + w + k * (h + g) + g, y - g);
        Vector2 br = new Vector2(x + w - k * g + g, y + h + g), bl = new Vector2(x - k * g - g, y + h + g);
        Quad(tl, tr, br, bl, c);
    }

    /// <summary>實心圓點（血滴、氣泡）</summary>
    static void Dot(Vector2 c, float r, Color col)
    {
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Tri(c, c + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * r, c + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * r, col);
        }
    }

    /// <summary>邊緣漸淡的圓（煙）：中心有顏色、外圈透明</summary>
    static void Blob(Vector2 c, float r, Color col)
    {
        const int n = 14;
        Color edge = new Color(col.r, col.g, col.b, 0f);
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            GL.Color(col); GL.Vertex3(c.x, c.y, 0f);
            GL.Color(edge); GL.Vertex3(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r, 0f);
            GL.Color(edge); GL.Vertex3(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r, 0f);
        }
    }

    static void Tri(Vector2 a, Vector2 b, Vector2 c, Color col)
    {
        GL.Color(col);
        GL.Vertex3(a.x, a.y, 0f); GL.Vertex3(b.x, b.y, 0f); GL.Vertex3(c.x, c.y, 0f);
    }

    static void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color col)
    {
        Tri(a, b, c, col);
        Tri(a, c, d, col);
    }

    static void Quad4(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color ca, Color cb, Color cc, Color cd)
    {
        GL.Color(ca); GL.Vertex3(a.x, a.y, 0f); GL.Color(cb); GL.Vertex3(b.x, b.y, 0f); GL.Color(cc); GL.Vertex3(c.x, c.y, 0f);
        GL.Color(ca); GL.Vertex3(a.x, a.y, 0f); GL.Color(cc); GL.Vertex3(c.x, c.y, 0f); GL.Color(cd); GL.Vertex3(d.x, d.y, 0f);
    }
}
