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

    [Tooltip("Number of resource slots to display (includes empty ones).")]
    [SerializeField] private int resourceSlotCount = 10;

    [Tooltip("Number of equipment slots to display (includes empty ones).")]
    [SerializeField] private int equipmentSlotCount = 10;

    [Header("Weight Display")]
    [Tooltip("Slider showing current weight as a fraction of max carry weight. " +
             "Leave null if you don't want a weight bar yet.")]
    [SerializeField] private Slider weightBarSlider;

    [Tooltip("Text displaying 'XX.X / YY.Y kg' weight values. " +
             "Leave null if you don't want weight text yet.")]
    [SerializeField] private TMP_Text weightText;

    [Tooltip("Color of the weight bar when under 70% capacity.")]
    [SerializeField] private Color weightBarNormalColor = new Color(0.3f, 0.8f, 0.4f);

    [Tooltip("Color of the weight bar when between 70-90% capacity.")]
    [SerializeField] private Color weightBarWarningColor = new Color(1f, 0.7f, 0.2f);

    [Tooltip("Color of the weight bar when over 90% capacity.")]
    [SerializeField] private Color weightBarFullColor = new Color(0.9f, 0.2f, 0.2f);

    // ───────────────────────── Private State ─────────────────────────

    private Inventory _localInventory;          // Equipment (tools, weapons)
    private ResourceInventory _localResources;  // Raw materials (wood, stone, fiber)
    private bool _isSubscribed;
    private Transform _slotContainer;           // Original grid (kept as reference)
    private Transform _resourceContainer;       // Programmatically created resource grid
    private Transform _equipmentContainer;      // Programmatically created equipment grid
    private readonly List<GameObject> _slotInstances = new List<GameObject>();
    private readonly List<GameObject> _sectionObjects = new List<GameObject>(); // Labels + containers

    // ───────────────────────── Lifecycle ─────────────────────────

    private void Start()
    {
        if (inventoryPanel != null)
        {
            inventoryPanel.SetActive(false);

            // Auto-discover the original slot container (GridLayoutGroup)
            _slotContainer = FindSlotContainer();
            if (_slotContainer == null)
            {
                Debug.LogError("[InventoryUI] Could not find a child with GridLayoutGroup " +
                               "inside the inventoryPanel!");
            }
            else
            {
                // Build two separate sections from the original grid
                BuildSectionLayout();
                Debug.Log("[InventoryUI] Built Resources + Equipment sections.");
            }
        }
        else
        {
            Debug.LogError("[InventoryUI] inventoryPanel is not assigned!");
        }
    }

    /// <summary>
    /// Programmatically creates two labeled sections inside the inventory panel:
    /// "Resources" grid and "Equipment" grid, using the original GridLayoutGroup
    /// settings as a template. Requires NO Inspector changes.
    /// </summary>
    private void BuildSectionLayout()
    {
        // Get the parent that holds the original grid container
        Transform layoutParent = _slotContainer.parent != null ? _slotContainer.parent : _slotContainer;

        // Copy grid settings from the original container
        GridLayoutGroup originalGrid = _slotContainer.GetComponent<GridLayoutGroup>();
        Vector2 cellSize = originalGrid != null ? originalGrid.cellSize : new Vector2(64, 64);
        Vector2 spacing = originalGrid != null ? originalGrid.spacing : new Vector2(4, 4);
        RectOffset padding = originalGrid != null ? originalGrid.padding : new RectOffset();
        int columns = originalGrid != null ? originalGrid.constraintCount : 5;

        // Hide the original container — we'll use our new ones instead
        _slotContainer.gameObject.SetActive(false);

        // Add a VerticalLayoutGroup to the panel if it doesn't have one
        // (so the sections stack vertically)
        var parentLayout = layoutParent.GetComponent<VerticalLayoutGroup>();
        if (parentLayout == null)
        {
            parentLayout = layoutParent.gameObject.AddComponent<VerticalLayoutGroup>();
            parentLayout.childControlWidth = true;
            parentLayout.childControlHeight = false;
            parentLayout.childForceExpandWidth = true;
            parentLayout.childForceExpandHeight = false;
            parentLayout.spacing = 8f;
            parentLayout.padding = new RectOffset(8, 8, 8, 8);
        }

        // ── Create RESOURCES section ──
        _resourceContainer = CreateSection(layoutParent, "Resources",
            cellSize, spacing, padding, columns);

        // ── Create EQUIPMENT section ──
        _equipmentContainer = CreateSection(layoutParent, "Equipment",
            cellSize, spacing, padding, columns);

        // ── Move weight bar to the bottom of the layout ──
        // The Slider and weight text are existing children of the panel.
        // The VerticalLayoutGroup will squish them unless we move them to the end
        // and give them a proper height via LayoutElement.
        if (weightBarSlider != null)
        {
            weightBarSlider.transform.SetAsLastSibling();
            var sliderLE = weightBarSlider.gameObject.GetComponent<LayoutElement>();
            if (sliderLE == null) sliderLE = weightBarSlider.gameObject.AddComponent<LayoutElement>();
            sliderLE.preferredHeight = 20f;
            sliderLE.minHeight = 20f;
        }

        if (weightText != null)
        {
            weightText.transform.SetAsLastSibling();
            var textLE = weightText.gameObject.GetComponent<LayoutElement>();
            if (textLE == null) textLE = weightText.gameObject.AddComponent<LayoutElement>();
            textLE.preferredHeight = 24f;
            textLE.minHeight = 24f;
        }
    }

    /// <summary>
    /// Creates a labeled section: a header TMP_Text + a GridLayoutGroup container.
    /// </summary>
    private Transform CreateSection(Transform parent, string label,
        Vector2 cellSize, Vector2 spacing, RectOffset padding, int columns)
    {
        // ── Header label ──
        GameObject headerObj = new GameObject($"{label}Header");
        headerObj.transform.SetParent(parent, false);
        _sectionObjects.Add(headerObj);

        RectTransform headerRect = headerObj.AddComponent<RectTransform>();
        headerRect.sizeDelta = new Vector2(0, 24);

        var tmpText = headerObj.AddComponent<TMPro.TextMeshProUGUI>();
        tmpText.text = label.ToUpper();
        tmpText.fontSize = 24;
        tmpText.fontStyle = TMPro.FontStyles.Bold;
        tmpText.color = new Color(0f, 0f, 0f, 1f);
        tmpText.alignment = TMPro.TextAlignmentOptions.Left;

        // ── Grid container ──
        GameObject containerObj = new GameObject($"{label}Container");
        containerObj.transform.SetParent(parent, false);
        _sectionObjects.Add(containerObj);

        RectTransform containerRect = containerObj.AddComponent<RectTransform>();
        containerRect.sizeDelta = new Vector2(0, 0);

        // Add Image component for background (optional subtle tint)
        var bg = containerObj.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.15f);

        // Add GridLayoutGroup matching the original
        var grid = containerObj.AddComponent<GridLayoutGroup>();
        grid.cellSize = cellSize;
        grid.spacing = spacing;
        grid.padding = new RectOffset(padding.left, padding.right, padding.top, padding.bottom);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;

        // Add ContentSizeFitter so the grid expands to fit its children
        var fitter = containerObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return containerObj.transform;
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

        var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
        var inventory = playerObj.GetComponent<Inventory>();
        var resources = playerObj.GetComponent<ResourceInventory>();

        // Wait until both are available
        if (inventory == null || resources == null) return;

        _localInventory = inventory;
        _localResources = resources;
        SubscribeToInventory();
        RebuildUI();

        Debug.Log("[InventoryUI] Found local player's inventories (Equipment + Resources) — UI ready.");
    }

    // ───────────────────────── Subscription Management ─────────────────────────

    private void SubscribeToInventory()
    {
        if (_isSubscribed || _localInventory == null || _localResources == null) return;

        // Subscribe to equipment changes
        _localInventory.Items.OnListChanged += OnInventoryChanged;
        _localInventory.CurrentWeight.OnValueChanged += OnWeightChanged;
        _localInventory.MaxWeight.OnValueChanged += OnWeightChanged;

        // Subscribe to resource changes
        _localResources.Items.OnListChanged += OnInventoryChanged;
        _localResources.CurrentWeight.OnValueChanged += OnWeightChanged;
        _localResources.MaxWeight.OnValueChanged += OnWeightChanged;

        _isSubscribed = true;
    }

    private void UnsubscribeFromInventory()
    {
        if (!_isSubscribed) return;

        if (_localInventory != null)
        {
            _localInventory.Items.OnListChanged -= OnInventoryChanged;
            _localInventory.CurrentWeight.OnValueChanged -= OnWeightChanged;
            _localInventory.MaxWeight.OnValueChanged -= OnWeightChanged;
        }

        if (_localResources != null)
        {
            _localResources.Items.OnListChanged -= OnInventoryChanged;
            _localResources.CurrentWeight.OnValueChanged -= OnWeightChanged;
            _localResources.MaxWeight.OnValueChanged -= OnWeightChanged;
        }

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

    /// <summary>
    /// Called whenever CurrentWeight or MaxWeight NetworkVariables change.
    /// Updates the weight bar without rebuilding the entire slot grid.
    /// </summary>
    private void OnWeightChanged(float previousValue, float newValue)
    {
        UpdateWeightBar();
    }

    // ───────────────────────── UI Building ─────────────────────────

    /// <summary>
    /// Destroys all existing slot instances and rebuilds from the current
    /// NetworkList state. Called on subscription and on every list change.
    /// </summary>
    private void RebuildUI()
    {
        if (slotPrefab == null) return;
        if (_localInventory == null || _localResources == null) return;
        if (_resourceContainer == null || _equipmentContainer == null) return;

        // Clear existing slots
        foreach (var slot in _slotInstances)
        {
            if (slot != null) Destroy(slot);
        }
        _slotInstances.Clear();

        // ── Build RESOURCE slots ──
        for (int i = 0; i < _localResources.Items.Count; i++)
        {
            CreateSlot(_localResources.Items[i], _resourceContainer);
        }
        int resourceEmpty = resourceSlotCount - _localResources.Items.Count;
        for (int i = 0; i < resourceEmpty; i++)
        {
            CreateEmptySlot(_resourceContainer);
        }

        // ── Build EQUIPMENT slots ──
        for (int i = 0; i < _localInventory.Items.Count; i++)
        {
            CreateSlot(_localInventory.Items[i], _equipmentContainer);
        }
        int equipEmpty = equipmentSlotCount - _localInventory.Items.Count;
        for (int i = 0; i < equipEmpty; i++)
        {
            CreateEmptySlot(_equipmentContainer);
        }

        UpdateWeightBar();
    }

    /// <summary>
    /// Updates the weight bar slider and text to reflect current/max carry weight.
    /// Reads from the Inventory's NetworkVariables (replicated from server).
    /// Changes color based on how full the inventory is.
    /// </summary>
    private void UpdateWeightBar()
    {
        if (_localInventory == null || _localResources == null) return;

        // Sum weights from BOTH inventories
        float current = _localInventory.CurrentWeight.Value + _localResources.CurrentWeight.Value;
        float max = _localInventory.MaxWeight.Value + _localResources.MaxWeight.Value;
        float ratio = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;

        // Update slider
        if (weightBarSlider != null)
        {
            weightBarSlider.value = ratio;

            Image fillImage = weightBarSlider.fillRect?.GetComponent<Image>();
            if (fillImage != null)
            {
                if (ratio >= 0.9f)
                    fillImage.color = weightBarFullColor;
                else if (ratio >= 0.7f)
                    fillImage.color = weightBarWarningColor;
                else
                    fillImage.color = weightBarNormalColor;
            }
        }

        // Update text
        if (weightText != null)
        {
            weightText.text = $"{current:F1} / {max:F1}";

            if (ratio >= 0.9f)
                weightText.color = weightBarFullColor;
            else
                weightText.color = Color.white;
        }
    }

    /// <summary>
    /// Creates a single populated slot from inventory data.
    /// Looks up the ItemDefinition for display name and icon.
    /// </summary>
    private void CreateSlot(InventoryItem item, Transform parent)
    {
        GameObject slotObj = Instantiate(slotPrefab, parent);
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
    private void CreateEmptySlot(Transform parent)
    {
        GameObject slotObj = Instantiate(slotPrefab, parent);
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
            // Opening inventory — rebuild content and force layout recalculation
            RebuildUI();

            // Force Unity to recalculate all layout groups immediately
            // (without this, the first open has broken layout because
            // VerticalLayoutGroup/ContentSizeFitter haven't calculated yet)
            Canvas.ForceUpdateCanvases();
            var panelRect = inventoryPanel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            }

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
