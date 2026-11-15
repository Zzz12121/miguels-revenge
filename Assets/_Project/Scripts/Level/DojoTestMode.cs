using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Local dojo testing toggle. Reuses the existing source-owned health floor.</summary>
[DisallowMultipleComponent]
public sealed class DojoTestMode : MonoBehaviour
{
    public float doublePressWindow=.4f,healAfterSeconds=5f;
    public bool Active {get;private set;}
    public int HealCount {get;private set;}
    float lastPress=float.NegativeInfinity,lastDamage;
    PlayerHealth player;
    void Awake(){player=GetComponent<PlayerHealth>();}
    void OnEnable(){if(!player)player=GetComponent<PlayerHealth>();if(player)player.OnDamaged+=OnDamaged;}
    void OnDisable(){if(player){player.OnDamaged-=OnDamaged;player.SetPracticeProtection(this,false);}Active=false;}
    void OnDamaged(float amount){lastDamage=Time.time;}
    public void SetActive(bool active)
    {
        if(!player||player.IsDead)return;Active=active;player.SetPracticeProtection(this,active);lastDamage=Time.time;
    }
    void Update()
    {
        if(!player)return;
        if(!GameUI.OverlayOpen&&Keyboard.current!=null&&Keyboard.current.pKey.wasPressedThisFrame)
        {
            float now=Time.unscaledTime;
            if(now-lastPress<=doublePressWindow){SetActive(!Active);lastPress=float.NegativeInfinity;}
            else lastPress=now;
        }
        if(Active&&!player.IsDead&&player.Health<player.maxHealth&&Time.time-lastDamage>=healAfterSeconds)
        {player.HealFull();lastDamage=Time.time;HealCount++;}
    }
    void OnGUI(){if(Active)GUI.Label(new Rect(16,92,420,26),"測試模式：生命至少 1，受擊後 5 秒回滿；雙擊 P 關閉");}
}
