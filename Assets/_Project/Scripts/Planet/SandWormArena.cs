using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 沙蟲低谷（第一關）：以這個物件為中心的矩形區域（中心 ± halfSize），切成四個方格
/// （1 = 東北、2 = 西北、3 = 西南、4 = 東南；以中心點的方位判斷，站到框外也一樣算在那一格裡，不能躲到外面）。
/// 玩家走進矩形範圍後開始：
///   1. 提示：四個象限依隨機順序一格一格亮起來（例如 3 → 1 → 4 → 2）
///   2. 攻擊：接著分成同樣次數的波，第 N 波只有「第 N 個亮起的格子」安全，其他格隨機冒出沙蟲（至少兩格），
///      玩家在被攻擊的格子裡就會被咬（站在那格的話沙蟲會直接從腳下冒出來）
///   撐過 rounds 輪之後沙蟲退回地底，獎勵的進階能力植物（rewardPlant，一開始隱藏）出現在低谷中央。
/// 玩家死亡或跑出範圍外 leaveMargin 公尺會重來；打過之後不會再觸發。
/// 沙蟲、地面的象限框和提示全部用程式產生，場景裡只要放這個元件和獎勵植物。
/// 沒有任何文字提示，只靠地面的亮光、噴沙和音效。
/// </summary>
public class SandWormArena : MonoBehaviour
{
    [Header("範圍")]
    [Tooltip("範圍的一半大小 (x, z)：預設 50 x 50，也就是中心往四個方向各 50 公尺，四格各 50 x 50")]
    public Vector2 halfSize = new Vector2(50f, 50f);
    [Tooltip("戰鬥中跑出範圍超過這個距離就重來")]
    public float leaveMargin = 20f;

    [Header("節奏")]
    public int rounds = 4;
    [Tooltip("每輪提示幾格（= 幾波攻擊）")]
    public int stepsPerRound = 4;
    [Tooltip("最後一輪提示幾格（= 幾次衝擊）")]
    public int lastRoundSteps = 6;
    [Tooltip("每格提示亮多久：第一輪 → 最後一輪（越來越快）")]
    public Vector2 hintTime = new Vector2(0.9f, 0.55f);
    public float hintGap = 0.25f;
    [Tooltip("提示結束到第一波之間的準備時間")]
    public float prepareTime = 1.5f;
    [Tooltip("每一波的倒數（期間不會提示哪裡危險）")]
    public float waveWarnTime = 1.3f;
    [Tooltip("沙蟲冒出前，地面先震動冒沙的時間")]
    public float rumbleLead = 0.4f;
    [Tooltip("沙蟲冒出後到下一波倒數開始")]
    public float waveInterval = 1.1f;
    public float restBetweenRounds = 2.5f;

    [Header("攻擊")]
    public float damage = 25f;
    public float knockback = 6f;
    [Tooltip("沙蟲冒出多高 (m)，會再乘上 attackWormScale")]
    public float wormHeight = 7f;
    [Tooltip("攻擊時冒出來的沙蟲大小倍率（身體粗細和高度）")]
    public float attackWormScale = 2f;

    [Header("獎勵")]
    [Tooltip("撐過之後出現的進階能力植物（一開始設成隱藏）")]
    public GameObject rewardPlant;

    [Header("顏色")]
    public Color lineColor = new Color(1f, 0.85f, 0.5f, 0.55f);
    public Color hintColor = new Color(1f, 0.85f, 0.2f, 0.7f);
    public Color safeColor = new Color(0.3f, 1f, 0.45f, 0.55f);
    public Color dangerColor = new Color(1f, 0.2f, 0.1f, 0.6f);

    enum State { Idle, Fighting, Done }

    State state = State.Idle;
    PlayerHealth player;
    Coroutine fight;
    readonly MeshRenderer[] quads = new MeshRenderer[4];
    readonly Color[] quadColor = new Color[4];
    readonly List<LineRenderer> lines = new List<LineRenderer>();
    readonly List<GameObject> worms = new List<GameObject>();
    GameObject visuals;
    MaterialPropertyBlock mpb;

    int hintQuadrant = -1;

