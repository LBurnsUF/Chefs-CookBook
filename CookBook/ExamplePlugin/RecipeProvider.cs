using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    /// <summary>
    /// Holds all chef crafting recipes.
    /// </summary>
    internal static class RecipeProvider
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _recipesBuilt;

        // Internal storage for recipes
        private static readonly List<ChefRecipe> _recipes = new List<ChefRecipe>();

        /// <summary>Public, read-only view of recipes.</summary>
        internal static IReadOnlyList<ChefRecipe> Recipes => _recipes;

        // fired when recipes are ready to prompt planner build 
        internal static event System.Action<IReadOnlyList<ChefRecipe>> OnRecipesBuilt;

        //--------------------------- LifeCycle -------------------------------
        /// <summary>
        /// Called once from CookBook.Awake().
        /// </summary>
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;

            ContentManager.onContentPacksAssigned += OnContentPacksAssigned; // subscribe to pack events to ensure recipes are built after all other recipes are handled
        }

        internal static void Shutdown()
        {
            ContentManager.onContentPacksAssigned -= OnContentPacksAssigned;
        }

        //--------------------------- ContentPack Tracking -------------------------------
        internal static void OnContentPacksAssigned(HG.ReadOnlyArray<ReadOnlyContentPack> _)
        {
            if (_recipesBuilt)
            {
                return;
            }

            CraftableCatalog.availability.CallWhenAvailable(() =>
            {
                if (_recipesBuilt)
                {
                    return;
                }

                BuildRecipes();
            });
        }

        /// <summary>
        /// Forces a complete rebuild of the recipe cache.
        /// Useful if ItemCatalog/EquipmentCatalog shifts at runtime.
        /// </summary>
        internal static void Rebuild()
        {
            _log.LogWarning("RecipeProvider: ForceRebuild requested. Re-indexing all recipes...");
            BuildRecipes();
        }

        /// <summary>
        /// Build the list of chef recipes from CraftableDef
        /// </summary>
        /// 
        private static void BuildRecipes()
        {
            _recipes.Clear();

            var recipesArray = CraftableCatalog.GetAllRecipes();
            if (recipesArray == null || recipesArray.Length == 0)
            {
                _log.LogWarning("RecipeProvider: no recipes returned from CraftableCatalog.GetAllRecipes().");
                return;
            }

            int itemOffset = ItemCatalog.itemCount;

            foreach (var recipeEntry in recipesArray)
            {
                if (recipeEntry == null)
                {
                    continue;
                }

                // ---------------- Resolve Result Index ----------------
                PickupIndex resultPickup = recipeEntry.result;
                if (!resultPickup.isValid || resultPickup == PickupIndex.none)
                {
                    continue;
                }

                PickupDef resultDef = PickupCatalog.GetPickupDef(resultPickup);
                if (resultDef == null)
                {
                    continue;
                }

                int resultIndex = -1;

                if (resultDef.itemIndex != ItemIndex.None)
                {
                    resultIndex = (int)resultDef.itemIndex;
                }
                else if (resultDef.equipmentIndex != EquipmentIndex.None)
                {
                    resultIndex = itemOffset + (int)resultDef.equipmentIndex;
                }
                else
                {
                    continue;
                }

                int resultCount = recipeEntry.amountToDrop;

                // ---------------- Resolve Ingredient Indices ----------------
                List<PickupIndex> ingredientPickups = recipeEntry.GetAllPickups();
                if (ingredientPickups == null || ingredientPickups.Count == 0)
                {
                    continue;
                }

                var rawIngredients = new List<Ingredient>();

                foreach (var ingPi in ingredientPickups)
                {
                    if (!ingPi.isValid || ingPi == PickupIndex.none)
                    {
                        continue;
                    }

                    PickupDef ingDef = PickupCatalog.GetPickupDef(ingPi);
                    if (ingDef == null)
                    {
                        continue;
                    }

                    int ingIndex = -1;

                    if (ingDef.itemIndex != ItemIndex.None)
                    {
                        ingIndex = (int)ingDef.itemIndex;
                    }
                    else if (ingDef.equipmentIndex != EquipmentIndex.None)
                    {
                        ingIndex = itemOffset + (int)ingDef.equipmentIndex;
                    }

                    if (ingIndex != -1)
                    {
                        rawIngredients.Add(new Ingredient(ingIndex, 1));
                    }
                }

                if (rawIngredients.Count == 0)
                {
                    continue;
                }

                if (rawIngredients.Count <= 2)
                {
                    var recipe = new ChefRecipe(resultIndex, resultCount, rawIngredients.ToArray());
                    _recipes.Add(recipe);
                }
                else
                {
                    var baseIng = rawIngredients[0];
                    for (int i = 1; i < rawIngredients.Count; i++)
                    {
                        var variantIng = rawIngredients[i];
                        var pair = new Ingredient[] { baseIng, variantIng };

                        var recipe = new ChefRecipe(resultIndex, resultCount, pair);
                        _recipes.Add(recipe);
                    }
                }
            }
            _recipesBuilt = true;
            _log.LogInfo($"RecipeProvider: Built {_recipes.Count} explicit recipes from game data.");
            OnRecipesBuilt?.Invoke(_recipes);
        }
    }

    /// <summary>ingredient entry</summary>
    internal readonly struct Ingredient
    {
        public readonly int UnifiedIndex;
        public readonly int Count;

        public Ingredient(int unifiedIndex, int count)
        {
            UnifiedIndex = unifiedIndex;
            Count = count;
        }

        public bool IsItem => UnifiedIndex < ItemCatalog.itemCount;

        public ItemIndex ItemIndex => IsItem
            ? (ItemIndex)UnifiedIndex
            : ItemIndex.None;

        public EquipmentIndex EquipIndex => IsItem
            ? EquipmentIndex.None
            : (EquipmentIndex)(UnifiedIndex - ItemCatalog.itemCount);

        public override int GetHashCode()
        {
            // Simple multiplicative hash
            int hash = 17;
            hash = hash * 31 + UnifiedIndex;
            hash = hash * 31 + Count;
            return hash;
        }

        public override bool Equals(object obj)
        {
            return obj is Ingredient other && Equals(other);
        }

        public bool Equals(Ingredient other)
        {
            return UnifiedIndex == other.UnifiedIndex &&
                   Count == other.Count;
        }
    }

    /// <summary>result entry</summary>
    internal sealed class ChefRecipe
    {
        public int ResultIndex { get; }
        public int ResultCount { get; }
        public Ingredient[] Ingredients { get; }

        public ChefRecipe(int resultIndex, int resultCount, Ingredient[] ingredients)
        {
            ResultIndex = resultIndex;
            ResultCount = resultCount;
            Ingredients = ingredients;
        }

        private int GetIngredientsCanonicalHash()
        {
            if (Ingredients == null || Ingredients.Length == 0)
            {
                return 0;
            }

            var ingredientHashes = new List<int>(Ingredients.Length);
            foreach (var ingredient in Ingredients)
            {
                ingredientHashes.Add(ingredient.GetHashCode());
            }

            ingredientHashes.Sort();

            int hash = 17;
            foreach (int ingHash in ingredientHashes)
            {
                hash = hash * 31 + ingHash;
            }
            return hash;
        }

        public override int GetHashCode()
        {
            int hash = 17;

            hash = hash * 31 + ResultIndex;
            hash = hash * 31 + ResultCount;

            hash = hash * 31 + GetIngredientsCanonicalHash();

            return hash;
        }

        public override bool Equals(object obj)
        {
            return obj is ChefRecipe other && Equals(other);
        }

        public bool Equals(ChefRecipe other)
        {
            if (other == null) return false;

            if (ResultIndex != other.ResultIndex ||
                ResultCount != other.ResultCount)
            {
                return false;
            }

            if (GetIngredientsCanonicalHash() != other.GetIngredientsCanonicalHash())
            {
                return false;
            }

            return true;
        }

    }
}