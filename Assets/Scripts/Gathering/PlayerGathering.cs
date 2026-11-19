using System.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Handles the player's resource gathering interaction and melee attacks.
/// Raycasts/overlaps from the player to detect ResourceNode objects and calls HitServerRpc.
///
/// NETWORKING FLOW:
/// ─ Input reading and raycasting happen CLIENT-SIDE (IsOwner only).
/// ─ Client-side tool validation provides immediate feedback (no network round-trip).
/// ─ If validation passes, we call ResourceNode.HitServerRpc(), which sends
///   the RPC to the server for authoritative processing.
/// ─ The server re-validates everything before granting resources.
///
/// ATTACKS:
/// ─ Bare hands trigger the "Punch" animation.
/// ─ Equipping an item whose ItemDefinition.toolTag is "Axe" triggers "AxeSwing".
/// ─ Attacks are only allowed while grounded and moving at walk speed or slower.
/// ─ The resource hit is resolved after <see cref="gatherImpactDelay"/> seconds so it
///   lines up with the swing's impact frame, not the button press.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerGathering : NetworkBehaviour
{
    // ───────────────────────── Configuration ─────────────────────────

    [Header("Interaction Settings")]
    [Tooltip("Maximum distance (meters) from the player to detect resource nodes. " +
             "Measured from the player's chest height using OverlapSphere.")]
    [SerializeField] private float interactRange = 3f;

    [Tooltip("Which layers the interaction raycast can hit. " +
             "IMPORTANT: Exclude the Player layer so the third-person camera ray " +
             "doesn't hit the player's own collider before reaching resources.")]
    [SerializeField] private LayerMask interactLayerMask = ~0; // Everything by default

    [Header("Equipped Tool")]
    [Tooltip("For testing: set the initial tool tag the player spawns with. " +
             "Leave empty for 'bare hands'. Set to 'Axe' to test tree gathering.")]
    [SerializeField] private string startingToolTag = "Axe";

    [Header("Attack Timing")]
    [Tooltip("Seconds into the swing/punch before the resource hit is resolved. " +
             "Tune this to the impact frame of your animation.")]
    [SerializeField] private float gatherImpactDelay = 0.35f;

    [Header("Visual Debug")]
    [Tooltip("If true, draws a debug ray in the Scene view showing the interaction raycast.")]
    [SerializeField] private bool debugDrawRay = true;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The tag of the player's currently equipped tool.
    /// Replicated to all peers so the server can validate tool requirements.
    /// Only the server writes this (clients request changes via equipment changes).
    /// </summary>
    public NetworkVariable<Unity.Collections.FixedString32Bytes> EquippedToolTag =
        new NetworkVariable<Unity.Collections.FixedString32Bytes>(
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

    // ───────────────────────── Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Server sets the initial tool tag
        if (IsServer)
        {
            if (!string.IsNullOrEmpty(startingToolTag))
            {
                EquippedToolTag.Value = new Unity.Collections.FixedString32Bytes(startingToolTag);
                Debug.Log($"[PlayerGathering] Client {OwnerClientId} spawned with tool: '{startingToolTag}'");
            }
        }

        // Only the owning client handles input and raycasting
        if (!IsOwner) return;

        // FORCE-SET the layer mask to "Everything except our own layer".
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
        // Only the local owner processes gathering input
        if (!IsOwner || _inputActions == null) return;

        // Lazy-find the camera (it may not be active on the first frame)
        if (_playerCamera == null)
        {
            _playerCamera = Camera.main;
            if (_playerCamera == null) return; // Camera not ready yet
            Debug.Log($"[PlayerGathering] Camera found: '{_playerCamera.gameObject.name}' " +
                      $"on '{_playerCamera.transform.root.gameObject.name}' " +
                      $"(layer mask: {interactLayerMask.value})");
        }

        if (Cursor.lockState != CursorLockMode.Locked) return;

        // Left mouse = attack (punch or axe swing)
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
    /// This mirrors the Animator transition conditions on the UpperBody layer.
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
    /// Falls back to the legacy EquippedToolTag for test setups.
    /// </summary>
    private bool ShouldUseAxeSwing()
    {
        if (_equipment != null && !string.IsNullOrEmpty(_equipment.EquippedItemId))
        {
            ItemDefinition def = ItemDatabase.GetItem(_equipment.EquippedItemId);
            if (def != null && def.toolTag == "Axe")
                return true;

            return false;
        }

        return EquippedToolTag.Value.ToString() == "Axe";
    }

    /// <summary>
    /// Begins an attack: plays the correct animation and schedules the resource hit.
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

        if (_attackRoutine != null) StopCoroutine(_attackRoutine);
        _attackRoutine = StartCoroutine(AttackDuration());

        StartCoroutine(DelayedGather());
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

        // Wait one frame for the Animator to start the transition
        yield return null;

        // Give the Animator a short window to actually enter the attack state
        float entryTimeout = 0.5f;
        while (_animator != null && entryTimeout > 0f)
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(1);
            if (stateInfo.IsName("Punch") || stateInfo.IsName("AxeSwing"))
                break;

            entryTimeout -= Time.deltaTime;
            yield return null;
        }

        // Poll until the attack animation finishes
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
                // Left the attack state (finished or interrupted)
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        IsAttacking = false;
    }

    /// <summary>
    /// Waits for the swing to reach its impact frame before resolving the gather.
    /// </summary>
    private IEnumerator DelayedGather()
    {
        yield return new WaitForSeconds(gatherImpactDelay);
        TryGather();
    }

    // ───────────────────────── Gathering Logic ─────────────────────────

    /// <summary>
    /// Performs an overlap check around the player for a ResourceNode,
    /// validates tool requirements CLIENT-SIDE, then sends the hit to the server.
    ///
    /// CLIENT-SIDE ONLY — called from a coroutine started in Update() which only runs on IsOwner.
    /// </summary>
    private void TryGather()
    {
        Vector3 playerPos = transform.position + Vector3.up * 1f; // ~chest height

        // Find all colliders within gather range of the player
        Collider[] hits = Physics.OverlapSphere(playerPos, interactRange, interactLayerMask);

        if (debugDrawRay)
        {
            Debug.DrawRay(playerPos, Vector3.up * interactRange, Color.yellow, 0.5f);
            Debug.DrawRay(playerPos, Vector3.forward * interactRange, Color.yellow, 0.5f);
            Debug.DrawRay(playerPos, Vector3.right * interactRange, Color.yellow, 0.5f);
        }

        ResourceNode closestNode = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            ResourceNode node = col.GetComponentInParent<ResourceNode>();
            if (node == null) continue;

            if (node.CurrentHealth.Value <= 0) continue;

            float dist = Vector3.Distance(playerPos, col.ClosestPoint(playerPos));
            if (debugDrawRay)
            {
                Debug.Log($"[PlayerGathering] Found '{node.ResourceType}' at distance {dist:F2}m " +
                          $"(object: '{col.gameObject.name}', layer: {LayerMask.LayerToName(col.gameObject.layer)})");
            }

            if (dist < closestDist)
            {
                closestDist = dist;
                closestNode = node;
            }
        }

        if (closestNode == null)
        {
            if (debugDrawRay)
            {
                Debug.Log($"[PlayerGathering] No ResourceNode within {interactRange}m of player. " +
                          $"(OverlapSphere found {hits.Length} colliders total, " +
                          $"playerPos: {playerPos}, mask: {interactLayerMask.value})");
            }
            return;
        }

        if (debugDrawRay)
        {
            Debug.DrawLine(playerPos, closestNode.transform.position, Color.green, 1f);
        }

        // Client-side tool validation (for responsiveness)
        string myToolTag = EquippedToolTag.Value.ToString();
        if (!string.IsNullOrEmpty(closestNode.RequiredToolTag))
        {
            if (myToolTag != closestNode.RequiredToolTag)
            {
                Debug.Log($"[PlayerGathering] Can't gather '{closestNode.ResourceType}' — " +
                          $"requires '{closestNode.RequiredToolTag}' but equipped '{myToolTag}'.");
                return;
            }
        }

        Debug.Log($"[PlayerGathering] Hitting '{closestNode.ResourceType}' " +
                  $"(health: {closestNode.CurrentHealth.Value}/{closestNode.MaxHealth}, " +
                  $"distance: {closestDist:F2}m)");

        closestNode.HitServerRpc(NetworkManager.Singleton.LocalClientId, myToolTag);
    }

    // ───────────────────────── Tool Management ─────────────────────────

    /// <summary>
    /// Returns the currently equipped tool tag.
    /// Used by ResourceNode on the server to verify the client's tool.
    /// </summary>
    public string GetEquippedToolTag()
    {
        return EquippedToolTag.Value.ToString();
    }

    /// <summary>
    /// Client requests to change their equipped tool.
    /// Kept for compatibility; in normal gameplay the equipment system drives this value.
    /// </summary>
    [ServerRpc]
    public void EquipToolServerRpc(string toolTag, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerGathering] Client {rpcParams.Receive.SenderClientId} " +
                             $"tried to change tool on client {OwnerClientId}'s player!");
            return;
        }

        EquippedToolTag.Value = new Unity.Collections.FixedString32Bytes(toolTag);
        Debug.Log($"[PlayerGathering] Client {OwnerClientId} equipped tool: '{toolTag}'");
    }
}
