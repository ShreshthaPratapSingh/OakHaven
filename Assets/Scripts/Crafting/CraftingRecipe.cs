using UnityEngine;

/// <summary>
/// ScriptableObject defining a single crafting recipe.
/// Maps a list of ingredients (resources consumed) to an output item produced.
///
/// NETWORKING NOTE:
/// Like ItemDefinition, this is local data — never sent over the network.
/// The server looks up recipes by recipeId string when processing craft requests.
/// Clients use it for UI display (showing ingredients, icons, etc.).
///
/// EDITOR SETUP:
/// 1. Right-click in Project → Create → OakHaven → Crafting Recipe
/// 2. Fill in the fields. All itemIds must match registered ItemDefinitions.
/// 3. Add the recipe to a WorkstationDefinition's availableRecipes array.
/// </summary>

[CreateAssetMenu(fileName = "CraftingReciipe", menuName = "Scriptable Objects/CraftingReciipe")]
public class CraftingRecipe : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique string ID for this recipe (e.g. 'recipe_stone_axe'). " +
             "Used to identify the recipe in ServerRpcs.")]
    public string recipeId;

    [Tooltip("Human-readable name displayed in the crafting UI.")]
    public string displayName;

    [Header("Ingredients")]
    [Tooltip("Resources consumed when crafting. Each entry's itemId must match " +
             "an ItemDefinition registered in the ItemDatabase.")]
    public Ingredient[] ingredients;

    [Header("Output")]
    [Tooltip("The itemId of the item produced (must match an ItemDefinition). " +
             "For tools/equipment, this goes into the player's Equipment inventory.")]
    public string outputItemId;

    [Tooltip("How many of the output item are produced per craft (usually 1 for tools).")]
    public int outputQuantity;

    [Header("Crafting Time")]
    [Tooltip("Seconds to complete the craft. 0 = instant. " +
             "If > 0, the workstation will show a progress bar and block other crafts.")]
    public float craftTimeSec = 0f;
}
