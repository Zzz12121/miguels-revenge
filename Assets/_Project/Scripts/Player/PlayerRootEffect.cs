using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家被「定身」（時鐘陷阱）：一段時間內不能移動、跳躍、翻滾、攻擊，身體稍微變灰，時間到恢復原狀。
/// 計時由玩家自己負責，陷阱被刪掉也會正常解除；重複被定身會延長到比較晚的那個時間。
/// 用 PlayerRootEffect.Apply(玩家, 秒數) 觸發，元件會自動加到玩家身上。
/// </summary>
public class PlayerRootEffect : MonoBehaviour
{
    [Tooltip("變灰的程度（0 = 不變，1 = 全灰）")]
    [Range(0f, 1f)] public float grayAmount = 0.45f;
    public Color grayColor = new Color(0.55f, 0.56f, 0.6f);

    public bool IsRooted { get; private set; }
    float endTime;

    ThirdPersonController controller;
    PlayerHealth health;
    Renderer[] renderers;
    // 每個材質原本的顏色（變灰前記下來，結束時還原）
    readonly List<(Material mat, int prop, Color color)> saved = new List<(Material, int, Color)>();
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    public static void Apply(GameObject player, float duration)
    {
        if (player == null || duration <= 0f) return;
        var hp = player.GetComponentInParent<PlayerHealth>();
        GameObject host = hp != null ? hp.gameObject : player;
        var fx = host.GetComponent<PlayerRootEffect>();
        if (fx == null) fx = host.AddComponent<PlayerRootEffect>();
        fx.Begin(duration);
    }

    void Awake()
    {
        controller = GetComponent<ThirdPersonController>();
        health = GetComponent<PlayerHealth>();
    }

    void OnEnable()
    {
        if (health != null) { health.OnDied += End; health.OnRespawned += End; }
    }

    void OnDisable()
    {
        if (health != null) { health.OnDied -= End; health.OnRespawned -= End; }
        End();
    }

    void Begin(float duration)
    {
        if (health != null && health.IsDead) return;
        float newEnd = Mathf.Max(endTime, Time.time + duration);
        // 不能移動：沒在定身中、或被延長時才套用（速度效果由控制器自己計時）
        if (controller != null && newEnd > endTime) controller.ApplyTemporarySpeedMultiplier(0f, newEnd - Time.time);
        endTime = newEnd;
        if (IsRooted) return;
        IsRooted = true;
        if (controller != null) controller.LockActions();
        SetGray(true);
        CombatVFX.Impact(transform.position + Vector3.up, new Color(1f, 0.85f, 0.3f));
    }

    void Update()
    {
        if (IsRooted && Time.time >= endTime) End();
    }

    void End()
    {
        if (!IsRooted) return;
        IsRooted = false;
        endTime = 0f;
        if (controller != null) controller.UnlockActions();
        SetGray(false);
    }

    /// <summary>
    /// 把玩家所有材質的顏色往灰色拉（直接改材質實例，結束時還原原本的顏色）。
    /// 用材質實例而不是 MaterialPropertyBlock，才不會蓋掉其他腳本設定的 PropertyBlock。
    /// </summary>
    void SetGray(bool on)
    {
        if (on)
        {
            saved.Clear();
            renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r == null || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                foreach (var m in r.materials)
                {
                    if (m == null) continue;
                    int prop = m.HasProperty(BaseColorId) ? BaseColorId : m.HasProperty(ColorId) ? ColorId : -1;
                    if (prop < 0) continue;
                    Color c = m.GetColor(prop);
                    saved.Add((m, prop, c));
                    Color g = Color.Lerp(c, new Color(grayColor.r, grayColor.g, grayColor.b, c.a), grayAmount);
                    m.SetColor(prop, g);
                }
            }
        }
        else
        {
            foreach (var (m, prop, c) in saved) if (m != null) m.SetColor(prop, c);
            saved.Clear();
        }
    }

    void OnGUI()
    {
        if (!IsRooted || GameUI.OverlayOpen) return;
        float left = Mathf.Max(0f, endTime - Time.time);
        var style = new GUIStyle(GameUI.Center) { fontSize = 22 };
        GameUI.Shadowed(new Rect(0f, Screen.height * 0.58f, Screen.width, 32f), $"被時間釘住了！ {left:0.0}s", style, new Color(1f, 0.85f, 0.3f));
    }
}
