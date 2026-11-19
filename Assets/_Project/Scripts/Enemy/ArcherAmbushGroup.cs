using UnityEngine;

/// <summary>
/// 往吊橋山路上的弓箭手埋伏。
/// 條件：老頭逃走演出（DojoElderEscape）已經結束（場景裡沒有老頭演出就不檢查），
///       而且玩家走進觸發範圍（triggerPoint 附近，或任何一名弓箭手的 alertRadius 內）。
/// 觸發後弓箭手一個接一個從掩體後閃身出來射箭。玩家死掉時全部縮回掩體，重生後再走過來會重新埋伏。
/// 已經被打倒的弓箭手不會復活。
/// 投降：其他弓箭手都被打倒、只剩最後一名時，他會走出來跪下把弓交給玩家（玩家還沒有弓箭技能時才會）。
/// </summary>
public class ArcherAmbushGroup : MonoBehaviour
{
    public AmbushArcher[] archers;
    [Tooltip("玩家走到這附近就觸發（世界座標）")]
    public Vector3 triggerPoint;
    public float triggerRadius = 9f;
    [Tooltip("玩家離任何一名弓箭手這麼近也會觸發")]
    public float alertRadius = 16f;
    [Tooltip("每名弓箭手閃身出來的間隔（秒）")]
    public float staggerSeconds = 0.35f;
    [Tooltip("只在老頭逃走之後才埋伏")]
    public bool requireElderEscape = true;
    [Tooltip("只剩最後一名弓箭手時，他會投降並把弓交給玩家（獲得弓箭技能）")]
    public bool lastArcherSurrenders = true;

    public bool Triggered { get; private set; }
    public bool SurrenderHappened { get; private set; }

    DojoElderEscape escape;
    PlayerHealth player;
    bool searchedEscape;

    void Update()
    {
        if (player == null) { player = PlayerHealth.Find(); if (player == null) return; }

        if (player.IsDead)
        {
            if (Triggered)
            {
                Triggered = false;
                foreach (var a in archers) if (a != null) a.Disengage();
            }
            return;
        }
        if (Triggered) { CheckSurrender(); return; }

        if (requireElderEscape)
        {
            if (!searchedEscape) { escape = FindAnyObjectByType<DojoElderEscape>(FindObjectsInactive.Include); searchedEscape = true; }
            if (escape != null && !escape.Completed) return;
        }

        Vector3 p = player.transform.position;
        bool near = (p - triggerPoint).sqrMagnitude <= triggerRadius * triggerRadius;
        if (!near)
            foreach (var a in archers)
                if (a != null && !a.IsDead && (a.transform.position - p).sqrMagnitude <= alertRadius * alertRadius) { near = true; break; }
        if (!near) return;

        Triggered = true;
        GameAudio.Play("SFX/RPG/drawKnife3", p, 0.6f, 0.7f, 0.05f, 1f, 40f);
        ThirdPersonCamera.ShakeMain(0.08f, 0.2f);
        float delay = 0.2f;
        foreach (var a in archers)
        {
            if (a == null || a.IsDead) continue;
            a.Engage(delay);
            delay += staggerSeconds;
        }
    }

    /// <summary>只剩一名還活著（其他都倒下了）→ 那一名投降</summary>
    void CheckSurrender()
    {
        if (!lastArcherSurrenders || SurrenderHappened || PlayerLoadout.BowUnlocked || archers == null || archers.Length < 2) return;
        AmbushArcher last = null;
        int alive = 0;
        foreach (var a in archers)
        {
            if (a == null || a.IsDead) continue;
            alive++;
            last = a;
        }
        if (alive != 1) return;
        SurrenderHappened = true;
        last.Surrender();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
        Gizmos.DrawWireSphere(triggerPoint, triggerRadius);
    }
}
