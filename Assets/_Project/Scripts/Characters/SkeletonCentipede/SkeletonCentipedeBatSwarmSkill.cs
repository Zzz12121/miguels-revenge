using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 百足駭骨技能「蝠群襲擊」：吼叫（Roar 動畫）→ 身後地面竄出一大群骷髏蝙蝠，先在頭頂盤旋
/// → 一隻接一隻俯衝撲向玩家，碰到玩家造成傷害後散開。
///
/// 傷害走 PlayerHealth.TakeDamage（有受傷無敵時間，所以一整群撞上去不會瞬間秒殺；翻滾可以全部躲掉）。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeBatSwarmSkill : MonoBehaviour
{
    [Header("時間")]
    [Tooltip("Roar 動畫開始到蝙蝠竄出的時間，要和動畫的 OnRoar 事件一致")]
    public float summonDelay = 0.55f;
    [Tooltip("蝙蝠在頭頂盤旋多久才開始俯衝")]
    public float circleTime = 1.2f;
    [Tooltip("每隻蝙蝠俯衝的間隔")]
    public float diveInterval = 0.09f;

    [Header("蝙蝠")]
    public GameObject batPrefab;
    public int batCount = 24;
    public float batScale = 1.6f;
    public float circleRadius = 7f;
    public float circleHeight = 7f;
    public float diveSpeed = 24f;
    public float damagePerBat = 6f;
    public float hitDistance = 1.3f;

    [Header("特效")]
    public GameObject summonEffectPrefab;
    public float summonEffectScale = 3.5f;
    public GameObject batHitEffectPrefab;
    public float batHitEffectScale = 1.2f;

    public bool IsRunning { get; private set; }
    public event Action Started;
    /// <summary>有蝙蝠打中玩家（參數：打中的位置、這下有沒有真的扣到血）。</summary>
    public event Action<Vector3, bool> BatHit;
    public event Action Finished;

    SkeletonCentipedeAnimationDriver driver;
    readonly List<SkeletonBat> bats = new();
    Transform circleAnchor;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        circleAnchor = new GameObject("BatSwarmAnchor").transform;
        circleAnchor.SetParent(transform, false);
    }

    void OnDisable()
    {
        StopAllCoroutines();
        foreach (var bat in bats) if (bat != null) bat.Disperse(transform.position);
        bats.Clear();
        IsRunning = false;
    }

    public bool TryStart()
    {
        if (IsRunning || driver.IsDead) return false;
        StartCoroutine(Run());
        return true;
    }

    IEnumerator Run()
    {
        IsRunning = true;
        Started?.Invoke();
        driver.PlayRoar();
        yield return new WaitForSeconds(summonDelay);
        if (driver.IsDead) { Finish(); yield break; }

        // 從身後的地面竄出來，往頭頂盤旋
        Vector3 behind = transform.position - transform.forward * circleRadius * 0.6f;
        Spawn(summonEffectPrefab, behind, summonEffectScale);
        circleAnchor.position = transform.position;
        for (int i = 0; i < batCount; i++)
        {
            float angle = i * 360f / batCount + UnityEngine.Random.Range(-8f, 8f);
            Vector3 from = behind + UnityEngine.Random.insideUnitSphere * 1.5f;
            from.y = transform.position.y + 0.5f;
            var bat = SkeletonBat.Spawn(batPrefab, from, Quaternion.LookRotation(Vector3.up + transform.forward * 0.3f),
                batScale * UnityEngine.Random.Range(0.8f, 1.2f));
            if (bat == null) break;
            bat.StartOrbit(circleAnchor, circleRadius * UnityEngine.Random.Range(0.6f, 1.1f), circleHeight * UnityEngine.Random.Range(0.75f, 1.2f),
                UnityEngine.Random.Range(150f, 220f) * (i % 3 == 0 ? -1f : 1f), angle);
            bats.Add(bat);
            if (i % 4 == 3) yield return null; // 分幾格生，避免一格卡頓
        }

        yield return new WaitForSeconds(circleTime);

        // 一隻接一隻俯衝
        var player = PlayerHealth.Find();
        for (int i = 0; i < bats.Count; i++)
        {
            if (driver.IsDead) break;
            var bat = bats[i];
            if (bat == null) continue;
            if (player == null || player.IsDead)
            {
                bat.Disperse(transform.position);
                continue;
            }
            Vector3 aim = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), UnityEngine.Random.Range(0.6f, 1.4f), UnityEngine.Random.Range(-0.6f, 0.6f));
            bat.StartDive(player.transform, aim, diveSpeed * UnityEngine.Random.Range(0.9f, 1.15f), hitDistance, b => OnBatReached(b, player));
            yield return new WaitForSeconds(diveInterval);
        }

        // 等最後一批飛完；追不到的（玩家一直跑）時間到就散開
        yield return new WaitForSeconds(2.5f);
        Finish();
    }

    void OnBatReached(SkeletonBat bat, PlayerHealth player)
    {
        if (player == null || player.IsDead) return;
        Vector3 at = bat.transform.position;
        Vector3 push = player.transform.position - transform.position;
        push.y = 0f;
        bool hurt = player.TakeDamage(damagePerBat, push.normalized * 3f + Vector3.up * 1.5f, false, "被骷髏蝙蝠群吞噬");
        Spawn(batHitEffectPrefab, at, batHitEffectScale);
        BatHit?.Invoke(at, hurt);
    }

    void LateUpdate()
    {
        if (circleAnchor != null) circleAnchor.position = transform.position;
    }

    void Finish()
    {
        foreach (var bat in bats) if (bat != null) bat.Disperse(transform.position);
        bats.Clear();
        IsRunning = false;
        Finished?.Invoke();
    }

    void Spawn(GameObject prefab, Vector3 position, float scale)
    {
        if (prefab == null) return;
        var fx = Instantiate(prefab, position, Quaternion.identity);
        fx.transform.localScale = Vector3.one * scale;
        Destroy(fx, 4f);
    }
}
