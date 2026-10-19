using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 時鐘敵人血量：底部 Boss 血條、說話系統（二階段變紅字）、受擊無敵幀、二階段觸發、死亡台詞，打倒後打開火堆旁的傳送門（傳送門一直都在，打倒 Boss 前進不去）
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class ClockEnemyHealth : MonoBehaviour, IDamageable
{
    public string enemyName = "時間吞噬者";
    public float maxHealth = 100f;
    [Tooltip("被打中時往後退的力道")]
    public float hitKnockback = 3f;
    [Tooltip("受傷後的無敵時間 (秒)，避免連續同一招扣血")]
    public float invincibleTime = 0.2f;
    [Tooltip("被打時身體閃一下的時間 (秒)")]
    public float flashTime = 0.08f;
    [Tooltip("被打時閃的顏色")]
    public Color flashColor = Color.white;

    [Header("打倒後")]
    [Tooltip("打倒後打開的傳送門（一直擺在場景裡、Start Locked 打勾）。留空 = 自動找場景裡的傳送門（第一關只有一個）")]
    public Portal exitPortal;

    [Header("說話系統設定")]
    public List<string> dialogueList = new List<string>
    {
        "「時間一分一秒流逝...」",
        "「時光可一去不復返。」",
        "「接受時間的審判吧！」",
        "「在絕對的時間領域中臣服吧！」"
    };
    public List<string> phaseTwoDialogueList = new List<string>
    {
        "「加速吧！時間的齒輪！」",
        "「你們的生命正迅速歸零！」",
        "「感受時間被剝奪的絕望吧！」"
    };
    public float dialogueInterval = 8.0f;
    public float dialogueShowDuration = 3.0f;
    
    private string currentDialogue = "";
    private bool showDialogue = false;
    private bool isPhaseTwo = false;
    private bool isDeadDialoguePlaying = false;
    private float lastHitTime = float.NegativeInfinity;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    public float LastHitTime { get; private set; } = float.NegativeInfinity;
    public bool ShowBar { get; set; } = false;
    public bool IsPhaseTwo => isPhaseTwo; // 讓 Controller 讀取二階段狀態

    public event System.Action<float> OnDamaged;
    public event System.Action OnDied;
    public event System.Action OnEnterPhaseTwo; // 通知 Controller 進入二階段震飛

    float trailingHealth;
    CharacterController cc;
    ThirdPersonController controller;
    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    bool flashing;
    static readonly RaycastHit[] fallHits = new RaycastHit[8];
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        controller = GetComponent<ThirdPersonController>();
        Health = trailingHealth = maxHealth;

        var all = new System.Collections.Generic.List<Renderer>(GetComponentsInChildren<Renderer>(true));
        all.RemoveAll(r => !r.enabled);
        renderers = all.ToArray();
        mpb = new MaterialPropertyBlock();
    }

    void Start()
    {
        StartCoroutine(DialogueRoutine());
    }

    IEnumerator DialogueRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(dialogueInterval);

            if (!IsDead && ShowBar && !isDeadDialoguePlaying)
            {
                var list = isPhaseTwo ? phaseTwoDialogueList : dialogueList;
                if (list.Count > 0)
                {
                    currentDialogue = list[Random.Range(0, list.Count)];
                    showDialogue = true;

                    yield return new WaitForSeconds(dialogueShowDuration);
                    showDialogue = false;
                }
            }
        }
    }

    public void TakeDamage(float amount, Vector3 hitPoint)
    {
        if (IsDead || amount <= 0f) return;

        // **無敵幀機制檢查**
        if (Time.time - lastHitTime < invincibleTime) return;
        lastHitTime = Time.time;

        Health = Mathf.Max(0f, Health - amount);
        LastHitTime = Time.time;

        // 檢查是否剛好從一階段跨入二階段 (< 40%)
        if (!isPhaseTwo && (Health / maxHealth) <= 0.4f)
        {
            isPhaseTwo = true;
            Debug.Log("【Boss 進入二階段】觸發震飛機制！");
            OnEnterPhaseTwo?.Invoke(); // 觸發二階段震飛事件
        }

        if (controller != null && controller.enabled)
        {
            Vector3 away = transform.position - hitPoint;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -transform.forward;
            controller.AddImpulse(away.normalized * hitKnockback + Vector3.up * 1.5f);
        }

        // 受傷音效：時鐘被打中的金屬聲
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.85f, Random.Range(0.85f, 1.05f), 0f, 1f, 35f);
        OnDamaged?.Invoke(amount);

        if (Health <= 0f) Die();
    }

    public void Kill()
    {
        if (!IsDead) { Health = 0f; Die(); }
    }

    public void HealFull()
    {
        if (!IsDead)
        {
            Health = trailingHealth = maxHealth;
            isPhaseTwo = false;
        }
    }

    void Die()
    {
        if (IsDead) return;
        IsDead = true;
        ShowBar = false;
        
        StartCoroutine(PlayDeathDialogueRoutine());
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.8f, 0.7f, 0.08f, 1f, 30f);

        if (exitPortal == null) exitPortal = FindAnyObjectByType<Portal>();
        if (exitPortal != null) exitPortal.Unlock();
        else Debug.LogWarning("[ClockEnemyHealth] 場景裡找不到傳送門，打倒 Boss 後沒辦法離開這一關");

        if (controller != null) controller.enabled = false;
        OnDied?.Invoke();
        StartCoroutine(DeathRoutine());
    }

    IEnumerator PlayDeathDialogueRoutine()
    {
        isDeadDialoguePlaying = true;
        currentDialogue = "「時間永不停息...」";
        showDialogue = true;
        yield return new WaitForSeconds(3.5f);
        showDialogue = false;
    }

    IEnumerator DeathRoutine()
    {
        cc.enabled = false;
        SetFlash(false);

        float feetOffset = cc.height * 0.5f - cc.center.y;
        float vy = 0f, fallen = 0f;
        while (fallen < 3f)
        {
            Vector3 feet = transform.position + Vector3.down * feetOffset;
            float step = Mathf.Max(0f, -vy * Time.deltaTime);
            int n = Physics.RaycastNonAlloc(feet + Vector3.up * 0.3f, Vector3.down, fallHits, 0.4f + step, ~0, QueryTriggerInteraction.Ignore);
            float ground = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
                if (!(fallHits[i].collider is CharacterController) && fallHits[i].point.y > ground) ground = fallHits[i].point.y;
            if (!float.IsNegativeInfinity(ground))
            {
                transform.position += Vector3.up * (ground - feet.y);
                break;
            }
            vy -= 25f * Time.deltaTime;
            transform.position += Vector3.up * vy * Time.deltaTime;
            fallen += Time.deltaTime;
            yield return null;
        }

        float t = 0f;
        Quaternion from = transform.rotation;
        Quaternion to = from * Quaternion.Euler(-80f, 0f, 0f);
        while (t < 0.4f)
        {
            t += Time.deltaTime;
            transform.rotation = Quaternion.Slerp(from, to, t / 0.4f);
            yield return null;
        }
        yield return new WaitForSeconds(1f);

        Vector3 p = transform.position;
        t = 0f;
        while (t < 0.8f)
        {
            t += Time.deltaTime;
            transform.position = p + Vector3.down * (t / 0.8f) * 1.2f;
            yield return null;
        }
        foreach (var r in renderers) if (r != null) r.enabled = false;
    }

    public void Revive()
    {
        StopAllCoroutines();
        StartCoroutine(DialogueRoutine());
        IsDead = false;
        isPhaseTwo = false;
        isDeadDialoguePlaying = false;
        Health = trailingHealth = maxHealth;
        LastHitTime = float.NegativeInfinity;
        foreach (var r in renderers) if (r != null) r.enabled = true;
        flashing = true;
        SetFlash(false);
        cc.enabled = true;
        if (controller != null) controller.enabled = true;
    }

    void Update()
    {
        if (Time.time - LastHitTime > 0.6f)
            trailingHealth = Mathf.MoveTowards(trailingHealth, Health, maxHealth * 0.5f * Time.deltaTime);
        if (trailingHealth < Health) trailingHealth = Health;

        if (!IsDead) SetFlash(Time.time - LastHitTime < flashTime);
    }

    void SetFlash(bool on)
    {
        if (on == flashing) return;
        flashing = on;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            mpb.Clear();
            if (on)
            {
                mpb.SetColor(BaseColorId, flashColor);
                mpb.SetColor(ColorId, flashColor);
            }
            r.SetPropertyBlock(mpb);
        }
    }

    void OnGUI()
    {
        if (!ShowBar) return;

        float w = Screen.width * 0.55f, h = 14f;
        float x = (Screen.width - w) * 0.5f, y = Screen.height - 70f;

        if (showDialogue)
        {
            var speechStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y - 55f, w, 32f), Texture2D.whiteTexture);

            GUI.color = isPhaseTwo ? Color.red : Color.cyan;
            GUI.Label(new Rect(x, y - 52f, w, 32f), currentDialogue, speechStyle);
            GUI.color = Color.white;
        }

        var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
        GUI.color = Color.white;
        GUI.Label(new Rect(x, y - 30f, w, 24f), enemyName, nameStyle);

        GUI.color = new Color(0f, 0f, 0f, 0.7f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 0.85f, 0.5f, 0.9f);
        GUI.DrawTexture(new Rect(x, y, w * (trailingHealth / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = new Color(0.7f, 0.08f, 0.08f);
        GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);
        
        GUI.color = Color.white;
    }
}