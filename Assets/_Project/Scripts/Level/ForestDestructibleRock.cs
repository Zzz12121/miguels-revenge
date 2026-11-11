using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可破壞岩石（孤島森林的立石、崖邊石、林間巨石）。
///   - 每被打一下：晃動、往內縮一下、顏色變暗一點（看起來越打越裂）、掉石屑
///   - 打滿 hitsToBreak 下（重擊算 2 下）：本體消失，碎成數塊會滾落的石塊＋粉塵，鏡頭震動
///   - 百足駭骨的身體撞到也會直接碎
///   - 玩家死亡重生時恢復
///
/// 掛在石頭 Prefab 實例的根物件上即可（底下要有 Collider，玩家的攻擊才打得到）。
/// 不能是 Static Batching 物件（否則晃動、隱藏不會生效），生成器會自動清掉 Static。
/// </summary>
public class ForestDestructibleRock : MonoBehaviour, IDamageable, IImpactSurface
{
    [Min(1)] public int hitsToBreak = 5;
    [Tooltip("傷害大於這個值算重擊，一次扣 2 下")]
    public float heavyDamageThreshold = 25f;

    [Header("受擊")]
    public float shakeTime = 0.18f;
    public float shakeAmount = 0.08f;
    [Tooltip("每下變暗多少（0~1）")]
    public float darkenPerHit = 0.08f;

    [Header("碎裂")]
    public int fragmentCount = 6;
    public Vector2 fragmentScale = new Vector2(0.28f, 0.45f);
    public float fragmentForce = 6f;
    public float fragmentLifetime = 6f;
    public string breakSound = "SFX/rock_hit";
    [Tooltip("碎裂音效最長幾秒（原音檔很長，只取前段的衝擊聲）")]
    public float breakSoundMaxSeconds = 0.6f;
    public float breakShake = 0.3f;

    [Header("百足駭骨撞到就碎")]
    public bool bossCanBreak = true;

    public ImpactSurface Surface => ImpactSurface.Rock;
    public bool IsDead => broken;

