using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Boss AI（用膠囊體代替模型）
///
/// 戰鬥開始（由 BossGate 呼叫 BeginFight，或勾選 autoStart）：競技場地板升起隨機的小山丘與柱子。
/// 平常：追著玩家（會繞過柱子、爬上小山丘），靠近時普通攻擊（揮擊）。
/// 每追擊一段時間，隨機使用一招特殊攻擊（不會連續重複同一招）：
///   1. 拍地  ：朝前方扇形拍一掌，衝擊波往外擴散、地板凹陷並出現不規則深坑。
///              衝擊波經過時腳還踩在地上就會受傷 → 要抓時機跳過去。
///   2. 怒吼  ：全場隨機一部分方塊＋玩家腳下的方塊，預警後塌陷，掉下去會死。
///   3. 光束  ：原地瞄準玩家後往追蹤方向掃射。柱子可以擋住光束一次，但會被打爆（外牆不會）。
///   4. 跳躍砸地：跳到玩家位置砸下來。落點會先在地板上標紅，要趕快跑出去；
///              落地後還有一圈往外擴散的衝擊波，要跳過去。玩家離太遠時 Boss 也會用這招拉近距離。
///
/// 玩家死亡重生時：Boss 回滿血、回到起始位置、地板壓平，回到「等待玩家進門」的狀態。
/// 測試用：遊戲中按數字鍵 1 / 2 / 3 / 4 可以直接叫 Boss 放對應的招式。
/// </summary>
public class BossController : MonoBehaviour
{
    [Header("參考")]
    public Transform player;
    public ArenaFloor arena;
    [Tooltip("身體模型的旋轉軸心（放在腳底），用來做前傾 / 後仰 / 伸展動作")]
    public Transform bodyPivot;
    [Tooltip("光束物件（一個沒有碰撞的長方體）")]
    public Transform beam;

    [Header("戰鬥開始")]
    [Tooltip("勾選 = 場景一開始就開打（測試用）；不勾 = 等 BossGate 觸發")]
    public bool autoStart = false;
    [Tooltip("戰鬥開始時是否產生隨機地形（山丘、柱子）")]
    public bool generateTerrainOnStart = true;
    [Tooltip("開場等待多久才開始行動 (秒)，地形會在這段時間升起")]
    public float startDelay = 3f;

    [Header("追擊")]
    public float moveSpeed = 3.5f;
    [Tooltip("轉身速度 (度/秒)")]
    public float turnSpeed = 150f;
    [Tooltip("離玩家這麼近就停下來（以中心點計算）")]
    public float stopDistance = 3.2f;
    [Tooltip("Boss 身體半徑，用來計算攻擊距離")]
    public float bodyRadius = 1.5f;
    [Tooltip("Boss 能直接走上去的高度差 (m)，超過就當成障礙物繞開（例如柱子）")]
    public float maxStepHeight = 2f;
    [Tooltip("爬上 / 走下地形時高度變化的速度 (m/s)")]
    public float groundFollowSpeed = 6f;
    [Tooltip("兩次特殊攻擊之間追擊多久 (秒，隨機範圍)")]
    public Vector2 chaseTimeRange = new Vector2(5f, 8f);

    [Header("普通攻擊（揮擊）")]
    public float swipeRange = 4.8f;
    public float swipeHalfAngle = 70f;
    public float swipeWindup = 0.6f;
    public float swipeCooldown = 1.6f;
    public float swipeDamage = 15f;

    [Header("1. 拍地")]
    public float slamRange = 48f;
    [Tooltip("扇形的半角，60 = 總共 120 度")]
    public float slamHalfAngle = 60f;
    [Tooltip("距離玩家超過這個值會先靠近")]
    public float slamApproachDistance = 18f;
    public float slamWindup = 1.3f;
    [Tooltip("衝擊波往外擴散的速度 (m/s)")]
    public float slamWaveSpeed = 22f;
    [Tooltip("衝擊波判定的厚度 (m)")]
    public float slamWaveWidth = 2.4f;
    [Tooltip("腳底高於地面這麼多就算跳過衝擊波")]
    public float slamDodgeHeight = 0.35f;
    public float slamDentDepth = 0.8f;
    [Tooltip("凹陷維持多久才開始恢復 (秒)")]
    public float slamDentHoldTime = 3f;
    [Tooltip("凹陷恢復原狀要花多久 (秒)")]
    public float slamDentRecoverTime = 1.5f;
    [Tooltip("凹洞裡更深的不規則深坑深度 (m)")]
    public float slamDeepHoleDepth = 2.2f;
    [Tooltip("深坑的比例，越大深坑越多 (0~1)")]
    [Range(0f, 1f)] public float slamDeepHoleAmount = 0.3f;
    [Tooltip("深坑形狀的雜訊縮放，越小坑越大塊、越大坑越碎")]
    public float slamDeepHoleNoiseScale = 0.35f;
    public float slamDamage = 25f;
    public Color slamTelegraphColor = new Color(1f, 0.55f, 0.1f);

