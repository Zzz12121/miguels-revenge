using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家攻擊的打擊感：命中瞬間
///   1. 頓幀（hitstop）：時間短暫放慢，刀砍進去的那一下會「卡」一下
///   2. 額外鏡頭震動（重擊、擊破更大）
///   3. 依被打的材質噴不同特效（ImpactFx）：血肉 = 白金火花、石頭 = 橘火花＋石屑＋粉塵、木頭 = 木屑
///   4. 依材質播放打擊音效
///
/// 只聽 PlayerCombat.OnHitLanded 事件，不改玩家攻擊的判定與傷害。
/// 場景裡沒有這個元件時，第一次命中會自動建立一個（用預設數值）；
/// 想調數值就把它放進場景（孤島森林場景生成器會放一個）。
/// </summary>
public class PlayerHitFeedback : MonoBehaviour
{
    [Header("頓幀（秒，真實時間）")]
    public float lightHitstop = 0.045f;
    public float heavyHitstop = 0.09f;
    [Tooltip("打死敵人 / 打碎石頭 / 砍倒樹時額外增加")]
    public float finishBonus = 0.06f;
    [Range(0f, 1f)] public float hitstopTimeScale = 0.05f;

    [Header("鏡頭震動（疊加在原本的震動上）")]
    public float lightShake = 0.05f;
    public float heavyShake = 0.14f;
    public float finishShake = 0.2f;

    [Header("音效")]
    public float volume = 0.8f;
    public string fleshSound = "SFX/RPG/knifeSlice";
    public string rockSound = "SFX/rock_hit";
    public string woodSound = "SFX/RPG/chop";
    [Tooltip("打擊音效最長幾秒（rock_hit 原檔有 4 秒）")]
    public float maxSoundSeconds = 0.35f;

    public static bool Enabled = true;

    static PlayerHitFeedback instance;
    float hitstopEnd;
    bool hitstopActive;
    float savedTimeScale = 1f;
    Transform player;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        PlayerCombat.OnHitLanded -= Handle;
        PlayerCombat.OnHitLanded += Handle;
    }

    static PlayerHitFeedback Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<PlayerHitFeedback>();
            if (instance == null) instance = new GameObject("PlayerHitFeedback (auto)").AddComponent<PlayerHitFeedback>();
            return instance;
        }
    }

    static void Handle(IDamageable target, Vector3 point, float dealt, bool heavy)
    {
        if (!Enabled || target == null) return;
        Instance.Play(target, point, heavy);
    }

    void OnDisable()
    {
        if (hitstopActive && Mathf.Approximately(Time.timeScale, hitstopTimeScale)) Time.timeScale = savedTimeScale;
        hitstopActive = false;
    }

    void Play(IDamageable target, Vector3 point, bool heavy)
    {
        var surface = target is IImpactSurface s ? s.Surface : ImpactSurface.Flesh;
        bool finishing = target.IsDead;

        if (player == null)
        {
            var pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null) player = pc.transform;
        }
        Vector3 dir = player != null ? point - player.position : Vector3.forward;
        dir.y = 0f;

        ImpactFx.Hit(point, dir, surface, heavy, finishing);

        float stop = (heavy ? heavyHitstop : lightHitstop) + (finishing ? finishBonus : 0f);
        StartHitstop(stop);
        ThirdPersonCamera.ShakeMain(finishing ? finishShake : heavy ? heavyShake : lightShake, 0.12f);

        string clip = surface switch { ImpactSurface.Rock => rockSound, ImpactSurface.Wood => woodSound, _ => fleshSound };
        float pitch = surface == ImpactSurface.Rock ? 1.45f : heavy ? 0.85f : 1f;
        AudioCutoff.PlayShort(clip, point, volume * (heavy ? 1f : 0.8f), pitch, maxSoundSeconds, 0.1f, 0.08f, 30f);
    }

    void StartHitstop(float seconds)
    {
        if (seconds <= 0f) return;
        hitstopEnd = Mathf.Max(hitstopEnd, Time.unscaledTime + seconds);
        if (hitstopActive) return;
        // 已經有其他系統在調時間（暫停、慢動作、劇情）就不插手
        if (Time.timeScale < 0.99f) return;
        savedTimeScale = Time.timeScale;
        hitstopActive = true;
        Time.timeScale = hitstopTimeScale;
        StartCoroutine(HitstopRoutine());
    }

    IEnumerator HitstopRoutine()
    {
        while (Time.unscaledTime < hitstopEnd) yield return null;
        // 頓幀期間按了暫停：等解除暫停後再恢復
        while (Time.timeScale == 0f) yield return null;
        if (Mathf.Approximately(Time.timeScale, hitstopTimeScale)) Time.timeScale = savedTimeScale;
        hitstopActive = false;
    }
}