    int hits;
    bool broken;
    Renderer[] renderers;
    Collider[] colliders;
    Vector3 restPos, restScale;
    Bounds bounds;
    MaterialPropertyBlock mpb;
    readonly List<GameObject> fragments = new List<GameObject>();
    readonly Collider[] overlap = new Collider[16];
    PlayerHealth player;
    Coroutine shakeRoutine;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        restPos = transform.localPosition;
        restScale = transform.localScale;
        bounds = WorldBounds();
        mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) player.OnRespawned += Restore;
        if (bossCanBreak) StartCoroutine(BossContactLoop());
    }

    void OnDestroy()
    {
        if (player != null) player.OnRespawned -= Restore;
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (broken || amount <= 0f) return;
        hits += amount >= heavyDamageThreshold ? 2 : 1;
        if (hits >= hitsToBreak) { Break(hitPoint); return; }

        SetDarken(Mathf.Clamp01(hits * darkenPerHit));
        if (shakeRoutine != null) StopCoroutine(shakeRoutine);
        shakeRoutine = StartCoroutine(Shake());
    }

    IEnumerator Shake()
    {
        float t = 0f;
        while (t < shakeTime)
        {
            t += Time.deltaTime;
            float k = 1f - t / shakeTime;
            transform.localPosition = restPos + Random.insideUnitSphere * shakeAmount * k;
            // 往內縮一下再彈回（打進去的手感）
            transform.localScale = restScale * (1f - 0.04f * k);
            yield return null;
        }
        transform.localPosition = restPos;
        transform.localScale = restScale;
        shakeRoutine = null;
    }

    IEnumerator BossContactLoop()
    {
        var wait = new WaitForSeconds(0.25f);
        while (true)
        {
            yield return wait;
            if (broken) continue;
            int n = Physics.OverlapBoxNonAlloc(bounds.center, bounds.extents, overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var boss = overlap[i].GetComponentInParent<SkeletonCentipedeBossAI>();
                if (boss == null) continue;
                Break(boss.transform.position);
                break;
            }
        }
    }

    /// <summary>直接打碎（from = 衝擊來源，碎塊往反方向飛）。</summary>
    public void Break(Vector3 from)
    {
        if (broken) return;
        broken = true;
        if (shakeRoutine != null) { StopCoroutine(shakeRoutine); shakeRoutine = null; }
        transform.localPosition = restPos;
        transform.localScale = restScale;
        foreach (var r in renderers) if (r != null) r.enabled = false;
        foreach (var c in colliders) if (c != null) c.enabled = false;

        SpawnFragments(from);
        float size = bounds.size.magnitude;
        ImpactFx.Dust(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), Mathf.Max(0.5f, size * 0.25f), Mathf.Clamp(size * 0.25f, 0.6f, 2f));
        ThirdPersonCamera.ShakeMain(breakShake, 0.25f);
        if (!string.IsNullOrEmpty(breakSound)) AudioCutoff.PlayShort(breakSound, bounds.center, 1f, 0.9f, breakSoundMaxSeconds);
    }

    void SpawnFragments(Vector3 from)
    {
        // 用石頭本身的 Mesh 縮小當碎塊（同樣的貼圖，看起來就是它碎開的）
        MeshFilter src = null;
        MeshRenderer srcR = null;
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            var mr = mf.GetComponent<MeshRenderer>();
            if (mf.sharedMesh == null || mr == null) continue;
            src = mf; srcR = mr;
            if (mf.name.Contains("LOD0")) break;
        }
        if (src == null) return;

        var playerCols = player != null ? player.GetComponentsInChildren<Collider>() : new Collider[0];
        float baseScale = src.transform.lossyScale.x;
        for (int i = 0; i < fragmentCount; i++)
        {
            var go = new GameObject("RockFragment");
            go.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.5f);
            go.transform.rotation = Random.rotation;
            go.transform.localScale = Vector3.one * baseScale * Random.Range(fragmentScale.x, fragmentScale.y);
            go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = srcR.sharedMaterials;
            r.SetPropertyBlock(mpb);
            var box = go.AddComponent<BoxCollider>();  // 自動貼合 Mesh 外框；不用 MeshCollider（不需要開 Read/Write）
            foreach (var pc in playerCols) if (pc != null) Physics.IgnoreCollision(box, pc);   // 碎塊不卡玩家
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = 2f;
            Vector3 dir = go.transform.position - from;
            dir.y = 0f;
            dir = (dir.sqrMagnitude > 0.01f ? dir.normalized : Random.onUnitSphere) + Vector3.up * Random.Range(0.4f, 1f);
            rb.AddForce(dir.normalized * fragmentForce * Random.Range(0.6f, 1.2f), ForceMode.VelocityChange);
            rb.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
            fragments.Add(go);
        }
        StartCoroutine(SinkFragments(new List<GameObject>(fragments)));
    }

    IEnumerator SinkFragments(List<GameObject> list)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, fragmentLifetime - 1.5f));
        // 停止物理、慢慢沉進地面消失
        foreach (var go in list)
        {
            if (go == null) continue;
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            var c = go.GetComponent<Collider>();
            if (c != null) c.enabled = false;
        }
        float t = 0f;
        while (t < 1.5f)
        {
            t += Time.deltaTime;
            foreach (var go in list) if (go != null) go.transform.position += Vector3.down * Time.deltaTime * 0.8f;
            yield return null;
        }
        foreach (var go in list) { fragments.Remove(go); if (go != null) Destroy(go); }
    }

    /// <summary>恢復原狀（玩家重生時自動呼叫）。</summary>
    public void Restore()
    {
        StopAllCoroutines();
        foreach (var go in fragments) if (go != null) Destroy(go);
        fragments.Clear();
        hits = 0;
        broken = false;
        shakeRoutine = null;
        transform.localPosition = restPos;
        transform.localScale = restScale;
        SetDarken(0f);
        foreach (var r in renderers) if (r != null) r.enabled = true;
        foreach (var c in colliders) if (c != null) c.enabled = true;
        if (bossCanBreak) StartCoroutine(BossContactLoop());
    }

    void SetDarken(float k)
    {
        // 用材質原本的 _BaseColor 乘上灰階（不改材質資產本身）
        float v = 1f - k;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (k <= 0f) { r.SetPropertyBlock(null); continue; }
            var m = r.sharedMaterial;
            Color baseCol = m != null && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
            mpb.Clear();
            mpb.SetColor("_BaseColor", new Color(baseCol.r * v, baseCol.g * v, baseCol.b * v, baseCol.a));
            r.SetPropertyBlock(mpb);
        }
    }

    Bounds WorldBounds()
    {
        bool has = false;
        var b = new Bounds(transform.position, Vector3.one);
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }
}
