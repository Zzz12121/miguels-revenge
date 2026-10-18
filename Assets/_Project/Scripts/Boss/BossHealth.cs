using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss 血量與畫面下方的 Boss 血條（類似艾爾登法環：名字 + 長條血條 + 受傷時的延遲白條）。
/// 被打時身體會閃白。
/// </summary>
public class BossHealth : MonoBehaviour, IDamageable
{
    public string bossName = "高塔守衛";
    public float maxHealth = 6000f; // 第三關 Boss（原本 600，調成 10 倍）
    [Tooltip("被打時身體閃白的時間 (秒)")]
    public float flashTime = 0.08f;
    [Tooltip("不要閃白的物件（例如光束）")]
    public List<Renderer> excludeFromFlash = new List<Renderer>();

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>要不要顯示畫面下方的血條（戰鬥開始才顯示）</summary>
    public bool ShowBar { get; set; }
    /// <summary>被打倒時在畫面上顯示「強敵已擊敗」的小標題。有過關大標題（VictoryBanner）時 BossController 會把它關掉，兩個才不會疊在一起</summary>
    public bool ShowDefeatLabel { get; set; } = true;
    /// <summary>無敵（例如進入二階段的怒吼時）</summary>
    public bool Invulnerable { get; set; }
    public event System.Action OnDied;
    public event System.Action<float> OnDamaged;

    float trailingHealth;      // 延遲扣掉的白條
    float lastHitTime = float.NegativeInfinity;
    float deathTime;
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Health = trailingHealth = maxHealth;
        mpb = new MaterialPropertyBlock();
        var list = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
        list.RemoveAll(r => excludeFromFlash.Contains(r));
        renderers = list.ToArray();
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || Invulnerable || amount <= 0f) return;
        Health = Mathf.Max(0f, Health - amount);
        lastHitTime = Time.time;
        // 受傷音效：低沉的碎石聲
        GameAudio.Play("SFX/rock_hit", transform.position + Vector3.up * 2f, 0.7f, Random.Range(0.55f, 0.7f), 0f, 1f, 60f);
        OnDamaged?.Invoke(amount);
        if (Health <= 0f) Die();
    }

    /// <summary>測試用：直接把血量設成某個值（會觸發二階段 / 死亡）</summary>
    public void SetHealth(float value)
    {
        if (IsDead) return;
        float before = Health;
        Health = Mathf.Clamp(value, 0f, maxHealth);
        lastHitTime = Time.time;
        if (Health < before) OnDamaged?.Invoke(before - Health);
        if (Health <= 0f) Die();
    }

    void Die()
    {
        if (IsDead) return;
        IsDead = true;
        deathTime = Time.time;
        OnDied?.Invoke();
    }

    public void ExcludeFromFlash(Renderer r)
    {
        if (r == null) return;
        excludeFromFlash.Add(r);
        if (renderers != null) renderers = System.Array.FindAll(renderers, x => x != r);
    }

    public void ResetHealth()
    {
        Health = trailingHealth = maxHealth;
        IsDead = false;
        SetFlash(false);
    }

    void Update()
    {
        // 白條：受傷 0.6 秒後才開始慢慢追上紅條
        if (Time.time - lastHitTime > 0.6f)
            trailingHealth = Mathf.MoveTowards(trailingHealth, Health, maxHealth * 0.5f * Time.deltaTime);
        if (trailingHealth < Health) trailingHealth = Health;

        SetFlash(Time.time - lastHitTime < flashTime);
    }

    bool flashing;
    void SetFlash(bool on)
    {
        if (on == flashing || renderers == null) return;
        flashing = on;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            mpb.Clear();
            if (on)
            {
                mpb.SetColor(BaseColorId, Color.white);
                mpb.SetColor(ColorId, Color.white);
            }
            r.SetPropertyBlock(mpb);
        }
    }

    // ---------- Boss 血條 ----------
    void OnGUI()
    {
        if (!ShowBar) return;
        if (IsDead && Time.time - deathTime > 5f) return; // 擊敗 5 秒後收起血條
        float w = Screen.width * 0.55f, h = 14f;
        float x = (Screen.width - w) * 0.5f, y = Screen.height - 70f;

        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 26f, w, 24f), bossName, nameStyle);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.5f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * (trailingHealth / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = new Color(0.7f, 0.08f, 0.08f);
        GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);

        if (IsDead && ShowDefeatLabel)
        {
            GUI.color = new Color(1f, 0.85f, 0.4f);
            var style = new GUIStyle(GUI.skin.label) { fontSize = 52, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 80f), "強敵已擊敗", style);
        }
        GUI.color = Color.white;
    }
}
