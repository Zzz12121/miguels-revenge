using UnityEngine;

/// <summary>
/// 短暫的特寫鏡頭：鏡頭平滑轉到目標正前方、慢慢繞一點；可以再推近到某個物件（例如臉上的時鐘）；
/// 時間到或呼叫 Stop 就還給原本的鏡頭（ThirdPersonCamera / Cinemachine），Stop(cut: true) = 直接切回。
/// 掛在 Main Camera 上、在所有鏡頭腳本的 LateUpdate 之後才套用，所以不用關掉原本的鏡頭系統。
/// 用不受暫停影響的時間，劇情對話框暫停遊戲時鏡頭照樣會推近。
/// 用法：BossFocusCamera.Play(boss, 2.4f);  BossFocusCamera.ZoomTo(faceClock, 1.3f, 0.8f);  BossFocusCamera.Stop(true);
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(31990)]
public sealed class BossFocusCamera : MonoBehaviour
{
    Transform target, focus;
    float start = -1f, duration, distance, height, lookHeight, orbit;
    float zoomStart, zoomTime, zoomDistance;
    float stopAt = -1f;
    const float BlendIn = 0.45f, BlendOut = 0.6f, OrbitTime = 5f;

    public static bool Active { get; private set; }

    static float Now => Time.unscaledTime;

    /// <summary>distance / height = 鏡頭在目標前方多遠、多高；lookHeight = 看向目標的哪個高度；orbit = 繞多少度。
    /// duration 給 float.PositiveInfinity = 一直拍到呼叫 Stop</summary>
    public static void Play(Transform target, float duration, float distance = 7f, float height = 2f, float lookHeight = 1.2f, float orbit = 25f)
    {
        var cam = Camera.main;
        if (cam == null || target == null) return;
        var f = cam.GetComponent<BossFocusCamera>();
        if (f == null) f = cam.gameObject.AddComponent<BossFocusCamera>();
        f.target = target; f.focus = null; f.start = Now; f.stopAt = -1f;
        f.duration = Mathf.Max(BlendIn + BlendOut, duration);
        f.distance = distance; f.height = height; f.lookHeight = lookHeight; f.orbit = orbit;
        Active = true;
    }

    /// <summary>從目前的特寫再推近到 focus 的正前方（focus 的 +Z 方向）distance 公尺</summary>
    public static void ZoomTo(Transform focus, float distance, float time)
    {
        var f = Current;
        if (f == null || f.start < 0f || focus == null) return;
        f.focus = focus; f.zoomStart = Now; f.zoomTime = Mathf.Max(0.01f, time); f.zoomDistance = distance;
    }

    /// <summary>cut = true 直接切回原本的鏡頭；false = 平滑轉回去</summary>
    public static void Stop(bool cut = false)
    {
        var f = Current;
        if (f == null || f.start < 0f) { Active = false; return; }
        if (cut) { f.start = -1f; Active = false; }
        else f.stopAt = Now;
    }

    static BossFocusCamera Current
    {
        get { var cam = Camera.main; return cam != null ? cam.GetComponent<BossFocusCamera>() : null; }
    }

    void OnDisable() => Active = false;

    void LateUpdate()
    {
        if (start < 0f) return;
        float t = Now - start;
        float outW = stopAt >= 0f ? 1f - Mathf.Clamp01((Now - stopAt) / BlendOut) : Mathf.Clamp01((duration - t) / BlendOut);
        if (target == null || outW <= 0f) { start = -1f; Active = false; return; }

        float w = Mathf.Min(Mathf.Clamp01(t / BlendIn), outW);
        w = w * w * (3f - 2f * w);

        Vector3 fwd = Vector3.ProjectOnPlane(target.forward, Vector3.up);
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
        fwd = Quaternion.AngleAxis(Mathf.Lerp(-orbit * 0.5f, orbit * 0.5f, Mathf.Clamp01(t / OrbitTime)), Vector3.up) * fwd.normalized;
        Vector3 pos = target.position + fwd * distance + Vector3.up * height;
        Quaternion rot = Quaternion.LookRotation(target.position + Vector3.up * lookHeight - pos);

        if (focus != null)
        {
            float z = Mathf.Clamp01((Now - zoomStart) / zoomTime);
            z = z * z * (3f - 2f * z);
            Vector3 ff = focus.forward;
            Vector3 closePos = focus.position + ff * zoomDistance + Vector3.up * zoomDistance * 0.08f;
            Quaternion closeRot = Quaternion.LookRotation(focus.position - closePos);
            pos = Vector3.Lerp(pos, closePos, z);
            rot = Quaternion.Slerp(rot, closeRot, z);
        }

        // 原本的鏡頭這一幀已經算好位置了，在它上面混合，還回去時就不會跳
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, pos, w), Quaternion.Slerp(transform.rotation, rot, w));
    }
}
