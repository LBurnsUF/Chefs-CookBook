using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;
using static CookBook.TierManager;
using static RoR2.Console;

namespace CookBook
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("rainorshine.CleanChef", BepInDependency.DependencyFlags.SoftDependency)]
    public class CookBook : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "rainorshine";
        public const string PluginName = "CookBook";
        public const string PluginVersion = "1.3.1";

        internal static ManualLogSource Log;

        public static ConfigEntry<int> MaxDepth;
        public static ConfigEntry<int> MaxChainsPerResult;
        public static ConfigEntry<int> MaxBridgeItemsPerChain;
        public static ConfigEntry<int> ComputeThrottleMs;
        public static ConfigEntry<string> TierOrder;
        public static ConfigEntry<KeyboardShortcut> AbortKey;
        public static ConfigEntry<bool> AllowMultiplayerPooling;
        public static ConfigEntry<bool> ConsiderDrones;
        public static ConfigEntry<bool> PreventCorruptedCrafting;
        public static ConfigEntry<bool> ShowCorruptedResults;
        internal static ConfigEntry<IndexSortMode> InternalSortOrder;
        internal static Dictionary<ItemTier, ConfigEntry<TierPriority>> TierPriorities = new();

        public static ConfigEntry<bool> DebugMode;
        public static ConfigEntry<bool> LogCraftMode;

        public static int DepthLimit => MaxDepth.Value;
        public static int ChainsLimit => MaxChainsPerResult.Value;
        public static int ThrottleMs => ComputeThrottleMs.Value;
        public static bool IsPoolingEnabled => AllowMultiplayerPooling.Value;
        public static bool IsDroneScrappingEnabled => ConsiderDrones.Value;
        public static bool ShouldBlockCorrupted => PreventCorruptedCrafting.Value;
        public static bool isDebugMode => DebugMode.Value;
        public static bool isLogCraftMode => DebugMode.Value;

        public void Awake()
        {
            Log = Logger;
            Log.LogInfo("CookBook: Awake()");

            AbortKey = Config.Bind(
                "General",
                "AbortKey",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "Key to hold to abort an active auto-crafting sequence."
            );
            ShowCorruptedResults = Config.Bind(
                "General",
                "Show Corrupted Results",
                true,
                "Display corrupted versions of craft results if corrupt version already owned."
            );

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
            MaxBridgeItemsPerChain = Config.Bind(
                "Performance",
                "Max Bridged Dependencies Per Result",
                5,
                "Maximum number of bridges between recipes within a single result, allows crafts to request intermediate items if it satisfies a later demand. Higher values yields a more complete search but increases compute time and memory usage."
            );

            DebugMode = Config.Bind<bool>(
                "Logging",
                "Enable Debug Mode",
                true,
                "When enabled, the console will show detailed logging of the backend, excluding culling logic (for spam reasons). Useful for debugging."
            );
            LogCraftMode = Config.Bind<bool>(
                "Logging",
                "Enable Craft Logging",
                false,
                "When enabled, the console will show logging of all culled chains. Useful when modifying the traversal algorithm."
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

            TierManager.Init(Log);
            RegisterAssets.Init();
            RecipeProvider.Init(Log); // Parse all chef recipe rules
            ChatNetworkHandler.Init(Log);

            StateController.Init(Log); // Initialize chef/state logic
            InventoryTracker.Init(Log); // Begin waiting for Enable signal

            DialogueHooks.Init(Log); // Initialize Chef Dialogue Hooks
            ObjectiveTracker.Init();
            CraftUI.Init(Log); // Initialize craft UI injection

            // VanillaCraftingTrace.Init(Log);
            // RecipeTrackerUI.Init(Log);

            TryHookCleanerChef();

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

            PreventCorruptedCrafting.SettingChanged += StateController.OnPreventCorruptedCraftingChanged;
            ShowCorruptedResults.SettingChanged += StateController.OnShowCorruptedResultsChanged;
            MaxDepth.SettingChanged += StateController.OnMaxDepthChanged; MaxBridgeItemsPerChain.SettingChanged += StateController.OnMaxBridgeItemsPerChainChanged;

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
            foreach (var tierEntry in TierPriorities.Values)
            {
                if (tierEntry != null) tierEntry.SettingChanged -= TierManager.OnTierPriorityChanged;
            }

            PreventCorruptedCrafting.SettingChanged -= StateController.OnPreventCorruptedCraftingChanged;
            ShowCorruptedResults.SettingChanged -= StateController.OnShowCorruptedResultsChanged;
            MaxDepth.SettingChanged -= StateController.OnMaxDepthChanged;
            MaxChainsPerResult.SettingChanged -= StateController.OnMaxChainsPerResultChanged;
            MaxBridgeItemsPerChain.SettingChanged -= StateController.OnMaxBridgeItemsPerChainChanged;

            RecipeProvider.OnRecipesBuilt -= StateController.OnRecipesBuilt;
            TierManager.OnTierOrderChanged -= StateController.OnTierOrderChanged;

            DialogueHooks.ChefUiOpened -= StateController.OnChefUiOpened;
            DialogueHooks.ChefUiClosed -= StateController.OnChefUiClosed;

            On.RoR2.CraftingController.FilterAvailableOptions -= RecipeFilter.PatchVanillaNRE;

            RecipeProvider.Shutdown();
            StateController.Shutdown();
            DialogueHooks.Shutdown();
            CraftUI.Shutdown();
            ObjectiveTracker.Shutdown();
            ChatNetworkHandler.ShutDown();
        }

        private void TryHookCleanerChef()
        {
            if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("rainorshine.CleanChef", out var info) || info == null)
                return;

            try
            {
                if (info.Instance == null)
                {
                    Log.LogInfo("CookBook: CleanChef present but not instantiated yet; deferring interop hook.");
                    RoR2Application.onLoad += () => { TryHookCleanerChef(); };
                    return;
                }

                var asm = info.Instance.GetType().Assembly;
                var apiType = asm.GetType("CleanerChef.CleanerChefAPI", throwOnError: false);
                if (apiType == null)
                {
                    Log.LogWarning("CookBook: CleanChef found but CleanerChefAPI type not found; skipping interop.");
                    return;
                }

                var evt = apiType.GetEvent("HaltCorruptionChanged");
                var prop = apiType.GetProperty("HaltCorruptionEnabled");
                if (evt == null || prop == null)
                {
                    Log.LogWarning("CookBook: CleanChef API missing expected members; skipping interop.");
                    return;
                }

                Action<bool> handlerMethod = StateController.OnCleanerChefHaltChanged;
                var handler = Delegate.CreateDelegate(evt.EventHandlerType, handlerMethod.Target, handlerMethod.Method);

                evt.AddEventHandler(null, handler);

                StateController.InstallCleanerChefInterop(apiType, evt, prop, handler);

                bool current = (bool)prop.GetValue(null, null);
                StateController.OnCleanerChefHaltChanged(current);

                Log.LogInfo("CookBook: CleanerChef interop hooked (reflection).");
            }
            catch (Exception e)
            {
                Log.LogWarning($"CookBook: CleanerChef interop failed; skipping. {e.GetType().Name}: {e.Message}");
            }
        }

    }

    internal static class DebugLog
    {
        public static void Trace(ManualLogSource log, string message)
        {
            if (!CookBook.isDebugMode)
                return;

            log.LogDebug(message);
        }
        public static void CraftTrace(ManualLogSource log, string message)
        {
            if (!CookBook.isLogCraftMode)
                return;

            log.LogDebug(message);
        }
    }
}