using Unity.Netcode;
using UnityEngine;

// Lives on the Player prefab, next to PlayerController.
//
// When a player spawns in a networked game, EVERY connected computer gets a copy
// of that player. This script makes sure each copy behaves correctly:
//   - your OWN player  → controls on, camera follows it
//   - everyone else's  → controls OFF (their position arrives over the network
//                        via ClientNetworkTransform, we never simulate them here)
//
// ➜ FIRST-PERSON REWRITE (note for whoever owns the character): PlayerController
//   is NOT modified by multiplayer — this script simply enables/disables it.
//   If you rename the class or add extra control/camera scripts, update the two
//   marked spots below. Everything else can stay as is.
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Tooltip("How far from the center players appear when they join.")]
    public float spawnRingRadius = 2.5f;

    public override void OnNetworkSpawn()
    {
        // ➜ Control scripts live only on YOUR player. (Rename/add here if needed.)
        var controller = GetComponent<PlayerController>();
        if (controller != null) controller.enabled = IsOwner;

        if (!IsOwner) return;

        // Spread players around a small circle so they don't spawn inside each other.
        // (CharacterController blocks direct teleports, so switch it off for a moment.)
        float angle = OwnerClientId * (Mathf.PI * 2f / 6f);
        var cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.position = new Vector3(
            Mathf.Sin(angle) * spawnRingRadius, 1.1f, Mathf.Cos(angle) * spawnRingRadius);
        if (cc != null) cc.enabled = true;

        // ➜ Camera follows only YOUR player. (Swap this when the FP camera lands.)
        var cam = FindFirstObjectByType<CameraFollow>();
        if (cam != null) cam.target = transform;
    }
}
