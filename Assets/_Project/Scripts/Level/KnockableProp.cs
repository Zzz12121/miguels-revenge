using UnityEngine;

/// <summary>
/// 可以被打飛的擺設（木桶、木箱）。DungeonGenerator 擺放時會自動加上，不用手動設定。
///
/// - 平常是固定不動的（不吃物理運算）
/// - 被玩家或小怪的攻擊掃到：往攻擊者的反方向飛出去、翻滾，之後就是一個普通的物理物件
/// - 玩家或小怪用走的頂到它：會把它往前頂開（越重的越難推），所以翻倒的木桶不會把路堵死
/// - 被打飛的那一瞬間暫時不跟附近的角色互撞（不然出手的人往前踏步會跟它擠在一起，畫面一頓一頓的）
///
/// 想在別的場景用：把這個元件加到任何有模型的物件上，執行時會自動幫它配好碰撞和 Rigidbody。
/// </summary>
public class KnockableProp : Knockable
{
    [Tooltip("被打到時的衝擊力道。同樣的力道，越重的東西飛得越近")]
    public float knockImpulse = 130f;
    [Tooltip("往上彈的比例（0 = 只會平移，1 = 往上跟往外一樣多）")]
    [Range(0f, 1f)] public float upwardRatio = 0.45f;
    [Tooltip("每立方公尺幾公斤，用來從大小算出重量")]
    public float density = 25f;
    [Tooltip("掉到地上 / 撞到東西的聲音（不填 = Kenney RPG Audio 的 bookPlace）")]
    public AudioClip[] landSounds;
    [Range(0f, 1f)] public float soundVolume = 0.6f;

    Rigidbody body;
    BoxCollider box;
    float lastKnockTime = float.NegativeInfinity;
    float lastThudTime = float.NegativeInfinity;

    public override bool IsDead => false; // 可以一直打

    /// <summary>把一個只有模型的物件變成可以打飛的擺設</summary>
    public static KnockableProp Setup(GameObject go)
    {
        var prop = go.GetComponent<KnockableProp>();
        if (prop == null) prop = go.AddComponent<KnockableProp>();
        prop.EnsurePhysics();
        return prop;
    }

    void Awake() => EnsurePhysics();

    void EnsurePhysics()
    {
        if (body != null) return;

        // 用模型的外框做一個方塊碰撞。模型匯入時自動產生的 Mesh Collider 是凹的，不能用在會動的物體上，要拿掉
        Bounds bounds = LocalBounds();
        foreach (var c in GetComponentsInChildren<Collider>(true))
            DestroyImmediate(c); // 要馬上拿掉（Destroy 會等到這一幀結束），下面才能放心加 Rigidbody
        box = gameObject.AddComponent<BoxCollider>();
        box.center = bounds.center;
        box.size = bounds.size * 0.97f; // 稍微小一點，緊貼著牆擺的時候不會一開始就卡進牆裡

        body = GetComponent<Rigidbody>();
        if (body == null) body = gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;             // 還沒被碰過之前固定不動
        Vector3 worldSize = Vector3.Scale(bounds.size, transform.lossyScale);
        float volume = Mathf.Abs(worldSize.x * worldSize.y * worldSize.z);
        body.mass = Mathf.Clamp(volume * density, 12f, 60f); // 有上限：一整疊木桶也要打得動
        body.linearDamping = 0.4f;
        body.angularDamping = 0.8f;
        body.maxDepenetrationVelocity = 3f;  // 萬一跟牆重疊，慢慢擠出來，不要整個彈飛
        enabled = false;                     // 還沒被碰過之前連 Update 都不用跑（Loosen 時才打開）
    }

