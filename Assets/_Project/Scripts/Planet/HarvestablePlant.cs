using UnityEngine;
using UnityEngine.InputSystem;

public class HarvestablePlant : MonoBehaviour
{
    [Header("採集設定")]
    public string resourceName = "Herb"; // 資源名稱
    public int resourceAmount = 1;     // 獲得數量
    public float interactRange = 2.0f; // 互動距離

    [Header("外觀（給沒有模型的空物件用）")]
    [Tooltip("這個物件本身沒有模型時，複製這個物件的外觀（例如場景裡另一株草藥）")]
    public GameObject copyVisualFrom;
    [Tooltip("開始時把高度貼齊地形（Terrain）")]
    public bool snapToTerrain = false;

    Transform player;

    void Start()
    {
        var hp = PlayerHealth.Find(); // 真正會移動、身上有 PlayerInventory 的玩家物件
        if (hp != null) player = hp.transform;

        if (snapToTerrain) SnapToTerrain(transform);
        if (copyVisualFrom != null && GetComponentInChildren<Renderer>() == null) CopyVisual();
    }

    /// <summary>把 t 的高度貼齊地形表面（用 Terrain 的高度，不會被洞穴頂或岩石擋到）</summary>
    public static void SnapToTerrain(Transform t)
    {
        var terrain = Terrain.activeTerrain;
        if (terrain == null) return;
        Vector3 p = t.position;
        p.y = terrain.SampleHeight(p) + terrain.transform.position.y;
        t.position = p;
    }

    /// <summary>複製另一株草藥的模型當外觀（只留模型，不帶腳本和碰撞）</summary>
    void CopyVisual()
    {
        var src = copyVisualFrom.transform;
        var copy = Instantiate(copyVisualFrom, transform.position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * src.rotation, transform);
        foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
        foreach (var col in copy.GetComponentsInChildren<Collider>(true)) Destroy(col);
        Vector3 ls = transform.lossyScale, want = src.lossyScale;
        copy.transform.localScale = new Vector3(want.x / ls.x, want.y / ls.y, want.z / ls.z);
        copy.SetActive(true);
        copy.name = "Visual";
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > interactRange) return;

        // 統一的互動提示：[F] 採集草藥（多個東西重疊時只有最近的會反應）
        InteractPrompt.Offer(this, dist, $"採集{DisplayName}");
        if (InteractPrompt.Pressed(this)) Harvest();
    }

    string DisplayName => resourceName == "Herb" ? "草藥" : resourceName;

    void Harvest()
    {
        var inventory = player.GetComponent<PlayerInventory>();
        if (inventory == null) inventory = player.gameObject.AddComponent<PlayerInventory>();
        inventory.AddResource(resourceName, resourceAmount);
        GameAudio.Play("SFX/RPG/cloth1", transform.position, 0.7f, 1.2f, 0.08f, 0.3f);
        VictoryBanner.ShowSmall($"{DisplayName} +{resourceAmount}", $"持有 {inventory.GetResource(resourceName)}", GameUI.Good);
        Destroy(gameObject);
    }
}