    [Header("2. 怒吼 → 地板塌陷")]
    public float roarDuration = 1.2f;
    [Tooltip("全場隨機塌陷的比例 (0~1)")]
    [Range(0f, 1f)] public float collapseRatio = 0.8f;
    [Tooltip("玩家腳下多大範圍一定會塌")]
    public float playerCollapseRadius = 3.2f;
    [Tooltip("Boss 腳下多大範圍不會塌（讓 Boss 有地方站）")]
    public float safeRadius = 7f;
    public float collapseWarnTime = 3f;
    [Tooltip("方塊掉下去後多久升回來")]
    public float collapseDownTime = 5f;

    [Header("3. 光束")]
    public float beamChargeTime = 2f;
    [Tooltip("蓄力時瞄準點追玩家的速度 (m/s)。要比玩家走路速度 (4.5) 慢，玩家跑起來才甩得掉")]
    public float beamTrackSpeed = 3.5f;
    [Tooltip("蓄力最後這段時間完全鎖定方向不再追蹤 (秒)")]
    public float beamLockTime = 0.3f;
    [Tooltip("瞄準點離 Boss 至少這麼遠，避免玩家繞到身邊時瞄準方向亂跳")]
    public float beamMinAimDistance = 8f;
    [Tooltip("旋轉速度 (度/秒)")]
    public float beamRotateSpeed = 40f;
    [Tooltip("總共轉幾度")]
    public float beamSweepDegrees = 120f;
    [Tooltip("光束中心離 Boss 腳底的高度，玩家腳底要高於 高度 + 粗細/2 才躲得過")]
    public float beamHeight = 0.6f;
    [Tooltip("光束的上下厚度 (m)；太厚玩家會跳不過")]
    public float beamThickness = 1.0f;
    [Tooltip("光束的左右寬度 (m)")]
    public float beamWidth = 2f;
    public float beamDamage = 20f;
    [Tooltip("光束會不會被柱子擋住")]
    public bool beamBlockedByPillars = true;
    [Tooltip("光束能不能打爆柱子")]
    public bool beamBreaksPillars = true;
    [Tooltip("柱子被光束掃到後，閃爍多久才倒下 (秒)；倒下前仍然擋得住光束")]
    public float beamPillarBreakTime = 0.3f;

    [Header("4. 跳躍砸地")]
    [Tooltip("玩家離這麼遠時，Boss 會用跳躍砸地拉近距離 (m)")]
    public float jumpMinDistance = 14f;
    [Tooltip("跳躍砸地的冷卻時間 (秒)")]
    public float jumpCooldown = 10f;
    [Tooltip("蹲下蓄力的時間 (秒)，這段時間會一直瞄準玩家")]
    public float jumpWindup = 0.7f;
    [Tooltip("在空中的時間 (秒)。落點在起跳時鎖定，玩家要在這段時間跑出紅圈")]
    public float jumpFlightTime = 1.3f;
    public float jumpHeight = 12f;
    [Tooltip("落地直接砸中的範圍半徑 (m)")]
    public float jumpLandRadius = 6f;
    public float jumpLandDamage = 35f;
    [Tooltip("落地後往外擴散的衝擊波最遠到哪 (m)，要跳過")]
    public float jumpWaveRange = 16f;
    public float jumpWaveDamage = 15f;
    [Tooltip("落地後的硬直 (秒)，是玩家反擊的好時機")]
    public float jumpRecoverTime = 1.5f;
    public Color jumpTelegraphColor = new Color(0.9f, 0.1f, 0.35f);

    [Header("除錯")]
    public bool debugKeys = true;
    public bool showStatus = true;

    public string CurrentAction { get; private set; } = "沉睡";
    /// <summary>戰鬥進行中（已觸發、Boss 還沒死）</summary>
    public bool FightActive { get; private set; }
    public bool IsDefeated => dead;

    public event System.Action OnFightStarted;
    /// <summary>玩家死亡後戰鬥重置（BossGate 用來把門恢復）</summary>
    public event System.Action OnFightReset;
    public event System.Action OnDefeated;

