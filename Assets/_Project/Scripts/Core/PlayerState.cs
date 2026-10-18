using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家身上要跨關卡保留 / 寫進存檔的狀態：血量、最大生命、能力升級（跑速、攻擊）、背包資源。
/// 每一關的 PlayerRig 都是場景裡各自的一份，換關卡時 GameManager 先 Capture() 舊的、再 Apply() 到新的。
/// </summary>
[System.Serializable]
public class PlayerState
{
    public float health;
    public float maxHealth;
    public float walkSpeed;
    public float sprintSpeed;
    public float damageMultiplier;
    public float[] comboDamage;
    public string[] resourceNames;
    public int[] resourceAmounts;
    /// <summary>能力植物：第一株選的路線（PlayerPerks.Path）和拿到的特殊能力</summary>
    public int upgradePath;
    public string basicUpgrade;
    public int[] perks;

    public static GameObject FindPlayer() => GameObject.FindWithTag("Player");

    /// <summary>讀出玩家目前的狀態；找不到玩家回傳 null</summary>
    public static PlayerState Capture(GameObject player)
    {
        if (player == null) return null;
        var s = new PlayerState();

        var hp = player.GetComponent<PlayerHealth>();
        if (hp != null)
        {
            s.maxHealth = hp.maxHealth;
            s.health = hp.IsDead ? hp.maxHealth : hp.Health;
        }
        var ctrl = player.GetComponent<ThirdPersonController>();
        if (ctrl != null)
        {
            s.walkSpeed = ctrl.walkSpeed;
            s.sprintSpeed = ctrl.sprintSpeed;
        }
        var combat = player.GetComponent<PlayerCombat>();
        if (combat != null)
        {
            s.damageMultiplier = combat.damageMultiplier;
            s.comboDamage = combat.comboDamage != null ? (float[])combat.comboDamage.Clone() : null;
        }
        var inv = player.GetComponent<PlayerInventory>();
        if (inv != null)
        {
            var all = inv.GetAll();
            s.resourceNames = new string[all.Count];
            s.resourceAmounts = new int[all.Count];
            int i = 0;
            foreach (var kv in all)
            {
                s.resourceNames[i] = kv.Key;
                s.resourceAmounts[i] = kv.Value;
                i++;
            }
        }
        var pk = player.GetComponent<PlayerPerks>();
        if (pk != null)
        {
            s.upgradePath = (int)pk.UpgradePath;
            s.basicUpgrade = pk.BasicUpgrade;
            var list = new List<int>();
            foreach (var p in pk.All) list.Add((int)p);
            s.perks = list.ToArray();
        }
        return s;
    }

    /// <summary>把狀態套到（新場景的）玩家身上</summary>
    public void Apply(GameObject player)
    {
        if (player == null) return;

        var hp = player.GetComponent<PlayerHealth>();
        if (hp != null && maxHealth > 0f) hp.SetHealth(maxHealth, health > 0f ? health : maxHealth);

        var ctrl = player.GetComponent<ThirdPersonController>();
        if (ctrl != null && walkSpeed > 0f)
        {
            ctrl.walkSpeed = walkSpeed;
            ctrl.sprintSpeed = sprintSpeed;
        }

        var combat = player.GetComponent<PlayerCombat>();
        if (combat != null && damageMultiplier > 0f)
        {
            combat.damageMultiplier = damageMultiplier;
            if (comboDamage != null && comboDamage.Length > 0) combat.comboDamage = (float[])comboDamage.Clone();
        }

        if (resourceNames != null && resourceNames.Length > 0)
        {
            var inv = player.GetComponent<PlayerInventory>();
            if (inv == null) inv = player.AddComponent<PlayerInventory>();
            var all = new Dictionary<string, int>();
            for (int i = 0; i < resourceNames.Length && i < resourceAmounts.Length; i++)
                all[resourceNames[i]] = resourceAmounts[i];
            inv.SetAll(all);
        }

        if (upgradePath != 0 || (perks != null && perks.Length > 0))
        {
            var list = new List<PlayerPerks.Perk>();
            if (perks != null) foreach (int p in perks) list.Add((PlayerPerks.Perk)p);
            PlayerPerks.For(player).SetAll((PlayerPerks.Path)upgradePath, basicUpgrade, list);
        }
    }
}
