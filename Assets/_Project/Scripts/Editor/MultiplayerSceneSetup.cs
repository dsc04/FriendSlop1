#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

// One-click multiplayer wiring.
// In Unity's TOP MENU:  Tools ▸ FriendSlop ▸ Set Up Multiplayer Scene
//
// Takes the foundation scene (Main.unity) and makes it network-ready:
//   1. Builds Assets/_Project/Prefabs/Player.prefab — the same first-person player
//      FoundationSceneBuilder creates (body, camera, input handler, wired refs),
//      plus the network pieces on top.
//   2. Removes the hand-placed Player and PlayerInputHandler from the scene —
//      from now on the NetworkManager spawns one player per person who connects.
//   3. Adds a MenuCamera so you can see the world before hosting/joining.
//   4. Adds a NetworkManager (+ Unity Transport) with the player prefab assigned.
//   5. Adds the ConnectionMenu (the HOST / JOIN screen).
//
// Safe to run again after pulling changes — it rebuilds the prefab and reuses
// the scene objects instead of duplicating them.
public static class MultiplayerSceneSetup
{
    const string ScenePath     = "Assets/_Project/Scenes/Main.unity";
    const string PrefabDir     = "Assets/_Project/Prefabs";
    const string PrefabPath    = PrefabDir + "/Player.prefab";
    const string PlayerMatPath = "Assets/_Project/Materials/PlayerMat.mat";

    // Must match the asset FoundationSceneBuilder looks for.
    const string InputActionsAssetName = "PlayerMovementAction";

