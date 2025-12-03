using BepInEx;
using BepInEx.Logging;
using R2API;

namespace CookBook
{
    [BepInDependency(LanguageAPI.PluginGUID, BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(ItemAPI.PluginGUID)]

    // This attribute is required, and lists metadata for your plugin.
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class CookBook : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "rainorshine";
        public const string PluginName = "CookBook";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private CraftPlanner _planner; // crafting planner
        private const int DefaultMaxDepth = 3;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            RecipeProvider.OnRecipesBuilt += OnRecipesBuilt; // subscribe to recipe completion event
            RecipeProvider.Init(Log); // Parse all chef recipe rules

            TierManager.Init(Log); 
            TierManager.OnTierOrderChanged += OnTierOrderChanged; // subscribe to sort order update events

            // TODO: Initialize settings UI via SettingsUI.cs

            ChefStateController.Init(Log); // Initialize chef/state logic
        }

        private void OnTierOrderChanged()
        {
            if (_planner != null)
            {
                Logger.LogInfo("Tier order changed — rebuilding all CraftPlanner plans.");
                _planner.RebuildAllPlans();

                // If we're already in Chef stage, refresh craftables
                if (ChefStateController.IsChefStage())
                {
                    
                    // safe to run Chef-specific plugin logic
                }
            }
        }


        /// <summary>
        /// Called when RecipeProvider has finished building ChefRecipe list. also fired when recipe list rebuilt
        /// </summary>
        private void OnRecipesBuilt(System.Collections.Generic.IReadOnlyList<ChefRecipe> recipes)
        {
            Log.LogInfo($"CookBook: OnRecipesBuilt fired with {recipes.Count} recipes; (re)constructing CraftPlanner.");

            // TODO: Pull maxdepth from settings.
            _planner = new CraftPlanner(recipes, DefaultMaxDepth, Log);
            ChefStateController.SetPlanner(_planner); // Hand a fresh planner to the state controller
        }

        private void OnDestroy()
        {
            // Clean up event subscription
            RecipeProvider.OnRecipesBuilt -= OnRecipesBuilt;
        }
    }
}
