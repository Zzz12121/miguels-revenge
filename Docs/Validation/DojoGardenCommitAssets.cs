using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
public static class DojoGardenCommitAssets
{
    public static object Run()
    {
        string fjg="Assets/Waldemarst/FreeJapaneseGarden/Prefabs/";
        var roots=new List<string>{"Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity",
            "Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoLivingGarden.prefab",
            "Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoInteractiveEntrance.prefab",
            "Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_FinalDojoArena_Living.prefab",
            "Assets/Waldemarst/JapaneseGardenPackage/Prefabs/SakuraTree_A.prefab",
            "Assets/Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_A.prefab",
            "Assets/Waldemarst/JapaneseGardenPackage/Prefabs/JapaneseMapleTree_C.prefab",
            "Assets/JTS_StoneLanterns/Prefabs/Lantern_Stone_A1.prefab",
            "Assets/JTS_StoneLanterns/Prefabs/Lantern_Stone_C2.prefab",
            "Assets/JTS_Shrine_Torii_Gates/Prefabs/Torii_Inari_Large1_LOD.prefab"};
        roots.AddRange(new[]{"Big","Mid"}.Select(s=>fjg+"Trees/Sakura/Tree_Sakura_"+s+"_Spring_01.prefab"));
        roots.AddRange(new[]{"Big","Mid"}.Select(s=>fjg+"Trees/BlackPine/Tree_BlackPine_"+s+"_Green_01.prefab"));
        roots.AddRange(new[]{"Big","Mid"}.Select(s=>fjg+"Trees/Bamboo/Tree_Bamboo_"+s+"_Green_01.prefab"));
        roots.Add(fjg+"Plants/Boxwood/Plant_Boxwood_Spring_01.prefab");roots.Add(fjg+"Plants/PaintedFern/Plant_PaintedFern_Spring_01.prefab");
        string rearPrefab="Assets/_Project/Prefabs/Environment/FinalDojoArena/PF_DojoRearLandscape.prefab";
        if(File.Exists(rearPrefab))roots.Add(rearPrefab);
        for(int i=1;i<=3;i++)roots.Add(fjg+"Misc/Rocks/JG_Rock_A_0"+i+".prefab");
        for(int i=1;i<=2;i++)roots.Add(fjg+"Misc/Rocks/JG_TobiIshi_A_0"+i+".prefab");
        foreach(string p in roots)if(!File.Exists(p))throw new Exception("Missing source "+p);
        var paths=new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(),true).Where(p=>p.StartsWith("Assets/")&&File.Exists(p)));
        foreach(string p in Directory.GetFiles("Assets/Waldemarst","*.cs",SearchOption.AllDirectories))paths.Add(p.Replace('\\','/'));
        foreach(string p in paths.ToArray())
        {
            if(File.Exists(p+".meta"))paths.Add(p+".meta");
            string d=Path.GetDirectoryName(p)?.Replace('\\','/');
            while(!string.IsNullOrEmpty(d)&&d!="Assets")
            {if(File.Exists(d+".meta"))paths.Add(d+".meta");d=Path.GetDirectoryName(d)?.Replace('\\','/');}
        }
        File.WriteAllLines("Temp/dojo-garden-dependencies.txt",paths.OrderBy(p=>p));
        return new{files=paths.Count,mb=Math.Round(paths.Sum(p=>new FileInfo(p).Length)/1048576.0,1),largest=paths.Select(p=>new FileInfo(p)).OrderByDescending(f=>f.Length).Take(10).Select(f=>new{f.FullName,mb=Math.Round(f.Length/1048576.0,1)}).ToArray()};
    }
}
