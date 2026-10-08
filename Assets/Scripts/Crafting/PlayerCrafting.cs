using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles the player's workstation interaction detection.
/// Uses OverlapSphere to find nearby workstations (same pattern as PlayerGathering).
/// Lives on the player prefab alongside PlayerGathering, Inventory, ResourceInventory.
///
/// NETWORKING FLOW:
/// ─ Input reading and proximity detection happen CLIENT-SIDE (IsOwner only).
/// ─ When the player interacts, this script calls Workstation.ContributeRepairServerRpc()
///   or Workstation.RequestCraftServerRpc() which run on the server.
///
/// EDITOR SETUP:
/// 1. Add this component to the player prefab (same GameObject as PlayerController).
/// 2. Set interactRange in the Inspector.
/// 3. Set interactLayerMask to include the layer your workstations are on.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerCrafting : NetworkBehaviour
{
    // ───────────────────────── Configuration ─────────────────────────

    [Header("Interaction Settings")]
    [Tooltip("Maximum distance to detect workstations. " +
             "Should match or be slightly less than Workstation.interactRange.")]
    [SerializeField] private float interactRange = 4f;

    [Tooltip("Which layers the interaction detection can hit.")]
    [SerializeField] private LayerMask interactLayerMask = ~0;

    [Header("Input")]
    [Tooltip("Key to interact with the nearest workstation.")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;

    // ───────────────────────── Private State ─────────────────────────

    /// <summary>
    /// The workstation currently in range (closest one). Null if none nearby.
    /// </summary>
    private Workstation _nearbyWorkstation;

    // ───────────────────────── Public Accessors ─────────────────────────

    /// <summary>
    /// The workstation currently in range. Used by UI to show interact prompts
    /// and workstation panels.
    /// </summary>
    public Workstation NearbyWorkstation => _nearbyWorkstation;

    // ───────────────────────── Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner) return;

        // Exclude our own layer from detection (same pattern as PlayerGathering)
        int playerLayer = gameObject.layer;
        interactLayerMask = ~(1 << playerLayer);
    }

    // ───────────────────────── Update (Client-Side Only) ─────────────────────────

    private void Update()
    {
        if (!IsOwner) return;

        // Continuously scan for nearby workstations
        DetectNearbyWorkstation();

        // Handle interact input
        if (Input.GetKeyDown(interactKey) && _nearbyWorkstation != null)
        {
            OnInteractWithWorkstation();
        }
    }

    // ───────────────────────── Workstation Detection ─────────────────────────

    /// <summary>
    /// Finds the closest workstation within range using OverlapSphere.
    /// Same approach as PlayerGathering.TryGather().
    /// </summary>
    private void DetectNearbyWorkstation()
    {
        Vector3 playerPos = transform.position + Vector3.up * 1f;
        Collider[] hits = Physics.OverlapSphere(playerPos, interactRange, interactLayerMask);

        Workstation closest = null;
        float closestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            Workstation ws = col.GetComponentInParent<Workstation>();
            if (ws == null) continue;

            float dist = Vector3.Distance(playerPos, col.ClosestPoint(playerPos));
            if (dist < closestDist)
            {
                closestDist = dist;
                closest = ws;
            }
        }

        _nearbyWorkstation = closest;
    }

    // ───────────────────────── Interaction ─────────────────────────

    /// <summary>
    /// Called when the player presses the interact key near a workstation.
    /// For now, logs the interaction. The UI system (Phase 4) will hook into this.
    /// </summary>
    private void OnInteractWithWorkstation()
    {
        if (_nearbyWorkstation == null) return;

        var def = _nearbyWorkstation.Definition;
        if (def == null)
        {
            Debug.LogWarning("[PlayerCrafting] Nearby workstation has no definition!");
            return;
        }

        if (!_nearbyWorkstation.IsRepaired.Value)
        {
            Debug.Log($"[PlayerCrafting] Workstation '{def.displayName}' needs repair. " +
                      $"Press interact to contribute resources.");
            // TODO: Open repair UI panel
        }
        else
        {
            Debug.Log($"[PlayerCrafting] Workstation '{def.displayName}' is ready. " +
                      $"Opening crafting menu...");
            // TODO: Open crafting UI panel
        }
    }

    // ───────────────────────── Public Methods (called by UI) ─────────────────────────

    /// <summary>
    /// Sends a repair contribution to the server.
    /// Called by the WorkstationUI when the player clicks a "Contribute" button.
    /// </summary>
    public void ContributeRepair(string itemId, int amount)
    {
        if (_nearbyWorkstation == null) return;

        _nearbyWorkstation.ContributeRepairServerRpc(
            NetworkManager.Singleton.LocalClientId, itemId, amount);
    }

    /// <summary>
    /// Sends a craft request to the server.
    /// Called by the WorkstationUI when the player clicks a "Craft" button.
    /// </summary>
    public void RequestCraft(string recipeId)
    {
        if (_nearbyWorkstation == null) return;

        _nearbyWorkstation.RequestCraftServerRpc(
            NetworkManager.Singleton.LocalClientId, recipeId);
    }
}
