using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// UI controller for the character selection screen.
/// Lives on the NetworkUI Canvas (or a child panel).
///
/// SETUP:
/// - Assign the main menu panel and the character selection panel.
/// - Assign a container with a GridLayoutGroup and a button prefab.
/// - Assign the same CharacterCatalog and CharacterSelectionManager used by the server.
/// - Wire the RelayManager Host/Join buttons to OnHostClicked / OnJoinClicked.
/// </summary>
public class CharacterSelectionUI : MonoBehaviour
{
    [Header("Panels")]
    [Tooltip("Panel containing Host/Join buttons and the join code input.")]
    [SerializeField] private GameObject mainMenuPanel;

    [Tooltip("Panel where character selection buttons are created.")]
    [SerializeField] private GameObject characterSelectionPanel;

    [Header("Main Menu References")]
    [SerializeField] private TMP_InputField joinCodeInputField;

    [Header("Selection UI")]
    [Tooltip("Parent transform for dynamically created character buttons.")]
    [SerializeField] private Transform characterButtonContainer;

    [Tooltip("Button prefab used for each character. Should have an Image and a TMP_Text child.")]
    [SerializeField] private GameObject characterButtonPrefab;

    [Tooltip("Text shown while waiting for availability (optional).")]
    [SerializeField] private TMP_Text selectionStatusText;

    [Tooltip("Button that returns to the main menu and shuts down networking.")]
    [SerializeField] private Button backButton;

    [Header("Data / Managers")]
    [SerializeField] private CharacterCatalog catalog;
    [SerializeField] private CharacterSelectionManager selectionManager;
    [SerializeField] private RelayManager relayManager;

    private readonly List<GameObject> _characterButtons = new List<GameObject>();

    private void Start()
    {
        ShowMainMenu();

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        if (selectionManager != null)
            selectionManager.OnTakenCharactersChanged += RefreshCharacters;
    }

    private void OnDisable()
    {
        if (selectionManager != null)
            selectionManager.OnTakenCharactersChanged -= RefreshCharacters;
    }

    /// <summary>
    /// Called by the Host button in the main menu.
    /// </summary>
    public void OnHostClicked()
    {
        Debug.Log("[CharacterSelectionUI] Host button clicked.");
        if (relayManager != null)
            relayManager.StartHostFlow();
        else
            Debug.LogError("[CharacterSelectionUI] relayManager is not assigned.");
    }

    /// <summary>
    /// Called by the Join button in the main menu.
    /// </summary>
    public void OnJoinClicked()
    {
        Debug.Log("[CharacterSelectionUI] Join button clicked.");
        if (relayManager != null)
            relayManager.StartClientFlow(joinCodeInputField != null ? joinCodeInputField.text : "");
        else
            Debug.LogError("[CharacterSelectionUI] relayManager is not assigned.");
    }

    /// <summary>
    /// Shows the character selection screen and refreshes availability.
    /// </summary>
    public void ShowCharacterSelection()
    {
        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(false);

        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshCharacters();
    }

    /// <summary>
    /// Returns to the main menu and disconnects if needed.
    /// </summary>
    public void ShowMainMenu()
    {
        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);

        if (mainMenuPanel != null)
            mainMenuPanel.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RefreshCharacters()
    {
        foreach (var btn in _characterButtons)
        {
            if (btn != null) Destroy(btn);
        }
        _characterButtons.Clear();

        if (catalog == null || characterButtonPrefab == null || characterButtonContainer == null)
        {
            SetStatus("Selection UI not fully configured.");
            return;
        }

        foreach (CharacterDefinition def in catalog.characters)
        {
            if (def == null) continue;

            bool available = selectionManager == null || selectionManager.IsCharacterAvailable(def.id);

            GameObject btnObj = Instantiate(characterButtonPrefab, characterButtonContainer);
            _characterButtons.Add(btnObj);

            Button button = btnObj.GetComponent<Button>();
            // Assign the portrait to the dedicated Icon child, not the card background.
            Transform iconTransform = btnObj.transform.Find("Icon");
            Image iconImage = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            TMP_Text label = btnObj.GetComponentInChildren<TMP_Text>();

            if (label != null)
                label.text = def.displayName;

            if (iconImage != null)
            {
                iconImage.enabled = def.icon != null;
                // Crop every source sprite to the icon area's aspect ratio so all
                // cards look identical — no stretching and no uneven letterboxing.
                iconImage.sprite = def.icon != null ? CropSpriteToAspect(def.icon, 0.82f) : null;
                iconImage.preserveAspect = false;
            }

            if (button != null)
            {
                button.interactable = available;
                string capturedId = def.id;
                button.onClick.AddListener(() => OnCharacterSelected(capturedId));
            }
        }

        SetStatus("Choose your character");
    }

    private void OnCharacterSelected(string characterId)
    {
        if (selectionManager != null)
            selectionManager.SelectCharacter(characterId);

        // Hide selection UI so the player can start playing.
        if (characterSelectionPanel != null)
            characterSelectionPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnBackClicked()
    {
        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
        }

        ShowMainMenu();
    }

    private void SetStatus(string message)
    {
        if (selectionStatusText != null)
            selectionStatusText.text = message;
    }

    /// <summary>
    /// Returns a new sprite cropped from the center of the source to the target aspect ratio.
    /// This makes every character card display the same shaped portrait regardless of source image.
    /// </summary>
    private Sprite CropSpriteToAspect(Sprite source, float targetAspect)
    {
        if (source == null || source.texture == null) return null;

        Rect texRect = source.textureRect;
        float sourceAspect = texRect.width / texRect.height;

        float cropWidth, cropHeight, x, y;
        if (sourceAspect > targetAspect)
        {
            // Source is too wide — crop the left/right sides.
            cropHeight = texRect.height;
            cropWidth = cropHeight * targetAspect;
            x = (texRect.width - cropWidth) * 0.5f;
            y = 0f;
        }
        else
        {
            // Source is too tall — crop the top/bottom.
            cropWidth = texRect.width;
            cropHeight = cropWidth / targetAspect;
            x = 0f;
            y = (texRect.height - cropHeight) * 0.5f;
        }

        Rect cropRect = new Rect(texRect.x + x, texRect.y + y, cropWidth, cropHeight);
        return Sprite.Create(source.texture, cropRect, new Vector2(0.5f, 0.5f), source.pixelsPerUnit);
    }
}
