using UnityEngine;

/// <summary>
/// 女書生的秘密：跟她說到第 talksNeeded 次話，她偷偷告訴玩家已經把村長家後院的門打開了（這是秘密），
/// 講完這次對話就把門解鎖並推開（DojoGate.Unlock + Push），不用等完成祠堂的試煉。
/// 門已經開了（試煉做完、或已經講過秘密）就講別的台詞。
/// </summary>
[RequireComponent(typeof(MerchantNPC))]
public class SecretGateOpener : MonoBehaviour
{
    public DojoGate gate;
    [Tooltip("第幾次對話時說出秘密")]
    public int talksNeeded = 3;
    [TextArea] public string[] secretLines =
    {
        "又是你……這已經是第三次了。看來你是真的想知道玄清道人的下落。", "@我想知道真相。",
        "跟你說個秘密：村長家後院那扇老門，開門的法子就寫在一本舊書的夾頁裡，我抄書的時候發現的。",
        "我已經偷偷把門打開了。你現在去村長家後院，直接走過去就行。",
        "記住，這是我們之間的秘密。千萬別讓村長知道是我做的。", "@放心，我不會說出去的。",
    };
    [Tooltip("門本來就開了（試煉做完）時講的")]
    [TextArea] public string[] alreadyOpenLines =
    {
        "村長家後院那扇門……看來你已經有辦法過去了。那我的秘密就派不上用場啦。",
    };
    [Tooltip("說出秘密之後再來對話")]
    [TextArea] public string[] afterLines =
    {
        "門已經開了，快去吧。……噓，別說是我。", "@知道了，謝謝你。",
    };

    public int Talks { get; private set; }
    public bool Told { get; private set; }

    MerchantNPC npc;
    bool openAfterTalk, wasTalking;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        if (gate == null) gate = FindFirstObjectByType<DojoGate>();
        npc.linesProvider = () =>
        {
            Talks++;
            if (Told) return afterLines;
            if (Talks < talksNeeded) return Talks == 1 ? npc.guideLines : npc.repeatLines;
            if (gate == null || gate.IsUnlocked) return Talks == talksNeeded ? alreadyOpenLines : npc.repeatLines;
            Told = true;
            openAfterTalk = true;
            return secretLines;
        };
    }

    void Update()
    {
        bool talking = npc.InConversation;
        if (wasTalking && !talking && openAfterTalk)
        {
            openAfterTalk = false;
            if (gate != null)
            {
                gate.Unlock();
                gate.Push();
            }
        }
        wasTalking = talking;
    }
}
