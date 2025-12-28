using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using System.Collections.Generic;
using System.Linq;

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

            ContentManager.onContentPacksAssigned += OnContentPacksAssigned;
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
                if (recipeEntry == null) continue;

                // 1. Resolve Result
                PickupIndex resultPickup = recipeEntry.result;
                if (!resultPickup.isValid || resultPickup == PickupIndex.none) continue;

                PickupDef resultDef = PickupCatalog.GetPickupDef(resultPickup);
                if (resultDef == null) continue;

                int resultIndex = resultDef.itemIndex != ItemIndex.None
                    ? (int)resultDef.itemIndex
                    : (resultDef.equipmentIndex != EquipmentIndex.None ? itemOffset + (int)resultDef.equipmentIndex : -1);

                if (resultIndex == -1) continue;

                // 2. Resolve Raw Ingredient Indices
                List<PickupIndex> ingredientPickups = recipeEntry.GetAllPickups();
                if (ingredientPickups == null || ingredientPickups.Count == 0) continue;

                var rawIndices = new List<int>();
                foreach (var ingPi in ingredientPickups)
                {
                    if (!ingPi.isValid || ingPi == PickupIndex.none) continue;
                    PickupDef ingDef = PickupCatalog.GetPickupDef(ingPi);
                    if (ingDef == null) continue;

                    int idx = ingDef.itemIndex != ItemIndex.None
                        ? (int)ingDef.itemIndex
                        : (ingDef.equipmentIndex != EquipmentIndex.None ? itemOffset + (int)ingDef.equipmentIndex : -1);

                    if (idx != -1) rawIndices.Add(idx);
                }

                if (rawIndices.Count == 0) continue;

                bool isPureBulk = rawIndices.Distinct().Count() == 1;

                if (isPureBulk && rawIndices.Count > 1)
                {
                    var consolidated = new Ingredient(rawIndices[0], rawIndices.Count);
                    var recipe = new ChefRecipe(resultIndex, recipeEntry.amountToDrop, new[] { consolidated });
                    _recipes.Add(recipe);
                }
                else if (rawIndices.Count <= 2)
                {
                    var ings = rawIndices.Select(idx => new Ingredient(idx, 1)).ToArray();
                    var recipe = new ChefRecipe(resultIndex, recipeEntry.amountToDrop, ings);
                    _recipes.Add(recipe);
                }
                else
                {
                    var baseIdx = rawIndices[0];
                    for (int i = 1; i < rawIndices.Count; i++)
                    {
                        var pair = new Ingredient[] { new Ingredient(baseIdx, 1), new Ingredient(rawIndices[i], 1) };
                        var recipe = new ChefRecipe(resultIndex, recipeEntry.amountToDrop, pair);
                        _recipes.Add(recipe);
                    }
                }
            }

            _recipesBuilt = true;
            _log.LogInfo($"RecipeProvider: Built {_recipes.Count} explicit recipes.");
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
    internal class ChefRecipe
    {
        public int ResultIndex { get; }
        public int ResultCount { get; }
        public Ingredient[] Ingredients { get; }
        private readonly int _cachedHash;

        public ChefRecipe(int resultIndex, int resultCount, Ingredient[] ingredients)
        {
            ResultIndex = resultIndex;
            ResultCount = resultCount;
            Ingredients = ingredients;
            _cachedHash = CalculateInitialHash();
        }

        private int CalculateInitialHash()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + ResultIndex;
                hash = hash * 31 + ResultCount;

                if (Ingredients != null)
                {
                    var sortedHashes = Ingredients.Select(i => i.GetHashCode()).ToList();
                    sortedHashes.Sort();

                    foreach (var h in sortedHashes)
                    {
                        hash = hash * 31 + h;
                    }
                }
                return hash;
            }
        }

        public override int GetHashCode() => _cachedHash;
        public override bool Equals(object obj) => obj is ChefRecipe other && Equals(other);

        public bool Equals(ChefRecipe other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            if (_cachedHash != other._cachedHash) return false;

            if (ResultIndex != other.ResultIndex || ResultCount != other.ResultCount) return false;
            if (Ingredients.Length != other.Ingredients.Length) return false;

            var thisIng = Ingredients.OrderBy(i => i.UnifiedIndex).ToArray();
            var otherIng = other.Ingredients.OrderBy(i => i.UnifiedIndex).ToArray();

            for (int i = 0; i < thisIng.Length; i++)
            {
                if (!thisIng[i].Equals(otherIng[i])) return false;
            }

            return true;
        }

    }
}