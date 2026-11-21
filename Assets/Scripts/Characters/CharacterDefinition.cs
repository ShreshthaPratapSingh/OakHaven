using UnityEngine;

/// <summary>
/// Defines one selectable player character.
/// All gameplay stats are identical across characters; this only changes appearance.
/// </summary>
[CreateAssetMenu(fileName = "NewCharacter", menuName = "OakHaven/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    [Tooltip("Unique stable ID used over the network (e.g. 'brute', 'ranger').")]
    public string id;

    [Tooltip("Display name shown in the character selection UI.")]
    public string displayName;

    [Tooltip("Icon shown on the character selection button.")]
    public Sprite icon;

    [Tooltip("Prefab instantiated as the player's visible model. Must use a compatible humanoid rig.")]
    public GameObject visualPrefab;
}
