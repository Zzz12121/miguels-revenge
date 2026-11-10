using UnityEngine;

/// <summary>
/// 習武場的懸空木條：玩家用拉索（EnergyTetherSkill 的「拉自己過去」）飛到木條旁邊就算碰到，木條下的燈籠會亮起來。
/// 畫面上的「◆」標記只在 markerVisible 打開時顯示（TetherCourse 在玩家進入習武場後才打開）。
/// </summary>
[RequireComponent(typeof(Collider))]
public class TetherTrainingBeam : MonoBehaviour
{
    [Tooltip("被拉到離木條多近才算碰到 (m)")]
    public float touchDistance = 3.2f;
    [Tooltip("過關後才顯示的東西（亮起來的燈籠）")]
    public GameObject litMarker;
    [Tooltip("多遠以內會在畫面上標出這根木條 (m)")]
    public float markerDistance = 60f;

    public bool Touched { get; private set; }
    [System.NonSerialized] public bool markerVisible;

    /// <summary>掉進深溝、從頭來過時把燈籠熄掉</summary>
    public void ResetTouch()
    {
        Touched = false;
        if (litMarker != null) litMarker.SetActive(false);
    }

    Collider beamCollider;
    Transform player;
    EnergyTetherSkill tether;
    GUIStyle markerStyle;

    void Awake()
    {
        beamCollider = GetComponent<Collider>();
        if (litMarker != null) litMarker.SetActive(false);
    }

    void Update()
    {
        if (Touched) return;
        if (tether == null)
        {
            var health = PlayerHealth.Find();
            if (health == null) return;
            player = health.transform;
            tether = health.GetComponent<EnergyTetherSkill>();
            if (tether == null) return;
        }
        if (!tether.IsPullingPlayer) return;
        Vector3 closest = beamCollider.ClosestPoint(player.position);
        if ((closest - player.position).sqrMagnitude > touchDistance * touchDistance) return;

        Touched = true;
        if (litMarker != null) litMarker.SetActive(true);
        GameAudio.Play("SFX/RPG/metalLatch", transform.position, 0.9f, 1.25f, 0f, 1f, 40f);
    }

    void OnGUI()
    {
        if (Touched || !markerVisible || player == null || GameUI.OverlayOpen) return;
        var cam = Camera.main;
        if (cam == null || (player.position - transform.position).sqrMagnitude > markerDistance * markerDistance) return;
        Vector3 p = cam.WorldToScreenPoint(transform.position);
        if (p.z <= 0f) return;
        markerStyle ??= new GUIStyle(GameUI.Center) { fontSize = 22 };
        GameUI.Shadowed(new Rect(p.x - 30f, Screen.height - p.y - 34f, 60f, 28f), "◆", markerStyle, GameUI.Accent);
    }
}
