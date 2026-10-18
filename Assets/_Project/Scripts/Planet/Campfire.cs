using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem; // 引入新版 Input System 命名空間

/// <summary>
/// 篝火互動：玩家靠近互動範圍內時顯示提示，按下 E 鍵後回復 20% 血量，回復完畢後粒子特效消失。
/// 當玩家重生時會自動重置，恢復成燃燒狀態。
/// </summary>
public class Campfire : MonoBehaviour
{
    [Header("設定")]
    [Tooltip("玩家靠近互動的範圍 (m)")]
    public float interactRange = 2.5f;
    [Tooltip("火堆的粒子特效物件 (Particle System)")]
    public GameObject fireParticleEffect;

    Transform player;
    PlayerHealth playerHealth;
    bool isUsed = false;          // 紀錄是否已被使用
    bool isPlayerInRange = false; // 紀錄玩家是否在互動範圍內

    void Start()
    {
        var p = GameObject.FindWithTag("Player");
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponentInChildren<PlayerHealth>();
            
            if (playerHealth != null)
            {
                playerHealth.OnRespawned += ResetCampfire;
            }
            else
            {
                Debug.LogWarning("[Campfire] 找到玩家物件，但在其自身或子物件中找不到 PlayerHealth 腳本！");
            }
        }
        else
        {
            Debug.LogWarning("[Campfire] 找不到 Tag 為 'Player' 的玩家物件！");
        }

        ResetCampfire();
    }

    void OnDestroy()
    {
        if (playerHealth != null)
        {
            playerHealth.OnRespawned -= ResetCampfire;
        }
    }

    void Update()
    {
        if (player == null || playerHealth == null) return;

        // 計算玩家與火堆的距離
        float dist = Vector3.Distance(transform.position, player.position);
        isPlayerInRange = (dist <= interactRange);

        // 使用新版 Input System 偵測 E 鍵是否被按下
        bool ePressed = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

        // 如果火堆尚未被使用、玩家在範圍內、且按下 E 鍵，則觸發回血
        if (!isUsed && isPlayerInRange && ePressed)
        {
            UseCampfire();
        }
    }

    void UseCampfire()
    {
        isUsed = true;
        isPlayerInRange = false; // 使用後立刻關閉提示

        float healAmount = playerHealth.maxHealth * 0.2f;
        StartCoroutine(HealAndExtinguish(healAmount));
    }

    IEnumerator HealAndExtinguish(float healAmount)
    {
        yield return new WaitForSeconds(0.2f);

        if (playerHealth != null)
        {
            playerHealth.Heal(healAmount);
            Debug.Log($"篝火生效：為玩家回復了 {healAmount} 點血量！目前的血量為：{playerHealth.Health}");
        }

        if (fireParticleEffect != null)
        {
            ParticleSystem ps = fireParticleEffect.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = false;
            }

            yield return new WaitForSeconds(1.5f);
            fireParticleEffect.SetActive(false);
        }
    }

    public void ResetCampfire()
    {
        StopAllCoroutines();
        isUsed = false;

        if (fireParticleEffect != null)
        {
            fireParticleEffect.SetActive(true);

            ParticleSystem ps = fireParticleEffect.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var emission = ps.emission;
                emission.enabled = true;
                ps.Clear();
                ps.Play();
            }
        }
    }

    // 繪製畫面提示 UI 與編輯器範圍黃圈
    void OnGUI()
    {
        if (!isUsed && isPlayerInRange)
        {
            float w = 300f, h = 40f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - 150f;

            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            GUI.color = Color.yellow;
            GUI.Label(new Rect(x, y, w, h), "按下 [ E ] 使用營火 (回復 20% 血量)", style);
            GUI.color = Color.white;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.orange;
        Gizmos.DrawWireSphere(transform.position, interactRange);
    }
}