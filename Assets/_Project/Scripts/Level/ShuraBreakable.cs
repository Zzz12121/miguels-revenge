using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 可破壞物件（酒樽、木箱、米俵、陶甕…）。玩家的攻擊會打它（實作 IDamageable），
/// 血量歸零 → 外觀和碰撞消失、噴出碎片和煙塵；百足駭骨的身體撞到也會直接撞碎。
/// 玩家死亡重生時全部恢復。
///
/// 換成自己的模型：把模型放在這個物件底下、加一個包住它的 Collider，再掛這個元件即可。
/// 有「已經切好的碎塊模型」（例如 Blender Cell Fracture）就指定 brokenPrefab，會改用它的碎塊；
/// 沒有的話會用模型材質生出小方塊碎片。
/// </summary>
public class ShuraBreakable : MonoBehaviour, IDamageable
{
    public float maxHealth = 30f;

    [Header("碎片")]
    [Tooltip("（選填）預先切好的碎塊模型；子物件會各自加上 Rigidbody 飛散")]
    public GameObject brokenPrefab;
    public int debrisCount = 8;
    public Vector2 debrisSize = new Vector2(0.12f, 0.35f);
    public float debrisForce = 5f;
    public float debrisLifetime = 3f;
    [Tooltip("碎片用的材質；留空 = 用模型本身的材質")]
    public Material debrisMaterial;

    [Header("特效 / 音效")]
    public GameObject breakEffectPrefab;
    public float breakEffectScale = 1f;
    public string breakSound = "SFX/rock_hit";
    public float breakSoundPitch = 1.3f;

    [Header("被打但還沒破")]
    public float hitShakeTime = 0.15f;
    public float hitShakeAmount = 0.06f;

    [Header("百足駭骨撞到就碎")]
    public bool bossCanBreak = true;

    public bool IsDead => broken;
    public bool IsBroken => broken;

    float health;
    bool broken;
    Renderer[] renderers;
    Collider[] colliders;
    Light[] lights;
    Vector3 restLocalPos;
    Bounds localBounds;
    float shakeUntil, nextBossCheck;
    PlayerHealth player;
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly Collider[] overlap = new Collider[16];

