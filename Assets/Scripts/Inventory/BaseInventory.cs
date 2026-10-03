using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Abstract base class for all per-player inventory systems.
/// Provides server-authoritative weight-checked, stack-aware item storage
/// backed by a replicated NetworkList<InventoryItem>.
///
/// SUBCLASSES:
/// ─ Inventory (EquipmentInventory): tools, weapons, crafted gear
/// ─ ResourceInventory: raw gathered materials (Wood, Stone, Fiber)
///
/// Each subclass is its own NetworkBehaviour with its own NetworkList and
/// NetworkVariables. Multiple NetworkBehaviours on one NetworkObject is
/// fully supported by Netcode for GameObjects — no conflicts.
///
/// NETWORKING FLOW:
/// ─ The NetworkList is owned by the server. Only the server writes to it.
/// ─ Changes auto-replicate to the owning client (and observers).
/// ─ Clients subscribe to Items.OnListChanged for UI updates.
///
/// WEIGHT SYSTEM:
/// ─ Each ItemDefinition has a 'weight' field (per-unit, set in Inspector).
/// ─ Before adding, the server checks if total weight would exceed maxCarryWeight.
/// ─ If so, only as many as fit are added (partial add).
/// ─ CurrentWeight and MaxWeight are NetworkVariables for client UI.
/// </summary>
public abstract class BaseInventory : NetworkBehaviour
{
    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The replicated inventory list. Initialized in Awake().
    /// </summary>
    public NetworkList<InventoryItem> Items { get; private set; }

    /// <summary>
    /// Current total weight. Server-authoritative, clients read for UI.
    /// </summary>
    public NetworkVariable<float> CurrentWeight = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Maximum carry weight. Server-authoritative, clients read for UI.
    /// </summary>
    public NetworkVariable<float> MaxWeight = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // ───────────────────────── Configuration ─────────────────────────

    [Header("Capacity Settings")]
    [Tooltip("Maximum number of unique item slots.")]
    [SerializeField] private int maxSlots = 20;

    [Tooltip("Maximum total weight this inventory can hold. " +
             "Total weight = sum of (quantity × itemDefinition.weight) for all slots.")]
    [SerializeField] private float maxCarryWeight = 50f;

    // ───────────────────────── Subclass Customization ─────────────────────────

    /// <summary>
    /// Log prefix for debug messages (e.g. "[Inventory]", "[ResourceInventory]").
    /// Override in subclasses for clearer console output.
    /// </summary>
    protected virtual string LogTag => "[BaseInventory]";

    // ───────────────────────── Lifecycle ─────────────────────────

    protected virtual void Awake()
    {
        // NetworkList MUST be created before OnNetworkSpawn.
        Items = new NetworkList<InventoryItem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            MaxWeight.Value = maxCarryWeight;
            Debug.Log($"{LogTag} Server initialized for client {OwnerClientId} " +
                      $"(maxCarryWeight: {maxCarryWeight}, maxSlots: {maxSlots})");
        }

