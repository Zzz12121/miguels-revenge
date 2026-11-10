using UnityEngine;

/// <summary>
/// 村民沒在跟玩家說話時的日常：照 stops 的順序走過去、在每一站做一件事（站著東張西望、坐下、打鐵、顧爐火、擦長椅、進屋），做完換下一站，繞一圈再從頭。
/// 玩家走近（noticeRadius 以內）或正在對話時停下來：坐著的先站起來、看著玩家；玩家走開（resumeRadius 以外）才接著做。
/// 坐、打鐵這些動作專案裡沒有適合人形骨架的動畫，用程式在 LateUpdate 擺骨頭（蓋在待機動畫上）。
/// 走路用 Animator 的 Speed / WalkRate（AC_Villager）。
/// </summary>
public class NpcRoutine : MonoBehaviour
{
    public enum Act
    {
        /// <summary>站著（東張西望）</summary>
        Stand,
        /// <summary>坐下（seatY = 坐面的高度）</summary>
        Sit,
        /// <summary>舉鎚往前面敲（鐵砧在正前方）</summary>
        Hammer,
        /// <summary>彎腰顧爐火</summary>
        Tend,
        /// <summary>彎腰擦東西（長椅、桌面）</summary>
        Wipe,
        /// <summary>走進屋裡（人消失一陣子再出來）</summary>
        Inside,
        /// <summary>只是路過的點，不停</summary>
        Pass,
        /// <summary>蹲下來看地上的東西（石頭），一隻手伸過去摸</summary>
        Crouch,
        /// <summary>原地蹦蹦跳跳</summary>
        Hop,
    }

    [System.Serializable]
    public struct Stop
    {
        public Vector3 position;
        public float yaw;
        public float duration;
        public Act act;
        public float seatY;
        [Tooltip("走到這一站的速度 (m/s)；0 = 用 walkSpeed。超過 runFrom 就用跑的")]
        public float speed;
    }

    public Stop[] stops;
    public Animator animator;
    public float walkSpeed = 1.1f;
    [Tooltip("速度超過這個就播跑步（Animator 的 Speed = 2）")]
    public float runFrom = 2f;
    [Tooltip("跑步動畫原本的前進速度（m/s，這個人縮放 1 時）")]
    public float animationRunSpeed = 3.4f;
    [Tooltip("走路動畫原本的前進速度（m/s，這個人縮放 1 時）")]
    public float animationWalkSpeed = 1.72f;
    public float noticeRadius = 4.5f;
    [Tooltip("開：玩家走近也繼續做自己的事（老漁夫坐著釣魚，不站起來）")]
    public bool ignorePlayer;
    public float resumeRadius = 6.5f;

    /// <summary>現在在做什麼（測試用）</summary>
    public Act CurrentAct => walking ? Act.Pass : stops != null && stops.Length > 0 ? stops[index].act : Act.Stand;
    public bool Walking => walking;
    public bool Interrupted => interrupted;
    public int StopIndex => index;
    public float SitWeight => sitW;

    int index;
    float timer, walkDone, walkLen, sitW, actW, insideT, crouchW, hopW, legSpeed;
    Vector3 modelRest;
    bool modelRestSet;
    bool walking, interrupted, hidden;
    Vector3 from, to;
    MerchantNPC npc;
    Transform player;
    Renderer[] renderers;
    float seed;

    Transform hips, spine, head, lUp, lLow, lFoot, rUp, rLow, rFoot, lArm, lFore, lHand, rArm, rFore, rHand;
    bool bonesFound;

    static readonly int SpeedId = Animator.StringToHash("Speed");
    static readonly int WalkRateId = Animator.StringToHash("WalkRate");

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        var hp = PlayerHealth.Find();
        if (hp != null) player = hp.transform;
        renderers = GetComponentsInChildren<Renderer>(true);
        seed = Random.value * 10f;
        // 坐下、打鐵是在 LateUpdate 擺骨頭：Animator 被剔除時（主鏡頭沒看到）會在別的鏡頭（對話的半身像）拍的時候蓋掉，所以一直更新
        if (animator != null) animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (stops == null || stops.Length == 0) { enabled = false; return; }
        index = 0;
        transform.SetPositionAndRotation(stops[0].position, Quaternion.Euler(0f, stops[0].yaw, 0f));
        timer = stops[0].duration * Random.Range(0.3f, 1f);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        bool talking = npc != null && npc.InConversation;
        bool near = false;
        if (player != null && !hidden)
        {
            Vector3 d = player.position - transform.position;
            d.y = 0f;
            near = !ignorePlayer && d.magnitude < (interrupted ? resumeRadius : noticeRadius);
            if (near && !talking && d.sqrMagnitude > 0.01f) Face(d, 220f);
        }
        interrupted = (near || (talking && !ignorePlayer)) && !hidden;

