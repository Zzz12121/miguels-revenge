using System.Collections;
using UnityEngine;

/// <summary>
/// 掛在敵人身上：敵人死亡後讓一個物件（例如傳送門）從地面長出來。兩種用法：
///   - Reveal Target：場景裡事先擺好、一開始關著的物件，在它原本的位置出現（固定位置，星球的傳送門用這個）
///   - Prefab：在敵人倒下的地方生成一個新的
/// 兩個都填的話用 Reveal Target。支援 ClockEnemyHealth 和 EnemyHealth。
/// </summary>
public class SpawnOnDeath : MonoBehaviour
{
    [Tooltip("場景裡事先擺好的物件（一開始設成不啟用），敵人死後在原本的位置出現")]
    public GameObject revealTarget;
    [Tooltip("沒有 Reveal Target 時：在敵人倒下的地方生成這個 Prefab")]
    public GameObject prefab;
    [Tooltip("死亡後幾秒才生成（等倒地動畫播完）")]
    public float delay = 2.5f;
    [Tooltip("長出來的時間 (秒)")]
    public float growTime = 1.2f;
    [Tooltip("生成後面向玩家")]
    public bool facePlayer = true;
    public string announceText = "傳送門出現了！";

    ClockEnemyHealth clockHealth;
    EnemyHealth enemyHealth;
    bool triggered; // 只會出現一次

    void Awake()
    {
        clockHealth = GetComponent<ClockEnemyHealth>();
        enemyHealth = GetComponent<EnemyHealth>();
        if (clockHealth != null) clockHealth.OnDied += HandleDied;
        if (enemyHealth != null) enemyHealth.OnDied += HandleDied;
        if (clockHealth == null && enemyHealth == null)
            Debug.LogWarning($"[SpawnOnDeath] {name} 上找不到 ClockEnemyHealth / EnemyHealth，不會生成任何東西。", this);
    }

    void OnDestroy()
    {
        if (clockHealth != null) clockHealth.OnDied -= HandleDied;
        if (enemyHealth != null) enemyHealth.OnDied -= HandleDied;
    }

    void HandleDied()
    {
        if (triggered || (revealTarget == null && prefab == null)) return;
        triggered = true;
        // 協程掛在獨立物件上，敵人之後被刪掉或關掉也不影響
        var runner = new GameObject("SpawnOnDeath_Runner").AddComponent<SpawnRunner>();
        runner.StartCoroutine(runner.Run(this, GroundBelow(transform.position)));
    }

    static Vector3 GroundBelow(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 2f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (var h in hits)
            if (!(h.collider is CharacterController) && h.point.y > best) best = h.point.y;
        if (!float.IsNegativeInfinity(best)) p.y = best;
        return p;
    }

    class SpawnRunner : MonoBehaviour
    {
        public IEnumerator Run(SpawnOnDeath s, Vector3 pos)
        {
            GameObject prefab = s.prefab;
            GameObject reveal = s.revealTarget;
            float growTime = s.growTime;
            bool facePlayer = s.facePlayer;
            string text = s.announceText;
            yield return new WaitForSeconds(s.delay);

            GameObject go;
            if (reveal != null)
            {
                // 固定位置：場景裡擺好的物件直接打開
                go = reveal;
                pos = go.transform.position;
                go.SetActive(true);
            }
            else
            {
                Quaternion rot = Quaternion.identity;
                var player = GameObject.FindWithTag("Player");
                if (facePlayer && player != null)
                {
                    Vector3 d = player.transform.position - pos;
                    d.y = 0f;
                    if (d.sqrMagnitude > 0.01f) rot = Quaternion.LookRotation(d);
                }
                go = Instantiate(prefab, pos, rot);
                go.name = prefab.name;
            }
            GameAudio.Play("SFX/RPG/metalLatch", pos, 1f, 0.6f, 0f, 1f, 80f);
            if (!string.IsNullOrEmpty(text)) VictoryBanner.ShowSmall(text, "", new Color(0.55f, 0.7f, 1f));

            Vector3 full = go.transform.localScale;
            float t = 0f;
            while (t < growTime)
            {
                t += Time.deltaTime;
                go.transform.localScale = full * Mathf.SmoothStep(0f, 1f, t / growTime);
                yield return null;
            }
            go.transform.localScale = full;
            Destroy(gameObject);
        }
    }
}
