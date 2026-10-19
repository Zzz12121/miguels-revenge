using System.Collections;
using UnityEngine;

/// <summary>
/// 時鐘陷阱，兩種用法：
///
/// 1. 站立的時鐘（Boss 召喚，原本的玩法）：隨機大小，玩家碰到就扣血並定身，壽命到了自己消失。
/// 2. 地上的時鐘（Boss 放在玩家腳下，呼叫 ArmAsFloorTrap）：平躺在地面上，倒數 1~2 秒（隨機），
///    倒數結束時玩家還站在鐘面範圍裡就會扣血並被定身（玩家會稍微變灰，時間到恢復）。
///
/// 兩種都會閃黃光（燈光 + 鐘面發光），越接近觸發 / 消失閃得越快。
/// </summary>
[RequireComponent(typeof(Collider))]
public class ClockTrap : MonoBehaviour
{
    [Header("時鐘設定")]
    [Tooltip("陷阱存在壽命（秒），時間到未被踩到也會引爆")]
    public float lifeTime = 5.0f;
    [Tooltip("隨機大小的範圍 (最小比例, 最大比例)")]
    public Vector2 minMaxScale = new Vector2(0.6f, 1.8f);

    [Header("效果設定")]
    public float damage = 15f;
    public float rootDuration = 2.0f;
    [Tooltip("時間到期或觸發時在當地生成的粒子特效 Prefab")]
    public GameObject triggerEffectPrefab;

    [Header("黃色閃爍")]
    public Color flashColor = new Color(1f, 0.85f, 0.15f);
    [Tooltip("閃爍頻率（次/秒）：開始閃 → 快結束時")]
    public Vector2 flashRate = new Vector2(1.5f, 8f);
    [Tooltip("站立的時鐘：消失前幾秒才開始閃黃光（之前維持原本的樣子）")]
    public float standingFlashLead = 1.5f;
    bool flashing;

    private bool isTriggered = false;

    [Header("地上的時鐘")]
    [Tooltip("平躺時鐘面露出地面的高度 (m)，其餘埋在地下")]
    public float floorExposed = 0.15f;

    // 地上的時鐘
    bool floorMode;
    Vector3 floorCenter;
    /// <summary>鐘面在地面上的中心（地上的時鐘大部分埋在地下，特效、判定都用這個點）</summary>
    Vector3 Surface => floorMode ? floorCenter : transform.position;
    float floorRadius;
    float armedAt, countdown;
    bool scaleSet;

    Renderer[] renderers;
    Color[] baseColors; // 每個模型原本的顏色（閃爍時從這個顏色往黃色拉）
    MaterialPropertyBlock mpb;
    Light flashLight;
    float spawnTime;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

    /// <summary>
    /// 在 position 的地面放一個平躺的時鐘，倒數 countdownSeconds 秒後判定（玩家在半徑 radius 內就中招）。
    /// clockPrefab = 有 ClockTrap 的時鐘 Prefab。
    /// </summary>
    public static ClockTrap SpawnFloorTrap(GameObject clockPrefab, Vector3 position, float radius, float countdownSeconds)
    {
        var go = Instantiate(clockPrefab, position, clockPrefab.transform.rotation);
        var trap = go.GetComponent<ClockTrap>();
        if (trap == null) trap = go.AddComponent<ClockTrap>();
        trap.ArmAsFloorTrap(radius, countdownSeconds);
        return trap;
    }

    /// <summary>改成地上的時鐘：平躺、縮放到指定半徑、倒數後判定（不會因為碰到就馬上觸發）</summary>
    public void ArmAsFloorTrap(float radius, float countdownSeconds)
    {
        floorMode = true;
        floorRadius = radius;
        countdown = countdownSeconds;
        armedAt = Time.time;
        scaleSet = true;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false; // 用距離判定
        floorCenter = transform.position; // LayFlat 找不到模型時的預設值
        LayFlat();
    }

    void Start()
    {
        spawnTime = Time.time;
        renderers = GetComponentsInChildren<Renderer>();
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            var m = renderers[i] != null ? renderers[i].sharedMaterial : null;
            baseColors[i] = m == null ? Color.white
                          : m.HasProperty(BaseColorId) ? m.GetColor(BaseColorId)
                          : m.HasProperty(ColorId) ? m.GetColor(ColorId) : Color.white;
        }
        mpb = new MaterialPropertyBlock();
        CreateLight();

