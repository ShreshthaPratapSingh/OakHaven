using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static registry that maps item ID strings → ItemDefinition ScriptableObjects.
///
/// NETWORKING NOTE:
/// This database is purely client-side / local. It exists so that when the
/// Inventory replicates an InventoryItem with ItemId = "wood", the UI can
/// look up the display name, icon, and stack size without sending
/// ScriptableObject references over the network.
///
/// The server also uses it for stack-size validation in Inventory.AddItem().
///
/// HOW IT WORKS:
/// ─ ItemDatabaseLoader (below) is a MonoBehaviour you place on a GameObject
///   in your scene (e.g. on the NetworkManager or a "GameSystems" object).
/// ─ In the Inspector, drag all your ItemDefinition assets into its array.
/// ─ On Awake(), it registers them into the static dictionary.
/// ─ Any script can then call ItemDatabase.GetItem("wood") from anywhere.
/// </summary>
public static class ItemDatabase
{
    private static readonly Dictionary<string, ItemDefinition> _items = new Dictionary<string, ItemDefinition>();
    private static bool _initialized = false;

    /// <summary>
    /// Register an ItemDefinition. Called by ItemDatabaseLoader on Awake().
    /// Safe to call multiple times with the same item (idempotent).
    /// </summary>
    public static void Register(ItemDefinition item)
    {
        if (item == null || string.IsNullOrEmpty(item.itemId))
        {
            Debug.LogWarning("[ItemDatabase] Tried to register a null or unnamed item — skipping.");
            return;
        }

        _items[item.itemId] = item;
    }

    /// <summary>
    /// Look up an ItemDefinition by its string ID.
    /// Returns null if the ID isn't registered (log a warning).
    /// Runs on ALL peers (server + clients).
    /// </summary>
    public static ItemDefinition GetItem(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return null;

        if (_items.TryGetValue(itemId, out ItemDefinition item))
        {
            return item;
        }

        // Don't spam warnings — this can happen briefly during initialization
        if (_initialized)
        {
            Debug.LogWarning($"[ItemDatabase] Item '{itemId}' not found in database. " +
                             "Did you add it to the ItemDatabaseLoader in the scene?");
        }
        return null;
    }

    /// <summary>
    /// Returns all registered item definitions. Useful for UI that needs
    /// to display a full item catalog.
    /// </summary>
    public static IEnumerable<ItemDefinition> GetAllItems() => _items.Values;

    /// <summary>
    /// Called by ItemDatabaseLoader after all items are registered.
    /// Enables "item not found" warnings.
    /// </summary>
    public static void MarkInitialized()
    {
        _initialized = true;
        Debug.Log($"[ItemDatabase] Initialized with {_items.Count} item(s).");
    }

    /// <summary>
    /// Clears the database. Useful for scene reloads / testing.
    /// </summary>
    public static void Clear()
    {
        _items.Clear();
        _initialized = false;
    }
}
