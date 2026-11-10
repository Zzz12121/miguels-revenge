using UnityEngine;

/// <summary>
/// 鐵匠鋪門口的武器架：玩家沒帶在身上的武器擺在架上，帶走的那一組就從架上消失。
/// </summary>
public class WeaponRackDisplay : MonoBehaviour
{
    [Tooltip("架上的大刀")]
    public GameObject greatsword;
    [Tooltip("架上的弓和小刀")]
    public GameObject bowKnife;
    [Tooltip("架上的長槍")]
    public GameObject spear;

    void Update()
    {
        var current = PlayerLoadout.Current;
        Set(greatsword, current != WeaponLoadout.Greatsword);
        Set(bowKnife, current != WeaponLoadout.BowKnife);
        Set(spear, current != WeaponLoadout.Spear);
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }
}
