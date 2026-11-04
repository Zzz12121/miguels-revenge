using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 百足駭骨特殊技能「蝠群護體」：血量每掉 20%（80%、60%、40%、20% 各一次）自動發動
///   吼叫 → 召喚一群骷髏蝙蝠繞著自己飛 → 期間慢慢回復 15% 最大血量 → 蝙蝠散開
///
/// 護衛蝙蝠碰到玩家會造成小傷害（走 PlayerHealth.TakeDamage，有受傷無敵時間）。
/// 需要同一個物件上有 BossHealth。每個門檻只會觸發一次，回血回到門檻以上也不會重複觸發。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeBatGuardSkill : MonoBehaviour
{
    [Header("觸發")]
    [Tooltip("每掉多少比例血量觸發一次")]
    [Range(0.05f, 0.5f)] public float thresholdStep = 0.2f;
    [Tooltip("每次回復最大血量的比例")]
    [Range(0f, 1f)] public float healFraction = 0.15f;
    [Tooltip("回血花的時間（秒），蝙蝠也繞這麼久")]
    public float healDuration = 4f;
    [Tooltip("回血分幾次")]
    public int healPulses = 6;
    [Tooltip("Roar 動畫開始到召喚蝙蝠的時間，要和動畫的 OnRoar 事件一致")]
    public float summonDelay = 0.55f;

    [Header("蝙蝠")]
    public GameObject batPrefab;
    public int batCount = 12;
    public float batScale = 1.6f;
    public float orbitRadius = 5.5f;
    public float orbitHeight = 2.5f;
    public float orbitDegreesPerSecond = 160f;
    public float contactDamage = 4f;
    public float contactRadius = 1.2f;

    [Header("特效")]
    public GameObject healEffectPrefab;
    public float healEffectScale = 3f;
    public GameObject summonEffectPrefab;
    public float summonEffectScale = 2.5f;

    public bool IsRunning { get; private set; }

    /// <summary>發動時觸發，參數是觸發的門檻（例如 0.8 代表 80%）。</summary>
    public event Action<float> Triggered;
    public event Action Finished;

    SkeletonCentipedeAnimationDriver driver;
    BossHealth health;
    readonly List<SkeletonBat> bats = new();
    readonly Queue<float> pending = new();
    float nextThreshold;
    Transform orbitAnchor;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        health = GetComponent<BossHealth>();
        nextThreshold = 1f - thresholdStep;
        orbitAnchor = new GameObject("BatGuardAnchor").transform;
        orbitAnchor.SetParent(transform, false);
    }

    void OnEnable()
    {
        if (health != null) health.OnDamaged += OnDamaged;
    }

    void OnDisable()
    {
        if (health != null) health.OnDamaged -= OnDamaged;
        StopAllCoroutines();
        Release();
        IsRunning = false;
    }

    /// <summary>重新開始戰鬥時呼叫（例如玩家重試），讓門檻重新計算。</summary>
    public void ResetThresholds()
    {
        nextThreshold = 1f - thresholdStep;
        pending.Clear();
    }

    void OnDamaged(float amount)
    {
        if (health == null || health.IsDead) return;
        float ratio = health.Health / Mathf.Max(1f, health.maxHealth);
        // 一次被打掉很多血可能一口氣跨過好幾個門檻：每個門檻都排一次
        while (nextThreshold > 0.001f && ratio <= nextThreshold)
        {
            pending.Enqueue(nextThreshold);
            nextThreshold -= thresholdStep;
        }
        if (!IsRunning && pending.Count > 0) StartCoroutine(Run());
    }

    void Update()
    {
        if (!IsRunning || bats.Count == 0) return;
        var player = PlayerHealth.Find();
        if (player == null || player.IsDead || contactDamage <= 0f) return;
        Vector3 chest = player.transform.position + Vector3.up;
        foreach (var bat in bats)
        {
            if (bat == null) continue;
            if ((bat.transform.position - chest).sqrMagnitude > contactRadius * contactRadius) continue;
            Vector3 push = bat.transform.position - transform.position;
            push.y = 0f;
            player.TakeDamage(contactDamage, push.normalized * 4f + Vector3.up * 2f, false, "被骷髏蝙蝠群咬死");
            break;
        }
    }

    IEnumerator Run()
    {
        IsRunning = true;
        while (pending.Count > 0)
        {
            float threshold = pending.Dequeue();
            if (driver.IsDead) break;
            Triggered?.Invoke(threshold);

            driver.PlayRoar();
            yield return new WaitForSeconds(summonDelay);
            if (driver.IsDead) break;

            orbitAnchor.position = transform.position;
            Spawn(summonEffectPrefab, transform.position, summonEffectScale, null);
            SummonBats();
            var heal = Spawn(healEffectPrefab, transform.position, healEffectScale, transform);

            // 分幾次脈衝回血（BossHealth.SetHealth 每次會閃一下白，剛好當作回血的視覺提示）
            float total = health != null ? health.maxHealth * healFraction : 0f;
            int pulses = Mathf.Max(1, healPulses);
            for (int pulse = 0; pulse < pulses; pulse++)
            {
                yield return new WaitForSeconds(healDuration / pulses);
                if (driver.IsDead || health == null || health.IsDead) break;
                health.SetHealth(health.Health + total / pulses);
            }
            if (heal != null) Destroy(heal, 0.5f);
            Release();
        }
        IsRunning = false;
        Finished?.Invoke();
    }

    void SummonBats()
    {
        Release();
        for (int i = 0; i < batCount; i++)
        {
            float angle = i * 360f / batCount;
            float rad = angle * Mathf.Deg2Rad;
            // 從地面往上竄出，再進入軌道
            Vector3 from = transform.position + new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * orbitRadius * 0.4f;
            var bat = SkeletonBat.Spawn(batPrefab, from, Quaternion.LookRotation(Vector3.up + Vector3.forward * 0.01f), batScale * UnityEngine.Random.Range(0.85f, 1.15f));
            if (bat == null) return;
            // 分兩層、正反方向繞，看起來比較像一團蝠群
            bool upper = i % 2 == 0;
            bat.StartOrbit(orbitAnchor, orbitRadius * (upper ? 1f : 0.8f), orbitHeight * (upper ? 1.3f : 0.7f),
                orbitDegreesPerSecond * (upper ? 1f : -0.8f), angle);
            bats.Add(bat);
        }
    }

    void LateUpdate()
    {
        if (orbitAnchor != null) orbitAnchor.position = transform.position;
    }

    void Release()
    {
        foreach (var bat in bats) if (bat != null) bat.Disperse(transform.position);
        bats.Clear();
    }

    GameObject Spawn(GameObject prefab, Vector3 position, float scale, Transform parent)
    {
        if (prefab == null) return null;
        var fx = Instantiate(prefab, position, Quaternion.identity, parent);
        fx.transform.localScale = Vector3.one * scale / (parent != null ? Mathf.Max(0.0001f, parent.lossyScale.x) : 1f);
        Destroy(fx, healDuration + 2f);
        return fx;
    }
}
