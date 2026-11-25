using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Abstract base class for all per-player inventory systems.
/// Provides server-authoritative weight-checked, stack-aware item storage
/// backed by a replicated NetworkList<InventorySlot>.
///
/// SUBCLASSES:
/// - Inventory (EquipmentInventory): tools, weapons, crafted gear
/// - ResourceInventory: raw gathered materials (Wood, Stone, Fiber)
///
/// Each subclass is its own NetworkBehaviour with its own NetworkList and
/// NetworkVariables. Multiple NetworkBehaviours on one NetworkObject is
/// fully supported by Netcode for GameObjects - no conflicts.
///
/// NETWORKING FLOW:
/// - The NetworkList is owned by the server. Only the server writes to it.
/// - Changes auto-replicate to the owning client (and observers).
/// - Clients subscribe to Items.OnListChanged for UI updates.
///
/// WEIGHT SYSTEM:
/// - Each ItemDefinition has a 'weight' field (per-unit, set in Inspector).
/// - Before adding, the server checks if total weight would exceed maxCarryWeight.
/// - If so, only as many as fit are added (partial add).
/// - CurrentWeight and MaxWeight are NetworkVariables for client UI.
///
/// INTERACTIONS:
/// - Slots are fixed-size. Empty slots are explicit entries with an empty ItemId.
/// - The server validates Move, Swap, Merge, Split and Drop requests.
/// </summary>
public abstract class BaseInventory : NetworkBehaviour
{
    // ------------------------------------------------------------------
    // Networked State
    // ------------------------------------------------------------------

    /// <summary>
    /// The replicated inventory slots. Initialized in Awake().
    /// Always contains exactly <see cref="maxSlots"/> entries.
    /// </summary>
    public NetworkList<InventorySlot> Items { get; private set; }

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

    // ------------------------------------------------------------------
    // Configuration
    // ------------------------------------------------------------------

    [Header("Capacity Settings")]
    [Tooltip("Maximum number of inventory slots.")]
    [SerializeField] private int maxSlots = 20;

    [Tooltip("Maximum total weight this inventory can hold. " +
             "Total weight = sum of (quantity * itemDefinition.weight) for all slots.")]
    [SerializeField] private float maxCarryWeight = 50f;

    [Header("Dropping")]
    [Tooltip("Prefab spawned when the player drops an item from this inventory. " +
             "Item-specific drop prefabs can override this on the ItemDefinition.")]
    [SerializeField] private GameObject defaultDropPrefab;

    // ------------------------------------------------------------------
    // Subclass Customization
    // ------------------------------------------------------------------

    /// <summary>
    /// Log prefix for debug messages (e.g. "[Inventory]", "[ResourceInventory]").
    /// Override in subclasses for clearer console output.
    /// </summary>
    protected virtual string LogTag => "[BaseInventory]";

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    protected virtual void Awake()
    {
        // NetworkList MUST be created before OnNetworkSpawn.
        // Default permissions are Server-write / Everyone-read, which is what we want.
        Items = new NetworkList<InventorySlot>();

        // Fill with empty slots so the list is fixed-size and stable.
        for (int i = 0; i < maxSlots; i++)
            Items.Add(new InventorySlot());
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            var carryCapacity = GetComponent<PlayerCarryCapacity>();
            MaxWeight.Value = carryCapacity != null ? carryCapacity.MaxCarryWeight.Value : maxCarryWeight;
            Debug.Log($"{LogTag} Server initialized for client {OwnerClientId} " +
                      $"(maxCarryWeight: {MaxWeight.Value}, maxSlots: {maxSlots})");
        }

