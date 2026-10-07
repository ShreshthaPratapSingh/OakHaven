using System;
using UnityEngine;


/// <summary>
/// A single ingredient entry used in crafting recipes and workstation repair costs.
/// Pairs an item ID with a required quantity.
///
/// NOT networked — this is purely data, serialized in ScriptableObjects.
/// The itemId must match an ItemDefinition.itemId registered in the ItemDatabase.
/// </summary>

[Serializable]
public struct Ingredient
{
    [Tooltip("Item ID matching an ItemDefinition (e.g. 'wood', 'stone', 'fiber'). " +
             "Must be registered in the ItemDatabaseLoader.")]
    public string itemId;

    [Tooltip("How many of this item are required.")]
    public int quantity;
}