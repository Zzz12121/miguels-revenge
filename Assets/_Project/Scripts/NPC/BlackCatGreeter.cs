using UnityEngine;

/// <summary>
/// 黑貓 NPC：平常站著待機（BC_Idle），玩家走近就播 GIF 那段「搓手 → 甩開 → 大力拍手 → 摀嘴偷笑 → 舉手 → 指人 → 揮開」（BC_Gesture）。
/// 玩家還在範圍內就一直重播；走遠之後會把正在播的那一輪做完，再回到待機。
/// 拍手音效：動畫連拍 4 下（節奏照 IG 短片），每一下兩手碰到時播對應的那一聲（第 1 下 = 短片裡的第 1 聲…）。
/// 嘴巴：手摀住嘴之後，把貼圖換成「沒有人類牙齒」的版本（像 GIF 一樣笑容消失），下一輪開始 / 回到待機時再換回來。
/// 用法：把 PF_BlackCat 拖進場景就好（選單 GeminiGrowUp > 黑貓 > 建立 Prefab 並放進目前場景）。
/// </summary>
[DisallowMultipleComponent]
public sealed class BlackCatGreeter : MonoBehaviour
{
    [Header("觸發範圍")]
    [Tooltip("玩家距離小於這個值（公尺）就開始拍手")]
    [Min(.5f)] public float triggerRadius = 3.5f;
    [Tooltip("玩家距離大於這個值才停（比觸發範圍大一點，避免在邊界一直切換）")]
    [Min(.5f)] public float exitRadius = 4.5f;

    [Header("轉向玩家")]
    public bool facePlayer = true;
    [Tooltip("每秒最多轉幾度")]
    [Min(0f)] public float turnSpeed = 220f;

    [Header("動畫")]
    [Tooltip("拍手動作的播放速度（1 = 跟 GIF 一樣快）")]
    [Range(.3f, 2f)] public float gestureSpeed = 1f;
    [Tooltip("玩家沒走開也要停一下：每做完一輪，休息幾秒再做下一輪（0 = 連續重播）")]
    [Min(0f)] public float pauseBetweenLoops = 0f;

    [Header("嘴巴（摀嘴之後笑容消失）")]
    public bool hideGrinAfterCover = true;
    [Tooltip("拍手動作播到第幾秒時把牙齒藏起來（動畫的秒數；手剛好摀住嘴巴的時候換，看不出來）")]
    [Min(0f)] public float hideMouthAt = 3.89f;
    [Tooltip("有牙齒的貼圖（平常）")]
    public Texture grinTexture;
    [Tooltip("沒有牙齒的貼圖（摀嘴之後）")]
    public Texture plainTexture;

    [Header("拍手音效")]
    [Tooltip("拍手聲（照短片裡的順序：第 1 個 = 第 1 下拍手）")]
    public AudioClip[] clapSounds;
    [Tooltip("拍手動作裡每一下兩手碰到的時間（動畫的秒數）")]
    public float[] clapTimes = { 1.09f, 1.87f, 2.60f, 3.23f };
    [Tooltip("音效檔開頭到「啪」之間的空白（秒）；提早這麼多開始播，聲音才會剛好對上")]
    [Min(0f)] public float clapSoundLead = .012f;
    [Range(0f, 1f)] public float clapVolume = 1f;
    [Tooltip("勾選 = 每一下隨機挑一聲（不會連續同一聲）；不勾 = 第 N 下播第 N 聲（跟短片一樣）")]
    public bool randomClap = false;
    [Tooltip("每一聲的音高隨機偏移（0.02 = ±2%）")]
    [Range(0f, .2f)] public float clapPitchJitter = .02f;
    [Tooltip("留空會自動加一個 3D AudioSource")]
    public AudioSource audioSource;

    [Tooltip("留空會自動找子物件裡的 Animator")]
    public Animator animator;
    [Tooltip("留空會自動找玩家（ThirdPersonController，找不到就找 Tag = Player）")]
    public Transform player;

    static readonly int NearHash = Animator.StringToHash("Near");
    static readonly int GestureSpeedHash = Animator.StringToHash("GestureSpeed");
    static readonly int GestureHash = Animator.StringToHash("Gesture");

    bool near;
    float restUntil;
    bool wasInGesture;
    float nextSearch;
    Renderer[] renderers;
    MaterialPropertyBlock block;
    int mouthState = -1; // -1 = 還沒設定、0 = 有牙齒、1 = 沒有牙齒
    static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
    static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    float lastGestureTime = -1f;   // 上一幀拍手動作的 normalizedTime（-1 = 不在拍手動作裡）
    int lastClap = -1;

