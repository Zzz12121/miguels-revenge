using System.Collections.Generic;
using UnityEngine;

public sealed partial class VoidRendSkillDemo
{
    [Header("Void Rend area combat")]
    [Min(.1f)] public float launchRadius = 4.5f;
    [Min(0f)] public float launchDamage = 100f;
    [Min(.1f)] public float dashDamageRadius = 1.6f;
    [Min(.1f)] public float explosionRadius = 12f;

    sealed class LiftedVictim
    {
        public Transform root;
        public Transform mover;           // 真正要抬起來的物件（通常就是 root；道場老頭變身後是書法分身）
        public Vector3 home;
        public Quaternion rotation;
        public IDamageable damageable;
        public readonly Dictionary<Behaviour, bool> behaviours = new();
        public readonly Dictionary<CharacterController, bool> controllers = new();
        public Rigidbody body;
        public bool kinematic, gravity;
        public Vector3 velocity, angularVelocity;
        public VoidRendLimpVictim limp;   // 挑空時四肢無力（Humanoid 才有）
        public AmbushArcher archer;       // 埋伏弓箭手：自己的移動流程要另外暫停
        public DojoCalligraphyEncounter encounter;   // 道場老頭：出招 / 瞬移流程要另外暫停
    }
    // 挑空時整個人在空中翻滾：繞著身體中心（離腳底 victimPivotHeight）轉 victimTumbleDegrees 度
    float victimTumbleDegrees;
    Vector3 victimTumbleAxis = Vector3.right;
    const float victimPivotHeight = .9f;
    readonly List<LiftedVictim> liftedVictims = new();
    readonly HashSet<IDamageable> hitThisCut = new();
    PlayerHealth protectedCaster;

    void SetWindupInvulnerability(bool value)
    {
        if (value && caster) protectedCaster = caster.GetComponent<PlayerHealth>();
        if (protectedCaster) protectedCaster.SetSkillInvulnerability(this, value);
        if (!value) protectedCaster = null;
    }

    Transform CombatRoot(Collider collider)
    {
        if (!collider || (caster && collider.transform.IsChildOf(caster))) return null;
        if (collider.GetComponentInParent<PlayerHealth>()) return null;
        var damageable = collider.GetComponentInParent<IDamageable>();
        if (damageable != null && damageable.IsDead) return null;
        if (damageable is Component owner) return owner.transform;
        var marker = collider.GetComponentInParent<VoidRendTargetable>();
        return marker ? marker.transform : null;
    }

    /// <summary>
    /// 要抬起來的是「整隻敵人」：被瞄準的常常是子物件（例如道場老頭的書法分身 DojoMaster Calligraphy Form），
    /// 本體、血量（BossHealth）、動作腳本在父物件上——只抬子物件的話畫面上的本體還站在地上。
    /// 往上找，最外層還帶著血量 / 敵人腳本的那個物件就是整隻敵人。
    /// </summary>
    static Transform LiftRoot(Transform t)
    {
        if (!t) return t;
        Transform best = t;
        for (var p = t.parent; p != null; p = p.parent)
            if (p.GetComponent<IDamageable>() != null || p.GetComponent<DojoMasterBoss>() || p.GetComponent<EnemyAI>() || p.GetComponent<BossController>())
                best = p;
        return best;
    }

    /// <summary>
    /// 畫面上真正在動的身體。道場老頭變身之後，看得到、會走路和瞬移的是子物件「書法分身」，
    /// 根物件（血量、AI）還留在原本的位置——衝刺目標點、挑起來的位置和翻滾中心都要用分身，
    /// 不然玩家會衝到老頭原本站的地方（衝過頭），挑起時分身繞著遠處的根物件轉，看起來沒有被挑起來。
    /// </summary>
    static Transform VictimBody(Transform root)
    {
        if (!root) return root;
        var encounter = root.GetComponent<DojoCalligraphyEncounter>();
        if (encounter && encounter.Transformed && encounter.Form && encounter.Form.gameObject.activeInHierarchy)
            return encounter.Form.transform;
        return root;
    }

