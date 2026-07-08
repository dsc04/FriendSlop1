using UnityEngine;

// Simple smooth follow-camera. In single-player it follows the object tagged "Player".
//
// ➜ MULTIPLAYER (later): make it follow only YOUR player — e.g. the player prefab
//   assigns `target = transform` in OnNetworkSpawn() when IsOwner is true. That way
//   each person's camera follows their own character.
public class CameraFollow : MonoBehaviour
{
    [Tooltip("Who to follow. If empty, it grabs the object tagged 'Player' at startup.")]
    public Transform target;

    [Tooltip("Camera position relative to the target.")]
    public Vector3 offset = new Vector3(0f, 6f, -8f);

    [Tooltip("Higher = the camera catches up faster.")]
    public float followSharpness = 10f;

    [Tooltip("Look slightly above the target's feet.")]
    public float lookAtHeight = 1f;

    void Start()
    {
        if (target == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) target = p.transform;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        float t = 1f - Mathf.Exp(-followSharpness * Time.deltaTime); // frame-rate independent smoothing
        transform.position = Vector3.Lerp(transform.position, desired, t);
        transform.LookAt(target.position + Vector3.up * lookAtHeight);
    }
}
