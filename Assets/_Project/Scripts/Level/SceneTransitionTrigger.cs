using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 玩家走進這個觸發區就切換到另一個場景（例如道場後山的吊橋 → ForestIsland）。
/// 有 GameManager 時用它的淡出淡入並帶著玩家狀態；沒有的話直接載入。
/// 目標場景必須在 Build Settings 裡。
/// </summary>
[RequireComponent(typeof(Collider))]
public class SceneTransitionTrigger : MonoBehaviour
{
    public string targetScene = "ForestIsland";
    [Tooltip("進入後延遲幾秒才切換（給玩家多走幾步、讓霧蓋上來）")]
    public float delay = 0f;

    bool fired;

    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (fired || string.IsNullOrEmpty(targetScene)) return;
        if (other.GetComponentInParent<PlayerHealth>() == null) return;
        fired = true;
        if (delay > 0f) Invoke(nameof(Go), delay); else Go();
    }

    void Go()
    {
        if (GameManager.Instance != null) GameManager.Instance.LoadLevel(targetScene);
        else if (Application.CanStreamedLevelBeLoaded(targetScene)) SceneManager.LoadScene(targetScene);
        else Debug.LogError($"[SceneTransitionTrigger] 場景「{targetScene}」不在 Build Settings 裡");
    }
}
