using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using UnityEngine;
using static CookBook.CraftPlanner;


namespace CookBook
{
    internal static class StateController
    {
        private static ManualLogSource _log;
        private static CraftPlanner _planner;

        private static bool _initialized = false;
        private static bool _subscribedInventoryHandler = false;
        internal static SceneDef _laststage = null;
        private static CraftingController _activeCraftingController;
        private static List<CraftPlanner.CraftableEntry> _lastCraftables = new List<CraftPlanner.CraftableEntry>();

        // Crafting Parameters
        internal static bool IsAutoCrafting => _craftingRoutine != null;
        private static GameObject _targetCraftingObject;
        private static Coroutine _craftingRoutine;
        private static GameObject _runnerGO;
        private static MonoBehaviour _runner;

        private static float _mealPrepTotalDuration = 4.0f;
        private static FieldInfo _mixField;
        private static FieldInfo _storeField;
        private static bool _reflectionAttemptedAndFound = false;

        // Events
        internal static event Action<IReadOnlyList<CraftPlanner.CraftableEntry>> OnCraftablesForUIChanged;
        internal static event Action OnChefStageEntered;
        internal static event Action OnChefStageExited;

        private class StateRunner : MonoBehaviour
        {
        }

        //--------------------------- LifeCycle -------------------------------
        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
            {
                return;
            }

            _log = log;
            _initialized = true;
            _planner = null;

            _runnerGO = new GameObject("CookBookStateRunner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerGO);

            Run.onRunStartGlobal += OnRunStart;
            Run.onRunDestroyGlobal += OnRunDestroy;
            Stage.onStageStartGlobal += OnStageStart;
        }

        /// <summary>
        /// Called on mod death.
        /// </summary>
        internal static void Shutdown()
        {
            Run.onRunStartGlobal -= OnRunStart;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            Stage.onStageStartGlobal -= OnStageStart;

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
            _subscribedInventoryHandler = false;

            _planner = null;
            _runnerGO = null;
            _lastCraftables.Clear();
        }

        /// <summary>
        /// Called whenever a new Stage instance starts.
        /// </summary>
        internal static void OnStageStart(Stage stage)
        {
            var sceneDef = stage ? stage.sceneDef : null;

            if (IsChefStage(sceneDef))
            {
                OnChefStageEntered?.Invoke();
            }
            else if (IsChefStage(_laststage))
            {
                OnChefStageExited?.Invoke();
            }

            _laststage = sceneDef; // update last stage
        }

        /// <summary>
        /// Lightweight Chef State tracking throughout each run.
        /// </summary>
        private static void OnRunStart(Run run)
        {
            StateController.OnChefStageEntered += EnableChef;
            StateController.OnChefStageExited += DisableChef;
            _runner = _runnerGO.AddComponent<StateRunner>();
        }

        /// <summary>
        /// Cleanup Chef State Tracker on run end.
        /// </summary>
        private static void OnRunDestroy(Run run)
        {
            if (_runner)
            {
                UnityEngine.Object.Destroy(_runner);
                _runner = null;
            }

            CraftUI.Detach();

            StateController.OnChefStageEntered -= EnableChef;
            StateController.OnChefStageExited -= DisableChef;

            _subscribedInventoryHandler = false;
            _lastCraftables.Clear();

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
        }

        // -------------------- Rebuild Events --------------------
        /// <summary>
        /// Called when RecipeProvider finishes building the recipe list.
        /// </summary>
        internal static void OnRecipesBuilt(System.Collections.Generic.IReadOnlyList<ChefRecipe> recipes)
        {
            _log.LogInfo($"CookBook: OnRecipesBuilt fired with {recipes.Count} recipes; constructing CraftPlanner.");

            var planner = new CraftPlanner(recipes, CookBook.MaxDepth.Value, _log);
            StateController.SetPlanner(planner);
        }

        // -------------------- CookBook Handshake Events --------------------
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
            {
                return;
            }

            OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnTierOrderChanged()
        {
            if (_lastCraftables == null || _lastCraftables.Count == 0)
            {
                return;
            }

            if (!StateController.IsChefStage())
            {
                return;
            }

            _lastCraftables.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnMaxDepthChanged(object _, EventArgs __)
        {
            if (_planner == null)
            {
                _log?.LogDebug("OnMaxDepthChanged(): planner is null, ignoring.");
                return;
            }

            int newDepth = CookBook.MaxDepth.Value;
            _log?.LogInfo($"OnMaxDepthChanged(): updated planner max depth to {newDepth}.");

            _planner.SetMaxDepth(newDepth);
            _planner.RebuildAllPlans();

            if (!IsChefStage())
            {
                _log?.LogDebug("OnMaxDepthChanged(): Maxdepth changed but not on chef stage, ommitting snapshot.");
                return;
            }

            TakeSnapshot(InventoryTracker.GetItemStacksCopy(), InventoryTracker.GetEquipmentStacksCopy());
        }

        // -------------------- Chef Events --------------------
        private static void EnableChef()
        {
            _log.LogInfo("enabling InventoryTracker and subscribing to inventory events.");

            if (!_subscribedInventoryHandler)
            {
                InventoryTracker.OnInventoryChanged += OnInventoryChanged;
                _subscribedInventoryHandler = true;
            }

            InventoryTracker.Enable();
            _targetCraftingObject = null;
        }

        private static void DisableChef()
        {
            _log.LogInfo("Disabling InventoryTracker and inventory events.");

            if (_subscribedInventoryHandler)
            {
                InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
                _subscribedInventoryHandler = false;
            }

            InventoryTracker.Disable();
        }

        internal static void OnChefUiOpened(CraftingController controller)
        {
            if (!IsChefStage())
            {
                return;
            }

            _activeCraftingController = controller;
            _targetCraftingObject = controller.gameObject;

            if (!IsAutoCrafting)
            {
                CraftUI.Attach(_activeCraftingController);
                CacheMealPrepTimings();
            }
        }

        internal static void OnChefUiClosed(CraftingController controller)
        {
            CraftUI.Detach();
            _activeCraftingController = null;
        }

        //-------------------------------- Crafting Logic ----------------------------------

        private static void CacheMealPrepTimings()
        {
            if (_reflectionAttemptedAndFound) return;

            try
            {
                var flags = BindingFlags.Public | BindingFlags.Static;

                float d1 = (float)typeof(EntityStates.MealPrep.WaitToBeginCooking).GetField("duration", flags).GetValue(null);
                float d2 = (float)typeof(EntityStates.MealPrep.Cooking).GetField("duration", flags).GetValue(null);
                float d3 = (float)typeof(EntityStates.MealPrep.CookingToIdle).GetField("duration", flags).GetValue(null);

                _mealPrepTotalDuration = d1 + d2 + d3;

                _reflectionAttemptedAndFound = true;
                _log.LogInfo($"[CookBook] MealPrep timings cached: {d1:F2} + {d2:F2} + {d3:F2} = {_mealPrepTotalDuration:F2}s total wait.");
            }
            catch (Exception e)
            {
                _log.LogWarning($"[CookBook] Failed to reflect MealPrep durations. Using default 4.0s. Error: {e.Message}");
            }
        }

        internal static void RequestCraft(RecipeChain chain)
        {
            if (_activeCraftingController == null)
            {
                _log.LogDebug("Attemmpted to initiate a craft but no active crafting controller.");
                return;
            }
            if (chain == null)
            {
                _log.LogDebug("Attemmpted to initiate a craft but no valid crafting chain.");
                return;
            }

            if (IsAutoCrafting)
            {
                _log.LogDebug("Craft already in progress, killing previous chain.");
                _runner.StopCoroutine(_craftingRoutine);
            }

            _craftingRoutine = _runner.StartCoroutine(CraftChainRoutine(chain));
        }

        // TODO: harden chain logic so that rather than instantly complaining about a missing item it ensures that the item from the previous craft in the chain was picked up. Check for droplet binaries/pickup binaries
        private static IEnumerator CraftChainRoutine(RecipeChain chain)
        {
            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>(chain.Steps.Reverse());
            _log.LogInfo($"[StateController] Headless craft started. {craftQueue.Count} steps remaining.");

            var interactor = LocalUserManager.GetFirstLocalUser()?.cachedBody?.GetComponent<Interactor>();

            while (craftQueue.Count > 0)
            {
                if (!_targetCraftingObject)
                {
                    _log.LogError("Cannot find crafting object. Aborting.");
                    yield break;
                }

                if (!interactor) yield break;

                float uiTimeout = 2.0f;
                while (_activeCraftingController == null && uiTimeout > 0)
                {
                    if (interactor) interactor.AttemptInteraction(_targetCraftingObject);

                    for (int i = 0; i < 5; i++)
                    {
                        if (_activeCraftingController != null) break;
                        yield return new WaitForFixedUpdate();
                    }
                    uiTimeout -= 0.1f;
                }

                if (_activeCraftingController == null)
                {
                    _log.LogError("Failed to re-open Crafting menu. Aborting chain.");
                    yield break;
                }

                ChefRecipe step = craftQueue.Dequeue();
                _activeCraftingController.ClearAllSlots();

                if (!SubmitIngredients(_activeCraftingController, step))
                {
                    _log.LogWarning($"Missing ingredients for step {step.ResultItem}. Aborting.");
                    CraftUI.CloseCraftPanel(_activeCraftingController);
                    _craftingRoutine = null;
                    yield break;
                }

                float confirmTimeout = 2.0f;
                while (!_activeCraftingController.AllSlotsFilled() && confirmTimeout > 0)
                {
                    confirmTimeout -= Time.fixedDeltaTime;
                    yield return new WaitForFixedUpdate();
                }

                if (!_activeCraftingController.AllSlotsFilled())
                {
                    _log.LogError("Slots did not fill in time, aborting.");
                    CraftUI.CloseCraftPanel(_activeCraftingController);
                    _craftingRoutine = null;
                    yield break;
                }

                _activeCraftingController.ConfirmSelection();
                CraftUI.CloseCraftPanel(_activeCraftingController);

                if (craftQueue.Count > 0)
                {
                    yield return new WaitForSeconds(_mealPrepTotalDuration + 0.2f);
                }

            }
            _craftingRoutine = null;
            _log.LogInfo("[StateController] Chain Complete.");

        }

        private static bool SubmitIngredients(CraftingController controller, ChefRecipe recipe)
        {
            var options = controller.options;

            HashSet<PickupIndex> availablePickups = new HashSet<PickupIndex>();
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].available) availablePickups.Add(options[i].pickup.pickupIndex);
            }