    enum Special { Slam, Roar, Beam, Jump }

    PlayerHealth playerHealth;
    BossHealth health;
    Special? lastSpecial;
    Special? forcedSpecial;
    float lastSwipeTime = float.NegativeInfinity;
    float lastJumpTime = float.NegativeInfinity;
    bool airborne;
    Vector3 startPosition;
    Quaternion startRotation;
    float currentBeamThickness;
    bool dead;
    readonly RaycastHit[] rayHits = new RaycastHit[128];
    float currentBeamWidth;

    bool PlayerAlive => player != null && (playerHealth == null || !playerHealth.IsDead);

    void Awake()
    {
        // 做成 Prefab 後無法事先連到場景裡的玩家，所以沒設定時自動用 Tag "Player" 找
        if (player == null)
        {
            var found = GameObject.FindWithTag("Player");
            if (found != null) player = found.transform;
        }
        if (arena == null) arena = transform.root.GetComponentInChildren<ArenaFloor>();
        if (player != null) playerHealth = player.GetComponent<PlayerHealth>();

        health = GetComponent<BossHealth>();
        if (health == null)
        {
            health = gameObject.AddComponent<BossHealth>();
            Debug.Log("[Boss] 沒有 BossHealth，已自動加上。想調整血量請在 Boss 上手動 Add Component → BossHealth。");
        }
        if (beam != null) health.ExcludeFromFlash(beam.GetComponent<Renderer>());
        health.OnDied += OnBossDied;

        startPosition = transform.position;
        startRotation = transform.rotation;

        if (playerHealth != null) playerHealth.OnRespawned += ResetFight;
        SetupBeam(false, beamThickness);
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= ResetFight;
        if (health != null) health.OnDied -= OnBossDied;
    }

    void Start()
    {
        if (autoStart) BeginFight();
    }

    void Update()
    {
        FollowGround();

        if (!debugKeys || !FightActive || Keyboard.current == null) return;
        if (Keyboard.current.digit1Key.wasPressedThisFrame) forcedSpecial = Special.Slam;
        if (Keyboard.current.digit2Key.wasPressedThisFrame) forcedSpecial = Special.Roar;
        if (Keyboard.current.digit3Key.wasPressedThisFrame) forcedSpecial = Special.Beam;
        if (Keyboard.current.digit4Key.wasPressedThisFrame) forcedSpecial = Special.Jump;
    }

    // =====================================================================
    //  戰鬥開始 / 重置 / 死亡
    // =====================================================================

    /// <summary>開始 Boss 戰（BossGate 在玩家進門後呼叫）</summary>
    public void BeginFight()
    {
        if (FightActive || dead) return;
        FightActive = true;
        health.ShowBar = true;

        if (generateTerrainOnStart && arena != null)
        {
            var keepFlat = new List<Vector3> { transform.position };
            if (player != null) keepFlat.Add(player.position);
            arena.GenerateTerrain(keepFlat);
        }
        StartCoroutine(Brain());
        OnFightStarted?.Invoke();
    }

    /// <summary>玩家重生時：Boss 回血、回原位、地板壓平，等玩家再次進門</summary>
    void ResetFight()
    {
        // Boss 已經被打倒：只把地板恢復，不重開戰鬥
        if (dead)
        {
            if (arena != null) arena.ResetAllTiles();
            return;
        }

        StopAllCoroutines();
        dead = false;
        airborne = false;
        forcedSpecial = null;
        lastSpecial = null;
        SetupBeam(false, beamThickness);
        if (bodyPivot != null)
        {
            bodyPivot.localRotation = Quaternion.identity;
            bodyPivot.localScale = Vector3.one;
        }
        transform.SetPositionAndRotation(startPosition, startRotation);
        health.ResetHealth();
        health.ShowBar = false;
        if (arena != null) arena.ResetAllTiles(true);
        FightActive = false;
        CurrentAction = "沉睡";
        OnFightReset?.Invoke();
    }

    void OnBossDied()
    {
        dead = true;
        FightActive = false;
        StopAllCoroutines();
        SetupBeam(false, beamThickness);
        if (arena != null)
            foreach (var t in arena.Tiles) t.SetTelegraph(null);
        CurrentAction = "擊敗";
        StartCoroutine(DeathRoutine());
        OnDefeated?.Invoke();
    }

