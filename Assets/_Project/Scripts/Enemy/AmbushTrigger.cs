using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 陷阱：玩家走進觸發範圍（紅色線框）時，隱藏的小怪會從天上掉到玩家附近。
/// - 地上會先出現紅圈，標示小怪要落下的位置，紅圈會越來越大
/// - 第一隻直接瞄準玩家當下的位置，其他的落在玩家周圍
/// - 落地時會砸傷附近的玩家，之後就跟一般小怪一樣追著打
/// 玩家死亡重生時，陷阱會重新布置（小怪再次隱藏、可以再觸發一次）。
///
/// 使用方式：把要埋伏的小怪放在這個物件底下並設為隱藏（取消勾選），拖進 Ambushers 清單。
/// </summary>
public class AmbushTrigger : MonoBehaviour
{
    [Tooltip("觸發範圍的大小（以這個物件為中心的方塊）")]
    public Vector3 size = new Vector3(16f, 6f, 10f);
    [Tooltip("觸發時從天上掉下來的小怪（平常會隱藏）")]
    public List<EnemyAI> ambushers = new List<EnemyAI>();

    [Header("落下")]
    [Tooltip("從多高的地方掉下來 (m)")]
    public float dropHeight = 14f;
    [Tooltip("落在玩家周圍多遠 (m，隨機範圍)")]
    public Vector2 dropDistance = new Vector2(2f, 4f);
    [Tooltip("第一隻直接瞄準玩家當下的位置")]
    public bool firstOnPlayer = true;
    [Tooltip("每隻之間間隔多久掉下來 (秒)")]
    public float dropInterval = 0.35f;

    Transform player;
    PlayerHealth playerHealth;
    bool triggered;

    void Awake()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        foreach (var a in ambushers)
            if (a != null) a.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (playerHealth != null) playerHealth.OnRespawned += ResetAmbush;
    }

    void OnDisable()
    {
        if (playerHealth != null) playerHealth.OnRespawned -= ResetAmbush;
    }

    void Update()
    {
        if (triggered || player == null) return;
        if (playerHealth != null && playerHealth.IsDead) return;
        if (!Contains(player.position)) return;

        triggered = true;
        StartCoroutine(Spring());
    }

    IEnumerator Spring()
    {
        for (int i = 0; i < ambushers.Count; i++)
        {
            var a = ambushers[i];
            if (a == null) continue;

            Vector3 p = player.position;
            if (!(i == 0 && firstOnPlayer))
            {
                float angle = Random.Range(0f, 360f);
                float dist = Random.Range(dropDistance.x, dropDistance.y);
                p += Quaternion.Euler(0f, angle, 0f) * Vector3.forward * dist;
            }

            a.gameObject.SetActive(true);
            a.BeginDrop(ClampInside(p), dropHeight);
            yield return new WaitForSeconds(dropInterval);
        }
    }

    /// <summary>玩家重生：小怪重新隱藏，陷阱可以再觸發</summary>
    void ResetAmbush()
    {
        StopAllCoroutines();
        triggered = false;
        foreach (var a in ambushers)
        {
            if (a == null || !a.gameObject.activeSelf) continue;
            a.ResetEnemy();
            a.gameObject.SetActive(false);
        }
    }

    bool Contains(Vector3 worldPos)
    {
        Vector3 l = transform.InverseTransformPoint(worldPos);
        return Mathf.Abs(l.x) <= size.x * 0.5f && Mathf.Abs(l.y) <= size.y * 0.5f && Mathf.Abs(l.z) <= size.z * 0.5f;
    }

    /// <summary>落點限制在觸發範圍內（離邊緣 1m），避免掉進牆裡</summary>
    Vector3 ClampInside(Vector3 worldPos)
    {
        Vector3 l = transform.InverseTransformPoint(worldPos);
        l.x = Mathf.Clamp(l.x, -size.x * 0.5f + 1f, size.x * 0.5f - 1f);
        l.z = Mathf.Clamp(l.z, -size.z * 0.5f + 1f, size.z * 0.5f - 1f);
        return transform.TransformPoint(l);
    }

    void OnDrawGizmos()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(Vector3.zero, size);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.08f);
        Gizmos.DrawCube(Vector3.zero, size);
    }
}
