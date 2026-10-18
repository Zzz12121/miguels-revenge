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

    [Header("頭上血條")]
    public bool showHealthBar = true;
    [Tooltip("血條離角色中心多高 (m)")]
    public float barHeightOffset = 1.5f;
    [Tooltip("被打後血條顯示幾秒")]
    public float barShowTime = 4f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public float LastHitTime { get; private set; } = float.NegativeInfinity;
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

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || amount <= 0f) return;
        if (hitsToKill > 0) amount = maxHealth / hitsToKill; // 每一下扣固定比例
        Health = Mathf.Max(0f, Health - amount);
        LastHitTime = Time.time;

        // 往打擊點的反方向退
        if (controller != null && controller.enabled)
        {
            Vector3 away = transform.position - hitPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            controller.AddImpulse(away.normalized * hitKnockback + Vector3.up * 1.5f);
        }

        // 受傷音效：盔甲 / 骨頭被打中的聲音（音高隨機一點，連續打不會聽起來一樣）
        GameAudio.PlayAt(GameAudio.Pick(hurtSounds, "SFX/RPG/metalPot1", "SFX/RPG/metalPot2", "SFX/RPG/metalPot3"),
                         transform.position, 0.75f, Random.Range(0.75f, 0.95f), 1f, 35f);
        OnDamaged?.Invoke(amount);
        if (Health <= 0f) Die();
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
