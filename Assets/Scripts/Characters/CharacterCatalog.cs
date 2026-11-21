using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Catalog of all selectable characters. Create one asset via
/// Assets > Create > OakHaven > Character Catalog and fill it with six definitions.
/// </summary>
[CreateAssetMenu(fileName = "CharacterCatalog", menuName = "OakHaven/Character Catalog")]
public class CharacterCatalog : ScriptableObject
{
    [Tooltip("All selectable characters. IDs must be unique.")]
    public List<CharacterDefinition> characters = new List<CharacterDefinition>();

    public CharacterDefinition GetCharacter(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] != null && characters[i].id == id)
                return characters[i];
        }
        return null;
    }

    public bool HasCharacter(string id) => GetCharacter(id) != null;
}