    void Awake()
    {
        health = maxHealth;
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);
        lights = GetComponentsInChildren<Light>(true);
        restLocalPos = transform.localPosition;
        localBounds = ComputeWorldBounds();
        nextBossCheck = Time.time + Random.value * 0.2f; // 錯開檢查時間
    }

    void Start()
    {
        player = PlayerHealth.Find();
        if (player != null) player.OnRespawned += Restore;
    }

    void OnDestroy()
    {
        if (player != null) player.OnRespawned -= Restore;
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (broken || amount <= 0f) return;
        health -= amount;
        if (health <= 0f) Break(hitPoint);
        else
        {
            shakeUntil = Time.time + hitShakeTime;
            SpawnDebris(hitPoint, Mathf.Max(1, debrisCount / 4), 0.5f);
        }
    }

    void Update()
    {
        if (broken) return;

        if (Time.time < shakeUntil)
            transform.localPosition = restLocalPos + Random.insideUnitSphere * hitShakeAmount;
        else if (transform.localPosition != restLocalPos)
            transform.localPosition = restLocalPos;

        // 百足駭骨的身體碰到就撞碎（每 0.2 秒檢查一次）
        if (bossCanBreak && Time.time >= nextBossCheck)
        {
            nextBossCheck = Time.time + 0.2f;
            int n = Physics.OverlapBoxNonAlloc(localBounds.center, localBounds.extents, overlap, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var boss = overlap[i].GetComponentInParent<SkeletonCentipedeBossAI>();
                if (boss == null) continue;
                Break(boss.transform.position);
                break;
            }
        }
    }

    /// <summary>直接打碎（from = 衝擊來的方向，碎片往反方向飛）。</summary>
    public void Break(Vector3 from)
    {
        if (broken) return;
        broken = true;
        transform.localPosition = restLocalPos;
        foreach (var r in renderers) if (r != null) r.enabled = false;
        foreach (var c in colliders) if (c != null) c.enabled = false;
        foreach (var l in lights) if (l != null) l.enabled = false;

        Vector3 center = localBounds.center;
        if (brokenPrefab != null) SpawnFractured(from);
        else SpawnDebris(from, debrisCount, 1f);

        if (breakEffectPrefab != null)
        {
            var fx = Instantiate(breakEffectPrefab, center, Quaternion.identity);
            fx.transform.localScale = Vector3.one * breakEffectScale;
            Destroy(fx, 4f);
        }
        if (!string.IsNullOrEmpty(breakSound)) GameAudio.Play(breakSound, center, 0.8f, breakSoundPitch, 0.1f, 1f, 40f);
    }

    /// <summary>恢復原狀（玩家重生時自動呼叫）。</summary>
    public void Restore()
    {
        StopAllCoroutines();
        foreach (var go in spawned) if (go != null) Destroy(go);
        spawned.Clear();
        health = maxHealth;
        broken = false;
        transform.localPosition = restLocalPos;
        foreach (var r in renderers) if (r != null) r.enabled = true;
        foreach (var c in colliders) if (c != null) c.enabled = true;
        foreach (var l in lights) if (l != null) l.enabled = true;
    }

    void SpawnDebris(Vector3 from, int count, float forceScale)
    {
        Material mat = debrisMaterial;
        if (mat == null)
            foreach (var r in renderers)
                if (r != null && r.sharedMaterial != null) { mat = r.sharedMaterial; break; }

        Vector3 center = localBounds.center;
        var pieces = new List<Transform>();
        for (int i = 0; i < count; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Debris";
            Vector3 p = center + Vector3.Scale(Random.insideUnitSphere, localBounds.extents * 0.6f);
            go.transform.SetPositionAndRotation(p, Random.rotation);
            float s = Random.Range(debrisSize.x, debrisSize.y);
            go.transform.localScale = new Vector3(s, s * Random.Range(0.4f, 1f), s * Random.Range(0.5f, 1.2f));
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Launch(go, p, from, forceScale);
            pieces.Add(go.transform);
        }
        StartCoroutine(FadeOut(pieces));
    }

    void SpawnFractured(Vector3 from)
    {
        var root = Instantiate(brokenPrefab, transform.position, transform.rotation);
        root.transform.localScale = transform.lossyScale;
        spawned.Add(root);
        var pieces = new List<Transform>();
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            var go = mf.gameObject;
            if (go.GetComponent<Collider>() == null)
            {
                var mc = go.AddComponent<MeshCollider>();
                mc.convex = true;
            }
            Launch(go, go.transform.position, from, 1f);
            pieces.Add(go.transform);
        }
        StartCoroutine(FadeOut(pieces));
    }

    void Launch(GameObject go, Vector3 at, Vector3 from, float forceScale)
    {
        if (!spawned.Contains(go) && go.transform.parent == null) spawned.Add(go);
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.2f;
        Vector3 dir = at - from;
        dir.y = 0f;
        dir = (dir.sqrMagnitude > 0.001f ? dir.normalized : Random.onUnitSphere) + Vector3.up * Random.Range(0.6f, 1.4f) + Random.insideUnitSphere * 0.5f;
        rb.AddForce(dir.normalized * debrisForce * forceScale * Random.Range(0.6f, 1.2f), ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 10f, ForceMode.VelocityChange);
    }

    IEnumerator FadeOut(List<Transform> pieces)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, debrisLifetime - 0.6f));
        var start = new List<Vector3>();
        foreach (var p in pieces) start.Add(p != null ? p.localScale : Vector3.zero);
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.deltaTime;
            float k = 1f - t / 0.6f;
            for (int i = 0; i < pieces.Count; i++) if (pieces[i] != null) pieces[i].localScale = start[i] * k;
            yield return null;
        }
        foreach (var p in pieces)
        {
            if (p == null) continue;
            spawned.Remove(p.gameObject);
            Destroy(p.gameObject);
        }
    }

    Bounds ComputeWorldBounds()
    {
        bool has = false;
        var b = new Bounds(transform.position, Vector3.one * 0.5f);
        foreach (var c in GetComponentsInChildren<Collider>(true))
        {
            if (!has) { b = c.bounds; has = true; }
            else b.Encapsulate(c.bounds);
        }
        if (!has)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
        return b;
    }
}
