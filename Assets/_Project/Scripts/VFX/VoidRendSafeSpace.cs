using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 虛空連斬（技能 1）不穿模、鏡頭不被擋：
///   - 空中連斬的每一個落點、收招停住的位置：檢查站不站得下（不卡進牆 / 地形 / 建築）、過去的路上有沒有東西擋，
///     不行就往目標靠近、左右偏一點再試，都不行就原地不動
///   - 收招落點：往下找地面或屋頂（玩家之後會落在上面），不會飄在空中或掉進地裡
///   - 技能鏡頭：小東西（樹、石頭、擺設）擋在鏡頭和角色 / 敵人中間就暫時隱藏（只留影子）；
///     大東西（地形、建築）擋住就把鏡頭拉近，拉到太近就往上抬，從上面拍
/// </summary>
public sealed partial class VoidRendSkillDemo
{
    [Header("Skill 1 / 不穿模、鏡頭不被擋")]
    [Tooltip("空中連斬離目標最遠幾公尺（原本 11m 會衝過頭、砍進旁邊的建築）")]
    [Min(1.5f)] public float latticeMaxRadius = 4f;
    [Tooltip("開場衝刺停在敵人腳邊多近（0 = 貼著敵人身體邊緣）")]
    [Min(0f)] public float approachStopGap = .1f;
    [Tooltip("技能鏡頭最近離注視點多遠；被大東西擋到更近時改成把鏡頭往上抬")]
    [Min(.8f)] public float skillCameraMinDistance = 2.4f;
    [Tooltip("比這個尺寸小的東西擋住鏡頭時直接隱藏 (m)")]
    public float skillCameraHideMaxSize = 9f;

    static readonly Collider[] safeOverlap = new Collider[48];
    static readonly RaycastHit[] safeHits = new RaycastHit[48];
    float skillCamAllowed = -1f, skillCamRaise;
    readonly Dictionary<Renderer, (ShadowCastingMode mode, float seen)> skillHidden = new();
    readonly Dictionary<Collider, Renderer[]> skillOccluderRenderers = new();
    readonly List<Renderer> skillRestoreBuffer = new();

    /// <summary>
    /// 玩家的根節點離腳底多高（PlayerRig 的根節點在 CharacterController 膠囊中心，大約腳底往上 1m）。
    /// 技能裡移動的是根節點，檢查站不站得下、貼地都要換算成腳底。
    /// </summary>
    float CasterFeetOffset
    {
        get
        {
            if (practiceBody == null || caster == null) return 0f;
            return -(practiceBody.center.y - practiceBody.height * .5f) * caster.lossyScale.y;
        }
    }
    Vector3 FeetOf(Vector3 root) => root - Vector3.up * CasterFeetOffset;
    Vector3 RootOnGround(Vector3 ground) => ground + Vector3.up * CasterFeetOffset;

    /// <summary>角色移動 / 鏡頭檢查時可以忽略的碰撞體：自己、被挑起來的敵人、其他角色、觸發區、會動的剛體</summary>
    bool SkillIgnores(Collider c)
    {
        if (c == null || c.isTrigger) return true;
        if (caster != null && c.transform.IsChildOf(caster)) return true;
        foreach (var v in liftedVictims) if (v.root && c.transform.IsChildOf(v.root)) return true;
        if (c is CharacterController || ArrowPassThrough.On(c)) return true;
        var rb = c.attachedRigidbody;
        if (rb != null && !rb.isKinematic) return true;
        // 角色（敵人、NPC）：連斬本來就是穿過去砍。可以打的石頭 / 樹（沒有骨架動畫）還是當成障礙物
        var dmg = c.GetComponentInParent<IDamageable>() as Component;
        if (dmg != null && dmg.GetComponentInChildren<Animator>() != null) return true;
        if (c.GetComponentInParent<Animator>() != null) return true;
        return false;
    }

    /// <summary>根節點在 root 時身體會不會卡進東西裡</summary>
    bool SkillSpaceBlocked(Vector3 root)
    {
        Vector3 feet = FeetOf(root);
        int n = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * .45f, feet + Vector3.up * 1.5f, .35f, safeOverlap, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (!SkillIgnores(safeOverlap[i])) return true;
        return false;
    }

