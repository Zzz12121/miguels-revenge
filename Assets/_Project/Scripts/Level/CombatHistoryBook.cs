using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 暫居地桌上的戰鬥紀錄簿：走近按 E 翻開，列出 CombatLog 記下的每一場戰鬥（最新的在最上面）。
/// 翻開時遊戲暫停、玩家不能動；滾輪 / ↑↓ / 手把十字鍵捲動，E / 空白鍵 / 右鍵 / 手把 B 闔上。
/// </summary>
public class CombatHistoryBook : MonoBehaviour
{
    public float readRadius = 2.2f;
    public string promptText = "翻閱戰鬥紀錄";

    PlayerHealth playerHealth;
    ThirdPersonController controller;
    bool open;
    int openFrame;
    float openTime, prevTimeScale = 1f, scroll;

    public static bool AnyOpen { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => AnyOpen = false;

    void Start()
    {
        playerHealth = PlayerHealth.Find();
        if (playerHealth != null) controller = playerHealth.GetComponent<ThirdPersonController>();
    }

    void Update()
    {
        if (open) { UpdateOpen(); return; }
        if (playerHealth == null || playerHealth.IsDead || StoryDialogue.Active || SteleInscription.AnyOpen || AnyOpen) return;
        Vector3 d = playerHealth.transform.position - transform.position;
        float dist = new Vector2(d.x, d.z).magnitude;
        if (dist > readRadius || Mathf.Abs(d.y) > 2.5f) return;
        InteractPrompt.Offer(this, dist, promptText);
        if (InteractPrompt.Pressed(this)) Open();
    }

    public void Open()
    {
        if (open) return;
        open = AnyOpen = true;
        openFrame = Time.frameCount;
        openTime = Time.unscaledTime;
        scroll = 0f;
        prevTimeScale = CombatHitStop.CaptureTimeScale();
        Time.timeScale = 0f;
        if (controller != null)
        {
            controller.LockActions();
            controller.inputEnabled = false;
        }
        GameAudio.Play("SFX/RPG/bookOpen", Vector3.zero, 0.7f, 0.9f, 0f, 0f);
    }

    void UpdateOpen()
    {
        if (GameUI.OverlayOpen) return;
        Time.timeScale = 0f;
        if (Time.frameCount == openFrame || Time.unscaledTime - openTime < 0.25f) return;

        var kb = Keyboard.current;
        var mouse = Mouse.current;
        float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        if (Mathf.Abs(wheel) > 0.01f) scroll -= Mathf.Sign(wheel) * RowHeight * 1.5f;
        float keys = 0f;
        if (kb != null) keys += (kb.downArrowKey.isPressed || kb.sKey.isPressed ? 1f : 0f) - (kb.upArrowKey.isPressed || kb.wKey.isPressed ? 1f : 0f);
        var pad = Gamepad.current;
        if (pad != null && InputBindings.PadEnabled) keys += -pad.leftStick.ReadValue().y + (pad.dpad.down.isPressed ? 1f : 0f) - (pad.dpad.up.isPressed ? 1f : 0f);
        scroll += Mathf.Clamp(keys, -1f, 1f) * 520f * Time.unscaledDeltaTime;

        // 不用 Esc 闔上：同一幀 GameManager 會看到時間恢復了，接著打開暫停選單
        bool close = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                  || (mouse != null && mouse.rightButton.wasPressedThisFrame)
                  || InputBindings.Pressed(GameAction.Interact) || PadInput.CancelPressed;
        if (close) Close();
    }

    void Close()
    {
        if (!open) return;
        open = AnyOpen = false;
        InputBindings.SuppressActionsThisFrame();
        Time.timeScale = prevTimeScale;
        if (controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            if (playerHealth == null || !playerHealth.IsDead) controller.inputEnabled = true;
        }
        GameAudio.Play("SFX/RPG/bookClose", Vector3.zero, 0.6f, 1f, 0f, 0f);
    }

    void OnDisable() => Close();

    // ---------------- 畫面 ----------------

    const float RowHeight = 58f;
    GUIStyle titleStyle, headStyle, rowStyle, smallStyle, resultStyle;

    void OnGUI()
    {
        if (!open || GameUI.OverlayOpen) return;
        GUI.depth = -1150;
        titleStyle ??= new GUIStyle(GameUI.Title) { fontSize = 30 };
        headStyle ??= new GUIStyle(GameUI.Small) { fontSize = 17 };
        rowStyle ??= new GUIStyle(GameUI.Small) { fontSize = 17, clipping = TextClipping.Clip };
        smallStyle ??= new GUIStyle(GameUI.Small) { fontSize = 14, clipping = TextClipping.Clip };
        resultStyle ??= new GUIStyle(GameUI.Center) { fontSize = 18 };
        foreach (var s in new[] { headStyle, rowStyle, smallStyle, resultStyle }) s.normal.textColor = Color.white;

        float k = Mathf.Clamp01((Time.unscaledTime - openTime) / 0.25f);
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f * k);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, k);

