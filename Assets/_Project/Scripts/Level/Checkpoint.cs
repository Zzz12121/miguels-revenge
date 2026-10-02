using UnityEngine;

/// <summary>
/// 檢查點（類似篝火 / 賜福）：玩家走近就會
///   - 把重生點設在這裡
///   - 補滿血
/// 發光部分會從暗變亮，表示已啟用。
/// </summary>
public class Checkpoint : MonoBehaviour
{
    [Tooltip("重生的位置與面向，留空就用這個物件本身")]
    public Transform respawnPoint;
    [Tooltip("走到多近會觸發 (m)")]
    public float activateRadius = 2.5f;
    public bool healOnTouch = true;

    [Header("外觀（可留空）")]
    public Renderer glow;
    public Color inactiveColor = new Color(0.3f, 0.25f, 0.15f);
    public Color activeColor = new Color(1f, 0.8f, 0.3f);

    Transform player;
    PlayerHealth playerHealth;
    bool playerInside;
    bool activated;
    float messageTime = float.NegativeInfinity;
    string message;
    MaterialPropertyBlock mpb;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        mpb = new MaterialPropertyBlock();
        SetGlow(inactiveColor);
    }

    void Update()
    {
        if (player == null || playerHealth == null) return;

        Vector3 d = player.position - transform.position;
        d.y = 0f;
        bool inside = d.magnitude <= activateRadius && !playerHealth.IsDead;

        if (inside && !playerInside) // 剛走進範圍
        {
            playerHealth.respawnPoint = respawnPoint != null ? respawnPoint : transform;
            if (healOnTouch) playerHealth.HealFull();

            message = activated ? "HP RESTORED" : "CHECKPOINT REACHED";
            GameAudio.Play(activated ? "SFX/RPG/metalClick" : "SFX/RPG/metalLatch", transform.position, 0.9f, 0.9f, 0f, 0.3f);
            messageTime = Time.time;
            if (!activated)
            {
                activated = true;
                SetGlow(activeColor);
            }
        }
        playerInside = inside;
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
        if (Time.time - messageTime > 2f) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 26, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUI.color = activeColor;
        GUI.Label(new Rect(0, Screen.height * 0.35f, Screen.width, 40f), message, style);
        GUI.color = Color.white;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.3f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, activateRadius);
    }
}
