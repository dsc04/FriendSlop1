#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-click multiplayer wiring.
// In Unity's TOP MENU:  Tools ▸ FriendSlop ▸ Set Up Multiplayer Scene
//
// Takes the foundation scene (Main.unity) and makes it network-ready:
//   1. Turns the player into a prefab at Assets/_Project/Prefabs/Player.prefab
//      (capsule + CharacterController + PlayerController + the network pieces).
//   2. Removes the hand-placed Player from the scene — from now on the
//      NetworkManager spawns one player per person who connects.
//   3. Adds a NetworkManager (+ Unity Transport) with that prefab assigned.
//   4. Adds the ConnectionMenu (the HOST / JOIN screen).
//
// Safe to run again after pulling changes — it rebuilds the prefab and reuses
// the scene objects instead of duplicating them.
public static class MultiplayerSceneSetup
{
    const string ScenePath     = "Assets/_Project/Scenes/Main.unity";
    const string PrefabDir     = "Assets/_Project/Prefabs";
    const string PrefabPath    = PrefabDir + "/Player.prefab";
    const string PlayerMatPath = "Assets/_Project/Materials/PlayerMat.mat";

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

        // The scene must not contain a hand-placed player — the network spawns them.
        var scenePlayer = GameObject.Find("Player");
        if (scenePlayer != null) Object.DestroyImmediate(scenePlayer);

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
            "A friend presses Play in their copy, types the code, clicks JOIN.\n\n" +
            "To test alone first: Window ▸ Multiplayer ▸ Multiplayer Play Mode →\n" +
            "enable Player 2 → Play. Host in one window, join from the other.\n\n" +
            "(Needs the project linked in Edit ▸ Project Settings ▸ Services.)",
            "Let's go");
    }

    // Build the networked player prefab from scratch (idempotent — same result every run).
    static GameObject BuildPlayerPrefab()
    {
        Directory.CreateDirectory(PrefabDir);

        var temp = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        temp.name = "Player";
        temp.tag = "Player";
        Object.DestroyImmediate(temp.GetComponent<CapsuleCollider>()); // CharacterController does the collision
        temp.transform.position = new Vector3(0f, 1.1f, 0f);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(PlayerMatPath);
        if (mat != null) temp.GetComponent<Renderer>().sharedMaterial = mat;

        var cc = temp.AddComponent<CharacterController>();
        cc.height = 2f; cc.radius = 0.5f; cc.center = Vector3.zero;

        temp.AddComponent<PlayerController>();

        // The network pieces:
        temp.AddComponent<NetworkObject>();          // makes it a networked thing at all
        temp.AddComponent<ClientNetworkTransform>(); // syncs position/rotation, owner drives
        temp.AddComponent<NetworkPlayerSetup>();     // owner-only controls + camera + spawn spot

        var prefab = PrefabUtility.SaveAsPrefabAsset(temp, PrefabPath);
        Object.DestroyImmediate(temp);
        return prefab;
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
