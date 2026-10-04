using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VikingBoatQuestSetup
{
    const string ScenePath = "Assets/_Project/Viking Village/Scenes/The_Viking_Village.unity";
    const string NextScenePath = "Assets/_Project/Scenes/Floor02.unity";
    const string PlayerPrefabPath = "Assets/_Project/Prefabs/PlayerRig.prefab";
    const string BoatPrefabPath = "Assets/_Project/Viking Village/Prefabs/Buildings/pf_build_boat_01.prefab";
    const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemy_Grunt.prefab";

    [MenuItem("GeminiGrowUp/設定維京村莊「擊敗敵人搭船過關」機制 (Setup Boat Quest)")]
    public static void SetupBoatQuest()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else return;
        }

        // 1. 關閉展示導覽攝影機
        GameObject camerasObj = GameObject.Find("Cameras");
        if (camerasObj != null)
        {
            camerasObj.SetActive(false);
            EditorUtility.SetDirty(camerasObj);
        }

        // 2. 確保主角就位
        GameObject playerRig = GameObject.Find("PlayerRig");
        if (playerRig == null)
        {
            var pPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (pPrefab != null)
            {
                playerRig = (GameObject)PrefabUtility.InstantiatePrefab(pPrefab);
                playerRig.name = "PlayerRig";
                playerRig.transform.position = new Vector3(13.7f, 5.4f, 4.4f);
                playerRig.transform.rotation = Quaternion.Euler(0f, 150f, 0f);
            }
        }

        // 3. 建立或設定搭乘用長船 QuestBoat
        GameObject questBoat = GameObject.Find("QuestBoat");
        VikingBoat boatScript = null;

        if (questBoat == null)
        {
            var boatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BoatPrefabPath);
            if (boatPrefab != null)
            {
                questBoat = (GameObject)PrefabUtility.InstantiatePrefab(boatPrefab);
                questBoat.name = "QuestBoat";
                Undo.RegisterCreatedObjectUndo(questBoat, "Create QuestBoat");
            }
            else
            {
                questBoat = new GameObject("QuestBoat");
            }
        }

        // 移除 Static 屬性，確保可動
        SetStaticRecursively(questBoat, false);

        // 碼頭水邊停泊位置（Y 座標 1.3f，面朝外海）
        questBoat.transform.position = new Vector3(-24.5f, 1.3f, 33.5f);
        questBoat.transform.rotation = Quaternion.Euler(0f, 120f, 0f);

        boatScript = questBoat.GetComponent<VikingBoat>();
        if (boatScript == null) boatScript = questBoat.AddComponent<VikingBoat>();

        boatScript.interactionRadius = 6.5f;
        boatScript.forwardSpeed = 9.0f;
        boatScript.reverseSpeed = 3.5f;
        boatScript.steeringTurnSpeed = 42.0f;
        boatScript.nextSceneName = "Floor02";
        boatScript.destinationPoint = new Vector3(-75f, 1.3f, 85f);
        boatScript.destinationRadius = 15f;

        // 建立乘船站立點
        Transform standing = questBoat.transform.Find("StandingPoint");
        if (standing == null)
        {
            GameObject sp = new GameObject("StandingPoint");
            sp.transform.SetParent(questBoat.transform);
            sp.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            sp.transform.localRotation = Quaternion.identity;
            standing = sp.transform;
        }
        boatScript.playerStandingPoint = standing;

        // 建立金黃色解鎖導引光柱
        Transform indicator = questBoat.transform.Find("ObjectiveBeacon");
        if (indicator == null)
        {
            GameObject beacon = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beacon.name = "ObjectiveBeacon";
            beacon.transform.SetParent(questBoat.transform);
            beacon.transform.localPosition = new Vector3(0f, 3.5f, 0f);
            beacon.transform.localScale = new Vector3(0.5f, 3.5f, 0.5f);

            var col = beacon.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            var mr = beacon.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                Material beaconMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
                beaconMat.color = new Color(1f, 0.85f, 0.2f, 0.7f);
                mr.sharedMaterial = beaconMat;
            }

            var lightObj = new GameObject("BeaconLight");
            lightObj.transform.SetParent(beacon.transform);
            lightObj.transform.localPosition = Vector3.zero;
            var l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.85f, 0.3f);
            l.range = 15f;
            l.intensity = 2.5f;

            indicator = beacon.transform;
        }
        boatScript.objectiveIndicator = indicator.gameObject;
        indicator.gameObject.SetActive(false); // 初始隱藏，敵人全滅後顯示

        // 4. 建立敵人管理器 EnemyWaveManager
        GameObject waveMgrObj = GameObject.Find("EnemyWaveManager");
        if (waveMgrObj == null)
        {
            waveMgrObj = new GameObject("EnemyWaveManager");
            Undo.RegisterCreatedObjectUndo(waveMgrObj, "Create EnemyWaveManager");
        }

        var waveMgr = waveMgrObj.GetComponent<VikingEnemyWaveManager>();
        if (waveMgr == null) waveMgr = waveMgrObj.AddComponent<VikingEnemyWaveManager>();

        waveMgr.targetBoat = boatScript;
        waveMgr.enemyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        waveMgr.autoStartDelay = 2.0f;
        waveMgr.spawnPoints = new List<Vector3>
        {
            new Vector3(19f, 20f, 9f),
            new Vector3(8f, 20f, 13f),
            new Vector3(9f, 20f, -1f),
            new Vector3(20f, 20f, 2f),
            new Vector3(14f, 20f, 15f)
        };

        // 5. 更新 Build Settings
        EnsureSceneInBuildSettings(ScenePath);
        EnsureSceneInBuildSettings(NextScenePath);

        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("[VikingBoatQuest] 成功配置「擊敗敵人搭船過關」完整機制！\n" +
                  "1. 長船停靠於碼頭 (QuestBoat)，位置已精確對齊水面。\n" +
                  "2. 敵人全滅後長船解鎖。\n" +
                  "3. 靠近按 [E] 鍵登船掌舵，可用 W/S/A/D 自行駕駛船隻。\n" +
                  "4. 航向外海指定地點（畫面上方會顯示距離與方位浮標），進入後螢幕淡出並載入下一關 (Floor02)！");

        EditorUtility.DisplayDialog("維京村莊搭船駕駛機制配置完成！",
            "已成功在《The Viking Village》場景中建立互動駕駛船隻機制：\n\n" +
            "1. ⚔️ 開場擊敗湧出的暗影守衛。\n" +
            "2. ⛵ 碼頭長船亮起金色光柱解鎖，靠近按 [E] 鍵登船。\n" +
            "3. 🎮 登船後切換為自由駕駛模式：\n" +
            "   - 【W】：加速航行\n" +
            "   - 【S】：倒退減速\n" +
            "   - 【A / D】：掌舵轉向\n" +
            "4. 📍 畫面上方顯示「外海目的地」方向與距離，並有水藍色航道浮標光柱引導。\n" +
            "5. 🌅 駕駛船隻進入指定外海範圍後，螢幕淡出並進入下一關 (Floor02)！\n\n" +
            "按下 Play (▶️) 即可立刻體驗！", "確定");
    }

    static void SetStaticRecursively(GameObject obj, bool isStatic)
    {
        obj.isStatic = isStatic;
        foreach (Transform child in obj.transform)
        {
            SetStaticRecursively(child.gameObject, isStatic);
        }
    }

    static void EnsureSceneInBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        foreach (var s in scenes)
        {
            if (s.path == scenePath) return;
        }
        scenes.Add(new EditorBuildSettingsScene(scenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[VikingBoatQuest] 已將場景加入 Build Settings: {scenePath}");
    }
}
