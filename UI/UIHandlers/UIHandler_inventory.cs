using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

public class UIHandler_inventory : IClosableUI
{
    private bool isDragging = false;
    private bool isHovering = false;
    private bool isCreated = false;
    private Vector2 pointerDownPosition;
    private Coroutine hoverCoroutine;
    private float hoverDelay = 0.5f;
    private int hoveredSlotIndex = -1;
    private int targetSlotIndex = -1;
    private int draggedItemIndex = -1;
    private VisualElement draggedElement;
    private VisualElement tooltip;
    private Label tooltipNameLabel;
    private Label tooltipTypeLabel;
    private Label tooltipDescriptionLabel;
    private UIDocument uiDocument;
    private List<VisualElement> slots = new List<VisualElement>();
    private PlayerInventory playerInventory;
    [SerializeField] private UIHandler_hotbar hotBarManager;

    // Start is called before the first frame update
    void Start()
    {
        uiDocument = GetComponent<UIDocument>();
        VisualElement root = uiDocument.rootVisualElement;
        VisualElement inventoryGrid = root.Q<VisualElement>("Grid");
        HideInventory();

        foreach (VisualElement slot in inventoryGrid.Children())
        {
            slots.Add(slot);
        }

        playerInventory = PlayerInventory.Instance;
        if (playerInventory == null)
        {
            Debug.LogError("PlayerInventory instance not found!");
            return;
        }

        // Subscribe to the OnInventoryChanged event
        playerInventory.OnInventoryChanged += RebuildInventoryUI;

        // Add pointer events to each slot
        for (int i = 0; i < slots.Count; i++)
        {
            int index = i;
            VisualElement slot = slots[index];
            slot.RegisterCallback<PointerDownEvent>(evt => OnSlotPointerDown(index, evt));
        }

        root.RegisterCallback<PointerMoveEvent>(OnGlobalPointerMove);
        root.RegisterCallback<PointerUpEvent>(OnGlobalPointerUp);
    }

    void OnDestroy()
    {
        // Unsubscribe from the OnInventoryChanged event
        playerInventory.OnInventoryChanged -= RebuildInventoryUI;
    }

    void RebuildInventoryUI()
    {
        ClearAllSlots();
        ShowAllItems();
    }

    // Clear all slots in the inventory
    void ClearAllSlots()
    {
        foreach (VisualElement slot in slots)
        {
            VisualElement icon = slot.Q<VisualElement>("Icon");
            icon.style.backgroundImage = null;

            Label quantityLabel = slot.Q<Label>("Quantity");
            if (quantityLabel != null)
            {
                quantityLabel.text = "";
            }
        }
    }

    // Show all items in the inventory
    void ShowAllItems()
    {
        List<ItemStack> playerItems = playerInventory.GetItems();

        for (int i = 0; i < playerItems.Count && i < slots.Count; i++)
        {
            if (playerInventory.IsEmptySlot(i)) continue;
            
            ItemStack currentItem = playerItems[i];
            VisualElement slot = slots[i];
            VisualElement icon = slot.Q<VisualElement>("Icon");
            if (icon != null)
            {
                icon.style.backgroundImage = currentItem.itemDefinition.ItemSprite.texture;
            }

            if (currentItem.quantity > 1)
            {
                Label quantityLabel = slot.Q<Label>("Quantity");
                if (quantityLabel != null)
                {
                    quantityLabel.text = currentItem.quantity.ToString();
                }
            }
        }
    }

    void OnSlotPointerDown(int index, PointerDownEvent evt)
    {
        pointerDownPosition = evt.position;
        List<ItemStack> playerItems = playerInventory.GetItems();

        // If the slot has an item
        if (index < playerItems.Count && playerItems[index] != null)
        {
            draggedItemIndex = index;

            if (isHovering && hoveredSlotIndex == index)
            {
                isHovering = false;
            }

            // Set the dragged element
            ItemStack playerItem = playerItems[index];

            VisualElement icon = new VisualElement();
            icon.style.backgroundImage = playerItem.itemDefinition.ItemSprite.texture;
            icon.AddToClassList("dragged-icon");
            draggedElement.Clear();
            draggedElement.Add(icon);

            if (playerItem.quantity > 1)
            {
                Label quantityLabel = new Label();
                quantityLabel.text = playerItem.quantity.ToString();
                quantityLabel.AddToClassList("quantity");
                draggedElement.Add(quantityLabel);
            }

            draggedElement.style.left = pointerDownPosition.x - draggedElement.worldBound.width / 2;
            draggedElement.style.top = pointerDownPosition.y - draggedElement.worldBound.height / 2;
            draggedElement.style.visibility = Visibility.Visible;
            slots[draggedItemIndex].Q<VisualElement>("Icon").style.visibility = Visibility.Hidden;
            slots[draggedItemIndex].Q<VisualElement>("Quantity").style.visibility = Visibility.Hidden;
        }
    }