        var stop = stops[index];
        float speed = 0f;
        float wantSit = 0f, wantAct = 0f, wantCrouch = 0f, wantHop = 0f;

        if (hidden)
        {
            // 在屋裡：時間到了才出來
            insideT -= dt;
            if (insideT <= 0f) SetHidden(false);
        }
        else if (interrupted)
        {
            // 停下來看著玩家（坐著的先站起來）
        }
        else if (walking)
        {
            walkDone += legSpeed * dt;
            float t = walkLen > 0.001f ? Mathf.Clamp01(walkDone / walkLen) : 1f;
            transform.position = Vector3.Lerp(from, to, t);
            Vector3 dir = to - from;
            dir.y = 0f;
            Face(dir, 300f);
            speed = legSpeed;
            if (t >= 1f)
            {
                walking = false;
                timer = stop.duration;
                if (stop.act == Act.Inside) { SetHidden(true); insideT = stop.duration; }
            }
        }
        else
        {
            // 在這一站：轉到該面向的方向，做這站的事
            if (stop.act != Act.Pass) Face(Quaternion.Euler(0f, stop.yaw, 0f) * Vector3.forward, 160f);
            if (stop.act == Act.Sit) wantSit = 1f;
            else if (stop.act == Act.Hammer || stop.act == Act.Tend || stop.act == Act.Wipe) wantAct = 1f;
            else if (stop.act == Act.Crouch) wantCrouch = 1f;
            else if (stop.act == Act.Hop) wantHop = 1f;
            timer -= dt;
            // 坐著的要先站起來才走
            if (timer <= 0f && sitW <= 0.02f && actW <= 0.05f && crouchW <= 0.05f && hopW <= 0.05f) StartWalk();
            else if (timer <= 0f) { wantSit = 0f; wantAct = 0f; wantCrouch = 0f; wantHop = 0f; }
        }

        sitW = Mathf.MoveTowards(sitW, wantSit, dt * 2.2f);
        actW = Mathf.MoveTowards(actW, wantAct, dt * 3f);
        crouchW = Mathf.MoveTowards(crouchW, wantCrouch, dt * 3f);
        hopW = Mathf.MoveTowards(hopW, wantHop, dt * 4f);

