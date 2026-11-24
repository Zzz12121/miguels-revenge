using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 永遠看得到整個角色（第三人稱）：取代 Cinemachine「被擋住就把鏡頭往前拉」的做法，因為那樣鏡頭常常會貼到角色身上，
/// 甚至跑進角色裡面，變成看不到人的近距離 / 第一人稱畫面。
///
/// 1. 擋在中間的小東西（樹、石頭、擺設，尺寸小於 hideOccluderMaxSize）：不拉近鏡頭，直接把它暫時隱藏（只留影子），離開後恢復
/// 2. 擋在中間的大東西（地形、整面牆、建築）：鏡頭最多只拉近到 fullViewMinDistance（拍得到整個角色的距離），
///    還不夠就把鏡頭往上抬（最多 maxAutoRaise 度），從上方越過障礙物；擋住的東西走開後慢慢降回原本的角度
/// 3. 角色（敵人、NPC）擋在中間不隱藏、也不拉近
/// 4. 建築物的零件（屋頂、屋簷、牆、柱子、走廊屋頂…）不隱藏、當成大東西處理：
///    建築是很多個小於 hideOccluderMaxSize 的零件拼起來的，藏掉一塊就會看到天空 / 屋子內部（破圖）。
///    判斷方式：物件或它的上層名字有建築關鍵字（buildingKeywords）、上層掛了 CameraNeverHide、
///    或整個物件在角色頭頂上方（屋頂、屋簷、橫樑這種從下面看擋住天空的東西）
/// 瞄準中不自動抬鏡頭（不然準心會跑掉），只隱藏擋住的東西。
///
/// hideSmallOccluders 關著（預設）：上面第 1 點不做了，擋在中間的東西一律不隱藏，全部跟祠堂、房子一樣當成大東西處理
/// （鏡頭拉近到 fullViewMinDistance、還不夠就往上抬越過去）；很細的東西（竹子、欄杆、燈柱，水平寬度小於 ignoreThinOccluder）
/// 只擋住一條，不理它（不然在竹林裡鏡頭會一直跳）。打開就回到原本「小東西直接隱藏」的做法。
/// </summary>
public partial class ThirdPersonCamera
{
    [Header("永遠看得到整個角色")]
    [Tooltip("開著：鏡頭不會被擋住的東西拉到角色身上；小東西直接隱藏，大東西改成把鏡頭抬高")]
    public bool keepPlayerVisible = true;
    [Tooltip("鏡頭至少離注視點多遠 (m)：這個距離才拍得到整個角色")]
    public float fullViewMinDistance = 3.2f;
    [Tooltip("被地形 / 大牆擋住時，最多把鏡頭往上抬幾度")]
    [Range(0f, 80f)] public float maxAutoRaise = 50f;
    [Tooltip("比這個尺寸小的東西（樹、石頭、擺設）擋在鏡頭和角色中間時直接隱藏 (m)")]
    public float hideOccluderMaxSize = 9f;
    [Tooltip("開著：擋在中間的小東西直接隱藏（舊做法）。關著：一律不隱藏，跟祠堂、房子一樣把鏡頭拉近 / 抬高繞過去")]
    public bool hideSmallOccluders = false;
    [Tooltip("hideSmallOccluders 關著時：水平方向比這個還細的東西（竹子、欄杆、燈柱）擋在中間不理它 (m)")]
    public float ignoreThinOccluder = 0.45f;
    [Tooltip("物件或它上層物件的名字含有這些字（不分大小寫）就當成建築，不會被隱藏")]
    public string[] buildingKeywords =
    {
        "roof", "eave", "ceiling", "wall", "house", "building", "architecture", "maindojo", "veranda", "walkway",
        "pillar", "column", "beam", "rafter", "gate", "torii", "shrine", "temple", "hall", "shoji", "stair",
        "bridge", "structure", "jv_house", "annex", "corridor", "屋", "牆", "柱",
    };
    [Tooltip("整個物件的底部比角色頭頂高（屋頂、屋簷、橫樑）就不隱藏")]
    public bool neverHideOverhead = true;
    [Tooltip("鏡頭往上抬 / 降回去的平滑時間 (秒)")]
    public float autoRaiseSmooth = 0.1f, autoLowerSmooth = 0.45f;

