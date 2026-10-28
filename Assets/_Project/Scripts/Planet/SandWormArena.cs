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
///   3. 每一波沙蟲竄出時，安全的那格會露出沙蟲尾巴，打中尾巴 tailHits 次才結束，沒打滿就一直循環下去；
///      剩最後一下時，象限的分隔線會轉 lastHitAngle 度，打亂玩家記住的位置。
///   結束後沙蟲退回地底，獎勵的進階能力植物（rewardPlant，一開始隱藏）出現在低谷中央。
/// 戰鬥中跑出範圍，沙蟲會把玩家叼回場內；玩家死亡則整場重來，而且要先離開範圍、再走進來才會重新觸發。
/// 打過之後不會再觸發。
/// 沙蟲、地面的象限框和提示全部用程式產生，場景裡只要放這個元件和獎勵植物。
/// 沒有任何文字提示，只靠地面的亮光、噴沙和音效。
/// </summary>
public class SandWormArena : MonoBehaviour
{
    [Header("範圍")]
    [Tooltip("範圍的一半大小 (x, z)：預設 50 x 50，也就是中心往四個方向各 50 公尺，四格各 50 x 50")]
    public Vector2 halfSize = new Vector2(50f, 50f);
    [Tooltip("戰鬥中跑出範圍超過這個距離，沙蟲就把玩家叼回來")]
    public float escapeMargin = 1.5f;

    [Header("節奏")]
    public int rounds = 4;
    [Tooltip("每輪提示幾格（= 幾波攻擊）")]
    public int stepsPerRound = 4;
    [Tooltip("第 rounds 輪之後（還沒打完尾巴就一直循環）每輪提示幾格")]
    public int lastRoundSteps = 6;
    [Tooltip("每格提示亮多久：第一輪 → 最後一輪（越來越快）")]
    public Vector2 hintTime = new Vector2(0.9f, 0.55f);
    public float hintGap = 0.25f;
    [Tooltip("提示結束到第一波之間的準備時間")]
    public float prepareTime = 1.5f;
    [Tooltip("每一波的倒數（期間不會提示哪裡危險）")]
    public float waveWarnTime = 2f;
    [Tooltip("沙蟲冒出前，地面先震動冒沙的時間")]
    public float rumbleLead = 0.4f;
    [Tooltip("沙蟲冒出後到下一波倒數開始")]
    public float waveInterval = 1.7f;
    public float restBetweenRounds = 2.5f;

    [Header("攻擊")]
    public float damage = 25f;
    public float knockback = 6f;
    [Tooltip("沙蟲冒出多高 (m)，會再乘上 attackWormScale")]
    public float wormHeight = 7f;
    [Tooltip("攻擊時冒出來的沙蟲大小倍率（身體粗細和高度）")]
    public float attackWormScale = 2f;

    [Header("音樂")]
    [Tooltip("戰鬥音樂（不填 = Resources/Audio/Music/bgm_battle_sandworm）。開戰時換上，打完 / 死掉重來時換回關卡音樂")]
    public AudioClip battleMusic;
    [Range(0f, 1f)] public float battleMusicVolume = 0.5f;

    [Header("尾巴")]
    [Tooltip("要打中尾巴幾次才結束（沒打滿會一直循環）")]
    public int tailHits = 5;
    [Tooltip("尾巴露出來多久 (s)")]
    public float tailTime = 2.4f;
    [Tooltip("剩最後一下時，象限分隔線轉幾度")]
    public float lastHitAngle = 45f;

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
    int tailHP;
    // 每次開始 / 重來都加一；舊的流程發現編號不一樣就自己結束
    int fightId;
    bool showBar;
    // 象限分隔線的角度（0 = 東西 / 南北，剩最後一下時會轉）
    float layoutAngle;
    // 死在場上：要先離開範圍再走進來才會重新開始
    bool needExit;
    Coroutine drag;
    readonly List<SandWormTail> tails = new List<SandWormTail>();
    readonly LineRenderer[] dividers = new LineRenderer[2];

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
        // 不只靠 OnDied 事件：同一個事件的其他訂閱者如果丟例外，這裡就收不到，所以每幀也自己檢查一次
        if (state == State.Fighting && player.IsDead) OnPlayerDied();

