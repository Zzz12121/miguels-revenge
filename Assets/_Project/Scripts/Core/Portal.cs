using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 關卡傳送門：玩家走到門前按 E → 透過 GameManager 前往下一關（或指定場景）。
/// 勾選 Start Locked 的話門一開始是暗的、進不去，要由其他腳本呼叫 Unlock() 才會開。
///
/// 外觀（石環、漩渦、燈光）由選單 GeminiGrowUp > 設定傳送門與關卡順序 建立的 Portal.prefab 提供，
/// 物件的藍色 Z 軸（forward）是門面朝向。
/// </summary>
public class Portal : MonoBehaviour
{
    public enum Destination { NextLevel, SpecificScene }

    [Header("目的地")]
    public Destination destination = Destination.NextLevel;
    [Tooltip("Destination = SpecificScene 時要去的場景名稱")]
    public string sceneName;

    [Tooltip("一開始關著，等其他腳本呼叫 Unlock() 才開")]
    public bool startLocked = false;

    [Header("互動")]
    public Key interactKey = Key.E;
    [Tooltip("勾選 = 改用統一的互動提示（[F] 進入傳送門，跟第一關其他互動一樣），上面的 Interact Key 不用")]
    public bool useSharedInteractPrompt = false;
    [Tooltip("離門多近可以互動 (m)")]
    public float interactDistance = 3.5f;

    [Header("外觀（可留空）")]
    [Tooltip("中間會旋轉、開門後才顯示的漩渦")]
    public Renderer surface;
    public Light glowLight;
    public Color openColor = new Color(0.45f, 0.6f, 1f);
    public Color lockedColor = new Color(0.6f, 0.15f, 0.1f);
    public float spinSpeed = 90f;

    public bool IsOpen { get; private set; }

    Transform player;
    PlayerHealth playerHealth;
    MaterialPropertyBlock mpb;
    float openedTime = float.NegativeInfinity;
    float lightBaseIntensity = 1f;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        mpb = new MaterialPropertyBlock();
        if (glowLight != null) lightBaseIntensity = glowLight.intensity;

        if (!startLocked) Unlock(false);
        ApplyLook();
    }

    /// <summary>開啟傳送門；announce = 播音效與提示</summary>
    public void Unlock(bool announce = true)
    {
        if (IsOpen) return;
        IsOpen = true;
        ApplyLook();
        if (!announce) return;
        openedTime = Time.time;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 1f, 0.6f, 0f, 1f, 80f);
    }

    void ApplyLook()
    {
        if (surface != null)
        {
            surface.enabled = IsOpen;
            mpb.Clear();
            mpb.SetColor("_BaseColor", openColor);
            mpb.SetColor("_EmissionColor", openColor * 2f);
            surface.SetPropertyBlock(mpb);
        }
        if (glowLight != null) glowLight.color = IsOpen ? openColor : lockedColor;
    }

    bool PlayerInRange()
    {
        if (player == null || (playerHealth != null && playerHealth.IsDead)) return false;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude <= interactDistance;
    }

    bool Busy => GameManager.Instance != null && GameManager.Instance.IsLoading;

    void Update()
    {
        // 漩渦旋轉 + 燈光呼吸
        if (surface != null && IsOpen) surface.transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
        if (glowLight != null)
            glowLight.intensity = lightBaseIntensity * (IsOpen ? 1f + 0.25f * Mathf.Sin(Time.time * 3f) : 0.35f);

        if (Busy || !PlayerInRange()) return;
        if (useSharedInteractPrompt)
        {
            InteractPrompt.Offer(this, Vector3.Distance(player.position, transform.position),
                                 IsOpen ? $"進入傳送門 → {TargetTitle()}" : "傳送門尚未啟動", IsOpen);
            if (InteractPrompt.Pressed(this)) Enter();
            return;
        }
        if (!IsOpen) return;
        if (Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame) Enter();
    }

    void Enter()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 1f, 1.4f, 0f, 1f, 50f);
        if (playerHealth != null) playerHealth.GetComponent<ThirdPersonController>().inputEnabled = false;
        if (destination == Destination.SpecificScene && !string.IsNullOrEmpty(sceneName)) gm.LoadLevel(sceneName);
        else gm.LoadNextLevel();
    }

    string TargetTitle()
    {
        var gm = GameManager.Instance;
        if (gm == null) return "";
        int i = destination == Destination.SpecificScene ? gm.IndexOf(sceneName) : gm.CurrentLevelIndex + 1;
        if (destination == Destination.NextLevel && i >= gm.levels.Length) return "結束冒險";
        return i >= 0 && i < gm.levels.Length ? gm.levels[i].title : sceneName;
    }

    void OnGUI()
    {
        if (useSharedInteractPrompt)
        {
            // 統一格式：開啟訊息用共用橫幅，互動提示由 InteractPrompt 畫
            float since = Time.time - openedTime;
            if (since < 3f && !GameUI.OverlayOpen) GameUI.Banner("傳送門已開啟", openColor, Mathf.Clamp01((3f - since) / 0.5f));
            return;
        }
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

        if (Time.time - openedTime < 3f)
        {
            GUI.color = openColor;
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40f), "傳送門已開啟", style);
        }

        if (Busy || !PlayerInRange()) return;
        GUI.color = IsOpen ? Color.white : new Color(1f, 0.6f, 0.5f);
        string text = IsOpen ? $"按 {interactKey} 進入傳送門 → {TargetTitle()}" : "傳送門尚未啟動";
        GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40f), text, style);
        GUI.color = Color.white;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.45f, 0.6f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactDistance);
        Gizmos.DrawRay(transform.position + Vector3.up, transform.forward * 2f);
    }
}
