using System.Collections;
using UnityEngine;

/// <summary>
/// 敵艦上的船員（弓箭手）：可以被玩家的箭射中。被射中 hitsToKill 次就倒下、掉進海裡，
/// 一艘船的船員全部倒下，船就會沉（EnemyBoat.OnCrewKilled）。射到船身沒有效果。
/// EnemyBoat 生成船員時自動加上（含一個膠囊碰撞體）。
/// </summary>
public class BoatCrewMember : MonoBehaviour, IDamageable
{
    [HideInInspector] public EnemyBoat boat;
    [Tooltip("被射中幾次會倒下")]
    public int hitsToKill = 1;

    int hits;
    public bool IsDead { get; private set; }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || amount <= 0f) return;
        hits++;
        GameAudio.Play("SFX/RPG/cloth2", hitPoint, 0.9f, Random.Range(0.9f, 1.1f), 0f, 1f, 50f);
        CombatVFX.Impact(hitPoint, new Color(1f, 0.35f, 0.3f));
        if (hits >= hitsToKill) Die();
    }

    void Die()
    {
        IsDead = true;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
        // 停在中箭當下的姿勢往後倒（弓箭包裡沒有死亡動畫）
        var anim = GetComponentInChildren<Animator>();
        if (anim != null) anim.enabled = false;
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 1f, 0.9f, 0.05f, 1f, 50f);
        if (boat != null) boat.OnCrewKilled(this);
        StartCoroutine(FallOverboard());
    }

    IEnumerator FallOverboard()
    {
        Quaternion from = transform.localRotation;
        Quaternion to = from * Quaternion.Euler(-85f, 0f, 0f);
        float t = 0f;
        while (t < 0.45f)
        {
            t += Time.deltaTime;
            transform.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / 0.45f));
            yield return null;
        }
        yield return new WaitForSeconds(0.6f);

        // 滑下船、沉進海裡
        Vector3 p = transform.position;
        t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime;
            transform.position = p + Vector3.down * (t * 2.5f);
            yield return null;
        }
        gameObject.SetActive(false);
    }
}
