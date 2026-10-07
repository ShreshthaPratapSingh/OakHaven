using UnityEngine;

[CreateAssetMenu(fileName = "WorkingStationDefinition", menuName = "Scriptable Objects/WorkingStationDefinition")]
public class WorkingStationDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Unique string ID for this workstation type (e.g. 'workbench', 'forge'). " +
             "Used to match recipes to workstations.")]
    public string workstationType;

    [Tooltip("Human-readable name displayed in the UI.")]
    public string displayName;

    [Header("Repair Costs")]
    [Tooltip("Resources required to repair/build this workstation before it becomes usable. " +
             "Multiple players can contribute resources toward these costs.")]
    public Ingredient[] repairCosts;

    [Header("Available Recipes")]
    [Tooltip("Crafting recipes this workstation can perform once repaired. " +
             "Drag CraftingRecipe ScriptableObject assets here.")]
    public CraftingRecipe[] availableRecipes;
}
