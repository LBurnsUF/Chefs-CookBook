// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.CraftableDef
using System;
using RoR2;
using UnityEngine;

[CreateAssetMenu(menuName = "RoR2/CraftableDef")]
public class CraftableDef : ScriptableObject
{
	[TypeRestrictedReference(new Type[]
	{
		typeof(ItemDef),
		typeof(EquipmentDef)
	})]
	public UnityEngine.Object pickup;

	public Recipe[] recipes;

	[HideInInspector]
	public ItemIndex itemIndex = ItemIndex.None;

	[HideInInspector]
	public EquipmentIndex equipmentIndex = EquipmentIndex.None;

	public PickupDef GetPickupDefFromResult()
	{
		PickupDef result = null;
		if (pickup is ItemDef)
		{
			result = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex((pickup as ItemDef).itemIndex));
		}
		else if (pickup is EquipmentDef)
		{
			result = PickupCatalog.GetPickupDef(PickupCatalog.FindPickupIndex((pickup as EquipmentDef).equipmentIndex));
		}
		return result;
	}
}
