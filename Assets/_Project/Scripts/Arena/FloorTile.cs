using System.Collections;
using UnityEngine;

/// <summary>
/// 地板上的一個小方塊。由 ArenaFloor 產生，負責自己的動畫：
/// - SetHeight：升高 / 降低（地形起伏、柱子）。升高時方塊會往下加長，底下不會出現空隙
/// - Dent：被拍地衝擊波打到時往下凹，之後慢慢回來
/// - Collapse：閃爍＋抖動預警 → 掉下去 → 一段時間後升回原位
/// - SetTint：暫時改顏色（例如 Boss 攻擊範圍預警）
/// </summary>
[RequireComponent(typeof(Renderer), typeof(Collider))]
public class FloorTile : MonoBehaviour
{
    public enum State { Normal, Shifting, Dented, Warning, Fallen, Restoring }

    [Tooltip("在格子中的座標（由 ArenaFloor 自動設定）")]
    public int gridX, gridZ;
    [Tooltip("可不可以被塌陷 / 凹陷 / 升高（貼著牆壁的外圈會設為 false）")]
    public bool canCollapse = true;

    [Header("塌陷外觀")]
    public Color warningColor = new Color(1f, 0.2f, 0.1f);
    public float shakeAmount = 0.06f;
    [Tooltip("預警期間慢慢下沉的距離")]
    public float warningSink = 0.15f;

    public State CurrentState { get; private set; } = State.Normal;
    public bool IsSolid => col != null && col.enabled;
    /// <summary>是不是柱子（光束會被柱子擋住）</summary>
    public bool IsPillar { get; private set; }
    /// <summary>目前設定的高度（相對原本地面）</summary>
    public float Height { get; private set; }

    /// <summary>靜止時的中心位置（已經算進高度）</summary>
    public Vector3 RestPosition =>
        basePosition + Vector3.up * (Height - Mathf.Max(Height, 0f) * 0.5f);
    /// <summary>靜止時的大小（升高時往下加長）</summary>
    public Vector3 RestScale =>
        new Vector3(baseScale.x, baseScale.y + Mathf.Max(Height, 0f), baseScale.z);
    /// <summary>靜止時上表面的高度</summary>
    public float RestTopY => basePosition.y + baseScale.y * 0.5f + Height;
    /// <summary>目前實際上表面的高度（凹陷時會變低）</summary>
    public float CurrentTopY => transform.position.y + transform.lossyScale.y * 0.5f;

    Renderer rend;
    Collider col;
    MaterialPropertyBlock mpb;
    Color baseColor = Color.gray;
    Coroutine routine;
    Vector3 basePosition;
    Vector3 baseScale;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        rend = GetComponent<Renderer>();
        col = GetComponent<Collider>();
        mpb = new MaterialPropertyBlock();
        basePosition = transform.position;
        baseScale = transform.localScale;

