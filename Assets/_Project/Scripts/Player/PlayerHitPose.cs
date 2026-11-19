using UnityEngine;

/// <summary>
/// 玩家的三種特殊受擊姿勢（純視覺，用程式擺骨架，疊在 Animator 的 Idle 上）：
///   被打飛 KnockedFlying：上升時蜷縮（膝蓋抱到胸前）→ 下墜時張開四肢、身體往後倒 → 仰躺在地上 → 坐起來 → 站起來
///   毒氣 PoisonGas：雙手摀住口鼻、駝背低頭、全身狂抖，每隔一下咳一次往前抽
///   防禦 GuardBrace：雙臂交叉擋在臉前、壓低重心、腳釘在原地；每被打一下上身往後震一下
///
/// 由 PlayerHealth.OnHitReaction 觸發；移動、無敵、僵直時間都由 PlayerHealth 決定，這裡只擺姿勢。
/// 擺姿勢的期間 PlayerVisualAnimator 會讓出骨架（OwnsPose），不會切到受擊動畫或下落動畫。
/// 想在編輯器預覽：Play 時在元件上按右鍵 → Preview 開頭的選單。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)] // 在 PlayerVisualAnimator (100) 之後擺姿勢
public sealed class PlayerHitPose : MonoBehaviour
{
    [Header("被打飛")]
    [Tooltip("空中身體往後倒的角度：起飛時 / 快落地時（負 = 往後仰）")]
    public float airPitchStart = -20f, airPitchEnd = -75f;
    [Tooltip("躺在地上時身體離地多高 (m，臀部骨頭)")]
    public float lieHipHeight = 0.16f;

    [Header("毒氣")]
    [Tooltip("全身抖動的角度 (度) 與頻率 (Hz)")]
    public float gasShakeAngle = 7f;
    public float gasShakeFrequency = 22f;
    [Tooltip("每隔幾秒咳一下（上身往前抽）")]
    public float gasCoughInterval = 0.36f;
    [Tooltip("咳嗽時從嘴巴噴出來的小團毒氣顏色（a = 0 就不噴）")]
    public Color gasCoughColor = new Color(0.45f, 0.85f, 0.25f, 0.55f);

    [Header("防禦")]
    [Tooltip("防禦時往下蹲多少 (m)")]
    public float guardCrouch = 0.1f;
    [Tooltip("每被打一下上身往後震的角度 (度)")]
    public float guardRecoilAngle = 14f;

    /// <summary>正在擺受擊姿勢（PlayerVisualAnimator 這段時間不碰骨架）</summary>
    public bool OwnsPose => active;
    public PlayerHitReaction Current => active ? type : PlayerHitReaction.Auto;

    Animator animator;
    PlayerHealth health;
    ThirdPersonController controller;
    PlayerWeaponPresenter weapon;
    Transform actor;

    Transform hips, spine, chest, upperChest, neck, head;
    Transform lUpperArm, lForearm, lHand, rUpperArm, rForearm, rHand;
    Transform lUpperLeg, lLowerLeg, lFoot, rUpperLeg, rLowerLeg, rFoot;
    float armLength = 0.55f, legLength = 0.85f;

    bool active;
    PlayerHitReaction type;
    float startedAt, duration;
    float lastHitAt;
    // 被打飛
    bool leftGround, landed;
    float landedAt, launchVy, descendAt;
    int lastCough;
    float releasedAt = float.PositiveInfinity; // 僵直結束後玩家開始移動 / 翻滾：0.12 秒內放掉姿勢

    void Awake()
    {
        health = GetComponent<PlayerHealth>();
        controller = GetComponent<ThirdPersonController>();
        actor = transform;
        ResolveAnimator();
    }

    void OnEnable() { if (health != null) { health.OnHitReaction += Play; health.OnDied += Stop; health.OnRespawned += Stop; } }
    void OnDisable() { if (health != null) { health.OnHitReaction -= Play; health.OnDied -= Stop; health.OnRespawned -= Stop; } active = false; }