    void HoldVictim(Transform root)
    {
        root = LiftRoot(root);
        if (!root || liftedVictims.Exists(v => v.root == root)) return;
        // The new lift takes over from a previous tether throw, never two motion owners at once.
        root.GetComponent<TetherThrownMotion>()?.Interrupt();
        Transform mover = VictimBody(root);
        var v = new LiftedVictim { root = root, mover = mover, home = mover.position, rotation = mover.rotation,
            damageable = root.GetComponent<IDamageable>(), body = root.GetComponent<Rigidbody>() };
        if (v.damageable != null && v.damageable.IsDead) return;
        liftedVictims.Add(v);
        if (v.body)
        {
            v.kinematic = v.body.isKinematic; v.gravity = v.body.useGravity;
            v.velocity = v.body.linearVelocity; v.angularVelocity = v.body.angularVelocity;
            v.body.isKinematic = true; v.body.useGravity = false;
        }
        foreach (var c in root.GetComponentsInChildren<CharacterController>(true))
        { v.controllers[c] = c.enabled; c.enabled = false; }
        v.archer = root.GetComponent<AmbushArcher>();
        if (v.archer) v.archer.SkillHold(true);
        v.encounter = root.GetComponent<DojoCalligraphyEncounter>();
        if (v.encounter) v.encounter.HoldForPlayerSkill(true);
        foreach (var c in root.GetComponentsInChildren<Behaviour>(true)) HoldBehaviour(v, c);
        if (root == target) foreach (var c in targetControllers) HoldBehaviour(v, c, true);
    }

    void HoldBehaviour(LiftedVictim victim, Behaviour component, bool explicitOptIn = false)
    {
        if (!component || component == this || victim.behaviours.ContainsKey(component)) return;
        if (!explicitOptIn && !ShouldSuspendVictimBehaviour(component) && !IsUnknownMover(component) &&
            !(component is VoidRendMovingTarget) && !(component is CalligraphyBrushPresentation) &&
            !(component is CalligraphyBossCombat) && !(component is CalligraphyClone)) return;
        victim.behaviours[component] = component.enabled;
        // Disabling a MonoBehaviour alone does NOT cancel its attack coroutines.
        if (component is EnemyCombat combat) { combat.CancelAttack(); combat.StopAllCoroutines(); }
        if (component is BossController boss) boss.SetSkillAirborne(true);
        if (component is ClockEnemyController clock) clock.SetSkillAirborne(true);
        if (component is ThirdPersonController movement) { movement.SetAIInput(Vector3.zero); movement.ResetMotion(); }
        component.enabled = false;
    }

    /// <summary>
    /// 名字看不出是 AI 的其他腳本（例如小隊戰術、舉盾格擋、各種自訂移動）也可能每一幀改敵人的位置，
    /// 讓敵人挑不起來：除了血量 / 外觀 / 標記這類只負責顯示的元件，其他 MonoBehaviour 一律先暫停。
    /// </summary>
    bool IsUnknownMover(Behaviour c)
    {
        if (!(c is MonoBehaviour) || c is IDamageable || c is VoidRendTargetable || c is VoidRendLimpVictim) return false;
        // 道場老頭變身後，DojoMasterBoss 每幀什麼都不動（由書法分身出招），但關掉它會在 OnDisable 裡停掉整場書法戰：
        // 技能放完老頭就站著不動、半血逃走也不會觸發。出招流程改由 DojoCalligraphyEncounter.HoldForPlayerSkill 暫停。
        if (c is DojoMasterBoss master && master.GetComponent<DojoCalligraphyEncounter>() is DojoCalligraphyEncounter enc && enc && enc.Transformed) return false;
        string n = c.GetType().Name;
        // Encounter / Escape：道場老頭的劇情流程（半血逃走要一直在等），不能停
        foreach (var keep in new[] { "Health", "Visual", "Targetable", "Flash", "Outline", "Highlight", "Bar", "Hud", "Marker", "Pose", "GripFix", "Audio", "Sfx", "Ragdoll", "Encounter", "Escape" })
            if (n.Contains(keep)) return false;
        return true;
    }

    /// <summary>挑起來之後檢查：敵人真的在該在的高度嗎？沒有的話列出還開著的元件（找出是誰把敵人拉回地面）</summary>
    void DiagnoseLift()
    {
        Vector3 offset = victimHeld - victimHome;
        Quaternion tilt = Quaternion.AngleAxis(victimTumbleDegrees, victimTumbleAxis);
        Vector3 pivot = Vector3.up * victimPivotHeight;
        foreach (var v in liftedVictims)
        {
            if (!v.mover) continue;
            Vector3 want = v.home + offset + (pivot - tilt * pivot);
            if (Vector3.Distance(v.mover.position, want) < .3f) continue;
            var enabled = new List<string>();
            foreach (var b in v.root.GetComponentsInChildren<Behaviour>()) if (b.enabled) enabled.Add(b.GetType().Name);
            Debug.LogWarning($"[VoidRend] {v.root.name} 沒有被挑起來：應該在 {want}，實際在 {v.mover.position}。還開著的元件：{string.Join(", ", enabled)}", v.root);
        }
    }

    void LaunchNearbyVictims()
    {
        Physics.SyncTransforms();
        // Centre at torso height, but retain each victim's own floor height and spacing.
        foreach (var collider in Physics.OverlapSphere(victimHome + Vector3.up, launchRadius, ~0, QueryTriggerInteraction.Collide))
            HoldVictim(CombatRoot(collider));
        hitThisCut.Clear();
        foreach (var v in liftedVictims) DamageOnce(v.damageable, launchDamage, v.home + Vector3.up);
    }

