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
    [Tooltip("Maximum distance (meters) the player can reach to gather resources.")]
    [SerializeField] private float interactRange = 4f;

    [Tooltip("Which layers the interaction raycast can hit. " +
             "Set this to include your resource node layer and exclude UI/player layers.")]
    [SerializeField] private LayerMask interactLayerMask = ~0; // Everything by default

    [Header("Equipped Tool")]
    [Tooltip("For testing: set the initial tool tag the player spawns with. " +
             "Leave empty for 'bare hands'. Set to 'Axe' to test tree gathering.")]
    [SerializeField] private string startingToolTag = "Axe";

    [Header("Visual Feedback")]
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

        _inputActions = new InputSystem_Actions();
        _inputActions.Player.Enable();

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
        }

        // Check for gather input — using the Attack action (left mouse button)
        // since it's already bound and makes sense for "swing tool at resource"
        if (_inputActions.Player.Attack.WasPressedThisFrame())
        {
            TryGather();
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
        // Raycast from screen center (crosshair) into the world
        Ray ray = _playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (debugDrawRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * interactRange, Color.yellow, 0.5f);
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, interactLayerMask))
        {
            return; // Didn't hit anything
        }

        // Check if we hit a ResourceNode (might be on a parent or the object itself)
        ResourceNode node = hit.collider.GetComponentInParent<ResourceNode>();
        if (node == null)
        {
            return; // Hit something, but it's not a resource node
        }

        // ── Client-side tool validation (for responsiveness) ──
        // This prevents unnecessary RPCs and gives instant feedback.
        // The server will re-validate, so this is purely a UX optimization.
        string myToolTag = EquippedToolTag.Value.ToString();
        if (!string.IsNullOrEmpty(node.RequiredToolTag))
        {
            if (myToolTag != node.RequiredToolTag)
            {
                Debug.Log($"[PlayerGathering] Can't gather '{node.ResourceType}' — " +
                          $"requires '{node.RequiredToolTag}' but equipped '{myToolTag}'.");

                // TODO: Show UI feedback like "Requires Axe" floating text
                return;
            }
        }

        // ── Send hit to server ──
        Debug.Log($"[PlayerGathering] Hitting '{node.ResourceType}' " +
                  $"(health: {node.CurrentHealth.Value}/{node.MaxHealth})");

        node.HitServerRpc(NetworkManager.Singleton.LocalClientId, myToolTag);
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
