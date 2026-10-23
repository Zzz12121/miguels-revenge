using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Range-only opt-in harness. No change to dock NPC or production health behaviour.</summary>
public sealed class CalligraphyBossRangeTest : MonoBehaviour
{
    public GameObject bossPrefab;
    public GameObject existingDisplay;
    public Vector3 spawnPosition=new Vector3(-7,0,2.5f);
    public float healAfterSeconds=5;
    public CalligraphyBossCombat Boss {get;private set;}
    public bool Running {get;private set;}
    PlayerHealth player;
    float lastDamage;
    bool menuOpen,displayWasActive;
    bool pendingNext;
    CursorLockMode previousCursor;
    bool previousVisible;
    ThirdPersonCamera pointerCamera;
    void Awake()
    {
        if(gameObject.scene.name!="VFX_VoidRend_Bocchi"){enabled=false;return;}
        player=PlayerHealth.Find();
    }
    public void StartTest()
    {
        if(!player)player=PlayerHealth.Find();
        if(!player || !bossPrefab)return;
        CancelPlayerSkills();
        if(!Running)
        {
            player.OnDamaged+=OnDamaged;player.SetPracticeProtection(this,true);
            if(existingDisplay){displayWasActive=existingDisplay.activeSelf;existingDisplay.SetActive(false);}
        }
        Running=true;lastDamage=Time.time;player.HealFull();
        if(!player.GetComponent<CalligraphyPlayerBladeTrail>())player.gameObject.AddComponent<CalligraphyPlayerBladeTrail>();
        if(Boss && Boss.IsDead){Destroy(Boss.gameObject);Boss=null;}
        if(!Boss)
        {
            var go=Instantiate(bossPrefab,spawnPosition,Quaternion.identity);Boss=go.GetComponent<CalligraphyBossCombat>();
        }
        Boss.GetComponent<TetherThrownMotion>()?.Interrupt();Boss.transform.position=spawnPosition;
        Boss.StartBattle(player,true);CloseMenu();
    }
    public void StopTest()
    {
        if(Running)CancelPlayerSkills();
        if(player){player.OnDamaged-=OnDamaged;player.SetPracticeProtection(this,false);}
        if(Boss){Boss.StopBattle();Destroy(Boss.gameObject);Boss=null;}
        if(Running && existingDisplay)existingDisplay.SetActive(displayWasActive);
        Running=false;pendingNext=false;CloseMenu();
    }
    void OnDamaged(float amount){if(amount>0)lastDamage=Time.time;}
    void CancelPlayerSkills()
    {
        SkillAimCancellation.Cancel();
        if(player)player.GetComponent<EnergyTetherSkill>()?.CancelForExternalTransition();
        foreach(var skill in FindObjectsByType<VoidRendSkillDemo>())
            if(skill.isActiveAndEnabled && skill.IsPlaying)skill.CancelSkill();
    }
    void Update()
    {
        if(Running && Boss && Boss.Vanished && Time.time-Boss.VanishedAt>=5)
        {
            Destroy(Boss.gameObject);Boss=null;StartTest();
        }
        if(Keyboard.current==null)return;
        if(Keyboard.current.f8Key.wasPressedThisFrame)
        {
            if(menuOpen)CloseMenu();
            else if(!GameUI.OverlayOpen && !SkillAimCancellation.IsAiming)
            {
                previousCursor=Cursor.lockState;previousVisible=Cursor.visible;menuOpen=true;
                pointerCamera=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;
                if(pointerCamera)pointerCamera.SetPointerUiOwner(this,true);
                Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            }
        }
        if(menuOpen && GameUI.OverlayOpen)CloseMenu();
        if(!Running)return;
        if(player && Time.time-lastDamage>=healAfterSeconds)player.HealFull();
        if(Keyboard.current.downArrowKey.wasPressedThisFrame && !GameUI.OverlayOpen)pendingNext=true;
        if(pendingNext)
        {
            bool busy=false;
            foreach(var skill in FindObjectsByType<VoidRendSkillDemo>())busy|=skill.IsPlaying;
            var tether=player?player.GetComponent<EnergyTetherSkill>():null;
            busy|=tether && tether.IsExecuting;
            if(!busy){pendingNext=false;Boss?.NextSkill();}
        }
    }
    void CloseMenu()
    {
        if(!menuOpen)return;menuOpen=false;
        if(pointerCamera)pointerCamera.SetPointerUiOwner(this,false);pointerCamera=null;
        Cursor.lockState=GameUI.OverlayOpen?CursorLockMode.None:previousCursor;
        Cursor.visible=GameUI.OverlayOpen || previousVisible;
    }
    void OnDisable()=>StopTest();
    void OnGUI()
    {
        float x=Screen.width-280;
        GUI.Box(new Rect(x,78,265,Running?116:82),"");
        GUI.Label(new Rect(x+10,84,250,25),"F8：顯示／收起測試游標");
        if(GUI.Button(new Rect(x+10,110,245,32),"遊墨行者 Boss 測試"))StartTest();
        if(Running)
        {
            GUI.Label(new Rect(x+10,145,245,23),"↓ 下一招 · 五秒無傷回滿 · HP ≥ 1");
            if(GUI.Button(new Rect(x+10,170,245,22),"停止測試"))StopTest();
        }
    }
}
