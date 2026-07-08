using UnityEngine;
using UnityEngine.InputSystem;   // this project uses the New Input System (activeInputHandler = 1)

// ─────────────────────────────────────────────────────────────────────────────
//  SIMPLE 3rd-PERSON CHARACTER  — single-player for now, built to go online later.
//
//  ➜ MULTIPLAYER (later — see MULTIPLAYER.md, section 2): to make this networked
//    you only need these small, already-marked changes:
//      1. add:    using Unity.Netcode;
//      2. change  ": MonoBehaviour"  →  ": NetworkBehaviour"
//      3. first line of Update():   if (!IsOwner) return;   (drive only YOUR player)
//      4. put shared state (health, score…) in NetworkVariable<T>
//      5. wrap world-changing actions in an [Rpc(SendTo.Server)] method
//    The input/movement split below is shaped so this stays a tiny change.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Run speed in units per second.")]
    public float moveSpeed = 6f;
    [Tooltip("How high a jump reaches, in units.")]
    public float jumpHeight = 1.4f;
    [Tooltip("Gravity strength (keep negative).")]
    public float gravity = -20f;
    [Tooltip("How quickly the character turns to face movement (smaller = snappier).")]
    public float turnSmoothTime = 0.08f;

    CharacterController _controller;
    Transform _camera;
    Vector3 _verticalVelocity;   // gravity + jump only
    float _turnVelocity;

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        if (Camera.main != null) _camera = Camera.main.transform;
    }

    void Update()
    {
        // ➜ MULTIPLAYER: this line becomes  if (!IsOwner) return;
        //    (so you only ever control YOUR OWN character, not everyone else's)

        // 1) READ INPUT — "what does the player want to do?"  (intent)
        Vector2 moveInput = ReadMoveInput();
        bool jumpPressed  = ReadJumpPressed();

        // 2) APPLY IT — "move the body"  (simulation)
        ApplyMovement(moveInput, jumpPressed);
    }

    // ---- input (kept separate on purpose) ----
    Vector2 ReadMoveInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return Vector2.zero;
        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float y = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
    }

    bool ReadJumpPressed()
    {
        var kb = Keyboard.current;
        return kb != null && kb.spaceKey.wasPressedThisFrame;
    }

    // ---- movement ----
    void ApplyMovement(Vector2 input, bool jumpPressed)
    {
        // Move relative to where the camera faces (feels natural in 3rd person).
        Vector3 forward = _camera ? Flatten(_camera.forward) : Vector3.forward;
        Vector3 right   = _camera ? Flatten(_camera.right)   : Vector3.right;
        Vector3 move    = forward * input.y + right * input.x;

        // Turn to face the direction we're moving.
        if (move.sqrMagnitude > 0.0001f)
        {
            float target = Mathf.Atan2(move.x, move.z) * Mathf.Rad2Deg;
            float angle  = Mathf.SmoothDampAngle(transform.eulerAngles.y, target, ref _turnVelocity, turnSmoothTime);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);
        }

        // Horizontal movement.
        _controller.Move(move * moveSpeed * Time.deltaTime);

        // Gravity + jump (vertical).
        if (_controller.isGrounded)
        {
            if (_verticalVelocity.y < 0f) _verticalVelocity.y = -2f; // small downward force to stay grounded
            if (jumpPressed) _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        _verticalVelocity.y += gravity * Time.deltaTime;
        _controller.Move(_verticalVelocity * Time.deltaTime);
    }

    static Vector3 Flatten(Vector3 v) { v.y = 0f; return v.normalized; }
}