        Items.OnListChanged += OnItemsChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        Items.OnListChanged -= OnItemsChanged;
    }

    // ───────────────────────── Server-Side: Add Item ─────────────────────────

    /// <summary>
    /// Adds items to this inventory. SERVER-ONLY.
    /// Checks weight capacity, stacks onto existing slots, creates new slots.
    /// </summary>
    /// <returns>Number of items actually added (0 = rejected, less = partial).</returns>
    public int AddItem(string itemId, int amount)
    {
        if (!IsServer)
        {
            Debug.LogError($"{LogTag} AddItem called on client!");
            return 0;
        }

        if (amount <= 0) return 0;

        // Look up item definition for stack size and weight
        float itemWeight = 1f;
        int maxStack = int.MaxValue;
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);
        if (itemDef != null)
        {
            maxStack = itemDef.maxStackSize;
            itemWeight = itemDef.weight;
        }

        // ── Weight capacity check ──
        float currentWeight = CurrentWeight.Value;
        float remainingCapacity = maxCarryWeight - currentWeight;

        if (remainingCapacity <= 0f)
        {
            Debug.Log($"{LogTag} Cannot add {amount}x {itemId} — at max weight " +
                      $"({currentWeight:F1}/{maxCarryWeight:F1}).");
            NotifyCapacityClientRpc(itemId, 0, amount);
            return 0;
        }

        int maxByWeight = (itemWeight > 0f)
            ? Mathf.FloorToInt(remainingCapacity / itemWeight)
            : amount;

        if (maxByWeight <= 0)
        {
            Debug.Log($"{LogTag} Cannot add {itemId} — not enough capacity " +
                      $"(need {itemWeight:F1}, have {remainingCapacity:F1}).");
            NotifyCapacityClientRpc(itemId, 0, amount);
            return 0;
        }

        int actualAmount = Mathf.Min(amount, maxByWeight);
        int remaining = actualAmount;

        // First pass: stack onto existing slots
        for (int i = 0; i < Items.Count && remaining > 0; i++)
        {
            InventoryItem slot = Items[i];
            if (slot.ItemId.ToString() == itemId && slot.Quantity < maxStack)
            {
                int spaceInSlot = maxStack - slot.Quantity;
                int toAdd = Mathf.Min(remaining, spaceInSlot);
                slot.Quantity += toAdd;
                Items[i] = slot;
                remaining -= toAdd;
            }
        }

        // Second pass: fill new slots
        while (remaining > 0 && Items.Count < maxSlots)
        {
            int toAdd = Mathf.Min(remaining, maxStack);
            Items.Add(new InventoryItem(itemId, toAdd));
            remaining -= toAdd;
        }

        int actuallyAdded = actualAmount - remaining;
        CurrentWeight.Value += actuallyAdded * itemWeight;

        if (actuallyAdded < amount)
        {
            int rejected = amount - actuallyAdded;
            Debug.Log(actuallyAdded > 0
                ? $"{LogTag} Partially added {actuallyAdded}/{amount}x {itemId} to client {OwnerClientId} " +
                  $"(weight: {CurrentWeight.Value:F1}/{maxCarryWeight:F1}). {rejected}x rejected."
                : $"{LogTag} Could not add {amount}x {itemId} — inventory full!");
            NotifyCapacityClientRpc(itemId, actuallyAdded, amount);
        }
        else
        {
            Debug.Log($"{LogTag} Added {actuallyAdded}x {itemId} to client {OwnerClientId} " +
                      $"(weight: {CurrentWeight.Value:F1}/{maxCarryWeight:F1})");
        }

        return actuallyAdded;
    }

    // ───────────────────────── Server-Side: Remove Item ─────────────────────────

    /// <summary>
    /// Removes items from this inventory. SERVER-ONLY.
    /// Validates sufficient quantity before deducting.
    /// </summary>
    /// <returns>True if the full amount was removed, false if insufficient.</returns>
    public bool RemoveItem(string itemId, int amount)
    {
        if (!IsServer)
        {
            Debug.LogError($"{LogTag} RemoveItem called on client!");
            return false;
        }

        if (amount <= 0) return true;

        int available = GetItemCount(itemId);
        if (available < amount)
        {
            Debug.Log($"{LogTag} Cannot remove {amount}x {itemId} — only {available} available.");
            return false;
        }

        // Look up item weight for updating CurrentWeight
        float itemWeight = 1f;
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);
        if (itemDef != null) itemWeight = itemDef.weight;

        int remaining = amount;
        for (int i = Items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventoryItem slot = Items[i];
            if (slot.ItemId.ToString() != itemId) continue;

            if (slot.Quantity <= remaining)
            {
                remaining -= slot.Quantity;
                Items.RemoveAt(i);
            }
            else
            {
                slot.Quantity -= remaining;
                Items[i] = slot;
                remaining = 0;
            }
        }

        int removed = amount - remaining;
        CurrentWeight.Value -= removed * itemWeight;

        Debug.Log($"{LogTag} Removed {removed}x {itemId} from client {OwnerClientId} " +
                  $"(weight: {CurrentWeight.Value:F1}/{maxCarryWeight:F1})");
        return true;
    }

    // ───────────────────────── ClientRpcs ─────────────────────────

    /// <summary>
    /// Notifies the owning client when items were rejected/partially added.
    /// </summary>
    [ClientRpc]
    private void NotifyCapacityClientRpc(string itemId, int added, int requested)
    {
        if (!IsOwner) return;

        if (added == 0)
        {
            Debug.Log($"{LogTag} Cannot carry any more {itemId} — " +
                      $"at max weight ({CurrentWeight.Value:F1}/{MaxWeight.Value:F1})!");
        }
        else
        {
            Debug.Log($"{LogTag} Only picked up {added}/{requested}x {itemId} — " +
                      $"weight limit reached ({CurrentWeight.Value:F1}/{MaxWeight.Value:F1}).");
        }

        // TODO: Show UI feedback (floating text, weight bar flash, etc.)
    }

    // ───────────────────────── Queries (Any Peer) ─────────────────────────

    /// <summary>
    /// Recalculates total weight from all slots. Runs on ANY peer.
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

    /// <summary>Remaining carry capacity. Runs on ANY peer.</summary>
    public float RemainingCapacity => MaxWeight.Value - CurrentWeight.Value;

    /// <summary>True if at or over weight limit. Runs on ANY peer.</summary>
    public bool IsOverweight => CurrentWeight.Value >= MaxWeight.Value;

    /// <summary>Check if inventory contains at least 'amount'. Runs on ANY peer.</summary>
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

    /// <summary>Total quantity of an item across all slots. Runs on ANY peer.</summary>
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

    // ───────────────────────── Change Callback ─────────────────────────

    /// <summary>
    /// Called on ALL peers when the NetworkList changes. Override for custom logging.
    /// </summary>
    protected virtual void OnItemsChanged(NetworkListEvent<InventoryItem> changeEvent)
    {
        if (IsOwner)
        {
            Debug.Log($"{LogTag} Change — Type: {changeEvent.Type}, " +
                      $"Index: {changeEvent.Index}, Value: {changeEvent.Value} " +
                      $"(weight: {CurrentWeight.Value:F1}/{MaxWeight.Value:F1})");
        }
    }
}
