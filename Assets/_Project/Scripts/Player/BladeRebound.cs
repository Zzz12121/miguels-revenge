using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 刀、長槍、小刀砍到石頭、木頭、牆壁（不是敵人、不是地面）：刀被彈回來（PlayerLoadout.Awake 自動掛上）。
///   碰到的那一刻頓一下（stopTime），刀沿著原本的刀路彈回去、退回架式（reboundTime）；人站在原地（不把人往後推：嚴禁滑步）；
///   石頭：火花 + 清脆的「鏘」（blade_stone_*），木頭：木屑 + 悶悶的「咚」（blade_wood_*）；鏡頭、手把震一下。
///   這一刀就斷在那裡（MeleeCombat.CancelAttack：不會穿過牆打到後面的東西），彈回去之後可以馬上再出招。
/// 判斷用刀實際的位置：PlayerWeaponPresenter 的刀柄 → 刀尖這一段，加上刀尖這一幀掃過的路徑。只看一般連擊、空中輕砍「揮下去」的那一段；
/// 這一刀已經砍到敵人就不彈。不算：地面（朝上、在腳邊的面）、地形（除了樹幹）、敵人、村民、會被推動的東西、看不見的擋牆。
/// 彈回去的動作：揮刀的姿勢、動畫都由 PlayerVisualAnimator 照「開始揮刀的時間」算，這裡把那個時間往回推，
/// 刀就沿著原路倒退回抬手前（不改它的程式，只調那個欄位；找不到欄位就只有頓一下、聲音、火花和震退）。
/// </summary>
[DefaultExecutionOrder(32150)]
[DisallowMultipleComponent]
public sealed class BladeRebound : MonoBehaviour
{
    [Tooltip("碰到的那一刻頓多久（實際秒數）")]
    public float stopTime = 0.08f;
    [Tooltip("刀彈回架式要多久（秒）")]
    public float reboundTime = 0.24f;

