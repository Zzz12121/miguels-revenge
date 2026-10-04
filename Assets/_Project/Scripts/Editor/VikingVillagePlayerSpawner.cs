using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VikingVillagePlayerSpawner
{
    const string ScenePath = "Assets/_Project/Viking Village/Scenes/The_Viking_Village.unity";
    const string PlayerPrefabPath = "Assets/_Project/Prefabs/PlayerRig.prefab";

    [MenuItem("GeminiGrowUp/在維京村莊加入第三人稱主角 (Add Player to Viking Village)")]
    public static void AddPlayerToVikingVillage()
    {
        Scene currentScene = SceneManager.GetActiveScene();
        if (currentScene.path != ScenePath)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                currentScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            else
            {
                return;
            }
        }

        // 1. 關閉原有的展示用導覽攝影機（避免 AudioListener 重複與畫面搶鏡）
        GameObject camerasObj = GameObject.Find("Cameras");
        if (camerasObj != null)
        {
            camerasObj.SetActive(false);
            EditorUtility.SetDirty(camerasObj);
            Debug.Log("[VikingVillage] 已關閉展示攝影機 Cameras 物件。");
        }

        // 2. 檢查是否已有 PlayerRig
        GameObject existingPlayerRig = GameObject.Find("PlayerRig");
        GameObject playerRigInstance = null;

        if (existingPlayerRig != null)
        {
            playerRigInstance = existingPlayerRig;
            Debug.Log("[VikingVillage] 場景中已存在 PlayerRig，為您選中該物件。");
        }
        else
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                EditorUtility.DisplayDialog("錯誤", $"找不到主角 Prefab：{PlayerPrefabPath}", "確定");
                return;
            }

            playerRigInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            playerRigInstance.name = "PlayerRig";
            Undo.RegisterCreatedObjectUndo(playerRigInstance, "Spawn PlayerRig");

            // 尋找村莊主要廣場地面高度
            Vector3 spawnPoint = new Vector3(13.7f, 20f, 4.4f);
            if (Physics.Raycast(spawnPoint, Vector3.down, out RaycastHit hit, 50f))
            {
                spawnPoint.y = hit.point.y + 0.1f;
            }
            else
            {
                spawnPoint.y = 5.3f;
            }

            playerRigInstance.transform.position = spawnPoint;
            playerRigInstance.transform.rotation = Quaternion.Euler(0f, 150f, 0f);

            // 確保子物件 Player 本地為 (0, 0, 0)
            Transform playerChild = playerRigInstance.transform.Find("Player");
            if (playerChild != null)
            {
                playerChild.localPosition = Vector3.zero;
            }

            Debug.Log($"[VikingVillage] 已在村莊生成主角 PlayerRig，位置：{spawnPoint}");
        }

        Selection.activeGameObject = playerRigInstance;
        EditorSceneManager.MarkSceneDirty(currentScene);
        EditorSceneManager.SaveScene(currentScene);

        Debug.Log("=========================================\n" +
                  "【第三人稱主角鍵盤操作說明】\n" +
                  "- WASD / 方向鍵：移動（角色自動面向鏡頭移動方向）\n" +
                  "- 滑鼠移動：旋轉第三人稱視角\n" +
                  "- 滑鼠滾輪：鏡頭拉近 / 拉遠\n" +
                  "- 左 Shift：衝刺加速\n" +
                  "- 空白鍵 (Space)：跳躍\n" +
                  "- 滑鼠左鍵：揮劍攻擊（三段連擊）\n" +
                  "- Esc 鍵：解鎖滑鼠游標；點擊畫面重新鎖定\n" +
                  "=========================================");
    }
}
