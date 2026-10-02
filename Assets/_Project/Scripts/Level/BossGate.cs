using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Boss 房的大門。
///   1. 玩家走到門前 → 顯示「按 E 推開大門」→ 門沉入地面
///   2. 玩家走進競技場（越過門線 enterDepth 公尺）→ 門升起封住退路，Boss 戰開始
///   3. Boss 被擊敗 → 門打開
///   4. 玩家死亡重生 → 門關回去，等玩家再次推開
///
/// 物件的藍色 Z 軸（forward）要指向競技場內部。
/// 如果遊戲開始時玩家已經在競技場裡（例如單獨測試 Boss 房的場景），會直接開打。
/// </summary>
public class BossGate : MonoBehaviour
{
    [Header("參考")]
    public BossController boss;
    [Tooltip("會升降的門板")]
    public Transform door;

    [Header("互動")]
    public Key interactKey = Key.E;
    [Tooltip("離門多近可以開門 (m)")]
    public float interactDistance = 4f;
    [Tooltip("越過門線多少公尺算是進入競技場")]
    public float enterDepth = 2.5f;

    [Header("門的動畫")]
    [Tooltip("開門時門往下沉多少 (m)")]
    public float openDepth = 7.5f;
    public float openTime = 1.2f;
    public float closeTime = 0.6f;

    enum GateState { Closed, Open, Sealed, Defeated }
    GateState state = GateState.Closed;

    Transform player;
    PlayerHealth playerHealth;
    Vector3 doorClosedLocal;
    Coroutine doorRoutine;

    void Awake()
    {
        if (boss == null) boss = transform.root.GetComponentInChildren<BossController>();
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        if (door != null) doorClosedLocal = door.localPosition;
    }

    void Start()
    {
        if (boss != null)
        {
            boss.OnFightReset += ResetGate;
            boss.OnDefeated += OnBossDefeated;
        }

        // 單獨測試 Boss 房：玩家一開始就在場內 → 直接開打
        if (player != null && PlayerDepth() > enterDepth && boss != null && !boss.FightActive)
        {
            state = GateState.Sealed;
            boss.BeginFight();
        }
    }

    void OnDestroy()
    {
        if (boss != null)
        {
            boss.OnFightReset -= ResetGate;
            boss.OnDefeated -= OnBossDefeated;
        }
    }

    /// <summary>玩家在門線的哪一邊：正數 = 競技場內，負數 = 走廊</summary>
    float PlayerDepth() => Vector3.Dot(player.position - transform.position, transform.forward);

    bool PlayerCanInteract()
    {
        if (player == null || (playerHealth != null && playerHealth.IsDead)) return false;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude <= interactDistance && PlayerDepth() < 0f;
    }

    void Update()
    {
        if (player == null) return;

        switch (state)
        {
            case GateState.Closed:
                if (PlayerCanInteract() && Keyboard.current != null && Keyboard.current[interactKey].wasPressedThisFrame)
                {
                    state = GateState.Open;
                    MoveDoor(true, openTime);
                    GameAudio.Play("SFX/RPG/doorOpen_1", door != null ? door.position : transform.position, 1f, 0.55f, 0f, 1f, 50f);
                    GameAudio.Play("SFX/RPG/creak1", door != null ? door.position : transform.position, 0.8f, 0.6f, 0f, 1f, 50f);
                }
                break;

            case GateState.Open:
                if (PlayerDepth() > enterDepth)
                {
                    state = GateState.Sealed;
                    MoveDoor(false, closeTime);
                    GameAudio.Play("SFX/RPG/doorClose_4", door != null ? door.position : transform.position, 1f, 0.5f, 0f, 1f, 60f);
                    GameAudio.Play("SFX/rock_hit", door != null ? door.position : transform.position, 0.5f, 1.2f, 0f, 1f, 60f);
                    ThirdPersonCamera.ShakeMain(0.2f, 0.3f);
                    if (boss != null) boss.BeginFight();
                }
                break;
        }
    }

    void ResetGate()
    {
        if (state == GateState.Defeated) return;
        state = GateState.Closed;
        MoveDoor(false, 0f);
    }

    void OnBossDefeated()
    {
        state = GateState.Defeated;
        MoveDoor(true, openTime);
    }

    void MoveDoor(bool open, float duration)
    {
        if (door == null) return;
        if (doorRoutine != null) StopCoroutine(doorRoutine);
        doorRoutine = StartCoroutine(DoorRoutine(doorClosedLocal + (open ? Vector3.down * openDepth : Vector3.zero), duration));
    }

    IEnumerator DoorRoutine(Vector3 target, float duration)
    {
        Vector3 from = door.localPosition;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            door.localPosition = Vector3.Lerp(from, target, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }
        door.localPosition = target;
        doorRoutine = null;
    }

    void OnGUI()
    {
        if (state != GateState.Closed || !PlayerCanInteract()) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 40f), $"按 {interactKey} 推開大門", style);
    }

    void OnDrawGizmos()
    {
        // 黃線 = 門線，綠線 = 進入競技場的判定線
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position - transform.right * 3f, transform.position + transform.right * 3f);
        Gizmos.color = Color.green;
        Vector3 e = transform.position + transform.forward * enterDepth;
        Gizmos.DrawLine(e - transform.right * 3f, e + transform.right * 3f);
        Gizmos.DrawRay(transform.position, transform.forward * 2f);
    }
}