    void OnGlobalPointerMove(PointerMoveEvent evt)
    {
        Vector2 pointerPosition = evt.position;

        // If the player is dragging an item
        if (draggedItemIndex >= 0)
        {
            isHovering = false;
            isDragging = true;
            tooltip.style.visibility = Visibility.Hidden;
            
            draggedElement.style.left = pointerPosition.x - draggedElement.layout.width / 2;
            draggedElement.style.top = pointerPosition.y - draggedElement.layout.height / 2;

            UpdateTargetSlotIndex();
        }
        else if (!isDragging)
        {
            int newHoveredSlotIndex = slots
                .Select((slot, index) => new { Slot = slot, Index = index })
                .Where(x => x.Slot.worldBound.Contains(pointerPosition))
                .Select(x => x.Index)
                .DefaultIfEmpty(-1)
                .First();

            if (newHoveredSlotIndex != hoveredSlotIndex)
            {
                // If the player is no longer hovering over the same slot
                if (isHovering)
                {
                    isHovering = false;
                    tooltip.style.visibility = Visibility.Hidden;
                }

                // Cancel any existing hover coroutine
                if (hoverCoroutine != null)
                {
                    StopCoroutine(hoverCoroutine);
                    hoverCoroutine = null;
                }

                if (newHoveredSlotIndex >= 0)
                {
                    List<ItemStack> playerItems = playerInventory.GetItems();
                    if (newHoveredSlotIndex < playerItems.Count && !playerInventory.IsEmptySlot(newHoveredSlotIndex))
                    {
                        hoveredSlotIndex = newHoveredSlotIndex;
                        isHovering = true;

                        // Start a new hover coroutine
                        hoverCoroutine = StartCoroutine(ShowTooltip(playerItems[hoveredSlotIndex].itemDefinition));
                    }
                }
                else 
                {
                    hoveredSlotIndex = -1;
                }
            }
        }
    }

    void OnGlobalPointerUp(PointerUpEvent evt)
    {
        if (draggedItemIndex >= 0 && targetSlotIndex != draggedItemIndex)
        {
            if (targetSlotIndex >= 0)
            {
                playerInventory.SwapItems(draggedItemIndex, targetSlotIndex);
            }
        }

        draggedElement.style.visibility = Visibility.Hidden;
        slots[draggedItemIndex].Q<VisualElement>("Icon").style.visibility = Visibility.Visible;
        slots[draggedItemIndex].Q<VisualElement>("Quantity").style.visibility = Visibility.Visible;

        draggedItemIndex = -1;
        targetSlotIndex = -1;
        isHovering = false;
        isDragging = false;
    }

    void UpdateTargetSlotIndex()
    {
        try
        {
            if (draggedElement.layout.width <= 0 || draggedElement.layout.height <= 0)
            {
                targetSlotIndex = -1;
                return;
            }
            
            targetSlotIndex = slots
                .Select((slot, index) => new { Slot = slot, Index = index })
                .Where(x => x.Slot.worldBound.Overlaps(draggedElement.worldBound) && x.Index != draggedItemIndex)
                .OrderBy(x => Vector2.Distance(x.Slot.worldBound.center, draggedElement.worldBound.center))
                .Select(x => x.Index)
                .DefaultIfEmpty(-1)
                .First();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error updating hovered slot index: {e.Message}");
            targetSlotIndex = -1;
        }
    }

    void CreateTooltip()
    {
        // Create a tooltip element
        tooltip = new VisualElement();
        tooltip.AddToClassList("tooltip-container");

        VisualElement tooltipAbove = new VisualElement();
        tooltipAbove.AddToClassList("tooltip-above");

        tooltipNameLabel = new Label();
        tooltipNameLabel.AddToClassList("tooltip-name-label");
        tooltipTypeLabel = new Label();
        tooltipTypeLabel.AddToClassList("tooltip-type-label");

        VisualElement tooltipUnder = new VisualElement();
        tooltipUnder.AddToClassList("tooltip-under");

        tooltipDescriptionLabel = new Label();
        tooltipDescriptionLabel.AddToClassList("tooltip-description-label");

        VisualElement tooltipFooter = new VisualElement();
        tooltipFooter.AddToClassList("tooltip-footer");

        tooltipAbove.Add(tooltipNameLabel);
        tooltipAbove.Add(tooltipTypeLabel);
        tooltipUnder.Add(tooltipDescriptionLabel);
        tooltip.Add(tooltipAbove);
        tooltip.Add(tooltipUnder);
        tooltip.Add(tooltipFooter);

        uiDocument.rootVisualElement.Add(tooltip);
        tooltip.style.visibility = Visibility.Hidden;
    }

    // Coroutine to show the tooltip after a delay
    IEnumerator ShowTooltip(ItemDefinition item)
    {
        yield return new WaitForSeconds(hoverDelay);

        // Set the tooltip content
        tooltipNameLabel.text = item.ItemName;
        tooltipTypeLabel.text = item.ItemCategory.ToString();
        tooltipDescriptionLabel.text = item.Description;

        // Position the tooltip near the hovered slot
        if (hoveredSlotIndex >= 0)
        {
            tooltip.style.left = slots[hoveredSlotIndex].worldBound.xMax - 10;
            tooltip.style.top = slots[hoveredSlotIndex].worldBound.yMax;
            tooltip.style.visibility = Visibility.Visible;
        }

        hoverCoroutine = null;
    }

    public override void ShowUI()
    {
        if (!isCreated)
        {
            RebuildInventoryUI();
            isCreated = true;
            CreateTooltip();

            // Create the dragged element and add it to the root visual element
            draggedElement = new VisualElement();
            draggedElement.AddToClassList("dragged-item");
            draggedElement.style.visibility = Visibility.Hidden;
            uiDocument.rootVisualElement.Add(draggedElement);
        }

        uiDocument.rootVisualElement.style.display = DisplayStyle.Flex;
        IClosableUI.openingUI = this;

        PlayerController.Instance.DisableGameplayActions();

        if (hotBarManager != null)
        {
            hotBarManager.Hide();
        }
    }

    void HideInventory()
    {
        uiDocument.rootVisualElement.style.display = DisplayStyle.None;
        IClosableUI.openingUI = null;
    }

    public override void CloseUI()
    {
        if (IClosableUI.openingUI != null)
        {
            HideInventory();
            PlayerController.Instance.EnableGameplayActions();

            if (hotBarManager != null)
            {
                hotBarManager.Show();
            }
        }
    }
}