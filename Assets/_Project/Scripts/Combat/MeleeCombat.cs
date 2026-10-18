using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 近戰連擊的共用邏輯。玩家（PlayerCombat）和小怪（EnemyCombat）都繼承這個類別，
/// 所以兩邊的揮劍動作、連擊節奏、往前踏步、判定方式完全一樣。
///
/// 子類別只需要決定三件事：
///   - GetAimDirection：每一刀要朝哪裡砍（玩家 = 鏡頭方向，小怪 = 玩家方向）
///   - WantsNextHit   ：要不要接下一段（玩家 = 有沒有再按左鍵，小怪 = 這輪還剩幾下）
///   - TryDamage      ：打到什麼東西才算數（玩家打敵人，小怪只打玩家）
/// 沒有指定武器模型時，會自動用一個長方體當劍。
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public abstract class MeleeCombat : MonoBehaviour
{
    [Header("連擊")]
    [Tooltip("每一段的傷害，數量 = 連擊段數")]
    public float[] comboDamage = { 12f, 12f, 25f };
    [Tooltip("抬手時間 (秒)")]
    public float windupTime = 0.1f;
    [Tooltip("揮劍、有傷害判定的時間 (秒)")]
    public float swingTime = 0.14f;
    [Tooltip("揮完之後的硬直 (秒)；這段時間內可以接下一段")]
    public float recoveryTime = 0.3f;
    [Tooltip("最後一段的硬直 (秒)")]
    public float finisherRecoveryTime = 0.5f;

    /// <summary>
    /// 每一段各自的抬手 / 揮劍 / 硬直時間（有填就優先用，沒填就用上面共用的數值）。
    /// 玩家的三段動畫長短不一樣，PlayerVisual 會依照每個動畫「砍到」的時間填進來，
    /// 傷害判定才會對上畫面上劍砍到的那一刻
    /// </summary>
    [System.NonSerialized] public float[] stepWindup, stepSwing, stepRecovery;
    /// <summary>硬直過了多少比例之後，有預約下一段就可以提早接（玩家設成接近 1，讓動畫播完整）</summary>
    [System.NonSerialized] public float chainAfter = 0.4f;

    float StepValue(float[] arr, int index, float fallback) =>
        arr != null && index >= 0 && index < arr.Length && arr[index] > 0f ? arr[index] : fallback;
    /// <summary>第 index 段的抬手時間</summary>
    public float WindupFor(int index) => StepValue(stepWindup, index, windupTime);
    /// <summary>第 index 段有傷害判定的時間</summary>
    public float SwingFor(int index) => StepValue(stepSwing, index, swingTime);

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
    [Tooltip("沒有指定武器時，自動產生的劍的顏色")]
    public Color weaponColor = new Color(0.8f, 0.85f, 0.95f);

    public bool IsAttacking { get; private set; }

    protected ThirdPersonController controller;
    readonly HashSet<object> hitThisSwing = new HashSet<object>();
    readonly Collider[] overlap = new Collider[32];
    Coroutine chain;

    // 武器的姿勢（相對角色）：x = 俯仰, y = 左右
    protected static readonly Vector3 RestPose = new Vector3(60f, 25f, 0f);

    protected virtual void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        if (weaponPivot == null) weaponPivot = CreateDefaultWeapon(transform, weaponColor);
        weaponPivot.localRotation = Quaternion.Euler(RestPose);
    }

    protected virtual void OnDisable()
    {
        CancelAttack();
    }

    // ---------------- 子類別要實作的部分 ----------------

    /// <summary>每一刀開始時要面向的方向</summary>
    protected abstract Vector3 GetAimDirection();
    /// <summary>這一刀之後要不要接下一段</summary>
    protected abstract bool WantsNextHit();
    /// <summary>判定球碰到 c 時要不要造成傷害；有打到就回傳 true。alreadyHit 用來避免一刀打同一個目標兩次</summary>
    protected abstract bool TryDamage(Collider c, float damage, Vector3 hitPoint, HashSet<object> alreadyHit);

    [Header("音效")]
    [Tooltip("揮劍聲（不填 = Kenney RPG Audio 的 knifeSlice / drawKnife）")]
    public AudioClip[] swingSounds;
    [Tooltip("打中的聲音（不填 = chop）")]
    public AudioClip hitSound;
    [Range(0f, 1f)] public float soundVolume = 0.7f;
    [Tooltip("音高（小怪預設比較低沉）")]
    public float soundPitch = 1f;
    /// <summary>子類別可以改預設音高（小怪 0.85）</summary>
    protected virtual float PitchScale => 1f;
    /// <summary>每一刀開始時呼叫（index = 第幾段，從 0 開始）</summary>
    protected virtual void OnSwingStart(int index) { }
    /// <summary>每一刀開始時通知外面（模型動畫用）</summary>
    public event System.Action<int> OnSwing;

    // ---------------- 對外 ----------------

    /// <summary>開始一輪連擊；已經在攻擊中就回傳 false</summary>
    protected bool BeginAttack()
    {
        if (IsAttacking || !isActiveAndEnabled) return false;
        chain = StartCoroutine(AttackChain());
        return true;
    }

    /// <summary>中斷攻擊（被打到硬直、死亡時使用）</summary>
    public void CancelAttack()
    {
        if (chain != null) StopCoroutine(chain);
        chain = null;
        if (weaponPivot != null) weaponPivot.localRotation = Quaternion.Euler(RestPose);
        if (IsAttacking && controller != null)
        {
            controller.speedMultiplier = 1f;
            controller.lockRotation = false;
        }
        IsAttacking = false;
    }

    // ---------------- 連擊流程 ----------------

    IEnumerator AttackChain()
    {
        IsAttacking = true;
        controller.speedMultiplier = attackMoveMultiplier;
        controller.lockRotation = true;

        int index = 0;
        while (true)
        {
            yield return Swing(index);

            bool last = index >= comboDamage.Length - 1;
            float recovery = StepValue(stepRecovery, index, last ? finisherRecoveryTime : recoveryTime);
            float t = 0f;
            while (t < recovery)
            {
                // 硬直過了 chainAfter（預設 40%）之後，如果要接下一段就提早接
                if (!last && WantsNextHit() && t >= recovery * chainAfter) break;
                t += Time.deltaTime;
                yield return null;
            }

            if (!last && WantsNextHit()) { index++; continue; }
            break;
        }

        // 收刀
        yield return RotateWeapon(weaponPivot.localEulerAngles, RestPose, 0.12f);
        controller.speedMultiplier = 1f;
        controller.lockRotation = false;
        IsAttacking = false;
        chain = null;
    }

    IEnumerator Swing(int index)
    {
        controller.FaceDirection(GetAimDirection());
        OnSwingStart(index);
        OnSwing?.Invoke(index);

        // 三種揮法：右→左橫砍、左→右橫砍、由上往下重劈
        Vector3 from, to;
        switch (index % 3)
        {
            case 0: from = new Vector3(15f, 110f, 0f); to = new Vector3(15f, -110f, 0f); break;
            case 1: from = new Vector3(15f, -110f, 0f); to = new Vector3(15f, 110f, 0f); break;
            default: from = new Vector3(-80f, 0f, 0f); to = new Vector3(80f, 0f, 0f); break;
        }

        // 抬手
        yield return RotateWeapon(weaponPivot.localEulerAngles, from, WindupFor(index));

        // 揮下去 + 往前踏步 + 判定
        GameAudio.PlayAt(GameAudio.Pick(swingSounds, "SFX/RPG/knifeSlice", "SFX/RPG/knifeSlice2", "SFX/RPG/drawKnife2"),
                         transform.position + Vector3.up, soundVolume * 0.8f,
                         soundPitch * PitchScale * Random.Range(0.92f, 1.08f) * (index % 3 == 2 ? 0.85f : 1f), 1f, 30f);
        hitThisSwing.Clear();
        controller.AddImpulse(transform.forward * lungeForce);
        float damage = comboDamage[Mathf.Clamp(index, 0, comboDamage.Length - 1)];
        float t = 0f;
        float swing = SwingFor(index);
        while (t < swing)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / swing);
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
            if (TryDamage(c, damage, c.ClosestPoint(center), hitThisSwing))
                GameAudio.PlayAt(GameAudio.Pick(hitSound, "SFX/RPG/chop"), c.ClosestPoint(center), soundVolume,
                                 soundPitch * PitchScale * Random.Range(0.9f, 1.1f), 1f, 30f);
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

    /// <summary>
    /// 用長方體做一把簡單的劍，掛在角色右肩。
    /// material 留空時會在執行中建立一個臨時材質（編輯器工具做 Prefab 時要傳入材質資產）。
    /// </summary>
    public static Transform CreateDefaultWeapon(Transform owner, Color color, Material material = null)
    {
        var pivot = new GameObject("WeaponPivot").transform;
        pivot.SetParent(owner, false);
        pivot.localPosition = new Vector3(0.45f, 0.2f, 0.1f); // 右肩附近

        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        blade.name = "Blade";
        DestroyImmediate(blade.GetComponent<Collider>()); // 劍本身不需要碰撞
        blade.transform.SetParent(pivot, false);
        blade.transform.localPosition = new Vector3(0f, 0f, 0.8f);
        blade.transform.localScale = new Vector3(0.1f, 0.25f, 1.4f);

        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
            {
                material = new Material(shader);
                material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.8f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.7f);
            }
        }
        if (material != null) blade.GetComponent<MeshRenderer>().sharedMaterial = material;
        pivot.localRotation = Quaternion.Euler(RestPose);
        return pivot;
    }

    protected virtual void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + transform.forward * hitForwardOffset, hitRadius);
    }
}