    void UpdateLiftedVictims()
    {
        if (!targetLocked) return;
        Vector3 offset = victimHeld - victimHome;
        Quaternion tilt = Quaternion.AngleAxis(victimTumbleDegrees, victimTumbleAxis);
        Vector3 pivot = Vector3.up * victimPivotHeight;
        foreach (var v in liftedVictims)
            if (v.mover) v.mover.SetPositionAndRotation(v.home + offset + (pivot - tilt * pivot), tilt * v.rotation);
    }

    /// <summary>挑空：每個被挑起來的敵人四肢開始無力垂下（slow = 慢動作倍率）</summary>
    void BeginVictimLimp(float slow)
    {
        foreach (var v in liftedVictims)
        {
            if (!v.root) continue;
            if (v.limp == null) v.limp = VoidRendLimpVictim.Attach(v.mover ? v.mover : v.root);
            if (v.limp != null) { v.limp.weight = 1f; v.limp.SlowFactor = slow; }
        }
    }

    void SetVictimLimpSlow(float slow)
    {
        foreach (var v in liftedVictims) if (v.limp != null) v.limp.SlowFactor = slow;
    }

    /// <summary>被砍到：四肢甩一下</summary>
    void KickVictimLimbs(Vector3 direction, float strength)
    {
        foreach (var v in liftedVictims) if (v.limp != null) v.limp.Kick(direction, strength);
    }

    void ReleaseLiftedVictims(bool cancelled)
    {
        victimTumbleDegrees = 0f;
        foreach (var v in liftedVictims)
        {
            if (v.limp != null) { v.limp.Release(); v.limp = null; }
            if (v.archer) v.archer.SkillHold(false);
            if (v.mover) v.mover.SetPositionAndRotation(v.home, v.rotation);
            bool dead = v.damageable is Component owner && owner && v.damageable.IsDead;
            if (v.body)
            {
                v.body.isKinematic = v.kinematic; v.body.useGravity = v.gravity;
                if (!v.kinematic) { v.body.linearVelocity = cancelled ? v.velocity : Vector3.zero; v.body.angularVelocity = cancelled ? v.angularVelocity : Vector3.zero; }
            }
            foreach (var pair in v.controllers) if (pair.Key && !dead) pair.Key.enabled = pair.Value;
            foreach (var pair in v.behaviours)
            {
                if (!pair.Key) continue;
                // Never resurrect dead AI after restoring the suspension snapshot.
                if (!dead || pair.Key is Animator) pair.Key.enabled = pair.Value;
                if (pair.Key is BossController boss) boss.SetSkillAirborne(false);
                if (pair.Key is ClockEnemyController clock) clock.SetSkillAirborne(false);
            }
            if (v.encounter) v.encounter.HoldForPlayerSkill(false);
        }
        liftedVictims.Clear();
    }

    void DamageOnce(IDamageable damageable, float amount, Vector3 point)
    {
        if (damageable == null || !(damageable is Component owner) || !owner || damageable.IsDead || !hitThisCut.Add(damageable)) return;
        damageable.TakeDamage(amount, point);
    }
    void DamageColliders(Collider[] colliders, float damage, Vector3 hit)
    {
        foreach (var c in colliders)
        {
            Transform root = CombatRoot(c);
            if (root) DamageOnce(root.GetComponent<IDamageable>(), damage, hit);
        }
    }
    void DamageDashArea(Vector3 from, Vector3 to)
    {
        Physics.SyncTransforms(); hitThisCut.Clear();
        foreach (var v in liftedVictims) if (v.mover) DamageOnce(v.damageable, voidRendDashDamage, v.mover.position + Vector3.up);
        DamageColliders(Physics.OverlapCapsule(from, to, dashDamageRadius, ~0, QueryTriggerInteraction.Collide), voidRendDashDamage, to);
        // The lattice cuts through a vertical volume, including enemies underneath its aerial tracks.
        from.y = to.y = arenaCenter.y + .9f;
        DamageColliders(Physics.OverlapCapsule(from, to, dashDamageRadius, ~0, QueryTriggerInteraction.Collide), voidRendDashDamage, to);
    }
    void DamageExplosion(Vector3 center)
    {
        Physics.SyncTransforms(); hitThisCut.Clear();
        DamageColliders(Physics.OverlapSphere(center, explosionRadius, ~0, QueryTriggerInteraction.Collide), voidRendDamage, center);
        // CharacterControllers are disabled while held, so use the held positions too.
        foreach (var v in liftedVictims)
            if (v.mover && Vector3.Distance(v.mover.position, center) <= explosionRadius)
                DamageOnce(v.damageable, voidRendDamage, center);
    }
}
