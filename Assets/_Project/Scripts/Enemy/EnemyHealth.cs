using System.Collections;
using UnityEngine;

/// <summary>
/// 小怪血量：
/// - 被打中會閃白、被擊退、短暫硬直（硬直由 EnemyAI 處理）
/// - 戰鬥中 / 剛被打過時，頭上會顯示小血條
/// - 死亡時往後倒下、沉入地面後消失；玩家重生時由 EnemyAI 呼叫 Revive 復活
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyHealth : MonoBehaviour, IDamageable
{
    public float maxHealth = 40f;
    [Tooltip("被打中時往後退的力道")]
    public float hitKnockback = 4f;
    [Tooltip("被打中後的硬直時間 (秒)，期間不能移動和攻擊")]
    public float staggerTime = 0.35f;
    [Tooltip("被打時身體閃一下的時間 (秒)")]
    public float flashTime = 0.08f;
    [Tooltip("被打時閃的顏色。有貼圖的模型閃白色看不出來，要用紅色之類的顏色")]
    public Color flashColor = Color.white;
    [Tooltip("受傷音效（不填 = Kenney RPG Audio 的 metalPot1~3）")]
    public AudioClip[] hurtSounds;
    [Tooltip("固定被打幾下就死（不管攻擊力多高、是第幾段連擊）。0 = 照傷害扣血")]
    public int hitsToKill = 0;

    [Header("要害（背後 / 頭部）")]
    [Tooltip("打中要害時幾下就死（要搭配 hitsToKill 使用）。0 = 沒有要害")]
    public int weakPointHitsToKill = 0;
    [Tooltip("打中點在碰撞膠囊最上面這個比例內 = 打到頭")]
    [Range(0f, 0.5f)] public float headZone = 0.22f;
    [Tooltip("打中點在背面多少度以內 = 從背後打（0 = 正後方一條線，90 = 整個背面半圈）")]
    [Range(0f, 90f)] public float backArcDegrees = 65f;
    [Tooltip("打中要害時噴的火花顏色")]
    public Color weakPointColor = new Color(1f, 0.82f, 0.25f);

    [Header("頭上血條")]
    public bool showHealthBar = true;
    [Tooltip("血條離角色中心多高 (m)")]
    public float barHeightOffset = 1.5f;
    [Tooltip("被打後血條顯示幾秒")]
    public float barShowTime = 4f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public float LastHitTime { get; private set; } = float.NegativeInfinity;
    /// <summary>最後一下被打時往哪個方向退（水平、單位向量）</summary>
    public Vector3 LastHitDirection { get; private set; }

    [Header("橋邊 / 懸崖邊被打倒")]
    [Tooltip("被打倒時，身體倒下去的那一側沒有地面（例如被打出橋外）→ 翻滾著掉下去，而不是原地倒下")]
    public bool rollOffLedges = true;
    [Tooltip("往倒下的方向多遠內沒有地面就算懸空 (m，從碰撞膠囊外緣量起)")]
    public float ledgeProbeReach = 1.1f;
    [Tooltip("腳下多深以內有東西才算地面 (m)")]
    public float ledgeProbeDepth = 3f;
    [Tooltip("翻滾掉落：每秒翻幾度、掉多久後消失 (秒)")]
    public float ledgeTumbleSpeed = 420f;
    public float ledgeFallTime = 2.6f;

    /// <summary>舉盾格擋（EnemyShieldBlock 設定）：回傳 true = 這一下被擋掉，不扣血也不硬直</summary>
    public System.Func<Vector3, bool> BlockCheck;
    /// <summary>戰鬥中強制顯示血條（EnemyAI 設定）</summary>
    public bool ForceShowBar { get; set; }

    public event System.Action<float> OnDamaged;
    public event System.Action OnDied;

    CharacterController cc;
    ThirdPersonController controller;
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    bool flashing;
    static readonly RaycastHit[] fallHits = new RaycastHit[8];
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        controller = GetComponent<ThirdPersonController>();
        // 只管「一開始就有顯示」的部分（裝了模型之後，被關掉的膠囊不要在復活時又跑出來）
        var all = new System.Collections.Generic.List<Renderer>(GetComponentsInChildren<Renderer>(true));
        all.RemoveAll(r => !r.enabled);
        renderers = all.ToArray();
        mpb = new MaterialPropertyBlock();
        Health = maxHealth;
    }

    public void TakeDamage(float amount, Vector3 hitPoint) => TakeDamage(amount, hitPoint, 1f);

    /// <summary>
    /// hitWeight = 這一下算幾下（盾反突刺 = 2）。
    /// 「固定被打幾下就死」（hitsToKill）的敵人也要吃到加倍：照傷害扣血的敵人 amount 已經是加倍後的值，不再乘。
    /// </summary>
    public void TakeDamage(float amount, Vector3 hitPoint, float hitWeight)
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (IsDead || amount <= 0f) return;
        if (BlockCheck != null && BlockCheck(hitPoint)) return;
        bool weak = hitsToKill > 0 && weakPointHitsToKill > 0 && IsWeakPoint(hitPoint);
        if (hitsToKill > 0) amount = maxHealth / (weak ? weakPointHitsToKill : hitsToKill) * Mathf.Max(1f, hitWeight); // 每一下扣固定比例（要害扣更多）
        Health = Mathf.Max(0f, Health - amount);
        if (weak) WeakPointFeedback(hitPoint);
        LastHitTime = Time.time;

        // 往打擊點的反方向退
        if (controller != null && controller.enabled)
        {
            Vector3 away = transform.position - hitPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            controller.AddImpulse(away.normalized * hitKnockback + Vector3.up * 1.5f);
        }
        {
            Vector3 away = transform.position - hitPoint;
            away.y = 0f;
            LastHitDirection = away.sqrMagnitude > 0.0001f ? away.normalized : -transform.forward;
        }

        // 受傷音效：盔甲被打中的聲音（音高隨機一點，連續打不會聽起來一樣）
        // 骷髏兵沒有盔甲，不放預設的金屬聲（玩家的打擊音效照樣有）；有指定 hurtSounds 的照用
        bool skeleton = name.IndexOf("Skeleton", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!skeleton || (hurtSounds != null && hurtSounds.Length > 0))
            GameAudio.PlayAt(GameAudio.Pick(hurtSounds, "SFX/RPG/metalPot1", "SFX/RPG/metalPot2", "SFX/RPG/metalPot3"),
                             transform.position, 0.75f, Random.Range(0.75f, 0.95f), 1f, 35f);
        OnDamaged?.Invoke(amount);
        if (Health <= 0f) Die();
    }

    /// <summary>打中點是不是要害：碰撞膠囊頂端（頭），或是背面（從背後打）</summary>
    public bool IsWeakPoint(Vector3 hitPoint)
    {
        Bounds b = cc != null && cc.enabled ? cc.bounds : new Bounds(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
        if (hitPoint.y >= b.max.y - b.size.y * headZone) return true;
        Vector3 fromCenter = hitPoint - b.center; fromCenter.y = 0f;
        if (fromCenter.sqrMagnitude < 0.0004f) return false;   // 打在正中心，分不出前後
        Vector3 back = -transform.forward; back.y = 0f;
        return Vector3.Angle(back, fromCenter) <= backArcDegrees;
    }

    void WeakPointFeedback(Vector3 hitPoint)
    {
        CombatVFX.Impact(hitPoint, weakPointColor);
        GameAudio.Play("SFX/RPG/knifeSlice", hitPoint, 0.9f, 0.75f, 0.06f, 1f, 35f);
        ThirdPersonCamera.ShakeMain(0.16f, 0.12f);
        var anim = GetComponentInChildren<Animator>();
        SmoothTween.PunchScale(anim != null ? anim.transform : transform, 0.12f, 0.22f);
    }

    /// <summary>直接死亡（例如掉出場地）</summary>
    public void Kill()
    {
        if (!IsDead) { Health = 0f; Die(); }
    }

    public void HealFull()
    {
        if (!IsDead) Health = maxHealth;
    }

    void Die()
    {
        IsDead = true;
        ForceShowBar = false;
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.8f, 0.7f, 0.08f, 1f, 30f);
        if (controller != null) controller.enabled = false; // 先停掉移動，才能關閉 CharacterController
        OnDied?.Invoke();
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        cc.enabled = false;
        SetFlash(false);

        // 倒下的那一側懸空（被打出橋外 / 懸崖邊）：翻滾著掉下去
        if (rollOffLedges && TryFindLedge(out Vector3 fallDir))
        {
            yield return RollOffLedge(fallDir);
            yield break;
        }

        // 在空中被打死（跳躍 / 從天而降途中）：先掉到地面；下面是洞就一直掉下去
        float feetOffset = cc.height * 0.5f - cc.center.y;
        float vy = 0f, fallen = 0f;
        while (fallen < 3f)
        {
            Vector3 feet = transform.position + Vector3.down * feetOffset;
            float step = Mathf.Max(0f, -vy * Time.deltaTime);
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, fallHits, 0.4f + step, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (!(fallHits[i].collider is CharacterController) && fallHits[i].point.y > ground) ground = fallHits[i].point.y;
            if (!float.IsNegativeInfinity(ground))
            {
                transform.position += Vector3.up * (ground - feet.y);
                break;
            }
            vy -= 25f * Time.deltaTime;
            transform.position += Vector3.up * vy * Time.deltaTime;
            fallen += Time.deltaTime;
            yield return null;
        }

        float t = 0f;
        var visual = GetComponent<EnemyVisual>();
        if (visual != null && visual.HasAnimator)
        {
            // 有模型：等死亡動畫播完
            yield return new WaitForSeconds(visual.deathDuration + 0.5f);
        }
        else
        {
            // 沒有模型（膠囊）：整個往後倒
            Quaternion from = transform.rotation;
            Quaternion to = from * Quaternion.Euler(-80f, 0f, 0f);
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                transform.rotation = Quaternion.Slerp(from, to, t / 0.4f);
                yield return null;
            }
            yield return new WaitForSeconds(1f);
        }

        // 沉入地面後消失
        Vector3 p = transform.position;
        t = 0f;
        while (t < 0.8f)
        {
            t += Time.deltaTime;
            transform.position = p + Vector3.down * (t / 0.8f) * 1.2f;
            yield return null;
        }
        foreach (var r in renderers) if (r != null) r.enabled = false;
    }

    // ---------------- 橋邊被打倒：翻滾掉落 ----------------

    readonly RaycastHit[] ledgeHits = new RaycastHit[8];

    /// <summary>腳下 point 附近 ledgeProbeDepth 以內有沒有地面（角色、觸發區、自己不算）</summary>
    bool HasGround(Vector3 point, float feetY)
    {
        Vector3 origin = new Vector3(point.x, feetY + 0.8f, point.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, ledgeHits, 0.8f + ledgeProbeDepth, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = ledgeHits[i].collider;
            if (c is CharacterController || c.transform.IsChildOf(transform)) continue;
            if (ledgeHits[i].normal.y > 0.5f) return true;
        }
        return false;
    }

    /// <summary>
    /// 被打倒的方向（最後一下的擊退方向，左右也看一下）有沒有懸空。
    /// 腳底正下方沒地面也算（已經站在橋外）。
    /// </summary>
    bool TryFindLedge(out Vector3 fallDir)
    {
        fallDir = LastHitDirection.sqrMagnitude > 0.01f ? LastHitDirection : -transform.forward;
        float feetY = transform.position.y - (cc.height * 0.5f - cc.center.y);
        if (!HasGround(transform.position, feetY)) return true;
        float reach = cc.radius + ledgeProbeReach;
        Vector3 side = Vector3.Cross(Vector3.up, fallDir);
        // 先看正後方，再看偏左 / 偏右 45 度（被斜斜地打到橋邊）
        foreach (var d in new[] { fallDir, (fallDir + side).normalized, (fallDir - side).normalized })
        {
            if (!HasGround(transform.position + d * reach, feetY))
            {
                fallDir = d;
                return true;
            }
        }
        return false;
    }

    /// <summary>往 fallDir 翻過橋邊：先往外倒、越過邊緣後一邊翻滾一邊掉下去，最後消失</summary>
    IEnumerator RollOffLedge(Vector3 fallDir)
    {
        var visual = GetComponent<EnemyVisual>();
        if (visual != null) visual.PlayLedgeFall();
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.7f, 0.55f, 0.08f, 1f, 40f);

        Vector3 axis = Vector3.Cross(Vector3.up, fallDir); // 往 fallDir 那一側倒下去的轉軸
        Vector3 velocity = fallDir * 2.6f + Vector3.up * 1.6f;
        Quaternion start = transform.rotation;
        float angle = 0f, t = 0f;
        // 0.25 秒內先往外倒（還沒離開橋面，翻得慢一點），之後全速翻滾
        while (t < ledgeFallTime)
        {
            float dt = Time.deltaTime;
            t += dt;
            velocity += Vector3.down * 22f * dt;
            velocity.x *= 1f - 0.4f * dt;
            velocity.z *= 1f - 0.4f * dt;
            transform.position += velocity * dt;
            float spin = Mathf.Lerp(ledgeTumbleSpeed * 0.45f, ledgeTumbleSpeed, Mathf.Clamp01(t / 0.25f));
            angle += spin * dt;
            // 翻滾之外加一點側轉，看起來是失控地滾下去
            transform.rotation = Quaternion.AngleAxis(angle, axis) * Quaternion.AngleAxis(Mathf.Sin(t * 3.1f) * 25f, fallDir) * start;
            yield return null;
        }
        foreach (var r in renderers) if (r != null) r.enabled = false;
    }

    /// <summary>復活（位置由 EnemyAI 負責放回原位）</summary>
    public void Revive()
    {
        StopAllCoroutines();
        IsDead = false;
        Health = maxHealth;
        LastHitTime = float.NegativeInfinity;
        foreach (var r in renderers) if (r != null) r.enabled = true;
        flashing = true;
        SetFlash(false);
        cc.enabled = true;
        if (controller != null) controller.enabled = true;
    }

    void Update()
    {
        if (!IsDead) SetFlash(Time.time - LastHitTime < flashTime);
    }

    void SetFlash(bool on)
    {
        if (on == flashing) return;
        flashing = on;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            mpb.Clear();
            if (on)
            {
                mpb.SetColor(BaseColorId, flashColor);
                mpb.SetColor(ColorId, flashColor);
            }
            r.SetPropertyBlock(mpb);
        }
    }

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (!showHealthBar || IsDead) return;
        if (!ForceShowBar && Time.time - LastHitTime > barShowTime) return;

        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * barHeightOffset);
        if (sp.z <= 0f || sp.z > 40f) return; // 在鏡頭後面或太遠就不畫

        const float w = 60f, h = 6f;
        float x = sp.x - w * 0.5f, y = Screen.height - sp.y - h;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
        GUI.color = new Color(0.8f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