    static Material glowMat, sandMat, wormMat, mouthMat, toothMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        glowMat = sandMat = wormMat = mouthMat = toothMat = null;
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) player.OnDied += OnPlayerDied;
        if (rewardPlant != null) rewardPlant.SetActive(false);
    }

    void OnDestroy()
    {
        if (player != null) player.OnDied -= OnPlayerDied;
    }

    void Update()
    {
        if (player == null)
        {
            player = PlayerHealth.Find();
            if (player != null) player.OnDied += OnPlayerDied;
            return;
        }
        if (state == State.Idle && !player.IsDead && Inside(player.transform.position, 0f)) BeginFight();
        else if (state == State.Fighting && !Inside(player.transform.position, leaveMargin)) ResetFight();
    }

    void OnPlayerDied() { if (state == State.Fighting) ResetFight(); }

    // ---------------- 戰鬥流程 ----------------

    void BeginFight()
    {
        state = State.Fighting;
        BuildVisuals();
        fight = StartCoroutine(introShown ? FightRoutine() : IntroThenFight());
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.4f, 0.05f, 0.3f);
    }

    bool introShown;

    /// <summary>第一次進來：一隻大沙蟲從中央冒出來說話（劇情對話），講完沉回地底才開始考驗</summary>
    IEnumerator IntroThenFight()
    {
        introShown = true;
        bool talking = true;
        Vector3 at = Ground(transform.position);
        SandBurst(at, 60, 5f, 1f, 3f);
        var worm = SpawnWorm(at, () => talking, 1.6f);
        if (player != null)
        {
            Vector3 d = Flat(player.transform.position - at);
            if (d.sqrMagnitude > 0.01f) worm.transform.rotation = Quaternion.LookRotation(d);
        }
        worms.Add(worm);
        ThirdPersonCamera.ShakeMain(0.2f, 0.8f);
        yield return new WaitForSeconds(1.2f);

        bool done = false;
        PlanetStory.PlayWormIntro(worm.transform, () => done = true);
        while (!done) yield return null;
        talking = false;
        yield return new WaitForSeconds(1.5f);
        yield return FightRoutine();
    }

    void ResetFight()
    {
        if (fight != null) StopCoroutine(fight);
        fight = null;
        foreach (var w in worms) if (w != null) Destroy(w);
        worms.Clear();
        if (visuals != null) Destroy(visuals);
        visuals = null;
        lines.Clear();
        hintQuadrant = -1;
        state = State.Idle;
    }

    IEnumerator FightRoutine()
    {
        for (int round = 0; round < rounds; round++)
        {
            float t = rounds > 1 ? round / (float)(rounds - 1) : 0f;
            float hint = Mathf.Lerp(hintTime.x, hintTime.y, t);

            // 1. 提示：隨機順序（相鄰兩步不會是同一格）
            var order = new int[round == rounds - 1 ? lastRoundSteps : stepsPerRound];
            for (int i = 0; i < order.Length; i++)
            {
                int q;
                do q = Random.Range(0, 4); while (i > 0 && q == order[i - 1]);
                order[i] = q;
            }

            yield return new WaitForSeconds(0.8f);
            for (int i = 0; i < order.Length; i++)
            {
                hintQuadrant = order[i];
                SetQuadrant(order[i], hintColor);
                GameAudio.Play("SFX/RPG/metalClick", QuadrantCenter(order[i]), 0.9f, 0.8f + i * 0.15f, 0f, 0.3f);
                yield return new WaitForSeconds(hint);
                SetQuadrant(order[i], Color.clear);
                hintQuadrant = -1;
                yield return new WaitForSeconds(hintGap);
            }

            yield return new WaitForSeconds(prepareTime);

            // 2. 攻擊：第 i 波只有 order[i] 安全
            for (int i = 0; i < order.Length; i++)
            {
                yield return new WaitForSeconds(waveWarnTime - rumbleLead);

                var attacked = PickAttacked(order[i]);
                var points = new List<Vector3>();
                foreach (int q in attacked)
                {
                    Vector3 p = EruptionPoint(q);
                    points.Add(p);
                    SandBurst(p, 25, 3f, 0.6f, 1.2f);
                }
                ThirdPersonCamera.ShakeMain(0.06f, rumbleLead);
                yield return new WaitForSeconds(rumbleLead);

                foreach (var p in points) worms.Add(SpawnWorm(p, null, attackWormScale));
                foreach (int q in attacked) SetQuadrant(q, dangerColor);
                SetQuadrant(order[i], safeColor);
                HitPlayerIfIn(attacked);
                ThirdPersonCamera.ShakeMain(0.25f, 0.35f);
                GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.5f, 0.05f, 0.3f);

                yield return new WaitForSeconds(0.6f);
                for (int q = 0; q < 4; q++) SetQuadrant(q, Color.clear);
                yield return new WaitForSeconds(waveInterval - 0.6f);
            }

            if (round < rounds - 1) yield return new WaitForSeconds(restBetweenRounds);
        }

        Win();
    }

    /// <summary>除了安全格以外的格子，隨機挑至少兩格冒出沙蟲</summary>
    List<int> PickAttacked(int safe)
    {
        var others = new List<int>();
        for (int q = 0; q < 4; q++) if (q != safe) others.Add(q);
        var result = new List<int>();
        foreach (int q in others) if (Random.value < 0.75f) result.Add(q);
        while (result.Count < 2)
        {
            int q = others[Random.Range(0, others.Count)];
            if (!result.Contains(q)) result.Add(q);
        }
        // 玩家站的格子如果該被攻擊，一定會被攻擊（隨機只決定其他沒人的格子）
        int playerQ = player != null ? QuadrantOf(player.transform.position) : -1;
        if (playerQ >= 0 && playerQ != safe && !result.Contains(playerQ)) result.Add(playerQ);
        return result;
    }

    void HitPlayerIfIn(List<int> attacked)
    {
        if (player == null || player.IsDead) return;
        if (!attacked.Contains(QuadrantOf(player.transform.position))) return;
        Vector3 away = Flat(player.transform.position - transform.position).normalized;
        player.TakeDamage(damage, (away + Vector3.up) * knockback, false, "被沙蟲吞下了");
    }

    void Win()
    {
        fight = null;
        state = State.Done;
        if (visuals != null) Destroy(visuals);
        visuals = null;
        SandBurst(Ground(transform.position), 60, 4f, 1f, 2f);
        if (rewardPlant != null)
        {
            rewardPlant.transform.position = Ground(transform.position);
            rewardPlant.SetActive(true);
        }
        GameAudio.Play("SFX/RPG/handleCoins", transform.position, 1f, 1f, 0f, 0.3f);
    }

    // ---------------- 象限 ----------------

    /// <summary>點在哪個象限（0 = 東北、1 = 西北、2 = 西南、3 = 東南），用角度判斷</summary>
    int QuadrantOf(Vector3 p)
    {
        Vector3 d = p - transform.position;
        if (d.x >= 0f) return d.z >= 0f ? 0 : 3;
        return d.z >= 0f ? 1 : 2;
    }

    /// <summary>點在不在範圍內（margin = 往外多算幾公尺）</summary>
    bool Inside(Vector3 p, float margin)
    {
        Vector3 d = p - transform.position;
        return Mathf.Abs(d.x) <= halfSize.x + margin && Mathf.Abs(d.z) <= halfSize.y + margin;
    }

    /// <summary>格子裡的一點：u, v = 0（靠中心）～ 1（靠外框）</summary>
    Vector3 QuadrantPoint(int q, float u, float v)
    {
        float sx = q == 0 || q == 3 ? 1f : -1f, sz = q <= 1 ? 1f : -1f;
        return transform.position + new Vector3(sx * u * halfSize.x, 0f, sz * v * halfSize.y);
    }

    Vector3 QuadrantCenter(int q) => Ground(QuadrantPoint(q, 0.5f, 0.5f));

    /// <summary>沙蟲冒出的位置：玩家在這格就從玩家腳下，不然是這格裡的隨機一點</summary>
    Vector3 EruptionPoint(int q)
    {
        if (player != null && !player.IsDead && QuadrantOf(player.transform.position) == q)
            return Ground(player.transform.position);
        return Ground(QuadrantPoint(q, Random.Range(0.15f, 0.9f), Random.Range(0.15f, 0.9f)));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static Vector3 Ground(Vector3 p)
    {
        var t = Terrain.activeTerrain;
        if (t != null) p.y = t.SampleHeight(p) + t.transform.position.y;
        return p;
    }

    // ---------------- 外觀 ----------------

    static void EnsureMaterials()
    {
        if (glowMat == null) glowMat = BossRageEffect.CreateParticleMaterial(Texture2D.whiteTexture, true);
        if (sandMat == null) sandMat = BossRageEffect.CreateParticleMaterial(BossRageEffect.SoftDot(), false);
        if (wormMat == null) wormMat = LitMaterial(new Color(0.62f, 0.48f, 0.3f));
        if (mouthMat == null) mouthMat = LitMaterial(new Color(0.25f, 0.05f, 0.06f));
        if (toothMat == null) toothMat = LitMaterial(new Color(0.95f, 0.9f, 0.78f));
    }

    static Material LitMaterial(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    /// <summary>地上的方框、十字分隔線和四塊可以亮起來的方格</summary>
    void BuildVisuals()
    {
        EnsureMaterials();
        mpb ??= new MaterialPropertyBlock();
        visuals = new GameObject("SandWormArenaVisuals");
        visuals.transform.SetParent(transform, false);

        // 外框：沿著四個邊取點貼地
        var outline = new List<Vector3>();
        Vector3 c = transform.position;
        Vector3[] corners =
        {
            c + new Vector3(halfSize.x, 0f, halfSize.y), c + new Vector3(-halfSize.x, 0f, halfSize.y),
            c + new Vector3(-halfSize.x, 0f, -halfSize.y), c + new Vector3(halfSize.x, 0f, -halfSize.y),
        };
        for (int e = 0; e < 4; e++)
            for (int i = 0; i < 24; i++)
                outline.Add(Ground(Vector3.Lerp(corners[e], corners[(e + 1) % 4], i / 24f)) + Vector3.up * 0.15f);
        AddLine(outline.ToArray(), true, 0.3f);
        for (int axis = 0; axis < 2; axis++)
        {
            Vector3 half = axis == 0 ? new Vector3(halfSize.x, 0f, 0f) : new Vector3(0f, 0f, halfSize.y);
            var pts = new Vector3[33];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = Ground(Vector3.Lerp(c - half, c + half, i / 32f)) + Vector3.up * 0.15f;
            AddLine(pts, false, 0.25f);
        }

        for (int q = 0; q < 4; q++)
        {
            var go = new GameObject($"Quadrant{q + 1}");
            go.transform.SetParent(visuals.transform, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = BuildSector(q);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glowMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quads[q] = mr;
            SetQuadrant(q, Color.clear);
        }
    }

    void AddLine(Vector3[] pts, bool loop, float width)
    {
        var go = new GameObject("Line");
        go.transform.SetParent(visuals.transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = loop;
        lr.widthMultiplier = width;
        lr.sharedMaterial = glowMat;
        lr.startColor = lr.endColor = lineColor;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        lines.Add(lr);
    }

    /// <summary>一格方格的網格，頂點貼著地形</summary>
    Mesh BuildSector(int q)
    {
        const int n = 16;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int j = 0; j <= n; j++)
            for (int i = 0; i <= n; i++)
                verts.Add(Ground(QuadrantPoint(q, i / (float)n, j / (float)n)) + Vector3.up * 0.12f);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, d = a + n + 1, e = d + 1;
                // 雙面都畫（方格翻轉的象限三角形方向會相反）
                tris.AddRange(new[] { a, b, d, b, e, d, a, d, b, b, d, e });
            }
        var mesh = new Mesh { name = $"WormQuadrant{q + 1}" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        var colors = new Color[verts.Count];
        for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
        mesh.colors = colors;
        mesh.RecalculateBounds();
        return mesh;
    }

    void SetQuadrant(int q, Color c)
    {
        quadColor[q] = c;
        var mr = quads[q];
        if (mr == null) return;
        mr.enabled = c.a > 0.001f;
        mr.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        mr.SetPropertyBlock(mpb);
    }

    void LateUpdate()
    {
        // 提示中的格子：亮度一閃一閃
        if (hintQuadrant >= 0 && quads[hintQuadrant] != null)
        {
            Color c = hintColor;
            c.a *= 0.65f + 0.35f * Mathf.Sin(Time.time * 14f);
            var mr = quads[hintQuadrant];
            mr.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", c);
            mr.SetPropertyBlock(mpb);
        }
    }

    /// <summary>從地面往上噴的沙</summary>
    static void SandBurst(Vector3 pos, int count, float speed, float life, float radius)
    {
        EnsureMaterials();
        var go = new GameObject("WormSand");
        go.transform.position = pos + Vector3.up * 0.1f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.5f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.7f, 0.45f, 0.85f), new Color(0.7f, 0.55f, 0.35f, 0.6f));
        main.gravityModifier = 0.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = radius;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.sharedMaterial = sandMat;
        ps.Play();
        Destroy(go, life + 1f);
    }

    /// <summary>程式產生的沙蟲：一節一節的身體，頭上一圈牙</summary>
    /// <param name="holdWhile">不是 null 的話：冒出來之後一直停著，直到它回傳 false 才沉下去（說話用，暫停中也會動）</param>
    GameObject SpawnWorm(Vector3 at, System.Func<bool> holdWhile = null, float scale = 1f)
    {
        EnsureMaterials();
        var root = new GameObject("SandWorm");
        root.transform.position = at;
        const int n = 10;
        var segs = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(root.transform, false);
            float size = Mathf.Lerp(2.1f, 1.5f, i / (float)(n - 1)) * scale;
            s.transform.localScale = new Vector3(size, size * 0.8f, size);
            s.GetComponent<Renderer>().sharedMaterial = wormMat;
            segs[i] = s.transform;
        }
        // 頭（segs[0]）上方：張開的嘴和一圈牙
        var mouth = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(mouth.GetComponent<Collider>());
        mouth.transform.SetParent(segs[0], false);
        mouth.transform.localPosition = Vector3.up * 0.42f;
        mouth.transform.localScale = new Vector3(0.75f, 0.05f, 0.75f);
        mouth.GetComponent<Renderer>().sharedMaterial = mouthMat;
        for (int k = 0; k < 8; k++)
        {
            var tooth = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(tooth.GetComponent<Collider>());
            tooth.transform.SetParent(segs[0], false);
            float a = k / 8f * Mathf.PI * 2f;
            Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            tooth.transform.localPosition = dir * 0.36f + Vector3.up * 0.5f;
            tooth.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (Vector3.up - dir * 0.5f).normalized);
            tooth.transform.localScale = new Vector3(0.09f, 0.16f, 0.09f);
            tooth.GetComponent<Renderer>().sharedMaterial = toothMat;
        }
        StartCoroutine(AnimateWorm(root, segs, at, holdWhile, scale));
        return root;
    }

    IEnumerator AnimateWorm(GameObject root, Transform[] segs, Vector3 at, System.Func<bool> holdWhile, float scale)
    {
        bool talking = holdWhile != null;
        float rise = talking ? 1f : 0.3f, hold = 0.6f, sink = talking ? 1.2f : 0.7f;
        float height = wormHeight * scale;
        float spacing = height / segs.Length;
        float phase = Random.value * 10f;
        float t = 0f;
        while (t < rise + hold + sink && root != null)
        {
            t += talking ? Time.unscaledDeltaTime : Time.deltaTime;
            if (talking && t > rise && holdWhile()) t = rise; // 說話中：停在最高點
            float h = t < rise ? Mathf.SmoothStep(0f, 1f, t / rise)
                    : t < rise + hold ? 1f
                    : 1f - Mathf.SmoothStep(0f, 1f, (t - rise - hold) / sink);
            float head = h * (height + 1f) - 1f;
            for (int i = 0; i < segs.Length; i++)
            {
                float y = head - i * spacing;
                float k = i / (float)segs.Length;
                float sway = Mathf.Sin(Time.unscaledTime * 4f + phase + i * 0.6f) * 0.35f * scale * (1f - k);
                segs[i].position = at + new Vector3(sway, y, sway * 0.6f);
                segs[i].gameObject.SetActive(y > -1.2f);
            }
            yield return null;
        }
        SandBurst(at, 15, 2f, 0.6f, 1f);
        if (root != null) Destroy(root);
        worms.Remove(root);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 c = transform.position;
        Gizmos.color = new Color(1f, 0.8f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(c, new Vector3(halfSize.x * 2f, 0.5f, halfSize.y * 2f));
        Gizmos.DrawLine(c - Vector3.right * halfSize.x, c + Vector3.right * halfSize.x);
        Gizmos.DrawLine(c - Vector3.forward * halfSize.y, c + Vector3.forward * halfSize.y);
        Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.4f);
        Gizmos.DrawWireCube(c, new Vector3((halfSize.x + leaveMargin) * 2f, 0.5f, (halfSize.y + leaveMargin) * 2f));
    }
}
