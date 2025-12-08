using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;

namespace CookBook
{
    internal static class StateController
    {
        private static ManualLogSource _log;
        private static CraftPlanner _planner;
        private static CraftingController _activeCraftingController;
        private static List<CraftPlanner.CraftableEntry> _lastCraftables = new List<CraftPlanner.CraftableEntry>();
        private static bool _subscribedInventoryHandler = false;
        private static bool _initialized = false;

        private static bool _chefDialogueOpen;
        internal static bool IsChefDialogueOpen => _chefDialogueOpen;

        // Events
        internal static event Action<IReadOnlyList<CraftPlanner.CraftableEntry>> OnCraftablesForUIChanged;

        //--------------------------- LifeCycle -------------------------------
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _log = log;
            
            _initialized = true;
            _planner = null; // initialized once ready

            // subscribe to stagechange events
            Stage.onStageStartGlobal += OnStageStart;
            Run.onRunDestroyGlobal += OnRunDestroy;
        }

        internal static void Shutdown()
        {
            Stage.onStageStartGlobal -= OnStageStart;
            Run.onRunDestroyGlobal -= OnRunDestroy;

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChanged -= OnInventoryChanged;

            _planner = null;
            _lastCraftables.Clear();
        }

        //-------------------------------- State Tracking ----------------------------------
        /// <summary>
        /// called whenever a new Stage instance starts, use for scene-based control
        /// </summary>
        internal static void OnStageStart(Stage stage)
        {
            var sceneDef = stage ? stage.sceneDef : null;

            if (IsChefStage(sceneDef))
            {
                _log.LogInfo("Entering Chef stage, enabling InventoryTracker and subscribing to inventory events.");

                if (!_subscribedInventoryHandler)
                {
                    InventoryTracker.OnInventoryChanged += OnInventoryChanged;
                    _subscribedInventoryHandler = true;
                }

                InventoryTracker.Enable();
            }
            else
            {
                InventoryTracker.Disable();
                InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
                _subscribedInventoryHandler = false;
            }
        }

        /// <summary>
        /// Called when RecipeProvider has finished building ChefRecipe list. also fired when recipe list rebuilt
        /// </summary>
        internal static void OnRecipesBuilt(System.Collections.Generic.IReadOnlyList<ChefRecipe> recipes)
        {
            _log.LogInfo($"CookBook: OnRecipesBuilt fired with {recipes.Count} recipes; constructing CraftPlanner.");

            var planner = new CraftPlanner(recipes, CookBook.MaxDepth.Value, _log);
            StateController.SetPlanner(planner); // Hand a fresh planner to the state controller
        }

        private static void OnInventoryChanged(int[] itemStacks, int[] equipmentStacks)
        {
            if (_planner == null)
            {
                _log.LogDebug("StateController.OnInventoryChanged: planner not assigned yet, ignoring.");
                return;
            }
            _planner.ComputeCraftable(itemStacks, equipmentStacks);
        }

        private static void OnCraftablesUpdated(List<CraftPlanner.CraftableEntry> craftables)
        {
            _lastCraftables = craftables;

            if (!StateController.IsChefStage())
                return;

            OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnTierOrderChanged()
        {
            if (_lastCraftables == null || _lastCraftables.Count == 0)
                return;

            if (!StateController.IsChefStage())
                return;

            _lastCraftables.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnMaxDepthChanged(object _, EventArgs __)
        {
            if (_planner == null)
            {
                _log?.LogDebug("OnMaxDepthChanged: planner is null, ignoring.");
                return;
            }

            int newDepth = CookBook.MaxDepth.Value;
            _log?.LogInfo($"OnMaxDepthChanged: updated planner max depth to {newDepth}.");

            _planner.SetMaxDepth(newDepth);
            _planner.RebuildAllPlans();

            // recompute craftables if on the Chef stage
            if (!IsChefStage())
                return;

            var itemStacks = InventoryTracker.GetItemStacksCopy();
            var equipmentStacks = InventoryTracker.GetEquipmentStacksCopy();

            if (itemStacks != null && equipmentStacks != null &&
                itemStacks.Length > 0 && equipmentStacks.Length > 0)
            {
                _planner.ComputeCraftable(itemStacks, equipmentStacks);
            }
        }
        
        /// <summary>
        /// Cleanup Chef State Tracker when the run ends
        /// </summary>
        private static void OnRunDestroy(Run run)
        {
            _log.LogInfo("Run ended -> disabling InventoryTracker and resetting Chef state.");
            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
            _subscribedInventoryHandler = false;
            _planner = null;
            _chefDialogueOpen = false;
            _lastCraftables.Clear();
            CraftUI.Detach();
        }

        // -------------------- Chef dialogue events --------------------
        internal static void OnChefUiOpened(CraftingController controller)
        {
            if (!IsChefStage())
            {
                return;
            }

            _chefDialogueOpen = true;
            _activeCraftingController = controller;
            CraftUI.Attach(_activeCraftingController); // show CraftUI
        }

        internal static void OnChefUiClosed(CraftingController controller)
        {
            CraftUI.Detach(); // Destroy CraftUI
            _chefDialogueOpen = false;

            if (_activeCraftingController == controller)
            {
                _activeCraftingController = null;
            }
        }

        //--------------------------------------- Planning Helpers ----------------------------------------
        /// <summary>
        /// Set the planner for a given StateController.
        /// </summary>
        internal static void SetPlanner(CraftPlanner planner)
        {
            if (_planner != null)
            {
                _planner.OnCraftablesUpdated -= OnCraftablesUpdated;
            }

            _planner = planner; // overwrite stale planner
            _log.LogInfo("StateController.SetPlanner(): CraftPlanner assigned.");

            if (_planner != null)
            {
                _planner.OnCraftablesUpdated += OnCraftablesUpdated;
            }

            if (!IsChefStage()) 
            {
                return;
            }

            if (!_subscribedInventoryHandler)
            {
                InventoryTracker.OnInventoryChanged += OnInventoryChanged;
                _subscribedInventoryHandler = true;
            }
            
            // compute initial snapshot
            var itemstacks = InventoryTracker.GetItemStacksCopy();
            var equipmentstacks = InventoryTracker.GetEquipmentStacksCopy(); // number of equipment with a given equipment index i
            if (itemstacks != null && equipmentstacks != null)
            {
                _planner.ComputeCraftable(itemstacks, equipmentstacks); // fires OnCraftablesUpdated, which then reassigns _lastCraftables
                _log.LogDebug($"StateController: initial craftable entries = {_lastCraftables.Count}");
            }
            else
            {
                _log.LogDebug("StateController.SetPlanner(): Attempted setplanner, but inventory doesn't exist.");
            }
        }

        //--------------------------------------- Generic Helpers ----------------------------------------
        /// <summary>
        /// Check if the current SceneDef corresponds to the Chef stage
        /// </summary>
        internal static bool IsChefStage(SceneDef sceneDef)
        {
            return sceneDef && sceneDef.baseSceneName == "computationalexchange";
        }

        /// <summary>
        /// retrieve the current SceneDef (if any)
        /// </summary>
        internal static SceneDef GetCurrentScene()
        {
            return Stage.instance ? Stage.instance.sceneDef : null;
        }

        /// <summary>
        /// True if the *current* scene is the Chef stage.
        /// Safe wrapper for external callers.
        /// </summary>
        internal static bool IsChefStage()
        {
            return IsChefStage(GetCurrentScene());
        }
    }
}
