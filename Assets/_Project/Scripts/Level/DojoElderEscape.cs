using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>Owns the half-health performance, its camera and the temporary player input lock.</summary>
[DisallowMultipleComponent]
public sealed class DojoElderEscape : MonoBehaviour
{
    const float VoiceStart = 1.02f;
    const float InkDissolveSeconds = .33f;
    const int HallWaypoint = 1;
    const int HouseCornerWaypoint = 5;
    public GameObject casting, embers, nova, muzzle;
    public Transform departureBeyondTorii;
    public Transform[] flightPath;
    public float flightSpeed = 11f;
    public float outdoorSway = 1.65f, indoorSway = .18f;
    public bool CloudHiddenAfterCameraReturn { get; private set; }
    public readonly List<Vector3> FlightSamples = new();
    /// <summary>First sample in the rear shot. The preceding gap is an edit, not a flight segment.</summary>
    public int IndoorCutSampleIndex { get; private set; } = -1;
    public int AvoidanceCorners { get; private set; }
    public bool Started { get; private set; }
    public bool Completed { get; private set; }
    public string Phase { get; private set; } = "Waiting for half health";
    public DojoEscapeInkCloud Cloud { get; private set; }
    public int ReachedWaypoint { get; private set; } = -1;
    public float RearCameraHeight { get; private set; }

    BossHealth health;
    DojoCalligraphyEncounter encounter;
    DojoMasterBoss boss;
    PlayerHealth player;
    ThirdPersonController motor;
    ThirdPersonCamera follow;
    CinemachineCamera camera;
    AudioClip staggerVoice;
    AudioSource voice;
    DojoElderStaggerPose pose;
    bool ownsLock, ownsCamera;
    string subtitle;
    GUIStyle subtitleStyle;
    Vector3 rearCameraStart, rearTarget;
    float rearStarted = -1f, rearHoldUntil;

    void Awake()
    {
        health = GetComponent<BossHealth>();
        encounter = GetComponent<DojoCalligraphyEncounter>();
        boss = GetComponent<DojoMasterBoss>();
    }

    void OnEnable() => health.OnDamaged += OnDamaged;

    void OnDamaged(float amount)
    {
        // Keep multi-hit cinematic skills from taking the boss below the threshold while we wait for their camera.
        if (!encounter || !encounter.escapeAtHalfHealth) return;
        bool fighting = Started || encounter.Running || (boss && boss.FightActive);
        float threshold = health.maxHealth * .5f;
        if (fighting && health.Health < threshold) health.SetHealth(threshold);
        if (Started || !encounter.Running || health.Health > threshold) return;
        Started = true;
        StartCoroutine(EscapeWhenPlayerSkillEnds());
    }

    IEnumerator EscapeWhenPlayerSkillEnds()
    {
        boss.SuspendForEscape();
        Phase = "Waiting for player skill to finish";
        float until = Time.unscaledTime + 10f;
        while (Time.unscaledTime < until && PlayerSkillPlaying()) yield return null;
        yield return null;
        try { yield return Escape(); }
        finally { ReleasePlayer(); }
    }

    static bool PlayerSkillPlaying()
    {
        foreach (var skill in FindObjectsByType<VoidRendSkillDemo>(FindObjectsInactive.Exclude))
            if (skill.IsPlaying) return true;
        return false;
    }

