using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家基本攻擊：滑鼠左鍵揮劍，連按可以打出三段連擊（第三段傷害較高）。
/// - 攻擊方向 = 鏡頭面向的方向（照企劃書「滑鼠瞄準攻擊方向」）
/// - 攻擊時移動變慢、角色會往前小踏一步
/// - 打中任何實作 IDamageable 的東西（例如 BossHealth）都會扣血
/// 沒有指定武器模型時，會自動用一個長方體當劍。
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class PlayerCombat : MonoBehaviour
{
    [Header("連擊")]
    [Tooltip("每一段的傷害，數量 = 連擊段數")]
    public float[] comboDamage = { 12f, 12f, 25f };
    [Tooltip("抬手時間 (秒)")]
    public float windupTime = 0.1f;
    [Tooltip("揮劍、有傷害判定的時間 (秒)")]
    public float swingTime = 0.14f;
    [Tooltip("揮完之後的硬直 (秒)；這段時間內按左鍵會接下一段")]
    public float recoveryTime = 0.3f;
    [Tooltip("最後一段的硬直 (秒)")]
    public float finisherRecoveryTime = 0.5f;

    [Header("判定")]
    [Tooltip("判定球離角色中心往前多遠")]
    public float hitForwardOffset = 1.3f;
    [Tooltip("判定球半徑")]
    public float hitRadius = 1.4f;

    [Header("手感")]
    [Tooltip("攻擊時的移動速度倍率")]
    [Range(0f, 1f)] public float attackMoveMultiplier = 0.15f;
    [Tooltip("每一段往前踏步的力道")]
    public float lungeForce = 5f;

    [Header("武器外觀（可留空）")]
    public Transform weaponPivot;

    public bool IsAttacking { get; private set; }

    ThirdPersonController controller;
    PlayerHealth health;
    bool attackQueued;
    readonly HashSet<IDamageable> hitThisSwing = new HashSet<IDamageable>();
    readonly Collider[] overlap = new Collider[32];

    // 武器的姿勢（相對角色）：x = 俯仰, y = 左右
    static readonly Vector3 RestPose = new Vector3(60f, 25f, 0f);

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        health = GetComponent<PlayerHealth>();
        if (weaponPivot == null) weaponPivot = CreateDefaultWeapon();
        weaponPivot.localRotation = Quaternion.Euler(RestPose);
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        var mouse = Mouse.current;
        if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!IsAttacking) StartCoroutine(AttackChain());
            else attackQueued = true;
        }
    }

    IEnumerator AttackChain()
    {
        IsAttacking = true;
        controller.speedMultiplier = attackMoveMultiplier;
        controller.lockRotation = true;

        int index = 0;
        while (true)
        {
            attackQueued = false;
            yield return Swing(index);

            bool last = index >= comboDamage.Length - 1;
            float recovery = last ? finisherRecoveryTime : recoveryTime;
            float t = 0f;
            while (t < recovery)
            {
                // 硬直一半之後，如果有按下一段就提早接
                if (!last && attackQueued && t >= recovery * 0.4f) break;
                t += Time.deltaTime;
                yield return null;
            }

            if (!last && attackQueued) { index++; continue; }
            break;
        }

        // 收刀
        yield return RotateWeapon(weaponPivot.localEulerAngles, RestPose, 0.12f);
        controller.speedMultiplier = 1f;
        controller.lockRotation = false;
        IsAttacking = false;
    }

    IEnumerator Swing(int index)
    {
        // 面向鏡頭前方
        Transform cam = controller.cameraTransform;
        if (cam != null) controller.FaceDirection(cam.forward);

        // 三種揮法：右→左橫砍、左→右橫砍、由上往下重劈
        Vector3 from, to;
        switch (index % 3)
        {
            case 0: from = new Vector3(15f, 110f, 0f); to = new Vector3(15f, -110f, 0f); break;
            case 1: from = new Vector3(15f, -110f, 0f); to = new Vector3(15f, 110f, 0f); break;
            default: from = new Vector3(-80f, 0f, 0f); to = new Vector3(80f, 0f, 0f); break;
        }

        // 抬手
        yield return RotateWeapon(weaponPivot.localEulerAngles, from, windupTime);

        // 揮下去 + 往前踏步 + 判定
        hitThisSwing.Clear();
        controller.AddImpulse(transform.forward * lungeForce);
        float damage = comboDamage[Mathf.Clamp(index, 0, comboDamage.Length - 1)];
        float t = 0f;
        while (t < swingTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / swingTime);
            // 用一般 Lerp（不是 LerpAngle），揮劍才會經過前方而不是從背後繞
            weaponPivot.localRotation = Quaternion.Euler(Vector3.Lerp(from, to, k));
            DoHitCheck(damage);
            yield return null;
        }
    }

    void DoHitCheck(float damage)
    {
        Vector3 center = transform.position + transform.forward * hitForwardOffset;
        int count = Physics.OverlapSphereNonAlloc(center, hitRadius, overlap, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            var c = overlap[i];
            if (c.transform == transform || c.transform.IsChildOf(transform)) continue;
            var target = c.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || hitThisSwing.Contains(target)) continue;

            hitThisSwing.Add(target);
            Vector3 point = c.ClosestPoint(center);
            target.TakeDamage(damage, point);
            ThirdPersonCamera.ShakeMain(0.08f, 0.1f);
        }
    }

    IEnumerator RotateWeapon(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            weaponPivot.localRotation = Quaternion.Euler(LerpAngles(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        weaponPivot.localRotation = Quaternion.Euler(to);
    }

    static Vector3 LerpAngles(Vector3 a, Vector3 b, float t) =>
        new Vector3(Mathf.LerpAngle(a.x, b.x, t), Mathf.LerpAngle(a.y, b.y, t), Mathf.LerpAngle(a.z, b.z, t));

    /// <summary>沒有武器模型時，用長方體做一把簡單的劍</summary>
    Transform CreateDefaultWeapon()
    {
        var pivot = new GameObject("WeaponPivot").transform;
        pivot.SetParent(transform, false);
        pivot.localPosition = new Vector3(0.45f, 0.2f, 0.1f); // 右肩附近

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Blade";
        DestroyImmediate(blade.GetComponent<Collider>()); // 劍本身不需要碰撞
        blade.transform.SetParent(pivot, false);
        blade.transform.localPosition = new Vector3(0f, 0f, 0.8f);
        blade.transform.localScale = new Vector3(0.1f, 0.25f, 1.4f);

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.8f, 0.85f, 0.95f));
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.8f);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.7f);
            blade.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
        return pivot;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + transform.forward * hitForwardOffset, hitRadius);
    }
}
