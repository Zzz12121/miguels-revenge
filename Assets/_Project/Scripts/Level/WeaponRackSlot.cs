using UnityEngine;

/// <summary>
/// 鐵匠鋪門口的一座武器架：走近按互動鍵（E）就直接換成架上這組武器（不用找女鐵匠）；
/// 靠近時架子旁邊浮出一張小卡，寫著這組武器的特性。架上的那組已經帶在身上時，卡片會寫「正帶著」，弓的話可以補箭。
/// </summary>
public class WeaponRackSlot : MonoBehaviour
{
    public WeaponLoadout loadout;
    public string displayName = "大刀";
    [Tooltip("卡片上的說明（一行一項）")]
    public string[] info;
    [Tooltip("多近可以按互動鍵 (m)")]
    public float range = 2.4f;
    [Tooltip("卡片多遠開始出現 (m)")]
    public float cardRange = 5f;
    [Tooltip("卡片浮在架子上方多高 (m)")]
    public float cardHeight = 2.1f;

    Transform player;
    GUIStyle titleStyle, lineStyle, hintStyle;
    Texture2D bowIcon;

    void Start()
    {
        var hp = PlayerHealth.Find();
        if (hp != null) player = hp.transform;
    }

    bool Carrying => PlayerLoadout.Current == loadout;
    bool CanRefill => loadout == WeaponLoadout.BowKnife && Carrying && PlayerLoadout.Arrows < PlayerLoadout.MaxArrows;

    float Distance()
    {
        if (player == null) return float.MaxValue;
        Vector3 d = player.position - transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    void Update()
    {
        if (player == null || StoryDialogue.Active || GameUI.OverlayOpen) return;
        float dist = Distance();
        if (dist > range) return;
        bool usable = !Carrying || CanRefill;
        InteractPrompt.Offer(this, dist, CanRefill ? "補滿箭" : Carrying ? $"正帶著{displayName}" : $"換成{displayName}", usable);
        if (usable && InteractPrompt.Pressed(this))
        {
            PlayerLoadout.Equip(loadout);
            GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 1.15f, 0f, 1f, 30f);
            CombatVFX.Impact(transform.position + Vector3.up * 1.2f, new Color(0.95f, 0.85f, 0.6f));
        }
    }

    void OnGUI()
    {
        if (player == null || GameUI.OverlayOpen || StoryDialogue.Active) return;
        float dist = Distance();
        if (dist > cardRange) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 sp = cam.WorldToScreenPoint(transform.position + Vector3.up * cardHeight);
        if (sp.z <= 0f) return;

        titleStyle ??= new GUIStyle(GameUI.Body) { fontStyle = FontStyle.Bold, fontSize = 16 };
        lineStyle ??= new GUIStyle(GameUI.Small) { fontSize = 13, wordWrap = true };
        hintStyle ??= new GUIStyle(GameUI.Small) { fontSize = 12 };
        if (bowIcon == null && loadout == WeaponLoadout.BowKnife) bowIcon = Resources.Load<Texture2D>("UI/JV_Icon_Bow");

        int n = info != null ? info.Length : 0;
        float w = 210f, h = 50f + n * 19f;
        var box = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);
        float a = Mathf.Clamp01((cardRange - dist) / 1.5f); // 走近時淡入
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, a);
        GameUI.Panel(box);
        float x = box.x + 10f;
        if (bowIcon != null)
        {
            GUI.DrawTexture(new Rect(box.xMax - 38f, box.y + 4f, 30f, 30f), bowIcon, ScaleMode.ScaleToFit, true);
        }
        GUI.color = new Color(GameUI.Accent.r, GameUI.Accent.g, GameUI.Accent.b, a);
        GUI.Label(new Rect(x, box.y + 5f, w - 20f, 22f), displayName, titleStyle);
        GUI.color = new Color(GameUI.TextColor.r, GameUI.TextColor.g, GameUI.TextColor.b, a);
        for (int i = 0; i < n; i++)
            GUI.Label(new Rect(x, box.y + 27f + i * 19f, w - 20f, 19f), info[i], lineStyle);
        string hint = CanRefill ? $"[{InteractPrompt.KeyName}] 補滿箭（剩 {PlayerLoadout.Arrows} 支）"
            : Carrying ? "正帶在身上"
            : $"[{InteractPrompt.KeyName}] 換成這組";
        GUI.color = new Color(GameUI.MutedColor.r, GameUI.MutedColor.g, GameUI.MutedColor.b, a);
        GUI.Label(new Rect(x, box.yMax - 22f, w - 20f, 19f), hint, hintStyle);
        GUI.color = prev;
    }
}
