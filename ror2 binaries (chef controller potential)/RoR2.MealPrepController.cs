// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.MealPrepController
using System.Collections.Generic;
using EntityStates.MealPrep;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

public class MealPrepController : NetworkBehaviour
{
	public CraftingController craftingController;

	public GameObject pickupTakenOrbPrefab;

	public EntityStateMachine esm;

	public void BeginCooking(Interactor activator, PickupIndex[] itemsToTake, PickupIndex reward, int count)
	{
		BeginCookingServer(activator, itemsToTake, reward, count);
	}

	public bool IsAffordable(PickupIndex[] itemsToTake, CharacterBody activatorBody)
	{
		Dictionary<ItemIndex, int> dictionary = new Dictionary<ItemIndex, int>();
		for (int i = 0; i < itemsToTake.Length; i++)
		{
			PickupDef pickupDef = PickupCatalog.GetPickupDef(itemsToTake[i]);
			ItemIndex itemIndex = pickupDef?.itemIndex ?? ItemIndex.None;
			EquipmentIndex equipmentIndex = pickupDef?.equipmentIndex ?? EquipmentIndex.None;
			if (itemIndex != ItemIndex.None)
			{
				if (!dictionary.ContainsKey(itemIndex))
				{
					dictionary[itemIndex] = 0;
				}
				if (activatorBody.inventory.GetItemCountPermanent(itemIndex) < ++dictionary[itemIndex])
				{
					return false;
				}
			}
			else if (equipmentIndex != EquipmentIndex.None && !activatorBody.inventory.HasEquipment(equipmentIndex))
			{
				return false;
			}
		}
		return true;
	}

	[Server]
	public void BeginCookingServer(Interactor activator, PickupIndex[] itemsToTake, PickupIndex reward, int count)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.MealPrepController::BeginCookingServer(RoR2.Interactor,RoR2.PickupIndex[],RoR2.PickupIndex,System.Int32)' called on client");
			return;
		}
		CharacterBody component = activator.GetComponent<CharacterBody>();
		Inventory inventory = component.inventory;
		if (!IsAffordable(itemsToTake, component))
		{
			return;
		}
		for (int i = 0; i < itemsToTake.Length; i++)
		{
			PickupDef pickupDef = PickupCatalog.GetPickupDef(itemsToTake[i]);
			ItemIndex itemIndex = pickupDef.itemIndex;
			EquipmentIndex equipmentIndex = pickupDef.equipmentIndex;
			if (itemIndex != ItemIndex.None)
			{
				inventory.RemoveItemPermanent(pickupDef.itemIndex);
				CreateItemTakenOrb(activator.transform.position, base.gameObject, pickupDef.pickupIndex);
			}
			else if (equipmentIndex != EquipmentIndex.None)
			{
				inventory.RemoveEquipment(pickupDef.equipmentIndex);
				CreateItemTakenOrb(activator.transform.position, base.gameObject, pickupDef.pickupIndex);
			}
		}
		if ((bool)esm)
		{
			esm.SetNextState(new WaitToBeginCooking
			{
				pickupsToTake = new List<PickupIndex>(itemsToTake),
				pickupToDrop = reward,
				amountToDrop = count
			});
		}
	}

	[Server]
	public void CreateItemTakenOrb(Vector3 effectOrigin, GameObject targetObject, PickupIndex pickupIndex)
	{
		if (!NetworkServer.active)
		{
			Debug.LogWarning("[Server] function 'System.Void RoR2.MealPrepController::CreateItemTakenOrb(UnityEngine.Vector3,UnityEngine.GameObject,RoR2.PickupIndex)' called on client");
			return;
		}
		EffectData effectData = new EffectData
		{
			origin = effectOrigin,
			genericFloat = 1.5f,
			genericUInt = (uint)(pickupIndex.value + 1)
		};
		effectData.SetNetworkedObjectReference(targetObject);
		EffectManager.SpawnEffect(pickupTakenOrbPrefab, effectData, transmit: true);
	}

	private void UNetVersion()
	{
	}

	public override bool OnSerialize(NetworkWriter writer, bool forceAll)
	{
		bool result = default(bool);
		return result;
	}

	public override void OnDeserialize(NetworkReader reader, bool initialState)
	{
	}

	public override void PreStartClient()
	{
	}
}
