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
        public const string PluginVersion = "1.2.6";

        internal static ManualLogSource Log;
        private const int DefaultMaxDepth = 3;

        public static ConfigEntry<int> MaxDepth;
        public static ConfigEntry<string> TierOrder;
        public static ConfigEntry<KeyboardShortcut> AbortKey;
        public static ConfigEntry<float> ComputeThrottle;
        public static ConfigEntry<bool> AllowMultiplayerPooling;

        internal static Dictionary<ItemTier, ConfigEntry<TierPriority>> TierPriorities = new();

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
            AbortKey = Config.Bind(
                "General",
                "AbortKey",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Key to hold to abort an active auto-crafting sequence."
            );
            ComputeThrottle = Config.Bind(
                "Performance",
                "ComputeThrottle",
                0.15f,
                "Delay (seconds) after inventory changes before recomputing recipes."
            );
            AllowMultiplayerPooling = Config.Bind(
                "General",
                "Allow Multiplayer Pooling",
                false,
                "If true, the planner will include items owned by teammates in its search (requires SPEX trades)."
            );

            TierManager.Init(Log);

            // Inside CookBook.Awake()
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

                    // 1. Bind normally
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

                // Initialize UI only after all tiers are cataloged and bound
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.rune580.riskofoptions"))
                {
                    SettingsUI.Init(this);
                }
            });

            MaxDepth.SettingChanged += StateController.OnMaxDepthChanged;
            TierManager.OnTierOrderChanged += StateController.OnTierOrderChanged;
            RecipeProvider.OnRecipesBuilt += StateController.OnRecipesBuilt;
            RecipeProvider.Init(Log); // Parse all chef recipe rules
            StateController.Init(Log); // Initialize chef/state logic
            DialogueHooks.Init(Log); // Initialize all Chef Dialogue Hooks
            InventoryTracker.Init(Log); // Begin waiting for Enable signal
            CraftUI.Init(Log); // Initialize craft UI injection
            ChatNetworkHandler.Init(Log);

            DialogueHooks.ChefUiOpened += StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed += StateController.OnChefUiClosed;
        }

        private void OnDestroy()
        {
            // unsubscribe from settings changes
            foreach (var tierEntry in TierPriorities.Values)
            {
                if (tierEntry != null)
                {
                    tierEntry.SettingChanged -= TierManager.OnTierPriorityChanged;
                }
            }
            MaxDepth.SettingChanged -= StateController.OnMaxDepthChanged;

            // Clean up global event subscriptions
            RecipeProvider.OnRecipesBuilt -= StateController.OnRecipesBuilt;
            TierManager.OnTierOrderChanged -= StateController.OnTierOrderChanged;

            DialogueHooks.ChefUiOpened -= StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed -= StateController.OnChefUiClosed;

            RecipeProvider.Shutdown();
            StateController.Shutdown();
            DialogueHooks.Shutdown();
            CraftUI.Shutdown();
        }
    }
}