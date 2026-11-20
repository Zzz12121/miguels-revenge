using UnityEngine;

/// <summary>
/// 村民進出房子用的正門拉門（藥鋪、茶屋）：Open() 兩片門板往兩邊滑開，Close() 滑回來關上。
/// NpcRoutine 的「進屋」會先拉開門、踏上門口的台階走到門檻（inside），人才消失、門關上；出來時反過來。
/// 跟 SlidingDoor（祠堂後門，只開不關）不一樣，這扇門會開會關，也不擋玩家（房子本身有碰撞）。
/// </summary>
public class HouseEntryDoor : MonoBehaviour
{
    [Tooltip("會滑動的兩片門板")]
    public Transform[] panels;
    [Tooltip("每片門板打開時移動多少（門板父物件的座標）")]
    public Vector3[] openOffsets;
    [Tooltip("門外的地上（村民在這裡停下來拉門）")]
    public Transform outside;
    [Tooltip("門檻上（地板的高度，村民走到這裡就算進屋了）")]
    public Transform inside;
    [Tooltip("拉開 / 關上要多久（村民用手拉，慢一點才看得出手跟著門板）")]
    public float openTime = 0.85f;

    Vector3[] closedPositions;
    float progress, target;
    Renderer[][] panelRenderers;

    /// <summary>
    /// 第 i 片門板讓人拉的地方（引手）：門板靠門中間那一邊、往內 12 cm，高度 y（夾在門板的上下範圍內），
    /// 在門板靠 from 那一面。門板滑開時跟著移動（手跟著門板走）。
    /// </summary>
    public Vector3 Grip(int i, float y, Vector3 from)
    {
        if (panels == null || i < 0 || i >= panels.Length || panels[i] == null) return transform.position;
        panelRenderers ??= new Renderer[panels.Length][];
        panelRenderers[i] ??= panels[i].GetComponentsInChildren<Renderer>(true);
        Bounds b = new Bounds(panels[i].position, Vector3.zero);
        bool any = false;
        foreach (var r in panelRenderers[i])
        {
            if (r == null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        Vector3 lat = transform.right;
        lat.y = 0f;
        lat.Normalize();
        Vector3 normal = Vector3.Cross(lat, Vector3.up); // 門面的法線（哪一邊都可以，下面依 from 決定）
        float c = Vector3.Dot(b.center - transform.position, lat);
        float sign = c >= 0f ? 1f : -1f;
        float half = Mathf.Abs(b.extents.x * lat.x) + Mathf.Abs(b.extents.z * lat.z);
        float thick = Mathf.Abs(b.extents.x * normal.x) + Mathf.Abs(b.extents.z * normal.z);
        float along = c - sign * Mathf.Max(0f, half - 0.12f);
        float face = Vector3.Dot(from - b.center, normal) >= 0f ? 1f : -1f;
        Vector3 p = transform.position + lat * along + normal * (Vector3.Dot(b.center - transform.position, normal) + face * (thick + 0.03f));
        p.y = Mathf.Clamp(y, b.min.y + 0.15f, b.max.y - 0.2f);
        return p;
    }

    /// <summary>0 = 關著，1 = 全開</summary>
    public float Progress => progress;
    public bool IsOpen => progress >= 0.999f;
    public bool IsClosed => progress <= 0.001f;

    void Awake()
    {
        closedPositions = new Vector3[panels.Length];
        for (int i = 0; i < panels.Length; i++) closedPositions[i] = panels[i] != null ? panels[i].localPosition : Vector3.zero;
    }

    public bool Opening => target >= 1f;

    public void Open()
    {
        if (target >= 1f) return;
        target = 1f;
        GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.6f, 1.15f, 0.05f, 1f, 22f);
    }

    public void Close()
    {
        if (target <= 0f) return;
        target = 0f;
        GameAudio.Play("SFX/RPG/doorClose_2", transform.position, 0.55f, 1.15f, 0.05f, 1f, 22f);
    }

    void Update()
    {
        if (Mathf.Approximately(progress, target)) return;
        progress = Mathf.MoveTowards(progress, target, Time.deltaTime / Mathf.Max(0.01f, openTime));
        float k = Mathf.SmoothStep(0f, 1f, progress);
        for (int i = 0; i < panels.Length; i++)
            if (panels[i] != null) panels[i].localPosition = closedPositions[i] + openOffsets[i] * k;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
        if (outside != null) Gizmos.DrawWireSphere(outside.position, 0.2f);
        if (inside != null) Gizmos.DrawWireSphere(inside.position, 0.2f);
        if (outside != null && inside != null) Gizmos.DrawLine(outside.position, inside.position);
    }
}
