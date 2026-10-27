using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Shared player skill boundary and feedback; movement/ordinary attacks are unaffected.</summary>
[DefaultExecutionOrder(-11000)]
public sealed class PlayerSkillAvailability : MonoBehaviour
{
    public const string PaintingMessage="畫中世界無法使用技能";
    static PlayerSkillAvailability instance;
    float noticeUntil;
    public bool NoticeVisible=>CalligraphyPaintingWorld.BlocksTether && Time.unscaledTime<noticeUntil;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void ResetStatics()=>instance=null;
    void Awake(){instance=this;}
    void Start(){if(RangeInputSettings.IsRange(this) && !GetComponent<RangeInputSettings>())gameObject.AddComponent<RangeInputSettings>();}
    public static bool TryUse()
    {
        if(RangeInputSettings.InputBlocked)return false;
        if(!CalligraphyPaintingWorld.BlocksTether)return true;
        if(instance)instance.noticeUntil=Time.unscaledTime+2;
        return false;
    }
    public static void CancelActive(PlayerHealth player)
    {
        SkillAimCancellation.Cancel();
        foreach(var skill in FindObjectsByType<VoidRendSkillDemo>())
            if(skill.caster==player.transform && skill.IsPlaying)skill.CancelSkill();
        player.GetComponent<EnergyTetherSkill>()?.CancelForExternalTransition();
    }
    void Update()
    {
        if(!CalligraphyPaintingWorld.BlocksTether || RangeInputSettings.InputBlocked || GameUI.OverlayOpen || Time.timeScale<=0)return;
        var keyboard=Keyboard.current;var mouse=Mouse.current;
        if((keyboard!=null && (keyboard.digit1Key.wasPressedThisFrame || keyboard.digit2Key.wasPressedThisFrame ||
            keyboard.digit3Key.wasPressedThisFrame || keyboard.digit4Key.wasPressedThisFrame)) || (mouse!=null && mouse.middleButton.wasPressedThisFrame))TryUse();
    }
    void OnGUI()
    {
        if(!NoticeVisible)return;
        int depth=GUI.depth;GUI.depth=-1400;
        float width=Mathf.Min(560,Screen.width-24);var rect=new Rect((Screen.width-width)*.5f,Screen.height*.23f,width,56);
        GameUI.Panel(rect,GameUI.Bad);GUI.Label(rect,PaintingMessage,GameUI.Center);GUI.depth=depth;
    }
    void OnDestroy(){if(instance==this)instance=null;}
}