    /// <summary>正在被彈回去（AttackFlow 等它彈完才接緩衝的攻擊）</summary>
    public static bool Busy { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Busy = false;

    static readonly string[] Stone = { "SFX/Gen/blade_stone_1", "SFX/Gen/blade_stone_2" };
    static readonly string[] Wood = { "SFX/Gen/blade_wood_1", "SFX/Gen/blade_wood_2" };
    static readonly string[] WoodWords = { "wood", "plank", "beam", "bamboo", "door", "barrel", "crate", "box", "log", "tree", "trunk", "table",
                                           "bench", "shoji", "lattice", "rack", "pier", "platform", "deck", "house", "residence", "shrine", "tea",
                                           "stall", "cart", "boat", "torii", "cedar", "paper", "panel", "pillar", "column", "post", "dojo", "fence" };
    static readonly string[] StoneWords = { "stone", "rock", "foundation", "boulder", "cliff", "wall", "step", "jar", "well", "lantern", "slab", "statue" };
    static readonly RaycastHit[] hits = new RaycastHit[16];
    static readonly FieldInfo SlashStartedAt = typeof(PlayerVisualAnimator).GetField("slashStartedAt", BindingFlags.Instance | BindingFlags.NonPublic);

    PlayerCombat combat;
    ThirdPersonController motor;
    PlayerWeaponPresenter presenter;
    PlayerVisualAnimator visual;
    float swingAt = -1f;
    int index;
    bool air, struckEnemy, rebounded;
    Vector3 prevTip;
    bool havePrev;
    // 彈回去：從碰到時的揮刀進度（rewindFrom 秒）倒退到 0
    bool rewinding;
    float rewindFrom, rewindStart;

    void Awake()
    {
        combat = GetComponent<PlayerCombat>();
        motor = GetComponent<ThirdPersonController>();
        if (combat == null) { enabled = false; return; }
        combat.OnSwing += OnSwing;
        combat.OnDealtHit += OnDealtHit;
    }

    void OnDestroy()
    {
        if (combat != null) { combat.OnSwing -= OnSwing; combat.OnDealtHit -= OnDealtHit; }
        Busy = false;
    }

    void OnSwing(int i)
    {
        // 新的一刀開始：不再倒退（玩家直接接下一刀）
        rewinding = false;
        Busy = false;
        if (combat.IsDashSlash || combat.IsPlunging) { swingAt = -1f; return; }
        swingAt = Time.time;
        index = i;
        air = combat.IsAirSlash;
        struckEnemy = rebounded = false;
        havePrev = false;
    }

    void OnDealtHit(float damage, IDamageable target)
    {
        if (target != null) struckEnemy = true;   // 砍到肉了：這一刀不彈
    }

    void Update()
    {
        if (!rewinding) return;
        if (visual == null || SlashStartedAt == null) { rewinding = false; Busy = false; return; }
        float u = Mathf.Clamp01((Time.unscaledTime - rewindStart) / Mathf.Max(0.05f, reboundTime));
        if (u >= 1f)
        {
            // 退回架式了：結束這一刀（揮刀動畫自己混回走 / 跑 / 待機）
            SlashStartedAt.SetValue(visual, float.NegativeInfinity);
            rewinding = false;
            Busy = false;
            return;
        }
        // 一開始彈得快、越退越慢（像被彈開的反作用力）
        float e = 1f - (1f - u) * (1f - u) * (1f - u);
        float age = Mathf.Lerp(rewindFrom, 0.01f, e);
        SlashStartedAt.SetValue(visual, Time.time - age);
    }

    void LateUpdate()
    {
        if (swingAt < 0f || rebounded || struckEnemy || combat == null || !combat.IsAttacking || combat.IsPlunging || combat.IsDashSlash) { havePrev = false; return; }
        if (presenter == null) presenter = GetComponentInChildren<PlayerWeaponPresenter>(true);
        if (presenter == null || presenter.BladeTip == null || presenter.BladePivot == null) return;
        float t = Time.time - swingAt;
        float speed = Mathf.Max(0.1f, combat.AttackSpeed);   // 空中刀的時間也照攻擊速度縮短
        float wu = air ? combat.airSlashWindup / speed : combat.WindupFor(index);
        float sw = air ? combat.airSlashSwing / speed : combat.SwingFor(index);
        if (t < wu || t > wu + sw) { havePrev = false; return; }
        Vector3 tip = presenter.BladeTip.position, grip = presenter.BladePivot.position;
        if (Solid(grip, tip, out var hit) || (havePrev && Solid(prevTip, tip, out hit))) Rebound(hit, t);
        prevTip = tip;
        havePrev = true;
    }

    // ---------------------------------------------------------------- 判斷

    /// <summary>a → b 這一段有沒有碰到會把刀彈開的東西（最近的那個）</summary>
    bool Solid(Vector3 a, Vector3 b, out RaycastHit best)
    {
        best = default;
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 0.01f) return false;
        int n = Physics.RaycastNonAlloc(a, d / len, hits, len, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue, feet = transform.position.y;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = hits[i];
            var c = h.collider;
            if (c == null || h.distance <= 0f || c.transform.IsChildOf(transform)) continue;
            if (c is TerrainCollider)
            {
                // 地形：只有樹幹算（打到的點比那裡的地面高很多）
                var terrain = c.GetComponent<Terrain>();
                if (terrain == null || h.point.y < terrain.SampleHeight(h.point) + terrain.transform.position.y + 0.4f) continue;
            }
            else
            {
                if (h.normal.y > 0.65f && h.point.y < feet + 0.5f) continue;                       // 地面、台階、踏石的頂
                if (c.GetComponentInParent<IDamageable>() != null) continue;                         // 敵人（砍到肉另外算）
                if (c.GetComponentInParent<MerchantNPC>() != null || c.GetComponentInParent<NpcRoutine>() != null) continue; // 村民
                var rb = c.attachedRigidbody;
                if (rb != null && !rb.isKinematic) continue;                                          // 會被推動的東西
                if (!Visible(c, h.point)) continue;                                                   // 看不見的擋牆
            }
            if (h.distance < bestDist) { bestDist = h.distance; best = h; found = true; }
        }
        return found;
    }

