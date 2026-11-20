using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 庭園裡的鳥叫：每隔幾秒，在範圍內隨機一個樹梢高度的位置叫一聲（麻雀、顫音、口哨、樹鶯、遠處的斑鳩），
/// 有時候會有另一隻在旁邊回應。玩家（聽的人）離太遠就不叫。
///
/// 放法：
///   1. 直接掛在物件上，物件位置 = 中心，size = 範圍（村莊由 JapaneseVillageBuilder 放在村長家庭園）
///   2. 不用動場景：場景裡有 GardenNames 裡的物件（道場的 CourtyardAndGarden / LivingGarden），
///      開場時會自動在它的範圍放一個
/// </summary>
public sealed class BirdAmbience : MonoBehaviour
{
    [Tooltip("範圍（x, z 公尺），中心是這個物件的位置")]
    public Vector2 size = new Vector2(30f, 30f);
    [Tooltip("鳥在地面上方幾公尺（樹梢）")]
    public Vector2 height = new Vector2(2.5f, 7f);
    [Tooltip("兩聲之間隔幾秒（隨機）")]
    public Vector2 interval = new Vector2(1.8f, 5.5f);
    [Range(0f, 1f)] public float volume = 0.5f;
    [Tooltip("聽的人離範圍邊緣超過這個距離就不叫")]
    public float hearRange = 30f;
    [Tooltip("要不要有日本樹鶯（「ホー、ホケキョ」）")]
    public bool uguisu = true;

    static readonly string[] Common =
    {
        "SFX/Gen/bird_sparrow_1", "SFX/Gen/bird_sparrow_2", "SFX/Gen/bird_sparrow_3",
        "SFX/Gen/bird_trill_1", "SFX/Gen/bird_trill_2", "SFX/Gen/bird_whistle_1", "SFX/Gen/bird_whistle_2",
    };

    /// <summary>沒有手動放鳥叫的場景，有這些名字的物件就自動在它的範圍放一個</summary>
    public static readonly string[] GardenNames = { "CourtyardAndGarden", "LivingGarden" };

    float next;
    Transform listener;

    void OnEnable() => next = Time.time + Random.Range(0.5f, interval.y);

    void Update()
    {
        if (Time.time < next) return;
        next = Time.time + Random.Range(interval.x, interval.y);
        if (listener == null)
        {
            var l = FindAnyObjectByType<AudioListener>();
            if (l == null) return;
            listener = l.transform;
        }
        Vector3 c = transform.position;
        Vector3 d = listener.position - c;
        float edge = Mathf.Max(Mathf.Abs(d.x) - size.x * 0.5f, Mathf.Abs(d.z) - size.y * 0.5f, 0f);
        if (edge > hearRange) return;

        Vector3 at = Spot();
        float roll = Random.value;
        string clip;
        float v = volume, pitch = Random.Range(0.93f, 1.08f);
        if (uguisu && roll < 0.14f) { clip = "SFX/Gen/bird_uguisu_1"; pitch = Random.Range(0.97f, 1.03f); }
        else if (roll < 0.24f) { clip = "SFX/Gen/bird_dove_1"; v *= 0.6f; at = c + new Vector3(Random.Range(-1f, 1f) * size.x, height.y, Random.Range(-1f, 1f) * size.y); }
        else clip = Common[Random.Range(0, Common.Length)];
        GameAudio.Play(clip, at, v, pitch, 0f, 1f, 45f);

        // 有時候另一隻在附近回一聲
        if (Random.value < 0.3f) StartCoroutine(Answer(clip, at));
    }

    System.Collections.IEnumerator Answer(string clip, Vector3 near)
    {
        yield return new WaitForSeconds(Random.Range(0.5f, 1.4f));
        Vector3 at = near + new Vector3(Random.Range(-6f, 6f), Random.Range(-1f, 1.5f), Random.Range(-6f, 6f));
        GameAudio.Play(clip, at, volume * 0.7f, Random.Range(0.95f, 1.12f), 0f, 1f, 45f);
    }

    Vector3 Spot()
    {
        Vector3 p = transform.position + new Vector3(Random.Range(-0.5f, 0.5f) * size.x, 0f, Random.Range(-0.5f, 0.5f) * size.y);
        float ground = transform.position.y;
        if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore)) ground = hit.point.y;
        p.y = ground + Random.Range(height.x, height.y);
        return p;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.5f, 0.5f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * (height.x + height.y) * 0.5f, new Vector3(size.x, height.y - height.x, size.y));
    }

    // ---------------- 沒放的場景：找庭園自動放 ----------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (var root in scene.GetRootGameObjects())
            if (root.GetComponentInChildren<BirdAmbience>(true) != null) return; // 已經放了就不重複
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (System.Array.IndexOf(GardenNames, t.name) < 0) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                var go = new GameObject("BirdAmbience (" + t.name + ")");
                SceneManager.MoveGameObjectToScene(go, scene);
                go.transform.position = new Vector3(b.center.x, b.min.y, b.center.z);
                var birds = go.AddComponent<BirdAmbience>();
                birds.size = new Vector2(Mathf.Clamp(b.size.x, 10f, 80f), Mathf.Clamp(b.size.z, 10f, 80f));
                return; // 一個場景放一個就夠了
            }
    }
}
