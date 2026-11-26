using System.IO;
using System.Linq;
using UnityEditor;
public static class InkboundTownV3Dependencies
{
    public static object Export()
    {
        var roots=new[]{
            InkboundTownBuilder.ScenePath,"Assets/_Project/Prefabs/Boss/PF_InkboundTown_Elder.prefab",
            "Assets/Resources/InkboundTown/M_IT_EntryScroll.mat","Assets/Resources/InkboundTown/M_IT_EntryRoller.mat",
            "Assets/Soswaewon/Meshes/Buildings/SM_JWD_Roof01a.fbx","Assets/Soswaewon/Meshes/Buildings/SM_JWD_Door01a.fbx",
            "Assets/Soswaewon/Meshes/Buildings/SM_JWD_Door01b.fbx","Assets/Soswaewon/Meshes/Buildings/SM_JWD_Door01c.fbx",
            "Assets/Soswaewon/Meshes/Environments/SM_Willow.fbx","Assets/Soswaewon/Meshes/Environments/SM_Maple.fbx",
            "Assets/Soswaewon/Meshes/Props/SM_Rock01a.fbx"};
        var paths=AssetDatabase.GetDependencies(roots,true).Distinct().OrderBy(p=>p).ToArray();
        File.WriteAllText("output/inkbound-town/v3/dependencies.json",Newtonsoft.Json.JsonConvert.SerializeObject(paths,Newtonsoft.Json.Formatting.Indented));return paths.Length;
    }
}
