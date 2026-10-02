using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 小怪 AI。移動用 ThirdPersonController（跟玩家同一套），攻擊用 EnemyCombat（跟玩家同一套揮劍）。
///
/// 狀態：
///   Idle     待機：站在原位，玩家進入警戒範圍（而且看得到）就發現玩家
///   Alerted  發現：頭上冒「!」愣一下，同時叫醒附近的同伴
///   Chase    追擊：靠近玩家、在攻擊距離內打 1~3 下連擊；被打中會硬直
///   Return   放棄：玩家跑太遠或自己離原位太遠，就走回原位並回滿血
///   Dropping 從天而降（陷阱小怪）：由 AmbushTrigger 呼叫 BeginDrop，落地時砸傷附近的玩家
///   Dead     死亡
/// 玩家死亡重生時，所有小怪會復活並回到原位。
/// </summary>
[RequireComponent(typeof(ThirdPersonController), typeof(EnemyCombat), typeof(EnemyHealth))]
public class EnemyAI : MonoBehaviour
{
    public enum State { Idle, Alerted, Chase, Return, Dropping, Dead }

    [Header("警戒")]
    [Tooltip("玩家進入這個半徑就會被發現 (m)")]
    public float alertRadius = 9f;
    [Tooltip("高度差超過這個值就不會發現（避免隔著樓層發現玩家）")]
    public float alertHeight = 4f;
    [Tooltip("需要看得到玩家才會發現（中間有牆擋住就不會）")]
    public bool requireLineOfSight = true;
    [Tooltip("發現玩家後愣住多久才開始追 (秒)，頭上會出現 !")]
    public float reactionTime = 0.5f;
    [Tooltip("發現玩家時，附近這個半徑內的同伴也會一起被叫醒 (m)")]
    public float callForHelpRadius = 6f;

    [Header("追擊")]
    [Tooltip("離玩家超過這個距離就衝刺（跟玩家按 Shift 一樣快）；0 = 不衝刺")]
    public float sprintDistance = 7f;
    [Tooltip("離開原本位置超過這個距離就放棄、走回去 (m)")]
    public float leashRadius = 20f;
    [Tooltip("玩家跑到這麼遠就放棄 (m)")]
    public float giveUpDistance = 18f;
    [Tooltip("和其他小怪保持的距離，避免擠成一團 (m)")]
    public float separationRadius = 1.6f;

    [Header("攻擊")]
    [Tooltip("離玩家這麼近就開始攻擊 (m)")]
    public float attackRange = 2.3f;
    [Tooltip("兩輪連擊之間休息多久 (秒，隨機範圍)")]
    public Vector2 attackInterval = new Vector2(0.8f, 1.8f);
    [Tooltip("一輪最多打幾下（跟玩家一樣最多三段）")]
    [Range(1, 3)] public int maxComboHits = 3;

    [Header("從天而降（陷阱小怪用）")]
    [Tooltip("落地時砸到玩家的範圍 (m)")]
    public float stompRadius = 2f;
    public float stompDamage = 15f;

    public State CurrentState => state;
    /// <summary>正在跟玩家戰鬥中</summary>
    public bool IsEngaged => state == State.Alerted || state == State.Chase || state == State.Dropping;

    static readonly List<EnemyAI> All = new List<EnemyAI>();

    ThirdPersonController controller;
    EnemyCombat combat;
    EnemyHealth health;
    CharacterController cc;
    Transform player;
    PlayerHealth playerHealth;

    Vector3 homePos;
    Quaternion homeRot;
    State state = State.Idle;
    float stateTimer;
    float staggerTimer;
    float nextAttackTime;
    bool wasAttacking;

    GameObject dropMarker;
    float dropStartY, dropGroundY;
    static Material dropMarkerMat;
    readonly RaycastHit[] rayHits = new RaycastHit[16];

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        combat = GetComponent<EnemyCombat>();
        health = GetComponent<EnemyHealth>();
        cc = GetComponent<CharacterController>();
        controller.aiControlled = true;

