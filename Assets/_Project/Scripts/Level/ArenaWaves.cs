using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Boss 房的波次戰：玩家進門、大門關上之後，先打兩波小怪，Boss 才從天而降。
///
///   進門 → 平坦的競技場，沒有 Boss、沒有岩石
///   WAVE 1（約 30 秒）：不定時生成小怪。有的從天上掉到玩家附近，有的在離玩家較遠的地方冒出來
///   WAVE 2（約 60 秒）：小怪照樣生成，另外每隔幾秒會有一條橫跨競技場的「切線」地板塌陷，之後再復原
///   清場：等塌掉的地板都復原、剩下的小怪被打完（或等太久）
///   BOSS：場地中央出現警告 → Boss 砸下來（會受傷）→ 岩石從地面冒出來 → Boss 戰開始
///
/// 玩家死掉重生時整個重來（小怪清空、Boss 再次隱藏）。
/// 這個元件放在 BossGate 上；沒有放的話 BossGate 會在遊戲開始時自動加上。
/// 小怪 Prefab 沒有指定時，會拿場景裡現有的小怪當範本。
/// </summary>
public class ArenaWaves : MonoBehaviour
{
    public enum Phase { Idle, Intro, Wave1, Break, Wave2, Clearing, BossArriving, Boss }

    [System.Serializable]
    public class WaveSettings
    {
        [Tooltip("這一波持續幾秒")]
        public float duration = 30f;
        [Tooltip("每隔幾秒生成一批小怪（隨機範圍）")]
        public Vector2 spawnInterval = new Vector2(3f, 6f);
        [Tooltip("一批最少 / 最多幾隻（隨機）")]
        public int minGroup = 1;
        public int maxGroup = 2;
        [Tooltip("場上同時最多幾隻，滿了就先不生")]
        public int maxAlive = 5;
        [Tooltip("（要先開啟 Spawn Near Player 才有用）在玩家附近鑽出來的機率；其餘的在離玩家較遠的地方冒出來")]
        [Range(0f, 1f)] public float dropChance = 0.45f;
    }

    [Header("參考（不填會自動找）")]
    public BossController boss;
    public ArenaFloor arena;
    [Tooltip("在遠處冒出來的一般小怪（不填 = 用場景裡現有的小怪當範本）")]
    public GameObject gruntPrefab;
    [Tooltip("從天上掉下來的小怪（不填 = 用場景裡的陷阱小怪，沒有就用一般小怪）")]
    public GameObject ambusherPrefab;

    [Header("第一波")]
    public WaveSettings wave1 = new WaveSettings
    { duration = 30f, spawnInterval = new Vector2(3.5f, 6.5f), minGroup = 1, maxGroup = 2, maxAlive = 5, dropChance = 0.45f };

    [Header("第二波")]
    public WaveSettings wave2 = new WaveSettings
    { duration = 60f, spawnInterval = new Vector2(3f, 5.5f), minGroup = 1, maxGroup = 3, maxAlive = 7, dropChance = 0.5f };

    [Header("第二波：地板切線塌陷")]
    [Tooltip("每隔幾秒產生一條切線")]
    public float stripInterval = 5f;
    [Tooltip("切線寬幾格（最少 / 最多，隨機）")]
    public int stripMinTiles = 4;
    public int stripMaxTiles = 5;
    [Tooltip("塌陷前閃爍預警幾秒")]
    public float stripWarnTime = 3f;
    [Tooltip("掉下去之後幾秒復原")]
    public float stripDownTime = 10f;
    [Tooltip("切線剛好通過玩家腳下的機率；其餘的位置完全隨機")]
    [Range(0f, 1f)] public float stripAimAtPlayerChance = 0.5f;
    [Tooltip("地板開始閃爍時的警告聲（不填 = SFX/floor_warning）")]
    public AudioClip stripWarningSound;
    [Range(0f, 1f)] public float stripWarningVolume = 0.8f;

