using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A networked item drop that lies in the world and can be picked up by players.
/// Created when a player drops items from their inventory.
///
/// NETWORKING FLOW:
/// ─ The server spawns the drop with item id and quantity.
/// ─ State replicates via NetworkVariables.
/// ─ A client calls RequestPickupServerRpc when interacting.
/// ─ The server validates sender identity, range, line-of-sight, and inventory,
///   then grants as many items as fit and either consumes or shrinks the drop.
///
/// PICKUP TARGET:
/// ─ Tools/equipment are added to the player's Inventory (equipment).
/// ─ Resources are added to the player's ResourceInventory.
/// ─ If the preferred inventory is full, the other inventory is tried as a fallback.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class WorldItemDrop : NetworkBehaviour
{
    [Header("Interaction")]
    [Tooltip("How close the player must be to collect this drop.")]
    [SerializeField] private float interactionRange = 1.8f;

    [Tooltip("Maximum distance the drop will search downward for ground when spawned.")]
    [SerializeField] private float groundSnapDistance = 3f;

    public float InteractionRange => interactionRange;

    [Header("Visuals")]
    [Tooltip("Renderer whose material color is tinted by item category.")]
    [SerializeField] private Renderer visualRenderer;

    [Tooltip("World-space text that displays the item name. Optional.")]
    [SerializeField] private TextMesh labelTextMesh;

    [Tooltip("World-space text that displays the quantity. Optional.")]
    [SerializeField] private TextMesh quantityTextMesh;

    private readonly NetworkVariable<FixedString32Bytes> _itemId = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> _quantity = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public string ItemId => _itemId.Value.ToString();
    public int Quantity => _quantity.Value;

    /// <summary>
    /// Server-only initializer. Must be called immediately after instantiation
    /// and before (or right after) NetworkObject.Spawn().
    /// </summary>
    public void Initialize(string itemId, int quantity)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[WorldItemDrop] Initialize should only be called on the server.");
            return;
        }

        _itemId.Value = new FixedString32Bytes(itemId);
        _quantity.Value = Mathf.Max(0, quantity);
        UpdateVisual();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            SnapToGround();
        }
        else
        {
            // Clients don't need to simulate the drop's physics — the server
            // drives position via NetworkTransform. Making it kinematic avoids
            // the local physics body fighting the replicated transform.
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
        }

        UpdateVisual();
    }

    /// <summary>
    /// Casts downward from the spawn position and places the drop on the first
    /// solid surface found. Prevents drops from hovering in mid-air.
    /// </summary>
    private void SnapToGround()
    {
        // Cast downward from just above the drop and land on the first solid
        // surface that isn't us or another player.
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, groundSnapDistance, ~0, QueryTriggerInteraction.Ignore);

        Debug.Log($"[WorldItemDrop] SnapToGround called from {transform.position}, origin {origin}, hits={hits.Length}");

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            // Ignore ourselves.
            if (hit.transform == transform || hit.transform.IsChildOf(transform))
            {
                Debug.Log($"[WorldItemDrop] Ignoring self hit '{hit.transform.name}'");
                continue;
            }

            // Ignore other players so we don't land on someone's head.
            NetworkObject hitNetObj = hit.transform.GetComponentInParent<NetworkObject>();
            if (hitNetObj != null && hitNetObj.IsPlayerObject)
            {
                Debug.Log($"[WorldItemDrop] Ignoring player hit '{hit.transform.name}'");
                continue;
            }

            // Place the drop so its physics collider base sits on the hit point.
            // The BoxCollider on the prefab is centered at y=0.15 with size 0.3,
            // so its bottom is at the object's position.
            Vector3 newPos = hit.point + Vector3.up * 0.01f;
            Debug.Log($"[WorldItemDrop] Snapping to '{hit.transform.name}' at {hit.point}, newPos={newPos}");
            transform.position = newPos;

            // Freeze the drop in place so it doesn't roll or slide off slopes.
            Rigidbody rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = Vector3.zero;
            }
            return;
        }

        Debug.LogWarning($"[WorldItemDrop] No ground found within {groundSnapDistance}m below {origin}. Drop will remain in air.");
    }

    private void UpdateVisual()
    {
        if (string.IsNullOrEmpty(ItemId)) return;

        ItemDefinition def = ItemDatabase.GetItem(ItemId);
        if (def != null)
        {
            if (labelTextMesh != null)
                labelTextMesh.text = def.displayName;

            if (quantityTextMesh != null)
                quantityTextMesh.text = $"x{_quantity.Value}";

            if (visualRenderer != null)
            {
                Color categoryColor = GetCategoryColor(def.category);
                // Use a material instance so we don't mutate shared assets.
                Material mat = visualRenderer.material;
                mat.color = categoryColor;
            }
        }
        else
        {
            if (labelTextMesh != null)
                labelTextMesh.text = ItemId;

            if (quantityTextMesh != null)
                quantityTextMesh.text = $"x{_quantity.Value}";
        }
    }

    private static Color GetCategoryColor(string category)
    {
        if (string.IsNullOrEmpty(category)) return Color.gray;
        string c = category.ToLowerInvariant();
        if (c == "wood" || c == "resource") return new Color(0.4f, 0.7f, 0.3f);
        if (c == "stone") return new Color(0.5f, 0.5f, 0.55f);
        if (c == "fiber") return new Color(0.8f, 0.9f, 0.3f);
        if (c == "tool") return new Color(0.7f, 0.5f, 0.3f);
        return Color.gray;
    }

    /// <summary>
    /// Called by the local player when they interact with this drop.
    /// Server validates everything before granting items.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestPickupServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!IsServer) return;
        if (_quantity.Value <= 0)
        {
            NetworkObject.Despawn(true);
            return;
        }

        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderId, out NetworkClient client))
        {
            Debug.LogWarning($"[WorldItemDrop] Request from unknown client {senderId}.");
            return;
        }

        NetworkObject playerObject = client.PlayerObject;
        if (playerObject == null)
        {
            Debug.LogWarning($"[WorldItemDrop] Client {senderId} has no player object.");
            return;
        }

        // Proximity validation
        Vector3 playerPos = playerObject.transform.position + Vector3.up * 1f;
        float distance = Vector3.Distance(playerPos, transform.position);
        if (distance > interactionRange + 0.5f)
        {
            Debug.LogWarning($"[WorldItemDrop] Client {senderId} is {distance:F2}m away from drop '{ItemId}' (max {interactionRange + 0.5f:F2}m).");
            return;
        }

        // Line-of-sight validation
        Vector3 targetPos = transform.position + Vector3.up * 0.3f;
        if (Physics.Linecast(playerPos, targetPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform hitRoot = hit.transform;
            if (hitRoot != transform && !hitRoot.IsChildOf(transform))
            {
                Debug.LogWarning($"[WorldItemDrop] Line of sight blocked for client {senderId} by '{hitRoot.name}'.");
                return;
            }
        }

        // Item validation
        ItemDefinition def = ItemDatabase.GetItem(ItemId);
        if (def == null)
        {
            Debug.LogError($"[WorldItemDrop] Unknown item id '{ItemId}'.");
            return;
        }

        Inventory equipmentInv = playerObject.GetComponent<Inventory>();
        ResourceInventory resourceInv = playerObject.GetComponent<ResourceInventory>();

        int granted = 0;
        if (IsEquippable(def))
        {
            // Tools/equipment prefer the equipment inventory, fallback to resources.
            if (equipmentInv != null)
                granted = equipmentInv.AddItem(ItemId, _quantity.Value);
            if (granted <= 0 && resourceInv != null)
                granted = resourceInv.AddItem(ItemId, _quantity.Value);
        }
        else
        {
            // Resources prefer the resource inventory, fallback to equipment.
            if (resourceInv != null)
                granted = resourceInv.AddItem(ItemId, _quantity.Value);
            if (granted <= 0 && equipmentInv != null)
                granted = equipmentInv.AddItem(ItemId, _quantity.Value);
        }

        if (granted <= 0)
        {
            Debug.Log($"[WorldItemDrop] Client {senderId} could not carry any '{ItemId}' (inventory full).");
            return;
        }

        _quantity.Value -= granted;
        Debug.Log($"[WorldItemDrop] Client {senderId} picked up {granted}x '{ItemId}' (remaining {_quantity.Value}).");

        if (_quantity.Value <= 0)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            UpdateVisual();
        }
    }

    private static bool IsEquippable(ItemDefinition def)
    {
        if (def == null) return false;
        if (def.equippedPrefab != null) return true;
        if (!string.IsNullOrEmpty(def.toolTag)) return true;
        string category = def.category?.ToLowerInvariant() ?? "";
        return category == "tool" || category == "weapon";
    }
}
