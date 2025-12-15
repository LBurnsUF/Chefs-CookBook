// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.UI.CraftingPanel
using System;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class CraftingPanel : MonoBehaviour
{
	public RectTransform ingredientContainer;

	public RectTransform inventoryContainer;

	public GridLayoutGroup gridlayoutGroup;

	public int slotCount;

	public PickupIcon result;

	public GameObject resultGlow;

	public MPButton confirmButton;

	private PickupPickerPanel pickupPickerPanel;

	private UIElementAllocator<MPButton> buttonAllocator;

	public GameObject slotPrefab;

	public EventFunctions eventFunctions;

	private MPEventSystemLocator mpEventSystemLocator;

	private bool previouslyHadPossibleResult;

	private bool justEnabled_NeedsToSetDefaultSelection;

	public CraftingController craftingController { get; set; }

	private void OnEnable()
	{
		justEnabled_NeedsToSetDefaultSelection = true;
	}

	public void GamepadConfirmButtonHit()
	{
		int index = 0;
		craftingController.ConfirmButtonHit(index);
	}

	public void GamepadCancelButtonHit()
	{
		craftingController.CancelButtonHit();
	}

	private void Awake()
	{
		eventFunctions = GetComponent<EventFunctions>();
		ScoreboardController.onScoreboardOpen += DestroyIt;
		PauseManager.onPauseStartGlobal = (Action)Delegate.Combine(PauseManager.onPauseStartGlobal, new Action(DestroyIt));
		mpEventSystemLocator = GetComponent<MPEventSystemLocator>();
		pickupPickerPanel = GetComponent<PickupPickerPanel>();
		buttonAllocator = new UIElementAllocator<MPButton>(ingredientContainer, slotPrefab);
		buttonAllocator.onCreateElement = OnCreateButton;
		gridlayoutGroup.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		gridlayoutGroup.constraintCount = slotCount;
		buttonAllocator.AllocateElements(slotCount);
		if ((bool)confirmButton)
		{
			confirmButton.onClick.AddListener(delegate
			{
				craftingController.ConfirmSelection();
				eventFunctions.DestroySelf();
			});
		}
	}

	private void OnDestroy()
	{
		ScoreboardController.onScoreboardOpen -= DestroyIt;
		PauseManager.onPauseStartGlobal = (Action)Delegate.Remove(PauseManager.onPauseStartGlobal, new Action(DestroyIt));
	}

	private void DestroyIt()
	{
		UnityEngine.Object.Destroy(base.gameObject);
	}

	private void OnCreateButton(int index, MPButton button)
	{
		button.onClick.AddListener(delegate
		{
			craftingController.ClearSlot(index);
			UpdateSlotVisuals(index);
		});
	}

	public void UpdateSlotVisuals(int slotIndex)
	{
		EquipmentIndex equipmentIndexFromSlot = craftingController.GetEquipmentIndexFromSlot(slotIndex);
		ItemIndex itemIndexFromSlot = craftingController.GetItemIndexFromSlot(slotIndex);
		Sprite sprite = null;
		if (itemIndexFromSlot != ItemIndex.None)
		{
			sprite = ItemCatalog.GetItemDef(itemIndexFromSlot).pickupIconSprite;
		}
		else if (equipmentIndexFromSlot != EquipmentIndex.None)
		{
			sprite = EquipmentCatalog.GetEquipmentDef(equipmentIndexFromSlot).pickupIconSprite;
		}
		if (buttonAllocator != null && buttonAllocator.elements != null)
		{
			MPButton mPButton = buttonAllocator.elements[slotIndex];
			Image component = mPButton.GetComponent<ChildLocator>().FindChild("Icon").GetComponent<Image>();
			component.sprite = sprite;
			component.enabled = sprite != null;
			mPButton.interactable = craftingController.ingredients[slotIndex] != PickupIndex.none;
		}
	}

	private void SetSelection(int index)
	{
		if (buttonAllocator.elements != null && buttonAllocator.elements.Count > index)
		{
			MPButton mPButton = buttonAllocator.elements[index];
			if (mPButton.interactable && justEnabled_NeedsToSetDefaultSelection)
			{
				mpEventSystemLocator?.eventSystem?.SetSelectedGameObject(mPButton.gameObject);
				justEnabled_NeedsToSetDefaultSelection = false;
			}
		}
	}

	public void UpdateResult(PickupIndex pickupIndex, int quantity = 1)
	{
		if (!(result != null) || !(resultGlow != null))
		{
			return;
		}
		PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
		if (pickupDef != null)
		{
			Image[] componentsInChildren = resultGlow.GetComponentsInChildren<Image>();
			Color baseColor = pickupDef.baseColor;
			Image[] array = componentsInChildren;
			foreach (Image image in array)
			{
				image.color = new Color(baseColor.r, baseColor.g, baseColor.b, image.color.a);
			}
			result.SetPickupIndex(pickupDef.pickupIndex, quantity, 0f);
		}
	}

	public void UpdateAllVisuals()
	{
		for (int i = 0; i < slotCount; i++)
		{
			UpdateSlotVisuals(i);
		}
		bool flag = craftingController.AllSlotsFilled() && craftingController.bestFitRecipe != null;
		result.gameObject.SetActive(flag);
		resultGlow.SetActive(flag);
		if (flag)
		{
			UpdateResult(craftingController.bestFitRecipe.result, craftingController.bestFitRecipe.amountToDrop);
		}
		confirmButton.interactable = flag;
		if (flag && !previouslyHadPossibleResult)
		{
			mpEventSystemLocator?.eventSystem?.SetSelectedGameObject(confirmButton.gameObject);
			justEnabled_NeedsToSetDefaultSelection = false;
		}
		previouslyHadPossibleResult = flag;
		if (flag || !justEnabled_NeedsToSetDefaultSelection || buttonAllocator == null || buttonAllocator.elements == null)
		{
			return;
		}
		bool flag2 = false;
		int count = buttonAllocator.elements.Count;
		for (int j = 0; j < count; j++)
		{
			if (buttonAllocator.elements[j].interactable)
			{
				SetSelection(j);
				flag2 = true;
				break;
			}
		}
		if (!flag2)
		{
			SetSelection(0);
		}
	}
}
