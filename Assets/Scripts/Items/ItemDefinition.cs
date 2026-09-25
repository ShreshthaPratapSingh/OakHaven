using UnityEngine;

/// <summary>
/// ScriptableObject defining an item's metadata (display name, icon, etc.).
///
/// NETWORKING NOTE:
/// ScriptableObjects are NOT sent over the network. Instead, we send the
/// string itemId and use ItemDatabase to look up the corresponding
/// ItemDefinition on each client for UI display purposes.
///
/// EDITOR SETUP:
/// 1. Right-click in Project → Create → OakHaven → Item Definition
/// 2. Fill in the fields (itemId must match what ResourceNode grants).
/// 3. Add to an ItemDatabaseAsset (see ItemDatabase.cs) or register manually.
/// </summary>
[CreateAssetMenu(fileName = "NewItem", menuName = "OakHaven/Item Definition")]
public class ItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique string ID used in networked data (e.g. 'wood', 'stone', 'fiber'). " +
             "Must match the resourceType on ResourceNode and the InventoryItem.ItemId.")]
    public string itemId;

    [Tooltip("Human-readable name displayed in the UI.")]
    public string displayName;

    [Header("Visuals")]
    [Tooltip("Icon displayed in inventory UI slots.")]
    public Sprite icon;

    [Header("Stacking")]
    [Tooltip("Maximum number of this item per inventory slot.")]
    public int maxStackSize = 99;

    [Header("Classification")]
    [Tooltip("Category for UI filtering/sorting (e.g. 'Resource', 'Tool', 'Consumable').")]
    public string category = "Resource";
}
