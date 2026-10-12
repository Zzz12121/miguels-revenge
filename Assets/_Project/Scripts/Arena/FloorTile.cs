using System.Collections;
using UnityEngine;

/// <summary>
/// 地板上的一塊石板（不規則的凸多邊形）。由 ArenaFloor 產生，負責自己的動畫：
/// - Dent：被拍地衝擊波打到時往下凹，之後慢慢回來
/// - Collapse：閃爍＋抖動預警 → 掉下去 → 一段時間後升回原位
/// - SetTelegraph：暫時改顏色（例如 Boss 攻擊範圍預警）
///
/// 石板的模型不會存在 Prefab 裡，而是每次載入時用 outline（輪廓）重新產生，
/// 所以專案不會多出一大堆模型檔。物件的位置 = 石板上表面的中心。
/// </summary>
[ExecuteAlways]
public class FloorTile : MonoBehaviour
{
    public enum State { Normal, Dented, Warning, Fallen, Restoring }

    // ---- 形狀（由 ArenaFloor 自動設定，不用手動改） ----
    /// <summary>石板的種子點（在地板座標裡的位置）。查「某個位置屬於哪一塊」時用</summary>
    [HideInInspector] public Vector2 site;
    /// <summary>輪廓：相對於這塊石板中心的凸多邊形（x→X、y→Z）</summary>
    [HideInInspector] public Vector2[] outline;
    /// <summary>石板中心在整個地板上的位置（貼圖用，讓相鄰石板的貼圖接得起來）</summary>
    [HideInInspector] public Vector2 uvOrigin;

    [Header("石板")]
    public float thickness = 2.5f;
    [Tooltip("上緣斜角的寬度 (m)，石板之間的溝就是這個")]
    public float bevel = 0.07f;
    [Tooltip("貼圖每幾公尺重複一次")]
    public float uvSize = 1.5f;
    [Tooltip("這塊石板的明暗（1 = 材質原本的顏色）。每塊稍微不一樣，才看得出一塊一塊的")]
    public float shade = 1f;
    [Tooltip("可不可以被塌陷 / 凹陷（貼著牆壁的外圈會設為 false）")]
    public bool canCollapse = true;

    [Header("塌陷外觀")]
    public Color warningColor = new Color(1f, 0.2f, 0.1f);
    public float shakeAmount = 0.06f;
    [Tooltip("預警期間慢慢下沉的距離")]
    public float warningSink = 0.15f;

    public State CurrentState { get; private set; } = State.Normal;
    public bool IsSolid => col != null && col.enabled;

    /// <summary>有沒有不規則石板的資料（false = 還是舊版的方塊）</summary>
    public bool HasShape => outline != null && outline.Length >= 3;
    /// <summary>種子點（舊版方塊地板沒有這個資料，就用方塊的位置）</summary>
    public Vector2 Site => HasShape ? site : new Vector2(transform.localPosition.x, transform.localPosition.z);

    /// <summary>靜止時的中心位置（水平位置拿來算距離）</summary>
    public Vector3 RestPosition => basePosition;
    /// <summary>靜止時上表面的高度</summary>
    public float RestTopY => basePosition.y + topOffset;
    /// <summary>目前實際上表面的高度（凹陷時會變低）</summary>
    public float CurrentTopY => transform.position.y + topOffset;

    Renderer rend;
    Collider col;
    MaterialPropertyBlock mpb;
    Color baseColor = Color.gray;
    Coroutine routine;
    Color? telegraph;          // Boss 攻擊範圍預警色（不管石板正在做什麼動畫都要顯示）
    Vector3 basePosition;
    float topOffset;           // 物件位置到上表面的距離（不規則石板 = 0；舊版方塊的原點在正中央）
    Vector3 boundsCenter, boundsHalf; // 石板的大概範圍（升回來之前檢查玩家有沒有卡在洞裡）
    Mesh renderMesh, colliderMesh;
    bool initialized;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake() => Init();

    void OnEnable()
    {
        // 編輯模式下重新載入場景 / 重新編譯之後，模型要再產生一次才看得到
        if (!initialized) Init();
        else EnsureMeshAssigned();
    }

    /// <summary>
    /// 模型不見了就補回來。編輯模式下 Prefab 被重新存檔時，Unity 會把場景裡的石板還原成 Prefab 的內容，
    /// 而模型沒有存在 Prefab 裡，所以會變成空的（ArenaFloor 在編輯模式下會定期呼叫這個檢查）。
    /// </summary>
    public void EnsureMeshAssigned()
    {
        if (!HasShape) return;
        if (!initialized) { Init(); return; }
        bool ok = renderMesh != null && colliderMesh != null;
        if (ok && TryGetComponent(out MeshFilter filter) && filter.sharedMesh != renderMesh) ok = false;
        if (ok && col is MeshCollider meshCollider && meshCollider.sharedMesh != colliderMesh) ok = false;
        if (!ok) Rebuild();
    }

