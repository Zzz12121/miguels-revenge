using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 預覽專用：按數字鍵 5 依序切換到下一個動作 / 技能，按 6 回到上一個（測試場景的滑鼠會被玩家鎖住，所以用鍵盤；1~4 是玩家技能鍵）。
/// 每次切換前會先讓百足駭骨回到起點（死了就先復活），所以一直按 5 就能從頭到尾看完全部動作。
///   ' 扣 Boss 10% 血（每掉 20% 自動觸發蝠群護體回血）   \ 回到起點
/// 游標沒被鎖住時也可以直接按畫面上的按鈕。只放在預覽 / 測試場景裡，不要掛到正式 Boss 上。
///
/// 衝刺的位移在正式遊戲裡是 Boss AI 的工作；這裡只是預覽用的簡易位移（DashStart ~ DashEnd 之間往前推）。
/// </summary>
[RequireComponent(typeof(SkeletonCentipedeAnimationDriver))]
public class SkeletonCentipedeAnimationPreview : MonoBehaviour
{
    SkeletonCentipedeAnimationDriver driver;
    float crawlSpeed;
    string lastEvent = "-";

    [Tooltip("預覽用衝刺距離（身長的倍數）")]
    [SerializeField] float dashDistanceInBodyLengths = 2.5f;
    [Tooltip("預覽用衝刺持續時間，要和動畫的 DashStart ~ DashEnd 一致")]
    [SerializeField] float dashDuration = 0.78f;

    Vector3 homePosition;
    Quaternion homeRotation;
    float bodyLength = 5f;
    Vector3 dashDirection;
    bool dashing;
    BossHealth health;

    (string label, System.Action play)[] steps;
    int current = -1;

