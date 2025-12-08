using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.ContentManagement;
using static RoR2.Console;

namespace CookBook
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class CookBook : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "rainorshine";
        public const string PluginName = "CookBook";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private const int DefaultMaxDepth = 3;

        public static ConfigEntry<int> MaxDepth;
        public static ConfigEntry<string> TierOrder;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            MaxDepth = Config.Bind(
                "General",
                "MaxDepth",
                DefaultMaxDepth,
                "Maximum crafting chain depth to explore when precomputing recipe plans."
            );
            TierOrder = Config.Bind(
                "General",
                "TierOrder",
                "Tier3,Tier2,Tier1,Boss,Lunar,VoidTier3,VoidTier2,VoidTier1,AssignedAtRuntime,NoTier",
                "Comma-separated tier order for sorting craftable items."
            );

            TierManager.Init(Log);

            // discover any custom item tiers
            ItemCatalog.availability.CallWhenAvailable(() => {
                var defaultTiers = TierManager.ParseTierOrder(TierOrder.Value);
                var merged = TierManager.MergeOrder(defaultTiers, TierManager.DiscoverTiersFromCatalog());

                // push discovered tiers back into the config
                string mergedCsv = TierManager.ToCsv(merged);
                if (TierOrder.Value != mergedCsv)
                {
                    Log.LogInfo($"CookBook: updating TierOrder config to include newly discovered tiers: {mergedCsv}");
                    TierOrder.Value = mergedCsv;
                }
                else
                {
                    // No new tiers; just apply the current order
                    TierManager.SetOrder(merged);
                }
            });

            TierOrder.SettingChanged += TierManager.OnTierOrderConfigChanged; // subscribe to sorting config change events
            MaxDepth.SettingChanged += StateController.OnMaxDepthChanged;

            // subscribe to recipe completion event
            RecipeProvider.OnRecipesBuilt += StateController.OnRecipesBuilt;

            // subscribe to sort order update events
            TierManager.OnTierOrderChanged += StateController.OnTierOrderChanged;

            // Init subsystems
            // TODO: Initialize settings UI via SettingsUI.cs
            RecipeProvider.Init(Log); // Parse all chef recipe rules
            StateController.Init(Log); // Initialize chef/state logic
            DialogueHooks.Init(Log); // Initialize all Chef Dialogue Hooks
            InventoryTracker.Init(Log); // Begin waiting for Enable signal
            CraftUI.Init(Log); // Initialize craft UI injection

            // Subscribe to Chef dialogue events
            DialogueHooks.ChefUiOpened += StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed += StateController.OnChefUiClosed;
        }

        private void OnDestroy()
        {
            // Clean up global event subscriptions
            RecipeProvider.OnRecipesBuilt -= StateController.OnRecipesBuilt;
            TierManager.OnTierOrderChanged -= StateController.OnTierOrderChanged;
            
            // unsubscribe from settings changes
            TierOrder.SettingChanged -= TierManager.OnTierOrderConfigChanged;
            MaxDepth.SettingChanged -= StateController.OnMaxDepthChanged;

            // unsubscribe from chef ui state events
            DialogueHooks.ChefUiOpened -= StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed -= StateController.OnChefUiClosed;

            // ask subsystems to clean up their own game hooks
            RecipeProvider.Shutdown();
            StateController.Shutdown();
            DialogueHooks.Shutdown();
            CraftUI.Shutdown();
        }
    }
}
