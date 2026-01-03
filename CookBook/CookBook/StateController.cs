using BepInEx;
using BepInEx.Logging;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CookBook
{
    internal static class StateController
    {
        private static ManualLogSource _log;
        private static CraftPlanner _planner;

        private static bool _initialized = false;
        private static bool _subscribedInventoryHandler = false;
        internal static SceneDef _laststage = null;

        private static List<CraftPlanner.CraftableEntry> _lastCraftables = new List<CraftPlanner.CraftableEntry>();
        private static GameObject _runnerGO;
        private static StateRunner _craftingHandler;
        private static Coroutine _throttleRoutine;

        // Crafting Parameters
        internal static CraftingController ActiveCraftingController { get; private set; }
        internal static GameObject TargetCraftingObject { get; private set; }
        internal static bool IsAutoCrafting => CraftingExecutionHandler.IsAutoCrafting;
        public static bool BatchMode { get; set; } = false;

        // Events
        internal static event Action<IReadOnlyList<CraftPlanner.CraftableEntry>> OnCraftablesForUIChanged;
        internal static event Action OnChefStageEntered;
        internal static event Action OnChefStageExited;

        //--------------------------- LifeCycle -------------------------------
        internal static void Init(ManualLogSource log)
        {
            if (_initialized) return;

            _log = log;
            _initialized = true;
            _planner = null;

            _runnerGO = new GameObject("CookBookStateRunner");
            UnityEngine.Object.DontDestroyOnLoad(_runnerGO);

            Run.onRunStartGlobal += OnRunStart;
            Run.onRunDestroyGlobal += OnRunDestroy;
            Stage.onStageStartGlobal += OnStageStart;

            DialogueHooks.ChefUiOpened += OnChefUiOpened;
            DialogueHooks.ChefUiClosed += OnChefUiClosed;

            ChatNetworkHandler.OnIncomingObjective += OnNetworkObjectiveReceived;
        }

        /// <summary>
        /// Called on mod death.
        /// </summary>
        internal static void Shutdown()
        {
            Run.onRunStartGlobal -= OnRunStart;
            Run.onRunDestroyGlobal -= OnRunDestroy;
            Stage.onStageStartGlobal -= OnStageStart;

            DialogueHooks.ChefUiOpened -= OnChefUiOpened;
            DialogueHooks.ChefUiClosed -= OnChefUiClosed;
            DialogueHooks.Shutdown();

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChangedWithIndices -= OnInventoryChanged;
            _subscribedInventoryHandler = false;

            ChatNetworkHandler.OnIncomingObjective -= OnNetworkObjectiveReceived;
            ChatNetworkHandler.Disable();

            _planner = null;
            _runnerGO = null;
            _lastCraftables.Clear();
        }

        /// <summary>
        /// Called whenever a new Stage instance starts. 
        /// </summary>
        internal static void OnStageStart(Stage stage)
        {
            if (CheckForChefPresence())
            {
                OnChefStageEntered?.Invoke();
            }
            else
            {
                OnChefStageExited?.Invoke();
            }
        }

        /// <summary>
        /// Lightweight Chef State tracking throughout each run.
        /// </summary>
        private static void OnRunStart(Run run)
        {
            OnChefStageEntered += EnableChef;
            OnChefStageExited += DisableChef;

            ChatNetworkHandler.Enable();
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

            OnChefStageEntered -= EnableChef;
            OnChefStageExited -= DisableChef;

            _subscribedInventoryHandler = false;
            _lastCraftables.Clear();

            ChatNetworkHandler.Disable();

            InventoryTracker.Disable();
            InventoryTracker.OnInventoryChangedWithIndices -= OnInventoryChanged;
        }

        // -------------------- CookBook Handshake Events --------------------
        private static void OnNetworkObjectiveReceived(NetworkUser sender, string command, int unifiedIdx, int quantity)
        {
            CraftingObjectiveTracker.AddAlliedRequest(sender, command, unifiedIdx, quantity);
        }

        internal static void OnRecipesBuilt(IReadOnlyList<ChefRecipe> recipes)
        {
            var planner = new CraftPlanner(recipes, CookBook.MaxDepth.Value, _log);
            SetPlanner(planner);
        }

        private static void OnInventoryChanged(int[] unifiedStacks, HashSet<int> changedIndices)
        {
            if (BatchMode) return;

            QueueThrottledCompute(unifiedStacks, changedIndices);
        }

        private static void OnCraftablesUpdated(List<CraftPlanner.CraftableEntry> craftables)
        {
            _lastCraftables = craftables;
            if (IsChefStage()) OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnTierOrderChanged()
        {
            if (_lastCraftables == null || !IsChefStage()) return;
            _lastCraftables.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesForUIChanged?.Invoke(_lastCraftables);
        }

        internal static void OnMaxDepthChanged(object _, EventArgs __)
        {
            if (_planner == null) return;

            _planner.SetMaxDepth(CookBook.MaxDepth.Value);

            if (IsChefStage())
            {
                QueueThrottledCompute(InventoryTracker.GetUnifiedStacksCopy(), null);
            }
        }

        internal static void OnMaxChainsPerResultChanged(object _, EventArgs __)
        {
            if (_planner == null) return;

            if (IsChefStage())
            {
                QueueThrottledCompute(InventoryTracker.GetUnifiedStacksCopy(), null);
            }
        }

        // -------------------- Chef Events --------------------
        private static void EnableChef()
        {
            _log.LogInfo("Chef controller enabled.");

            if (!_subscribedInventoryHandler)
            {
                InventoryTracker.OnInventoryChangedWithIndices += OnInventoryChanged;
                _subscribedInventoryHandler = true;
            }
            CraftingObjectiveTracker.Init();
            CraftingExecutionHandler.Init(_log, _craftingHandler);
            InventoryTracker.Enable();
            TargetCraftingObject = null;
        }

        private static void DisableChef()
        {
            if (!_subscribedInventoryHandler) return;

            InventoryTracker.OnInventoryChangedWithIndices -= OnInventoryChanged;
            _subscribedInventoryHandler = false;

            CraftingExecutionHandler.Abort();
            CraftingObjectiveTracker.Cleanup();
            InventoryTracker.Disable();

            _planner = null;
            _lastCraftables.Clear();
            _lastCraftables.TrimExcess();

            CraftUI.Detach();
        }

        internal static void OnChefUiOpened(CraftingController controller)
        {
            if (!IsChefStage()) return;

            ActiveCraftingController = controller;
            TargetCraftingObject = controller.gameObject;

            if (IsAutoCrafting) return;

            CraftUI.Attach(ActiveCraftingController);
        }

        internal static void OnChefUiClosed(CraftingController controller)
        {
            if (ActiveCraftingController == controller)
            {
                CraftUI.Detach();
                ActiveCraftingController = null;
            }
        }

        //-------------------------------- Craft Handling ----------------------------------
        internal static void RequestCraft(CraftPlanner.RecipeChain chain, int count)
        {
            if (ActiveCraftingController == null || chain == null) return;
            CraftingExecutionHandler.ExecuteChain(chain, count);
        }

        internal static void AbortCraft()
        {
            if (IsAutoCrafting)
            {
                _log.LogInfo("Local craft Abort requested. Signaling teammates...");

                ChatNetworkHandler.SendGlobalAbort();

                CraftingExecutionHandler.Abort();
            }
        }

        private class StateRunner : MonoBehaviour
        {
            private float _abortTimer = 0f;
            private const float ABORT_THRESHOLD = 0.6f;

            private void Update()
            {
                if (StateController.IsAutoCrafting)
                {
                    if (CookBook.AbortKey.Value.IsPressed())
                    {
                        _abortTimer += Time.deltaTime;
                        if (_abortTimer >= ABORT_THRESHOLD)
                        {
                            StateController.AbortCraft();
                            _abortTimer = 0f;
                        }
                    }
                    else
                    {
                        _abortTimer = 0f;
                    }
                }
            }
        }

        //--------------------------------------- Helpers ----------------------------------------
        internal static bool CheckForChefPresence()
        {
            if (UnityEngine.Object.FindObjectOfType<CraftingController>() != null)
                return true;

            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.Contains("CraftingController") || go.name.Contains("ChefStation"))
                {
                    return true;
                }
            }

            return false;
        }
        internal static void ForceRebuild()
        {
            if (_planner != null && IsChefStage()) _planner.ComputeCraftable(InventoryTracker.GetUnifiedStacksCopy(), null, false);
        }

        /// <summary>
        /// Set the planner for a given StateController.
        /// </summary>
        internal static void SetPlanner(CraftPlanner planner)
        {
            if (_planner != null) _planner.OnCraftablesUpdated -= OnCraftablesUpdated;
            _planner = planner;
            if (_planner != null) _planner.OnCraftablesUpdated += OnCraftablesUpdated;
            if (IsChefStage())
            {
                if (!_subscribedInventoryHandler)
                {
                    InventoryTracker.OnInventoryChangedWithIndices += OnInventoryChanged;
                    _subscribedInventoryHandler = true;
                }
                TakeSnapshot(InventoryTracker.GetUnifiedStacksCopy());
            }
        }

        private static void QueueThrottledCompute(int[] unifiedStacks, HashSet<int> changedIndices)
        {
            if (_throttleRoutine != null) _craftingHandler.StopCoroutine(_throttleRoutine);
            _throttleRoutine = _craftingHandler.StartCoroutine(ThrottledComputeRoutine(unifiedStacks, changedIndices));
        }

        //--------------------------------------- Coroutines ---------------------------------------------
        private static IEnumerator ThrottledComputeRoutine(int[] unifiedStacks, HashSet<int> changedIndices)
        {
            yield return new WaitForSecondsRealtime(CookBook.ComputeThrottleMs.Value / 1000f);
            if (_planner == null) yield break;

            if (ItemCatalog.itemCount != _planner.SourceItemCount)
            {
                RecipeProvider.Rebuild();

                SetPlanner(new CraftPlanner(RecipeProvider.Recipes, CookBook.MaxDepth.Value, _log));
            }

            _planner.ComputeCraftable(unifiedStacks, changedIndices, false);
            _throttleRoutine = null;
        }

        internal static bool IsChefStage(SceneDef sceneDef) => sceneDef && sceneDef.baseSceneName == "computationalexchange";
        internal static bool IsChefStage() => IsChefStage(Stage.instance ? Stage.instance.sceneDef : null);
        internal static void TakeSnapshot(int[] unifiedStacks) => _planner?.ComputeCraftable(unifiedStacks);
        internal static SceneDef GetCurrentScene() => Stage.instance ? Stage.instance.sceneDef : null;
    }
}