    [MenuItem("Tools/FriendSlop/Set Up Multiplayer Scene")]
    public static void Setup()
    {
        if (!File.Exists(ScenePath))
        {
            EditorUtility.DisplayDialog("FriendSlop",
                "Main scene not found.\n\nRun  Tools ▸ FriendSlop ▸ Build Foundation Scene  first, then run this again.",
                "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var playerPrefab = BuildPlayerPrefab();

        // The hand-placed player pieces go away — the network spawns players now.
        var scenePlayer = GameObject.Find("Player");
        if (scenePlayer != null) Object.DestroyImmediate(scenePlayer);      // its child camera goes with it
        var sceneInput = GameObject.Find("PlayerInputHandler");
        if (sceneInput != null) Object.DestroyImmediate(sceneInput);        // the input handler lives on the prefab now

        // --- Menu camera (shows the world until your player spawns) ---
        var menuCam = GameObject.Find("MenuCamera");
        if (menuCam == null)
        {
            menuCam = new GameObject("MenuCamera");
            menuCam.AddComponent<Camera>();
            menuCam.AddComponent<AudioListener>();
            menuCam.transform.position = new Vector3(0f, 6f, -8f);
            menuCam.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
        }

        // --- NetworkManager + transport ---
        var nmGo = GameObject.Find("NetworkManager");
        if (nmGo == null) nmGo = new GameObject("NetworkManager");
        var nm = nmGo.GetComponent<NetworkManager>();
        if (nm == null) nm = nmGo.AddComponent<NetworkManager>();
        var utp = nmGo.GetComponent<UnityTransport>();
        if (utp == null) utp = nmGo.AddComponent<UnityTransport>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = utp;
        nm.NetworkConfig.PlayerPrefab = playerPrefab;
        RegisterNetworkPrefab(nm, playerPrefab);
        EditorUtility.SetDirty(nmGo);

        // --- HOST / JOIN menu ---
        var menuGo = GameObject.Find("ConnectionMenu");
        if (menuGo == null) menuGo = new GameObject("ConnectionMenu");
        if (menuGo.GetComponent<ConnectionMenu>() == null) menuGo.AddComponent<ConnectionMenu>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[FriendSlop] Multiplayer scene ready. Press Play → HOST a room / JOIN by code.");
        EditorUtility.DisplayDialog("FriendSlop",
            "Multiplayer is wired up ✅\n\n" +
            "Press Play → click HOST a room → a short code appears.\n" +
            "A friend presses Play in their copy, types the code, clicks JOIN.\n" +
            "(Esc frees the mouse cursor while playing.)\n\n" +
            "To test alone first: Window ▸ Multiplayer ▸ Multiplayer Play Mode →\n" +
            "enable Player 2 → Play. Host in one window, join from the other.\n\n" +
            "(Needs the project linked in Edit ▸ Project Settings ▸ Services.)",
            "Let's go");
    }

    // Builds the networked player prefab from scratch (idempotent — same result every
    // run). Mirrors the player that FoundationSceneBuilder creates, then adds the
    // network pieces. Camera / AudioListener / PlayerInputHandler are saved DISABLED:
    // NetworkPlayerSetup switches them on only for the player YOU own.
    static GameObject BuildPlayerPrefab()
    {
        Directory.CreateDirectory(PrefabDir);

        const float capsuleHeight = 2f;
        const float capsuleRadius = 0.5f;

        var root = new GameObject("Player");
        root.tag = "Player";
        root.transform.position = new Vector3(0f, capsuleHeight * 0.5f, 0f);
        var cc = root.AddComponent<CharacterController>();
        cc.height = capsuleHeight;
        cc.radius = capsuleRadius;
        cc.center = Vector3.zero;
        var controller = root.AddComponent<PlayerController>();
        var input = root.AddComponent<PlayerInputHandler>();
        input.enabled = false;   // owner-only (see NetworkPlayerSetup)

        // Visible body.
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "PlayerBody";
        Object.DestroyImmediate(body.GetComponent<CapsuleCollider>()); // CharacterController does the collision
        body.transform.SetParent(root.transform);
        body.transform.localPosition = Vector3.zero;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath);
        if (mat != null) body.GetComponent<Renderer>().sharedMaterial = mat;

        // First-person camera (child, pitches up/down; the body turns left/right).
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        var ears = camGo.AddComponent<AudioListener>();
        cam.enabled = false;    // owner-only
        ears.enabled = false;   // owner-only
        camGo.transform.SetParent(root.transform);
        camGo.transform.localPosition = new Vector3(0f, capsuleHeight * 0.4f, 0f);
        camGo.transform.localRotation = Quaternion.identity;

        // Same inspector references FoundationSceneBuilder wires up.
        var so = new SerializedObject(controller);
        SetIfExists(so, "characterController", cc);
        SetIfExists(so, "mainCamera", cam);
        SetIfExists(so, "playerInputHandler", input);
        so.ApplyModifiedPropertiesWithoutUndo();
        WireInputActionAsset(input);

        // The network pieces:
        root.AddComponent<NetworkObject>();          // makes it a networked thing at all
        root.AddComponent<ClientNetworkTransform>(); // syncs position/rotation, owner drives
        root.AddComponent<NetworkPlayerSetup>();     // owner-only wake-up + spawn spot

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static void SetIfExists(SerializedObject so, string propertyName, Object value)
    {
        var prop = so.FindProperty(propertyName);
        if (prop == null)
        {
            Debug.LogWarning($"[FriendSlop] Field '{propertyName}' not found on {so.targetObject.GetType().Name} — " +
                             "did it get renamed? Wire it on the Player prefab by hand.");
            return;
        }
        prop.objectReferenceValue = value;
    }

    static void WireInputActionAsset(PlayerInputHandler inputHandler)
    {
        string[] guids = AssetDatabase.FindAssets($"{InputActionsAssetName} t:InputActionAsset");
        if (guids.Length == 0)
        {
            Debug.LogWarning($"[FriendSlop] Input Action Asset '{InputActionsAssetName}' not found — " +
                             "assign it on the Player prefab's PlayerInputHandler by hand.");
            return;
        }

        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
        var so = new SerializedObject(inputHandler);
        SetIfExists(so, "playerControls", asset);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // Netcode requires every spawnable prefab to be on a NetworkPrefabsList that the
    // NetworkManager knows about. This mirrors what the NetworkManager inspector does.
    static void RegisterNetworkPrefab(NetworkManager nm, GameObject prefab)
    {
        NetworkPrefabsList list = null;
        foreach (var guid in AssetDatabase.FindAssets("t:NetworkPrefabsList"))
        {
            list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(AssetDatabase.GUIDToAssetPath(guid));
            if (list != null) break;
        }
        if (list == null)
        {
            list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
            AssetDatabase.CreateAsset(list, "Assets/DefaultNetworkPrefabs.asset");
        }

        bool alreadyListed = false;
        foreach (var entry in list.PrefabList)
            if (entry != null && entry.Prefab == prefab) { alreadyListed = true; break; }
        if (!alreadyListed)
            list.Add(new NetworkPrefab { Prefab = prefab });
        EditorUtility.SetDirty(list);

        if (!nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(list))
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(list);
    }
}
#endif
