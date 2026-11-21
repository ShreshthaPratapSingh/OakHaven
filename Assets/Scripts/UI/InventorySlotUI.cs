using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Per-slot UI controller for the inventory grid.
/// Handles drag-and-drop, right-click drop, and middle-click split.
/// </summary>
public class InventorySlotUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [Tooltip("Child Image used to display the item icon.")]
    [SerializeField] private Image iconImage;

    [Tooltip("Child TMP_Text used to display the stack quantity.")]
    [SerializeField] private TMP_Text quantityText;

    /// <summary>
    /// The inventory this slot belongs to.
    /// </summary>
    public BaseInventory Inventory { get; set; }

    /// <summary>
    /// Index of this slot inside the inventory's NetworkList.
    /// </summary>
    public int SlotIndex { get; set; }

    /// <summary>
    /// Reference back to the main inventory UI controller.
    /// </summary>
    public InventoryUI InventoryUI { get; set; }

    /// <summary>
    /// The sprite currently shown in this slot (null for empty slots).
    /// </summary>
    public Sprite ItemIcon => iconImage != null ? iconImage.sprite : null;

    /// <summary>
    /// Refreshes the displayed icon and quantity from the replicated slot data.
    /// </summary>
    public void Refresh(InventorySlot slot)
    {
        if (iconImage == null)
            iconImage = FindChildComponent<Image>(transform, "Icon");
        if (quantityText == null)
            quantityText = FindChildComponent<TMP_Text>(transform, "QuantityText");

        if (slot.IsEmpty)
        {
            iconImage.sprite = null;
            iconImage.color = Color.clear;
            if (quantityText != null) quantityText.text = "";
            name = "Slot_Empty";
            return;
        }

        string itemId = slot.ItemId.ToString();
        ItemDefinition def = ItemDatabase.GetItem(itemId);

        if (iconImage != null)
        {
            if (def != null && def.icon != null)
            {
                iconImage.sprite = def.icon;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.color = new Color(0.6f, 0.8f, 0.5f, 0.8f);
            }
        }

        if (quantityText != null)
        {
            quantityText.text = slot.Quantity > 1 ? $"x{slot.Quantity}" : "";
        }

        name = $"Slot_{itemId}_{slot.Quantity}";
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (Inventory == null || InventoryUI == null) return;
        if (Inventory.Items[SlotIndex].IsEmpty) return;
        InventoryUI.BeginSlotDrag(this);
    }

    public void OnDrag(PointerEventData eventData)
    {
        InventoryUI.UpdateSlotDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        InventoryUI.EndSlotDrag(this, eventData);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (Inventory == null || InventoryUI == null) return;
        if (Inventory.Items[SlotIndex].IsEmpty) return;

        if (eventData.button == PointerEventData.InputButton.Right)
        {
            bool dropStack = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int amount = dropStack ? Inventory.Items[SlotIndex].Quantity : 1;
            Inventory.DropSlotServerRpc(SlotIndex, amount);
        }
        else if (eventData.button == PointerEventData.InputButton.Middle)
        {
            Inventory.SplitSlotServerRpc(SlotIndex, 0);
        }
    }

    private T FindChildComponent<T>(Transform parent, string childName) where T : Component
    {
        Transform child = FindChildRecursive(parent, childName);
        return child != null ? child.GetComponent<T>() : null;
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
