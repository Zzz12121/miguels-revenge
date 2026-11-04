using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 幻影分身。三種狀態：
///   Mirror ：玩家的「鏡像」——每一幀把玩家現在的姿勢烘成網格，對一個垂直平面鏡射後畫出來。
///            玩家往右邊衝、揮刀，分身就同時往左邊衝、揮刀（同一套動畫，不用另外做）。
///   Lure   ：停在原地的半透明分身（定格在最後的姿勢），會吸引附近小怪的仇恨；被小怪打一下就消散。
///   Dissolve：消散——往上飄、放大、淡掉，同時噴出藍色光點。
/// 由 PhantomStrike 建立與控制；小怪（EnemyAI）用 NearestLure 找要追的分身，EnemyCombat 打到它就呼叫 Break。
/// </summary>
[DefaultExecutionOrder(300)] // 在 PlayerVisualAnimator（100）擺完姿勢之後才烘網格
public sealed class PhantomDecoy : MonoBehaviour
{
    static readonly List<PhantomDecoy> lures = new List<PhantomDecoy>();
    static Material ghostMaterial;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    enum Mode { Mirror, Lure, Dissolve }
    Mode mode;

    Transform owner;
    SkinnedMeshRenderer[] skinned;
    MeshRenderer[] rigid;
    Mesh[] baked;
    Matrix4x4[] matrices;
    Mesh[] meshes;
    int drawCount;
    Matrix4x4 mirror = Matrix4x4.identity;
    Color color;
    MaterialPropertyBlock block;
    float lureRadius, lureUntil, dissolveStart, dissolveTime = .45f, spawnTime;
    CapsuleCollider body;

    /// <summary>還在場上（鏡像中或誘敵中）</summary>
    public bool Alive => mode != Mode.Dissolve;
    /// <summary>正在原地誘敵</summary>
    public bool IsLure => mode == Mode.Lure;

    /// <summary>離 from 最近、還在誘敵、且在它的吸引範圍內的分身（沒有就回傳 null）</summary>
    public static Transform NearestLure(Vector3 from)
    {
        Transform best = null; float bestSqr = float.MaxValue;
        for (int i = lures.Count - 1; i >= 0; i--)
        {
            var d = lures[i];
            if (d == null || d.mode != Mode.Lure) continue;
            float sqr = (d.transform.position - from).sqrMagnitude;
            if (sqr <= d.lureRadius * d.lureRadius && sqr < bestSqr) { bestSqr = sqr; best = d.transform; }
        }
        return best;
    }

    /// <summary>建立一個鏡像分身：鏡射平面通過 point、法線 normal（水平）</summary>
    public static PhantomDecoy Create(Transform owner, Vector3 point, Vector3 normal, Color color)
    {
        var go = new GameObject("PhantomClone");
        go.layer = 2; // Ignore Raycast：不擋鏡頭、地面射線
        var d = go.AddComponent<PhantomDecoy>();
        d.owner = owner;
        d.color = color;
        d.block = new MaterialPropertyBlock();
        d.skinned = owner.GetComponentsInChildren<SkinnedMeshRenderer>();
        var rigidList = new List<MeshRenderer>();
        foreach (var r in owner.GetComponentsInChildren<MeshRenderer>())
            if (r.GetComponent<MeshFilter>() != null && !r.name.Contains("Facing") && !(r.GetComponent<AfterimageGhost>() != null)) rigidList.Add(r);
        d.rigid = rigidList.ToArray();
        int n = d.skinned.Length + d.rigid.Length;
        d.baked = new Mesh[d.skinned.Length];
        for (int i = 0; i < d.baked.Length; i++) d.baked[i] = new Mesh { name = "PhantomClone" };
        d.meshes = new Mesh[n];
        d.matrices = new Matrix4x4[n];
        d.SetMirror(point, normal);
        d.spawnTime = Time.time;
        d.body = go.AddComponent<CapsuleCollider>();
        d.body.isTrigger = true;
        d.body.enabled = false;
        var cc = owner.GetComponent<CharacterController>();
        if (cc != null) { d.body.height = cc.height; d.body.radius = cc.radius; d.body.center = cc.center; }
        d.Capture();
        return d;
    }

    /// <summary>改成鏡像模式（剪刀斬時再跟著玩家動）：平面通過 point、法線 normal</summary>
    public void SetMirror(Vector3 point, Vector3 normal)
    {
        normal.y = 0f;
        normal = normal.sqrMagnitude > .0001f ? normal.normalized : Vector3.right;
        float d = -Vector3.Dot(normal, point);
        var m = Matrix4x4.identity;
        m.m00 = 1 - 2 * normal.x * normal.x; m.m01 = -2 * normal.x * normal.y; m.m02 = -2 * normal.x * normal.z; m.m03 = -2 * d * normal.x;
        m.m10 = -2 * normal.y * normal.x; m.m11 = 1 - 2 * normal.y * normal.y; m.m12 = -2 * normal.y * normal.z; m.m13 = -2 * d * normal.y;
        m.m20 = -2 * normal.z * normal.x; m.m21 = -2 * normal.z * normal.y; m.m22 = 1 - 2 * normal.z * normal.z; m.m23 = -2 * d * normal.z;
        mirror = m;
        if (mode == Mode.Lure) lures.Remove(this);
        mode = Mode.Mirror;
        if (body != null) body.enabled = false;
    }

