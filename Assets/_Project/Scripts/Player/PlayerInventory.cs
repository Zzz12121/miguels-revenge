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