using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第二關（維京村莊）岸邊那位老者 = 檢查點。
///
/// 玩家走到老者身邊（activateRadius 公尺內）就會：補滿血、把重生點設在老者面前、
/// 畫面中間顯示 CHECKPOINT REACHED、播放音效、地上擴散一圈金色光環，老者身上亮起一盞暖光。
/// 效果跟 Floor02 Boss 房前的石碑檢查點一樣，只是不蓋石碑，老者本身的外型完全不動。
///
/// 不需要手動設定：載入任何場景時，只要場景裡有名為 CalligraphyElder_DockDisplay 的物件，
/// 這個腳本就會自動掛到它身上（再加上 Checkpoint）。
/// 想調整範圍 / 顏色，可以在 Play 時選老者看 Inspector，或直接改下面的預設值。
/// 也可以在編輯器裡手動把這個元件加到老者身上，手動加的會優先使用（不會重複加）。
/// </summary>
[DisallowMultipleComponent]
public class DockElderCheckpoint : MonoBehaviour
{
    public const string ElderObjectName = "CalligraphyElder_DockDisplay";

    [Tooltip("走到離老者多近會觸發 (m)")]
    public float activateRadius = 3.5f;
    [Tooltip("重生點離老者多遠 (m)：會自動找老者周圍有木板地的那一側")]
    public float respawnDistance = 2.2f;
    [Tooltip("啟用時光環 / 燈光的顏色")]
    public Color glowColor = new Color(1f, 0.8f, 0.3f);

    Checkpoint checkpoint;

    // ---------- 自動安裝 ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded; // 關掉 Domain Reload 時不會重複註冊
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name != ElderObjectName) continue;
            if (root.GetComponent<DockElderCheckpoint>() == null) root.AddComponent<DockElderCheckpoint>();
        }
    }

    // ---------- 設定檢查點 ----------

    void Awake()
    {
        checkpoint = GetComponent<Checkpoint>();
        if (checkpoint == null)
        {
            checkpoint = gameObject.AddComponent<Checkpoint>();
            checkpoint.activateRadius = activateRadius;
            checkpoint.activeColor = glowColor;
        }
        checkpoint.buildStoneStele = false; // 不蓋石碑：外型保持原本的老者
        checkpoint.snapToGround = false;    // 老者已經站在木板上，不要移動他
        checkpoint.healOnTouch = true;
        checkpoint.ringOnActivate = true;
        if (checkpoint.respawnPoint == null) checkpoint.respawnPoint = MakeRespawnPoint();
    }

    /// <summary>在老者旁邊找一塊站得住的木板地當重生點（前、後、左、右依序試），找不到就用老者腳下</summary>
    Transform MakeRespawnPoint()
    {
        Physics.SyncTransforms(); // 剛載入場景時，確保碰撞體位置已經更新
        Vector3 origin = transform.position;
        Vector3 fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        Vector3 spot = origin;
        foreach (var dir in new[] { fwd, -fwd, right, -right })
        {
            if (TryGround(origin + dir * respawnDistance, origin.y, out Vector3 p)) { spot = p; break; }
        }

        var rp = new GameObject("ElderRespawnPoint").transform;
        // 稍微離地，CharacterController 才不會卡進木板
        rp.position = spot + Vector3.up * 0.15f;
        // 重生時面向老者
        Vector3 look = Vector3.ProjectOnPlane(origin - spot, Vector3.up);
        rp.rotation = look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : transform.rotation;
        rp.SetParent(transform, true);
        return rp;
    }

    bool TryGround(Vector3 at, float referenceY, out Vector3 point)
    {
        point = at;
        var hits = Physics.RaycastAll(new Vector3(at.x, referenceY + 2.5f, at.z), Vector3.down, 5f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
        {
            if (h.collider is CharacterController || h.collider.transform.IsChildOf(transform)) continue;
            // 只接受跟老者站的地方差不多高的地面（木板），太低的多半是水面或海底
            if (Mathf.Abs(h.point.y - referenceY) > 0.8f) continue;
            if (h.point.y > best) { best = h.point.y; point = h.point; }
        }
        return !float.IsNegativeInfinity(best);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(glowColor.r, glowColor.g, glowColor.b, 0.6f);
        Gizmos.DrawWireSphere(transform.position, activateRadius);
    }
}
