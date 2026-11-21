using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A single inventory slot that replicates via NetworkList.
///
/// Unlike <see cref="InventoryItem"/> (used by workstation repair contributions),
/// this struct represents a fixed player-inventory slot and can be empty.
/// Empty slots have an empty <see cref="ItemId"/> and a <see cref="Quantity"/> of 0.
///
/// NETWORKING NOTES:
/// - NetworkList requires INetworkSerializable and IEquatable.
/// - FixedString32Bytes is used instead of System.String for NGO compatibility.
/// </summary>
[Serializable]
public struct InventorySlot : INetworkSerializable, IEquatable<InventorySlot>
{
    public FixedString32Bytes ItemId;
    public int Quantity;

    public InventorySlot(string itemId, int quantity)
    {
        ItemId = new FixedString32Bytes(itemId);
        Quantity = quantity;
    }

    /// <summary>
    /// True when this slot holds no items.
    /// </summary>
    public bool IsEmpty => ItemId.IsEmpty || Quantity <= 0;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ItemId);
        serializer.SerializeValue(ref Quantity);
    }

    public bool Equals(InventorySlot other)
    {
        return ItemId.Equals(other.ItemId) && Quantity == other.Quantity;
    }

    public override bool Equals(object obj) => obj is InventorySlot other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ItemId, Quantity);
    public override string ToString() => IsEmpty ? "Empty" : $"{ItemId} x{Quantity}";
}
