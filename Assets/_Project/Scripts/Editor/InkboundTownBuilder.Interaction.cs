#if UNITY_EDITOR
using UnityEngine;
using System.Linq;

public static partial class InkboundTownBuilder
{
    static bool refreshingPassableFurnishings;
    // Keep complete objects together, while preventing a touched cup from hiding a whole shop.
    static bool BeginPassableUnit(ref Transform parent,string name)
    {
        var group=parent.GetComponentInParent<InkboundPassableGroup>();
        if(!group||!group.containerOnly)return false;
        parent=Root(name,parent);parent.gameObject.AddComponent<InkboundPassableGroup>();return true;
    }
    static void EndPassableUnit(Transform parent,bool created)
    {if(created)Combine(parent,"Passable_"+parent.name.Replace(' ','_'));}

    [UnityEditor.MenuItem("GeminiGrowUp/Inkbound Town V3/Update individual passable furnishings")]
    public static void UpdatePassableFurnishings()
    {
        if(Application.isPlaying)throw new System.InvalidOperationException("Exit Play mode first.");
        // Load the existing painted materials; this update preserves the user's material tuning.
        paper=Existing("Paper");plaster=Existing("Limewash");timber=Existing("BrushTimber");cloth=Existing("Linen");
        darkCloth=Existing("V3_FoldedPatternedLinen");bankStone=Existing("QuayLayeredStone");ceramic=Existing("Ceramic");
        ink=Existing("ContourInk");stone=Existing("RiverStone");leafWash=Existing("WillowPaleWash");bark=Existing("CurvedBark");
        plume=Existing("ReturningPigment");rng=new System.Random(3317);meshNumber=20000;
        refreshingPassableFurnishings=true;
        try
        {
        for(int type=0;type<8;type++)for(int variant=0;variant<2;variant++)
        {
            string path=Prefabs+"/PF_IT_V3_Shop"+type+"_"+variant+".prefab";
            var root=UnityEditor.PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old=root.transform.Find("Passable daily life furnishings");
                if(old)Object.DestroyImmediate(old.gameObject);
                // A prior partial update could have left unbaked duplicate closed facade boards.
                if(type==7)foreach(var filter in root.GetComponentsInChildren<MeshFilter>().Where(f=>f.transform.parent==root.transform&&f.name=="Bevelled joinery board").ToArray())
                    Object.DestroyImmediate(filter.gameObject);
                float w,d;
                // Derive dimensions from the saved solid floor, rather than moving the building.
                var floor=root.GetComponentsInChildren<BoxCollider>().First(c=>c.name=="Solid stone foundation");
                w=floor.transform.localScale.x;d=floor.transform.localScale.z;
                V3ShopGoods(root.transform,type,w,d);BindV3PassableEchoes(root.transform);
                var lod=root.GetComponent<LODGroup>();var levels=lod.GetLODs();
                levels[0].renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>!r.transform.IsChildOf(root.transform.Find("Distant simplified architecture"))).ToArray();
                lod.SetLODs(levels);lod.RecalculateBounds();UnityEditor.PrefabUtility.SaveAsPrefabAsset(root,path);
            }
            finally{UnityEditor.PrefabUtility.UnloadPrefabContents(root);}
        }
        }
        finally{refreshingPassableFurnishings=false;}
    }
    static Material Existing(string name)
    {
        var material=UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(Materials+"/M_IT_"+name+".mat");
        if(!material)throw new System.InvalidOperationException("Missing painted material "+name);return material;
    }
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
        var settings=town.gameObject.AddComponent<InkboundTownInkSettings>();settings.inkPlaybackSpeed=.5f;
        settings.passThroughInkMaterial=UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/VFX/Calligraphy/Materials/M_InkFluidRibbon.mat");
        foreach(var t in town.GetComponentsInChildren<Transform>(true))
        {
            if(t.name=="Planted ceramic threshold"||t.GetComponent<InkboundBoat>())
                if(!t.GetComponent<InkboundPassableGroup>())t.gameObject.AddComponent<InkboundPassableGroup>();
        }
        BindV3PassableEchoes(town);
    }
}
#endif