    float autoPitch, autoPitchVelocity;
    readonly RaycastHit[] viewHits = new RaycastHit[32];
    readonly Dictionary<Renderer, (ShadowCastingMode mode, float seen)> hiddenOccluders = new Dictionary<Renderer, (ShadowCastingMode, float)>();
    readonly Dictionary<Collider, Renderer[]> occluderRenderers = new Dictionary<Collider, Renderer[]>();
    readonly List<Renderer> restoreBuffer = new List<Renderer>();

    enum Occluder { Ignore, Hide, Solid }
    readonly Dictionary<Collider, bool> buildingCache = new Dictionary<Collider, bool>();

    /// <summary>這個碰撞體是不是建築的一部分（結果存起來，每個碰撞體只判斷一次）</summary>
    bool IsBuildingPart(Collider c)
    {
        if (buildingCache.TryGetValue(c, out bool cached)) return cached;
        bool building = false;
        for (Transform t = c.transform; t != null && !building; t = t.parent)
        {
            if (t.GetComponent<CameraNeverHide>() != null) { building = true; break; }
            string n = t.name.ToLowerInvariant();
            if (buildingKeywords != null)
                foreach (var k in buildingKeywords)
                    if (!string.IsNullOrEmpty(k) && n.Contains(k.ToLowerInvariant())) { building = true; break; }
        }
        buildingCache[c] = building;
        return building;
    }

    /// <summary>整個物件都在角色頭頂上方（屋頂、屋簷、橫樑）：藏掉的話從下面看會直接看到天空</summary>
    bool IsOverhead(Collider c)
    {
        if (!neverHideOverhead || target == null) return false;
        float head = target.position.y + FeetOffset() + 1.6f;
        return c.bounds.min.y > head;
    }

    Occluder Classify(Collider c)
    {
        if (c == null || c.isTrigger || c is CharacterController || ArrowPassThrough.On(c)) return Occluder.Ignore;
        if (target != null && c.transform.IsChildOf(target)) return Occluder.Ignore;
        if (!CalligraphyWorldIsolation.Allows(c.transform)) return Occluder.Ignore;
        if (c is TerrainCollider) return Occluder.Solid;
        var rb = c.attachedRigidbody;
        if (rb != null && !rb.isKinematic) return Occluder.Ignore;   // 會動的東西（被打飛的木桶、布娃娃）
        if (c.GetComponentInParent<IDamageable>() != null || c.GetComponentInParent<Animator>() != null) return Occluder.Ignore;   // 角色
        var size = c.bounds.size;
        if (Mathf.Max(size.x, size.y, size.z) > hideOccluderMaxSize) return Occluder.Solid;
        // 建築零件、頭頂上的東西：不隱藏（藏掉會破圖），當成大東西讓鏡頭拉近 / 抬高
        if (IsBuildingPart(c) || IsOverhead(c)) return Occluder.Solid;
        // 不隱藏：跟建築一樣處理；很細的東西不理它
        if (!hideSmallOccluders) return Mathf.Max(size.x, size.z) < ignoreThinOccluder ? Occluder.Ignore : Occluder.Solid;
        return Occluder.Hide;
    }

    /// <summary>從注視點往後（pitch, yaw 的反方向）最遠能放鏡頭的距離：只算「大東西」</summary>
    float ClearDistance(Vector3 lookPoint, float pitchDeg, float maxDist)
    {
        Vector3 back = -(Quaternion.Euler(pitchDeg, yaw, 0f) * Vector3.forward);
        int n = Physics.SphereCastNonAlloc(lookPoint, collisionRadius, back, viewHits, maxDist, collisionMask, QueryTriggerInteraction.Ignore);
        float clear = maxDist;
        for (int i = 0; i < n; i++)
        {
            var h = viewHits[i];
            if (h.distance <= 0f || Classify(h.collider) != Occluder.Solid) continue;
            clear = Mathf.Min(clear, Mathf.Max(0.3f, h.distance - 0.1f));
        }
        return clear;
    }

