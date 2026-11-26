// Pipeline eval_file body. Run in Play mode in DojoArena_ArchitecturePreview.
var player = PlayerHealth.Find();
var motor = player.GetComponent<ThirdPersonController>();
var body = player.GetComponent<CharacterController>();
var combat = player.GetComponent<PlayerCombat>();
var orbit = UnityEngine.Object.FindAnyObjectByType<ThirdPersonCamera>();
var boss = UnityEngine.Object.FindAnyObjectByType<DojoMasterBoss>();
var encounter = boss.GetComponent<DojoCalligraphyEncounter>();
var hp = boss.GetComponent<BossHealth>();
var escape = boss.GetComponent<DojoElderEscape>();
var cutscene = UnityEngine.Object.FindAnyObjectByType<DojoOpeningCutscene>();
var skill = player.GetComponentInChildren<VoidRendSkillDemo>();
var checks = new Dictionary<string, object>();
var errors = new List<string>();
var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
var output = "output/game-feel/boss-checks.json";
System.IO.Directory.CreateDirectory("output/game-feel");
void Save(string phase)
{
    System.IO.File.WriteAllText(output, Newtonsoft.Json.JsonConvert.SerializeObject(new
    {
        phase, checks, errors, encounterPhase = encounter.Phase, escapePhase = escape.Phase,
        castHistory = encounter.CastHistory.ToArray(), playerProtectedForObservation = true
    }, Newtonsoft.Json.Formatting.Indented));
}
void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
async System.Threading.Tasks.Task Until(Func<bool> condition, float timeout)
{
    float end = Time.realtimeSinceStartup + timeout;
    while (!condition())
    {
        if (Time.realtimeSinceStartup > end) throw new Exception("Timed out waiting for integration phase");
        await System.Threading.Tasks.Task.Delay(20);
    }
}
async System.Threading.Tasks.Task Wait(float seconds)
{
    float end = Time.realtimeSinceStartup + seconds;
    await Until(() => Time.realtimeSinceStartup >= end, seconds + 2f);
}
void Capture(string name)
{
    var camera = Camera.main;
    var texture = RenderTexture.GetTemporary(1280, 720, 24);
    var previousTarget = camera.targetTexture;
    var previousActive = RenderTexture.active;
    var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
    try
    {
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        System.IO.File.WriteAllBytes("output/game-feel/" + name + ".png", image.EncodeToPNG());
    }
    finally
    {
        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(texture);
        UnityEngine.Object.Destroy(image);
    }
}
async System.Threading.Tasks.Task Run()
{
    try
    {
        cutscene.enabled = false;
        player.SetSkillInvulnerability(encounter, true);
        if (cutscene.terraceStop && cutscene.meetPoint)
        {
            Vector3 direction = Vector3.ProjectOnPlane(cutscene.meetPoint.position - cutscene.terraceStop.position, Vector3.up);
            boss.transform.SetPositionAndRotation(cutscene.terraceStop.position,
                direction.sqrMagnitude > .01f ? Quaternion.LookRotation(direction) : boss.transform.rotation);
        }
        combat.CancelTransientActions();
        motor.ResetMotion();
        body.enabled = false;
        player.transform.position = boss.transform.position + boss.transform.forward * 5f + Vector3.up * .2f;
        body.enabled = true;
        orbit.yaw = Quaternion.LookRotation(boss.transform.position - player.transform.position).eulerAngles.y;
        orbit.pitch = 12f;
        orbit.SnapToTarget();
        Physics.SyncTransforms();
        Save("transforming through existing encounter entry");
        encounter.StartCoroutine(encounter.TransformAfterDialogue(null));
        await Until(() => encounter.Transformed, 15f);
        boss.BeginFight();
        Check(encounter.Running && boss.FightActive, "Production encounter did not begin");
        checks["productionEncounterAndTransformation"] = true;
        await Until(() => encounter.EntranceComplete, 12f);
        await Until(() => encounter.CastHistory.Count >= 2, 25f);
        Capture("dojo-combat");
        checks["originalEntranceAndRegularBossSkills"] = true;
        Save("testing player cinematic skill");
        Check(skill != null, "Existing player skill reference missing");
        hp.Invulnerable = true;
        skill.target = encounter.Form.transform;
        skill.playOnStart = false;
        skill.loopPreview = false;
        skill.PlaySkill();
        await Until(() => skill.IsPlaying, 4f);
        await Wait(2f);
        float cinematicScale = CombatHitStop.CaptureTimeScale();
        Time.timeScale = 0f;
        await Wait(.08f);
        float pausedElapsed = skill.Elapsed;
        string pausedStage = skill.Stage;
        Vector3 pausedPlayer = player.transform.position;
        Vector3 pausedTarget = skill.target.position;
        await Wait(.2f);
        Check(Time.timeScale == 0f && skill.Stage == pausedStage && Mathf.Abs(skill.Elapsed - pausedElapsed) < .001f,
            "Cinematic progression advanced or overwrote the menu pause");
        Check(Vector3.Distance(pausedPlayer, player.transform.position) < .001f
            && Vector3.Distance(pausedTarget, skill.target.position) < .001f, "Cinematic actors moved during pause");
        Time.timeScale = cinematicScale;
        checks["cinematicSlowMotionPausesWithoutAdvancingActorsOrStages"] = true;
        Capture("void-rend-integration");
        await Until(() => !skill.IsPlaying, 35f);
        Check(motor.inputEnabled && !motor.ActionsLocked && orbit.enabled, "Skill did not return player control/camera");
        Check(Mathf.Approximately(Time.timeScale, 1f), "Skill restored an invalid time scale");
        checks["existingVoidRendReturnsControlAndClock"] = true;
        hp.Invulnerable = false;
        Save("testing half health cinematic handoff");
        hp.SetHealth(hp.maxHealth * .5f);
        await Until(() => escape.Started, 5f);
        await Until(() => escape.Phase == "Player remarks on escape", 45f);
        if (StoryDialogue.Active)
        {
            var dialogue = UnityEngine.Object.FindAnyObjectByType<StoryDialogue>();
            typeof(StoryDialogue).GetMethod("Finish", flags).Invoke(dialogue, null);
        }
        await Until(() => escape.Completed, 6f);
        Check(motor.inputEnabled && !motor.ActionsLocked && !hp.ShowBar && orbit.enabled,
            "Escape handoff left movement, camera or boss HUD locked");
        Check(!boss.FightActive && !hp.IsDead && Mathf.Approximately(Time.timeScale, 1f), "Escape progression or clock regressed");
        checks["halfHealthEscapeDialogueAndCameraHandoff"] = true;
        Capture("dojo-control-return");
        Save("passed");
    }
    catch (Exception exception) { errors.Add(exception.GetBaseException().ToString()); Save("failed"); }
    finally
    {
        player.SetSkillInvulnerability(encounter, false);
        hp.Invulnerable = false;
        CombatHitStop.Cancel();
    }
}
_ = Run();
return "Dojo integration checks started";
