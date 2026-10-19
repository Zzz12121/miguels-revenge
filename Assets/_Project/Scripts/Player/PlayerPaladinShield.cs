using UnityEngine;

/// <summary>
/// 把原本聖騎士（Mixamo Paladin WProp）的盾牌拿給貓戰士用：舉盾（盾擊、按住右鍵格擋）時出現在左手。
///
/// 聖騎士的盾牌不是獨立的模型，是聖騎士網格的一部分、100% 綁在 Shield_joint 骨頭上，
/// 而且模型沒開 Read/Write 不能直接讀頂點。所以這裡的做法是：
///   1. 生成一隻隱藏的聖騎士，播一格 BlockIdle（舉盾格擋）動畫，記下那時盾牌相對身體的方向、相對左手的位置
///   2. 把聖騎士網格的骨頭換掉：Shield_joint 換成跟著貓戰士左手的掛點，其他骨頭全部換成縮成 0 的點（看不到）
/// 這樣畫出來的就是原本聖騎士的盾牌（同樣的模型和材質），不用另外匯出模型。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPaladinShield : MonoBehaviour
{
    [Tooltip("聖騎士模型（ThirdParty/Lite Sword and Shield Pack/Paladin WProp J Nordstrom.fbx）")]
    [SerializeField] GameObject paladinModel;
    [Tooltip("聖騎士的 Animator（Models/Paladin/Paladin_Player.controller，用來擺出舉盾姿勢）")]
    [SerializeField] RuntimeAnimatorController paladinController;
    [Tooltip("聖騎士的材質（Models/Paladin/M_Paladin.mat），留空 = 用模型自己的材質")]
    [SerializeField] Material paladinMaterial;
    [Tooltip("聖騎士模型原本的縮放（以前 PlayerRig 上是 1.13）")]
    [SerializeField] float paladinScale = 1.1304362f;
    [Tooltip("記錄盾牌姿勢用的動畫狀態 / 播到哪裡")]
    [SerializeField] string referenceState = "BlockIdle";
    [SerializeField, Range(0f, 1f)] float referenceTime = 0.5f;

    PlayerCombat combat;
    PlayerHealth health;
    Animator catAnimator;
    Transform catLeftHand;

    GameObject paladin;          // 隱藏的聖騎士（只剩盾牌看得到）
    Transform mount;             // 代替 Shield_joint，跟著貓戰士左手
    Quaternion shieldRotation;   // 盾牌相對角色身體的方向（舉盾姿勢）
    Vector3 shieldOffset;        // 盾牌相對左手的位置（角色身體座標）
    bool ready, shown;

    /// <summary>盾牌模型有沒有準備好（沒有指定模型時是 false）</summary>
    public bool Ready => ready;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        health = GetComponent<PlayerHealth>();
        catAnimator = GetComponentInChildren<Animator>(true);
    }

    void Start() => Build();

    void OnDestroy()
    {
        if (paladin != null) Destroy(paladin);
        if (mount != null) Destroy(mount.gameObject);
    }

    void Build()
    {
        if (paladinModel == null) return;

        paladin = Instantiate(paladinModel);
        paladin.name = "PaladinShield_Visual";
        paladin.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        paladin.transform.localScale = Vector3.one * paladinScale;

        Transform shieldJoint = null, leftHand = null;
        foreach (var t in paladin.GetComponentsInChildren<Transform>(true))
        {
            if (shieldJoint == null && t.name.EndsWith("Shield_joint")) shieldJoint = t;
            if (leftHand == null && t.name.EndsWith("LeftHand")) leftHand = t;
        }
        if (shieldJoint == null)
        {
            Debug.LogWarning("[PlayerPaladinShield] 聖騎士模型裡找不到 Shield_joint，不顯示盾牌", this);
            Destroy(paladin);
            return;
        }

        // 1. 擺出舉盾姿勢，記下盾牌相對身體 / 左手的位置和方向
        var anim = paladin.GetComponent<Animator>();
        if (anim != null && paladinController != null)
        {
            anim.runtimeAnimatorController = paladinController;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Play(referenceState, 0, referenceTime);
            anim.Update(0f);
        }
        if (anim != null && anim.isHuman && anim.GetBoneTransform(HumanBodyBones.LeftHand) != null)
            leftHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform root = paladin.transform;
        shieldRotation = Quaternion.Inverse(root.rotation) * shieldJoint.rotation;
        Vector3 handPos = leftHand != null ? leftHand.position : shieldJoint.position;
        shieldOffset = Quaternion.Inverse(root.rotation) * (shieldJoint.position - handPos);
        Vector3 shieldScale = shieldJoint.lossyScale;
        if (anim != null) anim.enabled = false;

        // 2. 換骨頭：Shield_joint → 掛點，其他 → 縮成 0 的點
        mount = new GameObject("PaladinShield_Mount").transform;
        mount.localScale = shieldScale;
        var collapsed = new GameObject("PaladinShield_Hidden").transform;
        collapsed.SetParent(mount, false);
        collapsed.localScale = Vector3.zero;

        bool any = false;
        foreach (var smr in paladin.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var bones = smr.bones;
            bool hasShield = false;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == shieldJoint) { bones[i] = mount; hasShield = true; }
                else bones[i] = collapsed;
            }
            if (!hasShield) { smr.enabled = false; continue; }
            smr.bones = bones;
            smr.rootBone = mount;
            if (paladinMaterial != null)
            {
                var mats = smr.sharedMaterials;
                for (int m = 0; m < mats.Length; m++) mats[m] = paladinMaterial;
                smr.sharedMaterials = mats;
            }
            smr.updateWhenOffscreen = true;
            smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            any = true;
        }
        // 聖騎士自己的骨架用不到了：關掉，免得每幀計算
        foreach (Transform child in paladin.transform)
            if (child.GetComponent<SkinnedMeshRenderer>() == null) child.gameObject.SetActive(false);

        if (!any)
        {
            Debug.LogWarning("[PlayerPaladinShield] 聖騎士網格沒有綁在 Shield_joint 上的部分，不顯示盾牌", this);
            Destroy(paladin);
            Destroy(mount.gameObject);
            return;
        }
        ready = true;
        SetShown(false);
    }

    void SetShown(bool show)
    {
        shown = show;
        if (paladin != null) paladin.SetActive(show);
    }

    // 動畫算完、PlayerVisualAnimator 調完手臂之後才擺盾牌
    void LateUpdate()
    {
        if (!ready) return;
        bool want = combat != null && combat.ShieldUp && !combat.bowMode && (health == null || !health.IsDead);
        if (want != shown) SetShown(want);
        if (!want) return;

        if (catLeftHand == null && catAnimator != null && catAnimator.isHuman)
            catLeftHand = catAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform body = transform;
        Vector3 hand = catLeftHand != null ? catLeftHand.position : body.position + body.up * 1.1f + body.forward * 0.35f;
        mount.SetPositionAndRotation(hand + body.rotation * shieldOffset, body.rotation * shieldRotation);
    }
}
