using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopUIManager : MonoBehaviour
{
    [Header("玩家參考 (可直接從 Inspector 拖入)")]
    public PlayerInventory playerInventory;
    public ThirdPersonController playerController;
    public MeleeCombat playerCombat;

    [Header("資源顯示 (左上方)")]
    public TextMeshProUGUI resourceText; // 請將畫面上的 ResourceText 拖進這裡
    public string resourceName = "Herb";

    [Header("兌換代價")]
    public int speedUpgradeCost = 5;
    public int attackUpgradeCost = 5;

    [Header("商人立繪與對話")]
    public Image shopperPortraitImage;
    public TextMeshProUGUI dialogueText;

    void OnEnable()
    {
        InitPlayerReferences();
        UpdateResourceUI();
        SetDialogue("Welcome to the shop! Choose your upgrade.");
    }

    void Update()
    {
        // 確保商店開啟時，數量若有變動（例如採集更多草藥）能即時更新顯示
        UpdateResourceUI();
    }

    void InitPlayerReferences()
    {
        // 如果已經在 Inspector 裡面拉好了，就直接跳過
        if (playerInventory != null && playerController != null && playerCombat != null) return;

        // 備用防呆：如果忘記在 Inspector 拖曳，改用精準的 FindFirstObjectByType 搜尋
        var inventoryComp = FindAnyObjectByType<PlayerInventory>();
        if (inventoryComp != null)
        {
            playerInventory = inventoryComp;
            var rootObj = inventoryComp.gameObject;
            if (playerController == null) playerController = rootObj.GetComponentInChildren<ThirdPersonController>();
            if (playerCombat == null) playerCombat = rootObj.GetComponentInChildren<MeleeCombat>();
        }
        else
        {
            Debug.LogError("[ShopUIManager] 場景中找不到任何掛載 PlayerInventory 腳本的物件！");
        }
    }

    void UpdateResourceUI()
    {
        // 如果還沒抓到背包，試著再抓一次
        if (playerInventory == null)
        {
            InitPlayerReferences();
        }

        if (playerInventory != null && resourceText != null)
        {
            int currentAmount = playerInventory.GetResource(resourceName);
            resourceText.text = $"{resourceName} : {currentAmount}";
        }
        else if (resourceText == null)
        {
            Debug.LogWarning("[ShopUIManager] Resource Text 欄位尚未指定！請將畫面上的文字物件拖入 Inspector 欄位中。");
        }
    }

    /// <summary>
    /// 綁定到【跑速 +10%】按鈕
    /// </summary>
    public void OnBuySpeedUpgrade()
    {
        InitPlayerReferences();
        if (playerInventory == null || playerController == null) return;

        if (playerInventory.ConsumeResource(resourceName, speedUpgradeCost))
        {
            playerController.walkSpeed *= 1.1f;
            playerController.sprintSpeed *= 1.1f;
            UpdateResourceUI();
            SetDialogue("Transaction successful! Your movement speed has been increased by 10%.");
        }
        else
        {
            SetDialogue($"Insufficient resources! Upgrading speed requires {resourceName} x {speedUpgradeCost}");
        }
    }

    /// <summary>
    /// 綁定到【攻擊力 +5】按鈕
    /// </summary>
    public void OnBuyAttackUpgrade()
    {
        InitPlayerReferences();
        if (playerInventory == null || playerCombat == null) return;

        if (playerInventory.ConsumeResource(resourceName, attackUpgradeCost))
        {
            if (playerCombat.comboDamage != null)
            {
                for (int i = 0; i < playerCombat.comboDamage.Length; i++)
                {
                    playerCombat.comboDamage[i] += 5f;
                }
            }
            UpdateResourceUI();
            SetDialogue("Transaction successful! Your attack power has been increased by 5 points.");
        }
        else
        {
            SetDialogue($"Insufficient resources! Upgrading attack requires {resourceName} x {attackUpgradeCost}");
        }
    }

    void SetDialogue(string message)
    {
        if (dialogueText != null)
        {
            dialogueText.text = message;
        }
    }
}