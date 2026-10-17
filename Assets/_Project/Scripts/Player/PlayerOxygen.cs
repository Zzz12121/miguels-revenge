using UnityEngine;

/// <summary>
/// 氧氣條（第二關維京村莊用）：
///   - 玩家的頭沉到水面（waterLevel）以下就算潛水，氧氣開始減少
///   - 氧氣用完後每 drownInterval 秒扣一次血（溺水），可能因此死亡
///   - 回到水面上氧氣會慢慢回滿
///   - 氧氣條只在水中（或氧氣還沒回滿）時顯示在血條下面，回滿後淡出；在陸地上看不到
///
/// 不用手動加：PlayerHealth 在有水的關卡（WaterScenes）會自動加上這個元件。
/// </summary>
[RequireComponent(typeof(PlayerHealth))]
public class PlayerOxygen : MonoBehaviour
{
    /// <summary>有水、需要氧氣條的關卡</summary>
    static readonly string[] WaterScenes = { "The_Viking_Village" };

    public static bool SceneHasWater(string sceneName) => System.Array.IndexOf(WaterScenes, sceneName) >= 0;

    [Header("水")]
    [Tooltip("水面高度（世界座標 Y）。維京村莊的海平面是 1.3（跟 VikingBoat 一樣）")]
    public float waterLevel = 1.3f;
    [Tooltip("頭頂往下多少公尺算是口鼻的位置，這個點低於水面就算潛水")]
    public float headOffset = 0.25f;

    [Header("氧氣")]
    public float maxOxygen = 100f;
    [Tooltip("潛水時每秒消耗多少氧氣（100 / 8 = 大約可以憋氣 12 秒）")]
    public float drainPerSecond = 8f;
    [Tooltip("浮出水面後每秒回復多少氧氣")]
    public float refillPerSecond = 35f;

    [Header("溺水")]
    [Tooltip("氧氣用完後每隔幾秒扣一次血")]
    public float drownInterval = 1f;
    public float drownDamage = 10f;

    [Header("音效（可留空）")]
    public AudioClip enterWaterSound;
    public AudioClip surfaceSound;

    public float Oxygen { get; private set; }
    public float Oxygen01 => maxOxygen > 0f ? Oxygen / maxOxygen : 0f;
    public bool IsUnderwater { get; private set; }

    CharacterController cc;
    PlayerHealth health;
    float drownTimer;
    float barAlpha;

    /// <summary>口鼻的世界座標高度</summary>
    float MouthY => cc.bounds.max.y - headOffset;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        Oxygen = maxOxygen;
        health.OnRespawned += Refill;
    }

    void OnDestroy()
    {
        if (health != null) health.OnRespawned -= Refill;
    }

    void Refill()
    {
        Oxygen = maxOxygen;
        IsUnderwater = false;
        drownTimer = 0f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        // 死掉、搭船 / 過場（CharacterController 被關掉）的時候不算潛水
        bool under = !health.IsDead && cc != null && cc.enabled && MouthY < waterLevel;

        if (under != IsUnderwater)
        {
            var clip = under ? enterWaterSound : surfaceSound;
            if (clip != null) GameAudio.PlayAt(clip, transform.position, 0.7f, 1f, 0f);
            drownTimer = 0f;
        }
        IsUnderwater = under;

        if (under)
        {
            Oxygen = Mathf.Max(0f, Oxygen - drainPerSecond * dt);
            if (Oxygen <= 0f)
            {
                drownTimer += dt;
                if (drownTimer >= drownInterval)
                {
                    drownTimer -= drownInterval;
                    health.TakeDamage(drownDamage, default, true, "溺水了");
                }
            }
        }
        else if (!health.IsDead)
        {
            Oxygen = Mathf.Min(maxOxygen, Oxygen + refillPerSecond * dt);
        }

        // 只有在水中（或氧氣還沒回滿）時顯示氧氣條
        bool show = under || (Oxygen < maxOxygen && !health.IsDead);
        barAlpha = Mathf.MoveTowards(barAlpha, show ? 1f : 0f, dt * (show ? 6f : 1.5f));
    }

    // ---------- 氧氣條（畫在血條下面，跟 PlayerHealth 的 HUD 同一個風格） ----------
    void OnGUI()
    {
        if (IsUnderwater)
        {
            // 水中畫面帶一點藍色
            GUI.color = new Color(0.05f, 0.25f, 0.45f, 0.28f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }
        if (barAlpha <= 0.001f) { GUI.color = Color.white; return; }

        const float w = 260f, h = 14f, x = 20f, y = 46f;
        float a = barAlpha;
        // 快沒氧氣時閃爍
        bool low = Oxygen01 < 0.25f;
        if (low && IsUnderwater) a *= 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.time * 8f));

        GUI.color = new Color(0f, 0f, 0f, 0.6f * a);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        GUI.color = low ? new Color(1f, 0.45f, 0.2f, a) : new Color(0.3f, 0.75f, 1f, a);
        GUI.DrawTexture(new Rect(x, y, w * Oxygen01, h), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, a);
        var style = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        string text = Oxygen <= 0f && IsUnderwater ? "氧氣用完了！快浮出水面" : $"氧氣 {Mathf.CeilToInt(Oxygen01 * 100f)}%";
        GUI.Label(new Rect(x + 6, y - 2, w, h + 4), text, style);
        GUI.color = Color.white;
    }
}
