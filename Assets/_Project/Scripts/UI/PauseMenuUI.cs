using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The game menu.
//
//   Not in a room yet  → the menu stays open as the MAIN MENU: Host / Join by code.
//   In a room          → Esc opens/closes it: room code, players, Resume, Leave.
//
// Opening the menu frees the mouse cursor and freezes YOUR character's controls;
// closing does the reverse. Joining a room closes it automatically, losing the
// room (host left / you left) brings it back.
//
// All references below are wired by Tools ▸ FriendSlop ▸ Set Up Multiplayer Scene,
// which also builds the Canvas — no hand-assembly needed.
public class PauseMenuUI : MonoBehaviour
{
    [Header("Wired by the setup tool")]
    public MultiplayerSession session;
    public GameObject menuRoot;           // the full-screen dim + window
    public GameObject notConnectedGroup;  // Host / Join controls
    public GameObject connectedGroup;     // code, players, Resume, Leave
    public Button hostButton;
    public TMP_InputField joinCodeInput;
    public Button joinButton;
    public TextMeshProUGUI roomCodeLabel;
    public Button copyCodeButton;
    public TextMeshProUGUI playersLabel;
    public Button resumeButton;
    public Button leaveButton;
    public Button quitButton;
    public TextMeshProUGUI statusLabel;

    bool _open = true;
    bool _wasInSession;

    void Start()
    {
        hostButton.onClick.AddListener(() => session.Host());
        joinButton.onClick.AddListener(() => session.Join(joinCodeInput.text));
        copyCodeButton.onClick.AddListener(() => GUIUtility.systemCopyBuffer = session.RoomCode);
        resumeButton.onClick.AddListener(() => SetOpen(false));
        leaveButton.onClick.AddListener(() => session.Leave());
        quitButton.onClick.AddListener(Application.Quit);

        // Room codes are uppercase — fix typing as it happens.
        joinCodeInput.onValidateInput = (text, index, ch) => char.ToUpperInvariant(ch);

        SetOpen(true);
    }

    void Update()
    {
        // Entering a room → menu slides away; dropping out of one → it comes back.
        if (session.InSession != _wasInSession)
        {
            _wasInSession = session.InSession;
            SetOpen(!session.InSession);
        }

        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && session.InSession)
            SetOpen(!_open);

        Refresh();
    }

    void SetOpen(bool open)
    {
        _open = open;
        menuRoot.SetActive(open);

        bool playing = session.InSession;
        Cursor.lockState = (open || !playing) ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = open || !playing;

        SetLocalPlayerControlsEnabled(!open);
    }

    // Freeze/unfreeze YOUR character while the menu is up. Only the movement/look
    // script — never PlayerInputHandler: its Input Action Asset is shared by every
    // player copy, and disabling it would switch input off game-wide
    // (see NetworkPlayerSetup).
    static void SetLocalPlayerControlsEnabled(bool enabled)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.LocalClient == null || nm.LocalClient.PlayerObject == null) return;
        var controller = nm.LocalClient.PlayerObject.GetComponent<PlayerController>();
        if (controller != null) controller.enabled = enabled;
    }

    void Refresh()
    {
        if (!_open) return;

        bool inSession = session.InSession;
        notConnectedGroup.SetActive(!inSession);
        connectedGroup.SetActive(inSession);

        hostButton.interactable = !session.Busy;
        joinButton.interactable = !session.Busy && joinCodeInput.text.Trim().Length >= 4;
        leaveButton.interactable = !session.Busy;

        if (inSession)
        {
            roomCodeLabel.text = "Room code:  " + session.RoomCode;
            playersLabel.text = $"Players:  {session.PlayerCount} / {session.MaxPlayers}";
        }

        statusLabel.text = session.Status;
        statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(session.Status));
    }
}
