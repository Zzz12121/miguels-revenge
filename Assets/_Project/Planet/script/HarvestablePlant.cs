using UnityEngine;
using UnityEngine.InputSystem;

public class HarvestablePlant : MonoBehaviour
{
    [Header("採集設定")]
    public string resourceName = "Herb"; // 資源名稱
    public int resourceAmount = 1;     // 獲得數量
    public float interactRange = 2.0f; // 互動距離

    Transform player;

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null) player = p.transform;
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);

        // 使用新版 Input System 檢查 E 鍵是否按下
        bool ePressed = false;
        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            ePressed = true;
        }

        if (dist <= interactRange && ePressed)
        {
            Harvest();
        }
    }

    void Harvest()
    {
        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.AddResource(resourceName, resourceAmount);
            Debug.Log($"成功採集 {resourceName} x {resourceAmount}");
        }

        Destroy(gameObject);
    }
}