using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 鐵匠鋪門口的女鐵匠（掛在她的 MerchantNPC 上）：第一次對話教玩家怎麼換武器、說明每種武器的特性；
/// 之後每次對話都可以選要帶哪一組（PlayerLoadout.Equip），選弓的時候箭補滿 20 支。
/// </summary>
[RequireComponent(typeof(MerchantNPC))]
public class BlacksmithShop : MonoBehaviour
{
    MerchantNPC npc;
    bool met;

    static string Name(WeaponLoadout w) => w == WeaponLoadout.BowKnife ? "弓與小刀" : w == WeaponLoadout.Spear ? "長槍" : "大刀";

    static readonly string[] Intro =
    {
        "喲，外地來的劍客？我是這間鐵匠鋪的鐵匠，村裡的刀、槍、弓，全都出自我這雙手。",
        "身上的傢伙不合用，就來找我換。門口架子上擺的，就是你現在沒帶在身上的。",
    };

    static string[] Traits(WeaponLoadout w)
    {
        switch (w)
        {
            case WeaponLoadout.BowKnife:
                return new[]
                {
                    $"弓與小刀：按住右鍵瞄準、左鍵放箭，對手還沒走近就打得到他。不過箭只有 {PlayerLoadout.MaxArrows} 支，射完就只剩腰上的小刀。",
                    "小刀搆不遠，得貼到對手面前，可是出手比大刀快得多。箭用完了回來找我，再幫你補滿。拿著弓的時候沒辦法舉盾，小心點。",
                };
            case WeaponLoadout.Spear:
                return new[]
                {
                    "長槍：用戳的，打得最遠，可以站在對手搆不到你的地方出手。",
                    "只是槍頭窄，左右掃不到，要正對著目標；對手貼得太近反而不好使。",
                };
            default:
                return new[]
                {
                    "大刀：一刀橫掃一大片，被好幾個人圍住的時候最好用，右鍵還能舉盾擋招。我幫你把刀身又加長了一截，掃得比以前更遠。",
                };
        }
    }

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        npc.linesProvider = Lines;
        npc.choiceProvider = Choice;
    }

    string[] Lines()
    {
        var lines = new List<string>();
        if (!met)
        {
            met = true;
            lines.AddRange(Intro);
            foreach (var w in new[] { WeaponLoadout.Greatsword, WeaponLoadout.BowKnife, WeaponLoadout.Spear }) lines.AddRange(Traits(w));
            lines.Add("想換哪一把，跟我說一聲就行。");
            return lines.ToArray();
        }
        lines.Add($"你現在帶的是{Name(PlayerLoadout.Current)}。" + (PlayerLoadout.Current == WeaponLoadout.BowKnife
            ? (PlayerLoadout.Arrows > 0 ? $"箭還剩 {PlayerLoadout.Arrows} 支。" : "箭都射完了吧？")
            : ""));
        lines.Add("要換傢伙嗎？選了之後我會再跟你說說那把兵器怎麼用。");
        return lines.ToArray();
    }

    DialogueChoice Choice()
    {
        bool refill = PlayerLoadout.Current == WeaponLoadout.BowKnife && PlayerLoadout.Arrows < PlayerLoadout.MaxArrows;
        return new DialogueChoice
        {
            question = "要帶哪一組兵器？",
            options = new[]
            {
                "大刀",
                refill ? $"弓與小刀（補滿 {PlayerLoadout.MaxArrows} 支箭）" : $"弓與小刀（箭 {PlayerLoadout.MaxArrows} 支）",
                "長槍",
                "先不換",
            },
            onPick = i =>
            {
                if (i >= 3) return new[] { "好，需要的時候再來。" };
                var w = i == 0 ? WeaponLoadout.Greatsword : i == 1 ? WeaponLoadout.BowKnife : WeaponLoadout.Spear;
                bool same = w == PlayerLoadout.Current;
                PlayerLoadout.Equip(w);
                GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 1.1f, 0f, 1f, 30f);
                var reply = new List<string>();
                if (same && w == WeaponLoadout.BowKnife) reply.Add($"箭幫你補滿了，{PlayerLoadout.MaxArrows} 支。");
                else if (same) reply.Add($"你手上本來就是{Name(w)}。我幫你重新磨了一下。");
                else reply.Add($"拿去，{Name(w)}。換下來的我先擺回架上。");
                reply.AddRange(Traits(w));
                return reply.ToArray();
            },
        };
    }
}