    IEnumerator Escape()
    {
        var form = encounter.Form;
        player = PlayerHealth.Find();
        if (!form || !player || player.IsDead) yield break;

        motor = player.GetComponent<ThirdPersonController>();
        follow = FindAnyObjectByType<ThirdPersonCamera>();
        if (motor)
        {
            motor.LockActions();
            ownsLock = true;
            motor.inputEnabled = false;
            motor.StopHorizontalMotion();
        }
        ownsCamera = true;
        if (follow) follow.enabled = false;
        camera = new GameObject("Dojo half-health escape camera").AddComponent<CinemachineCamera>();
        camera.Priority = 2000;
        SetFov(48f);

        var root = form.transform;
        Vector3 center = root.position + Vector3.up * 1.12f;
        float space = 1.5f;
        foreach (var hit in Physics.SphereCastAll(center, .24f, root.forward, space, ~0, QueryTriggerInteraction.Ignore))
            if (Scenery(hit.collider)) space = Mathf.Min(space, hit.distance);
        pose = new DojoElderStaggerPose(form.brush, root, Mathf.InverseLerp(.2f, 1.5f, space));
        Vector3 closeShot = ChooseCloseShot(root, center);
        Aim(closeShot, center);
        yield return Stagger(closeShot, center);
        yield return TurnToInk(form, closeShot, center);

        var navigation = new DojoInkFlightPath(transform);
        var frontDoor = Waypoint(0);
        var houseCorner = Waypoint(HouseCornerWaypoint);
        Vector3 frontShot = frontDoor ? frontDoor.position + new Vector3(8f, 1.5f, -5.5f) : Vector3.zero;
        rearCameraStart = houseCorner ? houseCorner.position + new Vector3(-24.1f, 29.5f, 18.3f) : new Vector3(-5f, 32f, 120f);
        rearTarget = houseCorner ? houseCorner.position : new Vector3(19f, 1.3f, 108f);

        Phase = "Flying toward main house";
        SetFov(54f);
        FlightSamples.Add(Cloud.transform.position);
        if (flightPath != null)
        {
            for (int i = 0; i < flightPath.Length; i++)
            {
                if (!flightPath[i]) continue;
                yield return FlyLeg(navigation, flightPath[i].position, i, frontDoor, frontShot);
                ReachedWaypoint = i;
                if (i != HallWaypoint || !TryIndoorCut(navigation, frontShot, out var reentry)) continue;

                // Give the entrance shot a frame after the last visible edge of ink clears the wall.
                yield return new WaitForSeconds(.09f);
                if (!Hidden(frontShot, Cloud.transform.position) || !Hidden(rearCameraStart, reentry)) continue;
                StartRearShot(true);
                Cloud.RelocateWhileHidden(reentry, Quaternion.LookRotation(houseCorner.position - reentry));
                IndoorCutSampleIndex = FlightSamples.Count;
                FlightSamples.Add(reentry);
                i = HouseCornerWaypoint - 1;
                yield return null;
            }
        }

        yield return ReturnToPlayer(navigation);
    }

    IEnumerator Stagger(Vector3 closeShot, Vector3 center)
    {
        Phase = "Elder stagger";
        staggerVoice = GameAudio.Clip("Voice/dojo_elder_stagger");
        float speakingTime = staggerVoice ? staggerVoice.length : 1.65f;
        float duration = VoiceStart + speakingTime + .10f;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            bool speaking = t >= VoiceStart;
            if (speaking && subtitle == null)
                voice = GameAudio.PlayAt(staggerVoice, pose.Focus, DialogueVoice.Volume, 1f, .15f, 28f);
            if (speaking) subtitle = t - VoiceStart < 1.26f ? "可……" : "可……可惡！";
            pose.Apply(Mathf.Clamp01(t / duration), 0f, speaking);
            Aim(closeShot + (center - closeShot).normalized * (.32f * t / duration), Vector3.Lerp(center, pose.Focus, .35f));
            yield return null;
        }

