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
/// WEIGHT SYSTEM:
/// ─ Each ItemDefinition has a 'weight' field (per-unit weight, set in Inspector).
/// ─ Before adding items, the server checks if the total weight would exceed
///   maxCarryWeight. If so, it adds only as many as fit (partial add).
/// ─ CurrentWeight and MaxWeight are NetworkVariables so clients can display
///   a weight bar in the UI without calculating it themselves.
///
/// EDITOR SETUP:
/// ─ Add this component to the player prefab (same GameObject as NetworkObject).
/// ─ Set maxCarryWeight in the Inspector (default: 50).
/// ─ Set each ItemDefinition's weight field in the Inspector.
/// </summary>
public class Inventory : NetworkBehaviour
{
    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The replicated inventory. Initialized in Awake() (before OnNetworkSpawn)
    /// because NetworkList must be created in the constructor or Awake.
    /// </summary>
    public NetworkList<InventoryItem> Items { get; private set; }

    /// <summary>
    /// Current total weight of all items in the inventory.
    /// Server-authoritative — updated by the server whenever items change.
    /// Clients read this for UI (weight bar display).
    /// </summary>
    public NetworkVariable<float> CurrentWeight = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Maximum carry weight for this player. Exposed as a NetworkVariable so
    /// clients can display it in UI. Could vary per player (e.g. strength buffs).
    /// Server-authoritative.
    /// </summary>
    public NetworkVariable<float> MaxWeight = new NetworkVariable<float>(
        0f, // Overwritten from maxCarryWeight on spawn
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [Header("Settings")]
    [Tooltip("Maximum number of unique item slots this inventory can hold.")]
    [SerializeField] private int maxSlots = 20;

    [Header("Weight Capacity")]
    [Tooltip("Maximum total weight this player can carry. " +
             "Total weight = sum of (quantity × itemDefinition.weight) for all slots.")]
    [SerializeField] private float maxCarryWeight = 50f;

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
            // Set the max weight NetworkVariable from the Inspector value
            MaxWeight.Value = maxCarryWeight;
            Debug.Log($"[Inventory] Server initialized inventory for client {OwnerClientId} " +
                      $"(maxCarryWeight: {maxCarryWeight})");
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
    /// WEIGHT CHECK: Before adding, calculates whether the requested amount would
    /// exceed maxCarryWeight. If so, only adds as many as fit (partial add).
    ///
    /// Logic: if a slot with the same itemId exists, stack onto it (respecting
    /// the item's max stack size from ItemDatabase). Otherwise, create a new slot.
    /// </summary>
    /// <returns>
    /// The number of items actually added (0 = fully rejected, less than 'amount' = partial add).
    /// Callers can check this to know if the add was fully/partially successful.
    /// </returns>
    public int AddItem(string itemId, int amount)
    {
        if (!IsServer)
        {
            Debug.LogError("[Inventory] AddItem called on client — this should never happen!");
            return 0;
        }

        if (amount <= 0) return 0;

        // Look up the item definition for stack size and weight
        float itemWeight = 1f;  // Default if not found
        int maxStack = int.MaxValue;
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);
        if (itemDef != null)
        {
            maxStack = itemDef.maxStackSize;
            itemWeight = itemDef.weight;
        }

        // ── Weight capacity check ──
        // Calculate how many items we can actually add without exceeding the weight cap.
        float currentWeight = CurrentWeight.Value;
        float remainingCapacity = maxCarryWeight - currentWeight;

        if (remainingCapacity <= 0f)
        {
            // Already at or over capacity — reject entirely
            Debug.Log($"[Inventory] Cannot add {amount}x {itemId} — inventory at max weight " +
                      $"({currentWeight:F1}/{maxCarryWeight:F1}).");
            NotifyOverweightClientRpc(itemId, 0, amount);
            return 0;
        }

        // How many units fit under the weight cap?
        int maxByWeight = (itemWeight > 0f)
            ? Mathf.FloorToInt(remainingCapacity / itemWeight)
            : amount; // Zero-weight items have no limit

        if (maxByWeight <= 0)
        {
            Debug.Log($"[Inventory] Cannot add {itemId} — not enough carry capacity " +
                      $"(need {itemWeight:F1}, have {remainingCapacity:F1} remaining).");
            NotifyOverweightClientRpc(itemId, 0, amount);
            return 0;
        }

        // Clamp the amount to what weight allows
        int actualAmount = Mathf.Min(amount, maxByWeight);
        int remaining = actualAmount;

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

        int actuallyAdded = actualAmount - remaining;

        // Update the weight NetworkVariable
        CurrentWeight.Value += actuallyAdded * itemWeight;

        if (actuallyAdded < amount)
        {
            int rejected = amount - actuallyAdded;
            if (actuallyAdded > 0)
            {
                Debug.Log($"[Inventory] Partially added {actuallyAdded}/{amount}x {itemId} " +
                          $"to client {OwnerClientId} (weight: {CurrentWeight.Value:F1}/{maxCarryWeight:F1}). " +
                          $"{rejected}x rejected — over capacity or inventory full.");
            }
            else
            {
                Debug.Log($"[Inventory] Could not add {amount}x {itemId} — inventory full!");
            }

            NotifyOverweightClientRpc(itemId, actuallyAdded, amount);
        }
        else
        {
            Debug.Log($"[Inventory] Added {actuallyAdded}x {itemId} to client {OwnerClientId} " +
                      $"(weight: {CurrentWeight.Value:F1}/{maxCarryWeight:F1})");
        }

        return actuallyAdded;
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

    // ───────────────────────── ClientRpcs ─────────────────────────

    /// <summary>
    /// Sent by the server to the owning client when an item add was rejected or
    /// partially added due to weight capacity. Clients use this for UI feedback.
    /// </summary>
    /// <param name="itemId">The item that was being added.</param>
    /// <param name="added">How many were actually added (0 = fully rejected).</param>
    /// <param name="requested">How many were originally requested.</param>
    [ClientRpc]
    private void NotifyOverweightClientRpc(string itemId, int added, int requested)
    {
        // Only show feedback on the owning client
        if (!IsOwner) return;

        if (added == 0)
        {
            Debug.Log($"[Inventory] Cannot carry any more {itemId} — " +
                      $"inventory at max weight ({CurrentWeight.Value:F1}/{MaxWeight.Value:F1})!");
        }
        else
        {
            Debug.Log($"[Inventory] Only picked up {added}/{requested}x {itemId} — " +
                      $"weight limit reached ({CurrentWeight.Value:F1}/{MaxWeight.Value:F1}).");
        }

        // TODO: Show UI feedback (floating text, weight bar flash, etc.)
    }

    // ───────────────────────── Weight Utilities ─────────────────────────

    /// <summary>
    /// Recalculates total weight from scratch by iterating all slots.
    /// Runs on ANY peer (uses the replicated NetworkList + local ItemDatabase).
    /// Useful for UI displays or verifying the server's CurrentWeight value.
    /// </summary>
    public float CalculateTotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < Items.Count; i++)
        {
            string id = Items[i].ItemId.ToString();
            ItemDefinition def = ItemDatabase.GetItem(id);
            float w = (def != null) ? def.weight : 1f;
            total += Items[i].Quantity * w;
        }
        return total;
    }

    /// <summary>
    /// Returns the remaining carry capacity (how much more weight can be added).
    /// Runs on ANY peer.
    /// </summary>
    public float RemainingCapacity => MaxWeight.Value - CurrentWeight.Value;

    /// <summary>
    /// Returns true if the inventory is at or over its weight limit.
    /// Runs on ANY peer.
    /// </summary>
    public bool IsOverweight => CurrentWeight.Value >= MaxWeight.Value;

    // ───────────────────────── Inventory Utilities ─────────────────────────

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
                      $"Index: {changeEvent.Index}, Value: {changeEvent.Value} " +
                      $"(weight: {CurrentWeight.Value:F1}/{MaxWeight.Value:F1})");
        }
    }
}