    [Header("小怪生成位置")]
    [Tooltip("關閉（預設）= 小怪全部在離玩家較遠的地方從地底鑽出來（地板先亮紫色，不會傷到玩家）。\n" +
             "開啟 = 有一部分會出現在玩家附近（地上有紅圈，鑽出來時站在紅圈裡會受傷），機率是每一波的 Drop Chance")]
    public bool spawnNearPlayer = false;
    [Tooltip("在玩家附近從地底鑽出來之前，地上的紅圈顯示幾秒")]
    public float emergeWarnTime = 1.2f;
    [Tooltip("（小怪沒有模型動畫時改成從天而降）從多高掉下來 (m)")]
    public float dropHeight = 14f;
    [Tooltip("出現在玩家周圍多遠 (m，隨機範圍)")]
    public Vector2 dropDistance = new Vector2(2f, 5f);
    [Tooltip("直接出現在玩家腳下的機率")]
    [Range(0f, 1f)] public float dropOnPlayerChance = 0.3f;
    [Tooltip("「遠處冒出來」至少離玩家多遠 (m)")]
    public float farSpawnMinDistance = 14f;
    [Tooltip("冒出來之前，地板先亮幾秒")]
    public float farSpawnWarnTime = 1f;
    public Color farSpawnColor = new Color(0.55f, 0.2f, 0.9f);

    [Header("流程")]
    [Tooltip("門關上後幾秒開始第一波")]
    public float introDelay = 1.5f;
    [Tooltip("兩波之間休息幾秒")]
    public float betweenWaveDelay = 3f;
    [Tooltip("第二波結束後，最多等幾秒讓玩家清掉剩下的小怪；時間到 Boss 就直接砸下來（剩下的小怪會被震死）")]
    public float clearTimeout = 15f;

    [Header("標題")]
    public string wave1Title = "WAVE 1";
    public string wave1Subtitle = "擊退來襲的敵人";
    public string wave2Title = "WAVE 2";
    public string wave2Subtitle = "小心腳下";
    public string bossTitle = "BOSS";
    [Tooltip("Boss 標題的副標（不填 = Boss 的名字）")]
    public string bossSubtitle = "";
    [Tooltip("標題顯示幾秒")]
    public float titleDuration = 2.8f;
    public Color waveTitleColor = new Color(1f, 0.86f, 0.45f);
    public Color bossTitleColor = new Color(1f, 0.25f, 0.15f);

    [Header("除錯")]
    [Tooltip("數字鍵 7 = 跳過目前這一波")]
    public bool debugKeys = true;
    public bool showStatus = true;

    public Phase CurrentPhase => phase;
    /// <summary>波次進行中（還沒輪到 Boss）</summary>
    public bool IsRunning => phase != Phase.Idle && phase != Phase.Boss;
    public int AliveCount => enemies.Count;

    Phase phase = Phase.Idle;
    Transform player;
    PlayerHealth playerHealth;
    readonly List<EnemyAI> enemies = new List<EnemyAI>();
    Transform container;
    GameObject templateHolder;
    int pendingSpawns;
    float phaseEndTime;
    bool skipRequested;

    string title = "", subtitle = "";
    Color titleColor = Color.white;
    float titleStart = float.NegativeInfinity;

    bool PlayerAlive => player != null && (playerHealth == null || !playerHealth.IsDead);

    // =====================================================================

    void Awake()
    {
        if (boss == null) boss = transform.root.GetComponentInChildren<BossController>();
        if (arena == null) arena = transform.root.GetComponentInChildren<ArenaFloor>();
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
    }

    void Start()
    {
        ResolveTemplates();
        if (boss != null)
        {
            boss.OnFightReset += ResetWaves;
            boss.OnArrived += KillRemaining;
            // Boss 先藏起來，等兩波打完才登場（大門關掉波次戰的話就不藏）
            var gate = GetComponent<BossGate>();
            bool wavesOn = gate == null || gate.wavesBeforeBoss;
            if (wavesOn && !boss.FightActive && !boss.IsDefeated && !boss.autoStart) boss.SetHidden(true);
        }
    }

    void OnDestroy()
    {
        if (boss != null)
        {
            boss.OnFightReset -= ResetWaves;
            boss.OnArrived -= KillRemaining;
        }
        if (container != null) Destroy(container.gameObject);
    }

    /// <summary>開始波次戰（BossGate 在大門關上時呼叫）</summary>
    public void Begin()
    {
        if (phase != Phase.Idle || boss == null || boss.IsDefeated || boss.FightActive) return;
        StartCoroutine(Run());
    }

