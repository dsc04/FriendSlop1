#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

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

    [MenuItem("Tools/FriendSlop/Build Foundation Scene")]
    public static void Build()
    {
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
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.tag = "Player";
        Object.DestroyImmediate(player.GetComponent<CapsuleCollider>()); // CharacterController does the collision
        player.transform.position = new Vector3(0f, 1.1f, 0f);
        player.GetComponent<Renderer>().sharedMaterial = MakeMaterial("PlayerMat", new Color(0.90f, 0.42f, 0.30f));
        var cc = player.AddComponent<CharacterController>();
        cc.height = 2f; cc.radius = 0.5f; cc.center = Vector3.zero;
        player.AddComponent<PlayerController>();

        // --- Camera ---
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        camGo.transform.position = new Vector3(0f, 6f, -8f);
        camGo.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
        camGo.AddComponent<CameraFollow>().target = player.transform;

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
}
#endif