    IEnumerator DeathRoutine()
    {
        // 在空中被打死：直接掉回地面
        if (airborne)
        {
            airborne = false;
            if (arena != null && arena.TryGetGroundHeight(transform.position, out float g))
                transform.position = new Vector3(transform.position.x, g, transform.position.z);
        }
        ThirdPersonCamera.ShakeMain(0.3f, 0.6f);
        yield return StretchBody(Vector3.one, 0.2f);
        yield return Lean(-85f, 1.4f); // 往後倒下
    }

    // =====================================================================
    //  主要流程
    // =====================================================================

    IEnumerator Brain()
    {
        CurrentAction = "甦醒";
        yield return new WaitForSeconds(startDelay);

        while (!dead)
        {
            if (!PlayerAlive)
            {
                CurrentAction = "待機";
                yield return null;
                continue;
            }

            yield return Chase(Random.Range(chaseTimeRange.x, chaseTimeRange.y));
            if (!PlayerAlive) continue;

            Special next = forcedSpecial ?? PickSpecial();
            forcedSpecial = null;
            lastSpecial = next;

            switch (next)
            {
                case Special.Slam: yield return Slam(); break;
                case Special.Roar: yield return Roar(); break;
                case Special.Beam: yield return BeamAttack(); break;
                case Special.Jump: yield return JumpSlam(); break;
            }

            CurrentAction = "喘息";
            yield return new WaitForSeconds(1f);
        }
    }

    Special PickSpecial()
    {
        var options = new List<Special> { Special.Slam, Special.Roar, Special.Beam, Special.Jump };
        if (lastSpecial.HasValue) options.Remove(lastSpecial.Value);
        return options[Random.Range(0, options.Count)];
    }

    // =====================================================================
    //  追擊 + 普通攻擊
    // =====================================================================

    IEnumerator Chase(float duration)
    {
        float t = 0f;
        while (t < duration && forcedSpecial == null)
        {
            if (!PlayerAlive) yield break;
            CurrentAction = "追擊";

            Vector3 toPlayer = FlatTo(player.position);
            FaceTowards(toPlayer, turnSpeed);
            if (toPlayer.magnitude > stopDistance)
                TryMove(toPlayer, moveSpeed * Time.deltaTime);

            // 玩家跑太遠：跳過去砸
            if (toPlayer.magnitude > jumpMinDistance && Time.time - lastJumpTime >= jumpCooldown)
            {
                yield return JumpSlam();
                t += jumpFlightTime + jumpRecoverTime;
                continue;
            }

            bool inRange = toPlayer.magnitude <= swipeRange;
            bool facing = Vector3.Angle(transform.forward, toPlayer) < 35f;
            if (inRange && facing && Time.time - lastSwipeTime >= swipeCooldown)
                yield return Swipe();

            t += Time.deltaTime;
            yield return null;
        }
    }

    IEnumerator Swipe()
    {
        CurrentAction = "揮擊";
        yield return Lean(-15f, swipeWindup);  // 後仰蓄力
        yield return Lean(25f, 0.1f);          // 揮下去

        if (PlayerInFan(swipeRange + 0.5f, swipeHalfAngle, out Vector3 dir))
            playerHealth?.TakeDamage(swipeDamage, dir.normalized * 9f + Vector3.up * 4f);

        yield return new WaitForSeconds(0.3f);
        yield return Lean(0f, 0.4f);
        lastSwipeTime = Time.time;
    }

    // =====================================================================
    //  1. 拍地
    // =====================================================================

