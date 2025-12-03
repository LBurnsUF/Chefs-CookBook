using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CookBook
{
    internal static class ChefStateController
    {
        private static ManualLogSource _log;
        private static CraftPlanner _planner;
        private static List<CraftPlanner.CraftableEntry> _lastCraftables = new List<CraftPlanner.CraftableEntry>();

        private static bool _initialized;
        private static bool _subscribedInventoryHandler = false;

        internal static event Action<List<CraftPlanner.CraftableEntry>> OnCraftablesUpdated;

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _planner = null; // initialized once ready

            InventoryTracker.Init(log);

            Stage.onStageStartGlobal += OnStageStart;
            Run.onRunDestroyGlobal += OnRunDestroy;

            log.LogInfo("ChefStateController.Init()");
        }

        //-------------------------------- State Tracking ----------------------------------
        /// <summary>
        /// called whenever a new Stage instance starts, use for scene-based control
        /// </summary>
        private static void OnStageStart(Stage stage)
        {
            var sceneDef = stage ? stage.sceneDef : null;
            var sceneName = sceneDef ? sceneDef.baseSceneName : "<null>";

            if (IsChefStage(sceneDef))
            {
                _log.LogInfo("Entering Chef stage, enabling InventoryTracker and subscribing to inventory events.");

                if (!_subscribedInventoryHandler)
                {
                    InventoryTracker.OnInventoryChanged += OnInventoryChanged;
                    _subscribedInventoryHandler = true;
                }

                InventoryTracker.Enable();

                // TODO: hook Chef NPC interaction state
                // TODO: hook into Chef UI and enable UI handling via CraftUI.cs
            }
            else
            {
                InventoryTracker.Disable();
                InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
                _subscribedInventoryHandler = false;

                // TODO: disable custom UI entirely
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
            _lastCraftables.Clear();
        }

        private static void OnInventoryChanged(int[] itemStacks, int[] equipmentStacks)
        {
            if (_planner == null)
            {
                _log.LogDebug("ChefStateController.OnInventoryChanged: planner not assigned yet, ignoring.");
                return;
            }

            _lastCraftables = _planner.ComputeCraftable(itemStacks, equipmentStacks);

            OnCraftablesUpdated?.Invoke(_lastCraftables); // notify listeners that new craftables is built using invoke
        }

        //--------------------------------------- Helpers ----------------------------------------
        /// <summary>
        /// Set the planner for a given StateController.
        /// </summary>
        internal static void SetPlanner(CraftPlanner planner)
        {
            _planner = planner;
            _log.LogInfo("ChefStateController.SetPlanner(): CraftPlanner assigned.");

            if (!IsChefStage())
            {
                return;
            }

            if (!_subscribedInventoryHandler)
            {
                InventoryTracker.OnInventoryChanged += OnInventoryChanged;
                _subscribedInventoryHandler = true;
            }
            
            var itemstacks = InventoryTracker.GetItemStacksCopy();
            var equipmentstacks = InventoryTracker.GetEquipmentStacksCopy();
            if (itemstacks != null && equipmentstacks != null)
            {
                _lastCraftables = _planner.ComputeCraftable(itemstacks, equipmentstacks);
                _log.LogDebug($"ChefStateController: initial craftable entries = {_lastCraftables.Count}");
            }
            else
            {
                _log.LogDebug("ChefStateController.SetPlanner(): Attempted setplanner, but inventory doesn't exist.");
            }
        }

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

        // TODO: Chef dialogue detection
        // - Hook the Chef NPC’s interaction component
        // - Detect when the player opens/closes that dialogue
        // - Show/hide cookbook UI only while that dialogue is open
    }
}