    /// <summary>碰到的地方附近有看得到的模型（自己或往上三層父物件底下的 Renderer 包住這個點）：看不見的擋牆不算</summary>
    static bool Visible(Collider c, Vector3 p)
    {
        Transform t = c.transform;
        for (int level = 0; level < 4 && t != null; level++, t = t.parent)
        {
            var rs = t.GetComponentsInChildren<Renderer>(false);
            if (rs.Length > 600) return false;   // 太大的群組（整個村子）：不算看得到
            foreach (var r in rs)
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.bounds;
                b.Expand(0.4f);
                if (b.Contains(p)) return true;
            }
        }
        return false;
    }

    static bool IsWood(Collider c, RaycastHit hit)
    {
        if (c is TerrainCollider) return true;   // 樹幹
        Transform t = c.transform;
        for (int level = 0; level < 4 && t != null; level++, t = t.parent)
        {
            string n = t.name.ToLowerInvariant();
            foreach (var w in StoneWords) if (n.Contains(w)) return false;
            foreach (var w in WoodWords) if (n.Contains(w)) return true;
        }
        return false;   // 不知道的就當成硬的（石頭、牆）
    }

    // ---------------------------------------------------------------- 彈回去

    void Rebound(RaycastHit hit, float swingAge)
    {
        rebounded = true;
        bool wood = IsWood(hit.collider, hit);
        float pitch = Random.Range(0.94f, 1.06f) * (PlayerLoadout.UsingKnife ? 1.2f : PlayerLoadout.Current == WeaponLoadout.Spear ? 0.92f : 1f);
        GameAudio.Play((wood ? Wood : Stone)[Random.Range(0, 2)], hit.point, wood ? 0.95f : 0.85f, pitch, 0f, 1f, 30f);
        CombatVFX.Impact(hit.point + hit.normal * 0.03f, wood ? new Color(0.62f, 0.45f, 0.27f) : new Color(1f, 0.82f, 0.45f));
        if (!wood) CombatVFX.Impact(hit.point + hit.normal * 0.08f, new Color(1f, 0.95f, 0.75f));   // 石頭多冒一點火花
        ThirdPersonCamera.ShakeMain(0.14f, 0.16f);
        if (InputBindings.RumbleEnabled) PadRumble.Pulse(0.55f, 0.8f, 0.12f);

        // 這一刀斷在這裡（人站在原地，不往後推：腳不會在地上滑）
        combat.CancelAttack();
        StartCoroutine(Kick());

        // 刀沿原路彈回去（揮刀動作照「開始揮刀的時間」算：把它往回推）
        if (visual == null) visual = GetComponentInChildren<PlayerVisualAnimator>(true);
        if (visual != null && SlashStartedAt != null)
        {
            object v = SlashStartedAt.GetValue(visual);
            float started = v is float f ? f : float.NegativeInfinity;
            float age = Time.time - started;
            if (age > 0f && age < 3f)
            {
                rewindFrom = age;
                rewindStart = Time.unscaledTime + stopTime;   // 頓完才開始彈
                rewinding = true;
                Busy = true;
            }
        }
    }

    /// <summary>頓一下（時間幾乎停住），長槍的雙手同時往回收（PlayerLoadout.ReboundKick）</summary>
    IEnumerator Kick()
    {
        bool stopped = false;
        if (Time.timeScale >= 0.99f) { Time.timeScale = 0.05f; stopped = true; }
        float start = Time.unscaledTime, total = stopTime + reboundTime;
        while (Time.unscaledTime - start < total)
        {
            float u = (Time.unscaledTime - start) / total;
            PlayerLoadout.ReboundKick = Mathf.Sin(Mathf.Clamp01(u * 1.6f) * Mathf.PI) * (1f - u * 0.5f);
            if (stopped && Time.unscaledTime - start >= stopTime)
            {
                if (Mathf.Approximately(Time.timeScale, 0.05f)) Time.timeScale = 1f;
                stopped = false;
            }
            yield return null;
        }
        PlayerLoadout.ReboundKick = 0f;
        if (stopped && Mathf.Approximately(Time.timeScale, 0.05f)) Time.timeScale = 1f;
    }

    void OnDisable()
    {
        PlayerLoadout.ReboundKick = 0f;
        if (Mathf.Approximately(Time.timeScale, 0.05f)) Time.timeScale = 1f;
        rewinding = false;
        Busy = false;
    }
}