        if (animator != null)
        {
            bool run = speed >= runFrom;
            float natural = (run ? animationRunSpeed : animationWalkSpeed) * transform.lossyScale.y;
            animator.SetFloat(SpeedId, speed > 0f ? (run ? 2f : 1f) : 0f, 0.12f, dt);
            animator.SetFloat(WalkRateId, speed > 0f ? speed / Mathf.Max(0.1f, natural) : 1f);
        }
    }

    void StartWalk()
    {
        index = (index + 1) % stops.Length;
        from = transform.position;
        to = stops[index].position;
        Vector3 d = to - from;
        d.y = 0f;
        walkLen = d.magnitude;
        walkDone = 0f;
        legSpeed = stops[index].speed > 0f ? stops[index].speed : walkSpeed;
        walking = true;
        if (walkLen < 0.05f) { walking = false; timer = stops[index].duration; }
    }

    void SetHidden(bool hide)
    {
        hidden = hide;
        foreach (var r in renderers) if (r != null) r.enabled = !hide;
        if (npc != null) npc.enabled = !hide; // 在屋裡的時候不能對話
        foreach (var c in GetComponents<Collider>()) c.enabled = !hide;
        if (!hide) { timer = 0f; GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.5f, 1.1f, 0.05f, 1f, 20f); }
        else GameAudio.Play("SFX/RPG/doorClose_2", transform.position, 0.5f, 1.1f, 0.05f, 1f, 20f);
    }

    void Face(Vector3 dir, float degPerSec)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-5f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), degPerSec * Time.deltaTime);
    }

    // ---------------- 姿勢 ----------------

    void FindBones()
    {
        bonesFound = true;
        if (animator == null || !animator.isHuman) return;
        Transform B(HumanBodyBones b) => animator.GetBoneTransform(b);
        hips = B(HumanBodyBones.Hips);
        spine = B(HumanBodyBones.Chest) ?? B(HumanBodyBones.Spine);
        head = B(HumanBodyBones.Head);
        lUp = B(HumanBodyBones.LeftUpperLeg); lLow = B(HumanBodyBones.LeftLowerLeg); lFoot = B(HumanBodyBones.LeftFoot);
        rUp = B(HumanBodyBones.RightUpperLeg); rLow = B(HumanBodyBones.RightLowerLeg); rFoot = B(HumanBodyBones.RightFoot);
        lArm = B(HumanBodyBones.LeftUpperArm); lFore = B(HumanBodyBones.LeftLowerArm); lHand = B(HumanBodyBones.LeftHand);
        rArm = B(HumanBodyBones.RightUpperArm); rFore = B(HumanBodyBones.RightLowerArm); rHand = B(HumanBodyBones.RightHand);
    }

    void LateUpdate()
    {
        if (hidden || animator == null) return;
        if (!bonesFound) FindBones();
        if (hips == null) return;
        Vector3 up = Vector3.up, fwd = transform.forward, side = transform.right;
        float s = transform.lossyScale.y;
        float time = Time.time + seed;

        // 站著的時候偶爾轉頭看看四周
        if (!walking && !interrupted && head != null && stops[index].act == Act.Stand)
            head.rotation = Quaternion.AngleAxis(Mathf.Sin(time * 0.6f) * 30f * Mathf.SmoothStep(0f, 1f, Mathf.Abs(Mathf.Sin(time * 0.23f))), up) * head.rotation;

        if (sitW > 0.001f) PoseSit(stops[index].seatY, sitW, up, fwd, side, s);
        if (actW > 0.001f) PoseWork(stops[index].act, actW, time, up, fwd, side, s);
        if (crouchW > 0.001f) PoseCrouch(crouchW, time, up, fwd, side, s);
        PoseHop(hopW, time, up, fwd, side, s);
    }

    void PoseSit(float seatY, float w, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        // 屁股降到坐面上，大腿往前平放、小腿往下，雙手放在大腿上
        float drop = Mathf.Max(0f, hips.position.y - (seatY + 0.1f * s));
        hips.position -= up * drop * w;
        Aim(lUp, lLow, fwd - side * 0.12f - up * 0.05f, w);
        Aim(rUp, rLow, fwd + side * 0.12f - up * 0.05f, w);
        Aim(lLow, lFoot, -up + fwd * 0.3f, w);
        Aim(rLow, rFoot, -up + fwd * 0.3f, w);
        Aim(lArm, lFore, -up * 0.85f + fwd * 0.35f - side * 0.08f, w);
        Aim(rArm, rFore, -up * 0.85f + fwd * 0.35f + side * 0.08f, w);
        Aim(lFore, lHand, fwd - up * 0.35f, w);
        Aim(rFore, rHand, fwd - up * 0.35f, w);
    }

    /// <summary>蹲下：屁股降到膝蓋高度附近，膝蓋往前、小腿往後收，低頭看地上，右手往前下方伸</summary>
    void PoseCrouch(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        float rest = hips.position.y - transform.position.y;
        hips.position -= up * rest * 0.5f * w;
        Aim(lUp, lLow, fwd * 0.9f - up * 0.25f - side * 0.25f, w);
        Aim(rUp, rLow, fwd * 0.9f - up * 0.25f + side * 0.25f, w);
        Aim(lLow, lFoot, -up - fwd * 0.55f, w);
        Aim(rLow, rFoot, -up - fwd * 0.55f, w);
        if (spine != null) spine.rotation = Quaternion.AngleAxis(32f * w, side) * spine.rotation;
        if (head != null) head.rotation = Quaternion.AngleAxis((22f + Mathf.Sin(time * 1.3f) * 6f) * w, side) * head.rotation;
        float poke = Mathf.Sin(time * 2.4f) * 0.12f;
        Aim(rArm, rFore, fwd * 0.6f - up * 0.75f + side * 0.15f, w);
        Aim(rFore, rHand, fwd * 0.7f - up * (0.6f + poke), w);
        Aim(lArm, lFore, -up * 0.8f + fwd * 0.4f - side * 0.2f, w);
        Aim(lFore, lHand, fwd - up * 0.3f, w);
    }

    /// <summary>蹦跳：整個人往上彈（模型往上移），跳到空中時膝蓋往上收</summary>
    void PoseHop(float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        var model = animator.transform;
        if (!modelRestSet) { modelRest = model.localPosition; modelRestSet = true; }
        if (w <= 0.001f) { model.localPosition = modelRest; return; }
        float h = Mathf.Abs(Mathf.Sin(time * 4.2f));
        model.localPosition = modelRest;
        model.position += up * h * 0.32f * s * w;
        float tuck = h * w;
        Aim(lUp, lLow, fwd * 0.6f - up * 0.8f - side * 0.1f, tuck);
        Aim(rUp, rLow, fwd * 0.6f - up * 0.8f + side * 0.1f, tuck);
        Aim(lLow, lFoot, -up - fwd * 0.3f, tuck);
        Aim(rLow, rFoot, -up - fwd * 0.3f, tuck);
        Aim(lArm, lFore, up * 0.6f - side * 0.6f + fwd * 0.2f, w * 0.8f);
        Aim(rArm, rFore, up * 0.6f + side * 0.6f + fwd * 0.2f, w * 0.8f);
    }

    float lastStrike = -1f;

    void PoseWork(Act act, float w, float time, Vector3 up, Vector3 fwd, Vector3 side, float s)
    {
        switch (act)
        {
            case Act.Hammer:
            {
                // 一下約 1.2 秒：慢慢舉高到頭後面 → 快速往前下敲 → 停一下
                float p = Mathf.Repeat(time * 0.83f, 1f);
                float k = p < 0.55f ? Mathf.SmoothStep(0f, 1f, p / 0.55f) : p < 0.68f ? 1f - Mathf.SmoothStep(0f, 1f, (p - 0.55f) / 0.13f) : 0f;
                if (spine != null) spine.rotation = Quaternion.AngleAxis(12f * w, side) * spine.rotation;
                Vector3 armUp = up * 0.85f + fwd * 0.2f + side * 0.25f, armDown = fwd * 0.75f - up * 0.45f + side * 0.1f;
                Vector3 foreUp = up * 0.3f - fwd * 0.6f, foreDown = fwd * 0.5f - up * 0.85f;
                Aim(rArm, rFore, Vector3.Slerp(armDown.normalized, armUp.normalized, k), w);
                Aim(rFore, rHand, Vector3.Slerp(foreDown.normalized, foreUp.normalized, k), w);
                // 左手扶著鐵砧上的東西
                Aim(lArm, lFore, fwd * 0.6f - up * 0.7f - side * 0.2f, w);
                Aim(lFore, lHand, fwd - up * 0.2f, w);
                // 敲下去的瞬間：叮一聲、冒火花
                if (w > 0.9f && p >= 0.68f && p < 0.72f && time - lastStrike > 0.5f)
                {
                    lastStrike = time;
                    Vector3 at = transform.position + fwd * 0.75f * s + up * 0.75f;
                    GameAudio.Play("SFX/RPG/metalPot1", at, 0.55f, Random.Range(1.3f, 1.5f), 0.05f, 1f, 25f);
                    CombatVFX.Impact(at, new Color(1f, 0.7f, 0.3f));
                }
                break;
            }
            case Act.Tend:
            {
                // 彎腰顧爐火：右手往前下方慢慢撥動
                if (spine != null) spine.rotation = Quaternion.AngleAxis(18f * w, side) * spine.rotation;
                float o = Mathf.Sin(time * 2f) * 0.18f;
                Aim(rArm, rFore, fwd * 0.7f - up * 0.6f + side * 0.2f + up * o, w);
                Aim(rFore, rHand, fwd - up * 0.2f + up * o, w);
                break;
            }
            case Act.Wipe:
            {
                // 彎腰擦長椅：右手在前下方左右來回
                if (spine != null) spine.rotation = Quaternion.AngleAxis(28f * w, side) * spine.rotation;
                float o = Mathf.Sin(time * 3.2f) * 0.6f;
                Aim(rArm, rFore, fwd * 0.7f - up * 0.7f + side * 0.15f, w);
                Aim(rFore, rHand, fwd * 0.6f - up * 0.5f + side * o, w);
                Aim(lArm, lFore, fwd * 0.4f - up * 0.8f - side * 0.2f, w);
                break;
            }
        }
    }

    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir.normalized), w) * bone.rotation;
    }

    void OnDrawGizmosSelected()
    {
        if (stops == null) return;
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.8f);
        for (int i = 0; i < stops.Length; i++)
        {
            Gizmos.DrawWireSphere(stops[i].position, 0.25f);
            Gizmos.DrawLine(stops[i].position, stops[(i + 1) % stops.Length].position);
        }
    }
}
