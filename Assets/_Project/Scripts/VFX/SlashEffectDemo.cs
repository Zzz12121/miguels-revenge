using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
public class SlashEffectDemo : MonoBehaviour
{
    [Header("References")]
    public Transform trailPoint;
    public TrailRenderer trailRenderer;
    public ParticleSystem sparks;
    public ParticleSystem smoke;

    [Header("Slash Shape")]
    public Vector3 localCenter = new Vector3(0.3f, 1.0f, 0.5f);
    public Vector3 rotationOffset = new Vector3(0f, -20f, 0f);
    public float radius = 1.0f;
    public float verticalScale = 0.55f;
    public float duration = 0.12f;

    [Header("Trigger")]
    public KeyCode triggerKey = KeyCode.Space;

    private bool isPlaying = false;

    void Update()
    {
        if (Keyboard.current != null &&
            Keyboard.current.spaceKey.wasPressedThisFrame &&
            !isPlaying)
        {
            StartCoroutine(PlaySlash());
        }
    }

    IEnumerator PlaySlash()
    {
        isPlaying = true;

        if (trailRenderer != null)
        {
            trailRenderer.Clear();
            trailRenderer.emitting = false;
        }

        // 一開始先噴一點火花
        if (sparks != null)
            sparks.Play();

        yield return null;

        Quaternion rot = transform.rotation * Quaternion.Euler(rotationOffset);
        Vector3 center = transform.TransformPoint(localCenter);

        float startAngle = -70f;
        float endAngle = 70f;

        if (trailRenderer != null)
            trailRenderer.emitting = true;

        float t = 0f;
        while (t < 1f)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, t) * Mathf.Deg2Rad;

            Vector3 localOffset = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * verticalScale,
                0f
            );

            Vector3 worldPos = center + rot * localOffset;

            if (trailPoint != null)
                trailPoint.position = worldPos;

            t += Time.deltaTime / duration;
            yield return null;
        }

        // 最終位置補一下
        float finalAngle = endAngle * Mathf.Deg2Rad;
        Vector3 finalOffset = new Vector3(
            Mathf.Cos(finalAngle) * radius,
            Mathf.Sin(finalAngle) * radius * verticalScale,
            0f
        );

        if (trailPoint != null)
            trailPoint.position = center + rot * finalOffset;

        if (trailRenderer != null)
            trailRenderer.emitting = false;

        // 結尾噴煙
        if (smoke != null)
            smoke.Play();

        isPlaying = false;
    }
}