using UnityEngine;

/// <summary>A bounded pool fed by real visible skill sweeps, not the player's damage triggers.</summary>
[DefaultExecutionOrder(10900)]
public sealed class InkboundSkillDisturbance:MonoBehaviour
{
    public int capacity=32;
    [Range(1,12)] public int highQualityEchoes=5;
    [Range(16,64)] public int lightweightSamples=32;
    InkboundDisturbanceSource[] pool;
    float[] expires;
    int next;
    void Awake()
    {
        InkboundEcho.HighQualityBudget=highQualityEchoes;InkboundEcho.LightweightSamples=lightweightSamples;
        pool=new InkboundDisturbanceSource[capacity];expires=new float[capacity];
        for(int j=0;j<capacity;j++)
        {var g=new GameObject("Pooled visible ink sweep "+j);g.transform.SetParent(transform,false);pool[j]=g.AddComponent<InkboundDisturbanceSource>();pool[j].enabled=false;}
    }
    void OnEnable()=>CalligraphyVisualContact.Swept+=Sweep;
    void OnDisable(){CalligraphyVisualContact.Swept-=Sweep;if(pool!=null)foreach(var s in pool)if(s)s.enabled=false;}
    void Sweep(Transform owner,Vector3 from,Vector3 to,float radius,Vector3 velocity)
    {
        if(!owner||owner.gameObject.scene!=gameObject.scene||pool==null)return;
        var s=pool[next];s.enabled=true;s.SetVisualSweep(from,to,radius,velocity);expires[next]=Time.time+.10f;next=(next+1)%pool.Length;
    }
    void LateUpdate()
    {if(pool!=null)for(int j=0;j<pool.Length;j++)if(pool[j].enabled&&Time.time>expires[j])pool[j].enabled=false;}
}
