using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  FIRST-PERSON CHARACTER CONTROLLER
//
//  Архитектура взята из FirstPersonController:
//    - движение относительно transform самого персонажа (не камеры)
//    - раздельное вращение: тело по Y, камера по X
//    - весь ввод приходит из внешнего PlayerInputHandler (не читаем Keyboard/Mouse тут)
//    - блокировка курсора, спринт
//
//  Физика прыжка/гравитации взята из PlayerController:
//    - jumpHeight задаётся в метрах (не "магическая" сила), высота считается по формуле
//    - своя переменная gravity, не зависящая от Physics.gravity проекта
//
//  ➜ MULTIPLAYER (позже, Unity Netcode): чтобы сделать это сетевым —
//      1. using Unity.Netcode;
//      2. ": MonoBehaviour"  →  ": NetworkBehaviour"
//      3. первая строка Update():  if (!IsOwner) return;
//      4. общее состояние (здоровье, очки и т.п.) — в NetworkVariable<T>
//      5. действия, влияющие на мир — в [Rpc(SendTo.Server)] методах
//    Разделение "чтение ввода → применение движения" уже готово под это.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Speeds")]
    [Tooltip("Скорость ходьбы, юниты в секунду.")]
    [SerializeField] private float walkSpeed = 3.0f;
    [Tooltip("Множитель скорости при спринте.")]
    [SerializeField] private float sprintMultiplier = 2.0f;

    [Header("Jump & Gravity")]
    [Tooltip("Высота прыжка в метрах.")]
    [SerializeField] private float jumpHeight = 1.2f;
    [Tooltip("Сила гравитации (держать отрицательной). Не зависит от Physics.gravity проекта.")]
    [SerializeField] private float gravity = -20f;

    [Header("Look Parameters")]
    [SerializeField] private float mouseSensitivity = 0.1f;
    [SerializeField] private float upDownLookRange = 80f;

    [Header("References")]
    [SerializeField] private CharacterController characterController;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private PlayerInputHandler playerInputHandler;

    Vector3 _verticalVelocity;   // только Y: гравитация + прыжок
    float _verticalRotation;     // накопленный угол наклона камеры (X)

    float CurrentSpeed => walkSpeed;

    void Awake()
    {
        // Подстраховка: если ссылки не проставлены в инспекторе, попробуем найти их сами.
        if (characterController == null) characterController = GetComponent<CharacterController>();
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        // ➜ MULTIPLAYER: тут появится  if (!IsOwner) return;

        HandleMovement();
        HandleRotation();
    }

    // ---- движение ----

    void HandleMovement()
    {
        Vector3 worldDirection = CalculateWorldDirection();

        Vector3 horizontal = worldDirection * CurrentSpeed;
        ApplyVerticalMotion();

        Vector3 move = new Vector3(horizontal.x, _verticalVelocity.y, horizontal.z);
        characterController.Move(move * Time.deltaTime);
    }

    Vector3 CalculateWorldDirection()
    {
        Vector3 inputDirection = new Vector3(playerInputHandler.MovementInput.x, 0f, playerInputHandler.MovementInput.y);
        Vector3 worldDirection = transform.TransformDirection(inputDirection);
        return worldDirection.normalized;
    }

    void ApplyVerticalMotion()
    {
        if (characterController.isGrounded)
        {
            // Небольшая постоянная сила вниз, чтобы isGrounded определялся стабильно.
            if (_verticalVelocity.y < 0f) _verticalVelocity.y = -2f;

            if (playerInputHandler.JumpTriggered)
            {
                // v = sqrt(h * -2 * g)  — даёт ровно нужную высоту прыжка jumpHeight.
                _verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
        }
        else
        {
            _verticalVelocity.y += gravity * Time.deltaTime;
        }
    }

    // ---- вращение ----

    void HandleRotation()
    {
        float mouseXRotation = playerInputHandler.RotationInput.x * mouseSensitivity;
        float mouseYRotation = playerInputHandler.RotationInput.y * mouseSensitivity;

        ApplyHorizontalRotation(mouseXRotation);
        ApplyVerticalRotation(mouseYRotation);
    }

    void ApplyHorizontalRotation(float rotationAmount)
    {
        transform.Rotate(0f, rotationAmount, 0f);
    }

    void ApplyVerticalRotation(float rotationAmount)
    {
        _verticalRotation = Mathf.Clamp(_verticalRotation - rotationAmount, -upDownLookRange, upDownLookRange);
        mainCamera.transform.localRotation = Quaternion.Euler(_verticalRotation, 0f, 0f);
    }
}