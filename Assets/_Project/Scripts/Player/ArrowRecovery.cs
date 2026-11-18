using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 射出去的箭可以撿回來（PlayerLoadout 射箭時掛在每支箭上）：
///   - 插在地上、牆上、樹上的箭：留在原地，走過去就撿回來（插得太高搆不到的，掉到正下方的地上）
///   - 射中敵人的箭：掉在敵人腳邊的地上（有 breakChance 的機率斷掉，撿不回來）
///   - 掉進水裡、飛出去太遠沒打到東西的箭：沒了
/// 只是加在 PlayerArrow 旁邊看它最後停在哪裡，不改箭本身的飛行和傷害。
/// </summary>
public sealed class ArrowRecover : MonoBehaviour
{
    [Tooltip("射中敵人之後斷掉、撿不回來的機率")]
    public float breakChance = 0.2f;

    PlayerArrow arrow;
    Transform startParent;
    Vector3 lastPos;
    Quaternion lastRot;
    float spawnTime;
    bool done;
    static bool quitting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        quitting = false;
        Application.quitting -= OnQuit;
        Application.quitting += OnQuit;
    }
    static void OnQuit() => quitting = true;

    void Awake()
    {
        arrow = GetComponent<PlayerArrow>();
        startParent = transform.parent;
        spawnTime = Time.time;
        lastPos = transform.position;
        lastRot = transform.rotation;
    }

    bool trailStyled;

    /// <summary>
    /// PlayerArrow 本來會拖一條很亮的淺藍色尾巴，飛很快的時候看起來像畫出來的一條線、看不到箭本身。
    /// 弓與小刀的箭（已經換成 JV_Arrow 模型）改成很短、很淡、跟箭身差不多顏色的尾巴，看得到箭的模型。
    /// </summary>
    void StyleTrail()
    {
        var trail = GetComponent<TrailRenderer>();
        if (trail == null) { trailStyled = Time.time - spawnTime > 0.5f; return; } // 尾巴還沒加上去：下一幀再試
        trailStyled = true;
        trail.time = 0.06f;
        trail.startWidth = 0.025f;
        trail.endWidth = 0f;
        var wood = new Color(0.78f, 0.66f, 0.48f);
        trail.startColor = new Color(wood.r, wood.g, wood.b, 0.35f);
        trail.endColor = new Color(wood.r, wood.g, wood.b, 0f);
        if (trail.sharedMaterial != null) trail.sharedMaterial.color = new Color(wood.r, wood.g, wood.b, 0.5f);
    }

    void LateUpdate()
    {
        if (!trailStyled) StyleTrail(); // PlayerArrow.Start 加好尾巴之後
        if (done) return;
        if (transform.parent != startParent) // PlayerArrow.Stick：插在打到的東西上
        {
            done = true;
            ArrowPickup.SpawnStuck(transform.position, transform.rotation, transform.parent);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // 原本的箭過幾秒才刪，先藏起來
            return;
        }
        lastPos = transform.position;
        lastRot = transform.rotation;
    }

    void OnDestroy()
    {
        if (done || quitting || !gameObject.scene.isLoaded || arrow == null) return;
        if (Time.time - spawnTime >= arrow.lifeTime - 0.05f) return;           // 飛太遠，沒打到東西
        if (lastPos.y < arrow.waterLevel + 0.3f) return;                         // 掉進水裡
        // 射中了目標（PlayerArrow 打中就刪掉自己）
        CombatLog.RegisterArrowHit(lastPos + lastRot * Vector3.forward * 0.4f);
        if (Random.value < breakChance) return;
        ArrowPickup.SpawnDropped(lastPos);
    }
}

