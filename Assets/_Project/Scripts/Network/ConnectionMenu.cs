using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// The on-screen HOST / JOIN menu — the whole "play with friends" flow lives here.
//
// How it works (all free, part of Unity Gaming Services):
//   HOST  → signs in anonymously → creates a session → Unity Relay gives us a
//           short room code → the game starts hosting.
//   JOIN  → friend types the code → connects through Relay to the host.
// Nobody needs a white IP, port forwarding, or an account — Relay tunnels the
// traffic and the Sessions API starts the NetworkManager for us automatically.
//
// Drawn with Unity's simple immediate-mode GUI so it needs zero scene setup.
// It's a developer menu — replace with a real lobby UI later.
public class ConnectionMenu : MonoBehaviour
{
    [Tooltip("Room size including the host. Design target is 2–6.")]
    public int maxPlayers = 6;

    ISession _session;          // the room we're currently in (null = main menu)
    string _joinCodeInput = "";
    string _status = "";
    bool _busy;

    void Awake()
    {
        // Without this, an unfocused window stops running — deadly when you're
        // alt-tabbing between "host" and "join" windows while testing.
        Application.runInBackground = true;
    }

    void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
    }

    void Update()
    {
        // The first-person controller locks the mouse cursor while you play.
        // Esc frees it so this menu stays clickable (Leave room, Copy code…).
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
    }

    // ---------- the actual networking ----------

    async Task EnsureSignedInAsync()
    {
#if UNITY_EDITOR
        // Each editor window (main editor + Multiplayer Play Mode virtual players)
        // must sign in as a DIFFERENT anonymous player, or Unity thinks one player
        // joined the room twice. The project path differs per window, so we derive
        // a stable per-window profile name from it.
        uint h = 2166136261u;
        foreach (char c in Application.dataPath) h = (h ^ c) * 16777619u;
        var options = new InitializationOptions();
        options.SetProfile("editor" + h);
        await UnityServices.InitializeAsync(options);
#else
        await UnityServices.InitializeAsync();
#endif
        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    async void Host()
    {
        _busy = true;
        _status = "Creating room…";
        try
        {
            await EnsureSignedInAsync();
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = true    // not listed publicly — joinable only by code
            }.WithRelayNetwork();
            _session = await MultiplayerService.Instance.CreateSessionAsync(options);
            _status = "";
        }
        catch (Exception e)
        {
            _session = null;
            _status = Friendly(e);
            Debug.LogException(e);
        }
        _busy = false;
    }

    async void Join()
    {
        _busy = true;
        _status = "Joining…";
        try
        {
            await EnsureSignedInAsync();
            string code = _joinCodeInput.Trim().ToUpperInvariant();
            _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
            _status = "";
        }
        catch (Exception e)
        {
            _session = null;
            _status = Friendly(e);
            Debug.LogException(e);
        }
        _busy = false;
    }

    async void Leave()
    {
        _busy = true;
        try { if (_session != null) await _session.LeaveAsync(); }
        catch (Exception e) { Debug.LogException(e); }
        _session = null;
        _status = "";
        _busy = false;
    }

    void OnClientDisconnect(ulong clientId)
    {
        // Fires on a client when WE lose the connection (usually: the host left).
        if (_session != null && NetworkManager.Singleton != null
            && !NetworkManager.Singleton.IsHost
            && clientId == NetworkManager.Singleton.LocalClientId)
        {
            _session = null;
            _status = "Disconnected — the host left or the connection dropped.";
        }
    }

    // Turn raw errors into something a human can act on (full details go to Console).
    static string Friendly(Exception e)
    {
        string m = e.Message ?? "";
        if (m.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Room not found — double-check the code.";
        if (m.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0
            || e is ServicesInitializationException)
            return "Project isn't linked to Unity Cloud yet:\nEdit ▸ Project Settings ▸ Services.";
        return "Error: " + m;
    }

    // ---------- the on-screen menu ----------

    void OnGUI()
    {
        // Keep the menu readable at any window size.
        float scale = Mathf.Max(1f, Screen.height / 540f);
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        GUILayout.BeginArea(new Rect(12, 12, 300, 400), GUI.skin.box);
        GUILayout.Label("FriendSlop — play together");

        if (_session == null)
        {
            GUI.enabled = !_busy;
            if (GUILayout.Button("HOST a room", GUILayout.Height(34))) Host();

            GUILayout.Space(8);
            GUILayout.Label("Have a code from a friend?");
            GUILayout.BeginHorizontal();
            _joinCodeInput = GUILayout.TextField(_joinCodeInput, 8, GUILayout.Height(30));
            bool canJoin = _joinCodeInput.Trim().Length >= 4;
            if (GUILayout.Button("JOIN", GUILayout.Width(70), GUILayout.Height(30)) && canJoin)
                Join();
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }
        else
        {
            GUILayout.Label(_session.IsHost ? "You are the HOST" : "Connected!");
            GUILayout.Space(4);
            GUILayout.Label("Room code — send it to your friends:");
            GUILayout.TextField(_session.Code ?? "…", 8, GUILayout.Height(30));
            if (GUILayout.Button("Copy code")) GUIUtility.systemCopyBuffer = _session.Code;
            GUILayout.Space(4);
            GUILayout.Label($"Players: {(_session.Players != null ? _session.Players.Count : 0)} / {_session.MaxPlayers}");
            GUILayout.Space(8);
            GUI.enabled = !_busy;
            if (GUILayout.Button("Leave room")) Leave();
            GUI.enabled = true;
        }

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Space(6);
            GUILayout.Label(_status);
        }

        GUILayout.EndArea();
    }
}
