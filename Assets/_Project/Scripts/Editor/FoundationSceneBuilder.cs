#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.InputSystem;

// One-click starter scene.
// In Unity's TOP MENU:  Tools ▸ FriendSlop ▸ Build Foundation Scene
//
// Builds a floor + a controllable player capsule + a follow camera + a light,
// saves it to Assets/_Project/Scenes/Main.unity, and opens it. Then press Play.
public static class FoundationSceneBuilder
{
    const string SceneDir  = "Assets/_Project/Scenes";
    const string ScenePath = SceneDir + "/Main.unity";
    const string MatDir    = "Assets/_Project/Materials";

    const string InputActionsAssetName = "PlayerMovementAction";

    [MenuItem("Tools/FriendSlop/Build Foundation Scene")]
    public static void Build()
    {
        // Don't blow away unsaved work — offer to save open scenes first (Cancel aborts).
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        Directory.CreateDirectory(SceneDir);
        Directory.CreateDirectory(MatDir);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // --- Sun ---
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // --- Ground (50 x 50 units) ---
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(5f, 1f, 5f);
        ground.GetComponent<Renderer>().sharedMaterial = MakeMaterial("GroundMat", new Color(0.48f, 0.52f, 0.58f));

        // --- Spawn point (handy once you add multiplayer) ---
        var spawn = new GameObject("SpawnPoint");
        spawn.transform.position = Vector3.zero;

        // --- Player ---
        const float capsuleHeight = 2f;
        const float capsuleRadius = 0.5f;

        var player = new GameObject("Player");
        player.name = "Player";
        player.tag = "Player";
        player.transform.position = new Vector3(0f, capsuleHeight * 0.5f, 0f);
        var cc = player.AddComponent<CharacterController>();
        cc.height = capsuleHeight;
        cc.radius = capsuleRadius;
        cc.center = Vector3.zero;
        var playerController = player.AddComponent<PlayerController>();

        // --- Capsule to Player --- 
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "PlayerBody";
        Object.DestroyImmediate(capsule.GetComponent<CapsuleCollider>());
        capsule.transform.SetParent(player.transform);
        capsule.transform.localPosition = Vector3.zero;
        capsule.GetComponent<Renderer>().sharedMaterial = MakeMaterial("PlayerMat", new Color(0.90f, 0.42f, 0.30f));

        // --- Camera ---
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.transform.SetParent(player.transform);
        camGo.transform.localPosition = new Vector3(0f, capsuleHeight * 0.4f, 0f);
        camGo.transform.localRotation = Quaternion.identity;

        // --- Player Input Handler ---
        var inputGo = new GameObject("PlayerInputHandler");
        var inputHandler = inputGo.AddComponent<PlayerInputHandler>();

        // --- Bind private SerializeField references to PlayerController ---
        WireReferences(playerController, cc, cam, inputHandler);
        WireInputActionAsset(inputHandler);

        // --- Save + register in Build Settings ---
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuild(ScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[FriendSlop] Foundation scene created at {ScenePath}. Press Play — WASD to move, Space to jump.");
        EditorUtility.DisplayDialog("FriendSlop",
            "Foundation scene created ✅\n\n" +
            "It's open now — just press Play.\nWASD = move, Space = jump.\n\n" +
            "(Saved to Assets/_Project/Scenes/Main.unity)", "Let's go");
    }

    static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard"); // fallback if URP name ever changes
        var m = new Material(shader);
        m.color = color;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);

        string path = $"{MatDir}/{name}.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void AddSceneToBuild(string path)
    {
        var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!scenes.Exists(s => s.path == path))
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void WireReferences(PlayerController controller, CharacterController cc, Camera cam, PlayerInputHandler input)
    {
        var so = new SerializedObject(controller);
 
        SetIfExists(so, "characterController", cc);
        SetIfExists(so, "mainCamera", cam);
        SetIfExists(so, "playerInputHandler", input);
 
        so.ApplyModifiedPropertiesWithoutUndo();
    }
 
    static void SetIfExists(SerializedObject so, string propertyName, Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogWarning($"[FriendSlop] Поле '{propertyName}' не найдено на PlayerController — " +
                              $"проверьте, что имя поля не поменялось.");
            return;
        }
        prop.objectReferenceValue = value;
    }

        static void WireInputActionAsset(PlayerInputHandler inputHandler)
    {
        string[] guids = AssetDatabase.FindAssets($"{InputActionsAssetName} t:InputActionAsset");
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[FriendSlop] Input Action Asset '{InputActionsAssetName}' не найден в проекте — " +
                              $"привяжите его на PlayerInputHandler вручную, либо проверьте имя (константа InputActionsAssetName).");
            return;
        }
        if (guids.Length > 1)
        {
            Debug.LogWarning($"[FriendSlop] Найдено несколько ассетов с именем '{InputActionsAssetName}' — " +
                              $"берём первый найденный, проверьте вручную, тот ли это файл.");
        }
 
        string path = AssetDatabase.GUIDToAssetPath(guids[0]);
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
 
        var so = new SerializedObject(inputHandler);
        SetIfExists(so, "playerControls", asset);
        so.ApplyModifiedPropertiesWithoutUndo();
    }


}
#endif
