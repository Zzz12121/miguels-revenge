using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 玩家（貓戰士）的頭髮、裙子彈簧骨。
/// 選單 GeminiGrowUp > 玩家 > 加上頭髮與裙子的彈簧骨：
///   1. 讀 PlayerCatWarrior_SpringBones.json（Art/PlayerCatWarrior/player_springbones.py 從網格算出來的：要加的骨頭 + 每個頂點的新權重）
///   2. 在 PF_PlayerCatWarrior_Visual 的骨架上加 SB_ 骨頭（裙子繞腰 8 串、頭髮後面 + 左右 3 串）
///   3. 複製一份網格（PlayerCatWarrior_SpringRigged.asset）換上新權重，換到玩家身上；原本的網格不動
///   4. 掛上 SpringBoneSystem（遊戲中讓它們晃動、不穿進腿）
/// 選單 GeminiGrowUp > 玩家 > 移除頭髮與裙子的彈簧骨：換回原本的網格、刪掉加的骨頭（完全復原）。
/// </summary>
public static class PlayerSpringBoneInstaller
{
    const string PlayerPrefab = "Assets/_Project/Prefabs/Player/PF_PlayerCatWarrior_Visual.prefab";
    const string Dir = "Assets/_Project/Models/PlayerCharacter/PlayerCatWarrior_Rigged/";
    const string SourceMesh = Dir + "PlayerCatWarrior_TailRigged_GarmentFixed.asset";
    const string RiggedMesh = Dir + "PlayerCatWarrior_SpringRigged.asset";
    const string DataPath = Dir + "PlayerCatWarrior_SpringBones.json";

    [System.Serializable] class V3 { public float x, y, z; public Vector3 V => new Vector3(x, y, z); }
    [System.Serializable] class BoneDef { public string name, parent; public V3 pos; }
    [System.Serializable] class WeightDef { public int v; public string[] b; public float[] w; }
    [System.Serializable] class Data { public string source; public int vertexCount; public float hipsToHead; public BoneDef[] bones; public WeightDef[] weights; }

