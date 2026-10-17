using System.Collections.Generic;
using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    private Dictionary<string, int> resources = new Dictionary<string, int>();

    public void AddResource(string name, int amount)
    {
        if (resources.ContainsKey(name))
            resources[name] += amount;
        else
            resources[name] = amount;
    }

    public int GetResource(string name)
    {
        return resources.ContainsKey(name) ? resources[name] : 0;
    }

    /// <summary>所有資源的複本（換關卡 / 存檔用）</summary>
    public Dictionary<string, int> GetAll() => new Dictionary<string, int>(resources);

    /// <summary>整個背包換成指定內容（換關卡 / 讀檔用）</summary>
    public void SetAll(Dictionary<string, int> all) => resources = new Dictionary<string, int>(all);

    public bool ConsumeResource(string name, int amount)
    {
        if (GetResource(name) >= amount)
        {
            resources[name] -= amount;
            return true;
        }
        return false;
    }
}