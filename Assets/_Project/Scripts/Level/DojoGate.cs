using UnityEngine;

/// <summary>
/// 村長家後院通往道場的大門。僧侶答應開門之前門鎖著（Unlock 之後才推得開）。
/// 解鎖後走到門前按 E（或直接往門走過去）把兩扇門往外推開，門洞裡亮起墨色漩渦；再按 E 進入，
/// 換場景時畫面輪播四幅古畫（PaintingSlideshow）。
/// </summary>
public class DojoGate : MonoBehaviour
{
    [Header("目的地")]
    public string sceneName;
    public string destinationTitle;

    [Header("門扇")]
    [Tooltip("兩扇門的門軸（門板掛在底下，繞 Y 軸轉）")]
    public Transform[] doorHinges;
    [Tooltip("每扇門推開時轉幾度")]
    public float[] openAngles;
    public float openDuration = 1.6f;
    [Tooltip("門往哪個方向推（世界座標，從玩家這邊往門外）")]
    public Vector3 pushDirection = Vector3.right;

    [Header("傳送門的光")]
    public Renderer surface;
    public Light glowLight;
    public float lightIntensity = 2.5f;

    [Header("轉場")]
    [Tooltip("換場景時輪播的圖（四幅古畫）")]
    public Texture2D[] slides;

    public float interactDistance = 4.5f;

    public bool IsUnlocked { get; private set; }
    public bool IsOpen { get; private set; }

    PlayerHealth player;
    CharacterController body;
    ThirdPersonController controller;
    float openT = -1f, pushTime;
    bool entering;

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null)
        {
            body = player.GetComponent<CharacterController>();
            controller = player.GetComponent<ThirdPersonController>();
        }
        if (surface != null) surface.enabled = false;
        if (glowLight != null) glowLight.intensity = 0f;
    }

    /// <summary>僧侶開了鎖：門還關著，要玩家自己推開</summary>
    public void Unlock()
    {
        if (IsUnlocked) return;
        IsUnlocked = true;
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 1f, 0.6f, 0f, 1f, 80f);
    }

    public void Push()
    {
        if (!IsUnlocked || openT >= 0f) return;
        openT = 0f;
        GameAudio.Play("SFX/RPG/doorOpen_2", transform.position, 1f, 0.7f, 0f, 1f, 40f);
        GameAudio.Play("SFX/RPG/creak3", transform.position, 0.9f, 0.6f, 0.05f, 1f, 40f);
    }

    void Update()
    {
        // 門慢慢被推開（一開始重、後面順）；開到一半漩渦亮起來
        if (openT >= 0f && openT < 1f)
        {
            openT = Mathf.Min(1f, openT + Time.deltaTime / openDuration);
            float e = openT * openT * (3f - 2f * openT);
            for (int i = 0; i < doorHinges.Length; i++)
                if (doorHinges[i] != null) doorHinges[i].localRotation = Quaternion.Euler(0f, openAngles[i] * e, 0f);
            if (surface != null) surface.enabled = openT > 0.35f;
            if (glowLight != null) glowLight.intensity = lightIntensity * Mathf.Clamp01((openT - 0.3f) / 0.7f);
            if (openT >= 1f) IsOpen = true;
        }
        if (glowLight != null && IsOpen) glowLight.intensity = lightIntensity * (1f + 0.2f * Mathf.Sin(Time.time * 3f));

        if (entering || player == null || player.IsDead) return;
        Vector3 d = player.transform.position - transform.position;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist > interactDistance) { pushTime = 0f; return; }

        if (!IsUnlocked)
        {
            InteractPrompt.Offer(this, dist, "大門深鎖，推不開", false);
            return;
        }
        if (openT < 0f)
        {
            InteractPrompt.Offer(this, dist, "推開大門");
            // 直接走過去頂著門也推得開
            bool leaning = body != null && dist < 2.2f && Vector3.Dot(body.velocity, pushDirection) > 0.8f;
            pushTime = leaning ? pushTime + Time.deltaTime : 0f;
            if (InteractPrompt.Pressed(this) || pushTime > 0.35f) Push();
            return;
        }
        if (!IsOpen) return;
        InteractPrompt.Offer(this, dist, string.IsNullOrEmpty(destinationTitle) ? "進入" : $"進入 → {destinationTitle}");
        if (InteractPrompt.Pressed(this)) Enter();
    }

    void Enter()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        entering = true;
        GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 1f, 1.4f, 0f, 1f, 50f);
        if (controller != null) controller.inputEnabled = false;
        PaintingSlideshow.Play(slides, () => gm.LoadLevel(sceneName), () =>
        {
            // 載入失敗（場景不在 Build Settings 之類）：恢復操作，可以再試一次
            entering = false;
            if (controller != null) controller.inputEnabled = true;
        });
    }

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.9f, 0.85f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, interactDistance);
    }
}
