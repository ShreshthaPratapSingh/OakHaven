using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A networked world pickup. Walking up to this object and pressing the interact
/// button (E) grants the configured item to the player's ResourceInventory.
///
/// NETWORKING FLOW:
/// ─ Detection and input happen client-side on the owning player.
/// ─ The client calls RequestPickupServerRpc on the pickup.
/// ─ The server validates sender identity, range, line-of-sight, and inventory,
///   then grants the item and consumes the pickup.
/// ─ Pickups are scene objects; when consumed they despawn for all clients.
///
/// SETUP:
/// 1. Add this component to a GameObject root alongside a NetworkObject.
/// 2. Add a trigger collider so PlayerInteractor can detect it with OverlapSphere.
/// 3. Configure itemId, amount, and interactionRange in the Inspector.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class WorldPickup : NetworkBehaviour
{
    [Header("Reward")]
    [Tooltip("Item ID to grant (e.g. 'stone', 'wood', 'fiber').")]
    [SerializeField] private string itemId;

    [Tooltip("Total amount granted by this pickup.")]
    [SerializeField] private int amount = 1;

    [Header("Interaction")]
    [Tooltip("How close the player must be to collect this pickup.")]
    [SerializeField] private float interactionRange = 1.8f;

    /// <summary>
    /// Item ID granted by this pickup.
    /// </summary>
    public string ItemId => itemId;

    /// <summary>
    /// Maximum distance at which this pickup can be collected.
    /// </summary>
    public float InteractionRange => interactionRange;

    /// <summary>
    /// Remaining amount on the server. Replicated to clients.
    /// </summary>
    private readonly NetworkVariable<int> _remaining = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            _remaining.Value = Mathf.Max(0, amount);
        }
    }

    /// <summary>
    /// Called by the local player when they press the interact button while this
    /// pickup is selected. Server validates everything before granting the reward.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (_remaining.Value <= 0) return;

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
        {
            Debug.LogWarning($"[WorldPickup] Request from unknown client {senderId}.");
            return;
        }

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null)
        {
            Debug.LogWarning($"[WorldPickup] Client {senderId} has no player object.");
            return;
        }

        // ── Proximity validation ──
        Vector3 playerPos = playerObject.transform.position + Vector3.up * 1f;
        float distance = Vector3.Distance(playerPos, transform.position);
        if (distance > interactionRange + 0.5f)
        {
            Debug.LogWarning($"[WorldPickup] Client {senderId} is {distance:F2}m away from pickup '{itemId}' (max {interactionRange + 0.5f:F2}m).");
            return;
        }

        // ── Line-of-sight validation ──
        Vector3 targetPos = transform.position + Vector3.up * 0.3f;
        if (Physics.Linecast(playerPos, targetPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform hitRoot = hit.transform;
            if (hitRoot != transform && !hitRoot.IsChildOf(transform))
            {
                Debug.LogWarning($"[WorldPickup] Line of sight blocked for client {senderId} by '{hitRoot.name}'.");
                return;
            }
        }

        // ── Item validation ──
        if (ItemDatabase.GetItem(itemId) == null)
        {
            Debug.LogError($"[WorldPickup] Unknown item id '{itemId}'.");
            return;
        }

        // ── Inventory grant ──
        ResourceInventory resourceInventory = playerObject.GetComponent<ResourceInventory>();
        if (resourceInventory == null)
        {
            Debug.LogWarning($"[WorldPickup] Client {senderId} has no ResourceInventory.");
            return;
        }

        int granted = resourceInventory.AddItem(itemId, _remaining.Value);
        if (granted <= 0)
        {
            Debug.Log($"[WorldPickup] Client {senderId} could not carry any '{itemId}' (inventory full).");
            return;
        }

        _remaining.Value -= granted;
        Debug.Log($"[WorldPickup] Client {senderId} picked up {granted}x '{itemId}' (remaining {_remaining.Value}).");

        if (_remaining.Value <= 0)
        {
            NetworkObject.Despawn(true);
        }
    }
}