    /// <summary>根節點從 a 衝到 b，身體（腳底上方 0.9m）路上有沒有東西擋</summary>
    bool SkillPathBlocked(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; float dist = d.magnitude;
        if (dist < .05f) return false;
        int n = Physics.SphereCastNonAlloc(FeetOf(a) + Vector3.up * .9f, .32f, d / dist, safeHits, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++) if (safeHits[i].distance > 0f && !SkillIgnores(safeHits[i].collider)) return true;
        return false;
    }

    /// <summary>空中連斬的落點：原本的位置不行就往目標靠近、左右偏一點；離地太近就抬高；都不行就留在原地</summary>
    Vector3 SafeLatticePoint(Vector3 from, Vector3 desired, Vector3 center)
    {
        Vector3 c = new Vector3(center.x, desired.y, center.z);
        Vector3 off = desired - c;
        foreach (float scale in new[] { 1f, .78f, .55f, .35f })
            foreach (float ang in new[] { 0f, 28f, -28f, 56f, -56f })
            {
                Vector3 p = c + Quaternion.Euler(0f, ang, 0f) * off * scale;
                // 不要貼著地面（或屋頂）砍：腳底至少離下面的東西 0.6m
                Vector3 f = FeetOf(p);
                if (Physics.Raycast(f + Vector3.up * .3f, Vector3.down, out var hit, 1.2f, ~0, QueryTriggerInteraction.Ignore) && !SkillIgnores(hit.collider))
                    p.y = Mathf.Max(p.y, RootOnGround(hit.point).y + .6f);
                if (SkillSpaceBlocked(p) || SkillPathBlocked(from, p)) continue;
                return p;
            }
        return from;
    }

    /// <summary>往下找地面或屋頂（skill 結束後玩家會落在上面）</summary>
    bool SkillGroundBelow(Vector3 p, float above, out Vector3 ground)
    {
        ground = p;
        int n = Physics.RaycastNonAlloc(p + Vector3.up * above, Vector3.down, safeHits, above + 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; bool found = false;
        for (int i = 0; i < n; i++)
        {
            var h = safeHits[i];
            if (SkillIgnores(h.collider) || h.normal.y < .55f) continue;
            if (h.distance < best) { best = h.distance; ground = h.point; found = true; }
        }
        return found;
    }

    /// <summary>
    /// 收招停住的位置：目標前方 finisherLandingDistance（照原本的方向），不行就換角度、換距離。
    /// 要求：下面有地面 / 屋頂、站在那裡不會卡進東西裡、從最後一刀的位置飛過去的路不被擋。
    /// ground = 地面那一點，hold = 根節點要放的位置（腳踩在地面上，再往上 holdHeight）。
    /// </summary>
    bool FindRecoverySpot(Vector3 from, Vector3 forward, float holdHeight, out Vector3 ground, out Vector3 hold, out Vector3 facing)
    {
        ground = hold = from; facing = forward;
        foreach (float dist in new[] { finisherLandingDistance, 3.2f, 6f, 2.2f })
            foreach (float ang in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f })
            {
                Vector3 dir = Quaternion.Euler(0f, ang, 0f) * forward;
                Vector3 p = victimHome + dir * dist;
                if (!SkillGroundBelow(p + Vector3.up * 2f, 6f, out Vector3 g)) continue;
                if (Mathf.Abs(g.y - victimHome.y) > 6f) continue;            // 懸崖下 / 很高的屋頂：不要
                Vector3 h = RootOnGround(g) + Vector3.up * holdHeight;
                if (SkillSpaceBlocked(RootOnGround(g) + Vector3.up * .05f) || SkillSpaceBlocked(h) || SkillPathBlocked(from, h)) continue;
                ground = g; hold = h; facing = dir;
                return true;
            }
        return false;
    }

    // ---------------- 技能鏡頭 ----------------

    void ResetSkillCameraSafety()
    {
        skillCamAllowed = -1f; skillCamRaise = 0f;
        RestoreSkillOccluders(true);
    }

    enum SkillOccluder { Ignore, Hide, Solid }

    SkillOccluder ClassifySkillOccluder(Collider c)
    {
        if (SkillIgnores(c)) return SkillOccluder.Ignore;
        if (c is TerrainCollider) return SkillOccluder.Solid;
        var size = c.bounds.size;
        return Mathf.Max(size.x, size.y, size.z) <= skillCameraHideMaxSize ? SkillOccluder.Hide : SkillOccluder.Solid;
    }

