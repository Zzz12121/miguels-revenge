using System.Collections;
using UnityEngine;

/// <summary>
/// 玩家血量：受傷、無敵時間、掉出場地死亡、自動重生，並用 OnGUI 畫一個簡單血條。
/// </summary>
[RequireComponent(typeof(ThirdPersonController))]
public class PlayerHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    [Tooltip("受傷後的無敵時間 (秒)，避免同一招連續扣血")]
    public float invincibleTime = 0.6f;
    [Tooltip("低於這個高度就判定掉下去死亡")]
    public float killY = -8f;
    public float respawnDelay = 2.5f;
    [Tooltip("重生點，留空就用遊戲開始時的位置（碰到 Checkpoint 會自動改掉）")]
    public Transform respawnPoint;

    public float Health { get; private set; }
    public bool IsDead { get; private set; }
    /// <summary>腳底的世界座標高度（用來判斷有沒有跳過衝擊波 / 光束）</summary>
    public float FeetY => transform.position.y + cc.center.y - cc.height * 0.5f;

    public event System.Action OnDied;
    public event System.Action OnRespawned;

    CharacterController cc;
    ThirdPersonController controller;
    Vector3 spawnPos;
    Quaternion spawnRot;
    float lastHitTime = float.NegativeInfinity;
    float lastFlashTime = float.NegativeInfinity;
    string deathReason = "";

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        controller = GetComponent<ThirdPersonController>();
        Health = maxHealth;
        spawnPos = transform.position;
        spawnRot = transform.rotation;

        // 還沒加攻擊元件的話自動加上（想調整數值請手動 Add Component → PlayerCombat）
        if (GetComponent<PlayerCombat>() == null)
        {
            gameObject.AddComponent<PlayerCombat>();
            Debug.Log("[Player] 沒有 PlayerCombat，已自動加上。想調整攻擊數值請在 Player 上手動 Add Component → PlayerCombat。");
        }
    }

    void Update()
    {
        if (!IsDead && transform.position.y < killY) Die("掉下去了");
    }

    /// <summary>造成傷害；回傳 true 代表這次真的有打到（不在無敵時間內）</summary>
    /// <param name="ignoreInvincibility">true = 持續傷害（例如光束），不受也不觸發受傷後的無敵時間</param>
    public bool TakeDamage(float amount, Vector3 knockback = default, bool ignoreInvincibility = false)
    {
        if (IsDead) return false;
        if (!ignoreInvincibility)
        {
            if (Time.time - lastHitTime < invincibleTime) return false;
            lastHitTime = Time.time;
        }
        lastFlashTime = Time.time;
        Health = Mathf.Max(0f, Health - amount);
        controller.AddImpulse(knockback);
        ThirdPersonCamera.ShakeMain(0.15f, 0.2f);
        GameAudio.Play("SFX/RPG/dropLeather", transform.position, 0.8f, 0.8f, 0.08f, 0.3f);
        if (Health <= 0f) Die("被打倒了");
        return true;
    }

    /// <summary>補滿血（檢查點使用）</summary>
    public void HealFull()
    {
        if (!IsDead) Health = maxHealth;
    }

    /// <summary>回復指定血量（篝火互動使用）</summary>
    public void Heal(float amount)
    {
        if (IsDead) return;
        Health = Mathf.Min(maxHealth, Health + amount);
    }

    void Die(string reason)
    {
        if (IsDead) return;
        IsDead = true;
        Health = 0f;
        deathReason = reason;
        controller.inputEnabled = false;
        OnDied?.Invoke();
        StartCoroutine(RespawnRoutine());
    }

    IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(respawnDelay);
        Vector3 pos = respawnPoint != null ? respawnPoint.position : spawnPos;
        Quaternion rot = respawnPoint != null ? respawnPoint.rotation : spawnRot;

        cc.enabled = false; // CharacterController 要先關掉才能直接傳送
        transform.SetPositionAndRotation(pos, rot);
        cc.enabled = true;

        controller.ResetMotion();
        controller.inputEnabled = true;
        Health = maxHealth;
        IsDead = false;
        lastHitTime = Time.time; // 重生後給一點無敵時間
        OnRespawned?.Invoke();
    }

    // ---------- 簡易 HUD（之後可以換成正式 UI） ----------
    void OnGUI()
    {
        const float w = 260f, h = 18f, x = 20f, y = 20f;
        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), Texture2D.whiteTexture);
        GUI.color = new Color(0.85f, 0.15f, 0.15f);
        GUI.DrawTexture(new Rect(x, y, w * (Health / maxHealth), h), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 6, y, w, h), $"HP {Mathf.CeilToInt(Health)} / {maxHealth}");

        // 受傷時畫面閃紅
        float flash = 1f - Mathf.Clamp01((Time.time - lastFlashTime) / 0.3f);
        if (flash > 0f && !IsDead)
        {
            GUI.color = new Color(1f, 0f, 0f, 0.25f * flash);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        }

        if (IsDead)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(0, 0, Screen.width, Screen.height), $"你死了（{deathReason}）", style);
        }
        GUI.color = Color.white;
    }
}