        homePos = transform.position;
        homeRot = transform.rotation;

        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        combat.target = player;
        health.OnDamaged += OnDamaged;
        health.OnDied += OnDied;
    }

    void OnEnable()
    {
        All.Add(this);
        if (playerHealth != null) playerHealth.OnRespawned += ResetEnemy;
    }

    void OnDisable()
    {
        All.Remove(this);
        if (playerHealth != null) playerHealth.OnRespawned -= ResetEnemy;
        ClearDropMarker();
    }

    // =====================================================================

    void Update()
    {
        if (state == State.Dead) return;

        // 掉出場地
        if (transform.position.y < -20f) { health.Kill(); return; }

        float dt = Time.deltaTime;
        stateTimer += dt;
        if (staggerTimer > 0f) staggerTimer -= dt;

        // 一輪連擊結束 → 休息一下再打
        if (wasAttacking && !combat.IsAttacking)
            nextAttackTime = Time.time + Random.Range(attackInterval.x, attackInterval.y);
        wasAttacking = combat.IsAttacking;

        health.ForceShowBar = IsEngaged;
        bool playerAlive = player != null && (playerHealth == null || !playerHealth.IsDead);

        switch (state)
        {
            case State.Idle:
                controller.SetAIInput(Vector3.zero);
                TurnTowards(homeRot * Vector3.forward, 180f);
                if (playerAlive && CanDetectPlayer()) Alert(true);
                break;

            case State.Alerted:
                controller.SetAIInput(Vector3.zero);
                if (playerAlive) TurnTowards(FlatToPlayer(), 360f);
                if (stateTimer >= reactionTime) SetState(State.Chase);
                break;

            case State.Chase:
                UpdateChase(playerAlive);
                break;

            case State.Return:
                UpdateReturn(playerAlive);
                break;

            case State.Dropping:
                UpdateDrop();
                break;
        }
    }

    void UpdateChase(bool playerAlive)
    {
        if (!playerAlive) { SetState(State.Return); return; }

        Vector3 to = FlatToPlayer();
        float dist = to.magnitude;
        if (dist > giveUpDistance || FlatDistance(transform.position, homePos) > leashRadius)
        {
            SetState(State.Return);
            return;
        }

        // 硬直中或正在揮劍：不移動（揮劍時的轉向和踏步由 EnemyCombat 處理）
        if (staggerTimer > 0f || combat.IsAttacking)
        {
            controller.SetAIInput(Vector3.zero);
            return;
        }

        if (dist > attackRange * 0.8f)
        {
            Vector3 dir = to.normalized + Separation();
            bool sprint = sprintDistance > 0f && dist > sprintDistance;
            controller.SetAIInput(dir.normalized, sprint);
        }
        else
        {
            controller.SetAIInput(Vector3.zero);
            TurnTowards(to, 360f);
        }

        if (dist <= attackRange && Time.time >= nextAttackTime && Vector3.Angle(transform.forward, to) < 50f)
            combat.StartCombo(Random.Range(1, maxComboHits + 1));
    }

    void UpdateReturn(bool playerAlive)
    {
        // 玩家又靠近（而且在自己的地盤內）→ 再追一次
        if (playerAlive && CanDetectPlayer() && FlatDistance(player.position, homePos) < leashRadius)
        {
            SetState(State.Chase);
            return;
        }

        Vector3 to = homePos - transform.position;
        to.y = 0f;
        if (to.magnitude < 0.4f)
        {
            controller.SetAIInput(Vector3.zero);
            health.HealFull();
            SetState(State.Idle);
            return;
        }
        controller.SetAIInput((to.normalized + Separation() * 0.5f).normalized);
    }

    // =====================================================================
    //  發現玩家 / 被打
    // =====================================================================

    bool CanDetectPlayer()
    {
        Vector3 d = player.position - transform.position;
        if (Mathf.Abs(d.y) > alertHeight) return false;
        d.y = 0f;
        if (d.magnitude > alertRadius) return false;
        return !requireLineOfSight || HasLineOfSight();
    }

    bool HasLineOfSight()
    {
        Vector3 a = transform.position + Vector3.up * 0.6f;
        Vector3 b = player.position + Vector3.up * 0.6f;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return true;
        int n = Physics.RaycastNonAlloc(a, d / len, rayHits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider is CharacterController) continue; // 角色不算遮擋
            return false;
        }
        return true;
    }

    /// <summary>發現玩家。callForHelp = true 時會叫醒附近的同伴</summary>
    public void Alert(bool callForHelp)
    {
        if (state != State.Idle && state != State.Return) return;
        SetState(State.Alerted);

        if (!callForHelp) return;
        foreach (var other in All)
        {
            if (other == this) continue;
            if (FlatDistance(other.transform.position, transform.position) <= callForHelpRadius)
                other.Alert(false);
        }
    }

    void OnDamaged(float amount)
    {
        staggerTimer = health.staggerTime;
        combat.CancelAttack();
        nextAttackTime = Mathf.Max(nextAttackTime, Time.time + health.staggerTime + 0.2f);

        // 從背後偷襲 → 直接進入戰鬥（不愣住）
        if (state == State.Idle || state == State.Return || state == State.Alerted)
        {
            SetState(State.Chase);
            foreach (var other in All)
                if (other != this && FlatDistance(other.transform.position, transform.position) <= callForHelpRadius)
                    other.Alert(false);
        }
    }

    void OnDied()
    {
        SetState(State.Dead);
        combat.CancelAttack();
        controller.SetAIInput(Vector3.zero);
        ClearDropMarker();
    }

    /// <summary>玩家重生時：復活並回到原位</summary>
    public void ResetEnemy()
    {
        combat.CancelAttack();
        ClearDropMarker();
        health.Revive();

        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.SetPositionAndRotation(homePos, homeRot);
        cc.enabled = true;

        controller.enabled = true;
        controller.ResetMotion();
        controller.SetAIInput(Vector3.zero);
        staggerTimer = 0f;
        nextAttackTime = 0f;
        wasAttacking = false;
        SetState(State.Idle);
    }

    // =====================================================================
    //  從天而降（陷阱小怪）
    // =====================================================================

    /// <summary>從 landingPoint 正上方 height 公尺處掉下來，落地時砸傷附近的玩家</summary>
    public void BeginDrop(Vector3 landingPoint, float height)
    {
        dropGroundY = FindGroundY(landingPoint, height, landingPoint.y - 1f);

        Vector3 start = new Vector3(landingPoint.x, dropGroundY + height, landingPoint.z);
        cc.enabled = false;
        transform.position = start;
        cc.enabled = true;
        controller.enabled = true;
        controller.ResetMotion();
        controller.SetAIInput(Vector3.zero);
        if (player != null) FaceInstant(FlatToPlayer());

        dropStartY = start.y;
        dropMarker = CreateDropMarker(new Vector3(landingPoint.x, dropGroundY + 0.03f, landingPoint.z));
        SetState(State.Dropping);
    }

    void UpdateDrop()
    {
        controller.SetAIInput(Vector3.zero);

        // 地上的紅圈隨著小怪落下越來越大
        if (dropMarker != null)
        {
            float k = Mathf.InverseLerp(dropStartY, dropGroundY + 1f, transform.position.y);
            float d = Mathf.Lerp(0.6f, stompRadius * 2f, k);
            dropMarker.transform.localScale = new Vector3(d, 0.01f, d);
        }

        if ((stateTimer > 0.15f && controller.IsGrounded) || stateTimer > 5f) Land();
    }

    void Land()
    {
        ClearDropMarker();
        ThirdPersonCamera.ShakeMain(0.25f, 0.25f);
        GameAudio.Play("SFX/rock_hit", transform.position, 0.45f, 1.4f, 0.08f, 1f, 40f);

        if (player != null && playerHealth != null && !playerHealth.IsDead)
        {
            Vector3 to = player.position - transform.position;
            float dy = to.y;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.01f ? to.normalized : transform.forward;
            if (to.magnitude <= stompRadius && Mathf.Abs(dy) < 2.5f)
                playerHealth.TakeDamage(stompDamage, dir * 7f + Vector3.up * 6f);

            // 剛好踩在玩家頭上 → 滑下來
            if (dy < -1f) controller.AddImpulse(-dir * 6f);
        }

        nextAttackTime = Time.time + 0.6f;
        SetState(State.Chase);
    }

    float FindGroundY(Vector3 point, float height, float fallback)
    {
        Vector3 origin = new Vector3(point.x, point.y + height + 5f, point.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayHits, height + 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++)
        {
            if (rayHits[i].collider is CharacterController) continue;
            if (rayHits[i].point.y > best) best = rayHits[i].point.y; // 最上面的地面
        }
        return float.IsNegativeInfinity(best) ? fallback : best;
    }

    static GameObject CreateDropMarker(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "DropMarker";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.6f, 0.01f, 0.6f);

        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (dropMarkerMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                dropMarkerMat = new Material(shader);
                dropMarkerMat.SetColor("_BaseColor", new Color(0.9f, 0.1f, 0.1f));
                dropMarkerMat.SetColor("_Color", new Color(0.9f, 0.1f, 0.1f));
            }
        }
        if (dropMarkerMat != null) r.sharedMaterial = dropMarkerMat;
        return go;
    }

    void ClearDropMarker()
    {
        if (dropMarker != null) Destroy(dropMarker);
        dropMarker = null;
    }

    // =====================================================================
    //  工具
    // =====================================================================

    void SetState(State s)
    {
        state = s;
        stateTimer = 0f;
    }

    Vector3 FlatToPlayer()
    {
        Vector3 v = player.position - transform.position;
        v.y = 0f;
        return v;
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    Vector3 Separation()
    {
        Vector3 push = Vector3.zero;
        foreach (var o in All)
        {
            if (o == this || o.state == State.Dead) continue;
            Vector3 d = transform.position - o.transform.position;
            d.y = 0f;
            float m = d.magnitude;
            if (m > 0.001f && m < separationRadius) push += d / m * (1f - m / separationRadius);
        }
        return push;
    }

    void TurnTowards(Vector3 flatDir, float degreesPerSecond)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flatDir),
                                                      degreesPerSecond * Time.deltaTime);
    }

    void FaceInstant(Vector3 flatDir)
    {
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(flatDir);
    }

    void OnGUI()
    {
        if (state != State.Alerted) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.9f);
        if (sp.z <= 0f || sp.z > 40f) return;

        var style = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        GUI.color = new Color(1f, 0.85f, 0.2f);
        GUI.Label(new Rect(sp.x - 20f, Screen.height - sp.y - 40f, 40f, 40f), "!", style);
        GUI.color = Color.white;
    }

    void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePos : transform.position;
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, alertRadius);   // 黃 = 警戒範圍
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(home, leashRadius);                 // 灰 = 最遠追到哪
    }
}
