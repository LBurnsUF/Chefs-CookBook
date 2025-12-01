using System.Collections.Generic;
using BepInEx.Logging;
using RoR2;

namespace CookBook
{
    /// <summary>
    /// Holds all chef crafting recipes.
    /// </summary>
    internal static class RecipeProvider
    {
        private static ManualLogSource _log;
        private static bool _initialized;

        // Internal storage for recipes
        private static readonly List<ChefRecipe> _recipes = new List<ChefRecipe>();

        /// <summary>Public, read-only view of recipes.</summary>
        internal static IReadOnlyList<ChefRecipe> Recipes => _recipes;

        /// <summary>
        /// Called once from CookBook.Awake().
        /// </summary>
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _log.LogInfo("RecipeProvider.Init()");

            ItemCatalog.availability.CallWhenAvailable(BuildRecipes);
        }
        /// <summary>
        /// Build the list of chef recipes.
        /// replace this with real parsing of the DLC chef rules.
        /// </summary>
        /// 
        private static void BuildRecipes()
        {
            _recipes.Clear(); // initialize
            _log.LogInfo("RecipeProvider: ItemCatalog available, building recipes.");

            ItemIndex knurl = ItemCatalog.FindItemIndex("Knurl");
            ItemIndex infusion = ItemCatalog.FindItemIndex("Infusion");
            ItemIndex seed = ItemCatalog.FindItemIndex("Seed");
            if (knurl == ItemIndex.None || infusion == ItemIndex.None || seed == ItemIndex.None)
            {
                _log.LogError("Failed to resolve one of the test items (Knurl / Infusion / Seed).");
                return;
            }

            //hardcoded test case
            _recipes.Add(new ChefRecipe(
               result: knurl,
               resultCount: 1,
               ingredients: new[]
       {
            new Ingredient(infusion, 1),
            new Ingredient(seed, 1),
               }));

            _log.LogInfo($"RecipeProvider: built {_recipes.Count} recipes.");
        }
    }


    /// <summary>Inventory management</summary>
    internal readonly struct Ingredient
    {
        public readonly ItemIndex Item;
        public readonly int Count;

        public Ingredient(ItemIndex item, int count)
        {
            Item = item;
            Count = count;
        }
    }

    /// <summary>Ingredients -> Result</summary>
    internal sealed class ChefRecipe
    {
        public ItemIndex Result { get; }
        public int ResultCount { get; }
        public Ingredient[] Ingredients { get; }

        public ChefRecipe(ItemIndex result, int resultCount, Ingredient[] ingredients)
        {
            Result = result;
            ResultCount = resultCount;
            Ingredients = ingredients;
        }
    }
}

