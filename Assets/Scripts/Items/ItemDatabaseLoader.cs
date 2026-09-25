using UnityEngine;

/// <summary>
/// MonoBehaviour that loads ItemDefinition ScriptableObjects into the
/// static ItemDatabase on scene start.
///
/// EDITOR SETUP:
/// 1. Create an empty GameObject in your scene (or use an existing one like NetworkManager).
/// 2. Add this component.
/// 3. Drag all your ItemDefinition assets into the "Item Definitions" array.
/// 4. This must exist in every scene that uses the inventory system.
/// </summary>
public class ItemDatabaseLoader : MonoBehaviour
{
    [Header("Drag all ItemDefinition assets here")]
    [SerializeField] private ItemDefinition[] itemDefinitions;

    private void Awake()
    {
        // Clear any stale data from a previous scene load
        ItemDatabase.Clear();

        if (itemDefinitions == null || itemDefinitions.Length == 0)
        {
            Debug.LogWarning("[ItemDatabaseLoader] No item definitions assigned! " +
                             "Drag your ItemDefinition ScriptableObjects into this component.");
            return;
        }

        foreach (var item in itemDefinitions)
        {
            ItemDatabase.Register(item);
        }

        ItemDatabase.MarkInitialized();
    }
}
