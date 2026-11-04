using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 百足駭骨技能「殘影圍殺」：
///   1. 衝到玩家周圍的圓上（Dash 蓄力姿勢 + 快速位移）
///   2. 以極快速度繞玩家轉圈 encircleDuration 秒，沿路留下紅色殘影；玩家腳下出現逐漸收緊的警示圈
///   3. 時間到 → 停下轉向玩家 → Pounce 撲擊：拋物線跳到玩家位置，落地時範圍內的玩家受到傷害
///
/// 技能執行期間由這個元件直接移動 Boss 的 Transform，Boss AI 要等 IsRunning 變回 false 再接手。
/// 傷害走 PlayerHealth.TakeDamage，所以翻滾無敵、舉盾格擋都照常有效。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeEncircleSkill : MonoBehaviour
{
    [Header("繞圈")]
    [Tooltip("繞圈半徑（公尺）")]
    public float orbitRadius = 8f;
    [Tooltip("繞圈角速度（度 / 秒）")]
    public float orbitDegreesPerSecond = 320f;
    [Tooltip("繞圈多久後撲擊（秒）")]
    public float encircleDuration = 3f;
    [Tooltip("繞圈時身體往內傾斜的角度")]
    public float leanAngle = 14f;
    [Tooltip("繞圈時爬行動畫的播放倍率")]
    public float crawlPlaybackRate = 3.2f;
    [Tooltip("衝到圓上花的時間（秒）")]
    public float entryDuration = 0.35f;

    [Header("撲擊")]
    public float damage = 30f;
    [Tooltip("落地傷害半徑（公尺）")]
    public float hitRadius = 4f;
    public float knockback = 9f;
    public float leapHeight = 4f;
    [Tooltip("要和 Pounce 動畫一致：觸發後幾秒離地、幾秒落地")]
    public float leapStart = 0.25f;
    public float leapEnd = 0.55f;
    public float recoverTime = 0.75f;
    [Tooltip("撲到玩家前方多少公尺停下（不要整隻疊在玩家身上）")]
    public float landShort = 2.5f;

    [Header("殘影")]
    public Material afterimageMaterial;
    [ColorUsage(true, true)] public Color afterimageColor = new Color(2.2f, 0.12f, 0.08f, 0.55f);
    public float afterimageInterval = 0.05f;
    public float afterimageLifetime = 0.4f;

    [Header("警示圈 / 特效")]
    public Material telegraphMaterial;
    [ColorUsage(true, true)] public Color telegraphColor = new Color(3f, 0.15f, 0.1f, 1f);
    public GameObject landingSlamPrefab;
    public GameObject landingDebrisPrefab;
    public GameObject dustPrefab;
    public float landingEffectScale = 3f;
    public float dustInterval = 0.12f;
    public float dustScale = 1.6f;

    public bool IsRunning { get; private set; }

    /// <summary>技能開始。</summary>
    public event Action Started;

    /// <summary>撲擊落地：落地位置、這次有沒有打中玩家。</summary>
    public event Action<Vector3, bool> PounceLanded;

    /// <summary>技能結束（正常結束或被死亡打斷）。</summary>
    public event Action Finished;

    SkeletonCentipedeAnimationDriver driver;
    SkinnedMeshRenderer[] skins;
    readonly Queue<Ghost> ghostPool = new();
    readonly List<Ghost> activeGhosts = new();
    LineRenderer telegraph;
    float groundOffset;
    Coroutine routine;
    MaterialPropertyBlock block;
    Quaternion facingFix = Quaternion.identity;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    class Ghost
    {
        public GameObject go;
        public Mesh[] meshes;
        public MeshRenderer[] renderers;
        public float bornAt;
    }

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        skins = GetComponentsInChildren<SkinnedMeshRenderer>();
        block = new MaterialPropertyBlock();

        // 模型的頭不一定正對 +Z：用「尾 → 頭」算出偏差，轉向時補回來
        Transform head = null, tailEnd = null;
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name.EndsWith("Head_0")) head = t;
            else if (t.name == "bone_7") tailEnd = t;
        }
        if (head != null && tailEnd != null)
        {
            Vector3 local = transform.InverseTransformDirection(head.position - tailEnd.position);
            local.y = 0f;
            if (local.sqrMagnitude > 0.0001f) facingFix = Quaternion.Inverse(Quaternion.LookRotation(local));
        }
    }

    void OnDisable()
    {
        Stop();
    }

    void OnDestroy()
    {
        foreach (var g in ghostPool) DestroyGhost(g);
        foreach (var g in activeGhosts) DestroyGhost(g);
        if (telegraph != null) Destroy(telegraph.gameObject);
    }

    public bool TryStart()
    {
        if (IsRunning || driver.IsDead) return false;
        routine = StartCoroutine(Run());
        return true;
    }

    public void Stop()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (telegraph != null) telegraph.enabled = false;
        if (IsRunning)
        {
            IsRunning = false;
            if (driver != null && !driver.IsDead) driver.SetMoveSpeed(0f);
            Finished?.Invoke();
        }
    }

    void Update()
    {
        FadeGhosts();
    }

    IEnumerator Run()
    {
        IsRunning = true;
        Started?.Invoke();
        groundOffset = transform.position.y - GroundY(transform.position);
        var player = PlayerHealth.Find();

        // 1. 衝到圓上：從玩家看過來 Boss 現在的方向，就是起始角度
        Vector3 center = Center(player);
        Vector3 fromCenter = transform.position - center;
        fromCenter.y = 0f;
        if (fromCenter.sqrMagnitude < 0.01f) fromCenter = -transform.forward;
        float angle = Mathf.Atan2(fromCenter.z, fromCenter.x) * Mathf.Rad2Deg;

        driver.SetMoveSpeed(1f);
        driver.SetCrawlPlaybackRate(crawlPlaybackRate);
        Vector3 entryFrom = transform.position;
        float nextGhost = 0f, nextDust = 0f;
        for (float t = 0f; t < entryDuration; t += Time.deltaTime)
        {
            if (driver.IsDead) { Stop(); yield break; }
            center = Center(player);
            Vector3 target = OrbitPoint(center, angle);
            Vector3 p = Vector3.Lerp(entryFrom, target, Mathf.SmoothStep(0f, 1f, t / entryDuration));
            Face(target - entryFrom, 0f);
            Place(p);
            TickTrail(ref nextGhost, ref nextDust);
            yield return null;
        }

        // 2. 高速繞圈 + 殘影 + 警示圈收緊
        ShowTelegraph(true);
        float elapsed = 0f;
        while (elapsed < encircleDuration)
        {
            if (driver.IsDead) { Stop(); yield break; }
            elapsed += Time.deltaTime;
            angle += orbitDegreesPerSecond * Time.deltaTime;
            center = Center(player);
            Vector3 p = OrbitPoint(center, angle);
            // 切線方向（逆時針繞）＝ 頭朝的方向，身體往圓心傾
            Vector3 tangent = new Vector3(-Mathf.Sin(angle * Mathf.Deg2Rad), 0f, Mathf.Cos(angle * Mathf.Deg2Rad));
            Face(tangent, leanAngle);
            Place(p);
            TickTrail(ref nextGhost, ref nextDust);
            UpdateTelegraph(center, elapsed / encircleDuration);
            yield return null;
        }

        // 3. 撲擊：停下、轉向玩家、跳過去
        driver.SetMoveSpeed(0f);
        center = Center(player);
        Vector3 start = transform.position;
        Vector3 toPlayer = center - start;
        toPlayer.y = 0f;
        Face(toPlayer, 0f);
        driver.PlayPounce();

        Vector3 landing = start;
        bool leapLocked = false;
        float pounceT = 0f;
        while (pounceT < leapEnd)
        {
            if (driver.IsDead) { Stop(); yield break; }
            pounceT += Time.deltaTime;
            if (!leapLocked && pounceT >= leapStart)
            {
                // 離地那一刻才鎖定落點，玩家在蓄力時還有機會跑開或翻滾
                leapLocked = true;
                center = Center(player);
                Vector3 dir = center - start;
                dir.y = 0f;
                float dist = Mathf.Max(0f, dir.magnitude - landShort);
                landing = start + (dir.sqrMagnitude > 0.001f ? dir.normalized * dist : Vector3.zero);
                Face(dir, 0f);
            }
            if (leapLocked)
            {
                float u = Mathf.Clamp01((pounceT - leapStart) / Mathf.Max(0.01f, leapEnd - leapStart));
                Vector3 p = Vector3.Lerp(start, landing, u);
                float groundY = Mathf.Lerp(GroundY(start), GroundY(landing), u);
                p.y = groundY + groundOffset + 4f * leapHeight * u * (1f - u);
                transform.position = p;
                UpdateTelegraph(center, 1f);
            }
            yield return null;
        }

        Place(landing);
        ShowTelegraph(false);
        bool hit = DealDamage(player, landing);
        Spawn(landingSlamPrefab, new Vector3(landing.x, GroundY(landing), landing.z), landingEffectScale);
        Spawn(landingDebrisPrefab, new Vector3(landing.x, GroundY(landing), landing.z), landingEffectScale * 0.8f);
        ThirdPersonCamera.ShakeMain(0.35f, 0.35f);
        PounceLanded?.Invoke(landing, hit);

        yield return new WaitForSeconds(recoverTime);
        routine = null;
        IsRunning = false;
        Finished?.Invoke();
    }

    Vector3 Center(PlayerHealth player)
    {
        if (player != null && !player.IsDead) return player.transform.position;
        // 場景裡沒有玩家（純預覽）：繞自己前方的一個點
        return transform.position + transform.forward * orbitRadius;
    }

    Vector3 OrbitPoint(Vector3 center, float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return center + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius;
    }

    void Place(Vector3 p)
    {
        p.y = GroundY(p) + groundOffset;
        transform.position = p;
    }

    void Face(Vector3 dir, float lean)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;
        // 逆時針繞時圓心在左邊，所以往左傾（繞 forward 正轉）
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.AngleAxis(lean, Vector3.forward) * facingFix;
    }

    bool DealDamage(PlayerHealth player, Vector3 landing)
    {
        if (player == null || player.IsDead) return false;
        Vector3 away = player.transform.position - landing;
        away.y = 0f;
        // 落點本來就停在玩家前方 landShort，所以範圍要把這段加回去
        if (away.magnitude > hitRadius + landShort) return false;
        Vector3 push = (away.sqrMagnitude > 0.001f ? away.normalized : transform.forward) * knockback + Vector3.up * 4f;
        return player.TakeDamage(damage, push, false, "被百足駭骨撲殺");
    }

    // ───────── 殘影 ─────────

    void TickTrail(ref float nextGhost, ref float nextDust)
    {
        if (Time.time >= nextGhost)
        {
            nextGhost = Time.time + afterimageInterval;
            SpawnGhost();
        }
        if (dustPrefab != null && Time.time >= nextDust)
        {
            nextDust = Time.time + dustInterval;
            Spawn(dustPrefab, new Vector3(transform.position.x, GroundY(transform.position), transform.position.z), dustScale);
        }
    }

    void SpawnGhost()
    {
        if (afterimageMaterial == null || skins.Length == 0) return;
        var g = ghostPool.Count > 0 ? ghostPool.Dequeue() : CreateGhost();
        for (int i = 0; i < skins.Length; i++)
        {
            var skin = skins[i];
            skin.BakeMesh(g.meshes[i], true);
            var t = g.renderers[i].transform;
            t.SetPositionAndRotation(skin.transform.position, skin.transform.rotation);
        }
        g.bornAt = Time.time;
        g.go.SetActive(true);
        activeGhosts.Add(g);
    }

    Ghost CreateGhost()
    {
        var g = new Ghost
        {
            go = new GameObject("SkeletonCentipede_Afterimage"),
            meshes = new Mesh[skins.Length],
            renderers = new MeshRenderer[skins.Length],
        };
        for (int i = 0; i < skins.Length; i++)
        {
            var part = new GameObject($"Part{i}");
            part.transform.SetParent(g.go.transform, false);
            g.meshes[i] = new Mesh { name = "AfterimageMesh" };
            g.meshes[i].MarkDynamic();
            part.AddComponent<MeshFilter>().sharedMesh = g.meshes[i];
            var mr = part.AddComponent<MeshRenderer>();
            mr.sharedMaterial = afterimageMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            g.renderers[i] = mr;
        }
        return g;
    }

    void FadeGhosts()
    {
        for (int i = activeGhosts.Count - 1; i >= 0; i--)
        {
            var g = activeGhosts[i];
            float u = (Time.time - g.bornAt) / afterimageLifetime;
            if (u >= 1f)
            {
                g.go.SetActive(false);
                activeGhosts.RemoveAt(i);
                ghostPool.Enqueue(g);
                continue;
            }
            Color c = afterimageColor;
            float fade = 1f - u;
            c.a *= fade * fade;
            c.r *= fade;
            c.g *= fade;
            c.b *= fade;
            block.SetColor(BaseColorId, c);
            foreach (var r in g.renderers) r.SetPropertyBlock(block);
        }
    }

    static void DestroyGhost(Ghost g)
    {
        if (g == null) return;
        foreach (var m in g.meshes) if (m != null) Destroy(m);
        if (g.go != null) Destroy(g.go);
    }

    // ───────── 警示圈 ─────────

    void ShowTelegraph(bool show)
    {
        if (telegraph == null)
        {
            if (!show || telegraphMaterial == null) return;
            var go = new GameObject("SkeletonCentipede_EncircleTelegraph");
            telegraph = go.AddComponent<LineRenderer>();
            telegraph.sharedMaterial = telegraphMaterial;
            telegraph.loop = true;
            telegraph.useWorldSpace = true;
            telegraph.positionCount = 64;
            telegraph.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            telegraph.receiveShadows = false;
            telegraph.alignment = LineAlignment.TransformZ;
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 線面朝上，平貼地面
        }
        telegraph.enabled = show;
    }

    void UpdateTelegraph(Vector3 center, float progress)
    {
        if (telegraph == null || !telegraph.enabled) return;
        // 從繞圈半徑收緊到撲擊範圍，越接近撲擊越亮、閃得越快
        float radius = Mathf.Lerp(orbitRadius * 0.85f, hitRadius, progress);
        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * Mathf.Lerp(6f, 28f, progress));
        Color c = telegraphColor * Mathf.Lerp(0.4f, 1.3f, progress) * pulse;
        c.a = Mathf.Lerp(0.35f, 1f, progress);
        telegraph.startColor = telegraph.endColor = c;
        telegraph.widthMultiplier = Mathf.Lerp(0.12f, 0.3f, progress);
        float y = GroundY(center) + 0.06f;
        for (int i = 0; i < telegraph.positionCount; i++)
        {
            float a = i / (float)telegraph.positionCount * Mathf.PI * 2f;
            telegraph.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, y, center.z + Mathf.Sin(a) * radius));
        }
    }

    // ───────── 工具 ─────────

    float GroundY(Vector3 at)
    {
        var hits = Physics.RaycastAll(at + Vector3.up * 6f, Vector3.down, 40f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue, y = at.y - groundOffset;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.collider.GetComponentInParent<PlayerHealth>() != null) continue;
            if (h.distance < best) { best = h.distance; y = h.point.y; }
        }
        return y;
    }

    void Spawn(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return;
        var fx = Instantiate(prefab, position, Quaternion.LookRotation(Vector3.forward));
        fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, 4f);
    }
}
