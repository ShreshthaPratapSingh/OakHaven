using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Per-player RESOURCE inventory — holds raw gathered materials (Wood, Stone, Fiber).
/// Extends BaseInventory for weight-checked, stack-aware, server-authoritative storage.
///
/// This component lives on the player prefab alongside Inventory (equipment).
/// Each has its OWN independent maxCarryWeight and slot count.
///
/// NETWORKING:
/// ─ Multiple NetworkBehaviours with their own NetworkLists on one NetworkObject
///   is fully supported by Netcode for GameObjects. No special handling needed.
/// ─ ResourceNode.HitServerRpc looks up the hitting player's ResourceInventory
///   to add gathered materials.
///
/// EDITOR SETUP:
/// ─ Add this component to the player prefab (same GameObject as NetworkObject).
/// ─ Set Max Carry Weight in the Inspector (default: 100 — resources are heavy).
/// ─ Set Max Slots in the Inspector (default: 30).
///
/// UI NOTE:
/// ─ Needs its own UI panel/tab separate from the equipment Inventory.
///   Uses the same BaseInventory API (Items, CurrentWeight, MaxWeight).
/// </summary>
public class ResourceInventory : BaseInventory
{
    protected override string LogTag => "[Resources]";

    // ───────────────────────── ServerRpcs ─────────────────────────

    /// <summary>
    /// Client-callable RPC to request adding a resource.
    /// Server validates sender ownership before performing the add.
    /// Primarily used for future features (e.g. picking up dropped resources).
    /// Gathering goes through ResourceNode.HitServerRpc → AddItem() directly.
    /// </summary>
    [ServerRpc]
    public void AddResourceServerRpc(string itemId, int amount, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Resources] Client {rpcParams.Receive.SenderClientId} tried to modify " +
                             $"client {OwnerClientId}'s resources — rejected!");
            return;
        }

        AddItem(itemId, amount);
    }

    /// <summary>
    /// Client-callable RPC to request removing a resource.
    /// Used by crafting/building UI where the client requests consumption.
    /// Server validates sufficient stock before removing.
    /// </summary>
    [ServerRpc]
    public void RemoveResourceServerRpc(string itemId, int amount, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Resources] Client {rpcParams.Receive.SenderClientId} tried to modify " +
                             $"client {OwnerClientId}'s resources — rejected!");
            return;
        }

        RemoveItem(itemId, amount);
    }
}
