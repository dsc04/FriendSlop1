using Unity.Netcode;
using UnityEngine;

// Lives on the Player prefab root, next to PlayerController.
//
// When a player spawns in a networked game, EVERY connected computer gets a copy
// of that player. This script makes each copy behave correctly:
//   - your OWN player  → controls + input + first-person camera ON, and the
//                        scene's MenuCamera switches off
//   - everyone else's  → stays "display only": position/rotation arrive over the
//                        network via ClientNetworkTransform, nothing simulates here
//
// On the prefab, PlayerInputHandler / Camera / AudioListener are saved DISABLED
// and are only switched ON for the owner. We never disable a remote copy's
// PlayerInputHandler at runtime: all copies share one InputActionAsset, so its
// OnDisable would switch off input for the whole game — including yours.
//
// ➜ FIRST-PERSON CHARACTER (note for its author): PlayerController and
//   PlayerInputHandler are NOT modified by multiplayer — this script only
//   enables/disables them. If you rename classes or add control scripts,
//   update the lookups below.
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Tooltip("How far from the center players appear when they join.")]
    public float spawnRingRadius = 2.5f;

    GameObject _menuCamera;   // scene camera shown before you host/join

    public override void OnNetworkSpawn()
    {
        // Simulation runs only on YOUR player.
        var controller = GetComponent<PlayerController>();
        if (controller != null) controller.enabled = IsOwner;

        if (!IsOwner) return;   // remote copies keep their prefab-default (off) state

        // Wake up the pieces that are disabled on the prefab (see note above).
        var input = GetComponent<PlayerInputHandler>();
        if (input != null) input.enabled = true;
        var cam = GetComponentInChildren<Camera>(true);
        if (cam != null) cam.enabled = true;
        var ears = GetComponentInChildren<AudioListener>(true);
        if (ears != null) ears.enabled = true;

        // Spread players around a small circle so they don't spawn inside each other.
        // (CharacterController blocks direct teleports, so switch it off for a moment.)
        float angle = OwnerClientId * (Mathf.PI * 2f / 6f);
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = new Vector3(
            Mathf.Sin(angle) * spawnRingRadius, 1f, Mathf.Cos(angle) * spawnRingRadius);
        if (cc != null) cc.enabled = true;

        // First-person camera took over — the menu camera can rest.
        _menuCamera = GameObject.Find("MenuCamera");
        if (_menuCamera != null) _menuCamera.SetActive(false);
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;

        // Back to the menu: scene camera on, cursor usable again.
        if (_menuCamera != null) _menuCamera.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
