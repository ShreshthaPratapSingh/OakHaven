using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Grants a configured set of items to a player when they first spawn.
///
/// NETWORKING:
/// ─ Only the server grants items.
/// ─ Items are added to either ResourceInventory (raw materials) or the equipment
///   Inventory based on the <see cref="StartingItem.isResource"/> flag.
/// ─ The grant happens one frame after OnNetworkSpawn so BaseInventory has
///   initialized its NetworkList and MaxWeight.
///
/// EDITOR SETUP:
/// 1. Add this component to the NetworkPlayer root prefab.
/// 2. Configure the Starting Items array in the Inspector.
/// 3. Make sure the scene has an ItemDatabaseLoader and the item IDs exist.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PlayerStartingLoadout : NetworkBehaviour
{
    [Serializable]
    public class StartingItem
    {
        [Tooltip("Item ID from the ItemDefinition (e.g. 'stone', 'wood', 'fiber', 'stone_axe').")]
        public string itemId;

        [Tooltip("How many of this item to grant.")]
        public int amount = 1;

        [Tooltip("If true, the item goes into ResourceInventory. If false, it goes into the equipment Inventory.")]
        public bool isResource = true;
    }

    [SerializeField] private StartingItem[] startingItems;

    private bool _granted;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer || _granted) return;

        // Wait one frame so BaseInventory.Awake/OnNetworkSpawn have run.
        StartCoroutine(GrantNextFrame());
    }

    private IEnumerator GrantNextFrame()
    {
        yield return null;
        Grant();
    }

    private void Grant()
    {
        if (_granted) return;
        _granted = true;

        if (startingItems == null || startingItems.Length == 0) return;

        Inventory equipmentInv = GetComponent<Inventory>();
        ResourceInventory resourceInv = GetComponent<ResourceInventory>();

        foreach (StartingItem entry in startingItems)
        {
            if (string.IsNullOrEmpty(entry.itemId) || entry.amount <= 0)
                continue;

            if (ItemDatabase.GetItem(entry.itemId) == null)
            {
                Debug.LogWarning($"[PlayerStartingLoadout] Unknown item id '{entry.itemId}' — skipping.");
                continue;
            }

            int added;
            string inventoryName;

            if (entry.isResource)
            {
                if (resourceInv == null)
                {
                    Debug.LogError("[PlayerStartingLoadout] No ResourceInventory found on player.");
                    continue;
                }
                added = resourceInv.AddItem(entry.itemId, entry.amount);
                inventoryName = "resource";
            }
            else
            {
                if (equipmentInv == null)
                {
                    Debug.LogError("[PlayerStartingLoadout] No equipment Inventory found on player.");
                    continue;
                }
                added = equipmentInv.AddItem(entry.itemId, entry.amount);
                inventoryName = "equipment";
            }

            Debug.Log($"[PlayerStartingLoadout] Granted {added}/{entry.amount}x '{entry.itemId}' " +
                      $"to client {OwnerClientId} ({inventoryName}).");
        }
    }
}