    void ResolveAnimator()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (weapon == null) weapon = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (animator == null || !animator.isHuman || hips != null) return;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        hips = B(HumanBodyBones.Hips);
        spine = B(HumanBodyBones.Spine);
        chest = B(HumanBodyBones.Chest);
        upperChest = B(HumanBodyBones.UpperChest);
        neck = B(HumanBodyBones.Neck);
        head = B(HumanBodyBones.Head);
        lUpperArm = B(HumanBodyBones.LeftUpperArm); lForearm = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
        rUpperArm = B(HumanBodyBones.RightUpperArm); rForearm = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
        lUpperLeg = B(HumanBodyBones.LeftUpperLeg); lLowerLeg = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
        rUpperLeg = B(HumanBodyBones.RightUpperLeg); rLowerLeg = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
        if (lUpperArm && lForearm && lHand)
            armLength = Vector3.Distance(lUpperArm.position, lForearm.position) + Vector3.Distance(lForearm.position, lHand.position);
        if (lUpperLeg && lLowerLeg && lFoot)
            legLength = Vector3.Distance(lUpperLeg.position, lLowerLeg.position) + Vector3.Distance(lLowerLeg.position, lFoot.position);
    }

    // ---------------- 對外 ----------------

    /// <summary>開始擺一種受擊姿勢（PlayerHealth 決定好姿勢後會自動呼叫）</summary>
    public void Play(PlayerHitReaction reaction, Vector3 knockDir)
    {
        if (reaction != PlayerHitReaction.KnockedFlying && reaction != PlayerHitReaction.PoisonGas
            && reaction != PlayerHitReaction.GuardBrace) return;
        ResolveAnimator();
        if (hips == null) return;
        lastHitAt = Time.time;
        // 同一種姿勢連續被打（例如骷髏接力砍在防禦上）：不重新開始，只延長並震一下
        if (active && type == reaction && reaction != PlayerHitReaction.KnockedFlying)
        {
            duration = Mathf.Max(duration, Time.time - startedAt + DurationFor(reaction));
            releasedAt = float.PositiveInfinity;
            return;
        }
        type = reaction;
        active = true;
        startedAt = Time.time;
        duration = DurationFor(reaction);
        leftGround = landed = false;
        lastCough = -1;
        releasedAt = float.PositiveInfinity;
        descendAt = float.PositiveInfinity;
        launchVy = controller != null ? Mathf.Max(1f, controller.VerticalVelocity) : 7f;
        if (weapon != null) weapon.SheatheNow();
        if (animator != null && animator.HasState(0, Animator.StringToHash("Idle")))
            animator.CrossFadeInFixedTime("Idle", 0.08f, 0, 0f);
    }

    public void Stop() => active = false;

    float DurationFor(PlayerHitReaction r)
    {
        if (health == null) return 1f;
        return r switch
        {
            PlayerHitReaction.PoisonGas => health.poisonGasStunTime + 0.25f,
            PlayerHitReaction.GuardBrace => health.guardBraceStunTime + 0.2f,
            _ => 4f, // 被打飛：落地之後才知道多久，見 UpdateKnockTimeline
        };
    }

    [ContextMenu("Preview/被打飛 (Knocked Flying)")]
    void PreviewKnock() => health?.TakeDamage(0.01f, -transform.forward * 6f + Vector3.up * 7f, false, "預覽", true, PlayerHitReaction.KnockedFlying);
    [ContextMenu("Preview/毒氣 (Poison Gas)")]
    void PreviewGas() => health?.TakeDamage(0.01f, Vector3.zero, false, "預覽", true, PlayerHitReaction.PoisonGas);
    [ContextMenu("Preview/防禦 (Guard Brace)")]
    void PreviewGuard() => health?.TakeDamage(0.01f, -transform.forward * 6f, false, "預覽", true, PlayerHitReaction.GuardBrace);

    // ---------------- 每幀 ----------------

    void LateUpdate()
    {
        if (!active) return;
        if (health != null && health.IsDead) { active = false; return; }
        float t = Time.time - startedAt;
        if (type == PlayerHitReaction.KnockedFlying) UpdateKnockTimeline(t);
        if (t >= duration) { active = false; return; }
        // 僵直結束、玩家已經在移動或翻滾：很快放掉姿勢，不要用站姿滑著走
        if (controller != null && float.IsPositiveInfinity(releasedAt) && !controller.IsStunned
            && (controller.IsRolling || controller.IsAirDashing || controller.CurrentSpeed > 0.6f || !controller.IsGrounded && type != PlayerHitReaction.KnockedFlying))
            releasedAt = Time.time;
        float release = float.IsPositiveInfinity(releasedAt) ? 1f : 1f - Smooth((Time.time - releasedAt) / 0.12f);
        if (release <= 0f || controller != null && controller.IsRolling) { active = false; return; }
        if (animator == null || !animator.enabled || hips == null) return;
        if (CalligraphyPlatformClimb.IsClimbing(actor)) return;

        globalWeight = release;
        switch (type)
        {
            case PlayerHitReaction.KnockedFlying: PoseKnock(t); break;
            case PlayerHitReaction.PoisonGas: PoseGas(t); break;
            case PlayerHitReaction.GuardBrace: PoseGuard(t); break;
        }
    }

    // =====================================================================
    //  被打飛
    // =====================================================================

    float LieTime => health != null ? health.knockDownLieTime : 0.55f;
    float GetUpTime => health != null ? health.knockDownGetUpTime : 0.6f;

    void UpdateKnockTimeline(float t)
    {
        bool grounded = controller == null || controller.IsGrounded;
        if (!grounded) leftGround = true;
        float vy = controller != null ? controller.VerticalVelocity : 0f;
        if (leftGround && vy <= 0f && float.IsPositiveInfinity(descendAt)) descendAt = Time.time;
        if (!landed && t > 0.15f && grounded && (leftGround || t > 0.4f))
        {
            landed = true;
            landedAt = Time.time;
            if (float.IsPositiveInfinity(descendAt)) descendAt = Time.time;
            duration = t + LieTime + GetUpTime;
            CombatVFX.Impact(actor.position + Vector3.up * 0.15f, new Color(0.75f, 0.68f, 0.55f));
        }
    }

    /// <summary>一組姿勢參數（身體座標：x = 右、y = 上、z = 前；手腳的位置以手長 / 腳長為單位，從肩膀 / 大腿根量）</summary>
    struct KnockPose
    {
        public float pitch, ground, curl, headPitch;
        public Vector3 hand, foot; // 左邊的手 / 腳；右邊自動左右鏡像
        public Vector3 elbowPole, kneePole;

        public static KnockPose Lerp(KnockPose a, KnockPose b, float k) => new KnockPose
        {
            pitch = Mathf.Lerp(a.pitch, b.pitch, k), ground = Mathf.Lerp(a.ground, b.ground, k),
            curl = Mathf.Lerp(a.curl, b.curl, k), headPitch = Mathf.Lerp(a.headPitch, b.headPitch, k),
            hand = Vector3.Lerp(a.hand, b.hand, k), foot = Vector3.Lerp(a.foot, b.foot, k),
            elbowPole = Vector3.Slerp(a.elbowPole, b.elbowPole, k), kneePole = Vector3.Slerp(a.kneePole, b.kneePole, k),
        };
    }

    // 蜷縮：膝蓋抱到胸前、低頭、雙手抱腿
    static readonly KnockPose Tuck = new KnockPose
    {
        curl = 38f, headPitch = 28f,
        hand = new Vector3(0.05f, -0.55f, 0.6f), foot = new Vector3(-0.05f, -0.3f, 0.45f),
        elbowPole = new Vector3(-1f, -0.3f, 0f), kneePole = new Vector3(0f, 0.4f, 1f),
    };
    // 張開：四肢往外甩、身體反弓、頭往後仰
    static readonly KnockPose Spread = new KnockPose
    {
        curl = -18f, headPitch = -22f,
        hand = new Vector3(-0.85f, 0.4f, 0.15f), foot = new Vector3(-0.28f, -0.9f, 0.2f),
        elbowPole = new Vector3(0f, -0.4f, -1f), kneePole = new Vector3(0f, 0f, 1f),
    };
    // 仰躺在地上：手腳攤開貼地
    static readonly KnockPose Lie = new KnockPose
    {
        pitch = -86f, ground = 1f, curl = -4f, headPitch = -8f,
        hand = new Vector3(-0.8f, 0.25f, -0.3f), foot = new Vector3(-0.22f, -0.95f, -0.08f),
        elbowPole = new Vector3(0f, 0f, -1f), kneePole = new Vector3(0f, 0f, 1f),
    };
    // 坐起來：手撐在身後、膝蓋彎起來
    static readonly KnockPose Sit = new KnockPose
    {
        pitch = -28f, ground = 1f, curl = 22f, headPitch = 10f,
        hand = new Vector3(-0.35f, -0.85f, -0.35f), foot = new Vector3(-0.15f, -0.45f, 0.75f),
        elbowPole = new Vector3(0f, 0f, -1f), kneePole = new Vector3(0f, 0.5f, 1f),
    };
    // 單膝跪起（站起來的前一刻）
    static readonly KnockPose Kneel = new KnockPose
    {
        pitch = 8f, ground = 0.55f, curl = 18f, headPitch = 6f,
        hand = new Vector3(-0.2f, -0.6f, 0.45f), foot = new Vector3(-0.12f, -0.55f, 0.55f),
        elbowPole = new Vector3(-0.5f, 0f, -1f), kneePole = new Vector3(0f, 0f, 1f),
    };

    void PoseKnock(float t)
    {
        KnockPose pose;
        float weight = 1f;
        if (!landed)
        {
            float vy = controller != null ? controller.VerticalVelocity : 0f;
            // 上升時蜷縮，開始往下掉之後 0.22 秒內張開
            float open = float.IsPositiveInfinity(descendAt) ? 0f : Smooth((Time.time - descendAt) / 0.22f);
            pose = KnockPose.Lerp(Tuck, Spread, open);
            pose.pitch = Mathf.Lerp(airPitchStart, airPitchEnd, Mathf.InverseLerp(launchVy, -launchVy * 0.8f, vy));
            weight = Smooth(t / 0.07f);
        }
        else
        {
            float since = Time.time - landedAt;
            if (since < LieTime)
            {
                // 摔到地上：0.12 秒內從張開變成躺平，落地時身體彈一下
                float k = Smooth(since / 0.12f);
                pose = KnockPose.Lerp(Spread, Lie, k);
                pose.pitch = Mathf.Lerp(airPitchEnd, Lie.pitch, k) + Mathf.Sin(Mathf.Clamp01(since / 0.3f) * Mathf.PI) * 6f;
                pose.ground = k;
            }
            else
            {
                float g = Mathf.Clamp01((since - LieTime) / GetUpTime);
                if (g < 0.4f) pose = KnockPose.Lerp(Lie, Sit, Smooth(g / 0.4f));
                else if (g < 0.72f) pose = KnockPose.Lerp(Sit, Kneel, Smooth((g - 0.4f) / 0.32f));
                else { pose = Kneel; weight = 1f - Smooth((g - 0.72f) / 0.28f); }
            }
        }
        ApplyKnockPose(pose, weight * globalWeight);
    }

    void ApplyKnockPose(KnockPose p, float w)
    {
        if (w <= 0.001f) return;
        Vector3 right = actor.right;
        float groundY = health != null ? health.FeetY : actor.position.y;

        // 整個身體繞臀部往後倒，躺地時把臀部降到地面
        hips.rotation = Quaternion.AngleAxis(p.pitch * w, right) * hips.rotation;
        if (p.ground > 0f)
        {
            float drop = Mathf.Max(0f, hips.position.y - (groundY + lieHipHeight));
            hips.position += Vector3.down * drop * p.ground * w;
        }
        Quaternion body = Quaternion.AngleAxis(p.pitch * w, right) * actor.rotation;

        BendSpine(p.curl * w, right);
        PitchHead(p.headPitch * w, right);

        // 手腳（左右鏡像）
        ReachRelative(lUpperArm, lForearm, lHand, body, p.hand, p.elbowPole, armLength, w, false);
        ReachRelative(rUpperArm, rForearm, rHand, body, p.hand, p.elbowPole, armLength, w, true);
        ReachRelative(lUpperLeg, lLowerLeg, lFoot, body, p.foot, p.kneePole, legLength, w, false);
        ReachRelative(rUpperLeg, rLowerLeg, rFoot, body, p.foot, p.kneePole, legLength, w, true);
    }

    // =====================================================================
    //  毒氣：摀住口鼻、狂抖
    // =====================================================================

    void PoseGas(float t)
    {
        float w = Envelope(t, 0.12f, 0.25f);
        if (w <= 0.001f) return;
        Vector3 right = actor.right, fwd = actor.forward, up = actor.up;
        Vector3 lPin = lFoot ? lFoot.position : Vector3.zero, rPin = rFoot ? rFoot.position : Vector3.zero;

        // 咳嗽：每隔 gasCoughInterval 秒往前抽一下（用 Perlin 微調，不要太規律）
        float phase = (t + Mathf.PerlinNoise(t * 0.7f, 3.1f) * 0.15f) / gasCoughInterval;
        float cough = Mathf.Pow(1f - Mathf.Repeat(phase, 1f), 3f);
        int coughIndex = Mathf.FloorToInt(phase);
        if (coughIndex != lastCough && head != null && gasCoughColor.a > 0f && t < duration - 0.3f)
        {
            lastCough = coughIndex;
            Vector3 mouthFwd = Vector3.ProjectOnPlane(head.forward, up).normalized;
            CombatVFX.Puff(head.position + mouthFwd * 0.22f - up * 0.05f, mouthFwd - up * 0.4f, gasCoughColor);
        }

        float f = gasShakeFrequency;
        float n1 = Noise(t * f, 0.3f), n2 = Noise(t * f, 5.7f), n3 = Noise(t * f, 9.2f);
        float a = gasShakeAngle * w;

        hips.position += (right * n1 * 0.012f + up * (n2 * 0.01f - 0.07f) + fwd * n3 * 0.008f) * w;
        hips.rotation = Quaternion.AngleAxis(n2 * a * 0.4f, fwd) * Quaternion.AngleAxis((10f + cough * 6f) * w, right) * hips.rotation;
        BendSpine((22f + cough * 14f) * w + n1 * a, right);
        if (chest != null) chest.rotation = Quaternion.AngleAxis(n3 * a, fwd) * Quaternion.AngleAxis(n2 * a, up) * chest.rotation;
        PitchHead((16f + cough * 10f) * w + n3 * a * 1.2f, right);
        if (head != null) head.rotation = Quaternion.AngleAxis(n1 * a * 1.3f, actor.up) * head.rotation;

        // 雙手摀住口鼻：手掌疊在嘴巴前面，手肘往下、往外撐
        if (head != null)
        {
            float s = armLength / 0.55f;
            Vector3 face = head.position + (up * -0.04f + head.forward * 0.02f) * s;
            Vector3 faceFwd = Vector3.ProjectOnPlane(head.forward, up).normalized;
            if (faceFwd.sqrMagnitude < 0.01f) faceFwd = fwd;
            Vector3 jitter = (right * n2 + up * n3) * 0.01f * s;
            Reach(lUpperArm, lForearm, lHand, face + faceFwd * 0.13f * s - right * 0.03f * s + jitter, -right + -up * 1.2f, w);
            Reach(rUpperArm, rForearm, rHand, face + faceFwd * 0.17f * s + right * 0.03f * s + jitter, right + -up * 1.2f, w);
            FacePalm(lHand, head.position, w);
            FacePalm(rHand, head.position, w);
        }
        PinFeet(lPin, rPin, w);
    }

    // =====================================================================
    //  防禦：雙臂交叉擋在臉前，原地站穩
    // =====================================================================

    void PoseGuard(float t)
    {
        float w = Envelope(t, 0.06f, 0.18f);
        if (w <= 0.001f) return;
        Vector3 right = actor.right, fwd = actor.forward, up = actor.up;
        Vector3 lPin = lFoot ? lFoot.position : Vector3.zero, rPin = rFoot ? rFoot.position : Vector3.zero;

        // 被打的那一下：上身往後震、全身抖一下，0.2 秒內恢復
        float sinceHit = Time.time - lastHitAt;
        float recoil = sinceHit < 0.22f ? Mathf.Sin(Mathf.Clamp01(sinceHit / 0.22f) * Mathf.PI) * (1f - sinceHit / 0.22f * 0.4f) : 0f;
        float shake = sinceHit < 0.15f ? Noise(Time.time * 40f, 1.3f) * (1f - sinceHit / 0.15f) : 0f;

        hips.position += (up * -guardCrouch - fwd * recoil * 0.05f) * w;
        hips.rotation = Quaternion.AngleAxis(6f * w, right) * hips.rotation;
        BendSpine((12f - recoil * guardRecoilAngle) * w + shake * 3f, right);
        PitchHead((14f - recoil * 6f) * w, right);

        if (head != null && chest != null)
        {
            float s = armLength / 0.55f;
            Vector3 guard = head.position + (fwd * 0.3f + up * -0.04f) * s - fwd * recoil * 0.05f;
            // 前臂在臉前交叉成 X：左手擋到右邊、右手擋到左邊（右手在外側，稍高）
            Reach(lUpperArm, lForearm, lHand, guard + (right * 0.1f + up * -0.03f) * s, -right * 1.4f - up, w);
            Reach(rUpperArm, rForearm, rHand, guard + (-right * 0.09f + up * 0.04f + fwd * 0.04f) * s, right * 1.4f - up, w);
            // 手背朝外：拳頭朝上
            if (lHand) lHand.rotation = Quaternion.Slerp(lHand.rotation, Quaternion.LookRotation(up, -fwd) * Quaternion.Euler(0f, 0f, 90f), w * 0.5f);
            if (rHand) rHand.rotation = Quaternion.Slerp(rHand.rotation, Quaternion.LookRotation(up, -fwd) * Quaternion.Euler(0f, 0f, -90f), w * 0.5f);
        }
        PinFeet(lPin, rPin, w);
    }

    // =====================================================================
    //  共用的小工具
    // =====================================================================

    void BendSpine(float degrees, Vector3 axis)
    {
        int n = (spine ? 1 : 0) + (chest ? 1 : 0) + (upperChest ? 1 : 0);
        if (n == 0) return;
        Quaternion q = Quaternion.AngleAxis(degrees / n, axis);
        if (spine) spine.rotation = q * spine.rotation;
        if (chest) chest.rotation = q * chest.rotation;
        if (upperChest) upperChest.rotation = q * upperChest.rotation;
    }

    void PitchHead(float degrees, Vector3 axis)
    {
        if (neck) neck.rotation = Quaternion.AngleAxis(degrees * 0.4f, axis) * neck.rotation;
        if (head) head.rotation = Quaternion.AngleAxis(degrees * (neck ? 0.6f : 1f), axis) * head.rotation;
    }

    /// <summary>手 / 腳伸到「從肩膀 / 大腿根算起、身體座標 offset × 長度」的位置。mirror = 右邊（x 反過來）</summary>
    void ReachRelative(Transform upper, Transform lower, Transform end, Quaternion body, Vector3 offset, Vector3 pole,
                       float length, float w, bool mirror)
    {
        if (!upper || !lower || !end) return;
        if (mirror) { offset.x = -offset.x; pole.x = -pole.x; }
        Reach(upper, lower, end, upper.position + body * offset * length, body * pole, w);
    }

    void Reach(Transform upper, Transform lower, Transform end, Vector3 goal, Vector3 pole, float w)
    {
        if (!upper || !lower || !end || w <= 0.001f) return;
        Vector3 current = end.position;
        SolveLimb(upper, lower, end, Vector3.Lerp(current, goal, w), pole);
    }

    /// <summary>雙腳踩在原本的位置（蹲下、震動時腳不會跟著陷進地裡或滑走）</summary>
    void PinFeet(Vector3 left, Vector3 right, float w)
    {
        Vector3 knee = actor.forward;
        if (lFoot) { var r = lFoot.rotation; Reach(lUpperLeg, lLowerLeg, lFoot, left, knee, 1f); lFoot.rotation = r; }
        if (rFoot) { var r = rFoot.rotation; Reach(rUpperLeg, rLowerLeg, rFoot, right, knee, 1f); rFoot.rotation = r; }
    }

    /// <summary>手掌轉向臉（摀住口鼻）</summary>
    static void FacePalm(Transform hand, Vector3 facePoint, float w)
    {
        if (!hand || hand.childCount == 0) return;
        Vector3 fingers = hand.GetChild(0).position - hand.position;
        Vector3 toFace = facePoint - hand.position;
        if (fingers.sqrMagnitude < 1e-6f || toFace.sqrMagnitude < 1e-6f) return;
        // 指尖朝上方偏內，不要直直戳向臉
        Vector3 want = Vector3.Slerp(Vector3.up, Vector3.ProjectOnPlane(toFace, Vector3.up).normalized, 0.35f);
        hand.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(fingers, want), w * 0.6f) * hand.rotation;
    }

    static void SolveLimb(Transform upper, Transform lower, Transform end, Vector3 goal, Vector3 pole)
    {
        Vector3 root = upper.position;
        float a = Vector3.Distance(upper.position, lower.position);
        float b = Vector3.Distance(lower.position, end.position);
        Vector3 to = goal - root;
        float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 0.001f, a + b - 0.001f);
        Vector3 dir = to.sqrMagnitude > 1e-8f ? to.normalized : (end.position - root).normalized;
        float along = (a * a - b * b + d * d) / (2f * d);
        float h = Mathf.Sqrt(Mathf.Max(0f, a * a - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(pole, dir);
        if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(lower.position - root, dir);
        bend = bend.sqrMagnitude > 1e-8f ? bend.normalized : Vector3.zero;
        Vector3 elbow = root + dir * along + bend * h;
        upper.rotation = Quaternion.FromToRotation(lower.position - root, elbow - root) * upper.rotation;
        lower.rotation = Quaternion.FromToRotation(end.position - lower.position, root + dir * d - lower.position) * lower.rotation;
    }

    float globalWeight = 1f;
    float Envelope(float t, float fadeIn, float fadeOut) =>
        Mathf.Min(Smooth(t / fadeIn), Smooth((duration - t) / fadeOut)) * globalWeight;

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
    static float Noise(float x, float seed) => Mathf.PerlinNoise(x, seed) * 2f - 1f;
}
