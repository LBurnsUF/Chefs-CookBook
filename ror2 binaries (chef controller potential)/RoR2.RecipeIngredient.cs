// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.RecipeIngredient
using System;
using RoR2;
using UnityEngine;

[Serializable]
public class RecipeIngredient
{
	[Tooltip("Must be either an ItemDef or EquipmentDef. If nothing is passed in, it instead must match the criteria specified by the ingredient's type, or the criteria below if no type is specified.")]
	[TypeRestrictedReference(new Type[]
	{
		typeof(ItemDef),
		typeof(EquipmentDef)
	})]
	public UnityEngine.Object pickup;

	public PickupIndex pickupIndex = PickupIndex.none;

	public IngredientTypeIndex type;

	[Tooltip("The item must be in this tier.")]
	[Header("Item Specific")]
	public ItemTier itemTier = ItemTier.NoTier;

	[Tooltip("The item must have all of these tags.")]
	public ItemTag[] requiredTags;

	[Tooltip("The item must have none of these tags.")]
	public ItemTag[] forbiddenTags;

	[Tooltip("The equipment must be a lunar equipment.")]
	[Header("Equipment Specific")]
	public bool isLunar;

	[Tooltip("The equipment must be a boss equipment.")]
	public bool isBoss;

	public bool IsDefinedPickup()
	{
		if (type == IngredientTypeIndex.AssetReference && (pickup != null || pickupIndex != PickupIndex.none))
		{
			return true;
		}
		return false;
	}

	public bool Validate(PickupIndex pickupIndex)
	{
		return IngredientTypeCatalog.GetIngredientTypeDef(type).Validate(this, pickupIndex);
	}
}
