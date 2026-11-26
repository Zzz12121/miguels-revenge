using Unity.Cinemachine;
using UnityEngine;

/// <summary>Adds impact recoil after the camera's framing and collision stages.</summary>
public sealed class CombatCameraImpulse : CinemachineExtension
{
    Vector3 direction;
    float strength, duration, startedAt;
    public float Intensity { get; set; } = 1f;

    public void Play(Vector3 worldDirection, float amplitude, float seconds)
    {
        float remaining = strength * Mathf.Clamp01(1f - (Time.unscaledTime - startedAt) / Mathf.Max(.01f, duration));
        if (amplitude < remaining) return;
        direction = worldDirection.sqrMagnitude > .001f ? worldDirection.normalized : Vector3.back;
        strength = Mathf.Clamp(amplitude, 0f, .35f);
        duration = Mathf.Clamp(seconds, .04f, .3f);
        startedAt = Time.unscaledTime;
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase camera,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Finalize || Time.timeScale <= 0f || GameUI.OverlayOpen) return;
        float age = (Time.unscaledTime - startedAt) / Mathf.Max(.01f, duration);
        if (age < 0f || age >= 1f || Intensity <= 0f) return;
        float envelope = Mathf.Sin(Mathf.Min(1f, age * 8f) * Mathf.PI * .5f) * (1f - age) * (1f - age);
        Vector3 local = Quaternion.Inverse(state.GetFinalOrientation()) * direction;
        float impulse = strength * Mathf.Clamp01(Intensity) * envelope;
        // Rotation leaves the collision-resolved camera position and movement basis untouched.
        state.OrientationCorrection *= Quaternion.Euler(-local.z * impulse * 3f,
            local.x * impulse * 2f, -local.x * impulse * 1.5f);
    }
}