        if (floorMode)
        {
            GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.8f, 1.6f, 0f, 1f, 30f);
            StartCoroutine(FloorCountdown());
            return;
        }

        // 1. 隨機生成時鐘大小
        if (!scaleSet)
        {
            float randomScale = Random.Range(minMaxScale.x, minMaxScale.y);
            transform.localScale = Vector3.one * randomScale;
        }

        // 2. 啟動壽命倒數（時間到未被踩到也會觸發特效消失）
        StartCoroutine(LifeCountdown());
    }

    IEnumerator LifeCountdown()
    {
        yield return new WaitForSeconds(lifeTime);
        TriggerExplosion();
    }

    void OnTriggerEnter(Collider other)
    {
        if (isTriggered || floorMode) return;

        // 偵測玩家踏入
        var playerHealth = other.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null && !playerHealth.IsDead)
        {
            isTriggered = true;
            Vector3 hitDir = (other.transform.position - transform.position).normalized;
            HitPlayer(playerHealth, hitDir * 2f);
            TriggerExplosion();
        }
    }

    /// <summary>扣血 + 定身（定身由玩家自己計時，陷阱接著銷毀也不會中斷，期間玩家會稍微變灰）</summary>
    void HitPlayer(PlayerHealth playerHealth, Vector3 knockback)
    {
        playerHealth.TakeDamage(damage, knockback, false, "被時間釘住了");
        if (!playerHealth.IsDead) PlayerRootEffect.Apply(playerHealth.gameObject, rootDuration);
    }

    // ---------------- 地上的時鐘 ----------------

    IEnumerator FloorCountdown()
    {
        float nextTick = 0f;
        while (Time.time - armedAt < countdown)
        {
            // 滴答聲越來越快
            float k = (Time.time - armedAt) / countdown;
            if (Time.time >= nextTick)
            {
                nextTick = Time.time + Mathf.Lerp(0.5f, 0.12f, k);
                GameAudio.Play("SFX/RPG/metalClick", transform.position, 0.45f, Mathf.Lerp(1.2f, 1.8f, k), 0f, 1f, 25f);
            }
            yield return null;
        }

        isTriggered = true;
        CombatVFX.Ring(Surface, flashColor, floorRadius);
        CombatVFX.Impact(Surface + Vector3.up * 0.3f, flashColor);
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 1f, 0.8f, 0f, 1f, 35f);

        var player = PlayerHealth.Find();
        if (player != null && !player.IsDead)
        {
            Vector3 d = player.transform.position - Surface;
            float dy = Mathf.Abs(d.y);
            d.y = 0f;
            if (d.magnitude <= floorRadius && dy < 2.5f) HitPlayer(player, Vector3.zero);
        }
        TriggerExplosion();
    }

    /// <summary>
    /// 讓時鐘平躺：找出模型最薄的那個軸（鐘面的法線），轉到朝上；再縮放到鐘面直徑 = 2 × floorRadius，貼在地上
    /// </summary>
    void LayFlat()
    {
        var rends = GetComponentsInChildren<MeshFilter>();
        var local = new Bounds();
        bool any = false;
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        foreach (var mf in rends)
        {
            if (mf.sharedMesh == null) continue;
            var b = mf.sharedMesh.bounds;
            Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(c);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        if (!any) return;

        Vector3 size = local.size;
        Vector3 thin = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
        Vector3 worldNormal = transform.TransformDirection(thin);
        if (Vector3.Dot(worldNormal, Vector3.up) < 0f) worldNormal = -worldNormal;
        transform.rotation = Quaternion.FromToRotation(worldNormal, Vector3.up) * transform.rotation;

        // 鐘面寬度（不是厚度的另外兩個軸裡比較大的）
        float face = thin == Vector3.right ? Mathf.Max(size.y, size.z) : thin == Vector3.up ? Mathf.Max(size.x, size.z) : Mathf.Max(size.x, size.y);
        if (face > 0.0001f) transform.localScale = transform.localScale * (floorRadius * 2f / (face * transform.localScale.x));

        // 貼地：放在地形 / 地面上方一點點
        Vector3 pos = transform.position;
        var terrain = Terrain.activeTerrain;
        float ground = terrain != null ? terrain.SampleHeight(pos) + terrain.transform.position.y : pos.y;
        // 鐘面很大（半徑好幾公尺），地面不平時取整個鐘面範圍內最高的地形，鐘面才不會有一半埋進坡裡
        if (terrain != null)
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Vector3 q = pos + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * floorRadius * 0.7f;
                ground = Mathf.Max(ground, terrain.SampleHeight(q) + terrain.transform.position.y);
            }
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<PlayerHealth>() == null)
            ground = Mathf.Max(ground, hit.point.y);
        // 用鐘面（Face）實際的包圍盒算：鐘面頂端露出地面 floorExposed，其餘埋在地下。
        // 只看鐘面不看指針：指針模型比較凸，用它來算會把整個鐘面壓到地底下看不到
        transform.position = new Vector3(pos.x, ground, pos.z);
        floorCenter = transform.position;
        float top = float.NegativeInfinity;
        var faceObj = FindChild(transform, "Face");
        foreach (var r in (faceObj != null ? faceObj : transform).GetComponentsInChildren<Renderer>())
            if (!(r is ParticleSystemRenderer)) top = Mathf.Max(top, r.bounds.max.y);
        if (!float.IsNegativeInfinity(top)) transform.position += Vector3.up * (ground + floorExposed - top);
    }

    static Transform FindChild(Transform t, string name)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>(true))
            if (c.name == name) return c;
        return null;
    }

    // ---------------- 黃色閃爍 ----------------

    void CreateLight()
    {
        var go = new GameObject("FlashLight");
        go.transform.SetParent(transform, false);
        go.transform.position = Surface + Vector3.up * 1.2f;
        flashLight = go.AddComponent<Light>();
        flashLight.type = LightType.Point;
        flashLight.color = flashColor;
        flashLight.range = floorMode ? floorRadius * 2.5f : 6f;
        flashLight.intensity = 0f;
        flashLight.shadows = LightShadows.None;
    }

    void Update()
    {
        if (renderers == null || isTriggered) return;

        // 0 = 開始閃，1 = 要觸發 / 消失了
        // 地上的時鐘：整段倒數都在閃；站立的時鐘：只有消失前 standingFlashLead 秒才閃
        float k;
        if (floorMode) k = Mathf.Clamp01((Time.time - armedAt) / Mathf.Max(0.01f, countdown));
        else
        {
            float left = lifeTime - (Time.time - spawnTime);
            if (left > standingFlashLead)
            {
                if (flashing) ClearFlash();
                return;
            }
            k = 1f - Mathf.Clamp01(left / Mathf.Max(0.01f, standingFlashLead));
        }
        flashing = true;
        float rate = Mathf.Lerp(flashRate.x, flashRate.y, k * k);
        float on = Mathf.Pow(0.5f + 0.5f * Mathf.Sin((Time.time - spawnTime) * rate * Mathf.PI * 2f), 2f);

        if (flashLight != null) flashLight.intensity = on * (floorMode ? 4f : 2.5f);
        Color glow = flashColor * (on * (floorMode ? 2.5f : 1.5f));
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || r is ParticleSystemRenderer) continue;
            Color tint = Color.Lerp(baseColors[i], flashColor, on * 0.7f);
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, tint);
            mpb.SetColor(ColorId, tint);
            mpb.SetColor(EmissionId, glow);
            r.SetPropertyBlock(mpb);
        }
    }

    /// <summary>還沒開始閃：恢復原本的樣子</summary>
    void ClearFlash()
    {
        flashing = false;
        if (flashLight != null) flashLight.intensity = 0f;
        foreach (var r in renderers)
        {
            if (r == null || r is ParticleSystemRenderer) continue;
            r.GetPropertyBlock(mpb);
            mpb.Clear();
            r.SetPropertyBlock(mpb);
        }
    }

    void OnGUI()
    {
        // 地上的時鐘：鐘面上方顯示倒數秒數
        if (!floorMode || isTriggered || GameUI.OverlayOpen) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(Surface + Vector3.up * 1.2f);
        if (sp.z <= 0f) return;
        float left = Mathf.Max(0f, countdown - (Time.time - armedAt));
        var style = new GUIStyle(GameUI.Center) { fontSize = 30 };
        GameUI.Shadowed(new Rect(sp.x - 60f, Screen.height - sp.y - 20f, 120f, 40f), left.ToString("0.0"), style, flashColor);
    }

    void TriggerExplosion()
    {
        if (triggerEffectPrefab != null)
        {
            // 在時鐘當前位置生成粒子特效
            Instantiate(triggerEffectPrefab, transform.position, Quaternion.identity);
        }

        Destroy(gameObject);
    }
}
