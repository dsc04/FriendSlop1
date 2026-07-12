#if UNITY_EDITOR
using System.IO;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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
//   5. Builds the game menu (Canvas UI): main menu with Host / Join by code,
//      which becomes the Esc pause menu once you're in a room.
//
// Safe to run again after pulling changes — it rebuilds the prefab and the menu
// and reuses the other scene objects instead of duplicating them.
public static class MultiplayerSceneSetup
{
    const string ScenePath     = "Assets/_Project/Scenes/Main.unity";
    const string PrefabDir     = "Assets/_Project/Prefabs";
    const string PrefabPath    = PrefabDir + "/Player.prefab";
    const string PlayerMatPath = "Assets/_Project/Materials/PlayerMat.mat";

    // Must match the asset FoundationSceneBuilder looks for.
    const string InputActionsAssetName = "PlayerMovementAction";

    // Menu palette.
    static readonly Color WindowBg  = new Color(0.09f, 0.10f, 0.13f, 0.97f);
    static readonly Color DimBg     = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color ButtonBg  = new Color(0.21f, 0.24f, 0.29f, 1f);
    static readonly Color AccentBg  = new Color(0.13f, 0.42f, 0.27f, 1f);
    static readonly Color FieldBg   = new Color(0.14f, 0.16f, 0.20f, 1f);
    static readonly Color TextMain  = new Color(0.93f, 0.94f, 0.96f, 1f);
    static readonly Color TextSoft  = new Color(0.62f, 0.66f, 0.72f, 1f);
    static readonly Color TextWarn  = new Color(0.95f, 0.72f, 0.45f, 1f);

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
        if (!EnsureTmpResources())
            return;
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