    /// <summary>所有子物件模型的外框（換算成這個物件自己的座標）</summary>
    Bounds LocalBounds()
    {
        bool has = false;
        var b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var mf in GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Bounds mb = mf.sharedMesh.bounds;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sy = -1; sy <= 1; sy += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(sx, sy, sz));
                        Vector3 p = transform.InverseTransformPoint(mf.transform.TransformPoint(corner));
                        if (!has) { b = new Bounds(p, Vector3.zero); has = true; }
                        else b.Encapsulate(p);
                    }
        }
        if (!has) b = new Bounds(Vector3.up * 0.5f, Vector3.one); // 沒有模型：當成 1m 的方塊
        return b;
    }

    /// <summary>碰撞方塊從中心往 worldDir 這個方向最遠伸出多少 (m)。擺放時用來算要離牆多遠</summary>
    public float HalfExtentAlong(Vector3 worldDir)
    {
        if (box == null) return 0.5f;
        worldDir.Normalize();
        Vector3 half = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
        float reach = Mathf.Abs(Vector3.Dot(worldDir, transform.right)) * Mathf.Abs(half.x)
                    + Mathf.Abs(Vector3.Dot(worldDir, transform.up)) * Mathf.Abs(half.y)
                    + Mathf.Abs(Vector3.Dot(worldDir, transform.forward)) * Mathf.Abs(half.z);
        // 碰撞方塊的中心不一定在物件原點上
        Vector3 centerOffset = transform.TransformVector(box.center);
        return reach + Vector3.Dot(worldDir, centerOffset);
    }

    void Loosen()
    {
        if (!body.isKinematic) return;
        body.isKinematic = false;
        body.interpolation = RigidbodyInterpolation.Interpolate; // 動起來之後才需要（畫面更新比物理更新快，補間才不會一格一格的）
        body.WakeUp();
        enabled = true;
    }

    public override void Knock(Vector3 attackerPosition, float damage)
    {
        if (Time.time - lastKnockTime < 0.12f) return; // 同一下攻擊不要算兩次
        lastKnockTime = Time.time;
        Loosen();

        Vector3 dir = body.worldCenterOfMass - attackerPosition;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitSphere;
        dir.y = 0f;
        // 方向隨機偏一點：靠牆的東西才不會每次都只是筆直撞牆再彈回來
        dir = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f) * dir.normalized;

        // 重擊（連擊第三下）飛得比較遠
        float power = knockImpulse * Mathf.Clamp(damage / 12f, 0.7f, 1.8f);
        body.AddForce(dir * power + Vector3.up * (power * upwardRatio), ForceMode.Impulse);
        body.AddTorque(Random.onUnitSphere * (power * 0.25f), ForceMode.Impulse);
        // 打的人這時候正往前踏步：先不要跟他互撞，等東西飛開再恢復
        IgnoreCharactersFor(box, body.worldCenterOfMass, 3.5f, 0.4f);
    }

    /// <summary>進關卡時先把會用到的音效載好，第一次打到東西時才不會為了讀檔頓一下</summary>
    public static void Prewarm()
    {
        GameAudio.Clip("SFX/RPG/bookPlace1");
        GameAudio.Clip("SFX/RPG/bookPlace2");
        GameAudio.Clip("SFX/RPG/bookPlace3");
        GameAudio.Clip("SFX/RPG/chop");
    }

    /// <summary>
    /// 有人用走的頂到它（ThirdPersonController 呼叫）：往他走的方向頂開。越重的東西，推的人會被拖得越慢。
    /// </summary>
    public override float Shove(Vector3 pusherPosition, Vector3 moveVelocity, Vector3 hitNormal, float topSpeed)
    {
        if (hitNormal.y > 0.6f) return 1f; // 踩在上面，不是從旁邊推
        moveVelocity.y = 0f;
        float speed = moveVelocity.magnitude;
        if (speed < 0.5f) return 1f;
        Vector3 moveDir = moveVelocity / speed;

        Vector3 toProp = body.worldCenterOfMass - pusherPosition;
        toProp.y = 0f;
        if (toProp.sqrMagnitude < 0.0001f || Vector3.Dot(moveDir, toProp.normalized) < 0.3f) return 1f; // 只是從旁邊擦過去

        Loosen();
        // 推的人會被拖慢成 slow 倍（越重越慢）；東西則被推得比他「稍微快一點」，一直保持在他前面一點點。
        // 如果兩邊一樣快，人每一幀都會頂到它、被擋一下，看起來走得一頓一頓的。
        // 直接設定速度（不用 AddForce）：這個函式一幀可能被呼叫好幾次，AddForce 要等到下一次物理更新才生效，會重複加上去
        float slow = Mathf.Clamp(28f / body.mass, 0.35f, 0.9f);
        float want = Mathf.Min(speed, topSpeed * slow) * 1.15f;
        Vector3 v = body.linearVelocity;
        float have = Vector3.Dot(v, moveDir);
        if (want > have) body.linearVelocity = v + moveDir * (want - have);
        return slow;
    }

    void OnCollisionEnter(Collision collision)
    {
        if (body == null || body.isKinematic) return;
        float impact = collision.relativeVelocity.magnitude;
        if (impact < 2.5f || Time.time - lastThudTime < 0.15f) return;
        lastThudTime = Time.time;
        GameAudio.PlayAt(GameAudio.Pick(landSounds, "SFX/RPG/bookPlace1", "SFX/RPG/bookPlace2", "SFX/RPG/bookPlace3"),
                         transform.position, soundVolume * Mathf.Clamp01(impact / 7f),
                         Random.Range(0.6f, 0.8f) * Mathf.Lerp(1.15f, 0.85f, Mathf.InverseLerp(12f, 160f, body.mass)), 1f, 30f);
    }

}
