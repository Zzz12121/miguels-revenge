using System.Collections;
using UnityEngine;

/// <summary>
/// 時鐘敵人血量：仿照 EnemyHealth，支援被打閃白、擊退、頭上血條、死亡倒地沉入、重生復活
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ClockEnemyHealth : MonoBehaviour, IDamageable
{
    public float maxHealth = 80f;
    [Tooltip("被打中時往後退的力道")]
    public float hitKnockback = 3f;
    [Tooltip("被打時身體閃一下的時間 (秒)")]
    public float flashTime = 0.08f;
    [Tooltip("被打時閃的顏色")]
    public Color flashColor = Color.white;

    [Header("頭上血條")]
    public bool showHealthBar = true;
    [Tooltip("血條離角色中心多高 (m)")]
    public float barHeightOffset = 1.8f;
    [Tooltip("被打後血條顯示幾秒")]
    public float barShowTime = 4f;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public float LastHitTime { get; private set; } = float.NegativeInfinity;
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
        
        var all = new System.Collections.Generic.List<Renderer>(GetComponentsInChildren<Renderer>(true));
        all.RemoveAll(r => !r.enabled);
        renderers = all.ToArray();
        mpb = new MaterialPropertyBlock();
        Health = maxHealth;
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || amount <= 0f) return;
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

        OnDamaged?.Invoke(amount);
        if (Health <= 0f) Die();
    }

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
        if (controller != null) controller.enabled = false;
        OnDied?.Invoke();
        StartCoroutine(DeathRoutine());
    }

    IEnumerator DeathRoutine()
    {
        cc.enabled = false;
        SetFlash(false);

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
        Quaternion from = transform.rotation;
        Quaternion to = from * Quaternion.Euler(-80f, 0f, 0f);
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(from, to, t / 0.4f);
            yield return null;
        }
        yield return new WaitForSeconds(1f);

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
        if (sp.z <= 0f || sp.z > 40f) return;

        const float w = 60f, h = 6f;
        float x = sp.x - w * 0.5f, y = Screen.height - sp.y - h;
        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x - 1, y - 1, w + 2, h + 2), Texture2D.whiteTexture);
        GUI.color = new Color(0.8f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}