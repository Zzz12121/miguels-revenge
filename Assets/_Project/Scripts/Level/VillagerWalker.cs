using UnityEngine;

/// <summary>
/// 會在村裡走動的村民：沿著 route 來回走，擺動手腳；玩家擋在前面或正在對話時會停下來，結束後繼續走。
/// 路線是世界座標（y 不用填，會貼著地面）。掛在有 MerchantNPC 的物件上就能對話。
/// 有指定 animator（綁好骨架的人形模型）就播待機/走路動畫；沒有的話擺動 body/手/腳這幾個分開的部位。
/// VillageLife 會偶爾叫它離開路線一下（Meet）：走到某個點、看著某個人聊一會，再走回離開的地方接著走。
/// </summary>
public class VillagerWalker : MonoBehaviour
{
    [Header("路線")]
    public Vector3[] route;
    [Tooltip("開：走到最後一點接回第一點（繞圈）。關：走到底再原路折返")]
    public bool loop;
    public float speed = 1.25f;
    [Tooltip("走到路線上每個點之後停多久（秒，隨機）")]
    public Vector2 pause = new Vector2(0.4f, 2.5f);
    [Tooltip("開：往下打射線貼著碰撞體走（石階）。關：只貼地形")]
    public bool followColliders;
    [Tooltip("玩家在前方多近就停下來（公尺）")]
    public float stopDistance = 2.2f;

    [Header("會擺動的部位")]
    public Transform body;
    public Transform armLeft, armRight, footLeft, footRight;

    [Header("人形模型（有就不用上面的部位）")]
    [Tooltip("參數 Speed：0 = 待機、1 = 走路；WalkRate：播放速度倍率")]
    public Animator animator;
    [Tooltip("走路動畫原本的前進速度（m/s，這個村民縮放 1 時）：用來調播放速度，腳步才不會滑")]
    public float animationWalkSpeed = 1.72f;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WalkRateId = Animator.StringToHash("WalkRate");

    const float StrideLength = 1.1f;   // 走一個完整步伐循環前進幾公尺
    const float FootSwing = 0.2f, FootLift = 0.07f, ArmSwing = 24f, BodyBob = 0.025f, BodyRoll = 1.6f;

