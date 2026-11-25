using UnityEngine;

/// <summary>
/// 百足駭骨擋住鏡頭看玩家的視線時：身體顏色變淡、變半透明，看得到後面的玩家；不擋了就慢慢變回原本的樣子。
/// （鏡頭的防擋 ThirdPersonCamera.FullView 不處理角色，所以這隻大傢伙擋在中間時原本整個玩家都看不到）
///
/// 判斷：從鏡頭往玩家的頭、胸、腳拉三條線，有一條穿過百足駭骨的碰撞方塊就算擋住。
/// 擋住時把身體的材質換成 Resources/SkeletonCentipedeSeeThrough.shader（複製原本的貼圖 / 顏色），完全不透明時換回原本的材質。
/// 由 SkeletonCentipedeBossAI 自動加上（分身複製時會一起帶著），不用手動放。倒下消散時自動關掉。
/// </summary>
[DisallowMultipleComponent]
public sealed class SkeletonCentipedeSeeThrough : MonoBehaviour
{
    [Tooltip("最透明的時候剩多少不透明度（0 = 全透明，1 = 不透明）")]
    [Range(0.05f, 1f)] public float minAlpha = 0.3f;
    [Tooltip("最淡的時候顏色往灰白靠多少（0 = 原本的顏色，1 = 全部變灰白）")]
    [Range(0f, 1f)] public float paleAmount = 0.55f;
    [Tooltip("變透明 / 變回來的速度（每秒）")]
    public float fadeInSpeed = 7f, fadeOutSpeed = 3.5f;

    static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    static readonly int PaleId = Shader.PropertyToID("_Pale");
    static Shader shader;

    BoxCollider box;
    SkinnedMeshRenderer[] skins;
    Material[][] originals;
    Material[] seeThrough;
    bool swapped;
    float fade;
    Transform player;
    CharacterController playerBody;
    float nextFind;
    SkeletonCentipedeDissolveVfx dissolve;
    BossHealth health;

    /// <summary>現在有沒有變透明（0 = 原本、1 = 最透明）</summary>
    public float Fade => fade;

    void Awake()
    {
        box = GetComponent<BoxCollider>();
        skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        health = GetComponent<BossHealth>();
    }

    void LateUpdate()
    {
        if (dissolve == null) dissolve = GetComponent<SkeletonCentipedeDissolveVfx>();
        bool off = (dissolve != null && dissolve.Running) || box == null || !box.enabled;
        if (off) { fade = 0f; Restore(); return; }

        bool blocking = Blocking();
        fade = Mathf.MoveTowards(fade, blocking ? 1f : 0f, Time.unscaledDeltaTime * (blocking ? fadeInSpeed : fadeOutSpeed));
        if (fade <= 0.001f) { Restore(); return; }
        if (!swapped) Swap();
        if (seeThrough == null) return;
        float k = fade * fade * (3f - 2f * fade);
        foreach (var m in seeThrough)
        {
            if (m == null) continue;
            m.SetFloat(AlphaId, Mathf.Lerp(1f, minAlpha, k));
            m.SetFloat(PaleId, paleAmount * k);
        }
    }

    bool Blocking()
    {
        if (player == null)
        {
            if (Time.unscaledTime < nextFind) return false;
            nextFind = Time.unscaledTime + 1f;
            var hp = PlayerHealth.Find();
            if (hp == null) return false;
            player = hp.transform;
            playerBody = hp.GetComponent<CharacterController>();
        }
        var cam = Camera.main;
        if (cam == null) return false;
        Vector3 from = cam.transform.position;
        // 鏡頭跑進百足駭骨的身體裡（從裡面拉線打不到方塊）：也算擋住
        if ((box.ClosestPoint(from) - from).sqrMagnitude < 1e-4f) return true;
        float feet = playerBody != null ? playerBody.bounds.min.y : player.position.y;
        Vector3 basePos = new Vector3(player.position.x, feet, player.position.z);
        for (int i = 0; i < 3; i++)
        {
            Vector3 to = basePos + Vector3.up * (i == 0 ? 0.3f : i == 1 ? 1.1f : 1.7f);
            Vector3 d = to - from;
            float len = d.magnitude;
            if (len < 0.05f) continue;
            if (box.Raycast(new Ray(from, d / len), out _, len)) return true;
        }
        return false;
    }

    void Swap()
    {
        if (shader == null) shader = Resources.Load<Shader>("SkeletonCentipedeSeeThrough");
        if (shader == null || !shader.isSupported || skins.Length == 0) return;
        originals = new Material[skins.Length][];
        var list = new System.Collections.Generic.List<Material>();
        for (int s = 0; s < skins.Length; s++)
        {
            var r = skins[s];
            if (r == null) continue;
            var src = r.sharedMaterials;
            originals[s] = src;
            var dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var m = new Material(shader) { name = "SkeletonCentipedeSeeThrough (Instance)" };
                if (src[i] != null)
                {
                    if (src[i].HasProperty("_BaseMap"))
                    {
                        m.SetTexture("_BaseMap", src[i].GetTexture("_BaseMap"));
                        m.SetTextureScale("_BaseMap", src[i].GetTextureScale("_BaseMap"));
                        m.SetTextureOffset("_BaseMap", src[i].GetTextureOffset("_BaseMap"));
                    }
                    if (src[i].HasProperty("_BaseColor")) m.SetColor("_BaseColor", src[i].GetColor("_BaseColor"));
                }
                dst[i] = m;
                list.Add(m);
            }
            r.sharedMaterials = dst;
        }
        seeThrough = list.ToArray();
        swapped = true;
    }

    void Restore()
    {
        if (!swapped) return;
        swapped = false;
        if (originals != null)
            for (int s = 0; s < skins.Length; s++)
                if (skins[s] != null && originals[s] != null) skins[s].sharedMaterials = originals[s];
        if (seeThrough != null) foreach (var m in seeThrough) if (m != null) Destroy(m);
        seeThrough = null;
        originals = null;
    }

    /// <summary>馬上換回原本的材質（召喚分身前呼叫：分身是複製本體，不能複製到半透明的暫時材質）</summary>
    public void ForceOpaque() { fade = 0f; Restore(); }

    void OnDisable() { fade = 0f; Restore(); }
}
