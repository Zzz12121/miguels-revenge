using UnityEngine;

/// <summary>
/// 茶屋甜點（三色糰子）的護盾：最大生命 20% 的量，受傷時先扣護盾、扣完才扣血（PlayerHealth.TakeDamage 呼叫 Absorb）。
/// 沒有時間限制，用完或倒下就沒了；存在 static 裡，換關卡還留著（不寫進存檔）。
/// 血條上方畫一層淡金色（VitalsHud），擋下傷害時身上閃一圈金光。
/// </summary>
public static class SweetShield
{
    /// <summary>剩下的護盾（生命點數）</summary>
    public static float Amount { get; private set; }
    /// <summary>吃甜點時給的量（畫血條用：Amount / Max = 還剩幾成）</summary>
    public static float Max { get; private set; }
    /// <summary>上一次擋下傷害的時間（unscaled，血條閃一下用）</summary>
    public static float LastHitTime { get; private set; } = float.NegativeInfinity;

    static bool hooked;
    static PlayerHealth hookedTo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        Amount = Max = 0f;
        LastHitTime = float.NegativeInfinity;
        hooked = false;
        hookedTo = null;
    }

    /// <summary>給護盾（amount 點）；已經有這麼多就不給，回傳 false</summary>
    public static bool Grant(float amount)
    {
        HookDeath();
        if (Amount + 0.01f >= amount) return false;
        Amount = Max = amount;
        return true;
    }

    /// <summary>受到 damage 點傷害：先扣護盾，回傳剩下要扣血的量</summary>
    public static float Absorb(float damage, Transform who)
    {
        if (Amount <= 0f || damage <= 0f) return damage;
        HookDeath();
        float taken = Mathf.Min(Amount, damage);
        Amount -= taken;
        LastHitTime = Time.unscaledTime;
        if (who != null)
        {
            GameAudio.Play("SFX/RPG/metalClick", who.position, 0.55f, Amount <= 0f ? 0.7f : 1.3f, 0.05f, 0.3f);
            ShieldFlash.Play(who, Amount <= 0f);
        }
        if (Amount < 0.01f) { Amount = 0f; Max = 0f; }
        return damage - taken;
    }

    /// <summary>倒下時護盾消失（每個場景的玩家各掛一次；VillageEconomy 在每次載入場景時呼叫）</summary>
    public static void HookDeath()
    {
        var hp = PlayerHealth.Find();
        if (hp == null || (hooked && hookedTo == hp)) return;
        if (hookedTo != null) hookedTo.OnDied -= Clear;
        hp.OnDied += Clear;
        hookedTo = hp;
        hooked = true;
    }

    static void Clear() { Amount = Max = 0f; }
}

/// <summary>護盾擋下傷害時，身上閃一圈淡金色的光（一個擴散、變淡的球），破掉時比較大</summary>
public sealed class ShieldFlash : MonoBehaviour
{
    static Material material;
    float t, life = 0.35f, size;
    Renderer rend;
    Transform follow;

    public static void Play(Transform who, bool broke)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "SweetShieldFlash";
        var f = go.AddComponent<ShieldFlash>();
        f.follow = who;
        f.size = broke ? 2.6f : 2f;
        f.life = broke ? 0.5f : 0.3f;
        f.rend = go.GetComponent<Renderer>();
        f.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (material == null)
        {
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            material = new Material(sh) { name = "Runtime_SweetShieldFlash" };
        }
        f.rend.sharedMaterial = material;
        f.Update();
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (follow != null) transform.position = follow.position + Vector3.up * 1f;
        transform.localScale = Vector3.one * Mathf.Lerp(size * 0.7f, size, Mathf.Sqrt(k));
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", new Color(1f, 0.85f, 0.45f, 0.28f * (1f - k)));
        block.SetColor("_BaseColor", new Color(1f, 0.85f, 0.45f, 0.28f * (1f - k)));
        rend.SetPropertyBlock(block);
        if (k >= 1f) Destroy(gameObject);
    }
}