    IEnumerator Slam()
    {
        CurrentAction = "拍地（準備）";

        // 太遠就先靠近
        float t = 0f;
        while (t < 3f && FlatTo(player.position).magnitude > slamApproachDistance)
        {
            Vector3 to = FlatTo(player.position);
            FaceTowards(to, turnSpeed);
            TryMove(to, moveSpeed * 1.4f * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }

        // 轉向玩家
        t = 0f;
        while (t < 0.35f)
        {
            FaceTowards(FlatTo(player.position), turnSpeed * 2f);
            t += Time.deltaTime;
            yield return null;
        }

        // 鎖定方向、在地板上標出範圍、後仰蓄力
        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;
        var tiles = arena != null
            ? arena.GetTilesInFan(origin, forward, slamHalfAngle, slamRange, bodyRadius)
            : new List<FloorTile>();
        foreach (var tile in tiles) tile.SetTelegraph(slamTelegraphColor);

        yield return Lean(-25f, slamWindup);

        // 拍下去！
        CurrentAction = "拍地";
        yield return Lean(40f, 0.08f);
        ThirdPersonCamera.ShakeMain(0.35f, 0.5f);

        // 用 Perlin 雜訊決定哪些方塊變成深坑，每次拍地形狀都不一樣
        Vector2 noiseOffset = new Vector2(Random.Range(0f, 1000f), Random.Range(0f, 1000f));
        float deepThreshold = 1f - slamDeepHoleAmount;
        foreach (var tile in tiles)
        {
            tile.SetTelegraph(null);
            float d = ArenaFloor.FlatDistance(tile.RestPosition, origin);
            float noise = Mathf.PerlinNoise(tile.gridX * slamDeepHoleNoiseScale + noiseOffset.x,
                                            tile.gridZ * slamDeepHoleNoiseScale + noiseOffset.y);
            bool deep = noise > deepThreshold;
            // 深坑的深度也加一點隨機，看起來比較不整齊
            float depth = deep ? slamDeepHoleDepth * Random.Range(0.8f, 1.2f) : slamDentDepth * Random.Range(0.85f, 1.1f);
            float delay = (d - bodyRadius) / slamWaveSpeed + (deep ? 0.15f : 0f);
            tile.Dent(delay, depth, slamDentHoldTime, slamDentRecoverTime);
        }

        // 衝擊波往外擴散，碰到還踩在地上的玩家就造成傷害
        yield return Shockwave(origin, forward, slamHalfAngle, bodyRadius, slamRange, slamWaveSpeed, slamWaveWidth, slamDamage);

        yield return new WaitForSeconds(0.4f);
        yield return Lean(0f, 0.5f);
    }

    /// <summary>玩家腳是不是踩在地上（用來判斷有沒有跳過衝擊波）</summary>
    bool PlayerOnGround()
    {
        if (playerHealth == null) return true;
        if (arena == null || !arena.TryGetGroundHeight(player.position, out float ground))
            return false; // 腳下是洞 → 不算踩在地上
        return playerHealth.FeetY < ground + slamDodgeHeight;
    }

    // =====================================================================
    //  2. 怒吼 → 地板塌陷
    // =====================================================================

    IEnumerator Roar()
    {
        CurrentAction = "怒吼";

        // 朝天伸展（之後換成真正的怒吼動畫）
        yield return Lean(-20f, 0.4f);
        yield return StretchBody(new Vector3(0.9f, 1.25f, 0.9f), 0.25f);
        ThirdPersonCamera.ShakeMain(0.25f, roarDuration);

        if (arena != null)
        {
            var chosen = new HashSet<FloorTile>();

            // 全場每一塊獨立隨機（山丘、柱子也一樣會塌）
            foreach (var tile in arena.Tiles)
                if (tile.canCollapse && Random.value < collapseRatio) chosen.Add(tile);

            // 玩家腳下一定塌
            foreach (var tile in arena.GetTilesNear(player.position, playerCollapseRadius))
                chosen.Add(tile);

            foreach (var tile in chosen)
            {
                bool nearBoss = ArenaFloor.FlatDistance(tile.RestPosition, transform.position) < safeRadius;
                if (!nearBoss)
                    tile.Collapse(collapseWarnTime, collapseDownTime);
            }
        }

        yield return new WaitForSeconds(roarDuration);
        yield return StretchBody(Vector3.one, 0.3f);
        yield return Lean(0f, 0.3f);
    }

    // =====================================================================
    //  3. 光束
    // =====================================================================

    IEnumerator BeamAttack()
    {
        // 原地發射，不用跑到中央
        float t;

        // 蓄力：瞄準點一開始就在玩家身上（站著不動會被打）
        CurrentAction = "光束（蓄力）";
        Vector3 aimPoint = player.position;
        FaceDirectionInstant(FlatTo(aimPoint));
        SetupBeam(true, 0.06f, 0.06f);

        // 瞄準點以固定速度 (m/s) 追玩家，比玩家移動慢，所以跑起來就能甩開
        // 同時記錄瞄準是往哪個方向轉的，發射後就往那個方向掃
        float sweepSign = 0f;
        t = 0f;
        while (t < beamChargeTime)
        {
            if (t < beamChargeTime - beamLockTime && PlayerAlive)
            {
                Vector3 prevDir = FlatTo(aimPoint);
                aimPoint = Vector3.MoveTowards(aimPoint, player.position, beamTrackSpeed * Time.deltaTime);

                // 瞄準點不能太靠近 Boss，不然方向會瞬間大轉
                Vector3 newDir = FlatTo(aimPoint);
                if (newDir.magnitude < beamMinAimDistance)
                {
                    newDir = (newDir.sqrMagnitude > 0.0001f ? newDir.normalized : transform.forward) * beamMinAimDistance;
                    aimPoint = transform.position + newDir;
                }

                float turned = Vector3.SignedAngle(prevDir, newDir, Vector3.up);
                if (Mathf.Abs(turned) > 0.01f) sweepSign = Mathf.Sign(turned);
                FaceDirectionInstant(newDir);
            }
            UpdateBeamLength();
            t += Time.deltaTime;
            yield return null;
        }

        // 玩家完全沒動過：往玩家目前所在的那一側掃；剛好正對就隨機
        if (sweepSign == 0f)
        {
            float side = Vector3.SignedAngle(transform.forward, FlatTo(player.position), Vector3.up);
            sweepSign = Mathf.Abs(side) > 1f ? Mathf.Sign(side) : (Random.value < 0.5f ? 1f : -1f);
        }

        // 發射並往追蹤方向旋轉
        CurrentAction = "光束";
        SetupBeam(true, beamThickness, beamWidth);
        ThirdPersonCamera.ShakeMain(0.15f, 0.3f);
        float swept = 0f;
        while (swept < beamSweepDegrees)
        {
            float step = beamRotateSpeed * Time.deltaTime;
            transform.Rotate(0f, step * sweepSign, 0f);
            swept += step;
            UpdateBeamLength(true);
            CheckBeamHit();
            yield return null;
        }

        SetupBeam(false, beamThickness);
    }

    float MaxBeamLength => arena != null ? arena.radius * 2f + 3f : 30f;

    void SetupBeam(bool active, float thickness) => SetupBeam(active, thickness, beamWidth);

    void SetupBeam(bool active, float thickness, float width)
    {
        if (beam == null) return;
        currentBeamThickness = thickness;
        currentBeamWidth = width;
        beam.gameObject.SetActive(active);
        beam.localRotation = Quaternion.identity;
        SetBeamLength(MaxBeamLength);
    }

    void SetBeamLength(float length)
    {
        beam.localScale = new Vector3(currentBeamWidth, currentBeamThickness, length);
        beam.localPosition = new Vector3(0f, beamHeight, bodyRadius * 0.8f + length * 0.5f);
    }

    /// <summary>光束碰到柱子或牆壁就截斷；firing = true 時持續照射會打爆柱子</summary>
    void UpdateBeamLength(bool firing = false)
    {
        if (beam == null) return;
        Vector3 origin = transform.position + Vector3.up * beamHeight + transform.forward * bodyRadius * 0.8f;
        float max = MaxBeamLength;
        float length = max;

        FloorTile blockingPillar = null;
        int count = Physics.RaycastNonAlloc(origin, transform.forward, rayHits, max, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = rayHits[i].collider;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (player != null && c.transform.IsChildOf(player)) continue;

            bool blocks;
            FloorTile tile = null;
            if (c.TryGetComponent(out tile))
                blocks = beamBlockedByPillars && tile.IsPillar;   // 小山丘不擋，柱子擋
            else
                blocks = true;                                    // 外牆、門等其他東西都擋（而且打不壞）
            if (blocks && rayHits[i].distance < length)
            {
                length = rayHits[i].distance;
                blockingPillar = tile;
            }
        }
        SetBeamLength(Mathf.Max(length, 0.1f));

        // 發射中掃到柱子：柱子開始碎裂，beamPillarBreakTime 秒後倒下
        // （光束掃過一根柱子通常只有 0.1~0.2 秒，所以不能用「持續照射」來判斷）
        if (firing && beamBreaksPillars && blockingPillar != null && !blockingPillar.IsCracking)
        {
            blockingPillar.ShatterAfter(beamPillarBreakTime);
            // 2x2 的大柱子：同一根的其他格也一起碎
            foreach (var n in arena.GetTilesNear(blockingPillar.RestPosition, arena.tileSize * 1.5f))
                if (n.IsPillar) n.ShatterAfter(beamPillarBreakTime);
            ThirdPersonCamera.ShakeMain(0.1f, 0.15f);
        }
    }

    void CheckBeamHit()
    {
        if (beam == null || !PlayerAlive || playerHealth == null) return;
        var hits = Physics.OverlapBox(beam.position, beam.lossyScale * 0.5f, beam.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.transform != player && !h.transform.IsChildOf(player)) continue;
            Vector3 push = Vector3.Cross(Vector3.up, beam.forward); // 往光束旋轉的方向推
            playerHealth.TakeDamage(beamDamage, push * 5f + Vector3.up * 5f);
            break;
        }
    }

