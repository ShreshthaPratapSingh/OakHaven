using System;
using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// A single inventory slot that replicates via NetworkList.
///
/// NETWORKING NOTES:
/// ─ NetworkList requires its element type to implement both
///   INetworkSerializable (so NGO knows how to serialize it) and
///   IEquatable (so NGO can detect when a slot actually changed).
/// ─ We use FixedString32Bytes instead of System.String because
///   managed types can't be used inside INetworkSerializable structs.
///   The 32-byte limit is plenty for item IDs like "wood", "stone", etc.
/// ─ This struct is used on ALL peers (server + clients) because
///   NetworkList replicates automatically.
/// </summary>
[Serializable]
public struct InventoryItem : INetworkSerializable, IEquatable<InventoryItem>
{
    /// <summary>
    /// Unique identifier matching an ItemDefinition (e.g. "wood", "stone", "fiber").
    /// FixedString32Bytes is a value type that NGO can serialize directly.
    /// </summary>
    public FixedString32Bytes ItemId;

    /// <summary>
    /// How many of this item the player holds in this slot.
    /// </summary>
    public int Quantity;

    public InventoryItem(string itemId, int quantity)
    {
        ItemId = new FixedString32Bytes(itemId);
        Quantity = quantity;
    }

    // ───────────────────────── INetworkSerializable ─────────────────────────

    /// <summary>
    /// Called by NGO to serialize/deserialize this struct for network transport.
    /// Runs on both server and clients during replication.
    /// </summary>
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref Quantity);
    }

    // ───────────────────────── IEquatable ─────────────────────────

    /// <summary>
    /// Required by NetworkList to detect changes. Two slots are equal if they
    /// have the same item ID and the same quantity.
    /// </summary>
    public bool Equals(InventoryItem other)
    {
        return ItemId.Equals(other.ItemId) && Quantity == other.Quantity;
    }

    public override bool Equals(object obj) => obj is InventoryItem other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(ItemId, Quantity);

    public override string ToString() => $"{ItemId} x{Quantity}";
}
