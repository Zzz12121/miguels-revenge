using UnityEngine;
using UnityEngine.InputSystem;

public class MerchantNPC : MonoBehaviour
{
    [Header("互動設定")]
    [Tooltip("玩家離商人多近才可以按 F 開啟商店 (公尺)")]
    public float interactRange = 3.0f;
    [Tooltip("拖入你的商店 UI Panel (例如 ShopPanel)")]
    public GameObject shopCanvasUI;

    Transform player;
    bool isOpen = false;

    void Start()
    {
        // 尋找標籤為 Player 的物件
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            Debug.Log("[MerchantNPC] 成功找到玩家物件 (Tag: Player)");
        }
        else
        {
            Debug.LogError("[MerchantNPC] 找不到 Tag 為 'Player' 的物件！請檢查場景中的玩家物件 Tag 設定。");
        }

        // 檢查 UI 欄位設定
        if (shopCanvasUI != null)
        {
            shopCanvasUI.SetActive(false);
        }
        else
        {
            Debug.LogWarning("[MerchantNPC] Shop Canvas UI 欄位是空的 (None)！請在 Inspector 欄位中把 ShopPanel 拖進去。");
        }
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);

        // 使用新版 Input System 檢查 F 鍵是否按下
        bool fPressed = false;
        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame)
        {
            fPressed = true;
        }

        // 當玩家在距離範圍內且按下 F 鍵
        if (dist <= interactRange && fPressed)
        {
            Debug.Log($"[MerchantNPC] 玩家在範圍內 (距離: {dist:F1}m) 按下了 F 鍵，切換商店視窗！");
            ToggleShop();
        }

        // 玩家離太遠時自動關閉商店
        if (isOpen && dist > interactRange + 1.0f)
        {
            Debug.Log("[MerchantNPC] 玩家離商人太遠，自動關閉商店。");
            CloseShop();
        }
    }

    void ToggleShop()
    {
        isOpen = !isOpen;
        if (shopCanvasUI != null)
        {
            shopCanvasUI.SetActive(isOpen);
            Debug.Log($"[MerchantNPC] 商店視窗顯示狀態改為: {isOpen}");
        }
        else
        {
            Debug.LogError("[MerchantNPC] 無法打開視窗：Shop Canvas UI 尚未指派！");
        }

        // 解鎖 / 鎖定滑鼠游標以方便點擊 UI
        Cursor.visible = isOpen;
        Cursor.lockState = isOpen ? CursorLockMode.None : CursorLockMode.Locked;
    }

    void CloseShop()
    {
        isOpen = false;
        if (shopCanvasUI != null)
        {
            shopCanvasUI.SetActive(false);
        }
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }
}