        Vector3 pos = player.transform.position;
        if (state == State.Idle)
        {
            if (player.IsDead) return;
            if (needExit) { if (!Inside(pos, 0f)) needExit = false; }
            else if (Inside(pos, 0f)) BeginFight();
        }
        else if (state == State.Fighting && drag == null && !player.IsDead && !Inside(pos, escapeMargin))
            drag = StartCoroutine(DragBack());
    }

    void OnPlayerDied()
    {
        if (state != State.Fighting) return;
        ResetFight();
        needExit = true;
    }

    // ---------------- 戰鬥流程 ----------------

    void BeginFight()
    {
        state = State.Fighting;
        tailHP = tailHits;
        layoutAngle = 0f;
        BuildVisuals();
        fight = StartCoroutine(introShown ? FightRoutine(0) : IntroThenFight());
        BattleMusic.Begin(battleMusic, battleMusicVolume, 1.5f, "Music/bgm_battle_sandworm");
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
        // 不要用 yield return FightRoutine()：巢狀的協程在外層被 StopCoroutine 時不會跟著停（死掉重生後沙蟲還會繼續攻擊）
        fight = StartCoroutine(FightRoutine(0));
    }

    void ResetFight()
    {
        if (fight != null) StopCoroutine(fight);
        fight = null;
        foreach (var w in worms) if (w != null) Destroy(w);
        worms.Clear();
        ClearTails(true);
        if (drag != null) { StopCoroutine(drag); drag = null; ReleasePlayer(); }
        if (visuals != null) Destroy(visuals);
        visuals = null;
        lines.Clear();
        hintQuadrant = -1;
        showBar = false;
        layoutAngle = 0f;
        state = State.Idle;
        fightId++;
        BattleMusic.End();
    }

    /// <summary>一輪接一輪，直到尾巴被打滿 tailHits 下（OnTailHit 會結束這個流程）</summary>
    IEnumerator FightRoutine(int startRound)
    {
        int id = fightId;
        showBar = true;
        for (int round = startRound; ; round++)
        {
            float t = rounds > 1 ? Mathf.Clamp01(round / (float)(rounds - 1)) : 1f;
            float hint = Mathf.Lerp(hintTime.x, hintTime.y, t);

            // 1. 提示：隨機順序（相鄰兩步不會是同一格）
            var order = new int[round >= rounds - 1 ? lastRoundSteps : stepsPerRound];
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
                if (id != fightId || state != State.Fighting) yield break;

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
                if (id != fightId || state != State.Fighting) yield break;

                foreach (var p in points) worms.Add(SpawnWorm(p, null, attackWormScale));
                foreach (int q in attacked) SetQuadrant(q, dangerColor);
                SetQuadrant(order[i], safeColor);
                SpawnTail(order[i]);
                HitPlayerIfIn(attacked);
                ThirdPersonCamera.ShakeMain(0.25f, 0.35f);
                GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.5f, 0.05f, 0.3f);

                yield return new WaitForSeconds(0.6f);
                for (int q = 0; q < 4; q++) SetQuadrant(q, Color.clear);
                yield return new WaitForSeconds(waveInterval - 0.6f);
            }

            yield return new WaitForSeconds(restBetweenRounds);
        }
    }

    // ---------------- 尾巴 ----------------

    /// <summary>安全格露出尾巴：玩家站在那格就出現在玩家附近，不然是那格裡的隨機一點</summary>
    void SpawnTail(int q)
    {
        Vector3 at = RandomPointIn(q);
        if (player != null && QuadrantOf(player.transform.position) == q)
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(3.5f, 6f);
                Vector3 p = player.transform.position + new Vector3(r.x, 0f, r.y);
                if (QuadrantOf(p) == q && Inside(p, -1f)) { at = p; break; }
            }
        at = Ground(at);
        SandBurst(at, 30, 3f, 0.7f, 1.2f);

        EnsureMaterials();
        var root = new GameObject("SandWormTail");
        root.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        const int n = 7;
        float scale = attackWormScale;
        var segs = new Transform[n];
        for (int i = 0; i < n; i++)
        {
            var s = GameObject.CreatePrimitive(i == n - 1 ? PrimitiveType.Capsule : PrimitiveType.Sphere);
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(root.transform, false);
            float size = Mathf.Lerp(1.5f, 0.45f, i / (float)(n - 1)) * scale;
            s.transform.localScale = i == n - 1 ? new Vector3(size * 0.6f, size, size * 0.6f) : new Vector3(size, size * 0.85f, size);
            s.GetComponent<Renderer>().sharedMaterial = i == n - 1 ? toothMat : wormMat;
            s.SetActive(false);
            segs[i] = s.transform;
        }
        // 碰撞（Trigger）：讓玩家的攻擊打得到
        var col = root.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.radius = 0.9f * scale;
        col.height = 3.2f * scale;
        col.center = Vector3.up * 1.4f * scale;
        var tail = root.AddComponent<SandWormTail>();
        tail.arena = this;
        tails.Add(tail);
        StartCoroutine(AnimateTail(tail, segs, scale));
    }

    IEnumerator AnimateTail(SandWormTail tail, Transform[] segs, float scale)
    {
        const float rise = 0.35f, sink = 0.45f;
        float height = 3.2f * scale, phase = Random.value * 10f;
        float t = 0f, h = 0f;
        bool hit = false;
        Vector3 at = tail.transform.position;
        while (tail != null)
        {
            t += Time.deltaTime;
            if (tail.IsDead) hit = true;
            bool sinking = hit || t > rise + tailTime;
            if (!sinking) h = Mathf.SmoothStep(0f, 1f, t / rise);
            else
            {
                h -= Time.deltaTime / sink;
                if (h <= 0f) break;
            }
            // 被打中或時間到：甩得更厲害
            float wiggle = sinking ? 2.5f : 1f;
            for (int i = 0; i < segs.Length; i++)
            {
                float k = i / (float)(segs.Length - 1);
                float y = h * height * k - (1f - h) * 1.5f;
                float sway = Mathf.Sin(Time.time * 5f * wiggle + phase + i * 0.8f) * 0.5f * scale * k * k * wiggle;
                segs[i].localPosition = new Vector3(sway, y, k * k * 0.8f * scale);
                segs[i].gameObject.SetActive(y > -0.8f * scale && h > 0.02f);
            }
            yield return null;
        }
        if (tail != null)
        {
            SandBurst(at, 12, 2f, 0.5f, 0.8f);
            tails.Remove(tail);
            Destroy(tail.gameObject);
        }
    }

    /// <summary>destroy = false：讓尾巴自己縮回去；true：直接刪掉（重來的時候）</summary>
    void ClearTails(bool destroy)
    {
        foreach (var t in tails)
            if (t != null)
            {
                t.IsDead = true;
                if (destroy) Destroy(t.gameObject);
            }
        if (destroy) tails.Clear();
    }

    /// <summary>尾巴被打中：扣一下；打滿就結束，剩最後一下時轉動象限</summary>
    internal void OnTailHit(SandWormTail tail, Vector3 point)
    {
        if (state != State.Fighting) return;
        tailHP--;
        SandBurst(point, 35, 5f, 0.8f, 1f);
        ThirdPersonCamera.ShakeMain(0.18f, 0.3f);
        GameAudio.Play("SFX/RPG/dropLeather", point, 1f, 1.4f, 0.05f, 0.3f);
        if (tailHP <= 0) { Win(); return; }
        if (tailHP == 1 && Mathf.Approximately(layoutAngle, 0f) && lastHitAngle != 0f)
        {
            if (fight != null) StopCoroutine(fight);
            fight = StartCoroutine(Relayout());
        }
    }

    /// <summary>剩最後一下：地面的分隔線轉動 lastHitAngle 度，之後用新的四格重新開始最難的那一輪</summary>
    IEnumerator Relayout()
    {
        hintQuadrant = -1;
        ClearTails(false);
        ThirdPersonCamera.ShakeMain(0.15f, 1.6f);
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.35f, 0.05f, 0.3f);
        SandBurst(Ground(transform.position), 60, 5f, 1f, 4f);
        yield return new WaitForSeconds(0.6f);

        float from = layoutAngle, to = layoutAngle + lastHitAngle;
        const float dur = 1.6f;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            layoutAngle = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / dur));
            RefreshLayout();
            for (int q = 0; q < 4; q++)
            {
                Color c = hintColor;
                c.a *= q % 2 == 0 ? 0.45f : 0.2f;
                SetQuadrant(q, c);
            }
            yield return null;
        }
        layoutAngle = to;
        RefreshLayout();
        for (int q = 0; q < 4; q++) SetQuadrant(q, Color.clear);
        yield return new WaitForSeconds(1f);
        fight = StartCoroutine(FightRoutine(Mathf.Max(0, rounds - 1)));
    }

    // ---------------- 跑出範圍：叼回來 ----------------

    IEnumerator DragBack()
    {
        var cc = player.GetComponent<CharacterController>();
        var ctrl = player.GetComponent<ThirdPersonController>();
        Vector3 start = player.transform.position;
        Vector3 ground = Ground(start);

        // 沙蟲從玩家腳下竄出、把玩家叼起來
        SandBurst(ground, 40, 4f, 0.8f, 2f);
        ThirdPersonCamera.ShakeMain(0.25f, 0.4f);
        GameAudio.Play("SFX/RPG/dropLeather", ground, 1f, 0.6f, 0.05f, 0.3f);
        worms.Add(SpawnWorm(ground, null, attackWormScale));
        if (cc != null) cc.enabled = false; // 關掉 CharacterController：控制器會停止移動，可以直接設定位置
        float lift = wormHeight * attackWormScale * 0.8f;
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            if (player.IsDead) { ReleasePlayer(); drag = null; yield break; }
            player.transform.position = start + Vector3.up * lift * Mathf.SmoothStep(0f, 1f, t / 0.35f);
            yield return null;
        }

        // 甩回場內：往中央方向，落在中心到外框約一半的地方
        Vector3 c = transform.position;
        Vector3 local = Flat(start - c);
        Vector3 target = Ground(c + new Vector3(Mathf.Clamp(local.x, -halfSize.x, halfSize.x) * 0.55f, 0f,
                                                Mathf.Clamp(local.z, -halfSize.y, halfSize.y) * 0.55f));
        Vector3 from = start + Vector3.up * lift;
        float dur = Mathf.Clamp(Vector3.Distance(from, target) / 30f, 0.7f, 1.4f);
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            if (player.IsDead) { ReleasePlayer(); drag = null; yield break; }
            float k = t / dur;
            player.transform.position = Vector3.Lerp(from, target, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 8f;
            yield return null;
        }
        player.transform.position = target + Vector3.up * 0.2f;
        SandBurst(target, 30, 3f, 0.6f, 1.5f);
        ThirdPersonCamera.ShakeMain(0.2f, 0.3f);
        ReleasePlayer();
        if (ctrl != null) ctrl.Stun(0.4f);
        drag = null;
    }

    void ReleasePlayer()
    {
        if (player == null) return;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = true;
        var ctrl = player.GetComponent<ThirdPersonController>();
        if (ctrl != null) ctrl.ResetMotion();
    }

    // ---------------- 沙蟲血量（沒有文字，只有一格一格） ----------------

    void OnGUI()
    {
        if (state != State.Fighting || !showBar || tailHits <= 0) return;
        if (Event.current.type != EventType.Repaint) return;
        float seg = 46f, gap = 6f, h = 10f;
        float w = tailHits * seg + (tailHits - 1) * gap;
        float x = (Screen.width - w) * 0.5f, y = 26f;
        Color full = tailHP == 1 ? new Color(dangerColor.r, dangerColor.g, dangerColor.b, 1f) : new Color(0.85f, 0.62f, 0.3f, 1f);
        for (int i = 0; i < tailHits; i++)
        {
            var r = new Rect(x + i * (seg + gap), y, seg, h);
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
            GUI.color = i < tailHP ? full : new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }
        GUI.color = Color.white;
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
        if (fight != null) StopCoroutine(fight);
        fight = null;
        state = State.Done;
        BattleMusic.End();
        showBar = false;
        hintQuadrant = -1;
        ClearTails(false);
        if (visuals != null) Destroy(visuals);
        visuals = null;
        StartCoroutine(DefeatRoutine());
    }

    /// <summary>打倒後：大沙蟲從中央冒出來說最後幾句話（劇情對話），沉回地底後才出現獎勵植物</summary>
    IEnumerator DefeatRoutine()
    {
        yield return new WaitForSeconds(0.8f);
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
        PlanetStory.PlayWormDefeat(worm.transform, () => done = true);
        while (!done) yield return null;
        talking = false;
        yield return new WaitForSeconds(1.8f); // 等沙蟲沉回地底

        SandBurst(at, 60, 4f, 1f, 2f);
        if (rewardPlant != null)
        {
            rewardPlant.transform.position = at;
            rewardPlant.SetActive(true);
        }
        GameAudio.Play("SFX/RPG/handleCoins", transform.position, 1f, 1f, 0f, 0.3f);
    }

    // ---------------- 象限 ----------------

    /// <summary>點在哪個象限（沒轉動時 0 = 東北、1 = 西北、2 = 西南、3 = 東南），用中心點的方位角判斷</summary>
    int QuadrantOf(Vector3 p)
    {
        Vector3 d = p - transform.position;
        float ang = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;
        return (int)(Mathf.Repeat(ang - layoutAngle, 360f) / 90f) % 4;
    }

    /// <summary>從中心往 angle 方向（度，0 = 東、逆時針）碰到外框的點</summary>
    Vector3 EdgePoint(float angle)
    {
        float a = angle * Mathf.Deg2Rad, dx = Mathf.Cos(a), dz = Mathf.Sin(a);
        float t = Mathf.Min(Mathf.Abs(dx) > 1e-4f ? halfSize.x / Mathf.Abs(dx) : float.MaxValue,
                            Mathf.Abs(dz) > 1e-4f ? halfSize.y / Mathf.Abs(dz) : float.MaxValue);
        return transform.position + new Vector3(dx * t, 0f, dz * t);
    }

    /// <summary>一格的外形（不含中心點）：兩條分隔線碰到外框的點，加上中間夾到的外框角落</summary>
    List<Vector3> SectorOutline(int q)
    {
        float a0 = layoutAngle + q * 90f;
        var pts = new List<Vector3> { EdgePoint(a0) };
        var corners = new List<(float rel, Vector3 p)>();
        for (int i = 0; i < 4; i++)
        {
            Vector3 off = new Vector3(i == 0 || i == 3 ? halfSize.x : -halfSize.x, 0f, i <= 1 ? halfSize.y : -halfSize.y);
            float rel = Mathf.Repeat(Mathf.Atan2(off.z, off.x) * Mathf.Rad2Deg - a0, 360f);
            if (rel > 0.01f && rel < 89.99f) corners.Add((rel, transform.position + off));
        }
        corners.Sort((x, y) => x.rel.CompareTo(y.rel));
        foreach (var c in corners) pts.Add(c.p);
        pts.Add(EdgePoint(a0 + 90f));
        return pts;
    }

    /// <summary>點在不在範圍內（margin = 往外多算幾公尺）</summary>
    bool Inside(Vector3 p, float margin)
    {
        Vector3 d = p - transform.position;
        return Mathf.Abs(d.x) <= halfSize.x + margin && Mathf.Abs(d.z) <= halfSize.y + margin;
    }

    /// <summary>格子裡隨機一點（離外框和中心留一點距離）</summary>
    Vector3 RandomPointIn(int q)
    {
        for (int i = 0; i < 40; i++)
        {
            Vector3 p = transform.position + new Vector3(Random.Range(-0.9f, 0.9f) * halfSize.x, 0f, Random.Range(-0.9f, 0.9f) * halfSize.y);
            if (QuadrantOf(p) == q && Flat(p - transform.position).magnitude > 4f) return p;
        }
        return QuadrantCenter(q);
    }

    Vector3 QuadrantCenter(int q)
    {
        Vector3 sum = transform.position;
        var pts = SectorOutline(q);
        foreach (var p in pts) sum += p;
        return Ground(sum / (pts.Count + 1));
    }

    /// <summary>沙蟲冒出的位置：玩家在這格就從玩家腳下，不然是這格裡的隨機一點</summary>
    Vector3 EruptionPoint(int q)
    {
        if (player != null && !player.IsDead && QuadrantOf(player.transform.position) == q)
            return Ground(player.transform.position);
        return Ground(RandomPointIn(q));
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
        for (int axis = 0; axis < 2; axis++) dividers[axis] = AddLine(new Vector3[0], false, 0.25f);

        for (int q = 0; q < 4; q++)
        {
            var go = new GameObject($"Quadrant{q + 1}");
            go.transform.SetParent(visuals.transform, false);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glowMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quads[q] = mr;
            SetQuadrant(q, Color.clear);
        }
        RefreshLayout();
    }

    /// <summary>依照目前的 layoutAngle 更新分隔線和四格的網格</summary>
    void RefreshLayout()
    {
        if (visuals == null) return;
        for (int axis = 0; axis < 2; axis++)
        {
            var lr = dividers[axis];
            if (lr == null) continue;
            float a = layoutAngle + axis * 90f;
            Vector3 p0 = EdgePoint(a + 180f), p1 = EdgePoint(a);
            var pts = new Vector3[33];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = Ground(Vector3.Lerp(p0, p1, i / 32f)) + Vector3.up * 0.15f;
            lr.positionCount = pts.Length;
            lr.SetPositions(pts);
        }
        for (int q = 0; q < 4; q++)
        {
            if (quads[q] == null) continue;
            var mf = quads[q].GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) Destroy(mf.sharedMesh);
            mf.sharedMesh = BuildSector(q);
        }
    }

    LineRenderer AddLine(Vector3[] pts, bool loop, float width)
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
        return lr;
    }

    /// <summary>一格的網格：從中心往外框拉成扇形的幾個三角形，每個再細分，頂點貼著地形</summary>
    Mesh BuildSector(int q)
    {
        const int n = 12;
        var verts = new List<Vector3>();
        var tris = new List<int>();
        Vector3 c = transform.position;
        var outline = SectorOutline(q);
        for (int s = 0; s + 1 < outline.Count; s++)
        {
            Vector3 eu = outline[s] - c, ev = outline[s + 1] - c;
            int start = verts.Count;
            // 三角形細分：第 i 列有 n - i + 1 個點
            var rowStart = new int[n + 1];
            for (int i = 0; i <= n; i++)
            {
                rowStart[i] = verts.Count;
                for (int j = 0; i + j <= n; j++)
                    verts.Add(Ground(c + eu * (i / (float)n) + ev * (j / (float)n)) + Vector3.up * 0.12f);
            }
            for (int i = 0; i < n; i++)
                for (int j = 0; i + j < n; j++)
                {
                    int a = rowStart[i] + j, b = a + 1, d = rowStart[i + 1] + j;
                    // 雙面都畫
                    tris.AddRange(new[] { a, b, d, a, d, b });
                    if (i + j + 1 < n)
                    {
                        int e = d + 1;
                        tris.AddRange(new[] { b, e, d, b, d, e });
                    }
                }
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
        Gizmos.DrawWireCube(c, new Vector3((halfSize.x + escapeMargin) * 2f, 0.5f, (halfSize.y + escapeMargin) * 2f));
    }
}

/// <summary>沙蟲露出來的尾巴：被玩家打中一下就縮回去，算一次</summary>
public class SandWormTail : MonoBehaviour, IDamageable
{
    internal SandWormArena arena;
    public bool IsDead { get; internal set; }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (IsDead || arena == null) return;
        IsDead = true;
        arena.OnTailHit(this, hitPoint);
    }
}
