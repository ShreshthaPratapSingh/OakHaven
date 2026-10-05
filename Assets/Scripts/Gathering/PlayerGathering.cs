using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles the player's resource gathering interaction.
/// Raycasts from the camera to detect ResourceNode objects and calls HitServerRpc.
///
/// NETWORKING FLOW:
/// ─ Input reading and raycasting happen CLIENT-SIDE (IsOwner only).
/// ─ Client-side tool validation provides immediate feedback (no network round-trip).
/// ─ If validation passes, we call ResourceNode.HitServerRpc(), which sends
///   the RPC to the server for authoritative processing.
/// ─ The server re-validates everything before granting resources.
///
/// EQUIPPED TOOL:
/// ─ equippedToolTag is a NetworkVariable so the server can verify what tool
///   the player actually has equipped (prevents spoofing).
/// ─ Only the server writes to it; clients request changes via EquipToolServerRpc.
/// ─ For initial testing with just trees, set it to "Axe" in the Inspector
///   or call EquipToolServerRpc("Axe") at start.
///
/// EDITOR SETUP:
/// 1. Add this component to the player prefab (same GameObject as PlayerController).
/// 2. Set interactRange in the Inspector (default: 4 meters).
/// 3. Set interactLayerMask to include the layer your resource nodes are on.
/// 4. No camera reference needed — we find Camera.main at runtime for the owner.
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

    [Header("Visual Debug")]
    [Tooltip("If true, draws a debug ray in the Scene view showing the interaction raycast.")]
    [SerializeField] private bool debugDrawRay = true;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The tag of the player's currently equipped tool.
    /// Replicated to all peers so the server can validate tool requirements.
    /// Only the server writes this (clients request changes via RPC).
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
    private Coroutine _punchRoutine;
    private bool _punchQueued;

    /// <summary>
    /// True while the punch animation is playing.
    /// Read by PlayerController to freeze movement during the swing.
    /// </summary>
    public bool IsPunching { get; private set; }

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
        // We use = instead of &= because the serialized Inspector value may not
        // include custom layers (like ResourceBreak) that were added after the
        // component was first created. This guarantees the raycast can hit all
        // world layers (terrain, resources, etc.) while skipping our own collider.
        int playerLayer = gameObject.layer;
        interactLayerMask = ~(1 << playerLayer);
        Debug.Log($"[PlayerGathering] Interact LayerMask set to: {interactLayerMask.value} " +
                  $"(excluding layer {playerLayer} '{LayerMask.LayerToName(playerLayer)}')");

        _inputActions = new InputSystem_Actions();
        _inputActions.Player.Enable();

        _animator = GetComponentInChildren<Animator>();

        // Find the player's camera. It's a child of the camera pivot on this player.
        // We use FindMainCamera() in Update since the camera might not be active yet.
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

        // Check for gather input — using the Attack action (left mouse button)
        // since it's already bound and makes sense for "swing tool at resource"
        if (_inputActions.Player.Attack.WasPressedThisFrame())
        {
            if (IsPunching)
            {
                // Already punching — queue the next punch to play after this one finishes
                _punchQueued = true;
            }
            else
            {
                StartPunch();
            }

            TryGather();
        }
    }

    /// <summary>
    /// Begins a punch: plays the animation and starts the movement-lock timer.
    /// </summary>
    private void StartPunch()
    {
        _punchQueued = false;

        if (_animator != null)
        {
            // Force-play Punch from frame 0, bypassing transitions (no Idle flicker)
            _animator.SetTrigger("Punch");
        }

        if (_punchRoutine != null) StopCoroutine(_punchRoutine);
        _punchRoutine = StartCoroutine(PunchDuration());
    }

    /// <summary>
    /// Locks movement for the punch animation duration.
    /// Polls the Animator each frame to detect the exact moment the Punch clip
    /// finishes, then either chains into a queued punch or unlocks movement.
    /// </summary>
    private IEnumerator PunchDuration()
    {
        IsPunching = true;

        // Wait one frame for the Animator to enter the Punch state
        yield return null;

        // Poll until the Punch animation finishes (normalizedTime >= 1.0)
        while (_animator != null)
        {
            AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(1);

            // Check we're still in the Punch state and it has completed
            if (stateInfo.IsName("Punch") && stateInfo.normalizedTime >= 1f)
            {
                break;
            }

            // If we've already left Punch (shouldn't happen, but safety check)
            if (!stateInfo.IsName("Punch"))
            {
                break;
            }

            yield return null;
        }

        if (_punchQueued)
        {
            // Chain directly into the next punch — no Idle in between
            StartPunch();
        }
        else
        {
            IsPunching = false;
        }
    }

    // ───────────────────────── Gathering Logic ─────────────────────────

    /// <summary>
    /// Performs a raycast from the camera center, checks for a ResourceNode,
    /// validates tool requirements CLIENT-SIDE, then sends the hit to the server.
    ///
    /// CLIENT-SIDE ONLY — called from Update() which only runs on IsOwner.
    /// </summary>
    private void TryGather()
    {
        // ─── THIRD-PERSON GATHERING APPROACH ───
        // A pixel-thin raycast from the camera rarely hits thin tree trunk colliders
        // because screen-center in third-person typically points at the foliage (no collider)
        // or the sky. Instead, we find the nearest ResourceNode within arm's reach
        // of the PLAYER'S position using OverlapSphere, which is standard for
        // third-person melee/gathering interactions.

        Vector3 playerPos = transform.position + Vector3.up * 1f; // ~chest height

        // Find all colliders within gather range of the player
        Collider[] hits = Physics.OverlapSphere(playerPos, interactRange, interactLayerMask);

        if (debugDrawRay)
        {
            // Draw the gather sphere in the Scene view
            Debug.DrawRay(playerPos, Vector3.up * interactRange, Color.yellow, 0.5f);
            Debug.DrawRay(playerPos, Vector3.forward * interactRange, Color.yellow, 0.5f);
            Debug.DrawRay(playerPos, Vector3.right * interactRange, Color.yellow, 0.5f);
        }

        // Find the closest ResourceNode among all hits
        ResourceNode closestNode = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            ResourceNode node = col.GetComponentInParent<ResourceNode>();
            if (node == null) continue;

            // Skip depleted nodes
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
            // Draw a line from player to the resource we're about to hit
            Debug.DrawLine(playerPos, closestNode.transform.position, Color.green, 1f);
        }

        // ── Client-side tool validation (for responsiveness) ──
        string myToolTag = EquippedToolTag.Value.ToString();
        if (!string.IsNullOrEmpty(closestNode.RequiredToolTag))
        {
            if (myToolTag != closestNode.RequiredToolTag)
            {
                Debug.Log($"[PlayerGathering] Can't gather '{closestNode.ResourceType}' — " +
                          $"requires '{closestNode.RequiredToolTag}' but equipped '{myToolTag}'.");

                // TODO: Show UI feedback like "Requires Axe" floating text
                return;
            }
        }

        // ── Send hit to server ──
        Debug.Log($"[PlayerGathering] Hitting '{closestNode.ResourceType}' " +
                  $"(health: {closestNode.CurrentHealth.Value}/{closestNode.MaxHealth}, " +
                  $"distance: {closestDist:F2}m)");

        closestNode.HitServerRpc(NetworkManager.Singleton.LocalClientId, myToolTag);
    }

    // ───────────────────────── Tool Management ─────────────────────────

    /// <summary>
    /// Returns the currently equipped tool tag.
    /// Used by ResourceNode on the server to verify the client's tool.
    /// Reads from the NetworkVariable, so it works on all peers.
    /// </summary>
    public string GetEquippedToolTag()
    {
        return EquippedToolTag.Value.ToString();
    }

    /// <summary>
    /// Client requests to change their equipped tool.
    /// In a full implementation, this would validate against the player's
    /// inventory to ensure they actually own the tool.
    /// </summary>
    [ServerRpc]
    public void EquipToolServerRpc(string toolTag, ServerRpcParams rpcParams = default)
    {
        // Validate sender owns this player
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[PlayerGathering] Client {rpcParams.Receive.SenderClientId} " +
                             $"tried to change tool on client {OwnerClientId}'s player!");
            return;
        }

        // TODO: Validate the player actually has this tool in their inventory

        EquippedToolTag.Value = new Unity.Collections.FixedString32Bytes(toolTag);
        Debug.Log($"[PlayerGathering] Client {OwnerClientId} equipped tool: '{toolTag}'");
    }
}