        Items.OnListChanged += OnItemsChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        Items.OnListChanged -= OnItemsChanged;
    }

    // ------------------------------------------------------------------
    // Server-Side: Add Item
    // ------------------------------------------------------------------

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

        var carryCapacity = GetComponent<PlayerCarryCapacity>();
        float effectiveMaxWeight = carryCapacity != null ? carryCapacity.MaxCarryWeight.Value : maxCarryWeight;

        // Look up item definition for stack size and weight
        float itemWeight = 1f;
        int maxStack = int.MaxValue;
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);
        if (itemDef != null)
        {
            maxStack = itemDef.maxStackSize;
            itemWeight = itemDef.weight;
        }

        // Weight capacity check
        float currentWeight = CurrentWeight.Value;
        float otherWeight = carryCapacity != null ? carryCapacity.GetTotalCurrentWeight() - currentWeight : 0f;
        float remainingCapacity = effectiveMaxWeight - otherWeight - currentWeight;

        if (remainingCapacity <= 0f)
        {
            float displayedCurrent = carryCapacity != null ? carryCapacity.GetTotalCurrentWeight() : currentWeight;
            Debug.Log($"{LogTag} Cannot add {amount}x {itemId} - at max weight " +
                      $"({displayedCurrent:F1}/{effectiveMaxWeight:F1}).");
            NotifyCapacityClientRpc(itemId, 0, amount);
            return 0;
        }

        int maxByWeight = (itemWeight > 0f)
            ? Mathf.FloorToInt(remainingCapacity / itemWeight)
            : amount;

        if (maxByWeight <= 0)
        {
            Debug.Log($"{LogTag} Cannot add {itemId} - not enough capacity " +
                      $"(need {itemWeight:F1}, have {remainingCapacity:F1}).");
            NotifyCapacityClientRpc(itemId, 0, amount);
            return 0;
        }

        int actualAmount = Mathf.Min(amount, maxByWeight);
        int remaining = actualAmount;

        // First pass: stack onto existing slots
        for (int i = 0; i < Items.Count && remaining > 0; i++)
        {
            InventorySlot slot = Items[i];
            if (slot.ItemId.ToString() == itemId && slot.Quantity < maxStack)
            {
                int spaceInSlot = maxStack - slot.Quantity;
                int toAdd = Mathf.Min(remaining, spaceInSlot);
                slot.Quantity += toAdd;
                Items[i] = slot;
                remaining -= toAdd;
            }
        }

        // Second pass: fill empty slots
        for (int i = 0; i < Items.Count && remaining > 0; i++)
        {
            if (Items[i].IsEmpty)
            {
                int toAdd = Mathf.Min(remaining, maxStack);
                Items[i] = new InventorySlot(itemId, toAdd);
                remaining -= toAdd;
            }
        }

        int actuallyAdded = actualAmount - remaining;
        CurrentWeight.Value += actuallyAdded * itemWeight;

        if (actuallyAdded < amount)
        {
            int rejected = amount - actuallyAdded;
            float displayedCurrent = carryCapacity != null ? carryCapacity.GetTotalCurrentWeight() : CurrentWeight.Value;
            Debug.Log(actuallyAdded > 0
                ? $"{LogTag} Partially added {actuallyAdded}/{amount}x {itemId} to client {OwnerClientId} " +
                  $"(weight: {displayedCurrent:F1}/{effectiveMaxWeight:F1}). {rejected}x rejected."
                : $"{LogTag} Could not add {amount}x {itemId} - inventory full!");
            NotifyCapacityClientRpc(itemId, actuallyAdded, amount);
        }
        else
        {
            float displayedCurrent = carryCapacity != null ? carryCapacity.GetTotalCurrentWeight() : CurrentWeight.Value;
            Debug.Log($"{LogTag} Added {actuallyAdded}x {itemId} to client {OwnerClientId} " +
                      $"(weight: {displayedCurrent:F1}/{effectiveMaxWeight:F1})");
        }

        return actuallyAdded;
    }

    // ------------------------------------------------------------------
    // Server-Side: Remove Item
    // ------------------------------------------------------------------

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
            Debug.Log($"{LogTag} Cannot remove {amount}x {itemId} - only {available} available.");
            return false;
        }

        float itemWeight = GetItemWeight(itemId);

        int remaining = amount;
        for (int i = Items.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlot slot = Items[i];
            if (slot.ItemId.ToString() != itemId) continue;

            if (slot.Quantity <= remaining)
            {
                remaining -= slot.Quantity;
                Items[i] = new InventorySlot();
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

    // ------------------------------------------------------------------
    // Server-Side: Slot Actions
    // ------------------------------------------------------------------

    /// <summary>
    /// Moves a stack from one slot to another empty slot, swaps with another
    /// stack, or merges with a matching stack. SERVER-ONLY.
    /// </summary>
    [ServerRpc]
    public void MoveOrMergeSlotServerRpc(int fromIndex, int toIndex, ServerRpcParams rpcParams = default)
    {
        if (!ValidateRequest(rpcParams)) return;
        if (!IsValidSlot(fromIndex) || !IsValidSlot(toIndex)) return;
        if (fromIndex == toIndex) return;

        InventorySlot from = Items[fromIndex];
        if (from.IsEmpty) return;

        InventorySlot to = Items[toIndex];

        if (to.IsEmpty)
        {
            // Move into empty slot
            Items[toIndex] = from;
            Items[fromIndex] = new InventorySlot();
        }
        else if (to.ItemId.ToString() == from.ItemId.ToString())
        {
            // Merge
            ItemDefinition def = ItemDatabase.GetItem(from.ItemId.ToString());
            int maxStack = def != null ? def.maxStackSize : int.MaxValue;
            int space = maxStack - to.Quantity;
            if (space <= 0) return;

            int move = Mathf.Min(from.Quantity, space);
            Items[toIndex] = new InventorySlot(to.ItemId.ToString(), to.Quantity + move);
            if (move >= from.Quantity)
                Items[fromIndex] = new InventorySlot();
            else
                Items[fromIndex] = new InventorySlot(from.ItemId.ToString(), from.Quantity - move);
        }
        else
        {
            // Swap
            Items[toIndex] = from;
            Items[fromIndex] = to;
        }
    }

    /// <summary>
    /// Splits a stack, moving half (or the requested amount) into the first empty slot.
    /// SERVER-ONLY.
    /// </summary>
    [ServerRpc]
    public void SplitSlotServerRpc(int fromIndex, int amount, ServerRpcParams rpcParams = default)
    {
        if (!ValidateRequest(rpcParams)) return;
        if (!IsValidSlot(fromIndex)) return;

        InventorySlot from = Items[fromIndex];
        if (from.IsEmpty || from.Quantity <= 1) return;

        int splitAmount = amount <= 0 ? from.Quantity / 2 : Mathf.Clamp(amount, 1, from.Quantity - 1);
        if (splitAmount <= 0) return;

        // Find first empty slot
        int emptyIndex = -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].IsEmpty)
            {
                emptyIndex = i;
                break;
            }
        }

        if (emptyIndex < 0)
        {
            Debug.Log($"{LogTag} Cannot split - no empty slots.");
            return;
        }

        Items[emptyIndex] = new InventorySlot(from.ItemId.ToString(), splitAmount);
        Items[fromIndex] = new InventorySlot(from.ItemId.ToString(), from.Quantity - splitAmount);
    }

    /// <summary>
    /// Drops the requested quantity from a slot into the world. SERVER-ONLY.
    /// </summary>
    [ServerRpc]
    public void DropSlotServerRpc(int fromIndex, int amount, ServerRpcParams rpcParams = default)
    {
        if (!ValidateRequest(rpcParams)) return;
        if (!IsValidSlot(fromIndex)) return;

        InventorySlot slot = Items[fromIndex];
        if (slot.IsEmpty) return;

        int dropAmount = Mathf.Clamp(amount, 1, slot.Quantity);
        string itemId = slot.ItemId.ToString();
        float itemWeight = GetItemWeight(itemId);

        // Remove from slot
        if (dropAmount >= slot.Quantity)
            Items[fromIndex] = new InventorySlot();
        else
            Items[fromIndex] = new InventorySlot(itemId, slot.Quantity - dropAmount);

        CurrentWeight.Value -= dropAmount * itemWeight;

        // Spawn the world drop
        SpawnWorldDrop(itemId, dropAmount);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private bool ValidateRequest(ServerRpcParams rpcParams)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"{LogTag} Client {rpcParams.Receive.SenderClientId} tried to modify " +
                             $"client {OwnerClientId}'s inventory - rejected!");
            return false;
        }
        return true;
    }

    private bool IsValidSlot(int index) => index >= 0 && index < maxSlots;

    private float GetItemWeight(string itemId)
    {
        ItemDefinition def = ItemDatabase.GetItem(itemId);
        return def != null ? def.weight : 1f;
    }

    private void SpawnWorldDrop(string itemId, int quantity)
    {
        if (!IsServer) return;

        ItemDefinition def = ItemDatabase.GetItem(itemId);
        GameObject prefab = def != null && def.dropPrefab != null ? def.dropPrefab : defaultDropPrefab;

        // Item-specific visual prefabs that are also static world pickups must not be
        // used for inventory drops — they contain a WorldPickup that grants a fixed
        // amount and despawns the object, losing the rest of the stack.
        if (prefab != null && prefab.GetComponent<WorldPickup>() != null)
        {
            Debug.LogWarning($"{LogTag} Drop prefab for '{itemId}' is a static WorldPickup; falling back to default drop prefab.");
            prefab = defaultDropPrefab;
        }

        if (prefab == null)
        {
            Debug.LogError($"{LogTag} Cannot drop '{itemId}' - no drop prefab assigned. " +
                           "Run Tools > OakHaven > Create World Item Drop Prefab and assign it to this inventory.");
            return;
        }

        // Validate the prefab before removing items from inventory.
        NetworkObject dropNetObj = prefab.GetComponent<NetworkObject>();
        WorldItemDrop dropBehaviour = prefab.GetComponent<WorldItemDrop>();
        if (dropNetObj == null || dropBehaviour == null)
        {
            Debug.LogError($"{LogTag} Drop prefab for '{itemId}' must have both NetworkObject and WorldItemDrop components.");
            return;
        }

        NetworkObject playerObj = NetworkManager.Singleton.ConnectedClients.TryGetValue(OwnerClientId, out var client)
            ? client.PlayerObject
            : null;

        Vector3 spawnPos = playerObj != null
            ? playerObj.transform.position + playerObj.transform.forward * 0.5f + Vector3.up * 0.5f
            : transform.position + Vector3.up * 0.5f;

        Debug.Log($"[BaseInventory] Spawning world drop for {itemId} x{quantity} at {spawnPos} using prefab '{prefab.name}'");
        GameObject dropInstance = Instantiate(prefab, spawnPos, Quaternion.identity);
        dropNetObj = dropInstance.GetComponent<NetworkObject>();
        dropBehaviour = dropInstance.GetComponent<WorldItemDrop>();

        EnsureDropPhysics(dropInstance);

        dropNetObj.Spawn(true);
        dropBehaviour.Initialize(itemId, quantity);
    }

    /// <summary>
    /// Ensures the drop has physics so it lands on the ground. Missing physics
    /// components are added as a safety net, but NetworkObject/WorldItemDrop must
    /// already be present in the prefab to keep network behaviour consistent.
    /// </summary>
    private static void EnsureDropPhysics(GameObject dropInstance)
    {
        Rigidbody rb = dropInstance.GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = dropInstance.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.isKinematic = false;
            rb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        if (dropInstance.GetComponent<Collider>() == null)
        {
            BoxCollider col = dropInstance.AddComponent<BoxCollider>();
            col.size = Vector3.one * 0.3f;
            col.center = new Vector3(0f, 0.15f, 0f);
        }
    }

    // ------------------------------------------------------------------
    // ClientRpcs
    // ------------------------------------------------------------------

    /// <summary>
    /// Notifies the owning client when items were rejected/partially added.
    /// </summary>
    [ClientRpc]
    private void NotifyCapacityClientRpc(string itemId, int added, int requested)
    {
        if (!IsOwner) return;

        var carryCapacity = GetComponent<PlayerCarryCapacity>();
        float current = carryCapacity != null ? carryCapacity.GetTotalCurrentWeight() : CurrentWeight.Value;
        float max = carryCapacity != null ? carryCapacity.MaxCarryWeight.Value : MaxWeight.Value;

        if (added == 0)
        {
            Debug.Log($"{LogTag} Cannot carry any more {itemId} - " +
                      $"at max weight ({current:F1}/{max:F1})!");
        }
        else
        {
            Debug.Log($"{LogTag} Only picked up {added}/{requested}x {itemId} - " +
                      $"weight limit reached ({current:F1}/{max:F1}).");
        }

        // TODO: Show UI feedback (floating text, weight bar flash, etc.)
    }

    // ------------------------------------------------------------------
    // Queries (Any Peer)
    // ------------------------------------------------------------------

    /// <summary>
    /// Recalculates total weight from all slots. Runs on ANY peer.
    /// </summary>
    public float CalculateTotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].IsEmpty) continue;
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
                total += Items[i].Quantity;
        }
        return total;
    }

    // ------------------------------------------------------------------
    // Change Callback
    // ------------------------------------------------------------------

    /// <summary>
    /// Called on ALL peers when the NetworkList changes. Override for custom logging.
    /// </summary>
    protected virtual void OnItemsChanged(NetworkListEvent<InventorySlot> changeEvent)
    {
        if (IsOwner)
        {
            Debug.Log($"{LogTag} Change - Type: {changeEvent.Type}, " +
                      $"Index: {changeEvent.Index}, Value: {changeEvent.Value} " +
                      $"(weight: {CurrentWeight.Value:F1}/{MaxWeight.Value:F1})");
        }
    }
}
