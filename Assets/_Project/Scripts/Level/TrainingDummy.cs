using UnityEngine;

/// <summary>
/// 習武場的木樁人偶。一開始是一根普通的木樁，被打中一下就「活過來」：木樁消失，變成身上綁著木樁的木頭人
/// （Banana Man 模型，四肢綁著一段段短木樁），走到玩家面前用雙手推擊。推擊和一般敵人的攻擊一樣，可以舉盾擋或翻滾躲開。
/// 總共打中 hitsRequired 下（含喚醒的那一下）就過關：木頭人停手、鞠躬，頭上綁紅布。頭上不顯示任何文字，要看動作反應。
/// 不會倒、不會死（練習對象），只在自己的木樁周圍 leashRadius 公尺內活動。
/// 動作：Animator 播待機 / 走路（參數 Speed），推擊、受擊、鞠躬在 LateUpdate 用程式擺骨頭疊上去。
/// </summary>
[DisallowMultipleComponent]
public class TrainingDummy : MonoBehaviour, IDamageable
{
    [Header("過關")]
    public int hitsRequired = 6;
    [Tooltip("過關後才顯示的東西（綁在頭上的紅布）")]
    public GameObject clearedMarker;

    [Header("外觀")]
    [Tooltip("還沒被打之前的木樁")]
    public GameObject postVisual;
    [Tooltip("被打之後出現的木頭人（Humanoid 模型，一開始關著）")]
    public GameObject fighter;
    public Animator fighterAnimator;

    [Header("移動")]
    [Tooltip("離自己的木樁最遠走多遠 (m)")]
    public float leashRadius = 6f;
    [Tooltip("玩家離木樁多遠以內才會追過去 (m)")]
    public float noticeRadius = 11f;
    public float moveSpeed = 2.1f;
    [Tooltip("停在離玩家多遠的地方出手 (m)")]
    public float standOff = 1.5f;

    [Header("推擊")]
    public float attackInterval = 2.2f;
    [Tooltip("推之前雙手往後收的預備時間，玩家靠這段時間反應 (秒)")]
    public float windupTime = 0.55f;
    public float strikeTime = 0.18f;
    public float recoverTime = 0.45f;
    [Tooltip("推得到的距離 (m)")]
    public float reach = 2.3f;
    public float damage = 4f;
    [Tooltip("把玩家往後推的力道")]
    public float pushForce = 11f;

    public int Hits { get; private set; }
    public bool IsAwake { get; private set; }
    public bool Cleared => Hits >= hitsRequired;
    public bool IsDead => false;

    const float MinHitInterval = 0.12f;  // 一下多段判定的技能不要瞬間算很多下
    const float WakeTime = 0.6f;

    enum Phase { Idle, Windup, Strike, Recover }
    Phase phase;
    PlayerHealth player;
    Vector3 home;
    float groundY, phaseTime, cooldown, lastHit = float.NegativeInfinity, recoil, wakeT = -1f, bow;
    Vector3 fighterScale;

    Transform spine, armL, armR, foreL, foreR;

    void Start()
    {
        player = PlayerHealth.Find();
        home = transform.position;
        groundY = home.y;
        if (fighter != null)
        {
            fighterScale = fighter.transform.localScale;
            fighter.SetActive(false);
        }
        if (clearedMarker != null) clearedMarker.SetActive(false);
    }

