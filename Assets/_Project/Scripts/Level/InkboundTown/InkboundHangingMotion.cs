using UnityEngine;

/// <summary>Small, slow rigid sway for suspended lanterns, independent of the visitor.</summary>
public sealed class InkboundHangingMotion:MonoBehaviour
{
    public float phase,amplitude=2.4f;
    Quaternion rest;
    void Start(){rest=transform.localRotation;}
    void LateUpdate()
    {
        if(Time.deltaTime<=0)return;
        float t=Time.time*.53f+phase*6.283f;
        transform.localRotation=rest*Quaternion.Euler(Mathf.Sin(t)*amplitude,0,Mathf.Sin(t*.73f+1)*amplitude*.65f);
    }
}
