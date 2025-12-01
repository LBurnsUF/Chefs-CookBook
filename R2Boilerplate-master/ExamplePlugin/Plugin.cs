using BepInEx;
using BepInEx.Logging;
using R2API;
using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;

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

        internal static ManualLogSource Log; // global logger
        private CraftPlanner _planner; // crafting planner

        // Run at game initialization.
        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            RecipeProvider.Init(Log); // Parse all chef recipe rules
            _planner = new CraftPlanner(RecipeProvider.Recipes, maxDepth: 3);
            ChefStateController.Init(Log, _planner); // Initialize chef/state logic
        }
    }
}
