using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 檢查點（類似篝火 / 賜福）：玩家走近就會
///   - 把重生點設在這裡
///   - 補滿血
/// 發光部分會從暗變亮，表示已啟用。
/// 勾選 Build Stone Stele 會在執行時自動用基本形狀蓋一塊石碑（不用另外擺模型）：
/// 平常石碑上的紋路是暗的，啟用後紅色紋路由下往上慢慢顯現並發光。
/// 其他腳本也可以直接呼叫 Activate()（例如 Boss 戰開始時，把重生點設在 Boss 區域入口）。
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Tooltip("重生的位置與面向，留空就用這個物件本身（有石碑時會自動放在石碑前方）")]
    public Transform respawnPoint;
    [Tooltip("走到多近會觸發 (m)")]
    public float activateRadius = 2.5f;
    public bool healOnTouch = true;

    [Header("外觀（可留空）")]
    public Renderer glow;
    public Color inactiveColor = new Color(0.3f, 0.25f, 0.15f);
    public Color activeColor = new Color(1f, 0.8f, 0.3f);

    [Header("石碑")]
    [Tooltip("執行時自動蓋一塊石碑當檢查點的外觀")]
    [FormerlySerializedAs("buildStonePillar")]
    public bool buildStoneStele = false;
    [Tooltip("石碑碑身高度 (m)")]
    [FormerlySerializedAs("pillarHeight")]
    public float steleHeight = 2.6f;
    [Tooltip("石碑紋路啟用後的顏色")]
    public Color runeColor = new Color(1f, 0.12f, 0.08f);
    [Tooltip("紋路顯現所需時間 (秒)")]
    public float revealTime = 1.5f;
    [Tooltip("開始時往下找地面，把石碑放在地上")]
    public bool snapToGround = false;
    [Tooltip("重生點離石碑多遠（往這個物件的正前方）")]
    public float respawnDistance = 2.2f;

    Transform player;
    PlayerHealth playerHealth;
    bool playerInside;
    bool activated;
    float activatedTime = float.NegativeInfinity;
    float messageTime = float.NegativeInfinity;
    string message;
    MaterialPropertyBlock mpb;
    Light glowLight;
    // 石碑的紋路：每一段記下它在碑面上的高度（0 = 底、1 = 頂），顯現時由下往上亮
    readonly List<(Renderer r, float height)> runes = new List<(Renderer, float)>();

    bool IsStele => runes.Count > 0;
    Color MessageColor => IsStele ? runeColor : activeColor;

    void Start()
    {
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null) player = playerHealth.transform;
        mpb = new MaterialPropertyBlock();
        if (snapToGround) SnapToGround();
        if (buildStoneStele && glow == null) BuildStele();
        if (respawnPoint == null && buildStoneStele)
        {
            respawnPoint = new GameObject("RespawnPoint").transform;
            respawnPoint.SetParent(transform, false);
            respawnPoint.localPosition = new Vector3(0f, 1.1f, respawnDistance);
        }
        if (IsStele) SetRunes(0f);
        else SetGlow(inactiveColor);
    }

    void Update()
    {
        if (IsStele && activated) UpdateRunes();
        if (player == null || playerHealth == null) return;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        bool inside = d.magnitude <= activateRadius && !playerHealth.IsDead;

        if (inside && !playerInside) // 剛走進範圍
        {
            if (healOnTouch) playerHealth.HealFull();
            Activate(activated ? "HP RESTORED" : "CHECKPOINT REACHED");
        }
        playerInside = inside;
    }

    /// <summary>把玩家的重生點設在這裡（第一次會亮起來並顯示訊息）</summary>
    public void Activate() => Activate(activated ? null : "CHECKPOINT REACHED");

    void Activate(string text)
    {
        if (playerHealth != null) playerHealth.respawnPoint = respawnPoint != null ? respawnPoint : transform;
        if (text != null)
        {
            message = text;
            messageTime = Time.time;
            GameAudio.Play(activated ? "SFX/RPG/metalClick" : "SFX/RPG/metalLatch", transform.position, 0.9f, 0.9f, 0f, 0.3f);
        }
        if (activated) return;
        activated = true;
        activatedTime = Time.time;
        if (IsStele)
        {
            GameAudio.Play("SFX/roar_rock_crumble", transform.position, 0.45f, 1.3f, 0f, 1f, 30f);
            CombatVFX.Ring(transform.position, runeColor, 2.5f);
        }
        else SetGlow(activeColor);
        if (glowLight != null) glowLight.enabled = true;
    }

    void SnapToGround()
    {
        var hits = Physics.RaycastAll(transform.position + Vector3.up * 10f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider is CharacterController || h.collider.transform.IsChildOf(transform)) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (!float.IsNegativeInfinity(best))
        {
            Vector3 p = transform.position;
            p.y = best;
            transform.position = p;
        }
    }

    // ---------------- 石碑 ----------------

    const float SteleWidth = 1.3f, SteleThick = 0.38f, BaseHeight = 0.35f;

    /// <summary>用基本形狀蓋一塊石碑：底座 → 碑身 → 圓弧碑頂，前後兩面刻上紋路</summary>
    void BuildStele()
    {
        var stone = MakeMaterial(new Color(0.42f, 0.4f, 0.38f), 0.1f, false);
        var dark = MakeMaterial(new Color(0.3f, 0.29f, 0.27f), 0.05f, false);
        var rune = MakeMaterial(Color.black, 0.5f, true);

        float h = steleHeight;
        Part(PrimitiveType.Cube, "Base", new Vector3(0f, BaseHeight * 0.5f, 0f), new Vector3(SteleWidth + 0.6f, BaseHeight, SteleThick + 0.6f), dark, true);
        Part(PrimitiveType.Cube, "Body", new Vector3(0f, BaseHeight + h * 0.5f, 0f), new Vector3(SteleWidth, h, SteleThick), stone, true);
        // 圓弧碑頂：圓柱轉成前後向，一半埋在碑身裡
        var top = Part(PrimitiveType.Cylinder, "Top", new Vector3(0f, BaseHeight + h, 0f), new Vector3(SteleWidth, SteleThick * 0.5f, SteleWidth), stone, true);
        top.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // 前後兩面都刻（玩家從哪邊走過來都看得到）
        foreach (float side in new[] { 1f, -1f }) CarveRunes(side, rune);

        glowLight = new GameObject("Light").AddComponent<Light>();
        glowLight.transform.SetParent(transform, false);
        glowLight.transform.localPosition = new Vector3(0f, BaseHeight + h * 0.6f, 0f);
        glowLight.type = LightType.Point;
        glowLight.color = runeColor;
        glowLight.range = 7f;
        glowLight.intensity = 0f;
        glowLight.enabled = false;
    }

    /// <summary>在碑面（side = 1 前面、-1 後面）刻紋路：中央直線、菱形環、中心的眼、上下的符文橫紋</summary>
    void CarveRunes(float side, Material mat)
    {
        float h = steleHeight;
        float z = side * (SteleThick * 0.5f + 0.004f);
        const float line = 0.055f, depth = 0.012f;

        // u = 左右 (m)，v = 碑面高度比例 (0~1)
        void Seg(float u, float v, float length, float angle, float width = line)
        {
            var t = Part(PrimitiveType.Cube, "Rune", new Vector3(u, BaseHeight + v * h, z), new Vector3(width, length, depth), mat, false);
            t.localRotation = Quaternion.Euler(0f, 0f, angle);
            runes.Add((t.GetComponent<Renderer>(), v));
        }

        // 中央的直線（從底部一路往上，紋路會順著它亮起來）
        Seg(0f, 0.27f, h * 0.42f, 0f);
        Seg(0f, 0.86f, h * 0.14f, 0f);

        // 菱形環
        float cv = 0.62f, r = 0.36f;
        float side45 = r * Mathf.Sqrt(2f);
        Seg(-r * 0.5f, cv + r * 0.5f / h, side45, -45f);
        Seg(r * 0.5f, cv + r * 0.5f / h, side45, 45f);
        Seg(-r * 0.5f, cv - r * 0.5f / h, side45, 45f);
        Seg(r * 0.5f, cv - r * 0.5f / h, side45, -45f);
        // 中心的眼
        Seg(0f, cv, 0.16f, 45f, 0.16f);

        // 菱形兩側的爪狀斜紋
        Seg(-0.45f, cv + 0.06f, 0.22f, 30f);
        Seg(0.45f, cv + 0.06f, 0.22f, -30f);
        Seg(-0.45f, cv - 0.06f, 0.22f, -30f);
        Seg(0.45f, cv - 0.06f, 0.22f, 30f);

        // 下方的符文橫紋（長短不一，像一行文字）：每兩個數字 = 起點 u、長度，避開中央直線
        float[] rows = { 0.12f, 0.2f, 0.28f };
        float[][] bars =
        {
            new[] { -0.5f, 0.2f, -0.24f, 0.16f, 0.08f, 0.14f, 0.28f, 0.22f },
            new[] { -0.46f, 0.32f, 0.1f, 0.3f },
            new[] { -0.52f, 0.12f, -0.34f, 0.24f, 0.12f, 0.18f, 0.36f, 0.14f },
        };
        for (int i = 0; i < rows.Length; i++)
            for (int k = 0; k + 1 < bars[i].Length; k += 2)
                Seg(bars[i][k] + bars[i][k + 1] * 0.5f, rows[i], bars[i][k + 1], 90f);

        // 頂端的弧紋
        Seg(-0.2f, 0.97f, 0.22f, -60f);
        Seg(0.2f, 0.97f, 0.22f, 60f);
    }

    /// <summary>紋路顯現：由下往上逐段亮起，亮完之後輕輕呼吸閃爍</summary>
    void UpdateRunes()
    {
        float k = (Time.time - activatedTime) / Mathf.Max(0.01f, revealTime);
        SetRunes(k);
        if (glowLight != null)
        {
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f);
            glowLight.intensity = 3f * Mathf.Clamp01(k) * pulse;
        }
    }

    /// <summary>reveal：0 = 全暗，1 = 全亮（超過 1 之後是呼吸閃爍）</summary>
    void SetRunes(float reveal)
    {
        Color carved = new Color(0.12f, 0.07f, 0.06f); // 沒啟用時：刻痕只是暗色的凹槽
        float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f);
        foreach (var (r, height) in runes)
        {
            if (r == null) continue;
            // 每一段在自己高度的時間點亮起，花 0.25 的時間從暗到亮
            float lit = Mathf.Clamp01((reveal * 1.25f - height) / 0.25f);
            Color baseColor = Color.Lerp(carved, runeColor, lit);
            Color emission = runeColor * (lit * 3f * (reveal >= 1f ? pulse : 1f));
            mpb.Clear();
            mpb.SetColor("_BaseColor", baseColor);
            mpb.SetColor("_Color", baseColor);
            mpb.SetColor("_EmissionColor", emission);
            r.SetPropertyBlock(mpb);
        }
    }

    Transform Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, bool solid)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (!solid) Destroy(go.GetComponent<Collider>());
        var rend = go.GetComponent<Renderer>();
        if (mat != null) rend.sharedMaterial = mat;
        if (!solid) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    static Material MakeMaterial(Color c, float smoothness, bool emissive)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) return null;
        var m = new Material(shader);
        m.SetColor("_BaseColor", c);
        m.SetColor("_Color", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (emissive)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", Color.black);
        }
        return m;
    }

    void SetGlow(Color c)
    {
        if (glow == null) return;
        mpb.Clear();
        mpb.SetColor("_BaseColor", c);
        mpb.SetColor("_Color", c);
        glow.SetPropertyBlock(mpb);
    }

    void OnGUI()
    {
        float since = Time.time - messageTime;
        if (since > 2f || GameUI.OverlayOpen) return;
        GameUI.Banner(message, MessageColor, Mathf.Clamp01((2f - since) / 0.5f));
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, activateRadius);
        if (buildStoneStele)
        {
            Gizmos.color = new Color(0.6f, 0.58f, 0.52f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(new Vector3(0f, BaseHeight + steleHeight * 0.5f, 0f), new Vector3(SteleWidth, steleHeight, SteleThick));
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.2f, 0.15f, 0.8f);
            Gizmos.DrawLine(transform.position + Vector3.up, transform.position + Vector3.up + transform.forward * respawnDistance);
        }
    }
}
