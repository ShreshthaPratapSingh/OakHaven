using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Applies the selected character visual to a player on all peers.
/// Lives on the NetworkPlayer prefab root.
///
/// NETWORKING:
/// - The server sets the replicated character id before the player object spawns.
/// - The NetworkVariable syncs to all clients, including late joiners.
/// - Each peer instantiates the corresponding visual prefab locally.
///
/// SETUP:
/// - Assign the same CharacterCatalog asset used by CharacterSelectionManager.
/// - Assign the default Brute visual root (the existing child on NetworkPlayer).
/// - Assign the default Animator if you want fallback animation controller copying.
/// </summary>
public class PlayerCharacterAppearance : NetworkBehaviour
{
    [Header("Catalog")]
    [Tooltip("Catalog of selectable characters.")]
    [SerializeField] private CharacterCatalog catalog;

    [Header("Default Visual")]
    [Tooltip("The existing Brute visual child on the player prefab. Disabled when a selected character is applied.")]
    [SerializeField] private Transform defaultVisualRoot;

    [Tooltip("Default Animator whose controller is copied to character visuals that lack one.")]
    [SerializeField] private Animator defaultAnimator;

    private readonly NetworkVariable<FixedString32Bytes> _characterId = new NetworkVariable<FixedString32Bytes>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Transform _currentVisualRoot;
    private Animator _currentAnimator;

    public string CharacterId => _characterId.Value.ToString();

    /// <summary>
    /// Server-only. Sets the character id before spawning so it is included in the spawn payload.
    /// </summary>
    public void SetCharacterId(string id)
    {
        if (!IsServer)
        {
            Debug.LogWarning("[PlayerCharacterAppearance] SetCharacterId can only be called on the server.");
            return;
        }
        _characterId.Value = new FixedString32Bytes(id);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _characterId.OnValueChanged += OnCharacterIdChanged;
        ApplyCharacter(_characterId.Value.ToString());
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _characterId.OnValueChanged -= OnCharacterIdChanged;
    }

    private void OnCharacterIdChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        ApplyCharacter(current.ToString());
    }

    private void ApplyCharacter(string id)
    {
        if (string.IsNullOrEmpty(id)) return;

        CharacterDefinition def = catalog != null ? catalog.GetCharacter(id) : null;
        if (def == null)
        {
            Debug.LogWarning($"[PlayerCharacterAppearance] Unknown character id '{id}'.");
            return;
        }

        if (def.visualPrefab == null)
        {
            Debug.LogWarning($"[PlayerCharacterAppearance] Character '{id}' has no visual prefab.");
            return;
        }

        // Disable the default Brute visual.
        if (defaultVisualRoot != null)
            defaultVisualRoot.gameObject.SetActive(false);

        // Destroy any previously applied character visual.
        if (_currentVisualRoot != null && _currentVisualRoot != defaultVisualRoot)
            Destroy(_currentVisualRoot.gameObject);

        // Instantiate the selected visual under the player root.
        GameObject visualInstance = Instantiate(def.visualPrefab, transform);
        visualInstance.transform.localPosition = Vector3.zero;
        visualInstance.transform.localRotation = Quaternion.identity;
        visualInstance.transform.localScale = Vector3.one;
        _currentVisualRoot = visualInstance.transform;

        // Resolve Animator on the new visual.
        _currentAnimator = visualInstance.GetComponentInChildren<Animator>();
        if (_currentAnimator != null && defaultAnimator != null && _currentAnimator.runtimeAnimatorController == null)
        {
            _currentAnimator.runtimeAnimatorController = defaultAnimator.runtimeAnimatorController;
            _currentAnimator.avatar = defaultAnimator.avatar;
        }
        if (_currentAnimator == null)
            _currentAnimator = defaultAnimator;

        // Rewire gameplay references that depend on the visible model.
        RewirePlayerReferences();

        Debug.Log($"[PlayerCharacterAppearance] Applied character '{id}' on {(IsServer ? "server" : "client")}.");
    }

    private void RewirePlayerReferences()
    {
        PlayerController controller = GetComponent<PlayerController>();
        if (controller != null)
            controller.SetPlayerModel(_currentVisualRoot);

        PlayerEquipment equipment = GetComponent<PlayerEquipment>();
        if (equipment != null)
        {
            Transform handSocket = FindOrCreateHandSocket(_currentVisualRoot);
            if (handSocket != null)
            {
                equipment.SetHandSocket(handSocket);
                equipment.SetAnimator(_currentAnimator);
            }
            else
            {
                Debug.LogWarning("[PlayerCharacterAppearance] Could not find or create a hand socket on the new visual.");
            }
        }
    }

    private Transform FindOrCreateHandSocket(Transform visualRoot)
    {
        if (visualRoot == null) return null;

        // Prefer an existing socket in the visual prefab.
        Transform existing = visualRoot.Find("HandSocket");
        if (existing != null) return existing;

        foreach (Transform t in visualRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "HandSocket")
                return t;
        }

        // Fall back to creating one under the right-hand bone.
        Transform hand = FindHandBone(visualRoot);
        if (hand != null)
        {
            GameObject socketGo = new GameObject("HandSocket");
            socketGo.transform.SetParent(hand, false);
            socketGo.transform.localPosition = Vector3.zero;
            socketGo.transform.localRotation = Quaternion.identity;
            return socketGo.transform;
        }

        return null;
    }

    private Transform FindHandBone(Transform root)
    {
        Transform exactActive = null;
        Transform exactAny = null;
        Transform partialActive = null;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            string clean = CleanBoneName(t.name);
            bool isRightHand = clean == "righthand";
            bool containsRightHand = clean.Contains("righthand");
            if (!isRightHand && !containsRightHand) continue;

            bool active = t.gameObject.activeInHierarchy;
            if (isRightHand)
            {
                if (active && exactActive == null) exactActive = t;
                if (exactAny == null) exactAny = t;
            }
            else if (containsRightHand)
            {
                if (active && (partialActive == null || t.name.Length < partialActive.name.Length))
                    partialActive = t;
            }
        }

        if (exactActive != null) return exactActive;
        if (exactAny != null) return exactAny;
        return partialActive;
    }

    private static string CleanBoneName(string name)
    {
        return name.Replace("mixamorig:", "")
                   .Replace("_", "")
                   .Replace(" ", "")
                   .ToLowerInvariant();
    }
}