        var mat = rend.sharedMaterial;
        if (mat != null)
        {
            if (mat.HasProperty(BaseColorId)) baseColor = mat.GetColor(BaseColorId);
            else if (mat.HasProperty(ColorId)) baseColor = mat.GetColor(ColorId);
        }
    }

    // ---------------- 顏色 ----------------

    /// <summary>改顏色；傳 null 恢復原本材質顏色</summary>
    public void SetTint(Color? color)
    {
        mpb.Clear();
        if (color.HasValue)
        {
            mpb.SetColor(BaseColorId, color.Value);
            mpb.SetColor(ColorId, color.Value);
        }
        rend.SetPropertyBlock(mpb);
    }

    /// <summary>只有在正常狀態時才改顏色（避免蓋掉塌陷的閃爍）</summary>
    public void SetTelegraph(Color? color)
    {
        if (CurrentState == State.Normal) SetTint(color);
    }

    // ---------------- 地形高度 ----------------

    /// <summary>把方塊升高 / 降低到 height，delay 秒後開始、花 duration 秒完成</summary>
    public void SetHeight(float height, bool isPillar, float delay, float duration)
    {
        if (!canCollapse) { height = 0f; isPillar = false; }
        ForceVisible();
        Vector3 fromPos = transform.position, fromScale = transform.localScale;
        Height = height;
        IsPillar = isPillar;
        Run(ShiftRoutine(fromPos, fromScale, delay, duration));
    }

    IEnumerator ShiftRoutine(Vector3 fromPos, Vector3 fromScale, float delay, float duration)
    {
        CurrentState = State.Shifting;
        if (delay > 0f) yield return new WaitForSeconds(delay);

        Vector3 toPos = RestPosition, toScale = RestScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.position = Vector3.Lerp(fromPos, toPos, k);
            transform.localScale = Vector3.Lerp(fromScale, toScale, k);
            yield return null;
        }
        transform.position = toPos;
        transform.localScale = toScale;
        CurrentState = State.Normal;
        routine = null;
    }

    // ---------------- 柱子被破壞 ----------------

    /// <summary>柱子正在碎裂中（還沒倒，仍然會擋光束）</summary>
    public bool IsCracking { get; private set; }

    /// <summary>柱子立刻被打爆（這場戰鬥內不會再長回來）</summary>
    public void Shatter() => ShatterAfter(0f);

    /// <summary>
    /// 柱子被打中：先閃爍抖動 delay 秒（這段時間仍然擋得住光束），然後崩塌回地面。
    /// 已經在碎裂中就忽略。
    /// </summary>
    public void ShatterAfter(float delay)
    {
        if (!IsPillar || IsCracking) return;
        IsCracking = true;
        ForceVisible();
        Run(ShatterRoutine(delay));
    }

    IEnumerator ShatterRoutine(float delay)
    {
        CurrentState = State.Shifting;
        Vector3 fromPos = transform.position, fromScale = transform.localScale;

        float crack = Mathf.Max(delay, 0.1f);
        float t = 0f;
        while (t < crack) // 閃白 + 抖動（還擋得住光束）
        {
            SetTint(Color.Lerp(warningColor, Color.white, Mathf.PingPong(t * 12f, 1f)));
            transform.position = fromPos + Random.insideUnitSphere * 0.12f;
            t += Time.deltaTime;
            yield return null;
        }

        // 真正倒下：不再是柱子，高度歸零
        IsPillar = false;
        IsCracking = false;
        Height = 0f;
        SetTint(warningColor);

        Vector3 toPos = RestPosition, toScale = RestScale;
        t = 0f;
        while (t < 0.35f) // 快速崩塌回地面
        {
            t += Time.deltaTime;
            float k = t / 0.35f;
            k *= k;
            transform.position = Vector3.Lerp(fromPos, toPos, k);
            transform.localScale = Vector3.Lerp(fromScale, toScale, k);
            yield return null;
        }
        transform.position = toPos;
        transform.localScale = toScale;
        SetTint(null);
        CurrentState = State.Normal;
        routine = null;
    }

    // ---------------- 凹陷 ----------------

    public void Dent(float delay, float depth, float holdTime, float recoverTime)
    {
        if (!canCollapse || CurrentState != State.Normal) return;
        Run(DentRoutine(delay, depth, holdTime, recoverTime));
    }

    IEnumerator DentRoutine(float delay, float depth, float holdTime, float recoverTime)
    {
        CurrentState = State.Dented;
        if (delay > 0f) yield return new WaitForSeconds(delay);
        SetTint(null);

        Vector3 rest = RestPosition;
        Vector3 down = rest + Vector3.down * depth;
        yield return MoveTo(rest, down, 0.07f);       // 瞬間砸下去
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
        transform.localScale = RestScale;

        // 1) 預警：越閃越快、越抖越大、慢慢下沉
        CurrentState = State.Warning;
        float t = 0f;
        while (t < warnTime)
        {
            float k = t / warnTime;
            float freq = Mathf.Lerp(1.5f, 10f, k * k);
            float blink = (Mathf.Sin(t * freq * Mathf.PI * 2f) + 1f) * 0.5f;
            SetTint(Color.Lerp(baseColor, warningColor, Mathf.Lerp(0.35f, 1f, k) * blink));

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
        Vector3 half = RestScale * 0.45f;
        while (PlayerOverlaps(RestPosition, half)) yield return null;

        rend.enabled = true;
        SetTint(null);
        yield return MoveTo(RestPosition + Vector3.down * (RestScale.y + 1f), RestPosition, 0.8f);
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

    /// <summary>立刻恢復成正常狀態；flatten = true 時連高度也歸零</summary>
    public void ResetTile(bool flatten = false)
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        if (flatten) { Height = 0f; IsPillar = false; }
        IsCracking = false;
        ForceVisible();
        transform.position = RestPosition;
        transform.localScale = RestScale;
        SetTint(null);
        CurrentState = State.Normal;
    }

    void ForceVisible()
    {
        rend.enabled = true;
        col.enabled = true;
        SetTint(null);
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
