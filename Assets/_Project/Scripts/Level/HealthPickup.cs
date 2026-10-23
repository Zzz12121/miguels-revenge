using UnityEngine;

/// <summary>
/// 補血道具：漂浮旋轉的紅十字，玩家走過去就自動撿起來回血。
///   - 血是滿的時候不會被撿走（留著之後再用），走近會顯示「HP 已滿」
///   - 撿起時：音效、綠色光點往上飄、畫面上跳出「+30 HP」
/// 用選單 GeminiGrowUp > 放置補血道具 自動散佈到每個關卡（Prefab：Prefabs/HealthPickup.prefab）。
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

    void Start()
    {
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
