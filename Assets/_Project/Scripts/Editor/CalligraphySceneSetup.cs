#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class CalligraphySceneSetup
{
    const string Prefab = "Assets/_Project/Prefabs/Boss/PF_CalligraphyElder_Presentation.prefab";
    public static void ConfigurePractice(GameObject elder)
    {
        elder.tag = "Enemy";
        if (!elder.GetComponent<CalligraphyPracticeTarget>()) elder.AddComponent<CalligraphyPracticeTarget>();
        var marker = elder.GetComponent<VoidRendTargetable>();
        if (!marker) marker = elder.AddComponent<VoidRendTargetable>();
        // The giant brush must not expand fallback body regions. Use explicit skeleton-attached zones.
        var settings = new SerializedObject(marker);
        settings.FindProperty("generateFallbackAimZones").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var tether = elder.GetComponent<TetherInteractable>();
        if (!tether) tether = elder.AddComponent<TetherInteractable>();
        tether.mode = TetherInteractable.TetherMode.Pullable;
        foreach (string bone in new[] { "Head", "Chest", "Left_Hand", "Right_Hand", "Left_LowerLeg", "Right_LowerLeg" })
        {
            var anchor = elder.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bone);
            if (!anchor || anchor.Find("SkillAimZone")) continue;
            var zone = new GameObject("SkillAimZone"); zone.transform.SetParent(anchor, false);
            var sphere = zone.AddComponent<SphereCollider>(); sphere.isTrigger = true;
            sphere.radius = (bone == "Chest" ? .25f : .16f) / Mathf.Max(.01f, anchor.lossyScale.x);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(elder);
    }

    [MenuItem("VFX/Calligraphy/Install Practice Target and Dock Elder")]
    public static void InstallMenu() => Debug.Log(Install());
    public static string Install()
    {
        if (EditorApplication.isPlaying) throw new Exception("Stop Play before installing.");
        var practice = SceneManager.GetSceneByPath("Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity");
        if (!practice.isLoaded) practice = EditorSceneManager.OpenScene("Assets/_Project/Scenes/VFX_VoidRend_Bocchi.unity", OpenSceneMode.Additive);
        var elder = practice.GetRootGameObjects().First(g => g.name == "CalligraphyElder_Showcase");
        ConfigurePractice(elder);
        EditorSceneManager.MarkSceneDirty(practice); EditorSceneManager.SaveScene(practice);

        var village = SceneManager.GetSceneByPath("Assets/_Project/Scenes/The_Viking_Village.unity");
        if (!village.isLoaded) village = EditorSceneManager.OpenScene("Assets/_Project/Scenes/The_Viking_Village.unity", OpenSceneMode.Additive);
        var dock = village.GetRootGameObjects().FirstOrDefault(g => g.name == "CalligraphyElder_DockDisplay");
        if (!dock) dock = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), village);
        dock.name = "CalligraphyElder_DockDisplay"; dock.tag = "Untagged";
        Vector3 position = new Vector3(-15.8f, 3.2f, 48.4f);
        Physics.SyncTransforms();
        var ground = Physics.RaycastAll(position + Vector3.up * 10, Vector3.down, 20, ~0, QueryTriggerInteraction.Ignore)
            .Where(h => h.collider.gameObject.scene == village && h.collider.name.Contains("boardwalk"))
            .OrderByDescending(h => h.point.y).ToArray();
        if (ground.Length == 0) throw new Exception("Dock boardwalk not found under elder location.");
        position.y = ground[0].point.y + .02f;
        dock.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 155, 0));
        var brush = dock.GetComponent<CalligraphyBrushPresentation>(); brush.showcase = true;
        // Neutral display: no damage receiver, enemy marker, or raycast collider.
        foreach (var c in dock.GetComponentsInChildren<Collider>(true)) { c.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(c); }
        if (!dock.GetComponent<CalligraphyNameplate>()) dock.AddComponent<CalligraphyNameplate>();
        PrefabUtility.RecordPrefabInstancePropertyModifications(dock);
        PrefabUtility.RecordPrefabInstancePropertyModifications(dock.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(brush);
        EditorSceneManager.MarkSceneDirty(village); EditorSceneManager.SaveScene(village);
        return "Practice enemy and body aim zones installed; neutral dock elder at " + position;
    }
}
#endif
