using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家基本攻擊與遠程反擊：
/// - 滑鼠左鍵：近戰揮劍連擊（三段連擊）
/// - 滑鼠右鍵：遠程劍氣/投射物反擊（可反擊後方追擊的敵艦！）
/// - 攻擊方向 = 鏡頭/準心朝向
/// </summary>
public class PlayerCombat : MeleeCombat
{
    [Header("測試與倍率")]
    [Tooltip("玩家攻擊力倍率")]
    public float damageMultiplier = 1f;

    [Header("遠程反擊設定")]
    [Tooltip("是否開啟滑鼠右鍵遠程攻擊")]
    public bool enableRangedAttack = true;
    [Tooltip("遠程投射物傷害")]
    public float rangedDamage = 35f;
    [Tooltip("遠程冷卻時間 (秒)")]
    public float rangedCooldown = 0.5f;

    PlayerHealth health;
    bool attackQueued;
    float nextRangedTime;

    protected override void Awake()
    {
        base.Awake();
        health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        bool leftClick = false;
        bool rightClick = false;

        var mouse = Mouse.current;
        if (mouse != null)
        {
            leftClick = mouse.leftButton.wasPressedThisFrame;
            rightClick = mouse.rightButton.wasPressedThisFrame;
        }
#if ENABLE_LEGACY_INPUT_MANAGER
        else
        {
            leftClick = Input.GetMouseButtonDown(0);
            rightClick = Input.GetMouseButtonDown(1);
        }
#endif

        // 左鍵：近戰連擊
        if (leftClick)
        {
            if (!BeginAttack()) attackQueued = true;
        }

        // 右鍵：遠程反擊投射物
        if (rightClick && enableRangedAttack && Time.time >= nextRangedTime)
        {
            FireRangedAttack();
        }
    }

    void FireRangedAttack()
    {
        nextRangedTime = Time.time + rangedCooldown;

        Vector3 aimDir = GetAimDirection();
        Vector3 spawnPos = transform.position + Vector3.up * 1.3f + aimDir * 0.8f;

        var projObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        projObj.name = "PlayerRangedSlash";
        projObj.transform.position = spawnPos;
        projObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        var col = projObj.GetComponent<Collider>();
        if (col != null) col.isTrigger = true;

        var mr = projObj.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            mat.color = new Color(0.2f, 0.9f, 1f); // 亮青藍色劍氣
            mr.sharedMaterial = mat;
        }

        var proj = projObj.AddComponent<PlayerProjectile>();
        proj.damage = rangedDamage * damageMultiplier;
        proj.direction = aimDir.normalized;

        // 音效
        GameAudio.Play("SFX/RPG/knifeSlice2", spawnPos, 0.85f, 1.35f, 0.08f, 1f, 35f);
    }

    protected override void OnSwingStart(int index) => attackQueued = false;

    protected override bool WantsNextHit() => attackQueued;

    protected override Vector3 GetAimDirection()
    {
        Transform cam = controller != null ? controller.cameraTransform : null;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        return cam != null ? cam.forward : transform.forward;
    }

    protected override bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit)
    {
        var target = c.GetComponentInParent<IDamageable>();
        if (target == null || target.IsDead || alreadyHit.Contains(target)) return false;

        alreadyHit.Add(target);
        target.TakeDamage(damage * damageMultiplier, hitPoint);
        ThirdPersonCamera.ShakeMain(0.08f, 0.1f);
        return true;
    }
}
