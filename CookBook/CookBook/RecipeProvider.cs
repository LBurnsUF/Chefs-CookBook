using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using System;
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
        internal static event System.Action OnRecipesBuilt;

        internal static int TotalDefCount { get; private set; }
        internal static int MaskWords { get; private set; }
        internal static ulong[][] ReqMasks { get; private set; }
        internal static int[][] ConsumersByIngredient { get; private set; }
        internal static int[][] ProducersByResult { get; private set; }
        internal static int[] ResultIdxByRecipe { get; private set; }
        internal static int[] IngAByRecipe { get; private set; }
        internal static int[] IngBByRecipe { get; private set; }
        internal static bool[] IsDoubleIngredientRecipe { get; private set; }
        internal static IReadOnlyDictionary<ChefRecipe, int> MasterIndexByRecipe => _masterIndexByRecipe;
        private static Dictionary<ChefRecipe, int> _masterIndexByRecipe = new();


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

            var uniqueRecipes = new HashSet<ChefRecipe>();
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

                PickupIndex resultPickup = recipeEntry.result;
                if (!resultPickup.isValid || resultPickup == PickupIndex.none) continue;

                PickupDef resultDef = PickupCatalog.GetPickupDef(resultPickup);
                if (resultDef == null) continue;

                List<PickupIndex> ingredientPickups = recipeEntry.GetAllPickups();
                if (ingredientPickups == null || ingredientPickups.Count == 0) continue;

                var ingredientNames = new List<string>();
                foreach (var ingPi in ingredientPickups)
                {
                    var def = (ingPi.isValid && ingPi != PickupIndex.none) ? PickupCatalog.GetPickupDef(ingPi) : null;
                    ingredientNames.Add(def?.internalName ?? "InvalidPickup");
                }
                string fullRecipeArrowStr = $"{string.Join(" + ", ingredientNames)} -> {resultDef.internalName}";

                bool isResultValid = true;
                if (resultDef.itemIndex != ItemIndex.None)
                {
                    ItemDef resItemDef = ItemCatalog.GetItemDef(resultDef.itemIndex);
                    if (resItemDef == null || resItemDef.hidden) isResultValid = false;
                }
                else if (resultDef.equipmentIndex != EquipmentIndex.None)
                {
                    if (EquipmentCatalog.GetEquipmentDef(resultDef.equipmentIndex) == null) isResultValid = false;
                }

                if (!isResultValid)
                {
                    DebugLog.Trace(_log, $"RecipeProvider: Skipping recipe {fullRecipeArrowStr} because the result is hidden or null.");
                    continue;
                }

                int resultIndex = resultDef.itemIndex != ItemIndex.None
                    ? (int)resultDef.itemIndex
                    : (resultDef.equipmentIndex != EquipmentIndex.None ? itemOffset + (int)resultDef.equipmentIndex : -1);

                if (resultIndex == -1) continue;

                var rawIndices = new List<int>();
                bool allIngredientsValid = true;

                foreach (var ingPi in ingredientPickups)
                {
                    if (!ingPi.isValid || ingPi == PickupIndex.none)
                    {
                        allIngredientsValid = false;
                        break;
                    }

                    PickupDef ingDef = PickupCatalog.GetPickupDef(ingPi);
                    if (ingDef == null)
                    {
                        allIngredientsValid = false;
                        break;
                    }

                    int idx = -1;
                    if (ingDef.itemIndex != ItemIndex.None)
                    {
                        ItemDef itemDef = ItemCatalog.GetItemDef(ingDef.itemIndex);

                        if (itemDef == null || itemDef.hidden)
                        {
                            DebugLog.Trace(_log, $"RecipeProvider: Skipping recipe {fullRecipeArrowStr} because ingredient {itemDef?.name} is hidden or has no tags.");
                            allIngredientsValid = false;
                            break;
                        }
                        idx = (int)ingDef.itemIndex;
                    }
                    else if (ingDef.equipmentIndex != EquipmentIndex.None)
                    {
                        EquipmentDef equipDef = EquipmentCatalog.GetEquipmentDef(ingDef.equipmentIndex);
                        if (equipDef == null)
                        {
                            DebugLog.Trace(_log, $"RecipeProvider: Skipping recipe {fullRecipeArrowStr} because equipment {equipDef?.name} is null.");
                            allIngredientsValid = false;
                            break;
                        }
                        idx = itemOffset + (int)ingDef.equipmentIndex;
                    }

                    if (idx != -1) rawIndices.Add(idx);
                    else { allIngredientsValid = false; break; }
                }

                if (!allIngredientsValid || rawIndices.Count == 0) continue;

                if (rawIndices.Count > 1 && rawIndices.All(x => x == rawIndices[0]))
                {
                    int a = rawIndices[0];
                    uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, a, (byte)rawIndices.Count, 0));
                }
                else if (rawIndices.Count <= 2)
                {
                    int a = rawIndices[0];
                    int b = (rawIndices.Count == 2) ? rawIndices[1] : -1;

                    if (b == -1)
                    {
                        uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, a, 2, 0));
                    }
                    else
                    {
                        CanonicalizePair(ref a, ref b);

                        if (a == b)
                        {
                            uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, a, 2, 0));
                        }
                        else
                        {
                            uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, b, 1, 1));
                        }
                    }
                }
                else
                {
                    int baseIdx = rawIndices[0];
                    for (int i = 1; i < rawIndices.Count; i++)
                    {
                        int a = baseIdx;
                        int b = rawIndices[i];

                        CanonicalizePair(ref a, ref b);

                        if (a == b)
                            uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, a, 2, 0));
                        else
                            uniqueRecipes.Add(new ChefRecipe(resultIndex, recipeEntry.amountToDrop, a, b, 1, 1));
                    }
                }
            }

            _recipes.AddRange(uniqueRecipes);
            _masterIndexByRecipe.Clear();
            for (int i = 0; i < _recipes.Count; i++)
                _masterIndexByRecipe[_recipes[i]] = i;
            BuildDerivedIndices();
            _recipesBuilt = true;
            if (CookBook.isDebugMode)
            {
                DebugLog.Trace(_log, $"RecipeProvider: Built {_recipes.Count} explicit recipes.");
            }
            OnRecipesBuilt?.Invoke();
        }

        private static void BuildDerivedIndices()
        {
            TotalDefCount = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
            MaskWords = (TotalDefCount + 63) / 64;

            var tmpConsumers = new List<int>[TotalDefCount];
            var tmpProducers = new List<int>[TotalDefCount];

            int n = _recipes.Count;
            ResultIdxByRecipe = new int[n];
            ReqMasks = new ulong[n][];
            IngAByRecipe = new int[n];
            IngBByRecipe = new int[n];
            IsDoubleIngredientRecipe = new bool[n];

            for (int i = 0; i < TotalDefCount; i++)
            {
                tmpConsumers[i] = new List<int>(4);
                tmpProducers[i] = new List<int>(2);
            }

            for (int r = 0; r < n; r++)
            {
                var recipe = _recipes[r];

                int resultIdx = recipe.ResultIndex;
                ResultIdxByRecipe[r] = resultIdx;

                if ((uint)resultIdx < (uint)TotalDefCount)
                    tmpProducers[resultIdx].Add(r);

                var mask = new ulong[MaskWords];

                int a = recipe.IngA;
                int b = recipe.HasB ? recipe.IngB : -1;

                IngAByRecipe[r] = a;
                IngBByRecipe[r] = b;
                IsDoubleIngredientRecipe[r] = recipe.IsDouble;

                if ((uint)a < (uint)TotalDefCount)
                {
                    int word = a >> 6;
                    int bit = a & 63;
                    mask[word] |= (1UL << bit);
                    tmpConsumers[a].Add(r);
                }

                if (b != -1 && b != a && (uint)b < (uint)TotalDefCount)
                {
                    int word = b >> 6;
                    int bit = b & 63;
                    mask[word] |= (1UL << bit);
                    tmpConsumers[b].Add(r);
                }

                ReqMasks[r] = mask;
            }

            ConsumersByIngredient = new int[TotalDefCount][];
            ProducersByResult = new int[TotalDefCount][];

            for (int i = 0; i < TotalDefCount; i++)
            {
                ConsumersByIngredient[i] = tmpConsumers[i].ToArray();
                ProducersByResult[i] = tmpProducers[i].ToArray();
            }
        }

        /// <summary>
        /// Generates a transient, filtered list of recipes based on current void corruptions.
        /// Does not modify the underlying master list.
        /// </summary>
        internal static List<ChefRecipe> GetFilteredRecipes(HashSet<ItemIndex> corruptedIndices)
        {
            if (!CookBook.PreventCorruptedCrafting.Value || corruptedIndices == null || corruptedIndices.Count == 0)
            {
                // If you want to avoid allocating here, you can return _recipes if callers treat it as read-only.
                return new List<ChefRecipe>(_recipes);
            }

            var transientList = new List<ChefRecipe>(_recipes.Count);

            int itemCount = ItemCatalog.itemCount;

            foreach (var recipe in _recipes)
            {
                bool hasCorrupted = false;

                // IngA
                int a = recipe.IngA;
                if ((uint)a < (uint)itemCount && corruptedIndices.Contains((ItemIndex)a))
                    hasCorrupted = true;

                // IngB (only if present)
                if (!hasCorrupted && recipe.HasB)
                {
                    int b = recipe.IngB;
                    if ((uint)b < (uint)itemCount && corruptedIndices.Contains((ItemIndex)b))
                        hasCorrupted = true;
                }

                if (!hasCorrupted)
                    transientList.Add(recipe);
            }

            return transientList;
        }

        private static void CanonicalizePair(ref int a, ref int b)
        {
            if (b < 0) return;
            if (b < a) { int t = a; a = b; b = t; }
        }
    }

    internal readonly struct Ingredient : IEquatable<Ingredient>
    {
        public readonly int UnifiedIndex;
        public readonly int Count;

        public Ingredient(int unifiedIndex, int count)
        {
            UnifiedIndex = unifiedIndex;
            Count = count;
        }

        public bool Equals(Ingredient other)
            => UnifiedIndex == other.UnifiedIndex && Count == other.Count;

        public override bool Equals(object obj)
            => obj is Ingredient other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ UnifiedIndex;
                h = (h * 31) ^ Count;
                return h;
            }
        }
    }

    /// <summary>result entry</summary>
    internal class ChefRecipe
    {
        public int ResultIndex { get; }
        public int ResultCount { get; }

        public readonly int IngA;
        public readonly int IngB;

        public readonly byte CountA;
        public readonly byte CountB;
        public bool HasB => CountB != 0;
        public bool IsDouble => (IngA == IngB) && CountA >= 2;

        public ChefRecipe(int resultIndex, int resultCount, int ingA, int ingB, byte countA, byte countB)
        {
            ResultIndex = resultIndex;
            ResultCount = resultCount;
            IngA = ingA;
            IngB = ingB;
            CountA = countA;
            CountB = countB;
        }

        public bool Equals(ChefRecipe other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return ResultIndex == other.ResultIndex
                && ResultCount == other.ResultCount
                && IngA == other.IngA && IngB == other.IngB
                && CountA == other.CountA && CountB == other.CountB;
        }

        public override bool Equals(object obj) => obj is ChefRecipe other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ ResultIndex;
                h = (h * 31) ^ ResultCount;

                h = (h * 31) ^ IngA;
                h = (h * 31) ^ IngB;

                h = (h * 31) ^ CountA;
                h = (h * 31) ^ CountB;
                return h;
            }
        }
    }
}