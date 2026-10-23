using UnityEngine;

/// <summary>
/// 深淵印記（力量路線進階能力「深淵復仇」）：掛在被標記的敵人身上，持續 duration 秒。
/// 期間玩家打在牠身上的傷害都會記下來，時間到時把累積的傷害再打一次（只有一次，不會再被記錄）。
/// 敵人頭上顯示紫色光和累積的傷害；用 AbyssMark.Apply / AbyssMark.Record 操作，不用手動掛。
/// </summary>
public class AbyssMark : MonoBehaviour
{
    public static readonly Color MarkColor = new Color(0.6f, 0.25f, 1f);

    IDamageable target;
    float endTime;
    float stored;
    Light glow;

    /// <summary>標記敵人（已經有印記的話不重複標記）</summary>
    public static void Apply(IDamageable target, float duration)
    {
        if (!(target is Component c) || c == null || target.IsDead) return;
        if (c.GetComponent<AbyssMark>() != null) return;
        var mark = c.gameObject.AddComponent<AbyssMark>();
        mark.target = target;
        mark.endTime = Time.time + duration;
        CombatVFX.Ring(c.transform.position + Vector3.up * 0.1f, MarkColor, 1.6f);
        GameAudio.Play("SFX/RPG/metalLatch", c.transform.position, 0.7f, 0.6f, 0.05f, 0.3f);
    }

    /// <summary>玩家打中敵人時呼叫：有印記就把這次的傷害記下來</summary>
    public static void Record(IDamageable target, float damage)
    {
        if (target is Component c && c != null && c.TryGetComponent(out AbyssMark mark)) mark.stored += damage;
    }

    void Start()
    {
        var go = new GameObject("AbyssGlow");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.2f;
        glow = go.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = MarkColor;
        glow.range = 4f;
    }

    void Update()
    {
        if (target == null || target.IsDead) { Destroy(this); return; }
        if (glow != null) glow.intensity = 2f + 1.5f * Mathf.Sin(Time.time * 10f);
        if (Time.time < endTime) return;

        Vector3 p = transform.position + Vector3.up * 1f;
        if (stored > 0f)
        {
            // 深淵復仇的傷害不受 Boss 受擊無敵時間影響
            if (target is IUnblockableDamageable unblockable) unblockable.TakeUnblockableDamage(stored, p);
            else target.TakeDamage(stored, p);
            CombatVFX.Impact(p, MarkColor);
            CombatVFX.Ring(transform.position + Vector3.up * 0.1f, MarkColor, 2.4f);
            GameAudio.Play("SFX/RPG/metalPot3", p, 1f, 0.6f, 0.05f, 0.3f);
        }
        Destroy(this);
    }

    void OnDestroy()
    {
        if (glow != null) Destroy(glow.gameObject);
    }

    static GUIStyle style;

    void OnGUI()
    {
        if(!CalligraphyWorldIsolation.Allows(transform))return;
        if (GameUI.OverlayOpen) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * 2.6f);
        if (sp.z <= 0f) return;
        if (style == null) style = new GUIStyle(GameUI.Center) { fontStyle = FontStyle.Bold };
        GameUI.Shadowed(new Rect(sp.x - 100f, Screen.height - sp.y - 14f, 200f, 28f),
                        $"深淵 {Mathf.Max(0f, endTime - Time.time):0.0}s　累積 {stored:0}", style, MarkColor);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => style = null;
}
