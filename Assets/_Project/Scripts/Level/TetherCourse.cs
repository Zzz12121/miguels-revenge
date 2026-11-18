using UnityEngine;

/// <summary>
/// 習武場的拉索深溝：溝裡沒有路，只能用拉索抓著懸空的木條一路盪到對岸。
/// 掉進溝裡（低於溝緣 fallDepth 公尺）就黑幕一下、回到起點；還沒全部碰過的話，已經亮的燈籠也會熄掉，要一口氣重來。
/// 木條頭上的「◆」標記只在玩家進入習武場（yardArea）之後才顯示。
/// </summary>
public class TetherCourse : MonoBehaviour
{
    public TetherTrainingBeam[] beams;
    [Tooltip("深溝的範圍（x, z）")]
    public Rect pitArea;
    [Tooltip("溝緣（地面）的高度")]
    public float rimY;
    [Tooltip("低於溝緣多少公尺算掉下去")]
    public float fallDepth = 2.5f;
    [Tooltip("掉下去之後回到哪裡（起點，溝的西岸）")]
    public Transform startPoint;
    [Tooltip("習武場的範圍（x, z）：在裡面才顯示木條的標記")]
    public Rect yardArea;
    [Tooltip("有設定的話，這個條件成立才顯示標記（例如師父答應之後）")]
    public System.Func<bool> markersAllowed;

    public bool Completed
    {
        get { foreach (var b in beams) if (!b.Touched) return false; return true; }
    }

    /// <summary>掉下去的次數（測試和調整難度用）</summary>
    public int Falls { get; private set; }

    PlayerHealth player;
    CharacterController body;
    ThirdPersonController controller;
    float fadeUntil = float.NegativeInfinity;
    const float FadeTime = 0.7f;

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null)
        {
            body = player.GetComponent<CharacterController>();
            controller = player.GetComponent<ThirdPersonController>();
        }
    }

    void Update()
    {
        if (player == null) return;
        Vector3 p = player.transform.position;
        bool inYard = yardArea.Contains(new Vector2(p.x, p.z)) && (markersAllowed == null || markersAllowed());
        foreach (var b in beams) b.markerVisible = inYard;

        if (!player.IsDead && pitArea.Contains(new Vector2(p.x, p.z)) && p.y < rimY - fallDepth) Restart();
    }

    void Restart()
    {
        Falls++;
        if (!Completed) foreach (var b in beams) b.ResetTouch();
        player.GetComponent<EnergyTetherSkill>()?.CancelForExternalTransition();
        if (body != null) body.enabled = false;
        player.transform.SetPositionAndRotation(startPoint.position, startPoint.rotation);
        if (body != null) body.enabled = true;
        if (controller != null) controller.ResetMotion();
        fadeUntil = Time.unscaledTime + FadeTime;
        GameAudio.Play("SFX/RPG/dropLeather", startPoint.position, 0.8f, 0.7f, 0f, 0f);
    }

    void OnGUI()
    {
        float left = fadeUntil - Time.unscaledTime;
        if (left <= 0f) return;
        GUI.depth = -1300;
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(left / FadeTime));
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