        float w = Mathf.Min(980f, Screen.width - 60f), h = Mathf.Min(680f, Screen.height - 80f);
        var box = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GameUI.Panel(box);
        GUI.color = new Color(GameUI.Accent.r, GameUI.Accent.g, GameUI.Accent.b, k);
        GUI.Label(new Rect(box.x, box.y + 12f, w, 40f), "戰鬥紀錄", titleStyle);

        // 總計
        var recs = CombatLog.Records;
        int wins = 0, losses = 0, kills = 0, fights = 0;
        foreach (var r in recs)
        {
            if (r.training) continue;
            fights++;
            if (r.Won) wins++;
            if (r.Lost) losses++;
            kills += r.kills;
        }
        GUI.color = new Color(GameUI.TextColor.r, GameUI.TextColor.g, GameUI.TextColor.b, k);
        var center = new GUIStyle(headStyle) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(box.x, box.y + 54f, w, 24f),
            $"實戰 {fights} 場　勝 {wins}　敗 {losses}　打倒 {kills} 名敵人　倒下 {CombatLog.Deaths} 次　（練習 {recs.Count - fights} 場）", center);

        var list = new Rect(box.x + 24f, box.y + 92f, w - 48f, h - 140f);
        GUI.color = new Color(1f, 1f, 1f, 0.12f * k);
        GUI.DrawTexture(new Rect(list.x, list.y - 6f, list.width, 1f), Texture2D.whiteTexture);

        if (recs.Count == 0)
        {
            GUI.color = new Color(GameUI.MutedColor.r, GameUI.MutedColor.g, GameUI.MutedColor.b, k);
            GUI.Label(new Rect(list.x, list.y + 40f, list.width, 30f), "還沒有任何戰鬥紀錄。打過一場之後再來翻翻看。", center);
        }
        else
        {
            float content = recs.Count * RowHeight;
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, content - list.height));
            GUI.BeginGroup(list);
            for (int i = 0; i < recs.Count; i++)
            {
                float y = i * RowHeight - scroll;
                if (y < -RowHeight || y > list.height) continue;
                DrawRow(recs[recs.Count - 1 - i], new Rect(0f, y, list.width, RowHeight - 6f), k);
            }
            GUI.EndGroup();
            // 捲軸
            if (content > list.height)
            {
                float barH = list.height * list.height / content;
                float barY = list.y + (list.height - barH) * (scroll / (content - list.height));
                GUI.color = new Color(1f, 1f, 1f, 0.25f * k);
                GUI.DrawTexture(new Rect(list.xMax + 8f, barY, 4f, barH), Texture2D.whiteTexture);
            }
        }

        GUI.color = new Color(GameUI.MutedColor.r, GameUI.MutedColor.g, GameUI.MutedColor.b, k);
        GUI.Label(new Rect(box.x, box.yMax - 38f, w - 24f, 26f), $"滾輪 / ↑↓ 捲動　[{InputBindings.DisplayName(GameAction.Interact)}] / 空白鍵 / 右鍵 闔上",
            new GUIStyle(smallStyle) { alignment = TextAnchor.MiddleRight });
        GUI.color = prev;
    }

    void DrawRow(CombatRecord r, Rect row, float k)
    {
        Color resultColor = r.Won ? GameUI.Good : r.Lost ? GameUI.Bad : r.training ? GameUI.Accent : GameUI.MutedColor;
        GUI.color = new Color(1f, 1f, 1f, 0.05f * k);
        GUI.DrawTexture(row, Texture2D.whiteTexture);
        GUI.color = new Color(resultColor.r, resultColor.g, resultColor.b, 0.9f * k);
        GUI.DrawTexture(new Rect(row.x, row.y, 4f, row.height), Texture2D.whiteTexture);

        // 結果
        GUI.Label(new Rect(row.x + 10f, row.y, 70f, row.height), r.result, resultStyle);
        // 對手、地點
        GUI.color = new Color(GameUI.TextColor.r, GameUI.TextColor.g, GameUI.TextColor.b, k);
        GUI.Label(new Rect(row.x + 92f, row.y + 3f, row.width * 0.5f, 26f), r.opponent, rowStyle);
        GUI.color = new Color(GameUI.MutedColor.r, GameUI.MutedColor.g, GameUI.MutedColor.b, k);
        string where = $"{r.when}　{r.place}" + (r.Lost && !string.IsNullOrEmpty(r.detail) ? $"　（{r.detail}）" : "");
        GUI.Label(new Rect(row.x + 92f, row.y + 27f, row.width * 0.55f, 22f), where, smallStyle);

        // 數字
        int t = Mathf.RoundToInt(r.seconds);
        string stats = $"{t / 60}:{t % 60:00}　命中 {r.hits}　造成 {r.dealt:0}　受到 {r.taken:0}" + (r.kills > 0 ? $"　打倒 {r.kills}" : "");
        GUI.color = new Color(GameUI.TextColor.r, GameUI.TextColor.g, GameUI.TextColor.b, 0.9f * k);
        GUI.Label(new Rect(row.x + row.width * 0.5f, row.y, row.width * 0.5f - 10f, row.height), stats,
            new GUIStyle(rowStyle) { alignment = TextAnchor.MiddleRight, fontSize = 15 });
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.4f, 0.6f);
        Gizmos.DrawWireSphere(transform.position, readRadius);
    }
}
