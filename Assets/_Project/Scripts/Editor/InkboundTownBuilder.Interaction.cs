#if UNITY_EDITOR
using UnityEngine;

public static partial class InkboundTownBuilder
{
    static void BindV3PassableEchoes(Transform root)
    {
        foreach(var hanging in root.GetComponentsInChildren<InkboundHangingMotion>(true))
            if(!hanging.GetComponent<InkboundPassableGroup>())hanging.gameObject.AddComponent<InkboundPassableGroup>();
        foreach(var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if(!filter.sharedMesh||!filter.GetComponent<MeshRenderer>()||!filter.GetComponentInParent<InkboundPassableGroup>()||filter.GetComponentInParent<InkboundEcho>())continue;
            var echo=filter.gameObject.AddComponent<InkboundEcho>();echo.plumeMaterial=plume;
        }
    }
    static void InstallV3PassableInteraction()
    {
        town.gameObject.AddComponent<InkboundTownInkSettings>().inkPlaybackSpeed=.5f;
        foreach(var t in town.GetComponentsInChildren<Transform>(true))
        {
            if(t.name=="Planted ceramic threshold"||t.GetComponent<InkboundBoat>())
                if(!t.GetComponent<InkboundPassableGroup>())t.gameObject.AddComponent<InkboundPassableGroup>();
        }
        BindV3PassableEchoes(town);
    }
}
#endif
