using UnityEngine;

/// <summary>
/// 純視覺武器掛點切換器。預設收在腰後，攻擊或技能期間才掛到右手。
/// 不處理傷害、命中或玩家輸入，可安全重用在正式 PlayerRig 與獨立 VFX 測試場景。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerWeaponPresenter : MonoBehaviour
{
    [SerializeField] Animator animator;
    [SerializeField] Transform weaponPivot;
    [SerializeField] Transform handSocket;
    [SerializeField] Transform waistSocket;
    [SerializeField] Vector3 handLocalPosition = Vector3.zero;
    [SerializeField] Vector3 handLocalEuler = new Vector3(60f, 25f, 0f);

    [Header("腰後收刀姿勢（以角色朝向為準，不使用骨盆的歪斜座標軸）")]
    [Tooltip("X=角色右方，Y=上方，Z=角色前方；負 Z 代表背後。單位是公尺。")]
    [SerializeField] Vector3 holsterOffset = new Vector3(0.30f, -0.22f, -0.32f);
    [Tooltip("刀尖相對角色指向哪裡。預設向角色左側，讓刀橫掛在腰後。")]
    [SerializeField] Vector3 holsterBladeDirection = Vector3.left;
    [Tooltip("沿刀身軸旋轉；若刀刃正反面不對，只需要調這個值。")]
    [SerializeField, Range(-180f, 180f)] float holsterRollDegrees;

    Transform hips;
    Transform weaponTip;

    float drawUntilUnscaledTime;
    int forcedDrawCount;
    bool drawn;

    public bool IsDrawn => drawn;

    void Awake()
    {
        EnsureReferences();
        SetDrawnImmediate(false);
    }

    void Update()
    {
        if (forcedDrawCount == 0 && drawn && Time.unscaledTime >= drawUntilUnscaledTime)
            SetDrawnImmediate(false);
    }

    void LateUpdate()
    {
        // Humanoid 的 Hips 每幀都會被動畫改變。收刀時在動畫之後重新套用世界姿勢，
        // 才不會因模型骨盆本身的軸向旋轉而跑到胸前或穿過身體。
        if (!drawn)
        {
            // Animator 在 Awake 時不一定已完成 Humanoid 初始化，因此在第一次 LateUpdate
            // 再補抓一次骨盆與刀尖；成功後後續只會做輕量的姿勢更新。
            if (hips == null || weaponTip == null) EnsureReferences();
            else ApplyWaistSocketPose();
        }
    }

    public void Configure(Animator newAnimator, Transform newWeaponPivot, Transform newHandSocket, Transform newWaistSocket)
    {
        animator = newAnimator;
        weaponPivot = newWeaponPivot;
        handSocket = newHandSocket;
        waistSocket = newWaistSocket;
        ApplyWaistSocketPose();
    }

    public void DrawForSeconds(float seconds)
    {
        drawUntilUnscaledTime = Mathf.Max(drawUntilUnscaledTime, Time.unscaledTime + Mathf.Max(0.1f, seconds));
        SetDrawnImmediate(true);
    }

    public void BeginSkillDraw()
    {
        forcedDrawCount++;
        SetDrawnImmediate(true);
    }

    public void EndSkillDraw(bool sheatheImmediately = true)
    {
        forcedDrawCount = Mathf.Max(0, forcedDrawCount - 1);
        if (forcedDrawCount == 0 && sheatheImmediately) SetDrawnImmediate(false);
    }

    public void SheatheNow()
    {
        forcedDrawCount = 0;
        drawUntilUnscaledTime = 0f;
        SetDrawnImmediate(false);
    }

    public void SetDrawnImmediate(bool shouldDraw)
    {
        EnsureReferences();
        Transform destination = shouldDraw ? handSocket : waistSocket;
        if (weaponPivot == null || destination == null) return;

        weaponPivot.SetParent(destination, false);
        weaponPivot.localPosition = shouldDraw ? handLocalPosition : Vector3.zero;
        weaponPivot.localRotation = shouldDraw ? Quaternion.Euler(handLocalEuler) : Quaternion.identity;
        weaponPivot.localScale = Vector3.one;
        drawn = shouldDraw;
    }

    void EnsureReferences()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            if (handSocket == null) handSocket = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null) hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (waistSocket == null)
            {
                if (hips != null)
                {
                    Transform existing = hips.Find("WaistWeaponSocket");
                    if (existing == null)
                    {
                        existing = new GameObject("WaistWeaponSocket").transform;
                        existing.SetParent(hips, false);
                    }
                    waistSocket = existing;
                }
            }
        }

        if (weaponPivot == null) weaponPivot = FindDeepChild(transform, "WeaponPivot");
        if (weaponTip == null && weaponPivot != null) weaponTip = FindDeepChild(weaponPivot, "WeaponTip");
        ApplyWaistSocketPose();
    }

    void ApplyWaistSocketPose()
    {
        if (waistSocket == null || hips == null || animator == null) return;

        Transform facing = animator.transform;
        waistSocket.position = hips.position
            + facing.right * holsterOffset.x
            + facing.up * holsterOffset.y
            + facing.forward * holsterOffset.z;

        Vector3 sourceDirection = Vector3.forward;
        if (weaponPivot != null && weaponTip != null)
        {
            Vector3 localTip = weaponPivot.InverseTransformPoint(weaponTip.position);
            if (localTip.sqrMagnitude > 0.0001f) sourceDirection = localTip.normalized;
        }

        Vector3 desiredDirection = holsterBladeDirection.sqrMagnitude > 0.0001f
            ? holsterBladeDirection.normalized
            : Vector3.left;
        Quaternion alignBlade = Quaternion.FromToRotation(sourceDirection, desiredDirection);
        Quaternion roll = Quaternion.AngleAxis(holsterRollDegrees, desiredDirection);
        waistSocket.rotation = facing.rotation * roll * alignBlade;
        waistSocket.localScale = Vector3.one;
    }

    static Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName) return child;
            Transform found = FindDeepChild(child, childName);
            if (found != null) return found;
        }
        return null;
    }
}
