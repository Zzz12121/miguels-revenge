using UnityEngine;

/// <summary>
/// 僧侶的過招：習武場完成後，僧侶從祠堂後門走進習武場等玩家（Appear）。跟他對話選擇過招後開始（BeginSpar）。
/// 一回合 = 僧侶出一招（看他抬手的預備動作，舉盾或翻滾躲開）→ 玩家趁他收招的空檔打中他一下。
/// 他沒出招之前（還沒露出空檔）打他會被擋下來，所以一定是一來一往。打滿 roundsRequired 回合就結束：僧侶停手、鞠躬。
/// 輕功：被打中第 1、3 下之後，僧侶一躍跳上圍牆（wallPerches），站一下再從牆上飛撲到玩家面前，落地直接點穴。
/// 點穴（Attack1，快速的戳擊）打中的話玩家會暈眩 stunSeconds 秒（頭上繞著幾顆星星）；舉盾擋下或翻滾躲開就沒事。
/// 不會倒、不會死；只在 arenaCenter 周圍 arenaRadius 公尺內走動，玩家跑遠了就回到中間等。
/// 動作：Animator 的狀態 Locomotion（平常：空手站 / 走路）、Combat（架勢 / 走 / 跑）、Attack1、Attack2、Block、Jump、InAir。
/// </summary>
[DisallowMultipleComponent]
public class MonkSparring : MonoBehaviour, IDamageable
{
    public Animator animator;
    public int roundsRequired = 5;

    [Header("場地")]
    [Tooltip("從哪裡走進習武場（祠堂後門內側）")]
    public Vector3 entryPoint;
    [Tooltip("過招的位置（習武場裡的空地）")]
    public Vector3 arenaCenter;
    public float arenaRadius = 6f;

    [Header("移動")]
    public float walkSpeed = 2.1f;
    public float moveSpeed = 2.6f;
    [Tooltip("停在離玩家多遠的地方出手 (m)")]
    public float standOff = 1.6f;
    [Tooltip("動畫參數 Speed = 實際速度 / 這個值（1 = 走路動畫原本的速度）")]
    public float animationWalkSpeed = 1.7f;

    [Header("出招")]
    public float attackInterval = 1.6f;
    [Tooltip("打得到的距離 (m)")]
    public float reach = 2.4f;
    public float damage = 6f;
    public float pushForce = 8f;
    [Tooltip("Attack1（點穴）/ Attack2（推掌）的長度（秒，已含播放速度）")]
    public float[] attackLength = { 1.15f, 1.5f };
    [Tooltip("Attack1 / Attack2 打中的時間點（佔整招的比例）")]
    public float[] attackHitAt = { 0.38f, 0.47f };

    [Header("點穴")]
    [Tooltip("被點穴之後暈眩幾秒（不能動、不能出招）")]
    public float stunSeconds = 1.6f;

    [Header("輕功")]
    [Tooltip("可以跳上去的牆頭（世界座標，牆的最上緣）")]
    public Vector3[] wallPerches;
    [Tooltip("跳上牆的速度 (m/s)：時間 = 距離 / 速度，限制在 0.6 ~ 1.3 秒")]
    public float leapSpeed = 14f;
    [Tooltip("在牆上站多久 (秒)")]
    public float perchTime = 0.8f;
    [Tooltip("從牆上飛撲下來的速度 (m/s)：時間限制在 0.5 ~ 1.1 秒")]
    public float diveSpeed = 17f;
    [Tooltip("跳躍弧線最高點比起點、終點中較高的那端再高多少 (m)")]
    public float leapArc = 3f;

    public bool Appeared { get; private set; }
    public bool Arrived { get; private set; }
    public bool Fighting { get; private set; }
    public bool Done { get; private set; }
    public int Rounds { get; private set; }
    /// <summary>出完一招、還沒被打中之前（打得到他）</summary>
    public bool Open { get; private set; }
    /// <summary>輕功：跳上牆、在牆上、從牆上飛撲下來</summary>
    public bool InQinggong => phase == Phase.LeapOut || phase == Phase.Perch || phase == Phase.Dive;
    /// <summary>點穴成功、施展輕功的次數（測試、調整難度用）</summary>
    public int Stuns { get; private set; }
    public int Leaps { get; private set; }
    public bool IsDead => false;

    const float MinHitInterval = 0.15f;

    enum Phase { Idle, Attack, LeapOut, Perch, Dive }
    Phase phase;
    int attackIndex, attackCount, perchIndex;
    float phaseTime, phaseLength, cooldown, lastHit = float.NegativeInfinity, recoil, bow, bowTime, groundY;
    bool struck, teleported, diveStrike;
    Vector3 leapFrom, leapTo;
    PlayerHealth player;
    ThirdPersonController playerController;
    MerchantNPC npc;
    VillagerWalker walker;
    Transform spine;

