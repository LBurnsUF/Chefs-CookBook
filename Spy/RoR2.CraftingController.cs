// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.CraftingController
using System.Collections.Generic;
using System.Linq;
using HG;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

[RequireComponent(typeof(NetworkUIPromptController))]
public class CraftingController : PickupPickerController
{
	[Min(1f)]
	[Tooltip("The total amount of Ingredients the crafter can support.")]
	public int ingredientCount = 1;

	private MPEventSystem mpEventSystem;

	private MealPrepController mealPrepController;

	private CraftingPanel panelController;

	private PickupIndex[] _ingredients;

	[Tooltip("Use this event to process result data.")]
	public UnityEvent<Interactor, PickupIndex[], PickupIndex, int> OnResultConfirmed;

	private CraftableCatalog.RecipeEntry[] _possibleRecipes;

	private PickupIndex result;

	private int amountToDrop;

	[Header("Override colors for if ingredient can be used to complete a recipe")]
	public Color ValidSelectedColor;

	public Color InvalidBackgroundSelectionColor;

	private const byte msgSubmit = 0;

	private const byte msgCancel = 1;

	private const byte msgConfirm = 2;

	private const byte msgClear = 3;

	private static readonly uint optionsDirtyBit;

	private static readonly uint availableDirtyBit;

	private static readonly uint ingredientsDirtyBit;

	private static readonly uint resultDirtyBit;

	private static readonly uint allDirtyBits;

	private static int kRpcRpcHandlePickupSelected;

	public PickupIndex[] ingredients
	{
		get
		{
			return _ingredients;
		}
		set
		{
			_ingredients = value;
			OnIngredientsChanged();
		}
	}

	public CraftableCatalog.RecipeEntry bestFitRecipe { get; private set; }

	private void OnIngredientsChanged()
	{
		AttemptFindPossibleRecipes();
		FilterAvailableOptions();
		if (panelController != null)
		{
			panelController.UpdateAllVisuals();
		}
		if (panelInstanceController != null)
		{
			panelInstanceController.SetPickupOptions(options);
			SetDirtyBit(optionsDirtyBit);
		}
	}

	private void AttemptFindPossibleRecipes()
	{
		_possibleRecipes = CraftableCatalog.GetAllRecipes();
		if (!AllSlotsEmpty())
		{
			_possibleRecipes = CraftableCatalog.FindRecipesThatCanAcceptIngredients(ingredients);
		}
		CraftableCatalog.FilterByEntitlement(ref _possibleRecipes);
		if (!AllSlotsFilled())
		{
			return;
		}
		bestFitRecipe = null;
		for (int i = 0; i < _possibleRecipes.Length; i++)
		{
			CraftableCatalog.RecipeEntry recipeEntry = _possibleRecipes[i];
			if (recipeEntry != null && CraftableCatalog.ValidateRecipe(recipeEntry, ingredients))
			{
				bestFitRecipe = recipeEntry;
			}
		}
		if (bestFitRecipe != null)
		{
			result = bestFitRecipe.result;
			amountToDrop = bestFitRecipe.amountToDrop;
			SetDirtyBit(resultDirtyBit);
		}
	}

