using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Handles the player's melee attacks and resource gathering.
///
/// NETWORKING FLOW:
/// ─ Input and aim detection happen CLIENT-SIDE (IsOwner only).
/// ─ When the player attacks, the client plays the animation and tells the server
///   the attack started.
/// ─ At the impact frame, the client raycasts for the resource it was actually
///   aiming at and sends that target to the server.
/// ─ The server validates aim, range, angle, line-of-sight, tool, and inventory,
///   then applies damage through ResourceNode.ServerApplyHit().
///
/// ATTACKS:
/// ─ Bare hands trigger the "Punch" animation.
/// ─ Equipping an item whose ItemDefinition.toolTag is "Axe" triggers "AxeSwing".
/// ─ Attacks are only allowed while grounded and moving at walk speed or slower.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerGathering : NetworkBehaviour
{
    // ───────────────────────── Configuration ─────────────────────────

    [Header("Aim & Hit")]
    [Tooltip("Maximum distance (meters) the hit can reach from the camera center.")]
    [FormerlySerializedAs("interactRange")]
    [SerializeField] private float maxHitDistance = 3f;

    [Tooltip("Radius of the sphere cast used for forgiving target selection. " +
             "Keep small so the player has to actually aim at the node.")]
    [SerializeField] private float hitRadius = 0.25f;

    [Tooltip("Layers the aim spherecast can hit. Must include resource-node layers. " +
             "Exclude the Player layer so the cast doesn't hit the local player.")]
    [SerializeField] private LayerMask interactLayerMask = ~0;

    [Header("Attack Timing")]
    [Tooltip("Seconds into the swing/punch before the resource hit is resolved. " +
             "Tune this to the impact frame of your animation.")]
    [SerializeField] private float gatherImpactDelay = 0.35f;

    [Tooltip("How long after attack start the impact RPC is still accepted by the server.")]
    [SerializeField] private float attackWindow = 1.0f;

    [Tooltip("Minimum seconds between attacks.")]
    [SerializeField] private float attackCooldown = 0.5f;

    [Header("Visual Debug")]
    [Tooltip("If true, draws the aim ray in the Scene view.")]
    [SerializeField] private bool debugDrawRay = true;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// Replicated tool tag used for UI/debug. The real authority is PlayerEquipment.
    /// </summary>
    public NetworkVariable<FixedString32Bytes> EquippedToolTag =
        new NetworkVariable<FixedString32Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // ───────────────────────── Private State ─────────────────────────

    private InputSystem_Actions _inputActions;
    private Camera _playerCamera;
    private Animator _animator;
    private NetworkAnimator _networkAnimator;
    private PlayerEquipment _equipment;
    private Coroutine _attackRoutine;

    /// <summary>
    /// True while the punch or axe-swing animation is playing.
    /// Read by PlayerController to freeze rotation and cap speed during the swing.
    /// </summary>
    public bool IsAttacking { get; private set; }

    // Server-side attack bookkeeping
    private float _lastAttackStartTime = -1000f;
    private float _nextAttackAllowedTime = -1000f;

    // ───────────────────────── Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner) return;

        int playerLayer = gameObject.layer;
        interactLayerMask = ~(1 << playerLayer);
        Debug.Log($"[PlayerGathering] Interact LayerMask set to: {interactLayerMask.value} " +
                  $"(excluding layer {playerLayer} '{LayerMask.LayerToName(playerLayer)}')");

        _inputActions = new InputSystem_Actions();
        _inputActions.Player.Enable();

        _animator = GetComponentInChildren<Animator>();
        _networkAnimator = GetComponent<NetworkAnimator>();
        _equipment = GetComponent<PlayerEquipment>();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsOwner && _inputActions != null)
        {
            _inputActions.Player.Disable();
            _inputActions.Dispose();
            _inputActions = null;
        }

        if (_attackRoutine != null)
        {
            StopCoroutine(_attackRoutine);
            _attackRoutine = null;
        }
        IsAttacking = false;
    }

    // ───────────────────────── Update (Client-Side Only) ─────────────────────────

    private void Update()
    {
        if (!IsOwner || _inputActions == null) return;

        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null) return;
        }

        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (_inputActions.Player.Attack.WasPressedThisFrame())
        {
            if (CanAttack())
            {
                StartAttack();
            }
        }
    }

    /// <summary>
    /// Attacks are only allowed while grounded and at walk speed or slower.
    /// </summary>
    private bool CanAttack()
    {
        if (IsAttacking) return false;
        if (_animator == null) return false;

        bool isGrounded = _animator.GetBool("isGrounded");
        float speed = _animator.GetFloat("Speed");

        return isGrounded && speed < 5.5f;
    }

    /// <summary>
    /// Returns true if the player is holding an item tagged as an Axe.
    /// </summary>
    private bool ShouldUseAxeSwing()
    {
        if (_equipment != null && !string.IsNullOrEmpty(_equipment.EquippedItemId))
        {
            ItemDefinition def = ItemDatabase.GetItem(_equipment.EquippedItemId);
            return def != null && def.toolTag == "Axe";
        }

        return false;
    }

    /// <summary>
    /// Begins an attack: plays the animation, notifies the server, and schedules the hit.
    /// </summary>
    private void StartAttack()
    {
        string trigger = ShouldUseAxeSwing() ? "AxeSwing" : "Punch";

        if (!HasTriggerParameter(trigger))
        {
            Debug.LogWarning($"[PlayerGathering] Animator is missing the '{trigger}' trigger. " +
                             "Run Tools > OakHaven > Add Axe Swing to Player Animator.");
            return;
        }

        if (_networkAnimator != null)
            _networkAnimator.SetTrigger(trigger);
        else if (_animator != null)
            _animator.SetTrigger(trigger);

        StartAttackServerRpc();

        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = StartCoroutine(AttackDuration());

        StartCoroutine(DelayedHit());
    }

    private bool HasTriggerParameter(string name)
    {
        if (_animator == null) return false;
        foreach (AnimatorControllerParameter p in _animator.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == name)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Tracks the attack animation and clears <see cref="IsAttacking"/> when it finishes.
    /// </summary>
    private IEnumerator AttackDuration()
    {
        IsAttacking = true;
        yield return null;

        float entryTimeout = 0.5f;
        while (_animator != null && entryTimeout > 0f)
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(1);
            if (stateInfo.IsName("Punch") || stateInfo.IsName("AxeSwing"))
                break;

            entryTimeout -= Time.deltaTime;
            yield return null;
        }

        const float timeout = 3f;
        float elapsed = 0f;

        while (_animator != null && elapsed < timeout)
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(1);

            bool inAttackState = stateInfo.IsName("Punch") || stateInfo.IsName("AxeSwing");
            if (inAttackState)
            {
                if (stateInfo.normalizedTime >= 1f)
                    break;
            }
            else
            {
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        IsAttacking = false;
    }

    /// <summary>
    /// Waits for the swing to reach its impact frame, then raycasts for the
    /// actually-aimed-at resource and sends it to the server.
    /// </summary>
    private IEnumerator DelayedHit()
    {
        yield return new WaitForSeconds(gatherImpactDelay);

        if (_playerCamera == null) yield break;

        // Aim from the camera crosshair. In third-person the camera is behind the
        // player, so a cast from the chest often misses small objects the crosshair
        // is actually on (like bushes).
        Vector3 origin = _playerCamera.transform.position;
        Vector3 direction = _playerCamera.transform.forward;

        if (debugDrawRay)
        {
            Debug.DrawRay(origin, direction * maxHitDistance * 2f, Color.yellow, 0.5f);
        }

        if (!Physics.SphereCast(origin, hitRadius, direction, out RaycastHit hit,
                                maxHitDistance * 2f, interactLayerMask, QueryTriggerInteraction.Collide))
        {
            if (debugDrawRay)
                Debug.Log("[PlayerGathering] Crosshair SphereCast hit nothing.");
            yield break;
        }

        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node == null)
        {
            if (debugDrawRay)
                Debug.Log($"[PlayerGathering] Crosshair hit '{hit.collider.name}' but it is not a ResourceNode.");
            yield break;
        }

        if (node.CurrentHealth.Value <= 0)
        {
            if (debugDrawRay)
                Debug.Log($"[PlayerGathering] Target '{node.ResourceType}' is already depleted.");
            yield break;
        }

        // Reject targets that are clearly out of melee range before bothering the server.
        float distanceToNode = Vector3.Distance(transform.position, node.transform.position);
        if (distanceToNode > maxHitDistance + 0.5f)
        {
            if (debugDrawRay)
                Debug.Log($"[PlayerGathering] Target '{node.ResourceType}' is too far from player ({distanceToNode:F2}m).");
            yield break;
        }

        if (debugDrawRay)
        {
            Debug.DrawLine(origin, hit.point, Color.green, 0.5f);
            Debug.Log($"[PlayerGathering] Aimed at '{node.ResourceType}' at {hit.distance:F2}m.");
        }

        AttackTargetServerRpc(node.NetworkObjectId, direction);
    }

    // ───────────────────────── ServerRpcs (Server Validates Everything) ─────────────────────────

    /// <summary>
    /// Called by the owning client when an attack starts. The server records the
    /// attack window so impact RPCs outside that window are rejected.
    /// </summary>
    [ServerRpc]
    private void StartAttackServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerGathering] Client {rpcParams.Receive.SenderClientId} tried to start an attack " +
                             $"on client {OwnerClientId}'s player — rejected.");
            return;
        }

        if (Time.time < _nextAttackAllowedTime)
        {
            Debug.Log($"[PlayerGathering] Client {OwnerClientId} attack rejected — cooldown.");
            return;
        }

        _lastAttackStartTime = Time.time;
        _nextAttackAllowedTime = Time.time + attackCooldown;
    }

    /// <summary>
    /// Called by the owning client at the attack impact frame. The server validates
    /// the claimed target and, if everything checks out, tells that node to apply the hit.
    /// </summary>
    [ServerRpc]
    private void AttackTargetServerRpc(ulong targetNetworkObjectId, Vector3 cameraForward,
                                       ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerGathering] Client {rpcParams.Receive.SenderClientId} tried to hit a target " +
                             $"on client {OwnerClientId}'s player — rejected.");
            return;
        }

        if (Time.time - _lastAttackStartTime > attackWindow)
        {
            Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} impact rejected — outside attack window.");
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
        {
            Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} targeted an unknown NetworkObject.");
            return;
        }

        ResourceNode node = netObj.GetComponent<ResourceNode>();
        if (node == null)
        {
            Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} target is not a ResourceNode.");
            return;
        }

        if (node.CurrentHealth.Value <= 0)
        {
            Debug.Log($"[PlayerGathering] Client {OwnerClientId} target is already depleted.");
            return;
        }

        // Range and angle validation
        Vector3 playerPos = transform.position + Vector3.up * 1f;
        Collider nodeCol = node.GetComponent<Collider>() ?? node.GetComponentInChildren<Collider>();
        if (nodeCol == null)
        {
            Debug.LogWarning($"[PlayerGathering] Target ResourceNode has no collider.");
            return;
        }

        Vector3 closestPoint = nodeCol.ClosestPoint(playerPos);
        float distance = Vector3.Distance(playerPos, closestPoint);
        if (distance > maxHitDistance + 0.5f)
        {
            Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} target is too far ({distance:F2}m).");
            return;
        }

        Vector3 toTarget = (closestPoint - playerPos).normalized;
        cameraForward.Normalize();
        if (Vector3.Dot(cameraForward, toTarget) < 0.5f)
        {
            Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} target is outside aim cone.");
            return;
        }

        // Line-of-sight validation (ignore the player's own collider)
        int mask = ~(1 << gameObject.layer);
        if (Physics.Raycast(playerPos, toTarget, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Collide))
        {
            if (hit.collider.GetComponentInParent<ResourceNode>() != node)
            {
                Debug.LogWarning($"[PlayerGathering] Client {OwnerClientId} target is blocked by '{hit.collider.name}'.");
                return;
            }
        }

        // Derive the real equipped tool tag from the authoritative equipment state
        string toolTag = "";
        PlayerEquipment equipment = GetComponent<PlayerEquipment>();
        if (equipment != null && !string.IsNullOrEmpty(equipment.EquippedItemId))
        {
            ItemDefinition def = ItemDatabase.GetItem(equipment.EquippedItemId);
            if (def != null)
                toolTag = def.toolTag;
        }

        if (string.IsNullOrEmpty(toolTag) && !string.IsNullOrEmpty(node.RequiredToolTag))
        {
            Debug.Log($"[PlayerGathering] Client {OwnerClientId} has no tool but node requires '{node.RequiredToolTag}'.");
            return;
        }

        node.ServerApplyHit(OwnerClientId, toolTag);
    }
}
