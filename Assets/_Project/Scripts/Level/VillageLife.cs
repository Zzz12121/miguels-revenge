using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 讓村子活起來：每隔一陣子安排一件事——
///   聊天：兩個在附近走動的村民停下來、面對面聊一會（一個說、一個聽，輪流），再各自回去走原本的路
///   拜訪：一個走動的村民離開路線去找某個人（海邊補網的漁夫、行商的攤子、砍竹人、鐵匠鋪……），站在旁邊聊一會再回去
/// 只走直線過去：中間被房子、圍牆擋住就不安排（用膠囊體掃一遍確認）。
/// </summary>
public class VillageLife : MonoBehaviour
{
    [System.Serializable]
    public class Spot
    {
        public string name;
        [Tooltip("被拜訪的人（或東西）")]
        public Transform host;
        [Tooltip("拜訪的人站在哪裡")]
        public Vector3 standAt;
        [Tooltip("誰會來（走動的村民）")]
        public VillagerWalker[] visitors;
    }

    public VillagerWalker[] walkers;
    public Spot[] spots;
    public Vector2 interval = new Vector2(7f, 14f);
    [Range(0f, 1f)] public float chatChance = 0.65f;
    [Tooltip("兩個人相距多遠以內會約著聊天（各走一半到中間碰面）")]
    public float chatRadius = 24f;
    public float visitRadius = 48f;

    /// <summary>安排過幾次（測試用）</summary>
    public int Chats { get; private set; }
    public int Visits { get; private set; }
    public string LastEvent { get; private set; } = "";

    class Pair
    {
        public VillagerWalker a, b;
        public TalkGestures ga, gb;
        public Transform host;
        public float until, nextSwap;
        public bool aSpeaks;
    }

    readonly List<Pair> active = new List<Pair>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    float next;

    void Start() => next = Time.time + Random.Range(3f, 6f);

    void Update()
    {
        // 正在聊的：輪流說話；結束了就收手
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var p = active[i];
            bool aChat = p.a != null && p.a.Chatting;
            bool bChat = p.b == null || p.b.Chatting;
            if (Time.time >= p.nextSwap) { p.aSpeaks = !p.aSpeaks; p.nextSwap = Time.time + Random.Range(2.2f, 3.6f); }
            if (p.ga != null) { p.ga.active = aChat; p.ga.speaking = p.aSpeaks; }
            if (p.gb != null) { p.gb.active = aChat && bChat; p.gb.speaking = !p.aSpeaks; }
            if (Time.time > p.until && !aChat && (p.b == null || !p.b.Chatting))
            {
                if (p.ga != null) p.ga.active = false;
                if (p.gb != null) { p.gb.active = false; p.gb.faceTarget = null; }
                active.RemoveAt(i);
            }
        }

        if (Time.time < next) return;
        next = Time.time + Random.Range(interval.x, interval.y);
        if (Random.value < chatChance) { if (!TryChat()) TryVisit(); }
        else if (!TryVisit()) TryChat();
    }

    bool Busy(VillagerWalker w)
    {
        foreach (var p in active) if (p.a == w || p.b == w) return true;
        return false;
    }

    bool TryChat()
    {
        var free = new List<VillagerWalker>();
        foreach (var w in walkers) if (w != null && w.isActiveAndEnabled && w.IsFree && !Busy(w)) free.Add(w);
        Shuffle(free);
        for (int i = 0; i < free.Count; i++)
            for (int j = i + 1; j < free.Count; j++)
            {
                var a = free[i];
                var b = free[j];
                Vector3 d = b.transform.position - a.transform.position;
                d.y = 0f;
                float dist = d.magnitude;
                if (dist < 2f || dist > chatRadius) continue;
                Vector3 mid = (a.transform.position + b.transform.position) * 0.5f;
                Vector3 dir = d / dist;
                Vector3 standA = mid - dir * 0.75f * Mathf.Max(1f, a.transform.lossyScale.y);
                Vector3 standB = mid + dir * 0.75f * Mathf.Max(1f, b.transform.lossyScale.y);
                if (!Clear(a.transform, a.transform.position, standA) || !Clear(b.transform, b.transform.position, standB)) continue;
                float stay = Random.Range(7f, 11f);
                a.Meet(standA, standB, stay);
                b.Meet(standB, standA, stay);
                active.Add(new Pair
                {
                    a = a, b = b, ga = a.GetComponent<TalkGestures>(), gb = b.GetComponent<TalkGestures>(),
                    until = Time.time + stay, nextSwap = Time.time + 2f, aSpeaks = Random.value < 0.5f,
                });
                Chats++;
                LastEvent = $"{a.name} 和 {b.name} 聊天";
                return true;
            }
        return false;
    }

    bool TryVisit()
    {
        if (spots == null || spots.Length == 0) return false;
        int start = Random.Range(0, spots.Length);
        for (int k = 0; k < spots.Length; k++)
        {
            var spot = spots[(start + k) % spots.Length];
            if (spot.host == null || spot.visitors == null) continue;
            bool hostBusy = false;
            foreach (var p in active) if (p.host == spot.host) hostBusy = true;
            if (hostBusy) continue;
            var list = new List<VillagerWalker>(spot.visitors);
            Shuffle(list);
            foreach (var v in list)
            {
                if (v == null || !v.isActiveAndEnabled || !v.IsFree || Busy(v)) continue;
                Vector3 d = spot.standAt - v.transform.position;
                d.y = 0f;
                if (d.magnitude > visitRadius || !Clear(v.transform, v.transform.position, spot.standAt)) continue;
                float stay = Random.Range(8f, 12f);
                v.Meet(spot.standAt, spot.host.position, stay);
                var hostGestures = spot.host.GetComponent<TalkGestures>();
                if (hostGestures != null) hostGestures.faceTarget = v.transform;
                active.Add(new Pair
                {
                    a = v, b = null, host = spot.host, ga = v.GetComponent<TalkGestures>(), gb = hostGestures,
                    until = Time.time + stay + d.magnitude / Mathf.Max(0.5f, v.speed), nextSwap = Time.time + 2f, aSpeaks = true,
                });
                Visits++;
                LastEvent = $"{v.name} 去{spot.name}";
                return true;
            }
        }
        return false;
    }

    /// <summary>從 from 直走到 to 會不會撞到東西（地形、自己、玩家、其他村民不算）</summary>
    bool Clear(Transform self, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.05f) return true;
        Vector3 a = from + Vector3.up * 0.6f, b = from + Vector3.up * 1.5f;
        int n = Physics.CapsuleCastNonAlloc(a, b, 0.3f, d / dist, hits, dist, ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c is TerrainCollider || c.transform.IsChildOf(self)) continue;
            if (c.GetComponentInParent<VillagerWalker>() != null || c.GetComponentInParent<MerchantNPC>() != null || c.GetComponentInParent<PlayerHealth>() != null) continue;
            if (hits[i].distance <= 0f) continue; // 一開始就重疊的（站在什麼東西旁邊）不算
            return false;
        }
        return true;
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
