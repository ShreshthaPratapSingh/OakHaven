using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// Client-side UI that displays the local player's inventory.
/// Subscribes to the Inventory's NetworkList.OnListChanged event and
/// rebuilds displayed slots whenever the server replicates a change.
///
/// NETWORKING RULES:
/// ─ This script NEVER modifies the NetworkList directly.
/// ─ It is purely a READER / DISPLAY layer.
/// ─ Any player actions (drop, use, etc.) trigger ServerRpcs on the Inventory.
/// ─ The UI rebuilds automatically when the server makes changes.
///
/// EDITOR SETUP:
/// 1. Add this component to a Canvas in your scene.
/// 2. Assign the "Inventory Panel" field to a child Panel inside the Canvas.
///    The panel MUST contain a child with a GridLayoutGroup component —
///    the script finds it automatically at runtime (no manual wiring needed).
/// 3. Assign the "Slot Prefab" field to your InventorySlot prefab from the Project window.
///
/// SLOT PREFAB STRUCTURE:
/// InventorySlot (GameObject + Image for background)
///   ├── Icon (child GameObject + Image component for the item sprite)
///   └── QuantityText (child GameObject + TMP_Text component for "x5")
/// </summary>
public class InventoryUI : MonoBehaviour
{
    // ───────────────────────── Inspector References ─────────────────────────

    [Header("UI References")]
    [Tooltip("The inventory panel root. Toggled on/off with the inventory key. " +
             "Must contain a child with a GridLayoutGroup (auto-discovered as slot container).")]
    [SerializeField] private GameObject inventoryPanel;

    [Tooltip("Prefab for a single inventory slot (drag from the Project window).")]
    [SerializeField] private GameObject slotPrefab;

