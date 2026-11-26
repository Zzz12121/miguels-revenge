using System;
using System.Linq;
using UnityEngine;

/// <summary>Scene-local exploration adapter. Does not change the shared player or encounter prefabs.</summary>
[DefaultExecutionOrder(-800)]
public sealed class InkboundExploration : MonoBehaviour
{
    public ThirdPersonController player;
    public Transform entrance;
    public string townTitle = "河鎮";
    [Tooltip("落到河面下方時，回到最近的安全岸邊高度")]
    public float waterReturnHeight = -2.8f;
    public bool showArrivalTitle = false;
    public bool allowCombat;
    public Vector3 LastSafePosition { get; private set; }
    public int WaterReturns { get; private set; }
    float startedAt, returnCooldown;
    GUIStyle titleStyle, subtitleStyle;
    GameManager.Level registration;

    void Start()
    {
        startedAt = Time.unscaledTime;
        if (!player) player = PlayerHealth.Find()?.GetComponent<ThirdPersonController>();
        if (!player) return;
        LastSafePosition = player.transform.position;
        var health = player.GetComponent<PlayerHealth>();
        if (health) health.SetSkillInvulnerability(this, !allowCombat);
        // Awake on the shared player may install optional skills. Disable them on this scene instance only.
        string[] combatTypes = { "PlayerCombat", "VoidRendSkillDemo", "EnergyTetherSkill", "PhantomStrike",
            "LeapCleaveStrike", "ShieldSlideStrike", "SpearArts", "PlayerItems" };
        foreach (var component in player.GetComponentsInChildren<MonoBehaviour>(true))
            if (!allowCombat && component && combatTypes.Contains(component.GetType().Name)) component.enabled = false;
        var manager = GameManager.Instance;
        if (manager && manager.FindLevel(gameObject.scene.name) == null)
        {
            registration = new GameManager.Level { sceneName = gameObject.scene.name, title = townTitle, music = "" };
            manager.storyLevels = manager.storyLevels.Concat(new[] { registration }).ToArray();
        }
    }

    void Update()
    {
        if (!player || Time.timeScale <= 0f) return;
        Vector3 position = player.transform.position;
        if (player.IsGrounded && position.y > -.35f && position.y < 8f) LastSafePosition = position;
        if (position.y < waterReturnHeight && Time.unscaledTime > returnCooldown) ReturnFromWater();
    }

    public void ReturnFromWater()
    {
        if (!player) return;
        returnCooldown = Time.unscaledTime + 1f;
        var body = player.GetComponent<CharacterController>();
        bool wasEnabled = body && body.enabled;
        Vector3 old = player.transform.position;
        if (body) body.enabled = false;
        player.transform.position = LastSafePosition + Vector3.up * .12f;
        player.ResetMotion();
        if (body) body.enabled = wasEnabled;
        var orbit = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (orbit) orbit.WarpWithTarget(player.transform.position - old);
        WaterReturns++;
    }

    void OnGUI()
    {
        if (!showArrivalTitle) return;
        float t = Time.unscaledTime - startedAt;
        float alpha = Mathf.Clamp01(t / 1.2f) * Mathf.Clamp01((7f - t) / 1.5f);
        if (alpha <= 0f) return;
        titleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 40, alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold, normal = { textColor = new Color(.12f,.13f,.14f) } };
        subtitleStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(.24f,.25f,.26f) } };
        Color old = GUI.color; GUI.color = new Color(1,1,1,alpha);
        GUI.Label(new Rect(0, Screen.height * .18f, Screen.width, 60), townTitle, titleStyle);
        GUI.Label(new Rect(0, Screen.height * .18f + 57, Screen.width, 32), "一幅畫，一城人，一段未曾遠去的午後。", subtitleStyle);
        GUI.color = old;
    }

    void OnDestroy()
    {
        if (player) player.GetComponent<PlayerHealth>()?.SetSkillInvulnerability(this, false);
        if (registration != null && GameManager.Instance)
            GameManager.Instance.storyLevels = GameManager.Instance.storyLevels.Where(x => x != registration).ToArray();
    }
}
