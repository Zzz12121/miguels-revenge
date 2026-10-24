using System;
using UnityEngine;

/// <summary>One Escape cancels targeting OR opens pause, regardless of Update order.</summary>
public static class SkillAimCancellation
{
    static Action cancel;
    static int consumedFrame = -1;
    public static bool IsAiming => cancel != null;
    public static bool EscapeConsumedThisFrame => consumedFrame == Time.frameCount;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { cancel = null; consumedFrame = -1; }
    public static bool Register(Action callback)
    {
        if (cancel != null) return false;
        cancel = callback;
        return true;
    }
    public static void Release(Action callback) { if (cancel == callback) cancel = null; }
    public static void ConsumeEscape() { consumedFrame = Time.frameCount; }
    public static bool CancelForEscape()
    {
        if (consumedFrame == Time.frameCount) return true;
        if (cancel == null) return false;
        consumedFrame = Time.frameCount;
        Cancel();
        return true;
    }
    public static void Cancel()
    {
        Action callback = cancel;
        cancel = null;
        callback?.Invoke();
    }
}
