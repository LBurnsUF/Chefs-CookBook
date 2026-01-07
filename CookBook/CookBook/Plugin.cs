using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using static CookBook.TierManager;

namespace CookBook
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class CookBook : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "rainorshine";
        public const string PluginName = "CookBook";
        public const string PluginVersion = "1.2.10";

        internal static ManualLogSource Log;

        public static ConfigEntry<int> MaxDepth;
        public static ConfigEntry<int> MaxChainsPerResult;
        public static ConfigEntry<int> ComputeThrottleMs;
        public static ConfigEntry<string> TierOrder;
        public static ConfigEntry<KeyboardShortcut> AbortKey;
        public static ConfigEntry<bool> AllowMultiplayerPooling;
        public static ConfigEntry<bool> ConsiderDrones;
        public static ConfigEntry<bool> PreventCorruptedCrafting;
        public static ConfigEntry<bool> DebugMode;
        public static ConfigEntry<bool> ShowCorruptedResults;
        internal static ConfigEntry<IndexSortMode> InternalSortOrder;
        internal static Dictionary<ItemTier, ConfigEntry<TierPriority>> TierPriorities = new();

        public static int DepthLimit => MaxDepth.Value;
        public static int ChainsLimit => MaxChainsPerResult.Value;
        public static int ThrottleMs => ComputeThrottleMs.Value;
        public static bool IsPoolingEnabled => AllowMultiplayerPooling.Value;
        public static bool IsDroneScrappingEnabled => ConsiderDrones.Value;
        public static bool ShouldBlockCorrupted => PreventCorruptedCrafting.Value;
        public static bool isDebugMode => DebugMode.Value;


        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            AllowMultiplayerPooling = Config.Bind(
                "Logic",
                "Allow Multiplayer Pooling",
                true,
                "If true, the planner will include items owned by teammates in its search (requires SPEX trades)."
            );
            ConsiderDrones = Config.Bind(
                "Logic",
                "Consider Drones for Crafting",
                true,
                "If enabled, the planner will include scrappable drones (potential scrap) in recipe calculations."
            );
            AbortKey = Config.Bind(
                "General",
                "AbortKey",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Key to hold to abort an active auto-crafting sequence."
            );
            MaxDepth = Config.Bind(
                "Logic",
                "Max Chain Depth",
                3,
                "Maximum crafting chain depth to explore when precomputing recipe plans. Higher values allow more indirect chains but increase compute time"
            );
            PreventCorruptedCrafting = Config.Bind(
                "Logic",
                "Prevent Corrupted Crafting",
                true,
                "If enabled, recipes for base items will be hidden/disabled if you hold their Void counterpart (e.g., hiding Ukulele recipes if you have Polylute)."
            );
            ShowCorruptedResults = Config.Bind(
                "UI",
                "Show Corrupted Results",
                true,
                "Display corrupted versions of craft results if corrupt version already owned."
            );

            ComputeThrottleMs = Config.Bind(
                "Performance",
                "Computation Throttle",
                500,
                "Delay (ms) after inventory changes before recomputing recipes."
            );
            MaxChainsPerResult = Config.Bind(
                "Performance",
                "Max Paths Per Result",
                40,
                "Maximum number of unique recipe paths to store for each result. Higher values allow more variety but increase compute time and memory usage."
            );
            InternalSortOrder = Config.Bind(
                "Tier Sorting",
                "Indexing Sort Mode",
                IndexSortMode.Descending,
                "How to sort items within the same tier: Ascending (0->99) or Descending (99->0)."
            );
            TierOrder = Config.Bind(
                "Tier Sorting",
                "Tier Priority Order",
                "FoodTier,NoTier,Equipment,Boss,Tier3,Tier2,Tier1,VoidTier3,VoidTier2,VoidTier1,Lunar",
                "The CSV order of item tiers for sorting. Tiers earlier in the list appear higher in the UI."
            );
            DebugMode = Config.Bind<bool>(
                "Logging",
                "Enable Debug Mode",
                false,
                "When enabled, the console will show detailed recipe chain calculations and execution dumps."
            );

            TierManager.Init(Log);
            RecipeProvider.Init(Log); // Parse all chef recipe rules
            StateController.Init(Log); // Initialize chef/state logic
            DialogueHooks.Init(Log); // Initialize all Chef Dialogue Hooks
            InventoryTracker.Init(Log); // Begin waiting for Enable signal
            CraftUI.Init(Log); // Initialize craft UI injection
            ChatNetworkHandler.Init(Log);
            RegisterAssets.Init();
            // VanillaCraftingTrace.Init(Log);
            //RecipeTrackerUI.Init(Log);

            ItemCatalog.availability.CallWhenAvailable(() =>
            {
                var defaultTiers = TierManager.ParseTierOrder(TierOrder.Value);
                var discoveredTiers = TierManager.DiscoverTiersFromCatalog();
                var merged = TierManager.MergeOrder(defaultTiers, discoveredTiers);

                string mergedCsv = TierManager.ToCsv(merged);
                if (TierOrder.Value != mergedCsv)
                {
                    Log.LogInfo($"CookBook: Syncing TierOrder config: {mergedCsv}");
                    TierOrder.Value = mergedCsv;
                }
                TierManager.SetOrder(merged);

                foreach (var tier in TierManager.GetAllKnownTiers())
                {
                    string friendlyName = TierManager.GetFriendlyName(tier);

                    var configEntry = Config.Bind<TierManager.TierPriority>(
                        "Tier Sorting",
                        $"Priority_{tier}",
                        TierManager.GetDefaultPriorityForTier(tier),
                        $"Priority for {friendlyName} items."
                    );

                    if (!Enum.IsDefined(typeof(TierManager.TierPriority), configEntry.Value))
                    {
                        configEntry.Value = TierManager.GetDefaultPriorityForTier(tier);
                    }

                    if (!TierPriorities.ContainsKey(tier))
                    {
                        TierPriorities[tier] = configEntry;
                        configEntry.SettingChanged += TierManager.OnTierPriorityChanged;
                    }
                }

                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions"))
                {
                    SettingsUI.Init(this);
                }
            });

            ConsiderDrones.SettingChanged += InventoryTracker.OnConsiderDronesChanged;
            ShowCorruptedResults.SettingChanged += StateController.OnShowCorruptedResultsChanged;
            MaxDepth.SettingChanged += StateController.OnMaxDepthChanged;
            MaxChainsPerResult.SettingChanged += StateController.OnMaxChainsPerResultChanged;
            InternalSortOrder.SettingChanged += TierManager.OnTierPriorityChanged;
            TierManager.OnTierOrderChanged += StateController.OnTierOrderChanged;
            RecipeProvider.OnRecipesBuilt += StateController.OnRecipesBuilt;
            DialogueHooks.ChefUiOpened += StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed += StateController.OnChefUiClosed;
            On.RoR2.CraftingController.FilterAvailableOptions += RecipeFilter.PatchVanillaNRE;
        }

        private void OnDestroy()
        {
            // unsubscribe from settings changes
            foreach (var tierEntry in TierPriorities.Values)
            {
                if (tierEntry != null) tierEntry.SettingChanged -= TierManager.OnTierPriorityChanged;
            }

            ShowCorruptedResults.SettingChanged -= StateController.OnShowCorruptedResultsChanged;
            MaxDepth.SettingChanged -= StateController.OnMaxDepthChanged;
            ConsiderDrones.SettingChanged -= InventoryTracker.OnConsiderDronesChanged;
            MaxChainsPerResult.SettingChanged -= StateController.OnMaxChainsPerResultChanged;
            // Clean up global event subscriptions
            RecipeProvider.OnRecipesBuilt -= StateController.OnRecipesBuilt;
            TierManager.OnTierOrderChanged -= StateController.OnTierOrderChanged;

            DialogueHooks.ChefUiOpened -= StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed -= StateController.OnChefUiClosed;

            On.RoR2.CraftingController.FilterAvailableOptions -= RecipeFilter.PatchVanillaNRE;

            RecipeProvider.Shutdown();
            StateController.Shutdown();
            DialogueHooks.Shutdown();
            CraftUI.Shutdown();
        }
    }
}