using System.Collections;
using UnityEngine;

/// <summary>
/// 地城房間之間的門：打開 = 沉進地板裡，關上 = 升起來擋住通道。
/// 平常是開著的；玩家走進還沒清過的房間時，那間房間的門全部關上，小怪殺光後再打開。
/// 由 DungeonGenerator 自動產生，一道門由相鄰的兩間房間共用。
/// </summary>
public class DungeonDoor : MonoBehaviour
{
    [Tooltip("門板的高度 (m)，打開時會往下沉這麼多再多一點")]
    public float height = 4f;
    public float openTime = 0.8f;
    public float closeTime = 0.35f;

    public bool IsClosed { get; private set; }

    Vector3 closedLocal;
    bool initialized;
    Coroutine routine;

    void Awake() => Init();

    void Init()
    {
        if (initialized) return;
        initialized = true;
        closedLocal = transform.localPosition; // 產生時放在「關上」的位置
    }

    Vector3 OpenLocal => closedLocal + Vector3.down * (height + 0.3f);

    /// <summary>立刻變成開著 / 關著，不播動畫也沒有聲音（產生地城、重置時用）</summary>
    public void SetImmediate(bool closed)
    {
        Init();
        if (routine != null) StopCoroutine(routine);
        routine = null;
        IsClosed = closed;
        transform.localPosition = closed ? closedLocal : OpenLocal;
    }

    public void Close()
    {
        Init();
        if (IsClosed) return;
        IsClosed = true;
        Move(closedLocal, closeTime);
        GameAudio.Play("SFX/RPG/doorClose_4", transform.position, 0.9f, 0.6f, 0.05f, 1f, 45f);
    }

    public void Open()
    {
        Init();
        if (!IsClosed) return;
        IsClosed = false;
        Move(OpenLocal, openTime);
        GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.8f, 0.6f, 0.05f, 1f, 45f);
    }

    void Move(Vector3 target, float duration)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(MoveRoutine(target, duration));
    }

    IEnumerator MoveRoutine(Vector3 target, float duration)
    {
        Vector3 from = transform.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.localPosition = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        transform.localPosition = target;
        routine = null;
    }
}
