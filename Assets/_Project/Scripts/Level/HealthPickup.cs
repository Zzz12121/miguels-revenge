using UnityEngine;

/// <summary>
/// 補血道具：漂浮旋轉的紅十字，玩家走過去就自動撿起來回血。
///   - 血是滿的時候不會被撿走（留著之後再用），走近會顯示「HP 已滿」
///   - 撿起時：音效、綠色光點往上飄、畫面上跳出「+30 HP」
/// 用選單 GeminiGrowUp > 放置補血道具 自動散佈到每個關卡（Prefab：Prefabs/HealthPickup.prefab）。
/// 木桶、木箱被打碎時也會用 HealthPickup.Spawn 掉出一個（會從地上彈出來）。
/// </summary>
public class HealthPickup : MonoBehaviour
{
    [Tooltip("回復多少血")]
    public float healAmount = 30f;
    [Tooltip("離多近會撿起來 (m)")]
    public float pickupRadius = 1.4f;

    [Header("外觀")]
    [Tooltip("會上下漂浮、旋轉的部分（留空 = 第一個子物件）")]
    public Transform visual;
    public float spinSpeed = 90f;
    public float bobHeight = 0.15f;
    public float bobSpeed = 2f;

    Transform player;
    PlayerHealth playerHealth;
    Vector3 visualBase;
    float bobPhase;
    bool picked;
    float pickedTime;
    Vector3 popupWorldPos;

    /// <summary>
    /// 在 groundPos（地面上）生成一個補血道具，從地上彈出來。
    /// prefab 留空 = 用程式做一個跟 Prefabs/HealthPickup 一樣的紅十字（紅光、會漂浮旋轉）。
    /// </summary>
    public static HealthPickup Spawn(Vector3 groundPos, GameObject prefab = null, float healAmount = 30f)
    {
        HealthPickup pickup;
        if (prefab != null)
        {
            var go = Instantiate(prefab, groundPos, Quaternion.identity);
            go.name = prefab.name;
            pickup = go.GetComponent<HealthPickup>();
            if (pickup == null) pickup = go.AddComponent<HealthPickup>();
        }
        else pickup = BuildDefault(groundPos, healAmount);
        pickup.popIn = true;
        return pickup;
    }

    static Material sharedMat;

    static HealthPickup BuildDefault(Vector3 pos, float healAmount)
    {
        var root = new GameObject("HealthPickup");
        root.transform.position = pos;
        var pickup = root.AddComponent<HealthPickup>();
        pickup.healAmount = healAmount;
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        visual.localPosition = new Vector3(0f, 0.9f, 0f);

        if (sharedMat == null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            if (sh != null)
            {
                sharedMat = new Material(sh) { name = "M_HealthPickup_Runtime" };
                Color red = new Color(0.9f, 0.1f, 0.1f);
                sharedMat.SetColor("_BaseColor", red);
                sharedMat.SetColor("_Color", red);
                sharedMat.EnableKeyword("_EMISSION");
                sharedMat.SetColor("_EmissionColor", new Color(1f, 0.15f, 0.15f) * 2f);
            }
        }
        foreach (var (n, scale) in new[] { ("Vertical", new Vector3(0.18f, 0.6f, 0.18f)), ("Horizontal", new Vector3(0.6f, 0.18f, 0.18f)) })
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = n;
            part.transform.SetParent(visual, false);
            part.transform.localScale = scale;
            Destroy(part.GetComponent<Collider>());
            var r = part.GetComponent<Renderer>();
            if (sharedMat != null) r.sharedMaterial = sharedMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var light = new GameObject("Glow").AddComponent<Light>();
        light.transform.SetParent(visual, false);
        light.type = LightType.Point;
        light.color = new Color(1f, 0.3f, 0.3f);
        light.range = 3.5f;
        light.intensity = 2f;
        light.shadows = LightShadows.None;
        pickup.visual = visual;
        return pickup;
    }

    // 從地上彈出來（木桶碎掉時）：一開始很小、往上跳一下再落回漂浮的高度
    bool popIn;
    float popTime;
    const float PopDuration = 0.45f;

    void Start()
    {
        popTime = Time.time;
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        if (visual != null) visualBase = visual.localPosition;
        bobPhase = Random.value * Mathf.PI * 2f; // 每個道具漂浮的節奏不一樣
    }

    void Update()
    {
        if (picked)
        {
            if (Time.time - pickedTime > 1.2f) Destroy(gameObject);
            return;
        }

        if (popIn && visual != null)
        {
            float k = (Time.time - popTime) / PopDuration;
            if (k < 1f)
            {
                visual.localScale = Vector3.one * Mathf.SmoothStep(0.2f, 1f, Mathf.Clamp01(k * 1.5f));
                visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.9f - (1f - k) * 0.6f);
                visual.Rotate(0f, spinSpeed * 3f * Time.deltaTime, 0f, Space.World);
                return; // 彈出來的這段時間還不能撿
            }
            popIn = false;
            visual.localScale = Vector3.one;
        }

        if (visual != null)
        {
            visual.localPosition = visualBase + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + bobPhase) * bobHeight);
            visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        }

        if (playerHealth == null || playerHealth.IsDead) return;
        if (!InRange() || playerHealth.Health >= playerHealth.maxHealth) return;

        Pick();
    }

    bool InRange()
    {
        Vector3 d = player.position - transform.position;
        return new Vector2(d.x, d.z).magnitude <= pickupRadius && Mathf.Abs(d.y) < 2.5f;
    }

    void Pick()
    {
        picked = true;
        pickedTime = Time.time;
        float before = playerHealth.Health;
        playerHealth.Heal(healAmount);
        healed = Mathf.RoundToInt(playerHealth.Health - before);

        GameAudio.Play("SFX/RPG/handleCoins", transform.position, 0.9f, 1.3f, 0.05f, 0.5f, 30f);
        GameAudio.Play("SFX/RPG/bookOpen", transform.position, 0.6f, 1.6f, 0.05f, 0.5f, 30f);
        CombatVFX.Heal(player);
        popupWorldPos = player.position + Vector3.up * 2.3f;

        if (visual != null) visual.gameObject.SetActive(false);
        foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;
    }

    int healed;
    static GUIStyle popupStyle;

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        var cam = Camera.main;
        if (cam == null) return;

        if (popupStyle == null)
            popupStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

        if (picked)
        {
            // 「+30 HP」往上飄、慢慢淡出
            float t = (Time.time - pickedTime) / 1.2f;
            Vector3 sp = cam.WorldToScreenPoint(popupWorldPos + Vector3.up * t * 0.8f);
            if (sp.z <= 0f) return;
            GUI.color = new Color(0.4f, 1f, 0.5f, 1f - t);
            GUI.Label(new Rect(sp.x - 100f, Screen.height - sp.y - 20f, 200f, 40f), $"+{healed} HP", popupStyle);
            GUI.color = Color.white;
            return;
        }

        // 血滿的時候走過去：提示「HP 已滿」
        if (playerHealth != null && !playerHealth.IsDead && player != null && InRange()
            && playerHealth.Health >= playerHealth.maxHealth)
        {
            Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 1.5f);
            if (sp.z <= 0f) return;
            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            GUI.Label(new Rect(sp.x - 100f, Screen.height - sp.y - 20f, 200f, 40f), "HP 已滿", popupStyle);
            GUI.color = Color.white;
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, pickupRadius);
    }
}
