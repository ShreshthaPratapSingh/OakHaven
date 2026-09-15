using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Networked third-person player controller (client-authoritative).
/// Uses CharacterController for movement and the generated InputSystem_Actions class for input.
/// Only processes input and movement when IsOwner is true.
/// NetworkTransform (Owner authority) syncs position/rotation to other clients.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(NetworkObject))]
public class PlayerController : NetworkBehaviour
{
    // ───────────────────────── Movement Settings ─────────────────────────

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 4f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float jumpHeight = 1.2f;
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float turnSmoothTime = 0.1f;

    [Header("Camera")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float cameraPitchMin = -30f;
    [SerializeField] private float cameraPitchMax = 60f;
    [SerializeField] private float cameraDistance = 5f;
    [SerializeField] private float cameraCollisionRadius = 0.2f;
    [SerializeField] private LayerMask cameraCollisionMask = ~0; // Collide with everything by default

    // ───────────────────────── References ─────────────────────────

    [Header("References (wired in prefab)")]
    [Tooltip("Empty child at head height — camera orbits around this.")]
    [SerializeField] private Transform cameraPivot;

    [Tooltip("Child containing the player's visual mesh. Rotated toward movement direction.")]
    [SerializeField] private Transform playerModel;

    // ───────────────────────── Private State ─────────────────────────

    private CharacterController _cc;
    private InputSystem_Actions _inputActions;

    private Vector3 _velocity;           // Accumulated vertical velocity (gravity + jump)
    private float _cameraPitch;          // Current camera pitch angle
    private float _cameraYaw;            // Current camera yaw angle
    private float _turnSmoothVelocity;   // SmoothDampAngle ref

    // ───────────────────────── Network Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _cc = GetComponent<CharacterController>();

        if (!IsOwner)
        {
            // Non-owned instances: disable CharacterController so NetworkTransform
            // can move the transform freely without CC interference.
            _cc.enabled = false;
            enabled = false; // Stop Update() from running
            return;
        }

        // Owner setup: enable input
        _inputActions = new InputSystem_Actions();
        _inputActions.Player.Enable();

        // Lock cursor for third-person camera control
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Initialize camera yaw to match the player's current facing
        _cameraYaw = transform.eulerAngles.y;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsOwner && _inputActions != null)
        {
            _inputActions.Player.Disable();
            _inputActions.Dispose();
            _inputActions = null;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ───────────────────────── Update Loop ─────────────────────────

    private void Update()
    {
        // This method only runs on the owning client (non-owners have enabled = false).
        HandleCameraLook();
        HandleMovement();
    }

    private void LateUpdate()
    {
        // Position the camera after all movement has been applied.
        UpdateCameraPosition();
    }

    // ───────────────────────── Camera Look ─────────────────────────

    private void HandleCameraLook()
    {
        Vector2 lookInput = _inputActions.Player.Look.ReadValue<Vector2>();

        _cameraYaw += lookInput.x * mouseSensitivity;
        _cameraPitch -= lookInput.y * mouseSensitivity;
        _cameraPitch = Mathf.Clamp(_cameraPitch, cameraPitchMin, cameraPitchMax);
    }

    private void UpdateCameraPosition()
    {
        if (cameraPivot == null) return;

        // Apply orbit rotation to the pivot
        cameraPivot.rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0f);

        // The camera itself (child of the pivot) is positioned at local (0, 0, -cameraDistance).
        // Apply spring-arm collision to avoid clipping through geometry.
        Transform cameraTransform = cameraPivot.GetChild(0); // PlayerCamera
        if (cameraTransform == null) return;

        Vector3 desiredLocalPos = new Vector3(0f, 0f, -cameraDistance);
        Vector3 desiredWorldPos = cameraPivot.TransformPoint(desiredLocalPos);
        Vector3 dirFromPivot = (desiredWorldPos - cameraPivot.position).normalized;
        float maxDist = cameraDistance;

        // Raycast from pivot toward desired camera position to detect walls
        if (Physics.SphereCast(cameraPivot.position, cameraCollisionRadius, dirFromPivot,
            out RaycastHit hit, maxDist, cameraCollisionMask))
        {
            // Pull the camera forward to avoid clipping
            float clampedDist = Mathf.Max(hit.distance - cameraCollisionRadius, 0.2f);
            cameraTransform.localPosition = new Vector3(0f, 0f, -clampedDist);
        }
        else
        {
            cameraTransform.localPosition = desiredLocalPos;
        }
    }

    // ───────────────────────── Movement ─────────────────────────

    private void HandleMovement()
    {
        // Ground check
        bool isGrounded = _cc.isGrounded;
        if (isGrounded && _velocity.y < 0f)
        {
            _velocity.y = -2f; // Small downward force to keep grounded
        }

        // Read input
        Vector2 moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
        bool isSprinting = _inputActions.Player.Sprint.IsPressed();

        // Calculate movement direction relative to the camera's yaw
        Vector3 forward = Quaternion.Euler(0f, _cameraYaw, 0f) * Vector3.forward;
        Vector3 right = Quaternion.Euler(0f, _cameraYaw, 0f) * Vector3.right;
        Vector3 moveDir = (forward * moveInput.y + right * moveInput.x).normalized;

        // Apply movement
        float speed = isSprinting ? sprintSpeed : walkSpeed;
        _cc.Move(moveDir * speed * Time.deltaTime);

        // Rotate model toward movement direction (third-person style)
        if (moveDir.sqrMagnitude > 0.01f && playerModel != null)
        {
            float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            float smoothedAngle = Mathf.SmoothDampAngle(
                playerModel.eulerAngles.y, targetAngle, ref _turnSmoothVelocity, turnSmoothTime);
            playerModel.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
        }

        // Jump
        if (_inputActions.Player.Jump.WasPressedThisFrame() && isGrounded)
        {
            // v = sqrt(2 * |gravity| * jumpHeight)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        // Gravity
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);
    }
}
