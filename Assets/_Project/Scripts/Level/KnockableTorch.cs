using System.Collections;
using UnityEngine;

/// <summary>
/// 可以被打掉的火把。DungeonGenerator 蓋房間時會自動加在牆上的火把上。
///
/// 1. 掛在牆上時被玩家或小怪的攻擊掃到 → 從牆上彈下來、邊轉邊掉到地上
/// 2. 落地後火焰閃幾下、冒一陣煙熄滅（那一帶會變暗），不能再點起來
/// 3. 之後它就是地上的一根木棍：走過去會把它踢飛，揮劍掃到會把它打飛（玩家和小怪都可以），可以一直玩
///
/// 火把的結構（DungeonGenerator.Torch 做的）：根物件上有 TorchFlicker，子物件 Stick（木棍）、Flame（火焰）、Light（光）。
/// 會掉下來所需要的 Rigidbody 和碰撞在一開始就準備好了（先關著），被打到時只是把開關打開，不會在戰鬥中才建立東西。
/// </summary>
public class KnockableTorch : Knockable
{
    [Tooltip("掛在牆上時可以被打到的範圍（相對火把，Z = 往房間裡面）。火把掛得比人高，所以範圍往下延伸，站在底下揮劍就打得到")]
    public Vector3 hitAreaCenter = new Vector3(0f, -0.4f, 0.45f);
    public Vector3 hitAreaSize = new Vector3(1.1f, 1.8f, 0.9f);
    [Tooltip("掉到地上後，火焰花多久熄滅 (秒)")]
    public float extinguishTime = 0.9f;
    [Tooltip("在地上被揮劍打到時飛出去的速度 (m/s)")]
    public float hitSpeed = 6f;
    [Tooltip("在地上被走路踢到時，飛出去的速度是走路速度的幾倍")]
    public float kickSpeedScale = 1.4f;
    [Tooltip("熄滅時的聲音（不填 = Resources/Audio/SFX/torch_out）")]
    public AudioClip extinguishSound;
    [Tooltip("掉到地上 / 撞到東西的聲音（不填 = Kenney RPG Audio 的 bookPlace）")]
    public AudioClip landSound;
    [Range(0f, 1f)] public float soundVolume = 0.7f;

    TorchFlicker flicker;
    Light torchLight;
    Transform flame, stick;
    GameObject hitArea;
    Rigidbody body;
    BoxCollider stickCollider;
    bool loose, landed;
    float looseTime;
    float lastKnockTime = float.NegativeInfinity;
    float lastKickTime = float.NegativeInfinity;
    float lastClackTime = float.NegativeInfinity;

    static Material smokeMaterial;
    static ParticleSystem smoke;

    public override bool IsDead => false; // 掉到地上之後還是可以繼續打

    void Awake()
    {
        flicker = GetComponent<TorchFlicker>();
        torchLight = GetComponentInChildren<Light>();
        flame = transform.Find("Flame");
        stick = transform.Find("Stick");

        // 掛在牆上時：攻擊的判定是「揮劍範圍內有沒有碰撞體」，所以要給火把一個看不見、不擋路的感應範圍。
        // 放在 Ignore Raycast 圖層：別的程式用預設設定打的射線不會被它擋到
        hitArea = new GameObject("HitArea");
        hitArea.layer = 2;
        hitArea.transform.SetParent(transform, false);
        hitArea.transform.localPosition = hitAreaCenter;
        var area = hitArea.AddComponent<BoxCollider>();
        area.size = hitAreaSize;
        area.isTrigger = true;

        // 掉下來要用的物理元件先準備好（木棍當碰撞體），還掛在牆上時關著
        if (stick != null)
        {
            stickCollider = stick.gameObject.AddComponent<BoxCollider>();
            stickCollider.enabled = false;
        }
        body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.mass = 1.5f;
        body.linearDamping = 0.2f;
        body.angularDamping = 0.6f;
        body.maxAngularVelocity = 25f;
        body.maxDepenetrationVelocity = 3f; // 萬一跟別的東西重疊，慢慢擠出來，不要整個彈飛
        enabled = false; // 還掛在牆上時不用每一幀更新（掉下來時才打開）
    }

