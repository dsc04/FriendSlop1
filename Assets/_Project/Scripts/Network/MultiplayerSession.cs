using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

// The "play together" logic, with no UI attached — PauseMenuUI is the face of it.
//
// How it works (all free, part of Unity Gaming Services):
//   HOST → signs in anonymously → creates a session → Unity Relay gives us a
//          short room code → the game starts hosting.
//   JOIN → friend types the code → connects through Relay to the host.
// Nobody needs a white IP, port forwarding, or an account — Relay tunnels the
// traffic and the Sessions API starts/stops the NetworkManager automatically.
public class MultiplayerSession : MonoBehaviour
{
    [Tooltip("Room size including the host. Design target is 2–6.")]
    public int maxPlayers = 6;

    public ISession Session { get; private set; }   // the room we're in (null = not connected)
    public bool Busy { get; private set; }          // a connect/leave is in progress
    public string Status { get; private set; } = ""; // human-readable error/progress line

    public bool InSession => Session != null;
    public bool IsHost => Session != null && Session.IsHost;
    public string RoomCode => Session != null ? Session.Code : "";
    public int PlayerCount => Session != null && Session.Players != null ? Session.Players.Count : 0;
    public int MaxPlayers => Session != null ? Session.MaxPlayers : maxPlayers;

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

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
    }

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

    public async void Host()
    {
        if (Busy || InSession) return;
        Busy = true;
        Status = "Creating room…";
        try
        {
            await EnsureSignedInAsync();
            var options = new SessionOptions
            {
                MaxPlayers = maxPlayers,
                IsPrivate = true    // not listed publicly — joinable only by code
            }.WithRelayNetwork();
            Session = await MultiplayerService.Instance.CreateSessionAsync(options);
            Status = "";
        }
        catch (Exception e)
        {
            Session = null;
            Status = Friendly(e);
            Debug.LogException(e);
        }
        Busy = false;
    }

    public async void Join(string code)
    {
        if (Busy || InSession) return;
        Busy = true;
        Status = "Joining…";
        try
        {
            await EnsureSignedInAsync();
            Session = await MultiplayerService.Instance.JoinSessionByCodeAsync(
                code.Trim().ToUpperInvariant());
            Status = "";
        }
        catch (Exception e)
        {
            Session = null;
            Status = Friendly(e);
            Debug.LogException(e);
        }
        Busy = false;
    }

    public async void Leave()
    {
        if (Busy) return;
        Busy = true;
        try { if (Session != null) await Session.LeaveAsync(); }
        catch (Exception e) { Debug.LogException(e); }
        Session = null;
        Status = "";
        Busy = false;
    }

    void OnClientDisconnect(ulong clientId)
    {
        // Fires on a client when WE lose the connection (usually: the host left).
        if (Session != null && NetworkManager.Singleton != null
            && !NetworkManager.Singleton.IsHost
            && clientId == NetworkManager.Singleton.LocalClientId)
        {
            Session = null;
            Status = "Disconnected — the host left or the connection dropped.";
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
}