    public bool PlayerNear => near;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        renderers = GetComponentsInChildren<Renderer>(true);
        block = new MaterialPropertyBlock();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 20f;
        }
    }

    void Update()
    {
        if (player == null && Time.time >= nextSearch) { nextSearch = Time.time + 1f; FindPlayer(); }
        if (animator == null) return;

        float distance = float.MaxValue;
        Vector3 toPlayer = Vector3.zero;
        if (player != null && player.gameObject.activeInHierarchy)
        {
            toPlayer = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
            distance = toPlayer.magnitude;
        }
        near = near ? distance <= Mathf.Max(exitRadius, triggerRadius) : distance <= triggerRadius;

        // 每輪做完之後的休息時間
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool inGesture = state.shortNameHash == GestureHash && !animator.IsInTransition(0);
        if (wasInGesture && !inGesture && pauseBetweenLoops > 0f) restUntil = Time.time + pauseBetweenLoops;
        wasInGesture = inGesture;
        if (inGesture && pauseBetweenLoops > 0f && state.normalizedTime >= .98f) restUntil = Time.time + pauseBetweenLoops;

        animator.SetBool(NearHash, near && Time.time >= restUntil);
        animator.SetFloat(GestureSpeedHash, gestureSpeed);

        UpdateMouth(state);
        UpdateClapSound(state);

        if (facePlayer && near && distance > .05f)
        {
            Quaternion look = Quaternion.LookRotation(toPlayer / distance, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }
    }

    void UpdateClapSound(AnimatorStateInfo state)
    {
        if (state.shortNameHash != GestureHash || clapSounds == null || clapSounds.Length == 0 || audioSource == null) { lastGestureTime = -1f; return; }
        var clips = animator.GetCurrentAnimatorClipInfo(0);
        float length = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.length : state.length;
        if (length <= .01f) return;
        // Update 在 Animator 算這一幀之前執行：先預估這一幀播完會到哪裡，聲音才不會晚一幀
        float now = state.normalizedTime + Time.deltaTime * Mathf.Max(0f, gestureSpeed * animator.speed) / length;
        float before = lastGestureTime < 0f ? Mathf.Min(now, 0f) : lastGestureTime;
        if (clapTimes != null)
            for (int j = 0; j < clapTimes.Length; j++)
            {
                float mark = Mathf.Max(0f, clapTimes[j] - clapSoundLead / Mathf.Max(.01f, gestureSpeed)) / length;
                // 這一幀之間有沒有經過第 j 下「啪」（含循環到下一輪的情況）
                float k = Mathf.Floor(before - mark) + 1f;
                if (before < k + mark && k + mark <= now) PlayClap(j);
            }
        lastGestureTime = now;
    }

    void PlayClap(int index)
    {
        int n = clapSounds.Length, i;
        if (randomClap && n > 1) { i = Random.Range(0, n - 1); if (i >= lastClap && lastClap >= 0) i++; }
        else i = index % n;
        lastClap = i;
        var clip = clapSounds[i];
        if (clip == null) return;
        audioSource.pitch = 1f + Random.Range(-clapPitchJitter, clapPitchJitter);
        audioSource.PlayOneShot(clip, clapVolume);
    }

    void UpdateMouth(AnimatorStateInfo state)
    {
        bool plain = false;
        if (hideGrinAfterCover && plainTexture != null && state.shortNameHash == GestureHash)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            float length = clips.Length > 0 && clips[0].clip != null ? clips[0].clip.length : state.length;
            float t = (state.normalizedTime - Mathf.Floor(state.normalizedTime)) * length;
            plain = t >= hideMouthAt;
            // 走開時：從拍手接回待機的過渡期間維持沒有牙齒，等真的回到待機才換回來
            if (animator.IsInTransition(0) && state.normalizedTime >= 1f) plain = true;
        }
        SetMouth(plain);
    }

    void SetMouth(bool plain)
    {
        int want = plain ? 1 : 0;
        if (want == mouthState || renderers == null) return;
        Texture tex = plain ? plainTexture : grinTexture;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            if (tex != null) { block.SetTexture(BaseMapId, tex); block.SetTexture(MainTexId, tex); }
            else block.Clear();
            r.SetPropertyBlock(block);
        }
        mouthState = want;
    }

    void OnDisable() { mouthState = -1; if (renderers != null) foreach (var r in renderers) if (r != null) r.SetPropertyBlock(null); }

    void FindPlayer()
    {
        var motor = FindAnyObjectByType<ThirdPersonController>();
        if (motor != null) { player = motor.transform; return; }
        try
        {
            var tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null) player = tagged.transform;
        }
        catch (UnityException) { }
    }

    void OnValidate()
    {
        if (exitRadius < triggerRadius) exitRadius = triggerRadius;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, .8f, .2f, .8f);
        DrawCircle(triggerRadius);
        Gizmos.color = new Color(1f, .8f, .2f, .3f);
        DrawCircle(exitRadius);
    }

    void DrawCircle(float r)
    {
        Vector3 c = transform.position + Vector3.up * .05f, prev = c + Vector3.forward * r;
        for (int i = 1; i <= 48; i++)
        {
            float a = i * Mathf.PI * 2f / 48f;
            Vector3 p = c + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * r;
            Gizmos.DrawLine(prev, p); prev = p;
        }
    }
}