    readonly RaycastHit[] hits = new RaycastHit[6];
    MerchantNPC npc;
    Transform player;
    Terrain terrain;
    int target = 1, direction = 1;
    float waitLeft, phase, walkBlend, currentY;
    Vector3 bodyRest, footLeftRest, footRightRest;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        var health = PlayerHealth.Find();
        if (health != null) player = health.transform;
        terrain = Terrain.activeTerrain;
        if (body != null) bodyRest = body.localPosition;
        if (footLeft != null) footLeftRest = footLeft.localPosition;
        if (footRight != null) footRightRest = footRight.localPosition;
        currentY = GroundY(transform.position);
        var p = transform.position;
        p.y = currentY;
        transform.position = p;
        waitLeft = Random.Range(0f, pause.y);
        if (animator != null) animator.Play("Locomotion", 0, Random.value); // 大家的待機動作不要同步
    }

    // ---------------- 跟別人聊天 / 去拜訪（VillageLife 安排） ----------------

    enum Social { None, Going, Staying, Returning }
    Social social;
    Vector3 socialStand, socialLook, socialReturn;
    float socialUntil, socialStay;

    /// <summary>沒在聊天、拜訪、也沒被玩家擋住或在跟玩家說話：可以安排</summary>
    public bool IsFree => social == Social.None && !IsStopped();
    /// <summary>站在那裡跟人聊天中</summary>
    public bool Chatting => social == Social.Staying;
    public Vector3 ChatLookAt => socialLook;

    /// <summary>走到 standAt，看著 lookAt 待 seconds 秒，再走回現在的位置接著走原本的路線</summary>
    public void Meet(Vector3 standAt, Vector3 lookAt, float seconds)
    {
        socialStand = standAt;
        socialLook = lookAt;
        socialStay = seconds;
        socialReturn = transform.position;
        social = Social.Going;
    }

    public void SetLook(Vector3 lookAt) => socialLook = lookAt;

    bool UpdateSocial()
    {
        switch (social)
        {
            case Social.Going:
            {
                bool moved = Step(socialStand, out bool arrived);
                if (arrived) { social = Social.Staying; socialUntil = Time.time + socialStay; }
                return moved;
            }
            case Social.Staying:
            {
                Vector3 d = socialLook - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 200f * Time.deltaTime);
                if (Time.time >= socialUntil) social = Social.Returning;
                return false;
            }
            case Social.Returning:
            {
                bool moved = Step(socialReturn, out bool arrived);
                if (arrived) social = Social.None;
                return moved;
            }
        }
        return false;
    }

    void Update()
    {
        bool moved = false;
        if (social != Social.None)
        {
            if (!IsStopped()) moved = UpdateSocial();
        }
        else if (route != null && route.Length >= 2 && !IsStopped())
        {
            if (waitLeft > 0f) waitLeft -= Time.deltaTime;
            else moved = StepTowardTarget();
        }

        walkBlend = Mathf.MoveTowards(walkBlend, moved ? 1f : 0f, Time.deltaTime * 5f);
        if (moved) phase += Time.deltaTime * speed / StrideLength * Mathf.PI * 2f;
        if (animator != null)
        {
            float natural = animationWalkSpeed * Mathf.Max(0.01f, transform.lossyScale.y);
            animator.SetFloat(SpeedId, walkBlend);
            animator.SetFloat(WalkRateId, Mathf.Lerp(1f, speed / natural, walkBlend));
        }
        else ApplyPose();
    }

    bool IsStopped()
    {
        if (npc != null && npc.InConversation) return true;
        if (player == null) return false;
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        return toPlayer.sqrMagnitude < stopDistance * stopDistance && Vector3.Dot(transform.forward, toPlayer) > 0.1f;
    }

    bool StepTowardTarget()
    {
        bool moved = Step(route[target], out bool arrived);
        if (arrived)
        {
            NextTarget();
            waitLeft = Random.Range(pause.x, pause.y);
        }
        return moved;
    }

    /// <summary>往 goal 走一步：回傳這一幀有沒有移動；arrived = 已經到了</summary>
    bool Step(Vector3 goal, out bool arrived)
    {
        Vector3 flat = goal - transform.position;
        flat.y = 0f;
        float distance = flat.magnitude;
        arrived = distance < 0.15f;
        if (arrived) return false;

        Vector3 dir = flat / distance;
        var look = Quaternion.LookRotation(dir);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 240f * Time.deltaTime);
        // 還在大幅轉身時先不要走，轉角才不會滑步
        if (Quaternion.Angle(transform.rotation, look) > 35f) return false;

        Vector3 p = transform.position + dir * Mathf.Min(speed * Time.deltaTime, distance);
        currentY = Mathf.Lerp(currentY, GroundY(p), 1f - Mathf.Exp(-14f * Time.deltaTime));
        p.y = currentY;
        transform.position = p;
        return true;
    }

    void NextTarget()
    {
        if (loop)
        {
            target = (target + 1) % route.Length;
            return;
        }
        if (target + direction >= route.Length || target + direction < 0) direction = -direction;
        target += direction;
    }

    float GroundY(Vector3 p)
    {
        float y = terrain != null ? terrain.SampleHeight(p) + terrain.transform.position.y : p.y;
        if (!followColliders) return y;

        var origin = new Vector3(p.x, y + 3f, p.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, hits, 6f, 1, QueryTriggerInteraction.Ignore);
        float best = y;
        for (int i = 0; i < count; i++)
        {
            var c = hits[i].collider;
            if (c.transform == transform || c is CharacterController || c is TerrainCollider) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        return best;
    }

    void ApplyPose()
    {
        float sin = Mathf.Sin(phase), cos = Mathf.Cos(phase);
        if (footLeft != null) footLeft.localPosition = footLeftRest + new Vector3(0f, Mathf.Max(0f, cos) * FootLift, sin * FootSwing) * walkBlend;
        if (footRight != null) footRight.localPosition = footRightRest + new Vector3(0f, Mathf.Max(0f, -cos) * FootLift, -sin * FootSwing) * walkBlend;
        if (armLeft != null) armLeft.localRotation = Quaternion.Euler(sin * ArmSwing * walkBlend, 0f, 0f);
        if (armRight != null) armRight.localRotation = Quaternion.Euler(-sin * ArmSwing * walkBlend, 0f, 0f);
        if (body != null)
        {
            body.localPosition = bodyRest + Vector3.up * (Mathf.Abs(sin) * BodyBob * walkBlend);
            body.localRotation = Quaternion.Euler(2.5f * walkBlend, 0f, sin * BodyRoll * walkBlend);
        }
    }
}
