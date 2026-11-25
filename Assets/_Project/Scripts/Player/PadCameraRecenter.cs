using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 用手把右搖桿轉鏡頭時，鏡頭偏離「上一個打中的敵人」太遠（maxDeviation 度），放開搖桿一下下就自動把鏡頭轉回去對著那個敵人
/// （不用在場景裡放東西，自動建立、跨場景存在）。
///   - 上一個打中的敵人：玩家的近戰 / 技能打中誰（PlayerCombat.OnDealtHit）；牠倒下、太久沒打（forgetAfter 秒）或離太遠（maxDistance）就不算
///   - 只在用手把的時候（PadInput.UsingPadRecently）；滑鼠轉鏡頭不管
///   - 搖桿推著的時候不搶；放開 releaseDelay 秒以後才轉回去，轉的途中再推搖桿就停（玩家優先）
///   - 瞄準、過場（HUD 藏起來）、選單、對話、時間停住的時候不動
/// ThirdPersonCamera 是隊友的，這裡不改它：在它之前（執行順序 -60）調它公開的 yaw / pitch，它接著照平常讀搖桿、算位置。
/// </summary>
[DefaultExecutionOrder(-60)]
public class PadCameraRecenter : MonoBehaviour
{
    [Tooltip("鏡頭的方向跟「玩家 → 敵人」的方向差超過幾度就校正")]
    public float maxDeviation = 70f;
    [Tooltip("放開右搖桿多久以後才校正（秒）")]
    public float releaseDelay = 0.3f;
    [Tooltip("轉回去要多久（平滑，秒）")]
    public float turnTime = 0.22f;
    [Tooltip("多久沒打那個敵人就不再對著牠（秒）")]
    public float forgetAfter = 15f;
    [Tooltip("敵人離玩家超過幾公尺就不對著牠")]
    public float maxDistance = 30f;

    static PadCameraRecenter host;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => host = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (host != null) return;
        host = new GameObject("PadCameraRecenter").AddComponent<PadCameraRecenter>();
        DontDestroyOnLoad(host.gameObject);
    }

    PlayerHealth player;
    PlayerCombat combat;
    ThirdPersonCamera cam;
    Transform enemy;
    IDamageable enemyHealth;
    float lastHitAt = -100f, releasedFor;
    bool correcting;
    float yawVel, pitchVel;

    void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    void OnDisable() { SceneManager.sceneLoaded -= OnSceneLoaded; Unhook(); }
    void OnSceneLoaded(Scene s, LoadSceneMode m) { Unhook(); enemy = null; enemyHealth = null; correcting = false; }

    void Hook()
    {
        player = PlayerHealth.Find();
        combat = player != null ? player.GetComponent<PlayerCombat>() : null;
        if (combat != null) combat.OnDealtHit += OnDealtHit;
    }

    void Unhook()
    {
        if (combat != null) combat.OnDealtHit -= OnDealtHit;
        combat = null;
        player = null;
        cam = null;
    }

    void OnDealtHit(float damage, IDamageable target)
    {
        if (!(target is Component c) || c == null || target.IsDead) return;   // 射箭打中（target = null）不算
        enemy = c.transform;
        enemyHealth = target;
        lastHitAt = Time.time;
    }

    void LateUpdate()
    {
        if (player == null || combat == null) { Hook(); if (player == null) return; }
        if (cam == null) cam = FindAnyObjectByType<ThirdPersonCamera>();
        if (cam == null) return;

        float dt = Time.unscaledDeltaTime;
        Vector2 look = PadInput.Look;
        bool stickHeld = look.sqrMagnitude > 0.2f * 0.2f;
        releasedFor = stickHeld ? 0f : releasedFor + dt;

        if (!Usable() || stickHeld) { correcting = false; return; }

        Vector3 from = player.transform.position, to = enemy.position - from;
        to.y = 0f;
        float want = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
        float off = Mathf.Abs(Mathf.DeltaAngle(cam.yaw, want));
        if (!correcting)
        {
            if (off <= maxDeviation || releasedFor < releaseDelay) return;
            correcting = true;
            yawVel = pitchVel = 0f;
        }

        // 轉回去：水平轉到正對敵人，上下轉到看得到牠（敵人比較高就抬一點、比較低就壓一點）
        float wantPitch = Mathf.Clamp(12f + Mathf.Atan2(from.y + 1.2f - EnemyCenterY(), Mathf.Max(1f, to.magnitude)) * Mathf.Rad2Deg * 0.6f,
                                      cam.minPitch + 5f, Mathf.Min(cam.maxPitch, 40f));
        cam.yaw = Mathf.SmoothDampAngle(cam.yaw, want, ref yawVel, turnTime, 720f, dt);
        cam.pitch = Mathf.Clamp(Mathf.SmoothDamp(cam.pitch, wantPitch, ref pitchVel, turnTime * 1.4f, 240f, dt), cam.minPitch, cam.maxPitch);
        if (Mathf.Abs(Mathf.DeltaAngle(cam.yaw, want)) < 4f) correcting = false;
    }

    /// <summary>現在可以校正嗎：有上一個打中的敵人（還活著、沒忘、不太遠），用手把，而且不在瞄準 / 過場 / 選單 / 對話</summary>
    bool Usable()
    {
        if (enemy == null || !enemy.gameObject.activeInHierarchy || enemyHealth == null || enemyHealth.IsDead) { enemy = null; return false; }
        if (Time.time - lastHitAt > forgetAfter) return false;
        if ((enemy.position - player.transform.position).sqrMagnitude > maxDistance * maxDistance) return false;
        if (player.IsDead || !PadInput.UsingPadRecently) return false;
        if (cam.AimBlend > 0.05f || SkillAimCancellation.IsAiming) return false;
        if (GameUI.OverlayOpen || StoryDialogue.Active || VitalsHud.Hidden || Time.timeScale <= 0f) return false;
        if (Cursor.lockState != CursorLockMode.Locked) return false;
        return true;
    }

    /// <summary>敵人身體中間的高度（模型的外框；找不到就腳底往上 1 m）</summary>
    float EnemyCenterY()
    {
        var r = enemy.GetComponentInChildren<Renderer>();
        return r != null ? r.bounds.center.y : enemy.position.y + 1f;
    }
}