    /// <summary>玩家死掉重生：小怪清空、回到還沒開始的狀態</summary>
    void ResetWaves()
    {
        StopAllCoroutines();
        foreach (var e in enemies)
            if (e != null) Destroy(e.gameObject);
        enemies.Clear();
        if (container != null)
            for (int i = container.childCount - 1; i >= 0; i--) Destroy(container.GetChild(i).gameObject);
        pendingSpawns = 0;
        skipRequested = false;
        titleStart = float.NegativeInfinity;
        phase = Phase.Idle;
        var gate = GetComponent<BossGate>();
        bool wavesOn = gate == null || gate.wavesBeforeBoss;
        if (wavesOn && boss != null && !boss.IsDefeated && !boss.autoStart) boss.SetHidden(true);
    }

    void Update()
    {
        // 死掉的小怪：等倒地動畫播完就移除
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            if (e == null) { enemies.RemoveAt(i); continue; }
            if (e.CurrentState == EnemyAI.State.Dead)
            {
                Destroy(e.gameObject, 3f);
                enemies.RemoveAt(i);
            }
        }

        if (debugKeys && IsRunning && Keyboard.current != null && Keyboard.current.digit7Key.wasPressedThisFrame)
            skipRequested = true;
    }

    // =====================================================================
    //  流程
    // =====================================================================

    IEnumerator Run()
    {
        phase = Phase.Intro;
        yield return new WaitForSeconds(introDelay);

        yield return RunWave(Phase.Wave1, wave1, wave1Title, wave1Subtitle, false);

        phase = Phase.Break;
        yield return new WaitForSeconds(betweenWaveDelay);

        yield return RunWave(Phase.Wave2, wave2, wave2Title, wave2Subtitle, true);

        // 清場：給玩家一點時間打完剩下的小怪
        phase = Phase.Clearing;
        phaseEndTime = Time.time + clearTimeout;
        skipRequested = false;
        while (!skipRequested && Time.time < phaseEndTime && (enemies.Count > 0 || pendingSpawns > 0))
            yield return null;
        // 場地中央的地板一定要先回來，Boss 才有地方落地（其他還沒復原的地板，Boss 登場、岩石冒出來時會一起升回來）
        float hardLimit = Time.time + stripWarnTime + stripDownTime + 3f;
        while (!LandingZoneSettled() && Time.time < hardLimit) yield return null;
        yield return new WaitForSeconds(0.5f);

        // Boss 登場
        phase = Phase.BossArriving;
        string sub = !string.IsNullOrEmpty(bossSubtitle) ? bossSubtitle
                   : (boss.GetComponent<BossHealth>() != null ? boss.GetComponent<BossHealth>().bossName : "");
        ShowTitle(bossTitle, sub, bossTitleColor);
        yield return new WaitForSeconds(0.8f);
        boss.ArriveAndBeginFight(arena != null ? arena.Center : boss.transform.position);

        while (!boss.FightActive && !boss.IsDefeated) yield return null;
        phase = Phase.Boss;
    }

    IEnumerator RunWave(Phase which, WaveSettings cfg, string waveTitle, string waveSubtitle, bool strips)
    {
        phase = which;
        skipRequested = false;
        ShowTitle(waveTitle, waveSubtitle, waveTitleColor);

        phaseEndTime = Time.time + cfg.duration;
        float nextSpawn = Time.time + 1.5f;
        float nextStrip = Time.time + 3f;

        while (Time.time < phaseEndTime && !skipRequested)
        {
            if (Time.time >= nextSpawn)
            {
                SpawnGroup(cfg);
                nextSpawn = Time.time + Random.Range(cfg.spawnInterval.x, cfg.spawnInterval.y);
            }
            // 最後幾秒不再產生新的切線（不然這一波結束了地板還在閃）
            if (strips && Time.time >= nextStrip && Time.time < phaseEndTime - stripWarnTime)
            {
                CollapseStrip();
                nextStrip = Time.time + stripInterval;
            }
            yield return null;
        }
    }

    /// <summary>Boss 落點（場地中央）的地板都好好的（沒有正在預警、掉下去、升回來的石板）</summary>
    bool LandingZoneSettled()
    {
        if (arena == null) return true;
        foreach (var t in arena.GetTilesNear(arena.Center, boss.jumpLandRadius + 2f))
        {
            var s = t.CurrentState;
            if (s == FloorTile.State.Warning || s == FloorTile.State.Fallen || s == FloorTile.State.Restoring) return false;
        }
        return true;
    }

    /// <summary>Boss 砸下來的瞬間：剩下的小怪全部被震死</summary>
    void KillRemaining()
    {
        foreach (var e in enemies)
        {
            if (e == null) continue;
            var h = e.GetComponent<EnemyHealth>();
            if (h != null) h.Kill();
        }
    }

    // =====================================================================
    //  第二波：切線塌陷
    // =====================================================================

    /// <summary>
    /// 從上方看，一條橫跨圓形競技場的直線帶狀區域（寬 stripMinTiles ~ stripMaxTiles 格）閃爍後塌陷。
    /// </summary>
    void CollapseStrip()
    {
        if (arena == null) return;
        Vector3 c = arena.Center;

        float angle = Random.Range(0f, Mathf.PI);
        Vector3 normal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)); // 垂直於切線的方向
        int tilesWide = Random.Range(Mathf.Min(stripMinTiles, stripMaxTiles), Mathf.Max(stripMinTiles, stripMaxTiles) + 1);
        float halfWidth = tilesWide * arena.tileSize * 0.5f;

        float offset; // 切線離圓心多遠
        if (PlayerAlive && Random.value < stripAimAtPlayerChance)
            offset = Vector3.Dot(player.position - c, normal) + Random.Range(-halfWidth, halfWidth) * 0.6f;
        else
            offset = Random.Range(-arena.radius, arena.radius) * 0.75f;

        foreach (var t in arena.Tiles)
        {
            float d = Vector3.Dot(t.RestPosition - c, normal) - offset;
            if (Mathf.Abs(d) <= halfWidth) t.Collapse(stripWarnTime, stripDownTime);
        }

        Vector3 ear = Camera.main != null ? Camera.main.transform.position : c;
        GameAudio.PlayAt(GameAudio.Pick(stripWarningSound, "SFX/floor_warning"), ear, stripWarningVolume, 1f, 0f);
        StartCoroutine(StripFallSound());
    }

    IEnumerator StripFallSound()
    {
        yield return new WaitForSeconds(stripWarnTime);
        Vector3 ear = Camera.main != null ? Camera.main.transform.position : transform.position;
        GameAudio.Play("SFX/rock_hit", ear, 0.35f, 0.7f, 0.08f, 0f);
    }

    // =====================================================================
    //  生成小怪
    // =====================================================================

    void SpawnGroup(WaveSettings cfg)
    {
        if (!PlayerAlive || arena == null) return;
        int room = cfg.maxAlive - enemies.Count - pendingSpawns;
        int count = Mathf.Min(Random.Range(cfg.minGroup, Mathf.Max(cfg.minGroup, cfg.maxGroup) + 1), room);

        for (int i = 0; i < count; i++)
        {
            if (spawnNearPlayer && Random.value < cfg.dropChance) SpawnDrop();
            else StartCoroutine(SpawnFar());
        }
    }

    /// <summary>
    /// 在玩家附近出現：地上先有紅圈，然後小怪從地底鑽出來，站在紅圈裡會受傷。
    /// 小怪沒有模型動畫（膠囊）的話，改成從天上掉下來。
    /// </summary>
    void SpawnDrop()
    {
        GameObject template = ambusherPrefab != null ? ambusherPrefab : gruntPrefab;
        if (template == null) return;

        Vector3 landing = player.position;
        if (Random.value >= dropOnPlayerChance || !Standable(landing))
        {
            bool found = false;
            for (int tries = 0; tries < 12 && !found; tries++)
            {
                Vector3 p = player.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward
                          * Random.Range(dropDistance.x, dropDistance.y);
                p = ClampInArena(p, 2f);
                if (Standable(p)) { landing = p; found = true; }
            }
            if (!found && !Standable(landing)) return; // 玩家附近都是洞，這次不生
        }
        landing.y = GroundY(landing);

        var ai = Create(template, landing + Vector3.up * 1.05f);
        if (ai == null) return;
        var visual = ai.GetComponent<EnemyVisual>();
        if (visual != null && visual.HasAnimator) ai.BeginEmerge(landing, emergeWarnTime);
        else ai.BeginDrop(landing, dropHeight);
    }

    /// <summary>在離玩家較遠的地方冒出來：地板先亮一下，然後小怪出現並衝向玩家</summary>
    IEnumerator SpawnFar()
    {
        GameObject template = gruntPrefab != null ? gruntPrefab : ambusherPrefab;
        if (template == null) yield break;

        // 找一個離玩家夠遠、站得住的位置
        Vector3 pos = Vector3.zero;
        bool found = false;
        float maxR = arena.radius - 3f;
        for (int tries = 0; tries < 40 && !found; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(Random.value) * maxR;
            Vector3 p = arena.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
            float need = tries < 30 ? farSpawnMinDistance : farSpawnMinDistance * 0.5f; // 找不到就放寬
            if (ArenaFloor.FlatDistance(p, player.position) < need || !Standable(p)) continue;
            pos = p;
            found = true;
        }
        if (!found) yield break;

        pendingSpawns++;
        var lit = arena.GetTilesNear(pos, arena.tileSize * 0.9f);
        foreach (var t in lit) t.SetTelegraph(farSpawnColor);
        yield return new WaitForSeconds(farSpawnWarnTime);
        foreach (var t in lit) t.SetTelegraph(null);
        pendingSpawns--;

        if (!Standable(pos)) yield break; // 這段時間地板塌了
        pos.y = GroundY(pos);
        var ai = Create(template, pos + Vector3.up * 1.05f);
        if (ai == null) yield break;

        var visual = ai.GetComponent<EnemyVisual>();
        if (visual != null && visual.HasAnimator)
            ai.BeginSpawn(visual.spawnDuration);            // 有模型：播從地底爬出來的動畫
        else
        {
            ai.ForceChase();
            var ctrl = ai.GetComponent<ThirdPersonController>();
            if (ctrl != null) ctrl.AddImpulse(Vector3.up * 5f); // 沒有模型：冒出來時小跳一下
        }
        GameAudio.Play("SFX/RPG/cloth3", pos, 0.8f, 0.6f, 0.1f, 1f, 45f);
    }

    EnemyAI Create(GameObject template, Vector3 position)
    {
        if (container == null) container = new GameObject("WaveEnemies").transform;

        Vector3 look = player != null ? player.position - position : Vector3.forward;
        look.y = 0f;
        Quaternion rot = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity;

        var go = Instantiate(template, position, rot, container);
        go.name = "WaveEnemy";
        go.SetActive(true); // 範本如果是場景裡隱藏的小怪，複製出來也是隱藏的
        var ai = go.GetComponent<EnemyAI>();
        if (ai == null) { Destroy(go); return null; }

        ai.SetArenaMode(arena != null ? arena.Center + Vector3.up : position);
        enemies.Add(ai);
        return ai;
    }

    /// <summary>這個位置的地板是不是好好的（沒有塌、沒有在閃）</summary>
    bool Standable(Vector3 p)
    {
        if (arena == null) return false;
        if (ArenaFloor.FlatDistance(p, arena.Center) > arena.radius - 1.5f) return false;
        var t = arena.GetTileAt(p);
        if (t == null || !t.IsSolid) return false;
        var s = t.CurrentState;
        return s != FloorTile.State.Warning && s != FloorTile.State.Fallen && s != FloorTile.State.Restoring;
    }

    float GroundY(Vector3 p) => arena != null && arena.TryGetGroundHeight(p, out float y) ? y : p.y;

    Vector3 ClampInArena(Vector3 p, float margin)
    {
        Vector3 from = p - arena.Center;
        from.y = 0f;
        float maxR = arena.radius - margin;
        if (from.magnitude > maxR)
        {
            Vector3 c = arena.Center + from.normalized * maxR;
            p.x = c.x;
            p.z = c.z;
        }
        return p;
    }

    /// <summary>
    /// 沒有指定 Prefab 時，拿場景裡現有的小怪複製一份當範本。
    /// 範本放在一個隱藏的物件底下，所以它自己不會動，也不會被打到。
    /// </summary>
    void ResolveTemplates()
    {
        if (gruntPrefab != null && ambusherPrefab != null) return;

        EnemyAI grunt = null, ambusher = null;
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var e in root.GetComponentsInChildren<EnemyAI>(true))
            {
                if (e.name.Contains("Ambush")) { if (ambusher == null) ambusher = e; }
                else if (grunt == null) grunt = e;
            }
        if (grunt == null) grunt = ambusher;
        if (ambusher == null) ambusher = grunt;

        if (grunt == null)
        {
            if (gruntPrefab == null && ambusherPrefab == null)
                Debug.LogWarning("[ArenaWaves] 沒有指定小怪 Prefab，場景裡也找不到小怪可以當範本，波次戰不會生成小怪。" +
                                 "請執行「GeminiGrowUp → 安裝波次戰」或把 Enemy_Grunt 拖進 ArenaWaves 的欄位。");
            return;
        }

        templateHolder = new GameObject("WaveEnemyTemplates");
        templateHolder.transform.SetParent(transform, false);
        templateHolder.SetActive(false);
        if (gruntPrefab == null) gruntPrefab = Instantiate(grunt.gameObject, templateHolder.transform);
        if (ambusherPrefab == null) ambusherPrefab = Instantiate(ambusher.gameObject, templateHolder.transform);
    }

    // =====================================================================
    //  標題和狀態顯示
    // =====================================================================

    void ShowTitle(string text, string sub, Color color)
    {
        title = text;
        subtitle = sub;
        titleColor = color;
        titleStart = Time.time;
        Vector3 ear = Camera.main != null ? Camera.main.transform.position : transform.position;
        GameAudio.Play("SFX/rock_hit", ear, 0.5f, 0.55f, 0f, 0f);
        GameAudio.Play("SFX/RPG/metalLatch", ear, 0.9f, 0.5f, 0f, 0f);
    }

    void OnGUI()
    {
        DrawTitle();
        if (!showStatus || !IsRunning || phase == Phase.Intro || phase == Phase.BossArriving) return;

        string text;
        float left = Mathf.Max(0f, phaseEndTime - Time.time);
        switch (phase)
        {
            case Phase.Wave1: text = $"{wave1Title}　剩餘 {Mathf.CeilToInt(left)} 秒　敵人 {enemies.Count}"; break;
            case Phase.Wave2: text = $"{wave2Title}　剩餘 {Mathf.CeilToInt(left)} 秒　敵人 {enemies.Count}"; break;
            case Phase.Clearing: text = enemies.Count > 0 ? $"清除剩下的敵人　{enemies.Count}　（{Mathf.CeilToInt(left)}）" : ""; break;
            default: text = ""; break;
        }

        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(0, 16, Screen.width, 30), text, style);
        if (debugKeys)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 44, Screen.width, 20), "測試：7 跳過這一波", small);
        }
    }

    void DrawTitle()
    {
        float e = Time.time - titleStart;
        if (e < 0f || e > titleDuration) return;

        const float fadeIn = 0.25f, fadeOut = 0.7f;
        float alpha = Mathf.Clamp01(e / fadeIn) * Mathf.Clamp01((titleDuration - e) / fadeOut);

        float h = Screen.height;
        float bandH = h * 0.24f, bandY = h * 0.2f;

        // 後面一條半透明黑帶，讓標題更醒目
        GUI.color = new Color(0f, 0f, 0f, 0.5f * alpha);
        GUI.DrawTexture(new Rect(0, bandY, Screen.width, bandH), Texture2D.whiteTexture);
        GUI.color = new Color(titleColor.r, titleColor.g, titleColor.b, 0.9f * alpha);
        GUI.DrawTexture(new Rect(0, bandY, Screen.width, 3f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(0, bandY + bandH - 3f, Screen.width, 3f), Texture2D.whiteTexture);

        var big = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(h * 0.12f),
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        var titleRect = new Rect(0, bandY, Screen.width, bandH * 0.72f);
        GUI.color = new Color(0f, 0f, 0f, alpha);
        GUI.Label(new Rect(titleRect.x + 4f, titleRect.y + 4f, titleRect.width, titleRect.height), title, big);
        GUI.color = new Color(titleColor.r, titleColor.g, titleColor.b, alpha);
        GUI.Label(titleRect, title, big);

        if (!string.IsNullOrEmpty(subtitle))
        {
            var sub = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(h * 0.034f),
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(0, bandY + bandH * 0.68f, Screen.width, bandH * 0.26f), subtitle, sub);
        }
        GUI.color = Color.white;
    }
}
