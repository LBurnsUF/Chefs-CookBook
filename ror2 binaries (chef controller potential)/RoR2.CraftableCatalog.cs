// RoR2, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null
// RoR2.CraftableCatalog
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HG;
using RoR2;
using RoR2.ContentManagement;
using RoR2.ExpansionManagement;
using UnityEngine;

public static class CraftableCatalog
{
	[Serializable]
	public class RecipeEntry
	{
		public Recipe recipe;

		[HideInInspector]
		public PickupIndex result;

		public int amountToDrop = 1;

		public IngredientSlotEntry[] possibleIngredients;

		public List<PickupIndex> GetAllPickups()
		{
			List<PickupIndex> list = new List<PickupIndex>();
			IngredientSlotEntry[] array = possibleIngredients;
			foreach (IngredientSlotEntry ingredientSlotEntry in array)
			{
				list.AddRange(ingredientSlotEntry.pickups);
			}
			return list;
		}

		public bool IsSlotFilled(IngredientSlotEntry slot, PickupIndex[] choices)
		{
			for (int i = 0; i < choices.Length; i++)
			{
				if (slot.pickups.Contains(choices[i]))
				{
					return true;
				}
			}
			return false;
		}

		public bool IsSlotFilled(IngredientSlotEntry slot, PickupIndex choice)
		{
			return slot.pickups.Contains(choice);
		}

		public bool ValidateSelection(PickupIndex[] choices)
		{
			int num = 0;
			bool[] array = new bool[possibleIngredients.Length];
			PickupIndex[] dest = new PickupIndex[choices.Length];
			ArrayUtils.CloneTo(choices, ref dest);
			for (int i = 0; i < possibleIngredients.Length; i++)
			{
				IngredientSlotEntry ingredientSlotEntry = possibleIngredients[i];
				for (int j = 0; j < dest.Length; j++)
				{
					PickupIndex pickupIndex = dest[j];
					if (pickupIndex == PickupIndex.none || !ingredientSlotEntry.pickups.Contains(pickupIndex))
					{
						continue;
					}
					dest[j] = PickupIndex.none;
					if (!array[i])
					{
						num++;
						array[i] = true;
						if (num >= possibleIngredients.Length)
						{
							return true;
						}
					}
				}
			}
			return num >= possibleIngredients.Length;
		}

		public bool ValidateSelection(Inventory inventory)
		{
			int num = 0;
			bool[] array = new bool[possibleIngredients.Length];
			_ = inventory.itemAcquisitionOrder;
			for (int i = 0; i < possibleIngredients.Length; i++)
			{
				IngredientSlotEntry ingredientSlotEntry = possibleIngredients[i];
				for (int j = 0; j < ingredientSlotEntry.pickups.Length; j++)
				{
					_ = ingredientSlotEntry.pickups;
					if (ingredientSlotEntry.Validate(inventory) && !array[i])
					{
						num++;
						array[i] = true;
						if (num >= possibleIngredients.Length)
						{
							return true;
						}
					}
				}
			}
			return num >= possibleIngredients.Length;
		}

		public List<PickupIndex> GetOtherIngredients(PickupIndex[] choices)
		{
			List<PickupIndex> list = new List<PickupIndex>();
			IngredientSlotEntry[] array = RemainingSlots(choices);
			foreach (IngredientSlotEntry ingredientSlotEntry in array)
			{
				list.AddRange(ingredientSlotEntry.pickups);
			}
			return list;
		}