    public override void Knock(Vector3 attackerPosition, float damage)
    {
        if (Time.time - lastKnockTime < 0.12f) return; // 同一下攻擊不要算兩次
        lastKnockTime = Time.time;

        if (!loose)
        {
            PopOffWall();
            return;
        }

        // 已經在地上：往攻擊者的反方向打飛（方向隨機偏一點），重擊飛得比較遠
        Vector3 dir = transform.position - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitSphere;
        dir.y = 0f;
        dir = Quaternion.Euler(0f, Random.Range(-25f, 25f), 0f) * dir.normalized;
        float power = Mathf.Clamp(damage / 12f, 0.8f, 1.6f);
        Launch(dir * (hitSpeed * power) + Vector3.up * Random.Range(2.5f, 4f) * power, 0.3f);
    }

    /// <summary>走路踢到（ThirdPersonController 呼叫）。在地上的火把很小，不管是從旁邊碰到還是踩上去都算踢</summary>
    public override float Shove(Vector3 pusherPosition, Vector3 moveVelocity, Vector3 hitNormal, float topSpeed)
    {
        if (!loose || Time.time - lastKickTime < 0.25f) return 1f;
        moveVelocity.y = 0f;
        float speed = moveVelocity.magnitude;
        if (speed < 0.8f) return 1f;
        lastKickTime = Time.time;

        Vector3 dir = Quaternion.Euler(0f, Random.Range(-20f, 20f), 0f) * (moveVelocity / speed);
        Launch(dir * Mathf.Max(speed * kickSpeedScale, 4f) + Vector3.up * Random.Range(1.5f, 2.8f), 0.25f);
        return 1f; // 一根木棍而已，踢的人不會變慢
    }

    /// <summary>用指定的速度飛出去、隨機翻滾；ignoreTime 秒內先不跟附近的角色互撞（才不會剛踢出去又絆到腳）</summary>
    void Launch(Vector3 velocity, float ignoreTime)
    {
        body.WakeUp();
        body.linearVelocity = velocity;
        body.angularVelocity = Random.onUnitSphere * Random.Range(8f, 16f);
        IgnoreCharactersFor(stickCollider, transform.position, 3f, ignoreTime);
    }

    void PopOffWall()
    {
        loose = true;
        looseTime = Time.time;
        enabled = true;
        if (hitArea != null) hitArea.SetActive(false);
        if (stickCollider != null) stickCollider.enabled = true;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        // 火把掛在牆上、攻擊的人面對著牆，所以不是往攻擊者的反方向飛（那是牆裡面），而是從牆上彈出來
        Vector3 inward = transform.forward;
        Vector3 side = transform.right * Random.Range(-1f, 1f);
        body.linearVelocity = inward * 1.7f + side * 1.2f + Vector3.up * 2.4f;
        body.angularVelocity = Random.onUnitSphere * Random.Range(3f, 7f);
        // 它會掉在出手的人附近：落地前先不要砸到人身上
        IgnoreCharactersFor(stickCollider, transform.position, 4f, 0.7f);

        StartCoroutine(FallAndGoOut());
    }

    void OnCollisionEnter(Collision collision)
    {
        if (!loose || collision.contactCount == 0) return;
        // 剛彈出來的瞬間可能還擦到牆，不算落地；碰到朝上的面（地板、木箱頂…）才算
        if (Time.time - looseTime > 0.15f && collision.GetContact(0).normal.y > 0.5f) landed = true;

        if (collision.relativeVelocity.magnitude > 1.5f && Time.time - lastClackTime > 0.12f)
        {
            lastClackTime = Time.time;
            GameAudio.PlayAt(GameAudio.Pick(landSound, "SFX/RPG/bookPlace2"), transform.position,
                             soundVolume * 0.6f, Random.Range(1.3f, 1.6f), 1f, 25f);
        }
    }

    void LateUpdate()
    {
        // 光源原本在火把前方；火把在地上翻滾時，讓光跟著火焰、固定在它上方一點，不會轉到地板底下或牆裡面
        if (loose && torchLight != null && torchLight.enabled && flame != null)
            torchLight.transform.position = flame.position + Vector3.up * 0.3f;
    }