    [MenuItem("GeminiGrowUp/玩家/加上頭髮與裙子的彈簧骨")]
    static void Install()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("彈簧骨", "請先停止 Play。", "OK"); return; }
        var text = AssetDatabase.LoadAssetAtPath<TextAsset>(DataPath);
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMesh);
        if (text == null || source == null) { EditorUtility.DisplayDialog("彈簧骨", "找不到資料或原本的網格：\n" + DataPath + "\n" + SourceMesh, "OK"); return; }
        var data = JsonUtility.FromJson<Data>(text.text);
        if (data == null || data.bones == null || data.vertexCount != source.vertexCount)
        {
            EditorUtility.DisplayDialog("彈簧骨", $"資料跟網格對不上（頂點數 {data?.vertexCount} vs {source.vertexCount}），可能網格換過了，要重新產生 JSON。", "OK");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var smr = FindBody(root);
            if (smr == null) { EditorUtility.DisplayDialog("彈簧骨", "玩家 Prefab 裡找不到身體的 SkinnedMeshRenderer。", "OK"); return; }
            RemoveSpringBones(root, smr, source);   // 重做：先清乾淨

            // ---- 骨頭 ----
            var bones = smr.bones.ToList();
            var bind = source.bindposes.ToList();
            var byName = new Dictionary<string, int>();
            for (int i = 0; i < bones.Count; i++) if (bones[i] != null && !byName.ContainsKey(bones[i].name)) byName[bones[i].name] = i;
            foreach (var def in data.bones)
            {
                if (!byName.TryGetValue(def.parent, out int pi)) { Debug.LogWarning("[彈簧骨] 找不到父骨頭 " + def.parent); continue; }
                Matrix4x4 parentBind = bind[pi];
                // bindpose 把網格座標換到骨頭自己的座標：新骨頭在父骨頭底下的位置、以及新骨頭自己的 bindpose
                Vector3 local = parentBind.MultiplyPoint3x4(def.pos.V);
                var go = new GameObject(def.name);
                go.transform.SetParent(bones[pi], false);
                go.transform.localPosition = local;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                bones.Add(go.transform);
                bind.Add(Matrix4x4.TRS(local, Quaternion.identity, Vector3.one).inverse * parentBind);
                byName[def.name] = bones.Count - 1;
            }

            // ---- 網格：複製一份換上新權重 ----
            var mesh = Object.Instantiate(source);
            mesh.name = Path.GetFileNameWithoutExtension(RiggedMesh);
            var weights = mesh.boneWeights;
            int changed = 0;
            foreach (var wd in data.weights)
            {
                var pairs = new List<KeyValuePair<int, float>>();
                for (int k = 0; k < wd.b.Length; k++)
                    if (byName.TryGetValue(wd.b[k], out int bi)) pairs.Add(new KeyValuePair<int, float>(bi, wd.w[k]));
                if (pairs.Count == 0 || wd.v < 0 || wd.v >= weights.Length) continue;
                pairs = pairs.OrderByDescending(p => p.Value).Take(4).ToList();
                float sum = pairs.Sum(p => p.Value);
                var bw = new BoneWeight();
                for (int k = 0; k < pairs.Count; k++)
                {
                    float w = pairs[k].Value / sum;
                    switch (k)
                    {
                        case 0: bw.boneIndex0 = pairs[k].Key; bw.weight0 = w; break;
                        case 1: bw.boneIndex1 = pairs[k].Key; bw.weight1 = w; break;
                        case 2: bw.boneIndex2 = pairs[k].Key; bw.weight2 = w; break;
                        default: bw.boneIndex3 = pairs[k].Key; bw.weight3 = w; break;
                    }
                }
                weights[wd.v] = bw;
                changed++;
            }
            mesh.bindposes = bind.ToArray();
            mesh.boneWeights = weights;
            if (AssetDatabase.LoadAssetAtPath<Mesh>(RiggedMesh) != null) AssetDatabase.DeleteAsset(RiggedMesh);
            AssetDatabase.CreateAsset(mesh, RiggedMesh);

            smr.bones = bones.ToArray();
            smr.sharedMesh = mesh;
            smr.updateWhenOffscreen = false;

            // ---- 讓它們動起來 ----
            var spring = root.GetComponent<SpringBoneSystem>();
            if (spring == null) spring = root.AddComponent<SpringBoneSystem>();
            spring.hipsToHead = data.hipsToHead;
            spring.settings = new[]
            {
                new SpringBoneSystem.ChainSettings("SB_Hair", 1.6f, 0.45f, 0.2f, 0.012f),
                new SpringBoneSystem.ChainSettings("SB_Skirt", 1.2f, 0.4f, 0.4f, 0.02f),
            };

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("彈簧骨",
                $"玩家加好了：\n骨頭 {data.bones.Length} 根（裙子 8 串、頭髮 3 串），改了 {changed} 個頂點的權重。\n" +
                "網格：" + RiggedMesh + "（原本的網格沒動）\n\n手感在 PF_PlayerCatWarrior_Visual 的 SpringBoneSystem 調。\n不想要了：GeminiGrowUp > 玩家 > 移除頭髮與裙子的彈簧骨。", "OK");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    [MenuItem("GeminiGrowUp/玩家/移除頭髮與裙子的彈簧骨")]
    static void Uninstall()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("彈簧骨", "請先停止 Play。", "OK"); return; }
        var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMesh);
        var root = PrefabUtility.LoadPrefabContents(PlayerPrefab);
        try
        {
            var smr = FindBody(root);
            if (smr == null || source == null) return;
            RemoveSpringBones(root, smr, source);
            var spring = root.GetComponent<SpringBoneSystem>();
            if (spring != null) Object.DestroyImmediate(spring);
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefab);
            EditorUtility.DisplayDialog("彈簧骨", "已換回原本的網格、刪掉加的骨頭。", "OK");
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    static SkinnedMeshRenderer FindBody(GameObject root) =>
        root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.sharedMesh != null)
            .OrderByDescending(r => r.sharedMesh.vertexCount)
            .FirstOrDefault();

    /// <summary>換回原本的網格、骨頭清單拿掉 SB_、刪掉 SB_ 物件</summary>
    static void RemoveSpringBones(GameObject root, SkinnedMeshRenderer smr, Mesh source)
    {
        var bones = smr.bones.Where(b => b == null || !b.name.StartsWith("SB_")).ToArray();
        if (bones.Length > source.bindposes.Length) bones = bones.Take(source.bindposes.Length).ToArray();
        smr.bones = bones;
        smr.sharedMesh = source;
        foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("SB_") && (t.parent == null || !t.parent.name.StartsWith("SB_"))).ToArray())
            Object.DestroyImmediate(t.gameObject);
    }
}