        // --- The game menu (main menu + Esc pause menu) ---
        BuildGameMenu();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[FriendSlop] Multiplayer scene ready. Press Play → HOST A ROOM / JOIN by code. Esc = menu.");
        EditorUtility.DisplayDialog("FriendSlop",
            "Multiplayer + game menu are wired up ✅\n\n" +
            "Press Play → HOST A ROOM → a short code appears in the menu.\n" +
            "A friend presses Play in their copy, types the code, clicks JOIN.\n" +
            "While playing, Esc opens the menu (code, players, Resume, Leave).\n\n" +
            "To test alone first: Window ▸ Multiplayer ▸ Multiplayer Play Mode →\n" +
            "enable Player 2 → Play. Host in one window, join from the other.",
            "Let's go");
    }

    // The menu is TextMeshPro-based; its resources are a one-time import.
    static bool EnsureTmpResources()
    {
        if (TMP_Settings.instance != null) return true;

        TMP_PackageResourceImporter.ImportResources(true, false, false);
        EditorUtility.DisplayDialog("FriendSlop",
            "TextMeshPro resources were just imported (one-time setup).\n\n" +
            "Now run  Tools ▸ FriendSlop ▸ Set Up Multiplayer Scene  once more.",
            "OK");
        return false;
    }

    // ─────────────────────────────── player prefab ───────────────────────────────

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

    // ─────────────────────────────── game menu UI ───────────────────────────────

    static void BuildGameMenu()
    {
        // Old versions of the menu go away; the canvas is rebuilt fresh every run.
        var legacy = GameObject.Find("ConnectionMenu");
        if (legacy != null) Object.DestroyImmediate(legacy);
        var oldMenu = GameObject.Find("GameMenu");
        if (oldMenu != null) Object.DestroyImmediate(oldMenu);

        // uGUI needs an EventSystem — with the New Input System's UI module.
        var esGo = GameObject.Find("EventSystem");
        if (esGo == null) esGo = new GameObject("EventSystem");
        if (esGo.GetComponent<EventSystem>() == null) esGo.AddComponent<EventSystem>();
        var oldModule = esGo.GetComponent<StandaloneInputModule>();
        if (oldModule != null) Object.DestroyImmediate(oldModule);   // old-input module would throw here
        if (esGo.GetComponent<InputSystemUIInputModule>() == null) esGo.AddComponent<InputSystemUIInputModule>();

        // --- Canvas root ---
        var canvasGo = new GameObject("GameMenu", typeof(RectTransform));
        canvasGo.layer = 5; // UI
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var session = canvasGo.AddComponent<MultiplayerSession>();
        var menu = canvasGo.AddComponent<PauseMenuUI>();

        // --- Full-screen dim + centered window ---
        var menuRoot = MakeUiObject("MenuRoot", canvasGo.transform);
        var dim = menuRoot.AddComponent<Image>();
        dim.color = DimBg;
        Stretch(menuRoot.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

        var window = MakeUiObject("Window", menuRoot.transform);
        var windowImg = window.AddComponent<Image>();
        windowImg.sprite = UiSprite();
        windowImg.type = Image.Type.Sliced;
        windowImg.color = WindowBg;
        var windowRt = window.GetComponent<RectTransform>();
        windowRt.sizeDelta = new Vector2(520, 0);
        var layout = window.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(32, 32, 28, 28);
        layout.spacing = 16;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = window.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        MakeLabel(window.transform, "Title", "FRIENDSLOP", 40, TextMain,
            TextAlignmentOptions.Center, FontStyles.Bold);

        // --- "Not in a room yet" group: Host / Join ---
        var notConnected = MakeGroup(window.transform, "NotConnectedGroup", 12);
        var hostButton = MakeButton(notConnected.transform, "HostButton", "HOST A ROOM", AccentBg, 56, 26);
        MakeLabel(notConnected.transform, "OrLabel", "— or join a friend —", 20, TextSoft,
            TextAlignmentOptions.Center);
        var joinRow = MakeUiObject("JoinRow", notConnected.transform);
        var rowLayout = joinRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 12;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;
        var joinCodeInput = MakeInputField(joinRow.transform, "JoinCodeInput", "ROOM CODE", 56);
        var joinButton = MakeButton(joinRow.transform, "JoinButton", "JOIN", ButtonBg, 56, 24);
        joinButton.GetComponent<LayoutElement>().preferredWidth = 140;

        // --- "In a room" group: code, players, Resume, Leave ---
        var connected = MakeGroup(window.transform, "ConnectedGroup", 12);
        var roomCodeLabel = MakeLabel(connected.transform, "RoomCodeLabel", "Room code:  ······", 28, TextMain,
            TextAlignmentOptions.Center, FontStyles.Bold);
        var copyCodeButton = MakeButton(connected.transform, "CopyCodeButton", "Copy code", ButtonBg, 42, 20);
        var playersLabel = MakeLabel(connected.transform, "PlayersLabel", "Players:  – / –", 24, TextSoft,
            TextAlignmentOptions.Center);
        var resumeButton = MakeButton(connected.transform, "ResumeButton", "RESUME  (Esc)", AccentBg, 56, 26);
        var leaveButton = MakeButton(connected.transform, "LeaveButton", "Leave room", ButtonBg, 44, 20);

        // --- Shared bottom: status line + quit ---
        var statusLabel = MakeLabel(window.transform, "StatusLabel", "", 20, TextWarn,
            TextAlignmentOptions.Center);
        var quitButton = MakeButton(window.transform, "QuitButton", "Quit game", ButtonBg, 40, 20);

        // --- Wire everything to the scripts ---
        menu.session = session;
        menu.menuRoot = menuRoot;
        menu.notConnectedGroup = notConnected;
        menu.connectedGroup = connected;
        menu.hostButton = hostButton;
        menu.joinCodeInput = joinCodeInput;
        menu.joinButton = joinButton;
        menu.roomCodeLabel = roomCodeLabel;
        menu.copyCodeButton = copyCodeButton;
        menu.playersLabel = playersLabel;
        menu.resumeButton = resumeButton;
        menu.leaveButton = leaveButton;
        menu.quitButton = quitButton;
        menu.statusLabel = statusLabel;
        EditorUtility.SetDirty(canvasGo);
    }

    // ---- small UI builders ----

    static Sprite UiSprite() =>
        AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

    static GameObject MakeUiObject(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = 5; // UI
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject MakeGroup(Transform parent, string name, float spacing)
    {
        var go = MakeUiObject(name, parent);
        var layout = go.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        return go;
    }

    static TextMeshProUGUI MakeLabel(Transform parent, string name, string text, float size,
        Color color, TextAlignmentOptions align, FontStyles style = FontStyles.Normal)
    {
        var go = MakeUiObject(name, parent);
        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = align;
        label.fontStyle = style;
        return label;
    }

    static Button MakeButton(Transform parent, string name, string text, Color bg,
        float height, float fontSize)
    {
        var go = MakeUiObject(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = UiSprite();
        img.type = Image.Type.Sliced;
        img.color = bg;
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;

        var label = MakeLabel(go.transform, "Label", text, fontSize, TextMain,
            TextAlignmentOptions.Center, FontStyles.Bold);
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        return button;
    }

    static TMP_InputField MakeInputField(Transform parent, string name, string placeholderText, float height)
    {
        var go = MakeUiObject(name, parent);
        var img = go.AddComponent<Image>();
        img.sprite = UiSprite();
        img.type = Image.Type.Sliced;
        img.color = FieldBg;
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1;

        var viewport = MakeUiObject("Text Area", go.transform);
        viewport.AddComponent<RectMask2D>();
        var viewportRt = viewport.GetComponent<RectTransform>();
        Stretch(viewportRt, new Vector2(16, 8), new Vector2(-16, -8));

        var placeholder = MakeLabel(viewport.transform, "Placeholder", placeholderText, 24, TextSoft,
            TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
        Stretch(placeholder.rectTransform, Vector2.zero, Vector2.zero);
        var text = MakeLabel(viewport.transform, "Text", "", 24, TextMain,
            TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform, Vector2.zero, Vector2.zero);

        var field = go.AddComponent<TMP_InputField>();
        field.targetGraphic = img;
        field.textViewport = viewportRt;
        field.textComponent = text;
        field.placeholder = placeholder;
        field.characterLimit = 8;
        return field;
    }

    static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }
}
#endif