    [Header("Toggle Settings")]
    [Tooltip("Key to toggle the inventory panel on/off.")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;

    [Header("Empty Slot")]
    [Tooltip("Sprite to show in empty/unused slots. Leave null for no empty slots.")]
    [SerializeField] private Sprite emptySlotSprite;

    [Tooltip("Total number of visual slots to display (includes empty ones).")]
    [SerializeField] private int displaySlotCount = 20;

    // ───────────────────────── Private State ─────────────────────────

    private Inventory _localInventory;
    private bool _isSubscribed;
    private Transform _slotContainer; // Auto-discovered at runtime from inventoryPanel
    private readonly List<GameObject> _slotInstances = new List<GameObject>();

    // ───────────────────────── Lifecycle ─────────────────────────

    private void Start()
    {
        // Start with inventory hidden
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);

            // Auto-discover the slot container: find the first child with a GridLayoutGroup
            _slotContainer = FindSlotContainer();
            if (_slotContainer == null)
            {
                Debug.LogError("[InventoryUI] Could not find a child with GridLayoutGroup " +
                               "inside the inventoryPanel! Add a GridLayoutGroup to the " +
                               "container where slots should appear.");
            }
            else
            {
                Debug.Log($"[InventoryUI] Auto-discovered slot container: '{_slotContainer.name}'");
            }
        }
        else
        {
            Debug.LogError("[InventoryUI] inventoryPanel is not assigned! " +
                           "Drag your InventoryPanel from the Hierarchy into this field.");
        }
    }

    private void Update()
    {
        // Try to find the local player's inventory if we haven't yet
        if (_localInventory == null)
        {
            TryFindLocalInventory();
        }

        // Toggle inventory visibility
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleInventory();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromInventory();
    }

    // ───────────────────────── Auto-Discovery ─────────────────────────

    /// <summary>
    /// Searches the inventoryPanel's children (recursively) for the first
    /// GameObject with a GridLayoutGroup component. This is where slot
    /// prefabs will be instantiated as children.
    /// </summary>
    private Transform FindSlotContainer()
    {
        if (inventoryPanel == null) return null;

        // Check the panel itself first
        var grid = inventoryPanel.GetComponent<GridLayoutGroup>();
        if (grid != null) return inventoryPanel.transform;

        // Search children recursively
        var grids = inventoryPanel.GetComponentsInChildren<GridLayoutGroup>(true);
        if (grids.Length > 0) return grids[0].transform;

        return null;
    }

    // ───────────────────────── Inventory Discovery ─────────────────────────

    /// <summary>
    /// Finds the local player's Inventory component.
    /// Called each frame until found (the player may not have spawned yet).
    /// This approach is necessary because the InventoryUI lives on a scene
    /// Canvas while the Inventory lives on the dynamically-spawned player prefab.
    /// </summary>
    private void TryFindLocalInventory()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;
        if (NetworkManager.Singleton.LocalClient?.PlayerObject == null) return;

        var inventory = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<Inventory>();
        if (inventory == null) return;

        _localInventory = inventory;
        SubscribeToInventory();
        RebuildUI(); // Initial build with current state

        Debug.Log("[InventoryUI] Found local player's inventory — UI ready.");
    }

    // ───────────────────────── Subscription Management ─────────────────────────

    private void SubscribeToInventory()
    {
        if (_isSubscribed || _localInventory == null) return;

        _localInventory.Items.OnListChanged += OnInventoryChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeFromInventory()
    {
        if (!_isSubscribed || _localInventory == null) return;

        _localInventory.Items.OnListChanged -= OnInventoryChanged;
        _isSubscribed = false;
    }

    // ───────────────────────── Event Handler ─────────────────────────

    /// <summary>
    /// Called whenever the NetworkList changes (add, remove, value update).
    /// Triggered by NGO's replication — we never call this ourselves.
    /// Simply rebuilds the entire UI from the current list state.
    ///
    /// NOTE: For large inventories, you'd want incremental updates based on
    /// changeEvent.Type and changeEvent.Index. For a survival game with ~20
    /// slots, a full rebuild is fine and much simpler to maintain.
    /// </summary>
    private void OnInventoryChanged(NetworkListEvent<InventoryItem> changeEvent)
    {
        RebuildUI();
    }

    // ───────────────────────── UI Building ─────────────────────────

    /// <summary>
    /// Destroys all existing slot instances and rebuilds from the current
    /// NetworkList state. Called on subscription and on every list change.
    /// </summary>
    private void RebuildUI()
    {
        if (_slotContainer == null || slotPrefab == null || _localInventory == null) return;

        // Clear existing slots
        foreach (var slot in _slotInstances)
        {
            if (slot != null) Destroy(slot);
        }
        _slotInstances.Clear();

        // Build occupied slots from inventory data
        for (int i = 0; i < _localInventory.Items.Count; i++)
        {
            InventoryItem item = _localInventory.Items[i];
            CreateSlot(item);
        }

        // Fill remaining slots with empty placeholders
        int emptyCount = displaySlotCount - _localInventory.Items.Count;
        for (int i = 0; i < emptyCount; i++)
        {
            CreateEmptySlot();
        }
    }

    /// <summary>
    /// Creates a single populated slot from inventory data.
    /// Looks up the ItemDefinition for display name and icon.
    /// </summary>
    private void CreateSlot(InventoryItem item)
    {
        GameObject slotObj = Instantiate(slotPrefab, _slotContainer);
        _slotInstances.Add(slotObj);

        // Ensure proper layout on the Icon and QuantityText children
        ConfigureSlotLayout(slotObj);

        // Apply the slot background sprite to the root Image
        Image bgImage = slotObj.GetComponent<Image>();
        if (bgImage != null && emptySlotSprite != null)
        {
            bgImage.sprite = emptySlotSprite;
            bgImage.color = Color.white;
        }

        // Find child components by name
        Image iconImage = FindChildComponent<Image>(slotObj, "Icon");
        TMP_Text quantityText = FindChildComponent<TMP_Text>(slotObj, "QuantityText");

        // Look up item definition for icon and display info
        string itemId = item.ItemId.ToString();
        ItemDefinition itemDef = ItemDatabase.GetItem(itemId);

        if (iconImage != null)
        {
            if (itemDef != null && itemDef.icon != null)
            {
                iconImage.sprite = itemDef.icon;
                iconImage.color = Color.white;
            }
            else
            {
                // No icon available — show a colored placeholder
                iconImage.sprite = null;
                iconImage.color = new Color(0.6f, 0.8f, 0.5f, 0.8f);
            }
        }

        if (quantityText != null)
        {
            quantityText.text = item.Quantity > 1 ? $"x{item.Quantity}" : "";
        }

        slotObj.name = $"Slot_{itemId}_{item.Quantity}";
    }

    /// <summary>
    /// Creates an empty slot placeholder.
    /// </summary>
    private void CreateEmptySlot()
    {
        GameObject slotObj = Instantiate(slotPrefab, _slotContainer);
        _slotInstances.Add(slotObj);

        ConfigureSlotLayout(slotObj);

        // Apply the slot background sprite to the root Image
        Image bgImage = slotObj.GetComponent<Image>();
        if (bgImage != null && emptySlotSprite != null)
        {
            bgImage.sprite = emptySlotSprite;
            bgImage.color = Color.white;
        }

        // Hide the icon on empty slots
        Image iconImage = FindChildComponent<Image>(slotObj, "Icon");
        if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.color = Color.clear; // Fully transparent — no item to show
        }

        TMP_Text quantityText = FindChildComponent<TMP_Text>(slotObj, "QuantityText");
        if (quantityText != null)
        {
            quantityText.text = "";
        }

        slotObj.name = "Slot_Empty";
    }

    /// <summary>
    /// Programmatically configures the RectTransform anchoring for slot children
    /// so the prefab doesn't need manual anchor setup in the Inspector.
    /// ─ Icon: stretches to fill the slot with 4px padding on all sides.
    /// ─ QuantityText: anchored to bottom-right corner.
    /// </summary>
    private void ConfigureSlotLayout(GameObject slotObj)
    {
        // ── Configure Icon: stretch to fill slot with padding ──
        Transform iconTransform = FindChildRecursive(slotObj.transform, "Icon");
        if (iconTransform != null)
        {
            RectTransform iconRect = iconTransform.GetComponent<RectTransform>();
            if (iconRect != null)
            {
                // Stretch to fill parent (anchors at all four corners)
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                // 4px padding on all sides
                iconRect.offsetMin = new Vector2(4f, 4f);   // left, bottom
                iconRect.offsetMax = new Vector2(-4f, -4f);  // right, top (negative = inward)
            }
        }

        // ── Configure QuantityText: anchor to bottom-right ──
        Transform textTransform = FindChildRecursive(slotObj.transform, "QuantityText");
        if (textTransform != null)
        {
            RectTransform textRect = textTransform.GetComponent<RectTransform>();
            if (textRect != null)
            {
                // Anchor to bottom-right corner
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 0.4f);
                textRect.offsetMin = new Vector2(2f, 0f);
                textRect.offsetMax = new Vector2(-2f, 0f);
            }

            // Configure text styling
            TMP_Text tmp = textTransform.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.fontSize = 14;
                tmp.alignment = TextAlignmentOptions.BottomRight;
                tmp.color = Color.white;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
            }
        }
    }


    // ───────────────────────── Toggle ─────────────────────────

    /// <summary>
    /// Toggles the inventory panel visibility and cursor state.
    /// When inventory is open, the cursor is unlocked so the player
    /// can interact with the UI.
    /// </summary>
    private void ToggleInventory()
    {
        if (inventoryPanel == null) return;

        bool newState = !inventoryPanel.activeSelf;
        inventoryPanel.SetActive(newState);

        if (newState)
        {
            // Opening inventory — unlock cursor for UI interaction
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            // Closing inventory — re-lock cursor for gameplay
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log($"[InventoryUI] Inventory panel {(newState ? "opened" : "closed")}.");
    }

    // ───────────────────────── Utility ─────────────────────────

    /// <summary>
    /// Finds a component on a named child object. Returns null if not found.
    /// Searches recursively through all children.
    /// </summary>
    private T FindChildComponent<T>(GameObject parent, string childName) where T : Component
    {
        Transform child = FindChildRecursive(parent.transform, childName);
        if (child == null)
        {
            return null;
        }
        return child.GetComponent<T>();
    }

    private Transform FindChildRecursive(Transform parent, string name)
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name) return child;

            Transform found = FindChildRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
