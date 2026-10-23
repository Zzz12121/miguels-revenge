using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reuses the original elder's pigment shader; fixed pool, no per-frame mesh baking.</summary>
public sealed class CalligraphyInkMist : MonoBehaviour
{
    sealed class Puff { public Transform t; public Renderer r; public Vector3 p,v; public float born,life,size,seed; public bool alive; }
    Puff[] pool;
    Mesh quad;
    MaterialPropertyBlock block;
    int cursor;
    public void Initialize(Material material,int capacity=96)
    {
        if(pool!=null || !material)return;
        block=new MaterialPropertyBlock();quad=CalligraphyGeometry.Quad();
        quad.colors=new[]{Color.white,Color.white,Color.white,Color.white};
        pool=new Puff[Mathf.Clamp(capacity,16,160)];
        for(int i=0;i<pool.Length;i++)
        {
            var go=new GameObject("Pooled diffusing pigment");go.transform.SetParent(transform,false);
            go.AddComponent<MeshFilter>().sharedMesh=quad;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.enabled=false;
            pool[i]=new Puff{t=go.transform,r=r};
        }
    }
    public void Emit(Vector3 position,Vector3 velocity,float size,float life=3)
    {
        if(pool==null)return;
        var p=pool[cursor++%pool.Length];p.p=position;p.v=velocity;p.size=size;p.life=life;p.born=Time.time;p.seed=Random.value*100;p.alive=true;p.r.enabled=true;
        p.t.position=position;p.t.localScale=Vector3.zero;
    }
    public void Clear(){if(pool!=null)foreach(var p in pool){p.alive=false;p.r.enabled=false;}}
    void LateUpdate()
    {
        if(pool==null)return;var camera=Camera.main;
        foreach(var p in pool)
        {
            if(!p.alive)continue;float age=Time.time-p.born,u=age/p.life;
            if(u>=1){p.alive=false;p.r.enabled=false;continue;}
            p.p+=(p.v*Mathf.Exp(-age*.9f)+new Vector3(Mathf.Sin(p.seed+age),.16f,Mathf.Cos(p.seed+age*.7f))*.1f)*Time.deltaTime;
            p.t.position=p.p;if(camera)p.t.rotation=camera.transform.rotation*Quaternion.Euler(0,0,p.seed+age*6);
            p.t.localScale=new Vector3(1,1.3f,1)*p.size*Mathf.Lerp(.25f,2.4f,Mathf.Sqrt(u));
            float alpha=Mathf.SmoothStep(0,1,u/.09f)*Mathf.Pow(1-u,1.2f);
            block.Clear();block.SetColor("_BaseColor",new Color(.002f,.003f,.005f,alpha*.92f));p.r.SetPropertyBlock(block);
        }
    }
    void OnDestroy(){if(quad)Destroy(quad);}
}