    // =====================================================================
    //  4. 跳躍砸地
    // =====================================================================

    IEnumerator JumpSlam()
    {
        lastJumpTime = Time.time;
        CurrentAction = "跳躍砸地（蓄力）";

        // 蹲下蓄力，同時轉向玩家
        float t = 0f;
        while (t < jumpWindup)
        {
            FaceTowards(FlatTo(player.position), turnSpeed * 2f);
            float k = t / jumpWindup;
            if (bodyPivot != null) bodyPivot.localScale = new Vector3(1f + 0.15f * k, 1f - 0.25f * k, 1f + 0.15f * k);
            t += Time.deltaTime;
            yield return null;
        }

        // 起跳瞬間鎖定落點（玩家當下的位置）
        Vector3 target = ClampInArena(player.position);
        target.y = arena != null && arena.TryGetGroundHeight(target, out float g) ? g : transform.position.y;

        var landTiles = arena != null ? arena.GetTilesNear(target, jumpLandRadius) : new List<FloorTile>();
        foreach (var tile in landTiles) tile.SetTelegraph(jumpTelegraphColor);

        // 飛行：拋物線
        CurrentAction = "跳躍砸地";
        airborne = true;
        if (bodyPivot != null) bodyPivot.localScale = new Vector3(0.9f, 1.15f, 0.9f);
        Vector3 start = transform.position;
        Vector3 flatDir = FlatTo(target);
        t = 0f;
        while (t < jumpFlightTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / jumpFlightTime);
            Vector3 p = Vector3.Lerp(start, target, k);
            p.y += jumpHeight * 4f * k * (1f - k);
            transform.position = p;
            FaceTowards(flatDir, turnSpeed);
            yield return null;
        }
        transform.position = target;
        airborne = false;

