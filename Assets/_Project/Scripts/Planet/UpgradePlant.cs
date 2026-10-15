using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 能力植物：走近按 E 撿起 → 遊戲暫停，三選一升級：
///   1. 跑速　　：走路 / 衝刺速度 ×(1 + speedBonus)
///   2. 攻擊力　：PlayerCombat.damageMultiplier + attackBonus（近戰和遠程都吃）
///   3. 最大生命：PlayerHealth.maxHealth + healthBonus，並補上同樣的血量
/// 可以用滑鼠點，也可以按 1 / 2 / 3。選完植物就消失。
/// </summary>
public class UpgradePlant : MonoBehaviour
{
    [Header("互動")]
    public Key interactKey = Key.E;
    [Tooltip("離植物多近可以撿 (m)")]
    public float interactRange = 3f;

    [Header("升級數值")]
    [Tooltip("跑速提升比例（0.15 = +15%）")]
    public float speedBonus = 0.15f;
    [Tooltip("攻擊倍率增加（0.25 = 傷害 +25%）")]
    public float attackBonus = 0.25f;
    [Tooltip("最大生命增加")]
    public float healthBonus = 30f;

    Transform player;
    ThirdPersonController controller;
    PlayerCombat combat;
    PlayerHealth health;
    ThirdPersonCamera cam;
    bool menuOpen;
    float prevTimeScale = 1f;
    GUIStyle titleStyle, buttonStyle, hintStyle;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p == null) return;
        player = p.transform;
        controller = p.GetComponent<ThirdPersonController>();
        combat = p.GetComponent<PlayerCombat>();
        health = p.GetComponent<PlayerHealth>();
    }

    bool PlayerInRange()
    {
        if (player == null || (health != null && health.IsDead)) return false;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude <= interactRange;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (!menuOpen)
        {
            if (PlayerInRange() && kb[interactKey].wasPressedThisFrame) OpenMenu();
            return;
        }

        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) Choose(0);
        else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) Choose(1);
        else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) Choose(2);
    }

    void OpenMenu()
    {
        menuOpen = true;
        prevTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        if (controller != null) controller.inputEnabled = false;
        // 鏡頭在游標沒鎖時按左鍵會重新鎖住游標，選單開著的時候先停用
        cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.enabled = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.9f, 1.2f, 0f, 0.3f);
    }

    void Choose(int option)
    {
        if (!menuOpen) return;
        string result;
        switch (option)
        {
            case 0:
                controller.walkSpeed *= 1f + speedBonus;
                controller.sprintSpeed *= 1f + speedBonus;
                result = $"跑速 +{speedBonus * 100f:0}%";
                break;
            case 1:
                combat.damageMultiplier += attackBonus;
                result = $"攻擊力 +{attackBonus * 100f:0}%";
                break;
            default:
                health.IncreaseMaxHealth(healthBonus);
                result = $"最大生命 +{healthBonus:0}";
                break;
        }

        menuOpen = false;
        Time.timeScale = prevTimeScale;
        if (controller != null) controller.inputEnabled = true;
        if (cam != null) cam.enabled = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        GameAudio.Play("SFX/RPG/coins", transform.position, 1f, 1f);
        VictoryBanner.ShowSmall("能力提升", result, new Color(0.5f, 1f, 0.6f));
        Destroy(gameObject);
    }

    void OnGUI()
    {
        if (!menuOpen)
        {
            if (!PlayerInRange()) return;
            var prompt = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40f), $"按 {interactKey} 撿起能力植物", prompt);
            return;
        }

        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        }

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float cy = Screen.height * 0.5f;
        GUI.Label(new Rect(0, cy - 170f, Screen.width, 50f), "選擇一項能力升級", titleStyle);

        string[] labels =
        {
            $"1\n跑速\n+{speedBonus * 100f:0}%",
            $"2\n攻擊力\n+{attackBonus * 100f:0}%",
            $"3\n最大生命\n+{healthBonus:0}",
        };
        const float w = 200f, h = 160f, gap = 30f;
        float x0 = (Screen.width - (w * 3f + gap * 2f)) * 0.5f;
        for (int i = 0; i < 3; i++)
            if (GUI.Button(new Rect(x0 + i * (w + gap), cy - 90f, w, h), labels[i], buttonStyle)) Choose(i);

        GUI.Label(new Rect(0, cy + 90f, Screen.width, 30f), "點擊或按 1 / 2 / 3 選擇", hintStyle);
    }

    void OnDisable()
    {
        // 保險：選單開著時物件被關掉，也要把遊戲恢復
        if (!menuOpen) return;
        menuOpen = false;
        Time.timeScale = prevTimeScale;
        if (controller != null) controller.inputEnabled = true;
        if (cam != null) cam.enabled = true;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.5f, 1f, 0.6f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}
