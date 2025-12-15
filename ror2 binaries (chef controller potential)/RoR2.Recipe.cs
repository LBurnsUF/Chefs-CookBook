// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.Recipe
using System;
using RoR2;
using UnityEngine;

[Serializable]
public class Recipe
{
	[Min(1f)]
	public int amountToDrop = 1;

	public RecipeIngredient[] ingredients;

	public int priority;

	[HideInInspector]
	public CraftableDef craftableDef;

	[HideInInspector]
	public int indexInCraftableDef = -1;

	public bool CheckIfValidIngredient(PickupIndex pickupIndex)
	{
		bool result = false;
		if (pickupIndex != PickupIndex.none)
		{
			for (int i = 0; i < ingredients.Length; i++)
			{
				RecipeIngredient recipeIngredient = ingredients[i];
				if (IngredientTypeCatalog.GetIngredientTypeDef(recipeIngredient.type).Validate(recipeIngredient, pickupIndex))
				{
					result = true;
				}
			}
		}
		return result;
	}

	public bool CheckIfValidIngredient(PickupDef pickupDef)
	{
		return CheckIfValidIngredient(pickupDef.pickupIndex);
	}

	public bool Validate(PickupIndex[] choices)
	{
		int num = 0;
		for (int i = 0; i < choices.Length; i++)
		{
			if (CheckIfValidIngredient(PickupCatalog.GetPickupDef(choices[i])))
			{
				num++;
			}
		}
		return num == ingredients.Length;
	}
}
