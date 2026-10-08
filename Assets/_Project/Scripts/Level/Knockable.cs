using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 場景裡「被打到會有反應」的擺設（木桶、木箱、火把…）的共同基底。
///
/// - 玩家的攻擊只認 IDamageable，所以實作它之後，玩家揮劍掃到就會自動呼叫 TakeDamage，不用改玩家的程式
/// - 小怪不會特地去打擺設，但揮劍時掃到一樣會打翻（EnemyCombat 直接呼叫 Knock）
/// - 玩家或小怪用走的頂到它：ThirdPersonController 會呼叫 Shove
///
/// 實際要怎麼反應寫在子類別：KnockableProp（木桶 / 木箱：被打飛、翻倒）、KnockableTorch（火把：掉到地上熄滅，之後可以踢來踢去）。
/// </summary>
public abstract class Knockable : MonoBehaviour, IDamageable
{
    static Transform player;
    static readonly Collider[] nearby = new Collider[64];

    readonly List<Collider> ignoredCharacters = new List<Collider>();
    Collider ignoringCollider;
    float ignoreUntil, nudgeAfter, giveUpAfter;

    /// <summary>true = 攻擊會直接忽略它</summary>
    public abstract bool IsDead { get; }

    /// <summary>被打到。attackerPosition = 打它的人站在哪裡（用來決定往哪邊飛），damage = 這一下的傷害（越重飛越遠）</summary>
    public abstract void Knock(Vector3 attackerPosition, float damage);

    /// <summary>
    /// 有人用走的頂到它。pusherPosition = 那個人的位置，moveVelocity = 他正在走的速度，
    /// hitNormal = 碰到的那一面朝哪裡（朝上 = 他是踩在上面），topSpeed = 他全速跑的速度。
    /// 回傳「推的人接下來要慢成幾倍」（1 = 不用變慢）。預設沒有反應。
    /// </summary>
    public virtual float Shove(Vector3 pusherPosition, Vector3 moveVelocity, Vector3 hitNormal, float topSpeed) => 1f;

    /// <summary>玩家的攻擊會走這裡（IDamageable 不會告訴我們是誰打的，所以直接當成玩家）</summary>
    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go != null) player = go.transform;
        }
        Knock(player != null ? player.position : hitPoint, amount);
    }

    /// <summary>
    /// 接下來 seconds 秒，own 這個碰撞體不跟 center 附近 radius 公尺內的角色（玩家、小怪）互相碰撞。
    ///
    /// 為什麼需要：打下去的同時角色會往前踏一步，剛好撞上才要開始飛的東西，兩邊互相擠來擠去，
    /// 角色和鏡頭會被推得一頓一頓的。讓它先飛離開，再恢復碰撞就不會了。
    /// </summary>
    protected void IgnoreCharactersFor(Collider own, Vector3 center, float radius, float seconds)
    {
        if (!Usable(own)) return;
        RestoreCharacterCollisions();
        ignoringCollider = own;
        int n = Physics.OverlapSphereNonAlloc(center, radius, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            if (!(nearby[i] is CharacterController)) continue;
            Physics.IgnoreCollision(own, nearby[i], true);
            ignoredCharacters.Add(nearby[i]);
        }
        ignoreUntil = Time.time + seconds;
        nudgeAfter = ignoreUntil + 0.6f;
        giveUpAfter = ignoreUntil + 6f;
    }

    void RestoreCharacterCollisions()
    {
        if (!Usable(ignoringCollider)) { ignoredCharacters.Clear(); return; }
        for (int i = ignoredCharacters.Count - 1; i >= 0; i--)
        {
            var c = ignoredCharacters[i];
            if (c == null) { ignoredCharacters.RemoveAt(i); continue; }
            // 對方暫時被關掉（例如正在被傳送）：這時候還原會失敗，留著等他回來再還原
            if (!Usable(c)) continue;
            Physics.IgnoreCollision(ignoringCollider, c, false);
            ignoredCharacters.RemoveAt(i);
        }
    }

    static bool Usable(Collider c) => c != null && c.enabled && c.gameObject.activeInHierarchy;

    protected virtual void Update()
    {
        // 被打出場景外（理論上不會，四周都有牆）就不用留著了
        if (transform.position.y < -40f) { Destroy(gameObject); return; }

        if (ignoredCharacters.Count == 0 || Time.time < ignoreUntil) return;

        // 時間到了，但還跟某個角色疊在一起：現在恢復碰撞的話他會被瞬間擠開。
        // 先等；等了一陣子還疊著就把東西往外推開；真的分不開（6 秒）才硬恢復
        if (Time.time < giveUpAfter && Usable(ignoringCollider))
        {
            Bounds mine = ignoringCollider.bounds;
            foreach (var c in ignoredCharacters)
            {
                if (!Usable(c) || !mine.Intersects(c.bounds)) continue;
                if (Time.time >= nudgeAfter) NudgeAwayFrom(c);
                ignoreUntil = Time.time + 0.1f;
                return;
            }
        }
        RestoreCharacterCollisions();
    }

    void NudgeAwayFrom(Collider character)
    {
        Rigidbody rb = ignoringCollider.attachedRigidbody;
        if (rb == null || rb.isKinematic) return;
        Vector3 away = rb.worldCenterOfMass - character.bounds.center;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere;
        away.y = 0f;
        away.Normalize();
        rb.WakeUp();
        Vector3 v = rb.linearVelocity;
        float have = Vector3.Dot(v, away);
        if (have < 2.5f) rb.linearVelocity = v + away * (2.5f - have);
    }
}
