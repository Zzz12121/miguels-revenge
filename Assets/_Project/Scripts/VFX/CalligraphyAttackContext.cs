using System;
using UnityEngine;

/// <summary>Cast identity, not input state. One dodge roll per cast; individual hits may still damage clones.</summary>
public static class CalligraphyAttackContext
{
    public readonly struct Attack
    {
        public readonly int cast, skill, hit;
        public Attack(int cast, int skill, int hit) { this.cast=cast; this.skill=skill; this.hit=hit; }
    }
    public static Attack Current { get; private set; }
    static int serial;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { serial=0; Current=default; }
    public static int NewCast() => ++serial;
    public static IDisposable Enter(int cast,int skill,int hit) => new Scope(new Attack(cast,skill,hit));
    sealed class Scope : IDisposable
    {
        readonly Attack previous;
        public Scope(Attack next) { previous=Current; Current=next; }
        public void Dispose() => Current=previous;
    }
    public static bool Evades(Transform target,int cast,int skill)
    {
        var boss=target ? target.GetComponentInParent<CalligraphyBossCombat>() : null;
        return boss && boss.TryEvade(cast,skill);
    }
}
