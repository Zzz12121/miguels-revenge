using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二關在船上用的弓：和敵艦弓箭手（Resources/Prefabs/BoatArcher，Mixamo Erika Archer）同一把弓，拿在貓戰士左手。
/// 由 PlayerShieldBowAnimator 自動加上，弓箭模式（PlayerCombat.bowMode）時出現。做法和 PlayerPaladinShield 一樣：
///
/// 弓箭手的弓不是獨立的模型，是 Erika 模型裡的一個 SkinnedMeshRenderer（綁在左手和弓臂骨頭上），
/// 而且模型沒開 Read/Write 不能直接讀頂點。所以：
///   1. 生成一個隱藏的弓箭手，擺待機姿勢，記下弓的每根骨頭相對左手的位置（弓的形狀，弓臂沒有彎）
///   2. 播一格拉弓（Draw）動畫，記下拉弓時左手相對身體的方向（弓直立、朝著前方）
///   3. 弓網格的骨頭全部換成掛點底下的替身，掛點跟著貓戰士左手；弓箭手其他部分全部關掉
/// 注意：這個 Mixamo FBX 的網格名稱是錯開的（叫 Erika_Archer_Bow_Mesh 的其實是眼睛），所以用材質名稱（Bow_MAT）找弓。
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerArcherBow : MonoBehaviour
{
    [Tooltip("弓箭手 Prefab 在 Resources 裡的路徑")]
    public string archerResource = "Prefabs/BoatArcher";
    [Tooltip("弓的材質名稱（用材質找弓，網格名稱不可靠）")]
    public string bowMaterialName = "Bow_MAT";
    [Tooltip("記錄握弓方向用的動畫狀態 / 播到哪裡（拉弓拉滿的時候）")]
    public string referenceState = "Draw";
    [Range(0f, 1f)] public float referenceTime = 0.55f;
    [Tooltip("弓的大小（1 = 和敵人弓箭手一樣）")]
    public float bowScale = 1f;

    PlayerCombat combat;
    PlayerHealth health;
    Animator catAnimator;
    Transform catLeftHand;

    GameObject archer;          // 隱藏的弓箭手（只剩弓看得到）
    Transform mount;            // 代替弓箭手的左手，跟著貓戰士左手
    Quaternion gripRotation;    // 拉弓時左手相對身體的方向
    bool ready, shown;

    /// <summary>弓準備好了沒（找不到弓箭手 Prefab 時是 false，會改用內建的弓）</summary>
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
        if (archer != null) Destroy(archer);
        if (mount != null) Destroy(mount.gameObject);
    }

    void Build()
    {
        var prefab = Resources.Load<GameObject>(archerResource);
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerArcherBow] 找不到 Resources/{archerResource}（選單 GeminiGrowUp > 安裝敵艦弓箭手 會建立），改用內建的弓", this);
            return;
        }

        archer = Instantiate(prefab);
        archer.name = "PlayerBow_Visual";
        archer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        foreach (var mb in archer.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false; // 弓箭手的腳本用不到
        foreach (var col in archer.GetComponentsInChildren<Collider>(true)) Destroy(col);

        var anim = archer.GetComponentInChildren<Animator>(true);
        SkinnedMeshRenderer bow = null;
        foreach (var smr in archer.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            foreach (var m in smr.sharedMaterials)
                if (bow == null && m != null && m.name.StartsWith(bowMaterialName)) bow = smr;
        Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        if (bow == null || hand == null)
        {
            Debug.LogWarning($"[PlayerArcherBow] 弓箭手模型裡找不到材質 {bowMaterialName} 的弓（找到：{bow != null}）或左手骨頭（找到：{hand != null}），改用內建的弓", this);
            Destroy(archer);
            return;
        }

        // 1. 待機姿勢：記下弓的每根骨頭相對左手的位置（弓的形狀，弓臂沒有彎）
        if (anim.runtimeAnimatorController != null)
        {
            anim.enabled = true;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Play("Idle", 0, 0f);
            anim.Update(0f);
        }
        var bones = bow.bones;
        Quaternion handInv = Quaternion.Inverse(hand.rotation);
        Vector3 handScale = hand.lossyScale;
        var localPos = new Vector3[bones.Length];
        var localRot = new Quaternion[bones.Length];
        var localScale = new Vector3[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            localPos[i] = Div(handInv * (bones[i].position - hand.position), handScale);
            localRot[i] = handInv * bones[i].rotation;
            localScale[i] = Div(bones[i].lossyScale, handScale);
        }
        Transform rootBone = bow.rootBone;

        // 2. 拉弓姿勢：記下左手相對身體的方向（弓直立、朝著前方）
        if (anim.runtimeAnimatorController != null)
        {
            anim.Play(referenceState, 0, referenceTime);
            anim.Update(0f);
        }
        gripRotation = Quaternion.Inverse(archer.transform.rotation) * hand.rotation;
        anim.enabled = false;

        // 3. 掛點（代替弓箭手的左手）+ 每根骨頭的替身
        mount = new GameObject("PlayerBow_Mount").transform;
        mount.localScale = handScale * bowScale;
        var proxies = new Dictionary<Transform, Transform>();
        var newBones = new Transform[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) { newBones[i] = mount; continue; }
            if (!proxies.TryGetValue(bones[i], out var proxy))
            {
                proxy = new GameObject(bones[i].name).transform;
                proxy.SetParent(mount, false);
                proxy.localPosition = localPos[i];
                proxy.localRotation = localRot[i];
                proxy.localScale = localScale[i];
                proxies[bones[i]] = proxy;
            }
            newBones[i] = proxy;
        }
        bow.bones = newBones;
        bow.rootBone = rootBone != null && proxies.TryGetValue(rootBone, out var r) ? r : mount;
        bow.enabled = true;               // 弓箭手 Prefab 裡有關掉的網格，確保弓是開的
        bow.updateWhenOffscreen = true;
        bow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // 弓以外的網格（身體、衣服、眼睛、箭）關掉；弓箭手自己的骨架也關掉，免得每幀計算
        foreach (var smr in archer.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr != bow) smr.enabled = false;
        var model = anim.transform;
        foreach (Transform child in model)
            if (child != bow.transform && child.GetComponent<SkinnedMeshRenderer>() == null) child.gameObject.SetActive(false);

        ready = true;
        SetShown(false);
        Debug.Log($"[PlayerArcherBow] 弓準備好了：網格 {bow.sharedMesh?.name}（{bow.name}）、{bones.Length} 根骨頭、材質 {bow.sharedMaterial?.name}", this);
    }

    static Vector3 Div(Vector3 a, Vector3 b) =>
        new Vector3(a.x / Mathf.Max(1e-5f, b.x), a.y / Mathf.Max(1e-5f, b.y), a.z / Mathf.Max(1e-5f, b.z));

    void SetShown(bool show)
    {
        shown = show;
        if (archer != null) archer.SetActive(show);
        if (mount != null) mount.gameObject.SetActive(show);
    }

    // 動畫算完之後才擺弓（不然會晚一幀跟不上手）
    void LateUpdate()
    {
        if (!ready) return;
        bool want = combat != null && combat.bowMode && (health == null || !health.IsDead);
        if (want != shown) SetShown(want);
        if (!want) return;

        if (catLeftHand == null && catAnimator != null && catAnimator.isHuman)
            catLeftHand = catAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform body = transform;
        Vector3 pos = catLeftHand != null ? catLeftHand.position : body.position + body.up * 1.2f + body.forward * 0.4f;
        mount.SetPositionAndRotation(pos, body.rotation * gripRotation);
    }
}