    void OnDestroy()
    {
        DestroyMesh(renderMesh);
        DestroyMesh(colliderMesh);
    }

    void Init()
    {
        initialized = true;
        rend = GetComponent<Renderer>();
        col = GetComponent<Collider>();
        if (mpb == null) mpb = new MaterialPropertyBlock();
        basePosition = transform.position;

        if (rend != null && rend.sharedMaterial != null)
        {
            var mat = rend.sharedMaterial;
            if (mat.HasProperty(BaseColorId)) baseColor = mat.GetColor(BaseColorId);
            else if (mat.HasProperty(ColorId)) baseColor = mat.GetColor(ColorId);
        }
        Rebuild();
    }

    // ---------------- 模型 ----------------

    /// <summary>用 outline 重新產生外觀和碰撞模型（ArenaFloor 設定好形狀之後也會呼叫）</summary>
    public void Rebuild()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        if (col == null) col = GetComponent<Collider>();
        if (mpb == null) mpb = new MaterialPropertyBlock();

        if (!HasShape)
        {
            // 舊版的方塊：原點在方塊正中央
            topOffset = transform.lossyScale.y * 0.5f;
            boundsCenter = Vector3.zero;
            boundsHalf = transform.lossyScale * 0.45f;
            return;
        }

        DestroyMesh(renderMesh);
        DestroyMesh(colliderMesh);
        renderMesh = ArenaMeshes.BuildSlab(outline, thickness, bevel, uvOrigin, uvSize);
        colliderMesh = ArenaMeshes.BuildSlabCollider(outline, thickness);
        // 不存進場景 / Prefab，載入時再產生
        renderMesh.hideFlags = ArenaMeshes.GeneratedFlags;
        colliderMesh.hideFlags = ArenaMeshes.GeneratedFlags;

        if (TryGetComponent(out MeshFilter filter)) filter.sharedMesh = renderMesh;
        if (col is MeshCollider meshCollider)
        {
            meshCollider.convex = true;
            meshCollider.sharedMesh = colliderMesh;
        }
        // 地板是平的，不需要投射陰影（還是會接收岩石和角色的陰影），少畫一千多次
        if (rend != null) rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        Vector2 min = outline[0], max = outline[0];
        foreach (var p in outline)
        {
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        topOffset = 0f;
        boundsCenter = new Vector3((min.x + max.x) * 0.5f, -thickness * 0.5f, (min.y + max.y) * 0.5f);
        boundsHalf = new Vector3((max.x - min.x) * 0.4f, thickness * 0.45f, (max.y - min.y) * 0.4f);

        if (CurrentState != State.Warning && CurrentState != State.Fallen) SetTint(telegraph);
    }