    float SkillCameraClear(Vector3 focus, Vector3 dir, float dist)
    {
        int n = Physics.SphereCastNonAlloc(focus, .3f, dir, safeHits, dist, ~0, QueryTriggerInteraction.Ignore);
        float clear = dist;
        for (int i = 0; i < n; i++)
        {
            var h = safeHits[i];
            if (h.distance <= 0f || ClassifySkillOccluder(h.collider) != SkillOccluder.Solid) continue;
            clear = Mathf.Min(clear, Mathf.Max(.4f, h.distance - .15f));
        }
        return clear;
    }

    /// <summary>
    /// 鏡頭想放在 desired（看著 focus）：被大東西擋住就拉近；拉到比 skillCameraMinDistance 還近就往上抬（最多 65 度）。
    /// 拉近 / 抬高馬上做，放開時慢慢回去（不會一直跳）。
    /// </summary>
    Vector3 ResolveSkillCamera(Vector3 focus, Vector3 desired)
    {
        Vector3 offset = desired - focus;
        float dist = offset.magnitude;
        if (dist < .2f) return desired;
        float need = Mathf.Min(dist, skillCameraMinDistance);

        float wantRaise = 0f;
        float clear = SkillCameraClear(focus, offset / dist, dist);
        if (clear < need)
        {
            float bestClear = clear; wantRaise = 0f;
            for (float r = 10f; r <= 65f; r += 11f)
            {
                Vector3 o = Vector3.RotateTowards(offset, Vector3.up * dist, r * Mathf.Deg2Rad, 0f);
                float c = SkillCameraClear(focus, o.normalized, dist);
                if (c > bestClear + .2f) { bestClear = c; wantRaise = r; }
                if (c >= need) break;
            }
        }
        float k = 1f - Mathf.Exp(-3.5f * Time.unscaledDeltaTime);
        skillCamRaise = wantRaise > skillCamRaise ? wantRaise : Mathf.Lerp(skillCamRaise, wantRaise, k);
        if (skillCamRaise > .01f) offset = Vector3.RotateTowards(offset, Vector3.up * dist, skillCamRaise * Mathf.Deg2Rad, 0f);

        float allowed = SkillCameraClear(focus, offset.normalized, dist);
        if (skillCamAllowed < 0f) skillCamAllowed = allowed;
        skillCamAllowed = allowed < skillCamAllowed ? allowed : Mathf.Lerp(skillCamAllowed, allowed, k);
        return focus + offset.normalized * Mathf.Min(dist, skillCamAllowed);
    }

    /// <summary>鏡頭到角色、到被挑起來的敵人之間的小東西：暫時隱藏（只留影子）；0.25 秒沒再擋住就恢復</summary>
    void HideSkillOccluders(Vector3 camPos)
    {
        if (caster != null) HideAlong(camPos, caster.position + Vector3.up * 1.1f);
        if (targetLocked) HideAlong(camPos, victimHeld + Vector3.up * 1f);
        RestoreSkillOccluders(false);
    }

    void HideAlong(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from; float len = d.magnitude;
        if (len < .05f) return;
        int n = Physics.SphereCastNonAlloc(from, .15f, d / len, safeHits, len, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = safeHits[i].collider;
            if (ClassifySkillOccluder(c) != SkillOccluder.Hide) continue;
            if (!skillOccluderRenderers.TryGetValue(c, out var rs))
            {
                rs = c.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0 && c.transform.parent != null)
                {
                    var p = c.transform.parent.GetComponentsInChildren<Renderer>();
                    if (p.Length <= 8) rs = p;
                }
                skillOccluderRenderers[c] = rs;
            }
            foreach (var r in rs)
            {
                if (r == null || r is ParticleSystemRenderer || r is LineRenderer) continue;
                if (skillHidden.TryGetValue(r, out var e)) skillHidden[r] = (e.mode, Time.unscaledTime);
                else { skillHidden[r] = (r.shadowCastingMode, Time.unscaledTime); r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; }
            }
        }
    }

    void RestoreSkillOccluders(bool all)
    {
        if (skillHidden.Count == 0) return;
        skillRestoreBuffer.Clear();
        foreach (var kv in skillHidden)
            if (all || kv.Key == null || Time.unscaledTime - kv.Value.seen > .25f) skillRestoreBuffer.Add(kv.Key);
        foreach (var r in skillRestoreBuffer)
        {
            if (r != null) r.shadowCastingMode = skillHidden[r].mode;
            skillHidden.Remove(r);
        }
    }
}
