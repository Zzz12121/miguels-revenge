using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第一關（Planet）的劇情：
///   - 開場：玩家頭暈醒來（原本應該要去地下城），遠處的商人出聲叫玩家過去，商人頭上出現指引標記
///   - 沙蟲低谷：沙蟲開口要玩家接受考驗（由 SandWormArena 呼叫 PlayWormIntro）；怎麼打寫在低谷前的石碑上（SteleInscription）
///   - 沙蟲被打倒：沙蟲說玩家逃不出這循環，玩家疑惑世界的秘密（由 SandWormArena 呼叫 PlayWormDefeat）
///   - Boss 房：玩家疑惑「這不是商人嗎？」，Boss 說商人只是棋子、自己的目標是時間的運行、要重回那個夜晚
///   - Boss 倒下：時間不會停止流動，總有一天會再奪回大權
/// 對話用 StoryDialogue（左邊玩家、右邊對方的半身像）。進入 Planet 場景時自動建立，不用放進場景。
/// </summary>
public class PlanetStory : MonoBehaviour
{
    public const string SceneName = "Planet";

    public string playerName = "米格爾";
    public string wormName = "沙蟲";
    [Tooltip("進場景後等多久才開始開場對話（等淡入結束）")]
    public float introDelay = 1.5f;

    public static readonly Color PlayerColor = new Color(0.6f, 0.85f, 1f);
    public static readonly Color MerchantColor = new Color(1f, 0.8f, 0.4f);
    public static readonly Color WormColor = new Color(0.95f, 0.72f, 0.4f);
    public static readonly Color BossColor = new Color(1f, 0.4f, 0.35f);

    static PlanetStory instance;

    PlayerHealth player;
    MerchantNPC merchant;
    ClockEnemyHealth boss;
    bool bossIntroDone;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != SceneName || FindAnyObjectByType<PlanetStory>() != null) return;
        var go = new GameObject("PlanetStory");
        SceneManager.MoveGameObjectToScene(go, scene);
        go.AddComponent<PlanetStory>();
    }

    void Awake() => instance = this;

    void Start()
    {
        player = PlayerHealth.Find();
        merchant = FindAnyObjectByType<MerchantNPC>();
        boss = FindAnyObjectByType<ClockEnemyHealth>();
        if (boss != null)
        {
            boss.suppressDeathBubble = true; // 死亡台詞改由劇情對話框說
            boss.OnDied += OnBossDied;
        }
        StartCoroutine(Intro());
    }

    void OnDestroy()
    {
        if (boss != null) boss.OnDied -= OnBossDied;
        if (instance == this) instance = null;
    }

    Transform P => player != null ? player.transform : null;
    Transform M => merchant != null ? merchant.transform : null;
    string MerchantName => merchant != null ? merchant.speakerName : "流浪商人";
    string BossName => boss != null ? boss.enemyName : "時間吞噬者";

    StoryDialogue.Line Me(string text) => StoryDialogue.L(playerName, P, false, text, PlayerColor);
    StoryDialogue.Line Merchant(string text) => StoryDialogue.L(MerchantName, M, true, text, MerchantColor);
    StoryDialogue.Line Boss(string text) => StoryDialogue.L(BossName, boss != null ? boss.transform : null, true, text, BossColor);

    // ---------------- 開場 ----------------

    IEnumerator Intro()
    {
        yield return new WaitForSecondsRealtime(introDelay);
        var gm = GameManager.Instance;
        while (gm != null && gm.IsLoading) yield return null;
        if (player == null) player = PlayerHealth.Find();

        StoryDialogue.Play(new[]
        {
            Me("（一陣頭暈目眩）……唔……這裡是哪裡？"),
            Me("我原本應該是要前往地下城的才對……怎麼會到了這片沙漠？"),
            Merchant("喂——！那邊的旅人！你沒事吧？"),
            Merchant("往這邊走！到營火這裡來，我跟你說說這是什麼地方！"),
        }, () => { if (merchant != null) merchant.StartGuide(); });
    }

    // ---------------- 沙蟲低谷 ----------------

    /// <summary>沙蟲低谷開戰前：沙蟲要玩家接受考驗（worm = 冒出來說話的沙蟲）</summary>
    public static void PlayWormIntro(Transform worm, System.Action onDone)
    {
        if (instance == null) { onDone?.Invoke(); return; }
        instance.WormIntro(worm, onDone);
    }

    public static void PlayWormDefeat(Transform worm, System.Action onDone)
    {
        if (instance == null) { onDone?.Invoke(); return; }
        instance.WormDefeat(worm, onDone);
    }

    void WormDefeat(Transform worm, System.Action onDone)
    {
        StoryDialogue.Line Worm(string text) => StoryDialogue.L(wormName, worm, true, text, WormColor);
        StoryDialogue.Play(new[]
        {
            Worm("就算你這次擊敗了我，還是逃不出這循環……"),
            Me("這個世界到底還隱藏了甚麼秘密……"),
            Worm("你永遠不會知道的。"),
        }, onDone);
    }

    void WormIntro(Transform worm, System.Action onDone)
    {
        StoryDialogue.Line Worm(string text) => StoryDialogue.L(wormName, worm, true, text, WormColor);
        StoryDialogue.Play(new[]
        {
            Worm("何人……膽敢踏入吾之沙穴……"),
            Me("什、什麼東西！？地底下有聲音！"),
            Worm("欲過此地，必先接受吾之考驗。記住大地的脈動——走錯一步，便葬身沙海。"),
            Worm("考驗，開始。"),
        }, onDone);
    }

    // ---------------- Boss ----------------

    void Update()
    {
        if (bossIntroDone || boss == null || boss.IsDead || !boss.ShowBar) return;
        bossIntroDone = true;
        StoryDialogue.Play(new[]
        {
            Me("……等等，這不是商人嗎？怎麼會是你！？"),
            Boss("商人？呵呵呵……那傢伙不過是我的一枚棋子罷了。"),
            Boss("把你引到這顆星球、讓你一路變強……全都在我的計畫之中。"),
            Boss("我的目標，是時間的運行本身。只要奪回它，我就能重回那個夜晚！"),
            Me("那個夜晚……？你到底在說什麼！"),
            Boss("多說無益。接受時間的審判吧！"),
        });
    }

    void OnBossDied() => StartCoroutine(BossDeath());

    IEnumerator BossDeath()
    {
        yield return new WaitForSecondsRealtime(1.2f); // 先讓 Boss 倒下
        StoryDialogue.Play(new[]
        {
            Boss("呃……就算我倒下……時間也不會停止流動……"),
            Boss("總有一天……我會再奪回大權……"),
        });
    }
}
