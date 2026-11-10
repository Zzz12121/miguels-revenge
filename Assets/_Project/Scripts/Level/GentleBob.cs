using UnityEngine;

/// <summary>
/// 讓物件在水面上輕輕起伏、搖晃（例如繫在碼頭邊的小船）。
/// 只做視覺上的微動，不需要 Water 或物理。
/// </summary>
public class GentleBob : MonoBehaviour
{
    [Tooltip("上下起伏的幅度 (公尺)")]
    public float height = 0.07f;
    [Tooltip("左右搖晃的角度 (度)")]
    public float tilt = 1.6f;
    [Tooltip("起伏速度")]
    public float speed = 0.8f;

    Vector3 startPosition;
    Quaternion startRotation;
    float phase;

    void Start()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
        phase = (startPosition.x + startPosition.z) * 0.37f; // 每艘船節奏不同
    }

    void Update()
    {
        float t = Time.time * speed + phase;
        transform.position = startPosition + Vector3.up * (Mathf.Sin(t) * height);
        transform.rotation = startRotation * Quaternion.Euler(Mathf.Sin(t * 0.7f) * tilt * 0.5f, 0f, Mathf.Sin(t * 0.9f + 1f) * tilt);
    }
}