        Phase = "Elder pounce";
        for (float t = 0f; t < .38f; t += Time.deltaTime)
        {
            float q = Mathf.Clamp01(t / .38f);
            pose.Apply(1f, q, false);
            Aim(closeShot + (center - closeShot).normalized * .32f,
                Vector3.Lerp(center, pose.Focus, .35f + q * .4f));
            yield return null;
        }
        pose.Apply(1f, 1f, false);
        subtitle = null;
    }

    IEnumerator TurnToInk(CalligraphyBossCombat form, Vector3 closeShot, Vector3 center)
    {
        Phase = "Twisting into ink";
        var body = form.brush.animator.transform;
        var brush = form.brush.brushPivot;
        var bodyScale = body.localScale;
        var brushScale = brush.localScale;
        var brushPoint = brush.position;
        Vector3 from = pose.InkOrigin;
        var cloudRoot = new GameObject("Agile airborne ink elder");
        Cloud = cloudRoot.AddComponent<DojoEscapeInkCloud>();
        Cloud.casting = casting;
        Cloud.embers = embers;
        Cloud.nova = nova;
        Cloud.muzzle = muzzle;
        cloudRoot.transform.SetPositionAndRotation(from, Quaternion.LookRotation(pose.Forward));
        cloudRoot.transform.localScale = Vector3.one * .05f;
        Cloud.Begin();

        for (float t = 0f; t < InkDissolveSeconds; t += Time.deltaTime)
        {
            float q = Mathf.SmoothStep(0f, 1f, t / InkDissolveSeconds);
            Vector3 at = from + (pose.Forward * .53f + Vector3.up * .32f) * q;
            body.localScale = bodyScale * (1f - q * .98f);
            body.position += at - pose.Focus;
            brush.position = Vector3.Lerp(brushPoint, at, q);
            brush.localScale = brushScale * (1f - q);
            cloudRoot.transform.position = at;
            cloudRoot.transform.localScale = Vector3.one * Mathf.Lerp(.05f, 1f, q);
            Aim(closeShot, Vector3.Lerp(center, at, .65f));
            yield return null;
        }
        foreach (var renderer in form.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
        foreach (var collider in form.GetComponentsInChildren<Collider>()) collider.enabled = false;
        brush.gameObject.SetActive(false);
        cloudRoot.transform.localScale = Vector3.one;
        pose.Restore();
        pose = null;
    }

    Transform Waypoint(int index) => flightPath != null && index < flightPath.Length ? flightPath[index] : null;

    bool TryIndoorCut(DojoInkFlightPath navigation, Vector3 frontShot, out Vector3 reentry)
    {
        reentry = default;
        var sideDoor = Waypoint(HouseCornerWaypoint - 1);
        var corner = Waypoint(HouseCornerWaypoint);
        if (!Waypoint(0) || !sideDoor || !corner || !Hidden(frontShot, Cloud.transform.position)) return false;

        // Skip from the interior hall to the covered exterior walkway, just before the ink clears the eaves.
        reentry = Vector3.Lerp(sideDoor.position, corner.position, .41f);
        return navigation.Clear(reentry, corner.position) && Hidden(rearCameraStart, reentry);
    }

    static bool Scenery(Collider collider) =>
        collider.GetComponentInParent<IDamageable>() == null && !collider.GetComponentInParent<DojoRiverWater>();

    static bool Obstructed(Vector3 eye, Vector3 target)
    {
        Vector3 ray = target - eye;
        foreach (var hit in Physics.RaycastAll(eye, ray.normalized, Mathf.Max(0f, ray.magnitude - .12f), ~0, QueryTriggerInteraction.Ignore))
            if (Scenery(hit.collider)) return true;
        return false;
    }

    static bool Hidden(Vector3 eye, Vector3 ink)
    {
        const float radius = .86f;
        Vector3 side = Vector3.Cross(Vector3.up, (ink - eye).normalized) * radius;
        Vector3 vertical = Vector3.up * radius;
        Vector3 depth = (ink - eye).normalized * radius;
        return Obstructed(eye, ink) && Obstructed(eye, ink + side) && Obstructed(eye, ink - side)
            && Obstructed(eye, ink + vertical) && Obstructed(eye, ink - vertical)
            && Obstructed(eye, ink + depth) && Obstructed(eye, ink - depth);
    }

    static Vector3 ChooseCloseShot(Transform root, Vector3 target)
    {
        Vector3 offset = new Vector3(1.65f, .45f, 3.95f);
        foreach (float angle in new[] { 0f, -45f, 39f, -90f, 90f, 145f })
        {
            Vector3 eye = target + Quaternion.AngleAxis(angle, Vector3.up) * (root.rotation * offset);
            if (!Obstructed(eye, target) && !Obstructed(eye, target - Vector3.up * .95f)) return eye;
        }
        return target + root.forward * 2.25f + Vector3.up * .45f;
    }

    IEnumerator FlyLeg(DojoInkFlightPath navigation, Vector3 goal, int waypoint, Transform frontDoor, Vector3 frontShot)
    {
        var ink = Cloud.transform;
        var corners = SafeFind(navigation, ink.position, goal);
        AvoidanceCorners += Mathf.Max(0, corners.Count - 1);
        foreach (var next in corners)
        {
            Vector3 start = ink.position;
            Vector3 delta = next - start;
            if (delta.sqrMagnitude < .0001f) continue;
            Vector3 forward = delta.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            float seconds = Mathf.Max(.12f, delta.magnitude / Mathf.Max(.1f, flightSpeed));
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float q = Mathf.Clamp01(t / seconds);
                Vector3 center = Vector3.Lerp(start, next, q);
                bool indoors = center.z > 77.5f && center.z < 101f && Mathf.Abs(center.x) < 19.5f;
                float sway = indoors ? indoorSway : outdoorSway;
                Vector3 desired = center + Mathf.Sin(q * Mathf.PI) *
                    (Vector3.up * (Mathf.Sin(Time.time * 4.3f) * .28f + Mathf.Sin(Time.time * 9.1f) * .09f) +
                    side * (Mathf.Sin(Time.time * 2.8f) * sway + Mathf.Sin(Time.time * 5.3f) * sway * .24f));
                var at = Drift(navigation, center, desired, ink.position);
                FlightSamples.Add(at);
                ink.SetPositionAndRotation(at, Quaternion.LookRotation(forward) * Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 5f) * 20f));
                AimFlight(at, waypoint, frontDoor, frontShot);
                yield return null;
            }
            ink.position = Drift(navigation, next, next, ink.position);
            FlightSamples.Add(ink.position);
            AimFlight(ink.position, waypoint, frontDoor, frontShot);
        }
    }

    void StartRearShot(bool withHiddenCut)
    {
        rearStarted = Time.time;
        rearHoldUntil = rearStarted + (withHiddenCut ? .42f : 0f);
        RearCameraHeight = rearCameraStart.y;
        Phase = "Rear courtyard descending view";
        Aim(rearCameraStart, rearTarget);
    }

    void AimFlight(Vector3 at, int waypoint, Transform frontDoor, Vector3 frontShot)
    {
        if (rearStarted < 0f && waypoint <= HallWaypoint)
        {
            if (!frontDoor)
            {
                Aim(at + new Vector3(5f, 2.4f, -7f), at);
                return;
            }
            float frameDoor = 1f - Mathf.SmoothStep(0f, 1f, (frontDoor.position.z - at.z) / 5f);
            Vector3 chase = at + new Vector3(5f, 2.4f, -7f);
            Aim(Vector3.Lerp(chase, frontShot, frameDoor), Vector3.Lerp(at, frontDoor.position, frameDoor));
            return;
        }
        if (rearStarted < 0f) StartRearShot(false);
        float descent = Mathf.SmoothStep(0f, 1f, (Time.time - rearHoldUntil) / 4.2f);
        bool rear = at.x < 47f;
        Phase = rear ? "Rear courtyard descending view" : "Flying through side gate toward mountains";
        Vector3 destination = rear ? new Vector3(at.x - 8f, 8f, Mathf.Clamp(at.z + 8f, 115f, 124f)) : at + new Vector3(-9f, 6f, -8f);
        Vector3 position = Vector3.Lerp(rearCameraStart, destination, descent);
        RearCameraHeight = position.y;
        Aim(position, Vector3.Lerp(rearTarget, at, descent));
    }

    IEnumerator ReturnToPlayer(DojoInkFlightPath navigation)
    {
        Phase = "Returning to player";
        var ink = Cloud.transform;
        Vector3 departing = ink.position;
        Vector3 beyond = departureBeyondTorii ? departureBeyondTorii.position : departing + ink.forward * 8f;
        var departureRoute = SafeFind(navigation, departing, beyond);
        int departureIndex = 0;
        if (follow)
        {
            follow.enabled = true;
            var yawDirection = Vector3.ProjectOnPlane(departing - player.transform.position, Vector3.up);
            if (yawDirection.sqrMagnitude > .001f) follow.yaw = Quaternion.LookRotation(yawDirection).eulerAngles.y;
            follow.pitch = 12f;
            follow.SnapToTarget();
            var playerCam = follow.cinemachineCamera;
            if (playerCam)
            {
                var standby = playerCam.StandbyUpdate;
                playerCam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Always;
                try
                {
                    Vector3 from = camera.transform.position;
                    Quaternion fromRot = camera.transform.rotation;
                    yield return null;
                    for (float t = 0f; t < 1.6f; t += Time.deltaTime)
                    {
                        float q = Mathf.SmoothStep(0f, 1f, t / 1.6f);
                        var state = playerCam.State;
                        camera.transform.SetPositionAndRotation(Vector3.Lerp(from, state.GetFinalPosition(), q),
                            Quaternion.Slerp(fromRot, state.GetFinalOrientation(), q));
                        if (departureIndex < departureRoute.Count)
                        {
                            var target = departureRoute[departureIndex];
                            var next = Vector3.MoveTowards(ink.position, target, flightSpeed * Time.deltaTime);
                            ink.position = Drift(navigation, next, next, ink.position);
                            if (Vector3.Distance(ink.position, target) < .1f) departureIndex++;
                        }
                        yield return null;
                    }
                }
                finally { if (playerCam) playerCam.StandbyUpdate = standby; }
            }
        }

        Destroy(camera.gameObject);
        camera = null;
        yield return null;
        Cloud.gameObject.SetActive(false);
        CloudHiddenAfterCameraReturn = true;
        Phase = "Player remarks on escape";
        bool done = false;
        StoryDialogue.Play(new[] { StoryDialogue.L("米格爾", player.transform, false, "這老頭竟然跑走了！", new Color(.65f, .85f, 1f)) }, () => done = true);
        while (!done) yield return null;
        health.ShowBar = false;
        Completed = true;
        Phase = "Escaped";
        Destroy(Cloud.gameObject);
    }

    // A bad air corridor must not strand the cinematic's controls or camera.
    public bool UsedFallbackRoute { get; private set; }
    bool ignoreObstacles;
    List<Vector3> SafeFind(DojoInkFlightPath navigation, Vector3 from, Vector3 to)
    {
        try { return navigation.Find(from, to); }
        catch (System.InvalidOperationException e) { Debug.LogWarning("[DojoElderEscape] " + e.Message + "，改用備用路線。", this); }
        UsedFallbackRoute = true;
        Vector3 rise = new Vector3(from.x, Mathf.Max(from.y, to.y), from.z);
        try
        {
            var path = navigation.Find(rise, to);
            if (navigation.Clear(from, rise)) { path.Insert(0, rise); return path; }
        }
        catch (System.InvalidOperationException) { }
        ignoreObstacles = true;
        return new List<Vector3> { rise, to };
    }

    Vector3 Drift(DojoInkFlightPath navigation, Vector3 center, Vector3 desired, Vector3 previous) =>
        ignoreObstacles ? desired : navigation.Drift(center, desired, previous);

    void SetFov(float fov)
    {
        var lens = camera.Lens;
        lens.FieldOfView = fov;
        camera.Lens = lens;
    }

    void Aim(Vector3 at, Vector3 target)
    {
        if (camera && (target - at).sqrMagnitude > .001f)
            camera.transform.SetPositionAndRotation(at, Quaternion.LookRotation(target - at));
    }

    void OnGUI()
    {
        if (subtitle == null || GameUI.OverlayOpen) return;
        subtitleStyle ??= new GUIStyle(GameUI.Body) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        float width = Mathf.Min(Screen.width - 32f, 280f);
        var box = new Rect((Screen.width - width) * .5f, Screen.height * .85f, width, 44f);
        Color old = GUI.color;
        GUI.depth = -1100;
        GUI.color = new Color(.045f, .038f, .032f, .77f);
        GUI.DrawTexture(box, Texture2D.whiteTexture);
        GUI.color = new Color(1f, .94f, .82f);
        GUI.Label(box, subtitle, subtitleStyle);
        GUI.color = old;
    }

    void ReleasePlayer()
    {
        if (ownsLock && motor)
        {
            motor.UnlockActions();
            if (!motor.enabled) motor.enabled = true;
            var body = motor.GetComponent<CharacterController>();
            if (body && !body.enabled) body.enabled = true;
            motor.inputEnabled = player && !player.IsDead;
            motor.SuppressJump(.3f);
        }
        ownsLock = false;
        if (ownsCamera && follow) follow.enabled = true;
        ownsCamera = false;
    }

    void OnDisable()
    {
        if (health) health.OnDamaged -= OnDamaged;
        StopAllCoroutines();
        if (voice && voice.clip == staggerVoice) voice.Stop();
        subtitle = null;
        pose?.Restore();
        pose = null;
        if (camera) Destroy(camera.gameObject);
        if (Cloud) Destroy(Cloud.gameObject);
        ReleasePlayer();
    }
}