        // 落地！
        if (bodyPivot != null) bodyPivot.localScale = new Vector3(1.25f, 0.7f, 1.25f);
        ThirdPersonCamera.ShakeMain(0.5f, 0.6f);

        // 落點形成隕石坑：中間最深、外圍淺；範圍內的柱子直接打爆
        foreach (var tile in landTiles)
        {
            tile.SetTelegraph(null);
            if (tile.IsPillar) { tile.Shatter(); continue; }
            float d = ArenaFloor.FlatDistance(tile.RestPosition, target) / jumpLandRadius;
            tile.Dent(0f, Mathf.Lerp(1.6f, 0.4f, d), 2f, 1.2f);
        }

        // 直接砸中
        if (PlayerAlive && playerHealth != null)
        {
            Vector3 to = player.position - target;
            to.y = 0f;
            if (to.magnitude <= jumpLandRadius && playerHealth.FeetY < target.y + 3f)
                playerHealth.TakeDamage(jumpLandDamage, (to.sqrMagnitude > 0.01f ? to.normalized : -transform.forward) * 10f + Vector3.up * 8f);
        }

        // 外圈地板的漣漪（只是視覺）
        if (arena != null)
        {
            foreach (var tile in arena.GetTilesNear(target, jumpWaveRange))
            {
                float d = ArenaFloor.FlatDistance(tile.RestPosition, target);
                if (d <= jumpLandRadius) continue;
                tile.Dent((d - jumpLandRadius) / slamWaveSpeed, 0.35f, 0.15f, 0.5f);
            }
        }

        // 往外擴散的衝擊波（360 度），要跳過
        StartCoroutine(StretchBody(Vector3.one, 0.4f));
        yield return Shockwave(target, transform.forward, 180f, jumpLandRadius, jumpWaveRange, slamWaveSpeed, slamWaveWidth, jumpWaveDamage);