		public int Required(PickupIndex pickupIndex)
		{
			int num = 0;
			IngredientSlotEntry[] array = possibleIngredients;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].pickups.Contains(pickupIndex))
				{
					num++;
				}
			}
			return num;
		}

		public IngredientSlotEntry[] RemainingSlots(PickupIndex[] choices)
		{
			List<IngredientSlotEntry> list = new List<IngredientSlotEntry>();
			bool[] array = new bool[possibleIngredients.Length];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = false;
			}
			PickupIndex[] dest = new PickupIndex[choices.Length];
			ArrayUtils.CloneTo(choices, ref dest);
			for (int j = 0; j < possibleIngredients.Length; j++)
			{
				IngredientSlotEntry ingredientSlotEntry = possibleIngredients[j];
				for (int k = 0; k < dest.Length; k++)
				{
					PickupIndex pickupIndex = dest[k];
					if (!(pickupIndex == PickupIndex.none) && ingredientSlotEntry.pickups.Contains(pickupIndex))
					{
						dest[k] = PickupIndex.none;
						array[j] = true;
					}
				}
			}
			for (int l = 0; l < array.Length; l++)
			{
				if (!array[l])
				{
					list.Add(possibleIngredients[l]);
				}
			}
			return list.ToArray();
		}

		public bool IsUsedInMultipleSlots(PickupIndex pickupIndex)
		{
			int num = 0;
			IngredientSlotEntry[] array = possibleIngredients;
			for (int i = 0; i < array.Length; i++)
			{
				if (array[i].pickups.Contains(pickupIndex))
				{
					num++;
				}
			}
			return num > 1;
		}
	}

	public class IngredientSlotEntry
	{
		public IngredientTypeIndex type;

		public int slotIndex;

		public PickupIndex[] pickups;

		public IngredientSlotEntry(int index)
		{
			slotIndex = index;
		}

		public bool Validate(PickupIndex pickupIndex)
		{
			return pickups.Contains(pickupIndex);
		}

		public bool Validate(Inventory inventory)
		{
			int num = pickups.Length;
			for (int i = 0; i < num; i++)
			{
				EquipmentIndex equipmentIndex = inventory.GetEquipmentIndex();
				if (equipmentIndex != EquipmentIndex.None && equipmentIndex == pickups[i].equipmentIndex)
				{
					return true;
				}
				if (inventory.GetItemCountPermanent(pickups[i].itemIndex) > 0)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static ResourceAvailability availability = default(ResourceAvailability);

	private static CraftableDef[] craftableDefs;

	private static List<RecipeEntry> allRecipes;

	public static Dictionary<PickupIndex, List<RecipeEntry>> pickupToIngredientSearchTable = new Dictionary<PickupIndex, List<RecipeEntry>>();

	public static Dictionary<PickupIndex, List<RecipeEntry>> resultToRecipeSearchTable = new Dictionary<PickupIndex, List<RecipeEntry>>();

	public static int recipeCount
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		get
		{
			if (ContentManager._craftableDefs == null)
			{
				return 0;
			}
			return ContentManager._craftableDefs.Length;
		}
	}

	[SystemInitializer(new Type[] { typeof(PickupCatalog) })]
	private static void Init()
	{
		SetCraftableDefs(ContentManager.craftableDefs);
		availability.MakeAvailable();
	}

	public static void SetCraftableDefs(CraftableDef[] newCraftableDefs)
	{
		ArrayUtils.CloneTo(newCraftableDefs, ref craftableDefs);
		allRecipes = new List<RecipeEntry>();
		for (int i = 0; i < ContentManager._craftableDefs.Length; i++)
		{
			CraftableDef craftableDef = ContentManager._craftableDefs[i];
			for (int j = 0; j < craftableDef.recipes.Length; j++)
			{
				Recipe recipe = craftableDef.recipes[j];
				recipe.craftableDef = craftableDef;
				recipe.indexInCraftableDef = j;
				RecipeIngredient[] ingredients = recipe.ingredients;
				foreach (RecipeIngredient recipeIngredient in ingredients)
				{
					recipeIngredient.pickupIndex = PickupIndex.none;
					if (recipeIngredient.IsDefinedPickup())
					{
						ItemDef itemDef = recipeIngredient.pickup as ItemDef;
						EquipmentDef equipmentDef = recipeIngredient.pickup as EquipmentDef;
						if (itemDef != null && itemDef.itemIndex != ItemIndex.None)
						{
							recipeIngredient.pickupIndex = PickupCatalog.FindPickupIndex(itemDef.itemIndex);
						}
						else if (equipmentDef != null && equipmentDef.equipmentIndex != EquipmentIndex.None)
						{
							recipeIngredient.pickupIndex = PickupCatalog.FindPickupIndex(equipmentDef.equipmentIndex);
						}
					}
				}
				allRecipes.Add(new RecipeEntry
				{
					recipe = recipe,
					result = craftableDef.GetPickupDefFromResult().pickupIndex,
					amountToDrop = recipe.amountToDrop,
					possibleIngredients = null
				});
			}
		}
		IEnumerable<PickupDef> allPickups = PickupCatalog.allPickups;
		foreach (RecipeEntry allRecipe in allRecipes)
		{
			if (!resultToRecipeSearchTable.ContainsKey(allRecipe.result))
			{
				resultToRecipeSearchTable.Add(allRecipe.result, new List<RecipeEntry>());
			}
			resultToRecipeSearchTable[allRecipe.result].Add(allRecipe);
			List<PickupIndex> list = new List<PickupIndex>();
			allRecipe.possibleIngredients = new IngredientSlotEntry[allRecipe.recipe.ingredients.Length];
			for (int l = 0; l < allRecipe.recipe.ingredients.Length; l++)
			{
				IngredientSlotEntry ingredientSlotEntry = new IngredientSlotEntry(l);
				ingredientSlotEntry.type = allRecipe.recipe.ingredients[l].type;
				list = new List<PickupIndex>();
				RecipeIngredient recipeIngredient2 = allRecipe.recipe.ingredients[l];
				foreach (PickupDef item in allPickups)
				{
					if (recipeIngredient2.Validate(item.pickupIndex))
					{
						list.Add(item.pickupIndex);
						if (!pickupToIngredientSearchTable.ContainsKey(item.pickupIndex))
						{
							pickupToIngredientSearchTable.Add(item.pickupIndex, new List<RecipeEntry>());
						}
						pickupToIngredientSearchTable[item.pickupIndex].Add(allRecipe);
						ingredientSlotEntry.pickups = list.ToArray();
					}
				}
				ingredientSlotEntry.slotIndex = l;
				allRecipe.possibleIngredients[l] = ingredientSlotEntry;
			}
		}
	}

	public static bool IsCompletable(PickupIndex ingredient, Inventory inventory)
	{
		if (!pickupToIngredientSearchTable.TryGetValue(ingredient, out var value))
		{
			return false;
		}
		foreach (RecipeEntry item in value)
		{
			if (item.ValidateSelection(inventory))
			{
				return true;
			}
		}
		return false;
	}

	public static bool ValidateRecipe(RecipeEntry recipe, PickupIndex[] choices)
	{
		bool result = true;
		PickupIndex[] dest = new PickupIndex[choices.Length];
		ArrayUtils.CloneTo(choices, ref dest);
		bool[] array = new bool[recipe.possibleIngredients.Length];
		for (int i = 0; i < recipe.possibleIngredients.Length; i++)
		{
			array[i] = false;
		}
		for (int j = 0; j < recipe.possibleIngredients.Length; j++)
		{
			IngredientSlotEntry ingredientSlotEntry = recipe.possibleIngredients[j];
			for (int k = 0; k < dest.Length; k++)
			{
				if (!array[ingredientSlotEntry.slotIndex] && ingredientSlotEntry.Validate(dest[k]))
				{
					array[ingredientSlotEntry.slotIndex] = true;
					dest[k] = PickupIndex.none;
				}
			}
		}
		for (int l = 0; l < array.Length; l++)
		{
			if (!array[l])
			{
				result = false;
			}
		}
		return result;
	}

	public static RecipeEntry[] FindApplicableRecipesByIngredients(PickupIndex[] choices)
	{
		if (choices.Length == 0)
		{
			return allRecipes.ToArray();
		}
		List<RecipeEntry> list = new List<RecipeEntry>();
		foreach (RecipeEntry allRecipe in allRecipes)
		{
			if (ValidateRecipe(allRecipe, choices))
			{
				list.Add(allRecipe);
			}
		}
		return list.ToArray();
	}

	public static RecipeEntry[] FindRecipesThatCanAcceptIngredients(PickupIndex[] choices)
	{
		if (choices.Length == 0)
		{
			return allRecipes.ToArray();
		}
		List<RecipeEntry> list = new List<RecipeEntry>();
		foreach (RecipeEntry allRecipe in allRecipes)
		{
			foreach (PickupIndex pickupIndex in choices)
			{
				if (allRecipe.recipe.CheckIfValidIngredient(pickupIndex))
				{
					list.Add(allRecipe);
				}
			}
		}
		return list.ToArray();
	}

	public static RecipeEntry[] FindAllRelatedRecipes(PickupIndex pickupIndex)
	{
		List<RecipeEntry> list = new List<RecipeEntry>();
		if (resultToRecipeSearchTable.ContainsKey(pickupIndex))
		{
			foreach (RecipeEntry item in resultToRecipeSearchTable[pickupIndex])
			{
				if (!list.Contains(item))
				{
					list.Add(item);
				}
			}
		}
		if (pickupToIngredientSearchTable.ContainsKey(pickupIndex))
		{
			foreach (RecipeEntry item2 in pickupToIngredientSearchTable[pickupIndex])
			{
				if (!list.Contains(item2))
				{
					list.Add(item2);
				}
			}
		}
		return list.ToArray();
	}

	public static RecipeEntry[] GetAllRecipes()
	{
		return allRecipes.ToArray();
	}

	public static void FilterByEntitlement(ref RecipeEntry[] source)
	{
		List<RecipeEntry> list = new List<RecipeEntry>();
		foreach (RecipeEntry item in new List<RecipeEntry>(source))
		{
			if (CheckForEntitlement(item.result))
			{
				list.Add(item);
			}
		}
		source = list.ToArray();
		static bool CheckForEntitlement(PickupIndex index)
		{
			PickupDef pickupDef = PickupCatalog.GetPickupDef(index);
			if (pickupDef.itemIndex != ItemIndex.None)
			{
				ItemDef itemDef = ItemCatalog.GetItemDef(pickupDef.itemIndex);
				if (itemDef != null)
				{
					ExpansionDef requiredExpansion = itemDef.requiredExpansion;
					if (requiredExpansion != null)
					{
						return Run.instance.IsExpansionEnabled(requiredExpansion);
					}
				}
			}
			else if (pickupDef.equipmentIndex != EquipmentIndex.None)
			{
				EquipmentDef equipmentDef = EquipmentCatalog.GetEquipmentDef(pickupDef.equipmentIndex);
				if (equipmentDef != null)
				{
					ExpansionDef requiredExpansion2 = equipmentDef.requiredExpansion;
					if (requiredExpansion2 != null)
					{
						return Run.instance.IsExpansionEnabled(requiredExpansion2);
					}
				}
			}
			return true;
		}
	}
}
