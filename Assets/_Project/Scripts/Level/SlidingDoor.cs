using UnityEngine;

/// <summary>
/// 拉門：一開始關著、擋路，呼叫 Open() 後門板滑開，碰撞也跟著關掉。打開之後就不會再關上。
/// </summary>
public class SlidingDoor : MonoBehaviour
{
    [Tooltip("會滑動的門板")]
    public Transform[] panels;
    [Tooltip("每片門板打開時移動多少（門板自己的父物件座標）")]
    public Vector3[] openOffsets;
    [Tooltip("門關著時擋路的碰撞體")]
    public Collider blocker;
    public float openTime = 0.7f;

    public bool IsOpen { get; private set; }

    Vector3[] closedPositions;
    float progress;

    void Awake()
    {
        closedPositions = new Vector3[panels.Length];
        for (int i = 0; i < panels.Length; i++) closedPositions[i] = panels[i].localPosition;
    }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (blocker != null) blocker.enabled = false;
        GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.9f, 1.1f, 0f, 1f, 30f);
    }

    void Update()
    {
        if (!IsOpen || progress >= 1f) return;
        progress = Mathf.Clamp01(progress + Time.deltaTime / Mathf.Max(0.01f, openTime));
        float k = Mathf.SmoothStep(0f, 1f, progress);
        for (int i = 0; i < panels.Length; i++)
            panels[i].localPosition = closedPositions[i] + openOffsets[i] * k;
    }
}
