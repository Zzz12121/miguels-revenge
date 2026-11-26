// Pipeline eval_file body. Creates an unsaved test scene using the production prefab.
if (Application.isPlaying) throw new Exception("Exit Play mode before creating the fixture");
if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().isDirty)
    throw new Exception("Save your current scene before creating the fixture");
UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
    UnityEditor.SceneManagement.NewSceneMode.Single);
var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
floor.name = "Game feel validation floor";
floor.transform.position = new Vector3(0, -.5f, 0);
floor.transform.localScale = new Vector3(80, 1, 80);
var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/PlayerRig.prefab");
var rig = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
rig.name = "Game feel production PlayerRig";
rig.GetComponentInChildren<ThirdPersonController>().transform.position = new Vector3(0, 1.1f, 0);
var sun = new GameObject("Validation light").AddComponent<Light>();
sun.type = LightType.Directional;
sun.transform.rotation = Quaternion.Euler(45, -30, 0);
return "Unsaved production-prefab fixture ready";