	[Server]
	protected override void HandlePickupSelected(int choiceIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.CraftingController::HandlePickupSelected(System.Int32)' called on client");
		}
		else
		{
			if ((uint)choiceIndex >= options.Length)
			{
				return;
			}
			ref Option reference = ref options[choiceIndex];
			if (reference.available)
			{
				onPickupSelected?.Invoke(reference.pickup.pickupIndex.value);
				onUniquePickupSelected?.Invoke(reference.pickup);
				if (synchronizeItemSelectionAcrossNetwork)
				{
					((PickupPickerController)this).CallRpcHandlePickupSelected(choiceIndex);
				}
			}
		}
	}

	[ClientRpc]
	protected override void RpcHandlePickupSelected(int choiceIndex)
	{
		if ((uint)choiceIndex < options.Length)
		{
			_ = options[choiceIndex].available;
		}
	}

	private void FilterAvailableOptions()
	{
		Option[] array = options;
		if (array.Length == 0)
		{
			return;
		}
		HashSet<PickupIndex> hashSet = new HashSet<PickupIndex>();
		CraftableCatalog.RecipeEntry[] possibleRecipes = _possibleRecipes;
		foreach (CraftableCatalog.RecipeEntry obj in possibleRecipes)
		{
			List<PickupIndex> list = obj.GetAllPickups();
			List<PickupIndex> otherIngredients = obj.GetOtherIngredients(ingredients);
			if (otherIngredients != null && otherIngredients.Count > 0)
			{
				list = otherIngredients;
			}
			if (list == null)
			{
				continue;
			}
			foreach (PickupIndex item in list)
			{
				if (!hashSet.Contains(item))
				{
					hashSet.Add(item);
				}
			}
		}
		PickupIndex[] array2 = new PickupIndex[1];
		Inventory inventory = networkUIPromptController.currentParticipantMaster.inventory;
		for (int j = 0; j < array.Length; j++)
		{
			if (AllSlotsFilled() || !CanTakePickupAfterUseByIngredientSlots(array[j].pickup.pickupIndex, ingredients))
			{
				array[j].available = false;
			}
			else
			{
				bool flag = hashSet.Contains(options[j].pickup.pickupIndex);
				array[j].available = flag;
				array2[0] = array[j].pickup.pickupIndex;
				CanTakePickupAfterUseByIngredientSlots(array[j].pickup.pickupIndex, array2);
				if (flag && CraftableCatalog.IsCompletable(array[j].pickup.pickupIndex, inventory))
				{
					array[j].overrideSelectedBGColor = ValidSelectedColor;
				}
			}
			if (!array[j].available)
			{
				array[j].overrideUnavailableBGColor = InvalidBackgroundSelectionColor;
			}
		}
		ArrayUtils.CloneTo(array, ref options);
	}

	private bool CanTakePickupAfterUseByIngredientSlots(PickupIndex pickupIndex, PickupIndex[] consumedIngredients)
	{
		CharacterMaster currentParticipantMaster = networkUIPromptController.currentParticipantMaster;
		if ((bool)currentParticipantMaster)
		{
			PickupDef pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
			if (pickupDef != null)
			{
				if (pickupDef.itemIndex != ItemIndex.None)
				{
					int num = consumedIngredients.Count((PickupIndex p) => p == pickupIndex);
					Inventory inventory = currentParticipantMaster.inventory;
					if ((bool)inventory)
					{
						return num < inventory.GetItemCountPermanent(pickupDef.itemIndex);
					}
				}
				else if (pickupDef.equipmentIndex != EquipmentIndex.None)
				{
					return true;
				}
			}
		}
		return false;
	}

	protected override void OnDisplayBegin(NetworkUIPromptController networkUIPromptController, LocalUser localUser, CameraRigController cameraRigController)
	{
		panelInstance = Object.Instantiate(panelPrefab, cameraRigController.hud.mainContainer.transform);
		panelInstanceController = panelInstance.GetComponent<PickupPickerPanel>();
		panelInstanceController.pickerController = this;
		panelController = panelInstance.GetComponent<CraftingPanel>();
		panelController.craftingController = this;
		if (excludedItemsDropTable == null)
		{
			excludedItemsDropTable = panelInstanceController.excludedItemsDropTable;
		}
		panelInstanceController.SetPickupOptions(options);
		OnDestroyCallback.AddCallback(panelInstance, base.OnPanelDestroyed);
		ClearAllSlots();
		OnIngredientsChanged();
		if ((bool)networkUIPromptController.currentParticipantMaster?.inventory)
		{
			networkUIPromptController.currentParticipantMaster.inventory.onInventoryChanged += OnIngredientsChanged;
		}
	}

	protected override void OnDisplayEnd(NetworkUIPromptController networkUIPromptController, LocalUser localUser, CameraRigController cameraRigController)
	{
		Object.Destroy(panelInstance);
		panelInstance = null;
		panelInstanceController = null;
		ClearAllSlots();
		if ((object)networkUIPromptController.currentParticipantMaster?.inventory != null)
		{
			networkUIPromptController.currentParticipantMaster.inventory.onInventoryChanged -= OnIngredientsChanged;
		}
	}

	private void Awake()
	{
		mealPrepController = GetComponent<MealPrepController>();
		networkUIPromptController = GetComponent<NetworkUIPromptController>();
		if (NetworkClient.active)
		{
			networkUIPromptController.onDisplayBegin += OnDisplayBegin;
			networkUIPromptController.onDisplayEnd += OnDisplayEnd;
		}
		if (NetworkServer.active)
		{
			networkUIPromptController.messageFromClientHandler = HandleClientMessage;
		}
		ingredients = new PickupIndex[ingredientCount];
		ClearAllSlots();
	}

	protected override void HandleClientMessage(NetworkReader reader)
	{
		switch (reader.ReadByte())
		{
		case 0:
		{
			int choiceIndex = reader.ReadInt32();
			HandlePickupSelected(choiceIndex);
			break;
		}
		case 1:
			networkUIPromptController.SetParticipantMaster(null);
			break;
		case 2:
			OnConfirmServer();
			break;
		case 3:
		{
			int index = reader.ReadInt32();
			ClearSlot(index);
			break;
		}
		}
	}

	[Server]
	public void OnConfirmServer()
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.CraftingController::OnConfirmServer()' called on client");
			return;
		}
		Interactor component = networkUIPromptController.currentParticipantMaster.GetBodyObject().GetComponent<Interactor>();
		if (OnResultConfirmed != null)
		{
			OnResultConfirmed.Invoke(component, ingredients, bestFitRecipe.result, bestFitRecipe.amountToDrop);
			return;
		}
		Debug.LogErrorFormat("CraftingController #{0} has no methods subscribing to OnResultConfirmed!", base.gameObject.GetInstanceID());
	}

	public void ConfirmSelection()
	{
		if (!NetworkServer.active)
		{
			NetworkWriter networkWriter = networkUIPromptController.BeginMessageToServer();
			networkWriter.Write((byte)2);
			networkUIPromptController.FinishMessageToServer(networkWriter);
		}
		else
		{
			OnConfirmServer();
		}
	}

	public void SendToMealprep(Interactor activator)
	{
		if ((bool)mealPrepController)
		{
			mealPrepController.BeginCooking(activator, ingredients, bestFitRecipe.result, bestFitRecipe.amountToDrop);
		}
	}

	public bool AllSlotsFilled()
	{
		for (int i = 0; i < ingredientCount; i++)
		{
			if (!IsIngredientSlotFilled(i))
			{
				return false;
			}
		}
		return true;
	}

	public bool AllSlotsEmpty()
	{
		for (int i = 0; i < ingredientCount; i++)
		{
			if (IsIngredientSlotFilled(i))
			{
				return false;
			}
		}
		return true;
	}

	public bool IsIngredientSlotFilled(int index)
	{
		PickupIndex safe = ArrayUtils.GetSafe(ingredients, index);
		if (safe != PickupIndex.none)
		{
			return safe.isValid;
		}
		return false;
	}

	protected override List<Option> GetGeneratedOptionsFromInteractor(Interactor activator)
	{
		if (!activator)
		{
			Debug.Log("No activator.");
			return null;
		}
		CharacterBody component = activator.GetComponent<CharacterBody>();
		if (!component)
		{
			Debug.Log("No body.");
			return null;
		}
		Inventory inventory = component.inventory;
		if (!inventory)
		{
			Debug.Log("No inventory.");
			return null;
		}
		List<Option> list = new List<Option>();
		EquipmentIndex equipmentIndex = inventory.GetEquipmentIndex();
		if (equipmentIndex != EquipmentIndex.None)
		{
			list.Add(new Option
			{
				pickup = new UniquePickup(PickupCatalog.FindPickupIndex(equipmentIndex)),
				available = true
			});
		}
		for (int i = 0; i < inventory.itemAcquisitionOrder.Count; i++)
		{
			ItemIndex itemIndex = inventory.itemAcquisitionOrder[i];
			ItemDef itemDef = ItemCatalog.GetItemDef(itemIndex);
			ItemTierCatalog.GetItemTierDef(itemDef.tier);
			PickupIndex pickupIndex = PickupCatalog.FindPickupIndex(itemIndex);
			int itemCountPermanent = inventory.GetItemCountPermanent(itemIndex);
			if ((itemDef.canRemove && !itemDef.hidden && itemCountPermanent > 0) || itemDef.ContainsTag(ItemTag.AllowedForUseAsCraftingIngredient))
			{
				list.Add(new Option
				{
					available = true,
					pickup = new UniquePickup(pickupIndex)
				});
			}
		}
		return list;
	}

	public int FindFirstAvailableIngredientSlot()
	{
		for (int i = 0; i < ingredientCount; i++)
		{
			if (!IsIngredientSlotFilled(i))
			{
				return i;
			}
		}
		return -1;
	}

	public int FindLastUsedIngredientSlot()
	{
		int num = 0;
		for (int i = 0; i < ingredientCount; i++)
		{
			if (!IsIngredientSlotFilled(i))
			{
				return Mathf.Clamp(num - 1, 0, ingredientCount - 1);
			}
			num++;
		}
		return ingredientCount - 1;
	}

	public ItemIndex GetItemIndexFromSlot(int index)
	{
		return PickupCatalog.GetPickupDef(ingredients[index])?.itemIndex ?? ItemIndex.None;
	}

	public EquipmentIndex GetEquipmentIndexFromSlot(int index)
	{
		return PickupCatalog.GetPickupDef(ingredients[index])?.equipmentIndex ?? EquipmentIndex.None;
	}

	public void SendToSlot(int index)
	{
		if (!AllSlotsFilled())
		{
			PickupIndex pickupIndex = new PickupIndex(index);
			int num = FindFirstAvailableIngredientSlot();
			if (num > -1)
			{
				SetSlot(pickupIndex, num);
			}
		}
	}

	[Server]
	private void UpdateSlotAndDirty(PickupIndex pickupIndex, int slot)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.CraftingController::UpdateSlotAndDirty(RoR2.PickupIndex,System.Int32)' called on client");
			return;
		}
		UpdateSlot(pickupIndex, slot);
		SetDirtyBit(ingredientsDirtyBit);
	}

	private void UpdateSlot(PickupIndex pickupIndex, int slot)
	{
		ingredients[slot] = pickupIndex;
		OnIngredientsChanged();
	}

	private void SetSlot(PickupIndex pickupIndex, int index)
	{
		UpdateSlotAndDirty(pickupIndex, index);
	}

	public void ClearSlot(int index)
	{
		if (NetworkServer.active)
		{
			SetSlot(PickupIndex.none, index);
		}
		else if (NetworkClient.active)
		{
			NetworkWriter networkWriter = networkUIPromptController.BeginMessageToServer();
			networkWriter.Write((byte)3);
			networkWriter.Write(index);
			networkUIPromptController.FinishMessageToServer(networkWriter);
		}
	}

	public void ConfirmButtonHit(int index)
	{
		if (AllSlotsFilled() && result != PickupIndex.none)
		{
			ConfirmSelection();
		}
		else
		{
			SendToSlot(index);
		}
	}

	public void CancelButtonHit()
	{
		if (AllSlotsEmpty())
		{
			panelController.eventFunctions.DestroySelf();
		}
		else
		{
			ClearSlot(FindLastUsedIngredientSlot());
		}
	}

	public void ClearAllSlots()
	{
		for (int i = 0; i < ingredientCount; i++)
		{
			ClearSlot(i);
		}
	}

	public override bool OnSerialize(NetworkWriter writer, bool initialState)
	{
		uint num = base.syncVarDirtyBits;
		if (initialState)
		{
			num = allDirtyBits;
		}
		bool num2 = (num & optionsDirtyBit) != 0;
		bool flag = (num & ingredientsDirtyBit) != 0;
		bool flag2 = (num & resultDirtyBit) != 0;
		bool flag3 = (num & availableDirtyBit) != 0;
		writer.WritePackedUInt32(num);
		if (num2)
		{
			writer.WritePackedUInt32((uint)options.Length);
			for (int i = 0; i < options.Length; i++)
			{
				ref Option reference = ref options[i];
				UniquePickup state = reference.pickup;
				writer.Write(in state);
				writer.Write(reference.available);
			}
		}
		if (flag)
		{
			writer.WritePackedUInt32((uint)ingredients.Length);
			for (int j = 0; j < ingredients.Length; j++)
			{
				writer.Write(ingredients[j]);
			}
		}
		if (flag2)
		{
			writer.Write(result);
			writer.WritePackedUInt32((uint)amountToDrop);
		}
		if (flag3)
		{
			writer.Write(base.available);
		}
		return num != 0;
	}

	public override void OnDeserialize(NetworkReader reader, bool initialState)
	{
		uint num = reader.ReadPackedUInt32();
		bool flag = (num & optionsDirtyBit) != 0;
		bool flag2 = (num & ingredientsDirtyBit) != 0;
		bool flag3 = (num & resultDirtyBit) != 0;
		bool flag4 = (num & availableDirtyBit) != 0;
		if (flag)
		{
			Option[] array = new Option[reader.ReadPackedUInt32()];
			for (int i = 0; i < array.Length; i++)
			{
				ref Option reference = ref array[i];
				reference.pickup = reader.ReadUniquePickup();
				reference.available = reader.ReadBoolean();
			}
			SetOptionsInternal(array);
		}
		if (flag2)
		{
			PickupIndex[] array2 = new PickupIndex[reader.ReadPackedUInt32()];
			for (int j = 0; j < array2.Length; j++)
			{
				ref PickupIndex reference2 = ref array2[j];
				reference2 = reader.ReadPickupIndex();
				UpdateSlot(reference2, j);
			}
		}
		if (flag3)
		{
			result = reader.ReadPickupIndex();
			amountToDrop = (int)reader.ReadPackedUInt32();
		}
		if (flag4)
		{
			base.available = reader.ReadBoolean();
		}
	}

	static CraftingController()
	{
		optionsDirtyBit = 1u;
		availableDirtyBit = 2u;
		ingredientsDirtyBit = 4u;
		resultDirtyBit = 8u;
		allDirtyBits = optionsDirtyBit | availableDirtyBit | ingredientsDirtyBit | resultDirtyBit;
		kRpcRpcHandlePickupSelected = -1191714349;
		NetworkBehaviour.RegisterRpcDelegate(typeof(CraftingController), kRpcRpcHandlePickupSelected, InvokeRpcRpcHandlePickupSelected);
		NetworkCRC.RegisterBehaviour("CraftingController", 0);
	}

	private void UNetVersion()
	{
	}

	protected new static void InvokeRpcRpcHandlePickupSelected(NetworkBehaviour obj, NetworkReader reader)
	{
		if (!NetworkClient.active)
		{
			Debug.LogError("RPC RpcHandlePickupSelected called on server.");
		}
		else
		{
			((CraftingController)obj).RpcHandlePickupSelected((int)reader.ReadPackedUInt32());
		}
	}

	public new void CallRpcHandlePickupSelected(int choiceIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogError("RPC Function RpcHandlePickupSelected called on client.");
			return;
		}
		NetworkWriter networkWriter = new NetworkWriter();
		networkWriter.Write((short)0);
		networkWriter.Write((short)2);
		networkWriter.WritePackedUInt32((uint)kRpcRpcHandlePickupSelected);
		networkWriter.Write(GetComponent<NetworkIdentity>().netId);
		networkWriter.WritePackedUInt32((uint)choiceIndex);
		SendRPCInternal(networkWriter, 0, "RpcHandlePickupSelected");
	}

	public override void PreStartClient()
	{
		base.PreStartClient();
	}
}
