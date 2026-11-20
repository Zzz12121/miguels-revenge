using System.Collections;
using UnityEngine;

/// <summary>
/// 漁村裡給米格爾暫住的老房子（暫居地）：可以從正門走進去、走出來。
///   - 走進屋裡時畫面上方顯示「暫居地」
///   - 被褥：按 E 休息，體力補滿，之後倒下就在這裡醒來
///   - 矮桌上的戰鬥紀錄簿（CombatHistoryBook）：翻開看打過的每一場仗
///   - 在村子裡倒下、或在道場 / 孤島被打倒（VillageReturn 把玩家抬回村子）都會在 wakePoint 醒來
/// 房子本身由 JapaneseVillageBuilder.BuildResidence 蓋好，這裡只負責互動。
/// </summary>
public class Residence : MonoBehaviour
{
    [Tooltip("醒來時站的位置與面向（被褥旁邊，面向門口）")]
    public Transform wakePoint;
    [Tooltip("被褥（休息的互動點）")]
    public Transform bed;
    public float bedRadius = 1.8f;
    [Tooltip("屋內範圍（本地座標的半寬 x、半深 z；y = 地板離房子原點多高）")]
    public Vector3 interiorHalf = new Vector3(5.6f, 0.81f, 5.6f);

    PlayerHealth player;
    bool inside;
    float fade, fadeTarget;
    bool resting;

    void Start()
    {
        player = PlayerHealth.Find();
        // 在村子裡倒下：在暫居地醒來
        if (player != null && wakePoint != null) player.respawnPoint = wakePoint;
    }

    /// <summary>玩家現在在屋子裡</summary>
    public bool PlayerInside => inside;

    bool Contains(Vector3 world)
    {
        Vector3 p = transform.InverseTransformPoint(world);
        return Mathf.Abs(p.x) < interiorHalf.x && Mathf.Abs(p.z) < interiorHalf.z && p.y > interiorHalf.y - 0.6f && p.y < interiorHalf.y + 3f;
    }

    void Update()
    {
        fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime * 2.2f);
        if (player == null) return;
        bool now = !player.IsDead && Contains(player.transform.position);
        if (now && !inside)
        {
            VictoryBanner.ShowSmall("暫居地", "村長借你住的老房子", new Color(1f, 0.85f, 0.6f));
            GameAudio.Play("SFX/RPG/doorOpen_1", transform.position, 0.35f, 1.2f, 0.05f, 0.5f, 20f);
        }
        inside = now;

        if (bed == null || resting || player.IsDead || StoryDialogue.Active || CombatHistoryBook.AnyOpen) return;
        Vector3 d = player.transform.position - bed.position;
        float dist = new Vector2(d.x, d.z).magnitude;
        if (dist > bedRadius || Mathf.Abs(d.y) > 2f) return;
        InteractPrompt.Offer(this, dist, "在被褥上休息（體力補滿，倒下後在這裡醒來）");
        if (InteractPrompt.Pressed(this)) StartCoroutine(Rest());
    }

    IEnumerator Rest()
    {
        resting = true;
        var controller = player.GetComponent<ThirdPersonController>();
        if (controller != null) { controller.LockActions(); controller.inputEnabled = false; }
        // 先把武器收起來（手往腰間 / 背後一收），收好了才躺下
        PlayerLoadout.Holster(this);
        float h0 = Time.time;
        while (!PlayerLoadout.WeaponsAway && Time.time - h0 < 1.2f) yield return null;
        GameAudio.Play("SFX/RPG/cloth4", bed.position, 0.7f, 0.8f, 0.05f, 0.5f, 20f);
        fadeTarget = 1f;
        while (fade < 0.999f) yield return null;
        player.HealFull();
        var stamina = player.GetComponent<PlayerStamina>();
        if (stamina != null) stamina.Refill();
        if (wakePoint != null)
        {
            player.respawnPoint = wakePoint;
            MovePlayer(player, wakePoint);
        }
        yield return new WaitForSecondsRealtime(0.8f);
        fadeTarget = 0f;
        VictoryBanner.ShowSmall("休息了一會兒", "體力全滿。倒下的話會在這裡醒來", new Color(0.6f, 1f, 0.7f));
        if (controller != null)
        {
            controller.UnlockActions();
            controller.SuppressJump(0.3f);
            controller.inputEnabled = true;
        }
        PlayerLoadout.Unholster(this); // 起來：再把武器拔出來
        while (fade > 0.001f) yield return null;
        resting = false;
    }

    /// <summary>把玩家放到 at（醒來的位置）</summary>
    public static void MovePlayer(PlayerHealth p, Transform at)
    {
        if (p == null || at == null) return;
        var cc = p.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        p.transform.SetPositionAndRotation(at.position, at.rotation);
        if (cc != null) cc.enabled = true;
        p.GetComponent<ThirdPersonController>()?.ResetMotion();
        var cam = Camera.main != null ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
        if (cam != null) cam.SnapToTarget();
    }

    void OnGUI()
    {
        if (fade <= 0.001f) return;
        GUI.depth = -1250;
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, fade);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.8f, 0.4f, 0.5f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(new Vector3(0f, interiorHalf.y + 1.5f, 0f), new Vector3(interiorHalf.x * 2f, 3f, interiorHalf.z * 2f));
        Gizmos.matrix = Matrix4x4.identity;
        if (bed != null) Gizmos.DrawWireSphere(bed.position, bedRadius);
    }
}