        CurrentAction = "硬直";
        yield return new WaitForSeconds(jumpRecoverTime);
    }

    /// <summary>
    /// 從 origin 往外擴散的衝擊波。halfAngle >= 180 = 全方向。
    /// 衝擊波經過玩家時，玩家腳還踩在地上就受傷（跳起來可以躲）。
    /// </summary>
    IEnumerator Shockwave(Vector3 origin, Vector3 forward, float halfAngle, float startRadius,
                          float range, float speed, float width, float damage)
    {
        float radius = startRadius;
        bool hit = false;
        while (radius < range + width)
        {
            radius += speed * Time.deltaTime;
            if (!hit && PlayerAlive)
            {
                Vector3 to = player.position - origin;
                to.y = 0f;
                bool inArc = halfAngle >= 180f || Vector3.Angle(forward, to) <= halfAngle + 5f;
                bool inRange = to.magnitude <= range + 0.5f;
                bool onWave = Mathf.Abs(to.magnitude - radius) <= width * 0.5f;
                if (inArc && inRange && onWave && PlayerOnGround())
                    hit = playerHealth != null && playerHealth.TakeDamage(damage, to.normalized * 7f + Vector3.up * 7f);
            }
            yield return null;
        }
    }

    // =====================================================================
    //  移動 / 地形
    // =====================================================================

    /// <summary>讓 Boss 的高度貼著腳下的地形（上下山丘）</summary>
    void FollowGround()
    {
        if (arena == null || dead || airborne) return;
        if (!arena.TryGetGroundHeight(transform.position, out float ground)) return; // 腳下是洞就維持高度
        Vector3 p = transform.position;
        p.y = Mathf.MoveTowards(p.y, ground, groundFollowSpeed * Time.deltaTime);
        transform.position = p;
    }

    /// <summary>
    /// 往 dir 方向移動 distance；前面是洞或太高的障礙（柱子）時，
    /// 會試著往左右偏一點繞過去。回傳有沒有成功移動。
    /// </summary>
    bool TryMove(Vector3 dir, float distance)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f || distance <= 0f) return false;
        dir.Normalize();

        float[] angles = { 0f, 35f, -35f, 70f, -70f };
        foreach (float a in angles)
        {
            Vector3 d = Quaternion.Euler(0f, a, 0f) * dir;
            Vector3 next = transform.position + d * distance;
            if (!CanStandAt(next, d)) continue;
            transform.position = ClampInArena(next);
            return true;
        }
        return false;
    }

    bool CanStandAt(Vector3 next, Vector3 dir)
    {
        if (arena == null) return true;
        var tile = arena.GetTileAt(next + dir * bodyRadius); // 檢查身體前緣下方
        if (tile == null || !tile.IsSolid) return false;
        return tile.RestTopY - transform.position.y <= maxStepHeight;
    }

    Vector3 ClampInArena(Vector3 p)
    {
        if (arena == null) return p;
        Vector3 fromCenter = p - arena.Center;
        fromCenter.y = 0f;
        float maxR = arena.radius - bodyRadius - 0.5f;
        if (fromCenter.magnitude > maxR)
        {
            Vector3 clamped = arena.Center + fromCenter.normalized * maxR;
            p.x = clamped.x;
            p.z = clamped.z;
        }
        return p;
    }

    // =====================================================================
    //  工具
    // =====================================================================

    Vector3 FlatTo(Vector3 target)
    {
        Vector3 v = target - transform.position;
        v.y = 0f;
        return v;
    }

    void FaceDirectionInstant(Vector3 flatDir)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flatDir);
    }

    void FaceTowards(Vector3 flatDir, float degreesPerSecond)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;
        Quaternion target = Quaternion.LookRotation(flatDir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, degreesPerSecond * Time.deltaTime);
    }

    bool PlayerInFan(float range, float halfAngle, out Vector3 dir)
    {
        dir = FlatTo(player.position);
        return PlayerAlive && dir.magnitude <= range && Vector3.Angle(transform.forward, dir) <= halfAngle;
    }

    IEnumerator Lean(float angle, float duration)
    {
        if (bodyPivot == null) { yield return new WaitForSeconds(duration); yield break; }
        Quaternion from = bodyPivot.localRotation;
        Quaternion to = Quaternion.Euler(angle, 0f, 0f);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            bodyPivot.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        bodyPivot.localRotation = to;
    }

    IEnumerator StretchBody(Vector3 scale, float duration)
    {
        if (bodyPivot == null) yield break;
        Vector3 from = bodyPivot.localScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            bodyPivot.localScale = Vector3.Lerp(from, scale, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        bodyPivot.localScale = scale;
    }

    void OnGUI()
    {
        if (!showStatus || (!FightActive && !dead)) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 20, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(0, 16, Screen.width, 30), $"BOSS：{CurrentAction}", style);
        if (debugKeys)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 44, Screen.width, 20), "測試：1 拍地 / 2 怒吼塌陷 / 3 光束", small);
        }
    }

    void OnDrawGizmosSelected()
    {
        // 在 Scene 視窗顯示攻擊範圍，方便調數值
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
        Vector3 p = transform.position + Vector3.up * 0.1f;
        Vector3 left = Quaternion.Euler(0, -slamHalfAngle, 0) * transform.forward;
        Vector3 right = Quaternion.Euler(0, slamHalfAngle, 0) * transform.forward;
        Gizmos.DrawLine(p, p + left * slamRange);
        Gizmos.DrawLine(p, p + right * slamRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, swipeRange);
    }
}
