using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Range-only movement bindings and a paused, pointer-owned rebinding overlay.</summary>
[DefaultExecutionOrder(-12000)]
public sealed class RangeInputSettings : MonoBehaviour
{
    public const string SceneName="VFX_VoidRend_Bocchi";
    public enum Action { Sprint,Jump,Roll }
    static RangeInputSettings instance;
    readonly Key[] keys={Key.LeftShift,Key.Space,Key.C};
    static readonly string[] labels={"衝刺","跳躍","翻滾"};
    const string PrefsPrefix="RangeControls.v1.";
    public bool IsOpen {get;private set;}
    public int WaitingFor {get;private set;}=-1;
    public string Feedback {get;private set;}="";
    public static bool InputBlocked=>instance && instance.IsOpen;
    ThirdPersonController controller;
    ThirdPersonCamera orbit;
    bool locked;
    float savedTimeScale;
    CursorLockMode savedCursor;
    bool savedCursorVisible;
    int openedFrame;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()=>instance=null;
    public static bool IsRange(Component owner)=>owner && owner.gameObject.scene.name==SceneName;
    public static Key Binding(Component owner,Action action)
    {
        if(IsRange(owner))return instance?instance.keys[(int)action]:action==Action.Sprint?Key.LeftShift:action==Action.Jump?Key.Space:Key.C;
        return action==Action.Sprint?Key.LeftShift:action==Action.Jump?Key.Space:Key.V;
    }
    public static string KeyName(Key key)=>key switch
    {
        Key.LeftShift=>"Left Shift",Key.RightShift=>"Right Shift",Key.LeftCtrl=>"Left Ctrl",Key.RightCtrl=>"Right Ctrl",
        Key.LeftAlt=>"Left Alt",Key.RightAlt=>"Right Alt",_=>key.ToString()
    };
    void Awake()
    {
        if(!IsRange(this)){enabled=false;return;}
        instance=this;controller=GetComponent<ThirdPersonController>();
        // Load through the same conflict rules; invalid old preferences never remove a control.
        for(int i=0;i<keys.Length;i++)
        {
            Key saved=(Key)PlayerPrefs.GetInt(PrefsPrefix+(Action)i,(int)keys[i]);
            if(!Reserved(saved))keys[i]=saved;
        }
        if(keys[0]==keys[1] || keys[0]==keys[2] || keys[1]==keys[2])
        {keys[0]=Key.LeftShift;keys[1]=Key.Space;keys[2]=Key.C;}
    }
    bool Reserved(Key key)
    {
        if(!Enum.IsDefined(typeof(Key),key) || key==Key.None || key==Key.IMESelected)return true;
        switch(key)
        {
            case Key.W:case Key.A:case Key.S:case Key.D:
            case Key.UpArrow:case Key.DownArrow:case Key.LeftArrow:case Key.RightArrow:
            case Key.K:case Key.Escape:case Key.F8:case Key.Enter:case Key.NumpadEnter:
            case Key.E:case Key.Q:case Key.G:case Key.M:
            case Key.Digit1:case Key.Digit2:case Key.Digit3:case Key.Digit4:
            case Key.Numpad1:case Key.Numpad2:case Key.Numpad3:case Key.Numpad4:return true;
        }
        var items=GetComponent<PlayerItems>();
        if(items && (key==items.potionKey || key==items.staminaPotionKey))return true;
        foreach(var map in FindObjectsByType<Minimap>())if(key==map.toggleKey)return true;
        return false;
    }
    public bool TryRebind(Action action,Key key,bool save=true)
    {
        int index=(int)action;
        if(index<0 || index>=keys.Length)return false;
        bool conflict=Reserved(key);
        for(int i=0;i<keys.Length;i++)if(i!=index && keys[i]==key)conflict=true;
        if(conflict){Feedback="按鍵衝突，無法改變。請選擇其他按鍵。";return false;}
        keys[index]=key;WaitingFor=-1;Feedback=labels[index]+"已改為 "+KeyName(key);
        if(save){PlayerPrefs.SetInt(PrefsPrefix+action,(int)key);PlayerPrefs.Save();}
        return true;
    }
    public void BeginRebind(Action action)
    {if(!IsOpen)return;WaitingFor=(int)action;Feedback="請按一下要使用的鍵盤按鍵（Esc 取消）";}
    public void Open()
    {
        if(IsOpen || !isActiveAndEnabled || GameUI.OverlayOpen || Time.timeScale<=0)return;
        SkillAimCancellation.Cancel();
        // Do not pause half-way through a cinematic which owns its own time/cursor state.
        foreach(var skill in FindObjectsByType<VoidRendSkillDemo>())if(skill.IsPlaying)return;
        var tether=GetComponent<EnergyTetherSkill>();if(tether && tether.IsExecuting)return;
        if(!SkillAimCancellation.Register(Close))return;
        savedTimeScale=Time.timeScale;savedCursor=Cursor.lockState;savedCursorVisible=Cursor.visible;
        IsOpen=true;WaitingFor=-1;Feedback="點選格子，再按一下想用的按鍵";openedFrame=Time.frameCount;
        if(controller){controller.LockActions();locked=true;}
        orbit=Camera.main?Camera.main.GetComponent<ThirdPersonCamera>():null;
        if(orbit)orbit.SetPointerUiOwner(this,true);
        Time.timeScale=0;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    public void Close()
    {
        if(!IsOpen)return;
        IsOpen=false;WaitingFor=-1;SkillAimCancellation.Release(Close);
        if(locked && controller)controller.UnlockActions();locked=false;
        if(orbit)orbit.SetPointerUiOwner(this,false);orbit=null;
        Time.timeScale=savedTimeScale;Cursor.lockState=savedCursor;Cursor.visible=savedCursorVisible;
    }
    void Update()
    {
        var keyboard=Keyboard.current;if(keyboard==null)return;
        if(!IsOpen){if(keyboard.kKey.wasPressedThisFrame)Open();return;}
        if(Time.frameCount==openedFrame)return;
        if(keyboard.escapeKey.wasPressedThisFrame)
        {
            if(WaitingFor>=0){WaitingFor=-1;Feedback="已取消改鍵";SkillAimCancellation.ConsumeEscape();}
            else SkillAimCancellation.CancelForEscape();
            return;
        }
        if(WaitingFor>=0)
        {
            foreach(var key in keyboard.allKeys)
                if(key.wasPressedThisFrame){TryRebind((Action)WaitingFor,key.keyCode);break;}
        }
        else if(keyboard.kKey.wasPressedThisFrame)Close();
    }
    void OnGUI()
    {
        if(!IsOpen)return;
        int oldDepth=GUI.depth;GUI.depth=-1500;
        Color old=GUI.color;GUI.color=new Color(0,0,0,.7f);GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),Texture2D.whiteTexture);GUI.color=old;
        // Scale one compact layout down for smaller Game Views.
        Matrix4x4 matrix=GUI.matrix;float scale=Mathf.Min(1,Screen.width/720f,Screen.height/420f);
        GUI.matrix=Matrix4x4.TRS(new Vector3(Screen.width*.5f,Screen.height*.5f,0),Quaternion.identity,Vector3.one*scale);
        GameUI.Panel(new Rect(-330,-165,660,330));
        GUI.Label(new Rect(-300,-145,600,42),"靶場按鍵設定",GameUI.Title);
        var center=new GUIStyle(GameUI.Center){fontSize=20};
        for(int i=0;i<3;i++)
        {
            Rect card=new Rect(-300+i*205,-80,190,112);
            if(GameUI.Option(card))BeginRebind((Action)i);
            GUI.Label(new Rect(card.x,card.y+10,card.width,30),labels[i],center);
            GUI.Label(new Rect(card.x,card.y+53,card.width,40),WaitingFor==i?"等待按鍵…":KeyName(keys[i]),center);
        }
        var note=new GUIStyle(GameUI.Small){alignment=TextAnchor.MiddleCenter,wordWrap=true};
        GUI.Label(new Rect(-300,45,600,45),Feedback,note);
        GUI.Label(new Rect(-300,95,600,25),"K／Esc 關閉　・　設定會自動保留　・　既有操作鍵不可重複使用",note);
        if(GUI.Button(new Rect(-55,126,110,27),"關閉"))Close();
        GUI.matrix=matrix;GUI.depth=oldDepth;
    }
    void OnDisable()=>Close();
    void OnDestroy(){Close();if(instance==this)instance=null;}
}
