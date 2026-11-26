using System.Collections.Generic;
using UnityEngine;

/// <summary>Reuses the short-lived impact systems after their last particle expires.</summary>
public sealed class ImpactParticlePool : MonoBehaviour
{
    const int RetainedPerEffect = 12;
    static readonly Dictionary<string, Stack<ParticleSystem>> available = new Dictionary<string, Stack<ParticleSystem>>();
    ParticleSystem particles;
    string effectName;
    bool returned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() => available.Clear();

    public static ParticleSystem Rent(string name)
    {
        if (!available.TryGetValue(name, out var pool))
            available[name] = pool = new Stack<ParticleSystem>();
        ParticleSystem result = null;
        while (pool.Count > 0 && !result) result = pool.Pop();
        if (!result)
        {
            var root = new GameObject(name);
            result = root.AddComponent<ParticleSystem>();
            var recycle = root.AddComponent<ImpactParticlePool>();
            recycle.particles = result;
            recycle.effectName = name;
        }
        result.GetComponent<ImpactParticlePool>().returned = false;
        result.gameObject.SetActive(true);
        return result;
    }

    void OnParticleSystemStopped()
    {
        if (returned || !particles || particles.IsAlive(true)) return;
        returned = true;
        if (!available.TryGetValue(effectName, out var pool))
            available[effectName] = pool = new Stack<ParticleSystem>();
        if (pool.Count >= RetainedPerEffect) { Destroy(gameObject); return; }
        gameObject.SetActive(false);
        pool.Push(particles);
    }
}
