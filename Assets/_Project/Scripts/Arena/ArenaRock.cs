using System.Collections;
using UnityEngine;

/// <summary>
/// 場上的岩石。兩種用法：
///   - Boss 房：ArenaFloor 在 Boss 戰開始時自動產生（大岩柱固定、小石塊可以推）
///   - 其他關卡：把 PushableRock.prefab 拖進場景就能用（不需要 ArenaFloor）
///
/// 功能：
///   - 推動（pushable）：玩家頂著它走就會被推動，推的時候玩家移動變慢（pushSpeedFactor）
///   - 會擋住 Boss 的光束；被光束照到會先閃爍一下再碎掉（ShatterAfter）
///   - 腳下的地板塌了、或被推進洞裡，就會掉下去
///   - Boss 撞到 / 拍地 / 跳砸：小石塊被擊飛（Knock），大岩柱被打碎（Smash）
///
/// 模型預設由程式產生（每顆都不一樣）。想用自己的模型：取消勾選 generateMesh，
/// 自己放好 MeshFilter / Collider，並把 radius、height 設成跟模型差不多的大小。
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ArenaRock : MonoBehaviour
{
    public enum State { Idle, Rising, Sliding, Cracking, Falling, Broken }

    [Header("外型")]
    [Tooltip("勾選 = 用下面的數值自動產生岩石模型")]
    public bool generateMesh = true;
    [Tooltip("形狀的亂數種子。0 = 每次進遊戲都不一樣")]
    public int seed = 0;
    [Tooltip("半徑 (m)")]
    public float radius = 0.9f;
    [Tooltip("露出地面的高度 (m)")]
    public float height = 1.4f;
    [Tooltip("勾選 = 往上變尖的岩柱；不勾 = 圓胖的石塊")]
    public bool spire = false;
    public Material material;

    [Header("推動")]
    [Tooltip("玩家可不可以推動它")]
    public bool pushable = true;
    [Tooltip("推的時候玩家的移動速度剩下幾成（越小 = 石頭越重）")]
    [Range(0.1f, 1f)] public float pushSpeedFactor = 0.45f;
    [Tooltip("被擊飛之後滑行的減速度 (m/s²)")]
    public float slideFriction = 16f;

    [Header("破壞")]
    [Tooltip("快碎掉時閃爍的顏色")]
    public Color crackColor = new Color(1f, 0.55f, 0.25f);
    [Tooltip("碎掉時噴出幾塊碎石")]
    public int debrisCount = 8;
    [Tooltip("碎掉的音效（不填 = SFX/rock_hit）")]
    public AudioClip breakSound;
    [Range(0f, 1f)] public float breakVolume = 0.7f;

    /// <summary>所在的競技場（ArenaFloor 產生的岩石才有；有的話用地板石板判斷腳下有沒有地）</summary>
    public ArenaFloor Arena { get; set; }
    public State CurrentState { get; private set; } = State.Idle;
    /// <summary>還擋得住東西（沒有碎、沒有掉下去）</summary>
    public bool IsStanding => CurrentState != State.Falling && CurrentState != State.Broken;
    /// <summary>正在閃爍、快碎了</summary>
    public bool IsCracking => CurrentState == State.Cracking;

    Renderer rend;
    Collider col;
    MaterialPropertyBlock mpb;
    Mesh renderMesh, colliderMesh;
    Color baseColor = Color.gray;
    float groundOffset;        // 物件原點比地面高多少（程式產生的岩石 = 0）
    bool groundOffsetKnown;
    Vector3 slideVelocity;
    float slideTravel;
    Transform slideIgnore;     // 被誰擊飛的（滑行時不會被它自己擋住）
    int lastPushFrame = -1;
    bool needsSync;            // 這一幀有移動，要通知物理引擎
    float lastMoveTime = float.NegativeInfinity;
    float nextGroundCheck;
    Coroutine routine;

    static readonly Collider[] overlapBuffer = new Collider[24];
    static readonly RaycastHit[] rayBuffer = new RaycastHit[12];
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    /// <summary>
    /// 這個碰撞體是不是某顆岩石的（碰撞體可能在岩石本身，也可能在它底下的子物件）。不是就回傳 null。
    /// </summary>
    public static ArenaRock From(Collider c)
    {
        if (c == null) return null;
        if (c.TryGetComponent(out ArenaRock rock)) return rock;
        var body = c.attachedRigidbody;
        return body != null && body.TryGetComponent(out rock) ? rock : null;
    }

    // =====================================================================
    //  初始化 / 模型
    // =====================================================================

    void Awake() => Setup();

    void OnEnable() => EnsureMeshAssigned();

    /// <summary>模型不見了就補回來（編輯模式下 Prefab 被重新存檔時，沒存檔的模型會被清掉）</summary>
    void EnsureMeshAssigned()
    {
        if (!generateMesh) return;
        bool ok = renderMesh != null && colliderMesh != null;
        if (ok && TryGetComponent(out MeshFilter filter) && filter.sharedMesh != renderMesh) ok = false;
        if (ok && TryGetComponent(out MeshCollider meshCollider) && meshCollider.sharedMesh != colliderMesh) ok = false;
        if (!ok) Setup();
    }

    void OnDestroy()
    {
        DestroyMesh(renderMesh);
        DestroyMesh(colliderMesh);
    }

    /// <summary>（重新）產生模型。改了 radius / height / spire / seed 之後呼叫</summary>
    public void Setup()
    {
        if (mpb == null) mpb = new MaterialPropertyBlock();

        if (generateMesh)
        {
            // 執行中缺什麼元件就自動補；編輯模式下只填模型，不亂加元件
            var filter = GetComponent<MeshFilter>();
            var meshRenderer = GetComponent<MeshRenderer>();
            var meshCollider = GetComponent<MeshCollider>();
            if (Application.IsPlaying(gameObject))
            {
                if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
                if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
                if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
            }

            int shapeSeed = seed != 0 ? seed : (Application.IsPlaying(gameObject) ? Random.Range(1, int.MaxValue) : 12345);
            DestroyMesh(renderMesh);
            DestroyMesh(colliderMesh);
            renderMesh = ArenaMeshes.BuildRock(shapeSeed, radius, height, spire, 2);
            colliderMesh = ArenaMeshes.BuildRock(shapeSeed, radius, height, spire, 1);
            renderMesh.hideFlags = ArenaMeshes.GeneratedFlags;
            colliderMesh.hideFlags = ArenaMeshes.GeneratedFlags;

            if (filter != null) filter.sharedMesh = renderMesh;
            if (meshRenderer != null && material != null) meshRenderer.sharedMaterial = material;
            if (meshCollider != null)
            {
                meshCollider.convex = true;
                meshCollider.sharedMesh = colliderMesh;
            }
        }

        rend = GetComponent<Renderer>();
        col = GetComponent<Collider>();

        // 會移動的碰撞體要有 Rigidbody（設成 Kinematic：位置完全由程式控制，不受物理引擎推擠）
        if (Application.IsPlaying(gameObject))
        {
            var body = GetComponent<Rigidbody>();
            if (body == null) body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        if (rend != null && rend.sharedMaterial != null)
        {
            var mat = rend.sharedMaterial;
            if (mat.HasProperty(BaseColorId)) baseColor = mat.GetColor(BaseColorId);
            else if (mat.HasProperty(ColorId)) baseColor = mat.GetColor(ColorId);
        }
    }

    static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }

    void SetTint(Color? color)
    {
        if (rend == null) return;
        if (!color.HasValue) { rend.SetPropertyBlock(null); return; }
        mpb.Clear();
        mpb.SetColor(BaseColorId, color.Value);
        mpb.SetColor(ColorId, color.Value);
        rend.SetPropertyBlock(mpb);
    }

    // =====================================================================
    //  每一幀：貼著地面、滑行
    // =====================================================================

    void Update()
    {
        if (!Application.IsPlaying(gameObject))
        {
            EnsureMeshAssigned(); // 編輯模式：只確認模型還在
            return;
        }

        if (CurrentState == State.Sliding) UpdateSlide();
        if (CurrentState == State.Idle || CurrentState == State.Sliding || CurrentState == State.Cracking) FollowGround();
    }

    void LateUpdate()
    {
        if (!Application.IsPlaying(gameObject) || !needsSync) return;
        needsSync = false;
        // 讓物理引擎馬上知道石頭的新位置，下一幀玩家才不會撞到「舊位置的石頭」
        Physics.SyncTransforms();
    }

    void FollowGround()
    {
        // 不在競技場裡的石頭：沒在動的時候不用每一幀都檢查地面
        if (Arena == null && Time.time - lastMoveTime > 0.5f && Time.time < nextGroundCheck) return;
        nextGroundCheck = Time.time + 0.4f;

        Vector3 p = transform.position;
        if (!groundOffsetKnown)
        {
            // 第一次：記住「原點比地面高多少」。找不到地面就當作原點在地面上
            groundOffset = TryGetGround(p, out float g0) ? p.y - g0 : 0f;
            if (generateMesh || Mathf.Abs(groundOffset) > height) groundOffset = 0f;
            groundOffsetKnown = true;
        }

        if (!TryGetGround(p, out float ground))
        {
            Fall();
            return;
        }
        float y = ground + groundOffset;
        if (Mathf.Abs(y - p.y) > 0.0005f)
        {
            p.y = y;
            transform.position = p;
            needsSync = true;
        }
    }

    /// <summary>pos 下面有沒有地、地面多高</summary>
    bool TryGetGround(Vector3 pos, out float y)
    {
        if (Arena != null)
        {
            var tile = Arena.GetTileAt(pos);
            if (tile != null)
            {
                y = tile.CurrentTopY;
                return tile.IsSolid;
            }
        }

        // 不在競技場的地板上：往下打一條射線找地面（從腳底上方 1m 往下找 6m；再往下都沒有地就當作懸空）
        Vector3 origin = new Vector3(pos.x, pos.y - groundOffset + 1f, pos.z);
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, rayBuffer, 6f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        y = 0f;
        for (int i = 0; i < n; i++)
        {
            var c = rayBuffer[i].collider;
            if (c == col || c is CharacterController) continue;
            if (From(c) != null) continue; // 自己和別的岩石都不算地面
            if (rayBuffer[i].distance < best)
            {
                best = rayBuffer[i].distance;
                y = rayBuffer[i].point.y;
            }
        }
        return best < float.MaxValue;
    }

    // =====================================================================
    //  推動 / 擊飛
    // =====================================================================

    /// <summary>
    /// 往 direction 推 distance 公尺（ThirdPersonController 撞到石頭時呼叫）。
    /// 回傳有沒有推動；前面被牆、別的石頭、Boss 擋住就推不動。
    /// </summary>
    public bool Push(Vector3 direction, float distance, Collider pusher)
    {
        if (!Application.IsPlaying(gameObject) || !pushable || CurrentState != State.Idle) return false;
        if (lastPushFrame == Time.frameCount) return true; // 同一幀只推一次
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || distance <= 0f) return false;

        Vector3 target = transform.position + direction.normalized * distance;
        if (IsBlocked(target, pusher)) return false;

        transform.position = target;
        lastPushFrame = Time.frameCount;
        MarkMoved();
        return true;
    }

    /// <summary>
    /// 被擊飛：以 velocity 的速度滑出去，慢慢停下來（推不動的大岩柱會直接碎掉）。
    /// ignore：滑行時不會被這個物件（和它底下的東西）擋住，通常傳入擊飛它的 Boss。
    /// </summary>
    public void Knock(Vector3 velocity, Transform ignore = null)
    {
        if (!Application.IsPlaying(gameObject) || !IsStanding || CurrentState == State.Rising) return;
        if (!pushable) { ShatterAfter(0.1f); return; }
        if (CurrentState == State.Cracking) return;
        velocity.y = 0f;
        slideVelocity = velocity;
        slideTravel = 0f;
        slideIgnore = ignore;
        CurrentState = State.Sliding;
    }

    void MarkMoved()
    {
        needsSync = true;
        lastMoveTime = Time.time;
    }

    void UpdateSlide()
    {
        float dt = Time.deltaTime;
        Vector3 target = transform.position + slideVelocity * dt;
        if (IsBlocked(target, null))
        {
            // 撞到東西：停下來（真的滑了一段才有撞擊聲）
            if (slideTravel > 0.5f && slideVelocity.magnitude > 3f) PlayHit(0.45f, 0.8f);
            slideVelocity = Vector3.zero;
        }
        else
        {
            slideTravel += (target - transform.position).magnitude;
            transform.position = target;
            MarkMoved();
            slideVelocity = Vector3.MoveTowards(slideVelocity, Vector3.zero, slideFriction * dt);
        }
        if (slideVelocity.sqrMagnitude < 0.09f)
        {
            slideVelocity = Vector3.zero;
            slideIgnore = null;
            CurrentState = State.Idle;
        }
    }

    /// <summary>移到 target 會不會撞到東西（地板、推的人、自己不算）</summary>
    bool IsBlocked(Vector3 target, Collider pusher)
    {
        if (Arena != null && ArenaFloor.FlatDistance(target, Arena.Center) > Arena.radius - radius * 0.7f)
            return true; // 競技場的牆

        float r = Mathf.Max(radius * 0.8f, 0.2f);
        Vector3 center = new Vector3(target.x, target.y - groundOffset + r + 0.3f, target.z); // 離地一點，才不會把地板算進去
        int n = Physics.OverlapSphereNonAlloc(center, r, overlapBuffer, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = overlapBuffer[i];
            if (c == col || c == pusher || From(c) == this) continue;
            if (c.TryGetComponent(out FloorTile _)) continue;
            if (slideIgnore != null && CurrentState == State.Sliding && c.transform.IsChildOf(slideIgnore)) continue;
            return true;
        }
        return false;
    }

    // =====================================================================
    //  升起 / 碎裂 / 掉落
    // =====================================================================

    /// <summary>從地底冒出來：delay 秒後開始，花 duration 秒（ArenaFloor 產生地形時呼叫）</summary>
    public void Rise(float delay, float duration)
    {
        if (!Application.IsPlaying(gameObject)) return;
        groundOffset = 0f;
        groundOffsetKnown = true;
        Run(RiseRoutine(delay, duration));
    }

    IEnumerator RiseRoutine(float delay, float duration)
    {
        CurrentState = State.Rising;
        Vector3 top = transform.position;
        Vector3 bottom = top + Vector3.down * (height * 1.05f + 0.2f);
        transform.position = bottom;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            // 快速衝出地面，最後稍微沉回去一點
            float eased = 1f - (1f - k) * (1f - k);
            float shake = (1f - k) * 0.05f;
            transform.position = Vector3.Lerp(bottom, top, eased) + new Vector3(Random.Range(-shake, shake), 0f, Random.Range(-shake, shake));
            yield return null;
        }
        transform.position = top;
        MarkMoved();
        CurrentState = State.Idle;
        routine = null;
    }

    /// <summary>立刻碎掉</summary>
    public void Shatter() => ShatterAfter(0f);

    /// <summary>
    /// 先閃爍抖動 delay 秒（這段時間仍然擋得住光束），然後碎掉。
    /// 已經在碎裂中、已經碎了或正在掉下去就忽略。
    /// </summary>
    public void ShatterAfter(float delay)
    {
        if (!Application.IsPlaying(gameObject) || !IsStanding || CurrentState == State.Cracking) return;
        Run(ShatterRoutine(delay));
    }

    IEnumerator ShatterRoutine(float delay)
    {
        CurrentState = State.Cracking;
        slideVelocity = Vector3.zero;
        float t = 0f;
        while (t < delay)
        {
            SetTint(Color.Lerp(crackColor, Color.white, Mathf.PingPong(t * 12f, 1f)));
            // 只抖外觀的水平位置；高度交給 FollowGround
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x + Random.Range(-0.03f, 0.03f), p.y, p.z + Random.Range(-0.03f, 0.03f));
            t += Time.deltaTime;
            yield return null;
        }
        Break();
    }

    void Break()
    {
        CurrentState = State.Broken;
        routine = null;
        if (col != null) col.enabled = false;
        if (rend != null) rend.enabled = false;
        needsSync = true;
        PlayHit(breakVolume, 1.1f);
        SpawnDebris();
        if (Arena != null) Arena.ForgetRock(this);
        Destroy(gameObject, 0.05f);
    }

    void SpawnDebris()
    {
        if (debrisCount <= 0) return;
        Material mat = rend != null ? rend.sharedMaterial : material;
        for (int i = 0; i < debrisCount; i++)
        {
            float size = radius * Random.Range(0.18f, 0.42f);
            Vector3 offset = new Vector3(Random.Range(-1f, 1f) * radius * 0.6f, Random.Range(0.15f, 1f) * height * 0.8f, Random.Range(-1f, 1f) * radius * 0.6f);
            Vector3 outward = new Vector3(offset.x, 0f, offset.z);
            outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.forward;
            Vector3 velocity = outward * Random.Range(2.5f, 7f) + Vector3.up * Random.Range(3f, 8f);
            RockChunk.Spawn(transform.position + offset, size, velocity, mat, transform.parent);
        }
    }

    void Fall()
    {
        if (!IsStanding) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(FallRoutine());
    }

    IEnumerator FallRoutine()
    {
        CurrentState = State.Falling;
        slideVelocity = Vector3.zero;
        SetTint(null);
        if (col != null) col.enabled = false;
        if (Arena != null) Arena.ForgetRock(this);

        float speed = 0f, t = 0f;
        while (t < 2f)
        {
            speed += 30f * Time.deltaTime;
            transform.position += Vector3.down * speed * Time.deltaTime;
            t += Time.deltaTime;
            yield return null;
        }
        Destroy(gameObject);
    }

    // =====================================================================
    //  Boss 的攻擊
    // =====================================================================

    /// <summary>
    /// 被 Boss 的攻擊打到（delay 秒後生效）：
    /// 可以推的小石塊 → 從 from 的反方向被擊飛；推不動的大岩柱 → 碎掉。
    /// </summary>
    public void Smash(Vector3 from, float knockSpeed, float delay, Transform attacker = null)
    {
        if (!Application.IsPlaying(gameObject) || !IsStanding || CurrentState == State.Cracking) return;
        if (!pushable) { StartCoroutine(Delayed(delay, () => ShatterAfter(0.12f))); return; }

        Vector3 away = transform.position - from;
        away.y = 0f;
        away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.forward;
        StartCoroutine(Delayed(delay, () => Knock(away * knockSpeed, attacker)));
    }

    IEnumerator Delayed(float delay, System.Action action)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        action();
    }

    // =====================================================================
    //  工具
    // =====================================================================

    void Run(IEnumerator r)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(r);
    }

    void PlayHit(float volume, float pitch)
    {
        GameAudio.PlayAt(GameAudio.Pick(breakSound, "SFX/rock_hit"), transform.position, volume, pitch * Random.Range(0.9f, 1.1f), 1f, 60f);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = pushable ? new Color(0.3f, 1f, 0.5f, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * Mathf.Max(radius * 0.8f, 0.2f), radius);
    }
}
