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
///   3. 光束  ：舉起右手，預警線先照在玩家身上並慢慢跟著（比走路慢），
///              發射後照射點直線追向玩家（比走路快、比跑步慢）。手臂會跟光束對成一直線。
///              站在光束裡每 0.3 秒受傷一次；照到的地板會碎掉，柱子能擋一下但會被打爆。
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
    [Tooltip("舉手 + 細線預警的時間 (秒)")]
    public float beamChargeTime = 2f;
    [Tooltip("蓄力時，預警線的照射點追玩家的速度 (m/s)。比玩家走路 (4.5) 慢，用走的就能甩開")]
    public float beamChargeChaseSpeed = 3.5f;
    [Tooltip("光束發射持續多久 (秒)")]
    public float beamDuration = 5f;
    [Tooltip("照射點直線追玩家的速度 (m/s)。比玩家走路 (4.5) 快、比衝刺 (8) 慢：用走的會被追上，要跑才躲得掉")]
    public float beamChaseSpeed = 6f;
    [Tooltip("照射點離 Boss 至少這麼遠 (m)")]
    public float beamMinAimDistance = 6f;
    [Tooltip("光束的粗細（直徑，m）")]
    public float beamDiameter = 2f;
    [Tooltip("站在光束裡每隔多久受一次傷 (秒)")]
    public float beamTickInterval = 0.3f;
    [Tooltip("每次受到的傷害")]
    public float beamTickDamage = 6f;
    [Tooltip("光束會不會破壞照到的地板")]
    public bool beamBreaksFloor = true;
    [Tooltip("地板被照到後閃爍多久才掉下去 (秒)")]
    public float beamFloorBreakDelay = 0.25f;
    [Tooltip("光束能不能打爆柱子（柱子會擋住光束）")]
    public bool beamBreaksPillars = true;
    [Tooltip("柱子被光束照到後，閃爍多久才倒下 (秒)；倒下前仍然擋得住光束")]
    public float beamPillarBreakTime = 0.3f;
    [Tooltip("光束從哪裡射出。留空 = 自動找模型的右手（hand.R）；找不到就從胸口射出")]
    public Transform beamSource;
    [Tooltip("發射時把右手臂轉成跟光束同一直線（覆蓋動畫的手臂角度）")]
    public bool beamAimArm = true;

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

    [Header("二階段（憤怒）")]
    [Tooltip("血量掉到一定比例時進入二階段")]
    public bool enablePhase2 = true;
    [Tooltip("血量剩多少比例時進入二階段（0.5 = 半血）")]
    [Range(0.05f, 0.95f)] public float phase2HealthRatio = 0.5f;
    [Tooltip("二階段移動速度倍率")]
    public float phase2MoveMultiplier = 1.5f;
    [Tooltip("二階段轉身速度倍率")]
    public float phase2TurnMultiplier = 1.4f;
    [Tooltip("二階段每次大招之間的追擊時間倍率（越小越常放大招）")]
    public float phase2ChaseTimeMultiplier = 0.7f;
    [Tooltip("二階段大招之後喘息的秒數（一階段是 1 秒）")]
    public float phase2RestTime = 0.4f;
    [Tooltip("進入二階段時怒吼的時間（這段時間 Boss 無敵）")]
    public float phase2RoarTime = 2.2f;
    [Tooltip("怒吼震開玩家的範圍 (m)")]
    public float phase2PushRadius = 10f;
    public float phase2PushForce = 16f;
    public float phase2PushDamage = 5f;

    [Header("音效")]
    [Tooltip("Boss 戰音樂（不填 = Resources/Audio/Music/boss_fight_music）。玩家進門、Boss 被觸發後才開始播")]
    public AudioClip bossMusic;
    [Range(0f, 1f)] public float musicVolume = 0.5f;
    [Tooltip("音樂淡入 / Boss 被打倒後淡出的秒數")]
    public float musicFadeIn = 1.5f;
    public float musicFadeOut = 4f;
    [Tooltip("怒吼聲（不填 = SFX/roar_rock_crumble）")]
    public AudioClip roarSound;
    [Tooltip("拍地、跳砸落地、柱子碎裂的撞擊聲（不填 = SFX/rock_hit）")]
    public AudioClip impactSound;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    [Header("除錯")]
    [Tooltip("數字鍵 1~4 放大招、5 = Boss 血量直接變成二階段門檻、6 = Boss 直接死亡")]
    public bool debugKeys = true;
    public bool showStatus = true;

    public string CurrentAction { get; private set; } = "沉睡";
    /// <summary>戰鬥進行中（已觸發、Boss 還沒死）</summary>
    public bool FightActive { get; private set; }
    public bool IsDefeated => dead;
    /// <summary>是否已經進入二階段</summary>
    public bool InPhase2 => phase2;

    public event System.Action OnFightStarted;
    /// <summary>玩家死亡後戰鬥重置（BossGate 用來把門恢復）</summary>
    public event System.Action OnFightReset;
    public event System.Action OnDefeated;

    enum Special { Slam, Roar, Beam, Jump }

    PlayerHealth playerHealth;
    BossHealth health;
    BossVisual visual;   // 有模型動畫時才會有（安裝石像模型後自動加上）
    Special? lastSpecial;
    Special? forcedSpecial;
    float lastSwipeTime = float.NegativeInfinity;
    float lastJumpTime = float.NegativeInfinity;
    bool airborne;
    Vector3 startPosition;
    Quaternion startRotation;
    float currentBeamDiameter;
    Vector3 beamAim;        // 光束照射點（只用 x, z）
    float nextBeamTick;
    int beamState;          // 0 = 關閉、1 = 蓄力（細線）、2 = 發射
    float armAimWeight;     // 手臂對準光束的程度（0~1，平滑過渡）
    bool dead;
    bool phase2;          // 已進入二階段
    bool phase2Pending;   // 血量到門檻了，等目前的招式放完就變身
    BossRageEffect rage;
    readonly RaycastHit[] rayHits = new RaycastHit[128];

    bool PlayerAlive => player != null && (playerHealth == null || !playerHealth.IsDead);
    float MoveSpeed => moveSpeed * (phase2 ? phase2MoveMultiplier : 1f);
    float TurnSpeed => turnSpeed * (phase2 ? phase2TurnMultiplier : 1f);

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
        health.OnDamaged += OnBossDamaged;

        rage = GetComponent<BossRageEffect>();
        if (rage == null) rage = gameObject.AddComponent<BossRageEffect>();

        visual = GetComponent<BossVisual>();
        if (visual == null) visual = null; // Unity 的「假 null」轉成真的 null，後面才能用 != null 判斷

        startPosition = transform.position;
        startRotation = transform.rotation;

        if (playerHealth != null) playerHealth.OnRespawned += ResetFight;

        // 光束從右手射出
        if (beamSource == null) beamSource = FindDeep(transform, "hand.R");
        HideBeam();
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }

    void OnDestroy()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= ResetFight;
        if (health != null) { health.OnDied -= OnBossDied; health.OnDamaged -= OnBossDamaged; }
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
        if (Keyboard.current.digit5Key.wasPressedThisFrame && !phase2)
            health.SetHealth(health.maxHealth * phase2HealthRatio);
        if (Keyboard.current.digit6Key.wasPressedThisFrame)
            health.SetHealth(0f);
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
        GameAudio.PlayMusic(GameAudio.Pick(bossMusic, "Music/boss_fight_music"), musicVolume, musicFadeIn);
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
        GameAudio.StopMusic(1.5f);
        dead = false;
        airborne = false;
        forcedSpecial = null;
        lastSpecial = null;
        phase2 = phase2Pending = false;
        health.Invulnerable = false;
        if (rage != null) rage.ResetRage();
        HideBeam();
        if (bodyPivot != null)
        {
            bodyPivot.localRotation = Quaternion.identity;
            bodyPivot.localScale = Vector3.one;
        }
        transform.SetPositionAndRotation(startPosition, startRotation);
        if (visual != null) visual.ResetVisual();
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
        HideBeam();
        if (arena != null)
            foreach (var t in arena.Tiles) t.SetTelegraph(null);
        CurrentAction = "擊敗";
        GameAudio.StopMusic(musicFadeOut);
        PlayRoar(0.65f, 0.9f);
        health.Invulnerable = false;
        if (rage != null) rage.SetRage(0f); // 死掉時紅光慢慢熄滅
        PlayAnim("Death");
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
        yield return Lean(-85f, 1.4f, true); // 往後倒下（不管有沒有模型動畫都完整倒下）
        PlayImpact(0.7f, 1f);                 // 倒地
        ThirdPersonCamera.ShakeMain(0.4f, 0.5f);
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

            if (phase2Pending) { yield return EnterPhase2(); continue; }

            float chaseTime = Random.Range(chaseTimeRange.x, chaseTimeRange.y);
            if (phase2) chaseTime *= phase2ChaseTimeMultiplier;
            yield return Chase(chaseTime);
            if (!PlayerAlive) continue;
            if (phase2Pending) { yield return EnterPhase2(); continue; }

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
            yield return new WaitForSeconds(phase2 ? phase2RestTime : 1f);
        }
    }

    // =====================================================================
    //  二階段
    // =====================================================================

    void OnBossDamaged(float amount)
    {
        if (!enablePhase2 || phase2 || phase2Pending || dead || health.IsDead) return;
        if (health.Health <= health.maxHealth * phase2HealthRatio)
        {
            phase2Pending = true;
            forcedSpecial = null; // 測試鍵預約的招式取消，先變身
        }
    }

    /// <summary>進入二階段：無敵怒吼、震開附近的玩家、身體亮起紅光，之後動作變快</summary>
    IEnumerator EnterPhase2()
    {
        phase2Pending = false;
        health.Invulnerable = true;
        CurrentAction = "憤怒";
        PlayAnim("Roar", 0.6f);

        yield return Lean(-14f, 0.6f);   // 往後仰蓄力

        // 爆發
        phase2 = true;
        rage.SetRage(1f);
        rage.Burst();
        PlayRoar(0.8f, 1f);
        PlayImpact(0.75f, 0.8f);
        ThirdPersonCamera.ShakeMain(0.6f, 1.2f);
        if (PlayerAlive)
        {
            Vector3 to = FlatTo(player.position);
            if (to.magnitude < phase2PushRadius)
            {
                Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
                playerHealth?.TakeDamage(phase2PushDamage, dir * phase2PushForce + Vector3.up * 6f);
            }
        }
        yield return Lean(8f, 0.15f);
        yield return new WaitForSeconds(Mathf.Max(0f, phase2RoarTime - 1.15f));
        yield return Lean(0f, 0.4f);

        EndAnim();
        health.Invulnerable = false;
    }

    // Boss 的聲音一半是 3D（聽得出方向），在競技場任何地方都聽得到
    void PlayRoar(float pitch, float volume) =>
        GameAudio.PlayAt(GameAudio.Pick(roarSound, "SFX/roar_rock_crumble"), transform.position + Vector3.up * 4f,
                         volume * sfxVolume, pitch, 0.5f, 90f);

    void PlayImpact(float pitch, float volume) =>
        GameAudio.PlayAt(GameAudio.Pick(impactSound, "SFX/rock_hit"), transform.position,
                         volume * sfxVolume, pitch * Random.Range(0.95f, 1.05f), 0.5f, 90f);

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
        while (t < duration && forcedSpecial == null && !phase2Pending)
        {
            if (!PlayerAlive) yield break;
            CurrentAction = "追擊";

            Vector3 toPlayer = FlatTo(player.position);
            FaceTowards(toPlayer, TurnSpeed);
            if (toPlayer.magnitude > stopDistance)
                TryMove(toPlayer, MoveSpeed * Time.deltaTime);

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
        PlayAnim("Swipe", swipeWindup + 0.1f);
        yield return Lean(-15f, swipeWindup);  // 後仰蓄力
        GameAudio.Play("SFX/RPG/knifeSlice", transform.position + Vector3.up * 3f, 0.9f * sfxVolume, 0.45f, 0.05f, 0.6f, 60f);
        yield return Lean(25f, 0.1f);          // 揮下去

        if (PlayerInFan(swipeRange + 0.5f, swipeHalfAngle, out Vector3 dir))
            playerHealth?.TakeDamage(swipeDamage, dir.normalized * 9f + Vector3.up * 4f);

        yield return new WaitForSeconds(0.3f);
        yield return Lean(0f, 0.4f);
        EndAnim();
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
            FaceTowards(to, TurnSpeed);
            TryMove(to, MoveSpeed * 1.4f * Time.deltaTime);
            t += Time.deltaTime;
            yield return null;
        }

        // 轉向玩家
        t = 0f;
        while (t < 0.35f)
        {
            FaceTowards(FlatTo(player.position), TurnSpeed * 2f);
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

        PlayAnim("Slam", slamWindup + 0.08f);
        yield return Lean(-25f, slamWindup);

        // 拍下去！
        CurrentAction = "拍地";
        yield return Lean(40f, 0.08f);
        ThirdPersonCamera.ShakeMain(0.35f, 0.5f);
        PlayImpact(1f, 1f);

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
        EndAnim();
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

        // 朝天伸展
        PlayAnim("Roar", 0.65f);
        yield return Lean(-20f, 0.4f);
        yield return StretchBody(new Vector3(0.9f, 1.25f, 0.9f), 0.25f);
        ThirdPersonCamera.ShakeMain(0.25f, roarDuration);
        PlayRoar(1f, 1f);

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
        EndAnim();
    }

    // =====================================================================
    //  3. 光束
    // =====================================================================

    IEnumerator BeamAttack()
    {
        CurrentAction = "光束（蓄力）";
        PlayAnim("Beam", 0.5f);   // 舉起右手

        // 蓄力：預警線直接照在玩家身上，之後以「比走路慢」的速度跟著玩家（不會受傷）
        beamAim = player.position;
        ClampBeamAim();
        ShowBeam(0.12f, 1);
        float t = 0f;
        while (t < beamChargeTime)
        {
            ChaseBeamAim(beamChargeChaseSpeed);
            FaceTowards(FlatTo(beamAim), TurnSpeed);
            t += Time.deltaTime;
            yield return null;
        }

        // 發射：照射點改用「比走路快、比跑步慢」的速度直線追玩家
        CurrentAction = "光束";
        ShowBeam(beamDiameter, 2);
        ThirdPersonCamera.ShakeMain(0.15f, 0.3f);
        nextBeamTick = 0f;
        t = 0f;
        while (t < beamDuration)
        {
            ChaseBeamAim(beamChaseSpeed);
            FaceTowards(FlatTo(beamAim), TurnSpeed);
            t += Time.deltaTime;
            yield return null;
        }

        HideBeam();
        EndAnim();
    }

    void ChaseBeamAim(float speed)
    {
        if (PlayerAlive)
        {
            Vector3 target = player.position;
            target.y = beamAim.y;
            beamAim = Vector3.MoveTowards(beamAim, target, speed * Time.deltaTime);
        }
        ClampBeamAim();
    }

    /// <summary>照射點不能太靠近 Boss，也不能跑出場地</summary>
    void ClampBeamAim()
    {
        beamAim.y = transform.position.y;
        Vector3 fromBoss = FlatTo(beamAim);
        if (fromBoss.magnitude < beamMinAimDistance)
            beamAim = transform.position + (fromBoss.sqrMagnitude > 0.0001f ? fromBoss.normalized : transform.forward) * beamMinAimDistance;
        beamAim = ClampInArena(beamAim);
    }

    /// <summary>照射點在地面上的位置（含山丘高度）</summary>
    Vector3 BeamAimOnGround()
    {
        Vector3 aim = beamAim;
        aim.y = arena != null && arena.TryGetGroundHeight(aim, out float gy) ? gy
              : (arena != null ? arena.Center.y : transform.position.y);
        return aim;
    }

    // 動畫在 Update 之後、LateUpdate 之前更新骨頭，所以手臂對準和光束位置都放在 LateUpdate，
    // 才能用「這一幀動畫算完的手」接著改，光束起點也才不會慢一幀
    void LateUpdate()
    {
        float target = beamState > 0 && beamAimArm ? 1f : 0f;
        armAimWeight = Mathf.MoveTowards(armAimWeight, target, Time.deltaTime / 0.25f);
        if (armAimWeight > 0f) AimArmAt(BeamAimOnGround(), armAimWeight);

        if (beamState > 0) UpdateBeam(beamState == 2);
    }

    /// <summary>把右上臂、前臂轉向目標點，讓 肩膀 → 手肘 → 手掌 → 光束 成一直線</summary>
    void AimArmAt(Vector3 point, float weight)
    {
        if (beamSource == null) return;
        Transform forearm = beamSource.parent;
        Transform upperArm = forearm != null ? forearm.parent : null;
        if (forearm == null || upperArm == null) return;

        // 1) 上臂：讓「肩膀 → 手腕」對準目標
        Vector3 cur = beamSource.position - upperArm.position;
        Vector3 want = point - upperArm.position;
        if (cur.sqrMagnitude > 0.0001f && want.sqrMagnitude > 0.0001f)
            upperArm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want), weight) * upperArm.rotation;

        // 2) 前臂：讓「手肘 → 手腕」也對準目標（把手肘打直）
        cur = beamSource.position - forearm.position;
        want = point - forearm.position;
        if (cur.sqrMagnitude > 0.0001f && want.sqrMagnitude > 0.0001f)
            forearm.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, want), weight) * forearm.rotation;
    }

    void ShowBeam(float diameter, int state)
    {
        if (beam == null) return;
        currentBeamDiameter = diameter;
        beamState = state;
        beam.gameObject.SetActive(true);
    }

    void HideBeam()
    {
        beamState = 0;
        if (beam != null) beam.gameObject.SetActive(false);
    }

    /// <summary>光束起點：右手掌前端（手腕再往手掌方向延伸 0.9m）；沒有模型時從胸口</summary>
    Vector3 BeamOrigin()
    {
        if (beamSource == null)
            return transform.position + Vector3.up * 4.5f + transform.forward * (bodyRadius + 0.3f);
        Vector3 wrist = beamSource.position;
        Vector3 dir = beamSource.parent != null ? (wrist - beamSource.parent.position).normalized : transform.forward;
        return wrist + dir * 0.9f;
    }

    /// <summary>
    /// 更新光束：從手直線照到照射點。中間碰到柱子 / 牆 / 隆起的地板就停在那裡。
    /// firing = true 時：破壞照到的地板、打爆柱子、每 beamTickInterval 秒傷害一次玩家。
    /// </summary>
    void UpdateBeam(bool firing)
    {
        if (beam == null || !beam.gameObject.activeSelf) return;

        Vector3 origin = BeamOrigin();
        Vector3 aim = BeamAimOnGround();

        Vector3 d = aim - origin;
        float len = d.magnitude;
        if (len < 0.1f) return;
        Vector3 dirN = d / len;

        // 找出光束最先碰到的東西（自己和玩家不算）
        Vector3 end = aim;
        Collider blocker = null;
        float best = len + 0.5f;
        int n = Physics.RaycastNonAlloc(origin, dirN, rayHits, len + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = rayHits[i].collider;
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            if (player != null && c.transform.IsChildOf(player)) continue;
            if (c is CharacterController) continue; // 小怪等角色不擋
            if (rayHits[i].distance < best) { best = rayHits[i].distance; blocker = c; }
        }
        if (blocker != null) end = origin + dirN * best;

        Vector3 seg = end - origin;
        beam.SetPositionAndRotation((origin + end) * 0.5f, Quaternion.LookRotation(seg));
        beam.localScale = new Vector3(currentBeamDiameter, currentBeamDiameter, seg.magnitude);

        if (!firing) return;

        // 打爆柱子
        if (beamBreaksPillars && blocker != null && blocker.TryGetComponent(out FloorTile hitTile)
            && hitTile.IsPillar && !hitTile.IsCracking && arena != null)
        {
            hitTile.ShatterAfter(beamPillarBreakTime);
            foreach (var t in arena.GetTilesNear(hitTile.RestPosition, arena.tileSize * 1.5f))
                if (t.IsPillar) t.ShatterAfter(beamPillarBreakTime); // 2x2 大柱子一起碎
            ThirdPersonCamera.ShakeMain(0.1f, 0.15f);
            GameAudio.PlayAt(GameAudio.Pick(impactSound, "SFX/rock_hit"), hitTile.transform.position, 0.6f * sfxVolume, 1.25f, 0.8f, 70f);
        }

        // 破壞照到的地板（Boss 腳下附近不會破）
        if (beamBreaksFloor && arena != null)
        {
            foreach (var t in arena.GetTilesNear(end, currentBeamDiameter * 0.5f))
            {
                if (t.IsPillar || !t.canCollapse) continue;
                if (ArenaFloor.FlatDistance(t.RestPosition, transform.position) < safeRadius) continue;
                t.Collapse(beamFloorBreakDelay, collapseDownTime);
            }
        }

        // 持續傷害：每 beamTickInterval 秒一次（不受受傷無敵時間影響）
        if (Time.time >= nextBeamTick && PlayerAlive && playerHealth != null && PlayerInBeam(end))
        {
            Vector3 away = player.position - end;
            away.y = 0f;
            Vector3 push = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
            playerHealth.TakeDamage(beamTickDamage, push * 2f + Vector3.up * 1f, true);
            nextBeamTick = Time.time + beamTickInterval;
        }
    }

    /// <summary>玩家在光束裡：碰到光束本體，或站在照射點的範圍內</summary>
    bool PlayerInBeam(Vector3 end)
    {
        Vector3 toEnd = player.position - end;
        float dy = toEnd.y;
        toEnd.y = 0f;
        if (toEnd.magnitude <= currentBeamDiameter * 0.5f + 0.5f && dy < 2.5f) return true;

        var hits = Physics.OverlapBox(beam.position, beam.lossyScale * 0.5f, beam.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (h.transform == player || h.transform.IsChildOf(player)) return true;
        return false;
    }

    // =====================================================================
    //  4. 跳躍砸地
    // =====================================================================

    IEnumerator JumpSlam()
    {
        lastJumpTime = Time.time;
        CurrentAction = "跳躍砸地（蓄力）";
        PlayAnim("Jump", jumpWindup);

        // 蹲下蓄力，同時轉向玩家
        float t = 0f;
        while (t < jumpWindup)
        {
            FaceTowards(FlatTo(player.position), TurnSpeed * 2f);
            float k = t / jumpWindup;
            SetBodyScale(new Vector3(1f + 0.15f * k, 1f - 0.25f * k, 1f + 0.15f * k));
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
        SetBodyScale(new Vector3(0.9f, 1.15f, 0.9f));
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
            FaceTowards(flatDir, TurnSpeed);
            yield return null;
        }
        transform.position = target;
        airborne = false;

        // 落地！
        PlayAnim("JumpLand");
        SetBodyScale(new Vector3(1.25f, 0.7f, 1.25f));
        ThirdPersonCamera.ShakeMain(0.5f, 0.6f);
        PlayImpact(0.85f, 1f);

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
        EndAnim();
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

    // ---------------- 模型動畫 ----------------

    void PlayAnim(string state, float secondsUntilHit = -1f)
    {
        if (visual != null) visual.PlayAction(state, secondsUntilHit);
    }

    void EndAnim()
    {
        if (visual != null) visual.EndAction();
    }

    /// <summary>有模型動畫時，程式做的整體前傾 / 伸縮要打折（不然會跟動畫疊加得太誇張）</summary>
    float ProceduralScale => visual != null && visual.HasAnimator ? visual.proceduralLean : 1f;

    void SetBodyScale(Vector3 scale)
    {
        if (bodyPivot != null) bodyPivot.localScale = Vector3.Lerp(Vector3.one, scale, ProceduralScale);
    }

    IEnumerator Lean(float angle, float duration, bool ignoreScale = false)
    {
        if (bodyPivot == null) { yield return new WaitForSeconds(duration); yield break; }
        if (!ignoreScale) angle *= ProceduralScale;
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
        scale = Vector3.Lerp(Vector3.one, scale, ProceduralScale);
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
        GUI.Label(new Rect(0, 16, Screen.width, 30), phase2 ? $"BOSS（二階段）：{CurrentAction}" : $"BOSS：{CurrentAction}", style);
        if (debugKeys)
        {
            var small = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(0, 44, Screen.width, 20), "測試：1 拍地 / 2 怒吼塌陷 / 3 光束 / 4 跳砸 / 5 打到二階段 / 6 直接擊敗", small);
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
