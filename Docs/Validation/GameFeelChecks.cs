// Pipeline eval_file body. Run in Play mode with the production PlayerRig on a flat test floor.
// Results are written incrementally so an interrupted run is distinguishable from a pass.
var motor = UnityEngine.Object.FindObjectsByType<ThirdPersonController>().First(c => !c.aiControlled);
var combat = motor.GetComponent<PlayerCombat>();
var health = motor.GetComponent<PlayerHealth>();
var stamina = motor.GetComponent<PlayerStamina>();
var body = motor.GetComponent<CharacterController>();
var orbit = UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
var results = new Dictionary<string, object>();
var errors = new List<string>();
var metrics = new Dictionary<string, object>();
var created = new List<GameObject>();
var output = "output/game-feel/runtime-checks.json";
System.IO.Directory.CreateDirectory("output/game-feel");
var originalKeyboard = UnityEngine.InputSystem.Keyboard.current;
var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("Game feel validation");
var backgroundBehavior = UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior;
var inputBehavior = UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode;
int originalFrameRate = Application.targetFrameRate;
float originalScale = Time.timeScale;
bool originalFeedback = PlayerHitFeedback.Enabled;
void Save(string phase)
{
    System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        phase, checks = results, metrics, errors,
        physicalMouseAndGamepadTesting = "Pending interactive playtest. Batch mode has no locked cursor."
    }, Newtonsoft.Json.Formatting.Indented));
}
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
object Invoke(object owner, string name, params object[] arguments)
{
    return owner.GetType().GetMethod(name, flags).Invoke(owner, arguments);
}
void Keys(params UnityEngine.InputSystem.Key[] keys)
{
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys));
    keyboard.MakeCurrent();
}
async System.Threading.Tasks.Task Wait(float seconds)
{
    float end = Time.realtimeSinceStartup + seconds;
    while (Time.realtimeSinceStartup < end) await System.Threading.Tasks.Task.Delay(5);
}
async System.Threading.Tasks.Task Until(Func<bool> condition, float timeout = 4f)
{
    float end = Time.realtimeSinceStartup + timeout;
    while (!condition())
    {
        if (Time.realtimeSinceStartup >= end) throw new Exception("Timed out waiting for gameplay state");
        await System.Threading.Tasks.Task.Delay(5);
    }
}
void QueueAttack()
{
    var field = typeof(PlayerCombat).GetField("attackBuffer", flags);
    object buffer = field.GetValue(combat);
    typeof(CombatInputBuffer).GetMethod("Queue").Invoke(buffer, new object[] { combat.attackBufferSeconds });
    field.SetValue(combat, buffer);
}
void BeginAttack()
{
    Check((bool)typeof(MeleeCombat).GetMethod("BeginAttack", flags).Invoke(combat, null), "Attack rejected");
}
async System.Threading.Tasks.Task Reset()
{
    Keys();
    CombatHitStop.Cancel();
    Time.timeScale = 1f;
    combat.CancelTransientActions();
    motor.inputEnabled = true;
    motor.ResetMotion();
    typeof(ThirdPersonController).GetField("nextRollTime", flags).SetValue(motor, 0f);
    body.enabled = false;
    motor.transform.SetPositionAndRotation(new Vector3(0f, 1.05f, 0f), Quaternion.identity);
    body.enabled = true;
    stamina.Refill();
    orbit.yaw = 0f;
    orbit.pitch = 5f;
    orbit.ClearLock();
    Physics.SyncTransforms();
    await Wait(.1f);
}
async System.Threading.Tasks.Task Case(string name, Func<System.Threading.Tasks.Task> test)
{
    Save(name);
    try { await Reset(); await test(); results[name] = true; }
    catch (Exception exception) { results[name] = false; errors.Add(name + " — " + exception.GetBaseException().Message); }
    Save(name + " complete");
}
async System.Threading.Tasks.Task Run()
{
    try
    {
        UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        UnityEngine.InputSystem.InputSystem.EnableDevice(keyboard);
        PlayerHitFeedback.Enabled = false;
        Application.targetFrameRate = 60;
        await Case("buffer expiration and replacement", async () =>
        {
            var buffer = new CombatInputBuffer(); buffer.Queue(.12f);
            Check(buffer.Pending, "New command missing");
            await Wait(.16f); Check(!buffer.Pending, "Stale command executed");
            buffer.Queue(.15f); buffer.Clear(); Check(!buffer.Pending, "Cancellation retained command");
        });
        await Case("movement acceleration stopping reversal and wall displacement", async () =>
        {
            Keys(UnityEngine.InputSystem.Key.W); await Wait(.3f);
            Check(motor.CurrentSpeed > motor.walkSpeed * .9f, "Movement did not accelerate");
            Keys(UnityEngine.InputSystem.Key.S); await Wait(.18f);
            Check(motor.HorizontalVelocity.z < 0f, "Reversal retained forward drift");
            Keys(); await Wait(.13f); Check(motor.CurrentSpeed < .05f, "Stopping retained drift");
            metrics["walkSpeed"] = motor.walkSpeed;
            metrics["acceleration"] = motor.acceleration;
            metrics["deceleration"] = motor.deceleration;
        });
        foreach (int frameRate in new[] { 30, 60, 120 })
        {
            await Case("roll distance with fixed " + frameRate + " Hz controller steps", async () =>
            {
                Application.targetFrameRate = frameRate;
                var start = motor.transform.position;
                Invoke(motor, "TryStartRoll", Vector3.forward);
                float step = 1f / frameRate;
                int count = Mathf.CeilToInt(motor.rollDuration / step) + 2;
                for (int i = 0; i < count; i++) Invoke(motor, "UpdateRoll", step);
                float distance = Vector3.ProjectOnPlane(motor.transform.position - start, Vector3.up).magnitude;
                metrics["rollDistance" + frameRate] = distance;
                Check(Mathf.Abs(distance - motor.rollDistance) < .09f, "Frame-dependent roll distance " + distance);
                Check(Mathf.Abs(stamina.Stamina - stamina.maxStamina * (1f - stamina.rollCost)) < .01f,
                    "Roll charged stamina more than once");
                Check(!motor.DodgeInvulnerable, "Invulnerability remained after roll");
            });
        }
        Application.targetFrameRate = 60;
        await Case("dodge pause and active invulnerability window", async () =>
        {
            Invoke(motor, "TryStartRoll", Vector3.forward);
            await Until(() => motor.IsRolling && motor.RollNormalizedTime > .12f);
            Check(motor.DodgeInvulnerable, "Active roll lacks invulnerability");
            Check(!health.TakeDamage(1f, Vector3.zero, true, "Validation", false), "Damage pierced active dodge window");
            float progress = motor.RollNormalizedTime;
            Time.timeScale = 0f; await Wait(.15f);
            Check(Mathf.Abs(progress - motor.RollNormalizedTime) < .001f, "Paused roll advanced");
            Time.timeScale = 1f;
            await Until(() => motor.IsRolling && motor.RollNormalizedTime > .84f);
            Check(!motor.DodgeInvulnerable, "Recovery still has invulnerability");
            float healthBefore = health.Health;
            Check(health.TakeDamage(1f, Vector3.zero, true, "Validation", false) && health.Health < healthBefore,
                "Dodge recovery incorrectly rejected damage");
            await Until(() => !motor.IsRolling);
        });
        await Case("attack anticipation acknowledgment and committed frames", async () =>
        {
            float start = Time.time;
            BeginAttack();
            Check(combat.IsAttacking, "Valid action did not start immediately");
            await Until(() => combat.Phase == MeleeCombat.AttackPhase.Anticipation);
            Invoke(motor, "TryStartRoll", Vector3.forward);
            Check(!motor.IsRolling, "Dodge canceled committed anticipation");
            await Until(() => combat.Phase == MeleeCombat.AttackPhase.Active);
            Check(!combat.CanDodgeCancel, "Active strike freely canceled");
            metrics["attackStartToActiveGameplaySeconds"] = Time.time - start;
            await Until(() => !combat.IsAttacking);
        });
        await Case("recovery attack buffer chains once", async () =>
        {
            int swings = 0; Action<int> onSwing = i => swings++;
            combat.OnSwing += onSwing;
            try
            {
                BeginAttack();
                await Until(() => combat.Phase == MeleeCombat.AttackPhase.Recovery);
                QueueAttack();
                await Until(() => swings >= 2);
                await Until(() => !combat.IsAttacking);
                Check(swings == 2, "Single buffered press repeated " + swings + " times");
            }
            finally { combat.OnSwing -= onSwing; }
        });
        await Case("early attack input expires", async () =>
        {
            int swings = 0; Action<int> onSwing = i => swings++;
            combat.OnSwing += onSwing;
            try { BeginAttack(); QueueAttack(); await Until(() => !combat.IsAttacking); Check(swings == 1, "Early input remained indefinitely"); }
            finally { combat.OnSwing -= onSwing; }
        });
        await Case("buffered dodge enters legal recovery", async () =>
        {
            BeginAttack(); await Until(() => combat.Phase == MeleeCombat.AttackPhase.Recovery);
            await Wait(.04f);
            var path = InputBindings.KeyboardPath(GameAction.Roll);
            var key = (UnityEngine.InputSystem.Controls.KeyControl)InputBindings.Resolve(path);
            Keys(key.keyCode); await Wait(.025f); Keys();
            await Until(() => motor.IsRolling);
            Check(!combat.IsAttacking, "Dodge left attack damage running");
            await Until(() => !motor.IsRolling);
        });
        await Case("missed strike has no hit stop and swing audio matches active phase", async () =>
        {
            int activeEvents = 0; Action<int> onActive = i => activeEvents++;
            combat.OnSwingActive += onActive;
            try
            {
                BeginAttack(); Check(activeEvents == 0, "Swing sound fired at anticipation start");
                await Until(() => !combat.IsAttacking);
                Check(activeEvents == 1 && !CombatHitStop.Active && Mathf.Approximately(Time.timeScale, 1f), "Miss feedback/timing mismatch");
            }
            finally { combat.OnSwingActive -= onActive; }
        });
        var dummy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        dummy.name = "Game feel practice target"; dummy.transform.position = new Vector3(0, 1.2f, 1.5f);
        var receiver = dummy.AddComponent<CalligraphyPracticeTarget>(); created.Add(dummy);
        await Case("actual blade contact damages once per swing", async () =>
        {
            receiver.ResetDamage(); BeginAttack(); await Until(() => !combat.IsAttacking);
            metrics["normalSwingDamage"] = receiver.DamageTaken;
            Check(receiver.DamageTaken > 0f && receiver.DamageTaken <= combat.comboDamage[0] * combat.damageMultiplier * combat.weaponDamageScale + .01f,
                "Blade missed target or damaged repeatedly " + receiver.DamageTaken);
        });
        await Case("lock retention switching invalidation and manual orbit priority", async () =>
        {
            Invoke(orbit, "AcquireLock", false);
            Check(orbit.LockedTarget, "Visible target was not acquired");
            float yaw = orbit.yaw;
            BeginAttack(); await Wait(.2f);
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw, orbit.yaw)) < 10f, "Action forced camera behind player");
            dummy.SetActive(false); await Wait(.06f);
            Check(!orbit.LockedTarget, "Inactive lock target was retained");
        });
        await Case("hit stop bounds overlapping requests pause and slow motion", async () =>
        {
            CombatHitStop.Request(.045f);
            Check(CombatHitStop.Active && Time.timeScale < .1f, "Impact did not begin");
            for (int i = 0; i < 8; i++) CombatHitStop.Request(.09f);
            await Wait(.16f);
            Check(!CombatHitStop.Active && Mathf.Approximately(Time.timeScale, 1f), "Overlapping impacts froze clock");
            await Wait(.2f); CombatHitStop.Request(.045f);
            float captured = CombatHitStop.CaptureTimeScale();
            Time.timeScale = 0f; await Wait(.12f); Time.timeScale = captured;
            Check(Mathf.Approximately(Time.timeScale, 1f), "Pause restored transient hit stop scale");
            Time.timeScale = .8f; CombatHitStop.Request(.04f);
            Check(Mathf.Approximately(Time.timeScale, .8f), "Hit stop overwrote an existing slow motion");
            Time.timeScale = 1f;
        });
        await Case("collision camera retracts immediately and recovers smoothly", async () =>
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(wall);
            wall.name = "Validation solid wall"; wall.transform.position = new Vector3(0, 2, -3);
            wall.transform.localScale = new Vector3(6, 4, .4f);
            float raise = orbit.maxAutoRaise; orbit.maxAutoRaise = 0f;
            try
            {
                Physics.SyncTransforms(); await Wait(.12f);
                var follow = orbit.cinemachineCamera.GetComponent<Unity.Cinemachine.CinemachineThirdPersonFollow>();
                float blocked = follow.CameraDistance;
                Check(blocked < 3.5f, "Camera remained behind wall " + blocked);
                wall.SetActive(false); Physics.SyncTransforms(); await Wait(.035f);
                float recovering = follow.CameraDistance;
                Check(recovering < orbit.distance - .2f, "Camera snapped straight back after obstruction");
                await Wait(1f);
                Check(follow.CameraDistance > blocked + 1f, "Camera did not recover distance");
                metrics["collisionDistance"] = blocked; metrics["firstRecoveryDistance"] = recovering;
            }
            finally { orbit.maxAutoRaise = raise; wall.SetActive(false); }
        });
        await Case("blocked locomotion uses actual displacement for animation", async () =>
        {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(wall);
            wall.transform.position = new Vector3(0, 2, 1.2f); wall.transform.localScale = new Vector3(6, 4, .3f);
            motor.aiControlled = true;
            try
            {
                Physics.SyncTransforms(); motor.SetAIInput(Vector3.forward); await Wait(.4f);
                Check(motor.CurrentSpeed > 1f && motor.AnimationSpeed < .1f, "Wall pushing still animates forward displacement");
            }
            finally { motor.SetAIInput(Vector3.zero); motor.aiControlled = false; wall.SetActive(false); }
        });
        await Case("heavy and wall occluded hits preserve damage rules", async () =>
        {
            dummy.SetActive(true); receiver.ResetDamage();
            typeof(PlayerCombat).GetField("heavyChain", flags).SetValue(combat, true);
            BeginAttack(); await Until(() => !combat.IsAttacking);
            float expected = combat.comboDamage[0] * combat.damageMultiplier * combat.weaponDamageScale * combat.heavyDamageScale;
            Check(Mathf.Abs(receiver.DamageTaken - expected) < .01f, "Heavy damage multiplier or duplicate hit changed");
            metrics["heavySwingDamage"] = receiver.DamageTaken;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); created.Add(wall);
            wall.transform.position = new Vector3(0, 1.5f, .8f); wall.transform.localScale = new Vector3(4, 3, .15f);
            receiver.ResetDamage(); Physics.SyncTransforms();
            typeof(PlayerCombat).GetField("heavyChain", flags).SetValue(combat, false);
            BeginAttack(); await Until(() => !combat.IsAttacking);
            Check(receiver.DamageTaken == 0f, "Normal strike damaged through solid wall");
            wall.SetActive(false); dummy.SetActive(false);
        });
        await Case("multi hit feedback is bounded and particles are reused", async () =>
        {
            PlayerHitFeedback.Enabled = true;
            for (int i = 0; i < 8; i++)
            {
                combat.ReportSkillHit(1f, receiver, new Vector3(0, 1, 4), true);
                await Wait(.035f);
            }
            await Wait(.4f);
            Check(Mathf.Approximately(Time.timeScale, 1f), "Multi-hit feedback held global slow motion");
            ImpactFx.Hit(new Vector3(0, 1, 4), Vector3.forward, ImpactSurface.Flesh, false, false);
            await Wait(1.4f);
            int first = UnityEngine.Object.FindObjectsByType<ImpactParticlePool>(FindObjectsInactive.Include).Length;
            ImpactFx.Hit(new Vector3(0, 1, 4), Vector3.forward, ImpactSurface.Flesh, false, false);
            await Wait(1.4f);
            int second = UnityEngine.Object.FindObjectsByType<ImpactParticlePool>(FindObjectsInactive.Include).Length;
            Check(second <= first, "Warm impact effect created a new set of objects");
            metrics["pooledImpactObjects"] = second;
            PlayerHitFeedback.Enabled = false;
        });
        await Case("damage interruption clears buffered actions", async () =>
        {
            BeginAttack(); QueueAttack(); motor.Stun(.1f); combat.CancelAttack(); await Wait(.2f);
            Check(!(bool)Invoke(combat, "WantsNextHit"), "Hit stun retained attack buffer");
            QueueAttack(); motor.LockActions(); motor.UnlockActions();
            Check(!(bool)Invoke(combat, "WantsNextHit"), "Cinematic lock retained attack buffer");
        });
        await Case("large vertical target and intentional switching remain stable", async () =>
        {
            var large = GameObject.CreatePrimitive(PrimitiveType.Capsule); created.Add(large);
            large.name = "Validation large boss"; large.transform.position = new Vector3(0, 5, 5);
            large.transform.localScale = new Vector3(2, 5, 2); large.AddComponent<BossHealth>();
            Physics.SyncTransforms(); Invoke(orbit, "AcquireLock", false);
            Check(orbit.LockedTarget, "Large target acquisition failed");
            large.transform.position = new Vector3(.2f, 19, .3f); Physics.SyncTransforms(); await Wait(.15f);
            Check(orbit.LockedTarget && Mathf.Abs(orbit.pitch) <= orbit.lockMaxPitch + .1f,
                "Vertical boss movement lost target or forced extreme pitch");
            large.SetActive(false); orbit.ClearLock();
            var left = GameObject.CreatePrimitive(PrimitiveType.Capsule); created.Add(left);
            var right = GameObject.CreatePrimitive(PrimitiveType.Capsule); created.Add(right);
            left.transform.position = new Vector3(-2, 1, 5); right.transform.position = new Vector3(2, 1, 5);
            left.AddComponent<CalligraphyPracticeTarget>(); right.AddComponent<CalligraphyPracticeTarget>();
            orbit.yaw = 0; orbit.pitch = 5; orbit.SnapToTarget(); Physics.SyncTransforms(); await Wait(.05f);
            Invoke(orbit, "AcquireLock", false); Transform first = orbit.LockedTarget;
            Invoke(orbit, "AcquireLock", true); Check(orbit.LockedTarget && orbit.LockedTarget != first, "Target switch retained same target");
            left.SetActive(false); right.SetActive(false); orbit.ClearLock();
        });
        await Case("existing frontal parry preserves health and restores clock", async () =>
        {
            motor.FaceDirection(Vector3.forward);
            Invoke(combat, "SetBlocking", true, false);
            float beforeHealth = health.Health;
            Check(health.TakeDamage(10f, Vector3.back * 3f, false, "Parry validation"), "Frontal defensive contact was rejected");
            Check(Mathf.Approximately(health.Health, beforeHealth), "Existing parry allowed health damage");
            await Wait(.2f);
            Check(Time.timeScale > 0f && Time.timeScale <= 1f, "Parry clock invalid");
            await Until(() => !ParrySlowMotion.Active && Mathf.Approximately(Time.timeScale, 1f), 3f);
        });
        await Case("parry slow motion yields a normal restore clock to menus and skills", async () =>
        {
            ParrySlowMotion.Play(.8f, .7f);
            await Until(() => ParrySlowMotion.Active, 2f);
            float captured = CombatHitStop.CaptureTimeScale();
            Check(Mathf.Approximately(captured, 1f) && !ParrySlowMotion.Active, "Captured a stale parry slow-motion scale");
            Time.timeScale = .2f;
            await Wait(.1f);
            Check(Mathf.Approximately(Time.timeScale, .2f), "Parry overwrote the cinematic clock");
            Time.timeScale = captured;
        });
        await Case("camera impact falloff and zero intensity setting", async () =>
        {
            var impulse = orbit.cinemachineCamera.GetComponent<CombatCameraImpulse>();
            float originalIntensity = orbit.cameraShakeIntensity;
            try
            {
                orbit.ImpactAt(motor.transform.position + Vector3.forward * 100f, .18f, .12f);
                Check((float)typeof(CombatCameraImpulse).GetField("strength", flags).GetValue(impulse) == 0f,
                    "Out-of-range impact retained full camera recoil");
                orbit.ImpactAt(motor.transform.position + Vector3.forward * 2f, .18f, .12f);
                Check((float)typeof(CombatCameraImpulse).GetField("strength", flags).GetValue(impulse) > .17f,
                    "Nearby impact lost its profile strength");
                orbit.cameraShakeIntensity = 0f; await Wait(.025f);
                var state = new Unity.Cinemachine.CameraState { RawOrientation = Quaternion.identity, OrientationCorrection = Quaternion.identity };
                object[] arguments = { orbit.cinemachineCamera, Unity.Cinemachine.CinemachineCore.Stage.Finalize, state, Time.unscaledDeltaTime };
                typeof(CombatCameraImpulse).GetMethod("PostPipelineStageCallback", flags).Invoke(impulse, arguments);
                var corrected = (Unity.Cinemachine.CameraState)arguments[2];
                Check(Quaternion.Angle(corrected.OrientationCorrection, Quaternion.identity) < .001f,
                    "Zero intensity did not disable additive camera recoil");
                Check(ThirdPersonCamera.MainShakeIntensity == 0f, "Cinematic shake ignored the shared intensity setting");
            }
            finally { orbit.cameraShakeIntensity = originalIntensity; }
        });
        await Case("menu close suppresses action edges for only that frame", async () =>
        {
            InputBindings.SuppressActionsThisFrame();
            Check(InputBindings.ActionsSuppressedThisFrame && !InputBindings.Pressed(GameAction.Attack),
                "Menu confirmation leaked to gameplay input");
            await Wait(.025f);
            Check(!InputBindings.ActionsSuppressedThisFrame, "Menu input suppression persisted");
        });
        await Case("scene transition clears pending commands and transient time", async () =>
        {
            var previousScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var temporaryScene = UnityEngine.SceneManagement.SceneManager.CreateScene("Game feel transition validation");
            try
            {
                QueueAttack(); CombatHitStop.Request(.09f);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(temporaryScene);
                Check(!(bool)Invoke(combat, "WantsNextHit") && !CombatHitStop.Active, "Transition retained buffered input or impact clock");
            }
            finally
            {
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(previousScene);
                UnityEngine.SceneManagement.SceneManager.UnloadSceneAsync(temporaryScene);
            }
            await Wait(.05f);
        });
        await Case("death and respawn cancel action and restore normal time", async () =>
        {
            BeginAttack(); QueueAttack(); CombatHitStop.Request(.09f);
            float respawnDelay = health.respawnDelay; health.respawnDelay = .15f;
            try
            {
                health.Kill("Game feel validation");
                Check(health.IsDead && !combat.IsAttacking && !CombatHitStop.Active, "Death left active attack or hit stop");
                await Until(() => !health.IsDead, 4f);
                Check(motor.inputEnabled && !motor.ActionsLocked && Mathf.Approximately(Time.timeScale, 1f), "Respawn did not restore control/time");
            }
            finally { health.respawnDelay = respawnDelay; }
        });
    }
    catch (Exception exception) { errors.Add(exception.ToString()); }
    finally
    {
        Keys(); CombatHitStop.Cancel(); Time.timeScale = originalScale;
        combat.CancelTransientActions(); motor.ResetMotion();
        PlayerHitFeedback.Enabled = originalFeedback;
        Application.targetFrameRate = originalFrameRate;
        UnityEngine.InputSystem.InputSystem.settings.editorInputBehaviorInPlayMode = inputBehavior;
        UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior = backgroundBehavior;
        UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
        if (originalKeyboard != null) originalKeyboard.MakeCurrent();
        foreach (var item in created) if (item) UnityEngine.Object.Destroy(item);
        Save(errors.Count == 0 ? "passed" : "failed");
    }
}
_ = Run();
return "Game feel runtime checks started";
