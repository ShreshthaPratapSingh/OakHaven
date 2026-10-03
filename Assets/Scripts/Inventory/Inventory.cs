using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player EQUIPMENT inventory — holds tools, weapons, and crafted gear.
/// Extends BaseInventory for weight-checked, stack-aware, server-authoritative storage.
///
/// This component lives on the player prefab alongside ResourceInventory.
/// Each has its OWN independent maxCarryWeight and slot count.
///
/// EDITOR SETUP:
/// ─ Add this component to the player prefab (same GameObject as NetworkObject).
/// ─ Set Max Carry Weight in the Inspector (default: 50).
/// ─ Set Max Slots in the Inspector (default: 20).
///
/// UI NOTE:
/// ─ The player has TWO inventories the UI reads from:
///   1. This Inventory (equipment) — InventoryUI currently reads from this
///   2. ResourceInventory (raw materials) — needs its own UI panel/tab
/// </summary>
public class Inventory : BaseInventory
{
    protected override string LogTag => "[Equipment]";

    // ───────────────────────── ServerRpcs ─────────────────────────

    /// <summary>
    /// Client-callable RPC to request adding an equipment item.
    /// Server validates sender ownership before performing the add.
    /// </summary>
    [ServerRpc]
    public void AddItemServerRpc(string itemId, int amount, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Equipment] Client {rpcParams.Receive.SenderClientId} tried to modify " +
                             $"client {OwnerClientId}'s equipment — rejected!");
            return;
        }

        AddItem(itemId, amount);
    }
}