    static readonly int SpeedId = Animator.StringToHash("Speed");

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) playerController = player.GetComponent<ThirdPersonController>();
        npc = GetComponent<MerchantNPC>();
        walker = GetComponent<VillagerWalker>();
    }

    /// <summary>習武場完成：出現在祠堂後門內側，走到空地等玩家</summary>
    public void Appear()
    {
        if (Appeared) return;
        Appeared = true;
        if (npc != null && npc.InConversation) return; // 正在跟玩家說話就先不要瞬間移動（Update 會再試）
        Teleport();
    }

    void Teleport()
    {
        teleported = true;
        if (walker != null) walker.enabled = false;
        groundY = arenaCenter.y;
        transform.position = entryPoint;
        Vector3 d = arenaCenter - entryPoint;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
        if (animator != null) animator.SetFloat("WalkRate", walkSpeed / Mathf.Max(0.1f, animationWalkSpeed)); // 走路動畫配合走路速度，腳才不會滑
        CombatVFX.Impact(entryPoint + Vector3.up * 1f, new Color(0.9f, 0.85f, 0.7f));
    }

    /// <summary>對話結束後開始過招</summary>
    public void BeginSpar()
    {
        if (Done || Fighting) return;
        Fighting = true;
        Open = false;
        phase = Phase.Idle;
        cooldown = 1f;
        if (npc != null) npc.enabled = false; // 過招時不能按 E 對話
        if (animator != null) animator.CrossFadeInFixedTime("Combat", 0.25f);
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.7f, 0.7f, 0f, 1f, 30f);
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (amount <= 0f || !Fighting || InQinggong || Time.time - lastHit < MinHitInterval) return;
        lastHit = Time.time;

        if (!Open)
        {
            // 還沒露出空檔：擋下來
            CombatVFX.Impact(hitPoint, new Color(0.8f, 0.85f, 0.95f));
            GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.8f, 1.2f, 0.1f, 1f, 30f);
            if (phase == Phase.Idle && animator != null) animator.CrossFadeInFixedTime("Block", 0.08f);
            return;
        }

        Open = false;
        Rounds++;
        recoil = 1f;
        CombatVFX.Impact(hitPoint, new Color(0.95f, 0.8f, 0.45f));
        GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.8f, 0.9f, 0.1f, 1f, 30f);
        if (player != null)
        {
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) MoveTo(transform.position + away.normalized * 0.6f);
        }
        if (phase == Phase.Attack && !struck)
        {
            phase = Phase.Idle; // 出招前被打中（剛好在空檔裡）就停手
            if (animator != null) animator.CrossFadeInFixedTime("Combat", 0.15f);
        }
        cooldown = attackInterval;
        if (Rounds >= roundsRequired) Finish();
        else if (Rounds % 2 == 1 && wallPerches != null && wallPerches.Length > 0) StartLeap(); // 第 1、3 下之後施展輕功
    }

    void Finish()
    {
        Fighting = false;
        Done = true;
        Open = false;
        phase = Phase.Idle;
        if (animator != null) animator.CrossFadeInFixedTime("Locomotion", 0.3f);
        if (npc != null) npc.enabled = true;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 1f, 0f, 1f, 40f);
    }

    void Update()
    {
        if (!Appeared) return;
        if (!teleported)
        {
            if (npc == null || !npc.InConversation) Teleport();
            return;
        }
        if (spine == null && animator != null && animator.isHuman) spine = animator.GetBoneTransform(HumanBodyBones.Spine);

        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 3f);
        bow = Mathf.MoveTowards(bow, Done && !Fighting ? 1f : 0f, Time.deltaTime * 1.2f);
        if (Done) bowTime += Time.deltaTime;

        float speed = 0f;
        Vector3 toPlayer = player != null ? player.transform.position - transform.position : Vector3.zero;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (!Arrived)
        {
            speed = Step(arenaCenter, 0f, walkSpeed);
            Vector3 left = arenaCenter - transform.position;
            left.y = 0f;
            if (left.sqrMagnitude < 0.04f) Arrived = true;
        }
        else if (InQinggong)
        {
            UpdateQinggong(toPlayer);
        }
        else if (Fighting && player != null && !player.IsDead)
        {
            bool engaged = (player.transform.position - arenaCenter).sqrMagnitude < (arenaRadius + 6f) * (arenaRadius + 6f);
            if (!engaged)
            {
                phase = Phase.Idle;
                speed = Step(arenaCenter, 0f, moveSpeed);
            }
            else
            {
                if (distance > 0.05f && !(phase == Phase.Attack && struck)) Face(toPlayer);
                if (phase == Phase.Idle)
                {
                    if (distance > standOff + 0.2f) speed = Step(player.transform.position, standOff, distance > 4f ? moveSpeed : walkSpeed);
                    cooldown -= Time.deltaTime;
                    if (cooldown <= 0f && distance < reach - 0.2f && recoil <= 0.01f) BeginAttack(attackCount++ % 2);
                }
                UpdateAttack(toPlayer, distance);
            }
        }
        else if (player != null && distance < 10f && distance > 0.05f && (npc == null || !npc.InConversation))
        {
            Face(toPlayer); // 等玩家（或鞠躬）時看著玩家
        }

        if (animator != null) animator.SetFloat(SpeedId, speed / Mathf.Max(0.1f, animationWalkSpeed), 0.12f, Time.deltaTime);
    }

    // ---------------- 輕功 ----------------

    void StartLeap()
    {
        // 挑一個離自己 5 m 以上的牆頭（輪流換，不要每次都跳同一個地方）
        int pick = -1;
        for (int k = 0; k < wallPerches.Length; k++)
        {
            int i = (perchIndex + k) % wallPerches.Length;
            Vector3 d = wallPerches[i] - transform.position;
            d.y = 0f;
            if (d.magnitude >= 5f) { pick = i; break; }
        }
        if (pick < 0) return;
        perchIndex = pick + 1;
        Leaps++;
        phase = Phase.LeapOut;
        phaseTime = 0f;
        leapFrom = transform.position;
        leapTo = wallPerches[pick];
        phaseLength = FlightTime(leapFrom, leapTo, leapSpeed, 0.6f, 1.3f);
        if (animator != null) animator.CrossFadeInFixedTime("Jump", 0.05f);
        GameAudio.Play("SFX/RPG/cloth3", transform.position, 0.9f, 0.8f, 0.05f, 1f, 30f);
        CombatVFX.Impact(transform.position + Vector3.up * 0.2f, new Color(0.85f, 0.8f, 0.65f));
    }

    void UpdateQinggong(Vector3 toPlayer)
    {
        phaseTime += Time.deltaTime;
        switch (phase)
        {
            case Phase.LeapOut:
            {
                float t = Mathf.Clamp01(phaseTime / phaseLength);
                transform.position = Arc(leapFrom, leapTo, leapArc, t);
                Vector3 dir = leapTo - leapFrom;
                dir.y = 0f;
                Face(dir);
                if (t >= 1f)
                {
                    phase = Phase.Perch;
                    phaseTime = 0f;
                    if (animator != null) animator.CrossFadeInFixedTime("Combat", 0.12f);
                    GameAudio.Play("SFX/RPG/footstep03", transform.position, 0.8f, 0.9f, 0.05f, 1f, 30f);
                }
                break;
            }
            case Phase.Perch:
                if (toPlayer.sqrMagnitude > 0.01f) Face(toPlayer);
                if (phaseTime >= perchTime)
                {
                    // 飛撲：落在玩家面前 standOff 的地方（不會落到場地外面）
                    phase = Phase.Dive;
                    phaseTime = 0f;
                    leapFrom = transform.position;
                    Vector3 target = player != null ? player.transform.position - toPlayer.normalized * standOff : arenaCenter;
                    Vector3 off = target - arenaCenter;
                    off.y = 0f;
                    if (off.magnitude > arenaRadius) off = off.normalized * arenaRadius;
                    leapTo = new Vector3(arenaCenter.x + off.x, groundY, arenaCenter.z + off.z);
                    phaseLength = FlightTime(leapFrom, leapTo, diveSpeed, 0.5f, 1.1f);
                    if (animator != null) animator.CrossFadeInFixedTime("InAir", 0.08f);
                    GameAudio.Play("SFX/RPG/cloth1", transform.position, 0.9f, 1.2f, 0.05f, 1f, 30f);
                }
                break;
            case Phase.Dive:
            {
                float t = Mathf.Clamp01(phaseTime / phaseLength);
                transform.position = Arc(leapFrom, leapTo, leapArc * 0.4f, t);
                if (toPlayer.sqrMagnitude > 0.01f) Face(toPlayer);
                if (t >= 1f)
                {
                    transform.position = leapTo;
                    CombatVFX.Impact(leapTo + Vector3.up * 0.1f, new Color(0.85f, 0.8f, 0.65f));
                    diveStrike = true;
                    BeginAttack(0); // 一落地就點穴
                }
                break;
            }
        }
    }

    static float FlightTime(Vector3 a, Vector3 b, float speed, float min, float max)
    {
        Vector3 d = b - a;
        d.y = 0f;
        return Mathf.Clamp(d.magnitude / Mathf.Max(0.1f, speed), min, max);
    }

    /// <summary>從 a 跳到 b 的拋物線：最高點比兩端中較高的那端再高 h</summary>
    static Vector3 Arc(Vector3 a, Vector3 b, float h, float t)
    {
        Vector3 p = Vector3.Lerp(a, b, t);
        float top = Mathf.Max(a.y, b.y) + h;
        float c = 2f * top - 0.5f * (a.y + b.y); // 二次貝茲曲線的控制點高度（t = 0.5 時剛好在 top）
        p.y = (1f - t) * (1f - t) * a.y + 2f * (1f - t) * t * c + t * t * b.y;
        return p;
    }

    // ---------------- 出招 ----------------

    void BeginAttack(int index)
    {
        phase = Phase.Attack;
        phaseTime = 0f;
        struck = false;
        attackIndex = index;
        if (animator != null) animator.CrossFadeInFixedTime(attackIndex == 0 ? "Attack1" : "Attack2", 0.1f);
        GameAudio.Play("SFX/RPG/cloth2", transform.position, 0.7f, 1.1f, 0.1f, 1f, 25f);
    }

    void UpdateAttack(Vector3 toPlayer, float distance)
    {
        if (phase != Phase.Attack) return;
        phaseTime += Time.deltaTime;
        float length = attackLength[attackIndex];
        if (!struck && phaseTime >= length * attackHitAt[attackIndex])
        {
            struck = true;
            MoveTo(transform.position + transform.forward * 0.3f);
            Strike(toPlayer, distance);
        }
        if (phaseTime >= length)
        {
            phase = Phase.Idle;
            cooldown = attackInterval;
            if (diveStrike) cooldown += 0.6f;
            diveStrike = false;
            Open = true; // 收招：露出空檔，這時打得到他
        }
    }

    void Strike(Vector3 toPlayer, float distance)
    {
        GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.6f, 0.8f, 0.1f, 1f, 25f);
        if (player == null || distance > reach || distance < 0.01f) return;
        Vector3 dir = toPlayer / distance;
        if (Vector3.Dot(transform.forward, dir) < 0.4f) return; // 只打得到正前方：翻滾到側面就躲開了

        // 有沒有真的打中（舉盾擋下、翻滾躲開都不算）：TakeDamage 裡真的扣血時才會發 OnDamaged
        bool landed = false;
        void OnHit(float _) => landed = true;
        bool point = attackIndex == 0;
        player.OnDamaged += OnHit;
        player.TakeDamage(point ? damage * 0.5f : damage, dir * (point ? pushForce * 0.3f : pushForce) + Vector3.up * 2f, false, "被僧侶打倒了");
        player.OnDamaged -= OnHit;
        if (point && landed) StunPlayer();
    }

    /// <summary>點穴：玩家暈眩，頭上繞著星星</summary>
    void StunPlayer()
    {
        Stuns++;
        if (playerController != null) playerController.Stun(stunSeconds);
        DizzyStars.Show(player.transform, stunSeconds);
        GameAudio.Play("SFX/RPG/metalPot2", player.transform.position, 0.6f, 1.6f, 0.05f, 1f, 25f);
        cooldown += stunSeconds; // 不趁人之危：等玩家醒過來再出下一招
    }

    float Step(Vector3 target, float stopAt, float maxSpeed)
    {
        Vector3 d = target - transform.position;
        d.y = 0f;
        float dist = d.magnitude - stopAt;
        if (dist <= 0.02f) return 0f;
        if (!Fighting || d.magnitude > 0.05f) Face(d);
        float move = Mathf.Min(dist, maxSpeed * Time.deltaTime);
        var before = transform.position;
        MoveTo(transform.position + d.normalized * move);
        return (transform.position - before).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    void MoveTo(Vector3 p)
    {
        if (Arrived)
        {
            Vector3 off = p - arenaCenter;
            off.y = 0f;
            if (off.magnitude > arenaRadius) off = off.normalized * arenaRadius;
            p = arenaCenter + off;
        }
        transform.position = new Vector3(p.x, groundY, p.z);
    }

    void Face(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        var look = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 360f * Time.deltaTime);
    }

    // 受擊往後仰、結束時鞠躬：動畫跑完之後再疊上去
    void LateUpdate()
    {
        if (spine == null) return;
        float b = bow * Mathf.Clamp01(1.6f - bowTime * 0.4f); // 鞠躬一下就直起身
        float lean = -recoil * 20f + b * 35f;
        if (Mathf.Abs(lean) > 0.01f) spine.rotation = Quaternion.AngleAxis(lean, transform.right) * spine.rotation;
    }
}