            foreach (var ing in recipe.Ingredients)
            {
                PickupIndex targetPickup = PickupIndex.none;

                if (ing.Kind == IngredientKind.Item)
                {
                    targetPickup = PickupCatalog.FindPickupIndex(ing.Item);
                }
                else if (ing.Kind == IngredientKind.Equipment)
                {
                    targetPickup = PickupCatalog.FindPickupIndex(ing.Equipment);
                }

                if (targetPickup == PickupIndex.none)
                {
                    _log.LogWarning($"[CookBook] Could not resolve pickup for {ing.Kind}");
                    return false;
                }

                if (!availablePickups.Contains(targetPickup))
                {
                    var debugName = PickupCatalog.GetPickupDef(targetPickup)?.internalName ?? "Unknown";
                    _log.LogWarning($"[CookBook] FAILED: Player missing required item: {debugName} (PickupIndex: {targetPickup.value})");
                    return false;
                }


                var name = PickupCatalog.GetPickupDef(targetPickup)?.internalName;
                _log.LogInfo($"[CookBook] Adding ingredient: {name} (ID: {targetPickup.value})");

                controller.SendToSlot(targetPickup.value);
            }

            return true;
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

            TakeSnapshot(InventoryTracker.GetItemStacksCopy(), InventoryTracker.GetEquipmentStacksCopy());
        }


        //--------------------------------------- Generic Helpers ----------------------------------------
        internal static bool IsChefStage(SceneDef sceneDef) => sceneDef && sceneDef.baseSceneName == "computationalexchange";
        internal static SceneDef GetCurrentScene() => Stage.instance ? Stage.instance.sceneDef : null;
        internal static bool IsChefStage() => IsChefStage(GetCurrentScene());
        internal static void TakeSnapshot(int[] itemstacks, int[] equipmentstacks)
        {
            var itemStacks = InventoryTracker.GetItemStacksCopy();
            var equipmentStacks = InventoryTracker.GetEquipmentStacksCopy();

            if (itemStacks != null && equipmentStacks != null)
            {
                _planner.ComputeCraftable(itemStacks, equipmentStacks);
                _log.LogDebug($"TakeSnapshot(): craftable entries = {_lastCraftables.Count}");
            }
            else
            {
                _log.LogDebug("TakeSnapshot(): Attempted snapshot but inventory doesn't exist.");
            }
        }
    }
}
