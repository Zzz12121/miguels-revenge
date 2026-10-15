using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopUIManager : MonoBehaviour
{
    [Header("資源顯示 (左上方)")]
    public TextMeshProUGUI resourceText; // 請將畫面上的 ResourceText 拖進這裡
    public string resourceName = "Herb";

    [Header("兌換代價")]
    public int speedUpgradeCost = 5;
    public int attackUpgradeCost = 5;

    [Header("商人立繪與對話")]
    public Image shopperPortraitImage;
    public TextMeshProUGUI dialogueText;

    private PlayerInventory playerInventory;
    private ThirdPersonController playerController;
    private MeleeCombat playerCombat;

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
        if (playerInventory != null) return; // 已經抓過了就不用重複抓

        var player = GameObject.FindWithTag("Player");
        if (player != null)
        {
            playerInventory = player.GetComponent<PlayerInventory>();
            playerController = player.GetComponent<ThirdPersonController>();
            playerCombat = player.GetComponent<MeleeCombat>();

            if (playerInventory == null)
                Debug.LogError("[ShopUIManager] 找到玩家物件，但玩家身上缺少 PlayerInventory 腳本！");
        }
        else
        {
            Debug.LogError("[ShopUIManager] 找不到 Tag 為 'Player' 的玩家物件！");
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