    /// <summary>木頭人出現之後 Animator 才會初始化，這時才找得到骨頭</summary>
    void FindBones()
    {
        if (fighterAnimator == null || !fighterAnimator.isHuman) return;
        spine = fighterAnimator.GetBoneTransform(HumanBodyBones.Spine);
        armL = fighterAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        armR = fighterAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        foreL = fighterAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        foreR = fighterAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (amount <= 0f || Cleared || Time.time - lastHit < MinHitInterval) return;
        lastHit = Time.time;
        Hits++;
        CombatVFX.Impact(hitPoint, new Color(0.85f, 0.65f, 0.4f));

        if (!IsAwake)
        {
            WakeUp();
            return;
        }
        // 被打中：身體往後仰、往後退一點，正在準備的推擊被打斷
        recoil = 1f;
        if (phase == Phase.Windup) { phase = Phase.Recover; phaseTime = 0f; }
        if (player != null)
        {
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude > 0.01f) MoveTo(transform.position + away.normalized * 0.35f);
        }
        if (Cleared)
        {
            phase = Phase.Idle;
            if (clearedMarker != null) clearedMarker.SetActive(true);
            GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 1f, 0f, 1f, 40f);
        }
    }

    void WakeUp()
    {
        IsAwake = true;
        wakeT = 0f;
        cooldown = attackInterval;
        if (postVisual != null) postVisual.SetActive(false);
        if (fighter != null)
        {
            fighter.SetActive(true);
            fighter.transform.localScale = fighterScale * 0.3f;
            FindBones();
        }
        CombatVFX.Impact(transform.position + Vector3.up * 1.2f, new Color(0.75f, 0.55f, 0.3f));
        GameAudio.Play("SFX/RPG/creak2", transform.position, 1f, 0.8f, 0.05f, 1f, 30f);
        if (Cleared && clearedMarker != null) clearedMarker.SetActive(true);
    }

    void Update()
    {
        if (!IsAwake) return;
        recoil = Mathf.MoveTowards(recoil, 0f, Time.deltaTime * 3f);
        bow = Mathf.MoveTowards(bow, Cleared ? 1f : 0f, Time.deltaTime * 1.5f);

        // 冒出來：從小長到原本大小
        if (wakeT >= 0f && fighter != null)
        {
            wakeT += Time.deltaTime;
            float k = Mathf.Clamp01(wakeT / WakeTime);
            fighter.transform.localScale = fighterScale * Mathf.Lerp(0.3f, 1f, 1f - (1f - k) * (1f - k));
            if (k >= 1f) wakeT = -1f;
        }

        float speed = 0f;
        if (player != null && !player.IsDead)
        {
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            float distance = toPlayer.magnitude;
            bool engaged = !Cleared && (player.transform.position - home).sqrMagnitude < noticeRadius * noticeRadius;

            // 面向玩家（過關後也看著玩家鞠躬）
            if (distance > 0.05f && (engaged || Cleared) && phase != Phase.Strike)
                Face(toPlayer);

            if (engaged)
            {
                if (phase == Phase.Idle)
                {
                    if (distance > standOff + 0.2f) speed = Step(player.transform.position, standOff);
                    cooldown -= Time.deltaTime;
                    if (cooldown <= 0f && distance < reach - 0.2f) BeginWindup();
                }
                UpdateAttack(toPlayer, distance);
            }
            else if (!Cleared)
            {
                phase = Phase.Idle;
                if ((transform.position - home).sqrMagnitude > 0.04f) speed = Step(home, 0f);
            }
        }
        if (fighterAnimator != null && fighterAnimator.isActiveAndEnabled) fighterAnimator.SetFloat("Speed", speed / moveSpeed, 0.15f, Time.deltaTime);
    }

    /// <summary>往 target 走，停在離它 stopAt 公尺；不會離開自己的木樁 leashRadius 以外</summary>
    float Step(Vector3 target, float stopAt)
    {
        Vector3 d = target - transform.position;
        d.y = 0f;
        float dist = d.magnitude - stopAt;
        if (dist <= 0.02f) return 0f;
        float move = Mathf.Min(dist, moveSpeed * Time.deltaTime);
        var before = transform.position;
        MoveTo(transform.position + d.normalized * move);
        return (transform.position - before).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    void MoveTo(Vector3 p)
    {
        Vector3 off = p - home;
        off.y = 0f;
        if (off.magnitude > leashRadius) off = off.normalized * leashRadius;
        transform.position = new Vector3(home.x + off.x, groundY, home.z + off.z);
    }

    void Face(Vector3 dir)
    {
        var look = Quaternion.LookRotation(dir.normalized);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 300f * Time.deltaTime);
    }

    void BeginWindup()
    {
        phase = Phase.Windup;
        phaseTime = 0f;
        GameAudio.Play("SFX/RPG/creak1", transform.position, 0.7f, 1.2f, 0.1f, 1f, 25f);
    }

    void UpdateAttack(Vector3 toPlayer, float distance)
    {
        switch (phase)
        {
            case Phase.Windup:
                phaseTime += Time.deltaTime;
                if (phaseTime >= windupTime)
                {
                    phase = Phase.Strike;
                    phaseTime = 0f;
                    MoveTo(transform.position + transform.forward * 0.4f); // 推的時候往前踏一步
                    Push(toPlayer, distance);
                }
                break;
            case Phase.Strike:
                phaseTime += Time.deltaTime;
                if (phaseTime >= strikeTime) { phase = Phase.Recover; phaseTime = 0f; }
                break;
            case Phase.Recover:
                phaseTime += Time.deltaTime;
                if (phaseTime >= recoverTime) { phase = Phase.Idle; cooldown = attackInterval; }
                break;
        }
    }

    void Push(Vector3 toPlayer, float distance)
    {
        GameAudio.Play("SFX/RPG/knifeSlice", transform.position, 0.6f, 0.7f, 0.1f, 1f, 25f);
        if (distance > reach || distance < 0.01f) return;
        // 只推得到正前方：翻滾到側面或背後就躲開了
        Vector3 dir = toPlayer / distance;
        if (Vector3.Dot(transform.forward, dir) < 0.4f) return;
        player.TakeDamage(damage, dir * pushForce + Vector3.up * 2.5f, false, "被木頭人推倒了");
    }

    // 擺姿勢：動畫跑完之後再疊上去
    void LateUpdate()
    {
        if (!IsAwake || fighterAnimator == null) return;

        // 推擊：預備時雙手往後收到胸前，推出去時雙臂往前伸直、身體前傾
        float push = 0f, pull = 0f;
        if (phase == Phase.Windup) pull = Mathf.SmoothStep(0f, 1f, phaseTime / windupTime);
        else if (phase == Phase.Strike) push = 1f;
        else if (phase == Phase.Recover) push = 1f - Mathf.SmoothStep(0f, 1f, phaseTime / recoverTime);

        Vector3 fwd = transform.forward, up = Vector3.up, right = transform.right;
        if (spine != null)
        {
            float lean = push * 18f - pull * 8f - recoil * 22f + bow * 38f;
            spine.rotation = Quaternion.AngleAxis(lean, right) * spine.rotation;
        }
        if (push > 0.01f || pull > 0.01f)
        {
            float w = Mathf.Max(push, pull);
            for (int s = 0; s < 2; s++)
            {
                var arm = s == 0 ? armL : armR;
                var fore = s == 0 ? foreL : foreR;
                if (arm == null || fore == null) continue;
                float side = s == 0 ? -1f : 1f;
                // 推出去：手臂指向正前方；收回來：上臂往下後方、前臂往前（手掌在胸前）
                Vector3 armDir = push > 0.01f ? (fwd + right * side * 0.12f - up * 0.05f) : (-up * 0.7f - fwd * 0.4f + right * side * 0.35f);
                Aim(arm, fore, armDir, w);
                var hand = fore.childCount > 0 ? fore.GetChild(0) : null;
                Vector3 foreDir = push > 0.01f ? fwd : (fwd + up * 0.6f);
                Aim(fore, hand, foreDir, w);
            }
        }
        else if (bow > 0.01f)
        {
            // 過關：雙手垂在身側
            for (int s = 0; s < 2; s++)
            {
                var arm = s == 0 ? armL : armR;
                var fore = s == 0 ? foreL : foreR;
                if (arm == null || fore == null) continue;
                Aim(arm, fore, -up + right * (s == 0 ? -0.15f : 0.15f) + fwd * 0.2f, bow);
            }
        }
    }

    /// <summary>把 bone 轉向，讓它的子骨頭落在 dir 方向（w = 轉多少）</summary>
    static void Aim(Transform bone, Transform child, Vector3 dir, float w)
    {
        if (bone == null || child == null || w <= 0.001f || dir.sqrMagnitude < 1e-6f) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-6f) return;
        bone.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(cur, dir.normalized), w) * bone.rotation;
    }
}
