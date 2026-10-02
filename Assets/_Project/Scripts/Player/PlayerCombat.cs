using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家基本攻擊：滑鼠左鍵揮劍，連按可以打出三段連擊（第三段傷害較高）。
/// - 攻擊方向 = 鏡頭面向的方向（照企劃書「滑鼠瞄準攻擊方向」）
/// - 攻擊時移動變慢、角色會往前小踏一步
/// - 打中任何實作 IDamageable 的東西（Boss、小怪…）都會扣血
/// 揮劍的動作與判定寫在共用的 MeleeCombat 裡，小怪也用同一套。
/// </summary>
public class PlayerCombat : MeleeCombat
{
    [Header("測試")]
    [Tooltip("玩家攻擊力倍率。測試 Boss 二階段 / 死亡時可以調成 10 之類的大數字，正式遊玩記得改回 1")]
    public float damageMultiplier = 1f;

    PlayerHealth health;
    bool attackQueued;

    protected override void Awake()
    {
        base.Awake();
        health = GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (health != null && health.IsDead) return;
        var mouse = Mouse.current;
        if (mouse == null || Cursor.lockState != CursorLockMode.Locked) return;

        if (mouse.leftButton.wasPressedThisFrame)
        {
            if (!BeginAttack()) attackQueued = true; // 攻擊中再按 = 預約下一段
        }
    }

    protected override void OnSwingStart(int index) => attackQueued = false;

    protected override bool WantsNextHit() => attackQueued;

    protected override Vector3 GetAimDirection()
    {
        Transform cam = controller.cameraTransform;
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
