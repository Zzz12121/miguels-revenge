using UnityEngine;

/// <summary>
/// 敵艦上的弓箭手（Mixamo Erika Archer）的動畫：平常播待機，射擊時播「拉弓放箭」。
/// EnemyBoat 射擊時呼叫 PlayDraw()，等回傳的秒數（拉滿弓、放手的那一刻）才真的射出箭。
/// Prefab（Resources/Prefabs/BoatArcher）由選單 GeminiGrowUp > 安裝敵艦弓箭手 建立。
/// </summary>
public class BoatArcherVisual : MonoBehaviour
{
    public Animator animator;
    [Tooltip("拉弓動畫裡放手的時間（秒，以原本速度播放時）")]
    public float releaseTime = 1.0f;
    [Tooltip("拉弓動畫的播放速度")]
    public float drawAnimSpeed = 1.2f;
    [Tooltip("箭從哪裡射出（相對弓箭手腳底）")]
    public Vector3 arrowOrigin = new Vector3(0.15f, 1.45f, 0.55f);

    bool drawing;
    float drawEndTime;

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    void Start()
    {
        if (!HasAnimator) return;
        animator.Play("Idle", 0, Random.value); // 每個人的待機動作錯開
    }

    /// <summary>開始拉弓；回傳幾秒後放箭</summary>
    public float PlayDraw()
    {
        if (!HasAnimator) return 0f;
        animator.speed = drawAnimSpeed;
        animator.CrossFadeInFixedTime("Draw", 0.1f, 0, 0f);
        drawing = true;
        float release = releaseTime / Mathf.Max(drawAnimSpeed, 0.01f);
        drawEndTime = Time.time + release + 0.45f;
        return release;
    }

    /// <summary>箭的發射位置（世界座標）</summary>
    public Vector3 ArrowWorldOrigin => transform.TransformPoint(arrowOrigin);

    void Update()
    {
        if (!drawing || Time.time < drawEndTime) return;
        drawing = false;
        animator.speed = 1f;
        animator.CrossFadeInFixedTime("Idle", 0.25f, 0, 0f);
    }
}