    /// <summary>鏡射一個世界座標的點（分身的位置 = 鏡射玩家的位置）</summary>
    public Vector3 MirrorPoint(Vector3 p) => mirror.MultiplyPoint3x4(p);
    public Vector3 MirrorDirection(Vector3 v) => mirror.MultiplyVector(v);

    /// <summary>停在原地誘敵 seconds 秒（radius = 吸引小怪的範圍），時間到自己消散</summary>
    public void BecomeLure(float seconds, float radius)
    {
        if (mode == Mode.Dissolve) return;
        Capture();
        mode = Mode.Lure;
        lureRadius = radius;
        lureUntil = Time.time + seconds;
        body.enabled = true;
        if (!lures.Contains(this)) lures.Add(this);
    }

    /// <summary>被打中：馬上消散</summary>
    public void Break() => Dissolve();

    public void Dissolve()
    {
        if (mode == Mode.Dissolve) return;
        if (mode == Mode.Mirror) Capture();
        mode = Mode.Dissolve;
        lures.Remove(this);
        if (body != null) body.enabled = false;
        dissolveStart = Time.time;
        Vector3 c = transform.position;
        var glow = new Color(color.r, color.g, color.b, 1f);
        CombatVFX.Puff(c + Vector3.down * .3f, Vector3.up, glow, true);
        CombatVFX.Puff(c + Vector3.up * .3f, Vector3.up, new Color(color.r, color.g, color.b, .6f), true);
        CombatVFX.Impact(c + Vector3.up * .2f, glow);
        GameAudio.Play("SFX/RPG/cloth3", c, .55f, 1.9f, .05f, .3f);
    }

    /// <summary>把玩家現在的姿勢烘下來，算出鏡射後要畫的位置</summary>
    void Capture()
    {
        if (owner == null) return;
        drawCount = 0;
        for (int i = 0; i < skinned.Length; i++)
        {
            var r = skinned[i];
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
            r.BakeMesh(baked[i], true);
            meshes[drawCount] = baked[i];
            matrices[drawCount++] = mirror * Matrix4x4.TRS(r.transform.position, r.transform.rotation, Vector3.one);
        }
        foreach (var r in rigid)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            meshes[drawCount] = mf.sharedMesh;
            matrices[drawCount++] = mirror * r.localToWorldMatrix;
        }
        transform.position = mirror.MultiplyPoint3x4(owner.position);
        Vector3 fwd = mirror.MultiplyVector(owner.forward); fwd.y = 0f;
        if (fwd.sqrMagnitude > .0001f) transform.rotation = Quaternion.LookRotation(fwd);
    }

    void LateUpdate()
    {
        if (owner == null) { Destroy(gameObject); return; }
        float alpha = color.a;
        Matrix4x4 extra = Matrix4x4.identity;
        switch (mode)
        {
            case Mode.Mirror:
                Capture();
                alpha *= Mathf.Clamp01((Time.time - spawnTime) / .08f); // 出現時淡入
                break;
            case Mode.Lure:
                if (Time.time >= lureUntil) { Dissolve(); break; }
                alpha *= .8f + .2f * Mathf.Sin(Time.time * 9f); // 誘敵中輕輕閃爍
                break;
        }
        if (mode == Mode.Dissolve)
        {
            float u = Mathf.Clamp01((Time.time - dissolveStart) / dissolveTime);
            if (u >= 1f) { Destroy(gameObject); return; }
            alpha *= (1f - u) * (1f - u);
            Vector3 pivot = transform.position;
            float s = 1f + .18f * u;
            extra = Matrix4x4.Translate(Vector3.up * (.5f * u)) * Matrix4x4.Translate(pivot)
                  * Matrix4x4.Scale(new Vector3(s, 1f + .05f * u, s)) * Matrix4x4.Translate(-pivot);
        }
        Draw(alpha, extra);
    }

    void Draw(float alpha, Matrix4x4 extra)
    {
        var mat = Material();
        if (mat == null) return;
        var c = color; c.a = alpha;
        block.SetColor(ColorId, c);
        block.SetColor(BaseColorId, c);
        for (int i = 0; i < drawCount; i++)
        {
            var mesh = meshes[i];
            if (mesh == null) continue;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
                Graphics.DrawMesh(mesh, extra * matrices[i], mat, 0, null, sub, block, false, false);
        }
    }

    static Material Material()
    {
        if (ghostMaterial != null) return ghostMaterial;
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) return null;
        ghostMaterial = new Material(shader) { name = "PhantomClone (runtime)", renderQueue = 3000 };
        return ghostMaterial;
    }

    void OnDestroy()
    {
        lures.Remove(this);
        if (baked != null) foreach (var m in baked) if (m != null) Destroy(m);
    }
}
