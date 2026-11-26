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
    public float heavyHitstop = 0.075f;
    [Tooltip("打死敵人 / 打碎石頭 / 砍倒樹時額外增加")]
    public float finishBonus = 0.025f;
    [Range(0f, .12f)] public float maximumHitstop = .12f;
    [Range(0f, .03f)] public float multiHitstop = .018f;
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
    [Tooltip("血肉的低頻命中聲已由 PlayerSfx 播放；需要額外材質聲時才開啟")]
    public bool layerFleshAudio = false;

    public static bool Enabled = true;

    static PlayerHitFeedback instance;
    [Header("連續命中的回饋上限")]
    [Min(.02f)] public float effectInterval = .055f;
    [Min(.05f)] public float multiHitInterval = .10f;
    float nextEffectAt, nextMultiHitAt;
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
        if (!Enabled || target == null || dealt <= 0f) return;
        Instance.Play(target, point, heavy);
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

        bool repeatedSkill = PlayerCombat.ReportingSkillHit;
        if (repeatedSkill && Time.unscaledTime < nextMultiHitAt) return;
        if (Time.unscaledTime < nextEffectAt) return;
        nextEffectAt = Time.unscaledTime + effectInterval;
        if (repeatedSkill) nextMultiHitAt = Time.unscaledTime + multiHitInterval;
        ImpactFx.Hit(point, dir, surface, heavy, finishing);

        float stop = (heavy ? heavyHitstop : lightHitstop) + (finishing ? finishBonus : 0f);
        stop = Mathf.Min(stop, repeatedSkill ? multiHitstop : maximumHitstop);
        // 刀 / 長槍砍到血肉（不是射箭、不是多段技能）：頓幀前後加上「刀砍進去變慢」「刀抽出來變慢」兩段（各多約 0.05 秒）
        bool blade = !repeatedSkill && surface == ImpactSurface.Flesh && PlayerLoadout.BladeHit;
        CombatHitStop.Request(stop, hitstopTimeScale, repeatedSkill ? .18f : .10f,
                              blade ? CombatHitStop.BladeEnterSeconds : 0f, blade ? CombatHitStop.BladeExitSeconds : 0f);
        var camera = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (camera) camera.ImpactAt(point, finishing ? finishShake : heavy ? heavyShake : lightShake, .12f);

        string clip = surface switch { ImpactSurface.Rock => rockSound, ImpactSurface.Wood => woodSound, _ => fleshSound };
        float pitch = surface == ImpactSurface.Rock ? 1.45f : heavy ? 0.85f : 1f;
        if (surface == ImpactSurface.Flesh && !layerFleshAudio) return;
        AudioCutoff.PlayShort(clip, point, volume * (heavy ? 1f : 0.8f), pitch, maxSoundSeconds, 0.1f, 0.08f, 30f);
    }

}
