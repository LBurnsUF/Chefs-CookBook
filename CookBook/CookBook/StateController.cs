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
        internal static ManualLogSource _log;
        private static CraftPlanner _planner;

        private static bool _initialized = false;
        internal static Stage _laststage = null;
        internal static bool _ischefstage = false;

        private static List<CraftPlanner.CraftableEntry> _newestCraftables = new List<CraftPlanner.CraftableEntry>();
        private static GameObject _runnerGO;
        private static StateRunner _craftingHandler;
        private static Coroutine _throttleRoutine;

        private static bool _cleanerChefHaltEnabled;
        private static bool _cleanerChefHooked;
        private static Delegate _cleanerChefHandler;
        private static Type _cleanerChefApiType;
        private static System.Reflection.EventInfo _haltChangedEvent;
        private static System.Reflection.PropertyInfo _haltEnabledProp;

        private static InventorySnapshot _newestSnapshot;
        private static int _computeEpoch = 0;

        internal static CraftingController ActiveCraftingController { get; set; }
        internal static GameObject TargetCraftingObject { get; set; }
        internal static bool IsAutoCrafting => CraftingExecutionHandler.IsAutoCrafting;
        public static bool BatchMode { get; set; } = false;

        // Events
        internal static event Action<IReadOnlyList<CraftPlanner.CraftableEntry>, InventorySnapshot> OnCraftablesForUIChanged;
        internal static event Action OnChefStageEntered;
        internal static event Action OnNonChefStageEntered;

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
            InventoryTracker.OnSnapshotChanged += OnInventoryChanged;

            OnChefStageEntered += EnableChef;
            OnNonChefStageEntered += DisableChef;
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

            InventoryTracker.Shutdown();
            InventoryTracker.OnSnapshotChanged -= OnInventoryChanged;

            ChatNetworkHandler.OnIncomingObjective -= OnNetworkObjectiveReceived;
            ChatNetworkHandler.ShutDown();

            OnChefStageEntered -= EnableChef;
            OnNonChefStageEntered -= DisableChef;

            try
            {
                if (_cleanerChefHooked && _haltChangedEvent != null && _cleanerChefHandler != null)
                {
                    _haltChangedEvent.RemoveEventHandler(null, _cleanerChefHandler);
                }
            }
            catch (Exception e)
            {
                _log.LogWarning($"CookBook: Failed to unhook CleanerChef interop: {e.GetType().Name}: {e.Message}");
            }
            finally
            {
                _cleanerChefHooked = false;
                _cleanerChefHandler = null;
                _cleanerChefApiType = null;
                _haltChangedEvent = null;
                _haltEnabledProp = null;
                _cleanerChefHaltEnabled = false;
            }

            _planner = null;
            _runnerGO = null;
            _initialized = false;
            _newestCraftables.Clear();
        }

        /// <summary>
        /// Called whenever a new Stage instance starts. 
        /// </summary>
        internal static void OnStageStart(Stage stage)
        {
            _ischefstage = CheckForChefPresence();

            if (_ischefstage)
            {
                OnChefStageEntered?.Invoke();
            }
            else
            {
                OnNonChefStageEntered?.Invoke();
            }
        }

        /// <summary>
        /// Lightweight Chef State tracking throughout each run.
        /// </summary>
        private static void OnRunStart(Run run)
        {
            InventoryTracker.Start();
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

            InventoryTracker.Shutdown();
            _newestCraftables.Clear();
        }

        // -------------------- CookBook Handshake Events --------------------
        private static void OnNetworkObjectiveReceived(NetworkUser sender, string command, int unifiedIdx, int quantity)
        {
            ObjectiveTracker.AddAlliedRequest(sender, command, unifiedIdx, quantity);
        }

        internal static void OnRecipesBuilt()
        {
            var planner = new CraftPlanner(CookBook.DepthLimit, _log);
            SetPlanner(planner);
        }

        private static void OnInventoryChanged(InventorySnapshot snapshot, int[] changedIndices, int changedcount, bool forceRebuild)
        {
            if (!_ischefstage) return;
            if (_planner == null) return;

            if (BatchMode)
            {
                return;
            }

            QueueThrottledCompute(snapshot, changedIndices, changedcount, forceRebuild);
        }


        private static void OnCraftablesUpdated(List<CraftPlanner.CraftableEntry> craftables, InventorySnapshot snap)
        {
            _newestCraftables = craftables;
            _newestSnapshot = snap;
            CraftUI.SetSnapshot(snap);
            if (IsChefStage()) OnCraftablesForUIChanged?.Invoke(_newestCraftables, snap);
        }

        internal static void OnTierOrderChanged()
        {
            if (_newestCraftables == null || _newestCraftables.Count == 0 || !IsChefStage()) return;
            _newestCraftables.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesForUIChanged?.Invoke(_newestCraftables, _newestSnapshot);
        }

        internal static void OnMaxDepthChanged(object _, EventArgs __)
        {
            if (_planner == null) return;

            _planner.SetMaxDepth(CookBook.DepthLimit);

            if (IsChefStage())
            {
                QueueThrottledCompute(_newestSnapshot, null, 0, true);
            }
        }

        internal static void OnMaxChainsPerResultChanged(object _, EventArgs __)
        {
            if (_planner == null) return;

            if (IsChefStage())
            {
                QueueThrottledCompute(_newestSnapshot, null, 0, true);
            }
        }

        internal static void OnMaxBridgeItemsPerChainChanged(object _, EventArgs __)
        {
            if (_planner == null) return;

            if (IsChefStage())
            {
                QueueThrottledCompute(_newestSnapshot, null, 0, true);
            }
        }

        internal static void OnShowCorruptedResultsChanged(object sender, EventArgs e)
        {
            StateController.ForceVisualRefresh();
        }

        internal static void OnCleanerChefHaltChanged(bool haltEnabled)
        {
            _cleanerChefHaltEnabled = haltEnabled;

            bool desired = !haltEnabled;

            if (CookBook.PreventCorruptedCrafting != null &&
                CookBook.PreventCorruptedCrafting.Value != desired)
            {
                CookBook.PreventCorruptedCrafting.Value = desired;
                DebugLog.Trace(_log, haltEnabled ? "CookBook: PreventCorruptedCrafting disabled (CleanerChef enabled)." : "CookBook: PreventCorruptedCrafting re-enabled (CleanerChef disabled).");
            }
        }

        internal static void OnPreventCorruptedCraftingChanged(object sender, EventArgs e)
        {
            if (_cleanerChefHaltEnabled && CookBook.PreventCorruptedCrafting.Value)
            {
                CookBook.PreventCorruptedCrafting.Value = false;
                DebugLog.Trace(_log, "CookBook: PreventCorruptedCrafting locked off (CleanerChef enabled).");
            }
        }

        // -------------------- Chef Events --------------------
        private static void EnableChef()
        {
            DebugLog.Trace(_log, "Chef controller enabled.");
            BatchMode = false;

            CraftingExecutionHandler.Init(_log, _craftingHandler);
            InventoryTracker.Resume();
            TargetCraftingObject = null;
        }

        private static void DisableChef()
        {
            BatchMode = false;

            CraftingExecutionHandler.Abort();
            InventoryTracker.Pause();

            _newestCraftables.Clear();
            _newestCraftables.TrimExcess();

            CraftUI.CloseCraftPanel();
        }

        internal static void OnChefUiOpened(CraftingController controller)
        {
            _ischefstage = true;

            ActiveCraftingController = controller;

            var interactable = controller.GetComponentInParent<IInteractable>();
            TargetCraftingObject = (interactable as Component)?.gameObject ?? controller.gameObject;

            if (IsAutoCrafting) return;

            CraftUI.SetSnapshot(_newestSnapshot);
            CraftUI.Attach(ActiveCraftingController);
        }

        internal static void OnChefUiClosed(CraftingController controller)
        {
            DebugLog.Trace(_log, "Crafting UI Closed, detaching CraftUI.");
            if (ActiveCraftingController == controller)
            {
                CraftUI.Detach();
                ActiveCraftingController = null;
            }
        }

        internal static void TryReleasePromptParticipant(CraftingController controller)
        {
            if (!controller) { DebugLog.Trace(_log, "Release: controller null"); return; }
            if (!UnityEngine.Networking.NetworkClient.active) { DebugLog.Trace(_log, "Release: NetworkClient inactive"); return; }

            var prompt = controller.GetComponent<NetworkUIPromptController>();
            if (!prompt) { DebugLog.Trace(_log, "Release: prompt missing"); return; }

            var w = prompt.BeginMessageToServer();
            if (w == null) { DebugLog.Trace(_log, "Release: writer null"); return; }

            w.Write((byte)1);
            prompt.FinishMessageToServer(w);
            DebugLog.Trace(_log, "Release: sent");
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
                DebugLog.Trace(_log, "Local craft Abort requested. Signaling teammates...");

                ChatNetworkHandler.SendGlobalAbort();

                CraftingExecutionHandler.Abort();
            }
        }

        internal class StateRunner : MonoBehaviour
        {
            internal float _abortTimer = 0f;
            internal const float ABORT_THRESHOLD = 0.6f;

            internal void Update()
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
        internal static void InstallCleanerChefInterop(
            Type apiType,
            System.Reflection.EventInfo haltChangedEvent,
            System.Reflection.PropertyInfo haltEnabledProp,
            Delegate handler)
        {
            _cleanerChefApiType = apiType;
            _haltChangedEvent = haltChangedEvent;
            _haltEnabledProp = haltEnabledProp;
            _cleanerChefHandler = handler;
            _cleanerChefHooked = true;
        }

        internal static void ForceVisualRefresh()
        {
            if (!IsChefStage()) return;

            _planner?.RefreshVisualOverridesAndEmit(InventoryTracker.GetSnapshot());
        }

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
            if (_planner == null || !IsChefStage()) return;

            var snapshot = InventoryTracker.GetSnapshot();
            if (snapshot.FilteredRecipes == null) return;

            _planner.ComputeCraftable(
                snapshot,
                forceUpdate: true
            );
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
                TakeSnapshot();
            }
        }

        internal static void QueueThrottledCompute(InventorySnapshot snapshot, int[] changedIndices, int changedCount, bool forceRebuild)
        {
            if (!_craftingHandler || !_ischefstage) return;

            _computeEpoch++;
            if (_throttleRoutine != null) _craftingHandler.StopCoroutine(_throttleRoutine);

            int myEpoch = _computeEpoch;

            DebugLog.Trace(_log, "Compute Routine Queued.");
            _throttleRoutine = _craftingHandler.StartCoroutine(ThrottledComputeRoutine(snapshot, changedIndices, changedCount, myEpoch, forceRebuild));
        }

        //--------------------------------------- Coroutines ---------------------------------------------
        internal static IEnumerator ThrottledComputeRoutine(InventorySnapshot snapshot, int[] changedIndices, int changedCount, int epoch, bool forceRebuild)
        {
            yield return new WaitForSecondsRealtime(CookBook.ComputeThrottleMs.Value / 1000f);
            if (epoch != _computeEpoch)
                yield break;

            if (_planner == null) yield break;

            if (ItemCatalog.itemCount != _planner.SourceItemCount)
            {
                RecipeProvider.Rebuild();
                SetPlanner(new CraftPlanner(CookBook.DepthLimit, _log));
            }

            if (epoch != _computeEpoch)
                yield break;

            _planner.ComputeCraftable(
                snapshot,
                changedIndices: changedIndices,
                changedCount: changedCount,
                forceRebuild
            );

            _throttleRoutine = null;
        }

        internal static bool IsChefStage() => _ischefstage;

        internal static void TakeSnapshot()
        {
            if (_planner == null) return;
            _planner.ComputeCraftable(
               InventoryTracker.GetSnapshot(),
               changedIndices: null,
               forceUpdate: true
           );
        }
    }
}