/// <summary>地上 / 牆上撿得回來的箭：玩家（拿著弓、箭沒滿）走到旁邊就自動撿起來，箭 +1</summary>
public sealed class ArrowPickup : MonoBehaviour
{
    const float PickRadius = 1.5f, PickHeight = 2.4f, LifeTime = 150f, Reach = 2.1f;
    const int MaxPickups = 40;
    static readonly List<ArrowPickup> all = new List<ArrowPickup>();
    static GameObject model;
    static bool modelLoaded;
    static readonly RaycastHit[] hits = new RaycastHit[12];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); model = null; modelLoaded = false; }

    /// <summary>現在地上有幾支撿得回來的箭</summary>
    public static int Count { get { all.RemoveAll(p => p == null); return all.Count; } }

    float bornAt;
    Transform player;
    float y0;

    /// <summary>插在東西上的箭：搆得到就留在原地，太高就掉到正下方</summary>
    public static void SpawnStuck(Vector3 pos, Quaternion rot, Transform surface)
    {
        if (GroundBelow(pos, out var ground) && pos.y - ground.y > Reach) { SpawnDropped(pos); return; }
        var p = Create(pos, rot);
        if (surface != null && surface.gameObject.scene.isLoaded) p.transform.SetParent(surface, true);
    }

    /// <summary>掉在地上的箭：平躺在 pos 正下方的地面，方向隨機</summary>
    public static void SpawnDropped(Vector3 pos)
    {
        if (!GroundBelow(pos, out var ground)) return;
        var flat = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(-Random.Range(0f, 3f), 0f, 0f);
        var offset = Random.insideUnitCircle * 0.5f;
        Create(ground + new Vector3(offset.x, 0.04f, offset.y), flat);
    }

    static bool GroundBelow(Vector3 pos, out Vector3 ground)
    {
        int n = Physics.RaycastNonAlloc(pos + Vector3.up * 0.5f, Vector3.down, hits, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        ground = pos;
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c is CharacterController || c.GetComponentInParent<IDamageable>() != null || c.attachedRigidbody != null) continue;
            if (hits[i].point.y > best) { best = hits[i].point.y; ground = hits[i].point; }
        }
        return !float.IsNegativeInfinity(best);
    }

    /// <summary>箭的模型沿 +Z（飛的方向）；箭頭在前面</summary>
    static ArrowPickup Create(Vector3 pos, Quaternion rot)
    {
        all.RemoveAll(x => x == null);
        if (all.Count >= MaxPickups) { Destroy(all[0].gameObject); all.RemoveAt(0); }
        var go = new GameObject("ArrowPickup");
        go.transform.SetPositionAndRotation(pos, rot);
        if (!modelLoaded) { modelLoaded = true; model = Resources.Load<GameObject>("Weapons/JV_Arrow"); }
        if (model != null)
        {
            // JV_Arrow 的模型沿 +Y、中心在原點：轉成沿 +Z，箭頭插進去一點（箭身大部分露在外面）
            // 跟飛行中的箭一樣擺（PlayerLoadout.OnArrowSpawned）：插著的箭看起來就是原本那支
            var m = Instantiate(model, go.transform, false);
            m.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            foreach (var c in m.GetComponentsInChildren<Collider>()) Destroy(c);
        }
        else
        {
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(go.transform, false);
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localScale = new Vector3(0.025f, 0.45f, 0.025f);
        }
        var p = go.AddComponent<ArrowPickup>();
        all.Add(p);
        return p;
    }

    void Start()
    {
        bornAt = Time.time;
        var h = PlayerHealth.Find();
        if (h != null) player = h.transform;
        y0 = transform.localScale.y;
    }

    void Update()
    {
        if (Time.time - bornAt > LifeTime)
        {
            // 慢慢縮小消失
            float k = 1f - (Time.time - bornAt - LifeTime) / 1.5f;
            if (k <= 0f) { Destroy(gameObject); return; }
            transform.localScale = Vector3.one * y0 * k;
        }
        if (player == null || PlayerLoadout.Current != WeaponLoadout.BowKnife || PlayerLoadout.Arrows >= PlayerLoadout.MaxArrows) return;
        Vector3 d = player.position - transform.position;
        if (new Vector2(d.x, d.z).sqrMagnitude > PickRadius * PickRadius || d.y > PickHeight || d.y < -PickHeight) return;
        if (Time.time - bornAt < 0.4f) return; // 剛射出去就站在旁邊：等一下再撿
        if (!PlayerLoadout.AddArrows(1)) return;
        GameAudio.Play("SFX/RPG/drawKnife1", transform.position, 0.55f, 1.5f, 0.1f, 0.5f, 15f);
        Destroy(gameObject);
    }

    void OnDestroy() => all.Remove(this);
}
