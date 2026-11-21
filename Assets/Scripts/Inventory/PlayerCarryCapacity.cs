using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Shared carry-capacity manager for a player.
///
/// The player has two inventories — <see cref="Inventory"/> (equipment) and
/// <see cref="ResourceInventory"/> (resources) — but they must share one combined
/// weight limit. This component owns that shared limit and exposes the total
/// current weight across both inventories.
///
/// SETUP:
/// 1. Add this component to the player prefab root alongside Inventory and
///    ResourceInventory.
/// 2. Set <see cref="maxCarryWeight"/> to the desired combined limit (default 100).
/// 3. BaseInventory will automatically use this shared limit when present.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerCarryCapacity : NetworkBehaviour
{
    [Header("Capacity")]
    [Tooltip("Combined maximum carry weight across equipment and resource inventories.")]
    [SerializeField] private float maxCarryWeight = 100f;

    /// <summary>
    /// Replicated shared carry limit. Server-write, everyone-read.
    /// </summary>
    public NetworkVariable<float> MaxCarryWeight = new NetworkVariable<float>(
        100f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Inventory _inventory;
    private ResourceInventory _resourceInventory;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _inventory = GetComponent<Inventory>();
        _resourceInventory = GetComponent<ResourceInventory>();

        if (IsServer)
        {
            MaxCarryWeight.Value = maxCarryWeight;
        }
    }

    /// <summary>
    /// Total current weight across both inventories.
    /// </summary>
    public float GetTotalCurrentWeight()
    {
        float invWeight = _inventory != null ? _inventory.CurrentWeight.Value : 0f;
        float resWeight = _resourceInventory != null ? _resourceInventory.CurrentWeight.Value : 0f;
        return invWeight + resWeight;
    }

    /// <summary>
    /// Remaining weight capacity before the player is fully encumbered.
    /// </summary>
    public float RemainingCapacity => MaxCarryWeight.Value - GetTotalCurrentWeight();
}
