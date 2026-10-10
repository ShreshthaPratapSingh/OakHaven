using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Client-side UI for interacting with workstations.
/// Builds all layout programmatically (no prefabs needed).
/// </summary>
public class WorkstationUI : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("The workstation panel root. Must be a Panel with an Image component.")]
    [SerializeField] private GameObject workstationPanel;

    // ───────────────────────── Private State ─────────────────────────

    private PlayerCrafting _playerCrafting;
    private Workstation _currentWorkstation;
    private bool _isOpen;
    private TMP_Text _titleText;
    private Transform _contentArea;
    private readonly List<GameObject> _builtObjects = new List<GameObject>();

    // ───────────────────────── Lifecycle ─────────────────────────

    private void Start()
    {
        if (workstationPanel != null)
        {
            SetupPanel();
            workstationPanel.SetActive(false);
        }
    }

    private void Update()
    {
        if (_playerCrafting == null)
        {
            TryFindPlayerCrafting();
            return;
        }

        if (_isOpen && _currentWorkstation != null)
        {
            if (_playerCrafting.NearbyWorkstation != _currentWorkstation)
                ClosePanel();
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromWorkstation();
    }

    // ───────────────────────── Panel Setup (Code-Built) ─────────────────────────

    private void SetupPanel()
    {
        // Configure the panel itself
        RectTransform panelRect = workstationPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.25f, 0.15f);
        panelRect.anchorMax = new Vector2(0.75f, 0.85f);
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Dark background
        Image panelBg = workstationPanel.GetComponent<Image>();
        if (panelBg != null)
            panelBg.color = new Color(0.12f, 0.12f, 0.12f, 0.95f);

        // Clear any existing children
        foreach (Transform child in workstationPanel.transform)
            Destroy(child.gameObject);

        // Add vertical layout to panel
        var vLayout = workstationPanel.AddComponent<VerticalLayoutGroup>();
        vLayout.padding = new RectOffset(20, 20, 15, 15);
        vLayout.spacing = 10f;
        vLayout.childControlWidth = true;
        vLayout.childControlHeight = false;
        vLayout.childForceExpandWidth = true;
        vLayout.childForceExpandHeight = false;

        // ── Title ──
        GameObject titleObj = new GameObject("TitleText");
        titleObj.transform.SetParent(workstationPanel.transform, false);
        var titleRect = titleObj.AddComponent<RectTransform>();
        var titleLE = titleObj.AddComponent<LayoutElement>();
        titleLE.preferredHeight = 40f;
        _titleText = titleObj.AddComponent<TextMeshProUGUI>();
        _titleText.text = "Workstation";
        _titleText.fontSize = 28;
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.color = Color.white;
        _titleText.alignment = TextAlignmentOptions.Center;

        // ── Divider line ──
        CreateDivider(workstationPanel.transform);

        // ── Scrollable content area ──
        GameObject contentObj = new GameObject("ContentArea");
        contentObj.transform.SetParent(workstationPanel.transform, false);
        var contentLE = contentObj.AddComponent<LayoutElement>();
        contentLE.flexibleHeight = 1f; // Take remaining space
        contentLE.preferredHeight = 200f;

        // Content area needs its own vertical layout
        var contentVLayout = contentObj.AddComponent<VerticalLayoutGroup>();
        contentVLayout.spacing = 6f;
        contentVLayout.padding = new RectOffset(5, 5, 5, 5);
        contentVLayout.childControlWidth = true;
        contentVLayout.childControlHeight = false;
        contentVLayout.childForceExpandWidth = true;
        contentVLayout.childForceExpandHeight = false;

        var contentFitter = contentObj.AddComponent<ContentSizeFitter>();
        contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Subtle background for content
        var contentBg = contentObj.AddComponent<Image>();
        contentBg.color = new Color(0f, 0f, 0f, 0.3f);

        _contentArea = contentObj.transform;

        // ── Divider line ──
        CreateDivider(workstationPanel.transform);

        // ── Close button ──
        GameObject closeBtnObj = CreateButton(workstationPanel.transform, "Close", () => ClosePanel());
        var closeBtnLE = closeBtnObj.GetComponent<LayoutElement>();
        if (closeBtnLE == null) closeBtnLE = closeBtnObj.AddComponent<LayoutElement>();
        closeBtnLE.preferredHeight = 35f;
    }

    // ───────────────────────── Discovery ─────────────────────────

    private void TryFindPlayerCrafting()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return;
        if (NetworkManager.Singleton.LocalClient?.PlayerObject == null) return;
        _playerCrafting = NetworkManager.Singleton.LocalClient.PlayerObject
            .GetComponent<PlayerCrafting>();
    }

    // ───────────────────────── Open / Close ─────────────────────────

    public void OpenPanel(Workstation workstation)
    {
        if (workstation == null) return;
        UnsubscribeFromWorkstation();

        _currentWorkstation = workstation;
        _isOpen = true;

        _currentWorkstation.IsRepaired.OnValueChanged += OnRepairStateChanged;
        _currentWorkstation.RepairContributions.OnListChanged += OnContributionsChanged;

        workstationPanel.SetActive(true);
        RebuildContent();

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Debug.Log($"[WorkstationUI] Opened panel for '{workstation.Definition?.displayName}'");
    }

    public void ClosePanel()
    {
        UnsubscribeFromWorkstation();
        _currentWorkstation = null;
        _isOpen = false;

        if (workstationPanel != null)
            workstationPanel.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public bool IsOpen => _isOpen;

    // ───────────────────────── Subscription ─────────────────────────

    private void UnsubscribeFromWorkstation()
    {
        if (_currentWorkstation != null)
        {
            _currentWorkstation.IsRepaired.OnValueChanged -= OnRepairStateChanged;
            _currentWorkstation.RepairContributions.OnListChanged -= OnContributionsChanged;
        }
    }

    private void OnRepairStateChanged(bool prev, bool next) => RebuildContent();
    private void OnContributionsChanged(NetworkListEvent<InventoryItem> e) => RebuildContent();

    // ───────────────────────── Content Building ─────────────────────────

    private void RebuildContent()
    {
        if (_currentWorkstation == null || _contentArea == null) return;

        // Clear previous content rows
        foreach (var obj in _builtObjects)
        {
            if (obj != null) Destroy(obj);
        }
        _builtObjects.Clear();

        var def = _currentWorkstation.Definition;
        if (def == null) return;

        if (!_currentWorkstation.IsRepaired.Value)
        {
            _titleText.text = $"{def.displayName} — NEEDS REPAIR";
            BuildRepairRows(def);
        }
        else
        {
            _titleText.text = $"{def.displayName} — READY";
            BuildCraftingRows(def);
        }
    }

    private void BuildRepairRows(WorkingStationDefinition def)
    {
        foreach (var cost in def.repairCosts)
        {
            int contributed = 0;
            for (int i = 0; i < _currentWorkstation.RepairContributions.Count; i++)
            {
                if (_currentWorkstation.RepairContributions[i].ItemId.ToString() == cost.itemId)
                {
                    contributed = _currentWorkstation.RepairContributions[i].Quantity;
                    break;
                }
            }

            bool isFulfilled = contributed >= cost.quantity;
            ItemDefinition itemDef = ItemDatabase.GetItem(cost.itemId);
            string displayName = itemDef != null ? itemDef.displayName : cost.itemId;

            // Build a row: [ItemName]  [Progress]  [Button]
            GameObject row = CreateRow(_contentArea);

            AddLabel(row.transform, displayName, 18, TextAlignmentOptions.Left, 1f);
            AddLabel(row.transform, $"{contributed} / {cost.quantity}", 18,
                TextAlignmentOptions.Center, 1f,
                isFulfilled ? new Color(0.3f, 0.9f, 0.3f) : Color.white);

            if (!isFulfilled)
            {
                int toContribute = cost.quantity - contributed;
                string capturedId = cost.itemId;
                CreateButton(row.transform, $"Contribute {toContribute}", () =>
                {
                    _playerCrafting.ContributeRepair(capturedId, toContribute);
                });
            }
            else
            {
                AddLabel(row.transform, "✓ Done", 16, TextAlignmentOptions.Center, 0.8f,
                    new Color(0.3f, 0.9f, 0.3f));
            }
        }
    }

    private void BuildCraftingRows(WorkingStationDefinition def)
    {
        foreach (var recipe in def.availableRecipes)
        {
            if (recipe == null) continue;

            // Recipe header row
            GameObject row = CreateRow(_contentArea);

            AddLabel(row.transform, recipe.displayName, 20, TextAlignmentOptions.Left, 1f,
                new Color(1f, 0.85f, 0.4f));

            // Ingredient text
            string ingText = "";
            foreach (var ing in recipe.ingredients)
            {
                ItemDefinition itemDef = ItemDatabase.GetItem(ing.itemId);
                string name = itemDef != null ? itemDef.displayName : ing.itemId;
                if (ingText.Length > 0) ingText += ", ";
                ingText += $"{ing.quantity}x {name}";
            }
            AddLabel(row.transform, ingText, 14, TextAlignmentOptions.Center, 1.5f,
                new Color(0.8f, 0.8f, 0.8f));

            string capturedRecipeId = recipe.recipeId;
            CreateButton(row.transform, "Craft", () =>
            {
                _playerCrafting.RequestCraft(capturedRecipeId);
            });
        }
    }

    // ───────────────────────── UI Factory Helpers ─────────────────────────

    private GameObject CreateRow(Transform parent)
    {
        GameObject row = new GameObject("Row");
        row.transform.SetParent(parent, false);
        _builtObjects.Add(row);

        row.AddComponent<RectTransform>();

        var le = row.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;

        var hLayout = row.AddComponent<HorizontalLayoutGroup>();
        hLayout.spacing = 10f;
        hLayout.padding = new RectOffset(10, 10, 5, 5);
        hLayout.childControlWidth = true;
        hLayout.childControlHeight = true;
        hLayout.childForceExpandWidth = false;
        hLayout.childForceExpandHeight = true;

        // Subtle row background
        var bg = row.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.05f);

        return row;
    }

    private void AddLabel(Transform parent, string text, float fontSize,
        TextAlignmentOptions alignment, float flexWidth, Color? color = null)
    {
        GameObject obj = new GameObject("Label");
        obj.transform.SetParent(parent, false);

        obj.AddComponent<RectTransform>();

        var le = obj.AddComponent<LayoutElement>();
        le.flexibleWidth = flexWidth;

        var tmp = obj.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = color ?? Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
    }

    private GameObject CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject btnObj = new GameObject("Button");
        btnObj.transform.SetParent(parent, false);

        btnObj.AddComponent<RectTransform>();

        var le = btnObj.AddComponent<LayoutElement>();
        le.preferredWidth = 150f;
        le.preferredHeight = 35f;

        var btnImage = btnObj.AddComponent<Image>();
        btnImage.color = new Color(0.25f, 0.55f, 0.25f, 1f);

        var btn = btnObj.AddComponent<Button>();
        btn.targetGraphic = btnImage;

        // Hover colors
        var colors = btn.colors;
        colors.highlightedColor = new Color(0.3f, 0.65f, 0.3f, 1f);
        colors.pressedColor = new Color(0.2f, 0.45f, 0.2f, 1f);
        btn.colors = colors;

        btn.onClick.AddListener(onClick);

        // Button text
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(btnObj.transform, false);

        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmp = textObj.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 16;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;

        return btnObj;
    }

    private void CreateDivider(Transform parent)
    {
        GameObject divider = new GameObject("Divider");
        divider.transform.SetParent(parent, false);

        divider.AddComponent<RectTransform>();

        var le = divider.AddComponent<LayoutElement>();
        le.preferredHeight = 2f;

        var img = divider.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.2f);
    }
}
