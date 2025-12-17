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
        private static MonoBehaviour _craftingHandler;

        // Events
        internal static event Action<IReadOnlyList<CraftPlanner.CraftableEntry>> OnCraftablesForUIChanged;
        internal static event Action OnChefStageEntered;
        internal static event Action OnChefStageExited;

        private class StateRunner : MonoBehaviour { }

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
            _craftingHandler = _runnerGO.AddComponent<StateRunner>();
        }

        /// <summary>
        /// Cleanup Chef State Tracker on run end.
        /// </summary>
        private static void OnRunDestroy(Run run)
        {
            if (_craftingHandler)
            {
                UnityEngine.Object.Destroy(_craftingHandler);
                _craftingHandler = null;
            }

            CraftUI.Detach();

            StateController.OnChefStageEntered -= EnableChef;
            StateController.OnChefStageExited -= DisableChef;

            _subscribedInventoryHandler = false;
            _lastCraftables.Clear();

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChanged -= OnInventoryChanged;
        }

        // -------------------- CookBook Handshake Events --------------------
        internal static void OnRecipesBuilt(System.Collections.Generic.IReadOnlyList<ChefRecipe> recipes)
        {
            var planner = new CraftPlanner(recipes, CookBook.MaxDepth.Value, _log);
            StateController.SetPlanner(planner);
        }

        private static void OnInventoryChanged(int[] unifiedStacks)
        {
            if (InventoryTracker.LastItemCountUsed != _planner.SourceItemCount)
            {
                _log.LogWarning("Catalog Shift Detected, Rebuilding Recipes.");
                RecipeProvider.Rebuild();
                return;
            }
            _planner.ComputeCraftable(unifiedStacks);
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

            TakeSnapshot(InventoryTracker.GetUnifiedStacksCopy());
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
            }
        }

        internal static void OnChefUiClosed(CraftingController controller)
        {
            CraftUI.Detach();
            _activeCraftingController = null;
        }

        //-------------------------------- Crafting Logic ----------------------------------
        private static IEnumerator WaitForPendingPickup(PickupIndex pickupIndex, int expectedGain, float timeout = 5.0f)
        {
            if (pickupIndex == PickupIndex.none)
            {
                yield break;
            }

            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;

            if (!body || !body.inventory)
            {
                yield break;
            }

            var def = PickupCatalog.GetPickupDef(pickupIndex);
            if (def == null)
            {
                yield break;
            }

            var name = def.internalName;
            _log.LogDebug($"[Chain] Waiting for result: {name} (Index: {pickupIndex.value}, quantity {expectedGain})...");

            int targetCount = GetOwnedCount(def, body) + expectedGain;

            float timer = 0f;

            while (timer < timeout)
            {
                int currentCount = GetOwnedCount(def, body);
                if (currentCount >= targetCount)
                {
                    _log.LogInfo($"[Chain] Confirmed pickup of {name} (Reached {currentCount}/{targetCount}). Proceeding.");
                    yield break;
                }

                timer += Time.fixedDeltaTime;
                yield return new WaitForFixedUpdate();
            }

            _log.LogWarning($"[Chain] Timed out waiting for {name}. Did the user not collect all items?");
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
                _craftingHandler.StopCoroutine(_craftingRoutine);
            }

            _craftingRoutine = _craftingHandler.StartCoroutine(CraftChainRoutine(chain));
        }

        private static IEnumerator CraftChainRoutine(RecipeChain chain)
        {
            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>(chain.Steps.Reverse());
            _log.LogInfo($"[StateController] Headless craft started. {chain.Steps.Count} steps remaining.");

            var interactor = LocalUserManager.GetFirstLocalUser()?.cachedBody?.GetComponent<Interactor>();
            PickupIndex lastCraftedPickup = PickupIndex.none;
            int lastCraftedQuantity = 0;

            while (craftQueue.Count > 0)
            {
                if (!_targetCraftingObject)
                {
                    _log.LogError("Cannot find crafting object. Aborting.");
                    yield break;
                }

                if (!interactor) yield break;

                if (lastCraftedPickup != PickupIndex.none)
                {
                    yield return WaitForPendingPickup(lastCraftedPickup, lastCraftedQuantity, 5.0f);
                }

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
                    string stepName = "Unknown";
                    if (step.ResultIndex < ItemCatalog.itemCount)
                        stepName = ItemCatalog.GetItemDef((ItemIndex)step.ResultIndex)?.nameToken;
                    else
                        stepName = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount))?.nameToken;

                    _log.LogWarning($"Missing ingredients to craft {stepName}. Aborting.");
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
                lastCraftedQuantity = step.ResultCount;

                if (step.ResultIndex < ItemCatalog.itemCount)
                {
                    lastCraftedPickup = PickupCatalog.FindPickupIndex((ItemIndex)step.ResultIndex);
                }
                else
                {
                    lastCraftedPickup = PickupCatalog.FindPickupIndex((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount));
                }

                if (craftQueue.Count > 0)
                {
                    yield return new WaitForSeconds(0.2f);
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

                if (ing.IsItem)
                {
                    targetPickup = PickupCatalog.FindPickupIndex(ing.ItemIndex);
                }
                else
                {
                    targetPickup = PickupCatalog.FindPickupIndex(ing.EquipIndex);
                }

                if (targetPickup == PickupIndex.none)
                {
                    _log.LogWarning($"[CookBook] Could not resolve pickup for UnifiedIndex {ing.UnifiedIndex}");
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

            TakeSnapshot(InventoryTracker.GetUnifiedStacksCopy());
        }


        //--------------------------------------- Generic Helpers ----------------------------------------
        internal static bool IsChefStage(SceneDef sceneDef) => sceneDef && sceneDef.baseSceneName == "computationalexchange";
        internal static SceneDef GetCurrentScene() => Stage.instance ? Stage.instance.sceneDef : null;
        internal static bool IsChefStage() => IsChefStage(GetCurrentScene());
        internal static void TakeSnapshot(int[] unifiedStacks)
        {
            if (unifiedStacks != null)
            {
                _planner.ComputeCraftable(unifiedStacks);
                _log.LogDebug($"TakeSnapshot(): craftable entries = {_lastCraftables.Count}");
            }
            else
            {
                _log.LogDebug("TakeSnapshot(): Attempted snapshot but inventory doesn't exist.");
            }
        }
        internal static int GetOwnedCount(PickupDef def, CharacterBody body)
        {
            if (def.itemIndex != ItemIndex.None)
            {
                return body.inventory.GetItemCountPermanent(def.itemIndex);
            }
            else if (def.equipmentIndex != EquipmentIndex.None)
            {
                return body.inventory.currentEquipmentIndex == def.equipmentIndex ? 1 : 0;
            }
            return 0;
        }
    }
}
