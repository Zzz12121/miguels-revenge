using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class DojoPlayerSetup
{
    const string ScenePath = "Assets/_Project/Scenes/DojoArena_ArchitecturePreview.unity";
    const string RigPath = "Assets/_Project/Prefabs/PlayerRig.prefab";
    public static string Install()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlaying || scene.path != ScenePath) throw new InvalidOperationException("Open the dojo preview in Edit mode first.");
        if (scene.isDirty) throw new InvalidOperationException("Save existing scene edits before installing the player.");
        if (scene.GetRootGameObjects().Any(g => g.GetComponentInChildren<PlayerHealth>(true))) throw new InvalidOperationException("Scene already contains a player.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        var previewCamera = GameObject.Find("PreviewLightingAndCameras/Main Camera");
        if (!prefab || !previewCamera) throw new InvalidOperationException("Shared player prefab or preview camera missing.");
        Physics.SyncTransforms();
        if (!Physics.Raycast(new Vector3(0, 10, 65), Vector3.down, out var floor, 20, ~0, QueryTriggerInteraction.Ignore)) throw new Exception("Courtyard spawn has no floor.");
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(rig, "Add existing player to dojo");
        rig.name = "PlayerRig";
        var player = rig.GetComponentInChildren<PlayerHealth>(true);
        var cc = player.GetComponent<CharacterController>();
        var movement = player.GetComponent<ThirdPersonController>();
        var camera = rig.GetComponentInChildren<Camera>(true);
        var follow = camera.GetComponent<ThirdPersonCamera>();
        rig.transform.position = floor.point + Vector3.up * .06f;
        player.transform.localPosition = new Vector3(0, cc.height * .5f - cc.center.y, 0);
        player.transform.rotation = Quaternion.identity;
        movement.cameraTransform = camera.transform;
        follow.target = player.transform;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 350);
        foreach (var skills in rig.GetComponentsInChildren<VoidRendSkillDemo>(true))
        {
            skills.caster = player.transform;
            skills.skillCamera = camera;
            skills.playOnStart = false;
            skills.loopPreview = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(skills);
        }
        Undo.RecordObject(previewCamera, "Use player camera in dojo");
        previewCamera.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(movement);
        PrefabUtility.RecordPrefabInstancePropertyModifications(follow);
        PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = player.gameObject;
        return "Saved shared cat-warrior PlayerRig at " + player.transform.position + ", feet=" + (player.transform.position.y + cc.center.y - cc.height * .5f) + "; player camera targetOffset=" + follow.targetOffset + "; preview camera inactive; skill autoplay off.";
    }
    public static string InspectRuntime()
    {
        var p = PlayerHealth.Find();
        if (!p) throw new Exception("Player missing");
        var cc = p.GetComponent<CharacterController>();
        var camera = Camera.main;
        return "player=" + p.transform.position + " feet="+p.FeetY+" grounded="+cc.isGrounded+" health="+p.Health+" active cameras="+Camera.allCamerasCount+" listeners="+UnityEngine.Object.FindObjectsByType<AudioListener>().Count(l=>l.isActiveAndEnabled)+" camera="+camera.transform.position+" target="+camera.GetComponent<ThirdPersonCamera>().target.name+" animator="+p.GetComponentInChildren<Animator>().runtimeAnimatorController.name;
    }
}
