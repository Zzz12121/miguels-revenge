using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 從漁村村長家後院的大門（DojoGate）穿過去到道場時，不用再從入口走一段路：
/// 新場景一載好就把玩家放到道場大門（DojoCourtyardGate）外、面對著門，轉場的古畫播完、遊戲恢復時自動推開大門，
/// 直接接上道場的開場演出（DojoOpeningCutscene：推門特寫 → 老師父喝茶 → ……）。
/// 只有從村子的大門過來才會這樣（DojoGate.Enter 設 Pending）；讀檔、從別的地方進道場照原本的。不改演出本身。
/// </summary>
public class DojoGateArrival : MonoBehaviour
{
    public const string DojoScene = "DojoArena_ArchitecturePreview";
    /// <summary>剛穿過村子的大門、正在換到道場</summary>
    public static bool Pending;
    [Tooltip("玩家站在門外多遠 (m)：要在大門能互動的距離（DojoCourtyardGate.interactDistance）以內")]
    public float standDistance = 2.6f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => Pending = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
    }

    static void OnLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Pending) return;
        if (scene.name != DojoScene) { Pending = false; return; }
        Pending = false;
        new GameObject("DojoGateArrival").AddComponent<DojoGateArrival>();
    }

    IEnumerator Start()
    {
        yield return null; // 等場景裡的東西 Start 完（玩家、大門、演出）
        var gate = FindAnyObjectByType<DojoCourtyardGate>();
        var cutscene = FindAnyObjectByType<DojoOpeningCutscene>();
        var hp = PlayerHealth.Find();
        if (gate == null || cutscene == null || hp == null || gate.IsOpen || cutscene.IsPlaying) { Destroy(gameObject); yield break; }

        // 站到門外（玩家出生的那一側）、面對大門
        Transform g = gate.transform;
        float side = g.InverseTransformPoint(hp.transform.position).z >= 0f ? 1f : -1f;
        Vector3 stand = g.TransformPoint(new Vector3(0f, 0f, side * standDistance));
        if (Physics.Raycast(stand + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) stand.y = hit.point.y + 0.02f;
        Vector3 face = g.position - stand;
        face.y = 0f;
        var cc = hp.GetComponent<CharacterController>();
        bool was = cc != null && cc.enabled;
        if (cc != null) cc.enabled = false;
        hp.transform.SetPositionAndRotation(stand, face.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(face) : hp.transform.rotation);
        Physics.SyncTransforms();
        if (cc != null && was) cc.enabled = true;
        var cam = FindAnyObjectByType<ThirdPersonCamera>();
        if (cam != null) cam.SnapToTarget();

        // 轉場的古畫播完、遊戲恢復了才推門（大門在時間停住時不能互動）
        while (PaintingSlideshow.Active || Time.timeScale <= 0f || (GameManager.Instance != null && GameManager.Instance.IsLoading)) yield return null;
        yield return new WaitForSeconds(0.25f);
        if (!gate.IsOpen && !cutscene.IsPlaying && !gate.TryToggle())
            Debug.LogWarning("[DojoGateArrival] 推不開道場大門（玩家不在門前？），照原本的讓玩家自己走過去");
        Destroy(gameObject);
    }
}