    /// <summary>在 LateUpdate 裡呼叫：決定鏡頭最後的角度和距離，並隱藏擋住角色的小東西</summary>
    void KeepPlayerVisible(Vector3 lookPoint, ref Quaternion rotation, ref float dist)
    {
        float need = Mathf.Min(dist, fullViewMinDistance);
        float want = 0f;
        bool canRaise = aimBlend < 0.3f && maxAutoRaise > 0f;
        if (canRaise && ClearDistance(lookPoint, pitch, dist) < need)
        {
            // 往上抬，找第一個「拉近也不用比 need 更近」的角度；都不行就選空間最大的那個
            float bestClear = -1f, bestOff = maxAutoRaise;
            for (float off = 8f; off <= maxAutoRaise + 0.01f; off += 7f)
            {
                float p = Mathf.Min(pitch + off, 85f);
                float c = ClearDistance(lookPoint, p, dist);
                if (c >= need) { bestOff = off; bestClear = c; break; }
                if (c > bestClear) { bestClear = c; bestOff = off; }
            }
            want = bestOff;
        }
        autoPitch = Mathf.SmoothDamp(autoPitch, want, ref autoPitchVelocity, want > autoPitch ? autoRaiseSmooth : autoLowerSmooth,
                                     Mathf.Infinity, Time.unscaledDeltaTime);
        if (!canRaise && autoPitch < 0.05f) autoPitch = 0f;

        float finalPitch = Mathf.Min(pitch + autoPitch, 85f);
        rotation = Quaternion.Euler(finalPitch, yaw, 0f);
        dist = Mathf.Min(dist, ClearDistance(lookPoint, finalPitch, dist));

        HideOccluders(lookPoint - rotation * Vector3.forward * dist);
    }

    /// <summary>鏡頭到角色頭、胸、腳的連線上有小東西 → 暫時隱藏（只留影子）；0.2 秒沒再擋住就恢復</summary>
    void HideOccluders(Vector3 camPos)
    {
        float feet = target.position.y + FeetOffset();
        Vector3 basePos = new Vector3(target.position.x, feet, target.position.z) + climbShift;
        for (int k = 0; k < 3; k++)
        {
            Vector3 point = basePos + Vector3.up * (k == 0 ? 1.7f : k == 1 ? 1.1f : 0.25f);
            Vector3 d = point - camPos;
            float len = d.magnitude;
            if (len < 0.05f) continue;
            int n = Physics.SphereCastNonAlloc(camPos, 0.12f, d / len, viewHits, len, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = viewHits[i].collider;
                if (Classify(c) != Occluder.Hide) continue;
                foreach (var r in RenderersOf(c))
                {
                    if (r == null) continue;
                    if (hiddenOccluders.TryGetValue(r, out var e)) hiddenOccluders[r] = (e.mode, Time.unscaledTime);
                    else
                    {
                        hiddenOccluders[r] = (r.shadowCastingMode, Time.unscaledTime);
                        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;   // 看不到，但影子還在
                    }
                }
            }
        }
        RestoreOccluders(false);
    }

    Renderer[] RenderersOf(Collider c)
    {
        if (occluderRenderers.TryGetValue(c, out var rs)) return rs;
        // 碰撞體常常掛在模型的父物件或子物件上：先找自己底下，沒有就找上一層底下（但不能大到整個場景）
        rs = c.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0 && c.transform.parent != null)
        {
            var parentRs = c.transform.parent.GetComponentsInChildren<Renderer>();
            if (parentRs.Length <= 8) rs = parentRs;
        }
        occluderRenderers[c] = rs;
        return rs;
    }

    void RestoreOccluders(bool all)
    {
        if (hiddenOccluders.Count == 0) return;
        restoreBuffer.Clear();
        foreach (var kv in hiddenOccluders)
            if (all || kv.Key == null || Time.unscaledTime - kv.Value.seen > 0.2f) restoreBuffer.Add(kv.Key);
        foreach (var r in restoreBuffer)
        {
            if (r != null) r.shadowCastingMode = hiddenOccluders[r].mode;
            hiddenOccluders.Remove(r);
        }
    }

    void OnDisable() => RestoreOccluders(true);
}
