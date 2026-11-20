using UnityEngine;

/// <summary>
/// 會問玩家問題的村民（例如行商問玩家從哪裡來）：第一次對話講完 MerchantNPC.guideLines 之後問 question，
/// 玩家選了第幾個就講 replies 的第幾段；之後再來對話改講 repeatAfter 的第幾段（記得玩家的回答）。
/// 一段台詞裡用 '|' 分成好幾句。
/// </summary>
[RequireComponent(typeof(MerchantNPC))]
public class NpcQuestion : MonoBehaviour
{
    [TextArea] public string question;
    public string[] options;
    [Tooltip("選了第幾個就講第幾段（'|' 分句）")]
    [TextArea] public string[] replies;
    [Tooltip("回答過之後再來對話：照玩家選的講第幾段（'|' 分句）")]
    [TextArea] public string[] repeatAfter;

    /// <summary>玩家選了第幾個（還沒回答 = -1）</summary>
    public int Answer { get; private set; } = -1;

    MerchantNPC npc;
    bool asking;

    void Start()
    {
        npc = GetComponent<MerchantNPC>();
        npc.linesProvider = () =>
        {
            asking = Answer < 0;
            if (asking) return npc.guideLines;
            return Split(repeatAfter, Answer) ?? npc.repeatLines;
        };
        npc.choiceProvider = () => !asking || options == null || options.Length == 0 ? null : new DialogueChoice
        {
            question = question,
            options = options,
            onPick = i =>
            {
                Answer = i;
                return Split(replies, i);
            },
        };
    }

    static string[] Split(string[] parts, int i) =>
        parts != null && i >= 0 && i < parts.Length && !string.IsNullOrEmpty(parts[i]) ? parts[i].Split('|') : null;
}