    IEnumerator FallAndGoOut()
    {
        // 等它掉到地上（最多等 1.8 秒，卡在什麼東西上也照樣熄滅）
        float t = 0f;
        while (!landed && t < 1.8f)
        {
            t += Time.deltaTime;
            yield return null;
        }

        Vector3 firePos = flame != null ? flame.position : transform.position;
        GameAudio.PlayAt(GameAudio.Pick(extinguishSound, "SFX/torch_out"), firePos, soundVolume, Random.Range(0.92f, 1.08f), 1f, 30f);
        SmokePuff(firePos, 16, 1f);

        // 火焰閃幾下、越來越小
        t = 0f;
        while (t < extinguishTime)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / extinguishTime);
            float sputter = 0.55f + 0.45f * Mathf.PerlinNoise(t * 26f, 3.7f);
            if (flicker != null) flicker.dim = (1f - k) * (1f - k) * sputter;
            yield return null;
        }
        if (flicker != null) { flicker.dim = 0f; flicker.enabled = false; }
        if (torchLight != null) torchLight.enabled = false;
        if (flame != null) flame.gameObject.SetActive(false);
        // 熄滅之後木棍繼續留在地上當物理物件（可以踢、可以打）；靜止不動時物理引擎會自動讓它休眠，不耗效能
    }

    // =====================================================================
    //  煙：所有火把共用同一個粒子系統（要冒煙時只是請它在那個位置多噴幾顆）
    // =====================================================================

    /// <summary>
    /// 進關卡時呼叫一次：把煙的粒子系統、材質、音效先準備好，並在 warmupPosition（玩家一開始看得到的地方）
    /// 噴一顆完全透明的粒子，讓顯示卡先把這個材質準備好。第一次打掉火把時就不會為了這些東西頓一下。
    /// </summary>
    public static void Prewarm(Vector3 warmupPosition)
    {
        GameAudio.Clip("SFX/torch_out");
        GameAudio.Clip("SFX/RPG/bookPlace2");
        SmokePuff(warmupPosition, 1, 0f, 8f);
    }

    static bool EnsureSmoke()
    {
        if (smoke != null) return true;
        if (smokeMaterial == null)
        {
            Texture2D dot = BossRageEffect.SoftDot();
            smokeMaterial = BossRageEffect.CreateParticleMaterial(dot, false);
            if (smokeMaterial == null) return false;
            // 材質和貼圖都是程式產生的，而且所有火把共用、要一直留著
            dot.hideFlags = HideFlags.HideAndDontSave;
            smokeMaterial.hideFlags = HideFlags.HideAndDontSave;
        }

        var go = new GameObject("TorchSmoke");
        smoke = go.AddComponent<ParticleSystem>();
        smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = smoke.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 5f;
        main.gravityModifier = -0.04f; // 煙往上飄
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var emission = smoke.emission;
        emission.rateOverTime = 0f;
        var shape = smoke.shape;
        shape.enabled = false;

        var col = smoke.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var size = smoke.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.5f, 1f, 1.8f));

        var pr = go.GetComponent<ParticleSystemRenderer>();
        pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.material = smokeMaterial;
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;
        smoke.Play();
        return true;
    }

    /// <summary>在 position 冒一陣煙。alphaScale = 0 時粒子完全透明（Prewarm 用）；lifetime &gt; 0 可以指定粒子活多久</summary>
    static void SmokePuff(Vector3 position, int count, float alphaScale, float lifetime = 0f)
    {
        if (!EnsureSmoke()) return;
        for (int i = 0; i < count; i++)
        {
            var p = new ParticleSystem.EmitParams
            {
                position = position + Random.insideUnitSphere * 0.12f,
                velocity = Vector3.up * Random.Range(0.5f, 1.3f) + Random.insideUnitSphere * 0.35f,
                startSize = Random.Range(0.25f, 0.5f),
                startLifetime = lifetime > 0f ? lifetime : Random.Range(1f, 1.9f),
                startColor = new Color(0.55f, 0.55f, 0.58f, Random.Range(0.3f, 0.5f) * alphaScale),
                applyShapeToPosition = false,
            };
            smoke.Emit(p, 1);
        }
    }
}
