using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>Opt-in continuation after the existing escape performance. Does not replace phase one.</summary>
public static class InkboundTownJourney
{
    public const string Scene="InkboundTown_Exploration";
    public static bool Pending {get;private set;}
    public static float MaximumHealth {get;private set;}
    public static float RemainingHealth {get;private set;}
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset(){Pending=false;SceneManager.sceneLoaded-=Loaded;SceneManager.sceneLoaded+=Loaded;}
    static void Loaded(Scene scene,LoadSceneMode mode)
    {
        if(scene.name==Scene){Register();return;}
        Pending=false;
        if(!Application.CanStreamedLevelBeLoaded(Scene))return;
        foreach(var escape in Object.FindObjectsByType<DojoElderEscape>(FindObjectsInactive.Exclude))
            if(!escape.GetComponent<InkboundTownEntranceBridge>())escape.gameObject.AddComponent<InkboundTownEntranceBridge>();
    }
    public static void Carry(BossHealth health)
    {Register();Pending=true;MaximumHealth=health.maxHealth;RemainingHealth=Mathf.Max(1,health.Health);}
    public static void Register()
    {
        var manager=GameManager.Instance;if(!manager||manager.FindLevel(Scene)!=null)return;
        manager.storyLevels=(manager.storyLevels??System.Array.Empty<GameManager.Level>()).Concat(new[]{new GameManager.Level{sceneName=Scene,title="河鎮",music=""}}).ToArray();
    }
}

public sealed class InkboundTownEntranceBridge:MonoBehaviour
{
    DojoElderEscape escape;Transform portal;bool loading;float unfurledAt;
    void Start(){escape=GetComponent<DojoElderEscape>();}
    void Update()
    {
        if(!escape||loading)return;
        if(!portal&&escape.Started&&escape.flightPath!=null&&escape.ReachedWaypoint>=escape.flightPath.Length-1)CreatePortal();
        if(portal)portal.localScale=new Vector3(1,Mathf.Lerp(.015f,1,Mathf.SmoothStep(0,1,(Time.time-unfurledAt)/1.4f)),1);
        if(!escape.Completed||!portal)return;
        var player=PlayerHealth.Find();
        if(!player||player.IsDead||Vector3.Distance(player.transform.position,portal.position)>3.4f)return;
        if(Keyboard.current!=null&&Keyboard.current.eKey.wasPressedThisFrame)
        {
            loading=true;InkboundTownJourney.Carry(GetComponent<BossHealth>());
            if(GameManager.Instance)GameManager.Instance.LoadLevel(InkboundTownJourney.Scene);
            else SceneManager.LoadSceneAsync(InkboundTownJourney.Scene);
        }
    }
    void CreatePortal()
    {
        portal=new GameObject("Unrolled painting - voluntary town entry").transform;
        var waypoint=escape.flightPath?.LastOrDefault(t=>t);
        Vector3 p=waypoint?waypoint.position:transform.position+transform.forward*6;
        var ground=Physics.RaycastAll(p+Vector3.up*.15f,Vector3.down,8,~0,QueryTriggerInteraction.Ignore).Where(h=>h.normal.y>.65f&&!h.collider.GetComponentInParent<PlayerHealth>()&&!h.transform.IsChildOf(transform)).OrderBy(h=>h.distance).FirstOrDefault();
        if(ground.collider)p=ground.point;
        Vector3 facing=PlayerHealth.Find()?PlayerHealth.Find().transform.position-p:-Vector3.forward;facing.y=0;if(facing.sqrMagnitude<.01f)facing=Vector3.back;
        portal.position=p;portal.rotation=Quaternion.LookRotation(facing);unfurledAt=Time.time;
        var material=Resources.Load<Material>("InkboundTown/M_IT_EntryScroll");
        for(int side=-1;side<=1;side+=2)
        {
            var pole=GameObject.CreatePrimitive(PrimitiveType.Cylinder);pole.name="Scroll roller";pole.transform.SetParent(portal,false);
            pole.transform.localPosition=new Vector3(0,side<0?.2f:3.8f,0);pole.transform.localRotation=Quaternion.Euler(0,0,90);pole.transform.localScale=new Vector3(.13f,1.55f,.13f);
            Object.Destroy(pole.GetComponent<Collider>());var roller=Resources.Load<Material>("InkboundTown/M_IT_EntryRoller");if(roller)pole.GetComponent<Renderer>().sharedMaterial=roller;
        }
        var sheet=GameObject.CreatePrimitive(PrimitiveType.Quad);sheet.name="Suspended ink painting";sheet.transform.SetParent(portal,false);sheet.transform.localPosition=Vector3.up*2;sheet.transform.localScale=new Vector3(2.8f,3.6f,1);Object.Destroy(sheet.GetComponent<Collider>());
        if(material)sheet.GetComponent<Renderer>().sharedMaterial=material;
    }
    void OnGUI()
    {
        if(!escape||!escape.Completed||!portal||loading||!PlayerHealth.Find()||Vector3.Distance(PlayerHealth.Find().transform.position,portal.position)>3.4f)return;
        GUI.Label(new Rect(Screen.width*.5f-110,Screen.height*.75f,260,32),"E　走入畫中的河鎮");
    }
    void OnDestroy(){if(portal)Object.Destroy(portal.gameObject);}
}
