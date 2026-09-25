using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative inventory attached to each player's NetworkObject.
///
/// NETWORKING FLOW:
/// ─ The NetworkList<InventoryItem> is owned by the server.
///   Only the server may Add/Remove/Modify elements.
/// ─ Changes are automatically replicated to the owning client
///   (and any observers, depending on NetworkObject visibility settings).
/// ─ Clients subscribe to Items.OnListChanged to react to updates
///   (e.g. rebuilding the inventory UI). They NEVER write to the list directly.
/// ─ AddItem is called by the server internally (e.g. from ResourceNode.HitServerRpc).
///   AddItemServerRpc is provided so a client *could* request an add
///   (useful for future features like picking up dropped items), but the
///   server validates everything.
///
/// EDITOR SETUP:
/// ─ Add this component to the player prefab (same GameObject as NetworkObject).
/// ─ No Inspector wiring needed — it self-initializes in Awake().
/// </summary>
public class Inventory : NetworkBehaviour
{
    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The replicated inventory. Initialized in Awake() (before OnNetworkSpawn)
    /// because NetworkList must be created in the constructor or Awake.
    /// </summary>
    public NetworkList<InventoryItem> Items { get; private set; }

    [Header("Settings")]
    [Tooltip("Maximum number of unique item slots this inventory can hold.")]
    [SerializeField] private int maxSlots = 20;

    // ───────────────────────── Lifecycle ─────────────────────────

    private void Awake()
    {
        // NetworkList MUST be created before OnNetworkSpawn is called.
        // Creating it here ensures it's ready for both server and clients.
        Items = new NetworkList<InventoryItem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            Debug.Log($"[Inventory] Server initialized inventory for client {OwnerClientId}");
        }

        // Both server and clients can subscribe to changes.
        // The owning client uses this to update the UI.
        Items.OnListChanged += OnInventoryChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        Items.OnListChanged -= OnInventoryChanged;
    }

    // ───────────────────────── Server-Side Methods ─────────────────────────

    /// <summary>
    /// Adds items to the inventory. SERVER-ONLY — call this from other server-side
    /// code (e.g. ResourceNode.HitServerRpc).
    ///
    /// Logic: if a slot with the same itemId exists, stack onto it (respecting
    /// the item's max stack size from ItemDatabase). Otherwise, create a new slot.
    /// </summary>
    /// <returns>True if the items were added successfully, false if inventory is full.</returns>
    public bool AddItem(string itemId, int amount)
    {
        if (!IsServer)
        {
            Debug.LogError("[Inventory] AddItem called on client — this should never happen!");
            return false;
        }

        if (amount <= 0) return false;

        // Look up the item definition for stack size validation
        int maxStack = int.MaxValue; // Default: unlimited stacking
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);
        if (itemDef != null)
        {
            maxStack = itemDef.maxStackSize;
        }

        int remaining = amount;

        // First pass: try to stack onto existing slots
        for (int i = 0; i < Items.Count && remaining > 0; i++)
        {
            InventoryItem slot = Items[i];
            if (slot.ItemId.ToString() == itemId && slot.Quantity < maxStack)
            {
                int spaceInSlot = maxStack - slot.Quantity;
                int toAdd = Mathf.Min(remaining, spaceInSlot);
                slot.Quantity += toAdd;
                Items[i] = slot; // Must reassign — NetworkList needs the setter to detect the change
                remaining -= toAdd;
            }
        }

        // Second pass: fill new slots with remaining amount
        while (remaining > 0 && Items.Count < maxSlots)
        {
            int toAdd = Mathf.Min(remaining, maxStack);
            Items.Add(new InventoryItem(itemId, toAdd));
            remaining -= toAdd;
        }

        if (remaining > 0)
        {
            Debug.LogWarning($"[Inventory] Could not add {remaining}x {itemId} — inventory full!");
            return false;
        }

        Debug.Log($"[Inventory] Added {amount}x {itemId} to client {OwnerClientId}'s inventory");
        return true;
    }

    // ───────────────────────── ServerRpcs ─────────────────────────

    /// <summary>
    /// Client-callable RPC to request adding an item. The server validates and
    /// performs the actual add. Useful for future features like picking up world drops.
    ///
    /// SECURITY NOTE: In production, you'd validate that the client is actually
    /// near the item they're trying to pick up, etc. For now, we just do the add.
    /// </summary>
    [ServerRpc]
    public void AddItemServerRpc(string itemId, int amount, ServerRpcParams rpcParams = default)
    {
        // Validate that the RPC sender actually owns this inventory
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Inventory] Client {rpcParams.Receive.SenderClientId} tried to modify " +
                             $"client {OwnerClientId}'s inventory — rejected!");
            return;
        }

        AddItem(itemId, amount);
    }

    // ───────────────────────── Utilities ─────────────────────────

    /// <summary>
    /// Check if the inventory contains at least 'amount' of a given item.
    /// Runs on any peer (server or client) since NetworkList is replicated.
    /// </summary>
    public bool HasItem(string itemId, int amount = 1)
    {
        int total = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].ItemId.ToString() == itemId)
            {
                total += Items[i].Quantity;
                if (total >= amount) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Returns the total quantity of a given item across all slots.
    /// Runs on any peer since NetworkList is replicated.
    /// </summary>
    public int GetItemCount(string itemId)
    {
        int total = 0;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].ItemId.ToString() == itemId)
            {
                total += Items[i].Quantity;
            }
        }
        return total;
    }

    // ───────────────────────── Debug Logging ─────────────────────────

    /// <summary>
    /// Called on ALL peers whenever the NetworkList changes (add, remove, value change).
    /// Primarily useful for debug logging; the UI subscribes separately.
    /// </summary>
    private void OnInventoryChanged(NetworkListEvent<InventoryItem> changeEvent)
    {
        // Only log on the owning client for clarity (avoid duplicate logs on server+client)
        if (IsOwner)
        {
            Debug.Log($"[Inventory] Change detected — Type: {changeEvent.Type}, " +
                      $"Index: {changeEvent.Index}, Value: {changeEvent.Value}");
        }
    }
}