    void Awake()
    {
        driver = GetComponent<SkeletonCentipedeAnimationDriver>();
        driver.AttackImpact += attack => lastEvent = $"AttackImpact({attack}) @ {Time.time:0.00}s";
        driver.DeathFinished += () => lastEvent = $"DeathFinished @ {Time.time:0.00}s";
        driver.TailSwing += side => lastEvent = $"TailSwing({side}) @ {Time.time:0.00}s";
        driver.DashStart += BeginDash;
        driver.DashEnd += () => dashing = false;
        var encircle = GetComponent<SkeletonCentipedeEncircleSkill>();
        if (encircle != null)
            encircle.PounceLanded += (at, hit) => lastEvent = hit ? $"撲擊命中玩家 @ {Time.time:0.00}s" : $"撲擊落空 @ {Time.time:0.00}s";

        health = GetComponent<BossHealth>();
        if (health != null)
        {
            health.ShowBar = true;
            health.OnDied += () => driver.PlayDeath();
        }
        var guard = GetComponent<SkeletonCentipedeBatGuardSkill>();
        if (guard != null) guard.Triggered += threshold => lastEvent = $"蝠群護體（血量 {threshold:P0}）@ {Time.time:0.00}s";
        var swarm = GetComponent<SkeletonCentipedeBatSwarmSkill>();
        if (swarm != null) swarm.BatHit += (at, hurt) => lastEvent = hurt ? $"蝙蝠咬到玩家 @ {Time.time:0.00}s" : $"蝙蝠被閃掉 @ {Time.time:0.00}s";

        steps = new (string, System.Action)[]
        {
            ("待機 Idle", () => SetCrawl(0f)),
            ("爬行 Crawl", () => SetCrawl(0.5f)),
            ("揮砍前肢 Slash", () => driver.PlaySlash()),
            ("連續揮砍 Sweep Combo", () => driver.PlaySweepCombo()),
            ("甩尾（往左）Tail Whip L", () => driver.PlayTailWhip(SkeletonCentipedeAnimationDriver.TailWhipSide.Left)),
            ("甩尾（往右）Tail Whip R", () => driver.PlayTailWhip(SkeletonCentipedeAnimationDriver.TailWhipSide.Right)),
            ("遠程 Ranged", () => FireRanged()),
            ("衝刺 Dash", () => StartDash()),
            ("撲咬 Bite", () => driver.PlayBite()),
            ("殘影圍殺 Encircle", () => StartEncircle()),
            ("蝠群襲擊 Bat Swarm", () => StartBatSwarm()),
            ("受擊 Hit", () => driver.PlayHit()),
            ("死亡 Death", () => driver.PlayDeath()),
        };

        homePosition = transform.position;
        homeRotation = transform.rotation;
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            bodyLength = Mathf.Max(b.size.x, b.size.z);
        }
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit5Key.wasPressedThisFrame) PlayStep(current + 1);
        if (kb.digit6Key.wasPressedThisFrame) PlayStep(current - 1);
        if (kb.backslashKey.wasPressedThisFrame) ResetPosition();
        if (kb.quoteKey.wasPressedThisFrame) DamageSelf();

        if (dashing) MoveDash();
    }

    /// <summary>切到第 index 個動作（超過尾端就回到第一個）。先把狀態清乾淨：復活、停止爬行、回到起點。</summary>
    void PlayStep(int index)
    {
        current = ((index % steps.Length) + steps.Length) % steps.Length;
        if (driver.IsDead) Revive();
        SetCrawl(0f);
        ResetPosition();
        steps[current].play();
        lastEvent = $"播放：{steps[current].label}";
    }

    void StartDash()
    {
        // 先轉向玩家再衝
        var player = FindPlayer();
        if (player != null)
        {
            Vector3 to = player.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(to);
        }
        driver.PlayDash();
    }

    void BeginDash()
    {
        lastEvent = $"DashStart @ {Time.time:0.00}s";
        dashDirection = transform.forward;
        dashDirection.y = 0f;
        dashDirection.Normalize();
        dashing = true;
    }

    void MoveDash()
    {
        float speed = bodyLength * dashDistanceInBodyLengths / Mathf.Max(0.1f, dashDuration);
        Vector3 step = dashDirection * speed * Time.deltaTime;
        // 前方有東西（牆、玩家）就停下，不穿過去
        Vector3 chest = transform.position + Vector3.up * bodyLength * 0.15f;
        foreach (var hit in Physics.RaycastAll(chest, dashDirection, step.magnitude + bodyLength * 0.35f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            dashing = false;
            return;
        }
        transform.position += step;
    }

    void StartEncircle()
    {
        var skill = GetComponent<SkeletonCentipedeEncircleSkill>();
        if (skill == null) { lastEvent = "沒有 SkeletonCentipedeEncircleSkill"; return; }
        if (skill.TryStart()) lastEvent = $"殘影圍殺開始 @ {Time.time:0.00}s";
    }

    void StartBatSwarm()
    {
        var skill = GetComponent<SkeletonCentipedeBatSwarmSkill>();
        if (skill == null) { lastEvent = "沒有 SkeletonCentipedeBatSwarmSkill"; return; }
        if (skill.TryStart()) lastEvent = $"蝠群襲擊開始 @ {Time.time:0.00}s";
    }

    void DamageSelf()
    {
        if (health == null) { lastEvent = "沒有 BossHealth"; return; }
        health.TakeDamage(health.maxHealth * 0.1f, transform.position);
    }

    void ResetPosition()
    {
        var skill = GetComponent<SkeletonCentipedeEncircleSkill>();
        if (skill != null) skill.Stop();
        dashing = false;
        transform.SetPositionAndRotation(homePosition, homeRotation);
    }

    static GameObject FindPlayer()
    {
        try { return GameObject.FindWithTag("Player"); }
        catch (UnityException) { return null; }
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(12, Screen.height * 0.1f, 260, 560), GUI.skin.box);
        GUILayout.Label("百足駭骨 動畫預覽");
        string next = steps[(current + 1) % steps.Length].label;
        GUILayout.Label($"按 5 → 下一個：{next}\n按 6 → 上一個");

        for (int i = 0; i < steps.Length; i++)
        {
            string mark = i == current ? "▶ " : "　";
            if (GUILayout.Button($"{mark}{i + 1}. {steps[i].label}")) PlayStep(i);
        }

        GUILayout.Space(4);
        if (GUILayout.Button(health != null ? $"' 扣 10% 血（目前 {health.Health / health.maxHealth:P0}）" : "' 扣血（沒有 BossHealth）")) DamageSelf();
        if (GUILayout.Button("\\ 回到起點")) ResetPosition();

        GUILayout.Space(6);
        GUILayout.Label($"事件：{lastEvent}");
        GUILayout.EndArea();
    }

    /// <summary>預覽時瞄準玩家（找 Player 標籤），找不到就往正前方 20 公尺。</summary>
    void FireRanged()
    {
        var player = FindPlayer();
        Vector3 target = player != null
            ? player.transform.position + Vector3.up * 1f
            : transform.position + transform.forward * 20f;
        driver.PlayRanged(target);
    }

    void Revive()
    {
        driver.Revive();
        if (health != null) health.ResetHealth();
        var guard = GetComponent<SkeletonCentipedeBatGuardSkill>();
        if (guard != null) guard.ResetThresholds();
    }

    void SetCrawl(float speed)
    {
        crawlSpeed = speed;
        driver.SetMoveSpeed(speed);
    }
}