    static void DestroyMesh(Mesh mesh)
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh);
        else DestroyImmediate(mesh);
    }

    // ---------------- 顏色 ----------------

    /// <summary>這塊石板平常的顏色（材質顏色 × 自己的明暗）</summary>
    Color OwnColor => new Color(baseColor.r * shade, baseColor.g * shade, baseColor.b * shade, baseColor.a);

    /// <summary>改顏色；傳 null 恢復平常的顏色</summary>
    public void SetTint(Color? color)
    {
        if (rend == null) return;
        if (!color.HasValue && Mathf.Abs(shade - 1f) < 0.001f)
        {
            // 就是材質原本的顏色：不用個別設定，Unity 才能把石板合在一起畫（比較省效能）
            rend.SetPropertyBlock(null);
            return;
        }
        Color c = color ?? OwnColor;
        mpb.Clear();
        mpb.SetColor(BaseColorId, c);
        mpb.SetColor(ColorId, c);
        rend.SetPropertyBlock(mpb);
    }

    /// <summary>
    /// 設定 / 清除 Boss 攻擊範圍的預警色。
    /// 石板正在恢復、凹陷中也會顯示；只有塌陷閃爍中（Warning）和掉下去（Fallen）時
    /// 先記住，等石板回來時再套用。
    /// </summary>
    public void SetTelegraph(Color? color)
    {
        telegraph = color;
        if (CurrentState != State.Warning && CurrentState != State.Fallen) SetTint(color);
    }

    // ---------------- 凹陷 ----------------

    /// <summary>
    /// 往下凹。正在恢復、已經凹陷中的石板也會被打斷改成凹陷，
    /// 只有正在塌陷 / 已經掉下去 / 還在地底等待升起的石板不受影響。
    /// </summary>
    public void Dent(float delay, float depth, float holdTime, float recoverTime)
    {
        if (!canCollapse) return;
        if (CurrentState == State.Warning || CurrentState == State.Fallen) return;
        if (CurrentState == State.Restoring && !rend.enabled) return;
        Run(DentRoutine(delay, depth, holdTime, recoverTime));
    }

    IEnumerator DentRoutine(float delay, float depth, float holdTime, float recoverTime)
    {
        CurrentState = State.Dented;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        // 如果是打斷了恢復的動畫，先把顯示和碰撞恢復正常
        rend.enabled = true;
        col.enabled = true;
        SetTint(telegraph);

        Vector3 rest = RestPosition;
        Vector3 down = rest + Vector3.down * depth;
        yield return MoveTo(transform.position, down, 0.07f); // 瞬間砸下去（從目前位置開始）
        yield return new WaitForSeconds(holdTime);
        yield return MoveTo(down, rest, recoverTime); // 慢慢回來

        CurrentState = State.Normal;
        routine = null;
    }

    // ---------------- 塌陷 ----------------

    public void Collapse(float warnTime, float downTime)
    {
        if (!canCollapse) return;
        if (CurrentState == State.Warning || CurrentState == State.Fallen || CurrentState == State.Restoring) return;
        Run(CollapseRoutine(warnTime, downTime));
    }

    IEnumerator CollapseRoutine(float warnTime, float downTime)
    {
        Vector3 rest = RestPosition;
        Color own = OwnColor;

        // 1) 預警：越閃越快、越抖越大、慢慢下沉
        CurrentState = State.Warning;
        float t = 0f;
        while (t < warnTime)
        {
            float k = t / warnTime;
            float freq = Mathf.Lerp(1.5f, 10f, k * k);
            float blink = (Mathf.Sin(t * freq * Mathf.PI * 2f) + 1f) * 0.5f;
            SetTint(Color.Lerp(own, warningColor, Mathf.Lerp(0.35f, 1f, k) * blink));

            Vector3 shake = new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)) * shakeAmount * k;
            transform.position = rest + shake + Vector3.down * warningSink * k;

            t += Time.deltaTime;
            yield return null;
        }

        // 2) 掉下去
        CurrentState = State.Fallen;
        col.enabled = false;
        SetTint(warningColor * 0.5f);
        float speed = 0f, fallTime = 0f;
        Vector3 p = transform.position;
        while (fallTime < 1.2f)
        {
            speed += 30f * Time.deltaTime;
            p += Vector3.down * speed * Time.deltaTime;
            transform.position = p;
            fallTime += Time.deltaTime;
            yield return null;
        }
        rend.enabled = false;
        yield return new WaitForSeconds(Mathf.Max(0f, downTime - fallTime));

        // 3) 升回原位（如果玩家剛好卡在這個位置就先等一下）
        CurrentState = State.Restoring;
        while (PlayerOverlaps(RestPosition + boundsCenter, boundsHalf)) yield return null;

        rend.enabled = true;
        SetTint(telegraph);
        yield return MoveTo(RestPosition + Vector3.down * (thickness + 1f), RestPosition, 0.8f);
        col.enabled = true;
        CurrentState = State.Normal;
        routine = null;
    }

    static bool PlayerOverlaps(Vector3 center, Vector3 halfExtents)
    {
        var hits = Physics.OverlapBox(center, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
            if (h is CharacterController) return true;
        return false;
    }

    /// <summary>
    /// 掉下去（或正要掉下去）的石板提早升回來，不等原本的時間。好好的石板不受影響。
    /// Boss 登場時用，讓波次戰留下的洞補起來。
    /// </summary>
    public void RestoreNow(float delay = 0f)
    {
        if (CurrentState == State.Warning)
        {
            // 還在閃、還沒掉：取消就好（上面可能還站著人，碰撞不能關）
            if (routine != null) StopCoroutine(routine);
            routine = null;
            transform.position = RestPosition;
            rend.enabled = true;
            col.enabled = true;
            SetTint(telegraph);
            CurrentState = State.Normal;
            return;
        }
        if (CurrentState != State.Fallen && CurrentState != State.Restoring) return;
        Run(RestoreRoutine(delay));
    }

    IEnumerator RestoreRoutine(float delay)
    {
        CurrentState = State.Restoring;
        col.enabled = false;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        rend.enabled = true;
        SetTint(telegraph);
        Vector3 from = transform.position;
        Vector3 below = RestPosition + Vector3.down * (thickness + 1f);
        if (from.y < below.y) from = below;
        yield return MoveTo(from, RestPosition, 0.8f);
        col.enabled = true;
        CurrentState = State.Normal;
        routine = null;
    }

    /// <summary>立刻恢復成正常狀態（flatten 是舊版地形用的參數，現在地板一直是平的）</summary>
    public void ResetTile(bool flatten = false)
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        telegraph = null;
        rend.enabled = true;
        col.enabled = true;
        transform.position = RestPosition;
        SetTint(null);
        CurrentState = State.Normal;
    }

    // ---------------- 工具 ----------------

    void Run(IEnumerator r)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(r);
    }

    IEnumerator MoveTo(Vector3 from, Vector3 to, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        transform.position = to;
    }
}
