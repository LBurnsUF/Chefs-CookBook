using BepInEx.Logging;
using RoR2;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class CraftingExecutionHandler
    {
        private static ManualLogSource _log;

        private static ObjectiveTracker.ObjectiveToken _currentObjective;
        private static Coroutine _craftingRoutine;
        private static MonoBehaviour _runner;

        public static bool IsAutoCrafting => _craftingRoutine != null;

        internal static void Init(ManualLogSource log, MonoBehaviour runner)
        {
            _log = log;
            _runner = runner;
        }

        public static void ExecuteChain(CraftPlanner.RecipeChain chain, int repeatCount)
        {
            if (CookBook.isDebugMode)
            {
                DumpChain(chain, repeatCount);
            }
            Abort();
            _craftingRoutine = _runner.StartCoroutine(CraftChainRoutine(chain, repeatCount));
        }

        public static void Abort()
        {
            if (_runner != null && _craftingRoutine != null)
            {
                _runner.StopCoroutine(_craftingRoutine);
            }

            _craftingRoutine = null;
            StateController.BatchMode = false;

            if (_currentObjective != null)
            {
                _currentObjective.Complete();
                _currentObjective = null;
            }

            if (StateController.ActiveCraftingController)
                CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);
        }

        private static IEnumerator CraftChainRoutine(CraftPlanner.RecipeChain chain, int repeatCount)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body) { Abort(); yield break; }
            var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

            Dictionary<int, int> tierMaxNeeded = new Dictionary<int, int>();
            if (chain.DroneCostSparse != null)
            {
                foreach (var req in chain.DroneCostSparse)
                {
                    if (!tierMaxNeeded.ContainsKey(req.ScrapIndex)) tierMaxNeeded[req.ScrapIndex] = 0;
                    tierMaxNeeded[req.ScrapIndex] += req.Count;
                }
            }

            Dictionary<int, int> startCounts = new Dictionary<int, int>();
            Dictionary<int, int> currentProgress = new Dictionary<int, int>();


            if (chain.DroneCostSparse != null && chain.DroneCostSparse.Length > 0)
            {
                localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

                foreach (var req in chain.DroneCostSparse)
                {
                    if (!startCounts.ContainsKey(req.ScrapIndex))
                    {
                        var pi = GetPickupIndexFromUnified(req.ScrapIndex);
                        startCounts[req.ScrapIndex] = GetOwnedCount(PickupCatalog.GetPickupDef(pi), body);
                        currentProgress[req.ScrapIndex] = 0;
                    }

                    currentProgress[req.ScrapIndex] += req.Count;
                    int inventoryGoal = startCounts[req.ScrapIndex] + currentProgress[req.ScrapIndex];
                    string droneName = GetDroneName(req.DroneIdx);

                    if (req.Owner == null || req.Owner == localUser)
                    {
                        yield return HandleAcquisition(
                            GetPickupIndexFromUnified(req.ScrapIndex),
                            inventoryGoal,
                            $"Scrap {droneName} for"
                        );
                    }
                    else
                    {
                        ChatNetworkHandler.SendObjectiveRequest(req.Owner, "SCRAP", req.ScrapIndex, req.Count);
                        string teammateName = req.Owner?.userName ?? "Teammate";

                        yield return HandleAcquisition(
                            GetPickupIndexFromUnified(req.ScrapIndex),
                            inventoryGoal,
                            $"Wait for {teammateName} to scrap {droneName} for"
                        );

                        ChatNetworkHandler.SendObjectiveSuccess(req.Owner, req.ScrapIndex);
                    }
                }
            }

            if (chain.AlliedTradeSparse != null && chain.AlliedTradeSparse.Length > 0)
            {
                foreach (var req in chain.AlliedTradeSparse)
                {
                    PickupIndex pi = GetPickupIndexFromUnified(req.UnifiedIndex);

                    int currentOwned = GetOwnedCount(PickupCatalog.GetPickupDef(pi), body);
                    int tradeGoal = currentOwned + req.Count;

                    ChatNetworkHandler.SendObjectiveRequest(req.Donor, "TRADE", req.UnifiedIndex, req.Count);

                    string donorName = req.Donor?.userName ?? "Ally";

                    yield return HandleAcquisition(pi, tradeGoal, $"Wait for {donorName} to trade");

                    ChatNetworkHandler.SendObjectiveSuccess(req.Donor, req.UnifiedIndex);
                }
            }

            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>();
            var singleChainSteps = chain.Steps.Where(s => !(s is TradeRecipe)).ToList();

            for (int i = 0; i < repeatCount; i++)
                foreach (var step in singleChainSteps) craftQueue.Enqueue(step);

            PickupIndex lastPickup = PickupIndex.none;
            int lastQty = 0;
            int totalSteps = craftQueue.Count;
            int completedSteps = 0;

            while (craftQueue.Count > 0)
            {
                if (CookBook.AbortKey.Value.IsPressed()) { Abort(); yield break; }

                if (lastPickup != PickupIndex.none)
                {
                    var def = PickupCatalog.GetPickupDef(lastPickup);
                    SetObjectiveText($"Collect {Language.GetString(def?.nameToken ?? "Result")}");
                    yield return WaitForPendingPickup(lastPickup, lastQty);
                    CompleteCurrentObjective();
                    lastPickup = PickupIndex.none;
                }

                ChefRecipe step = craftQueue.Peek();

                body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                if (body)
                {
                    foreach (var ing in step.Ingredients)
                    {
                        if (!ing.IsItem)
                        {
                            SetObjectiveText($"Preparing {GetStepName(step)}...");
                            yield return EnsureEquipmentIsActive(body, ing.EquipIndex);
                        }
                    }
                }

                while (StateController.ActiveCraftingController == null)
                {
                    body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                    var interactor = body?.GetComponent<Interactor>();

                    if (!StateController.TargetCraftingObject || !body || !interactor)
                    {
                        _log.LogWarning("Lost target or body during assembly. Aborting.");
                        Abort();
                        yield break;
                    }

                    SetObjectiveText("Approach Wandering CHEF");
                    float maxDist = interactor.maxInteractionDistance + 6f;
                    float distSqr = (body.corePosition - StateController.TargetCraftingObject.transform.position).sqrMagnitude;

                    if (distSqr <= (maxDist * maxDist))
                    {
                        interactor.AttemptInteraction(StateController.TargetCraftingObject);
                    }
                    yield return new WaitForSeconds(0.2f);
                }

                if (StateController.ActiveCraftingController != null)
                {
                    var controller = StateController.ActiveCraftingController;

                    string stepName = GetStepName(step);
                    SetObjectiveText($"Processing {stepName}...");

                    StateController.BatchMode = true;
                    controller.ClearAllSlots();

                    bool submitAttempted = SubmitIngredients(controller, step);

                    if (submitAttempted)
                    {
                        float syncTimeout = 2.0f;
                        while (controller != null && !controller.AllSlotsFilled() && syncTimeout > 0)
                        {
                            syncTimeout -= Time.deltaTime;
                            yield return null;
                        }

                        if (controller != null && controller.AllSlotsFilled())
                        {
                            _log.LogInfo($"[Execution] {stepName} verified and server-synced.");

                            craftQueue.Dequeue();
                            completedSteps++;

                            lastQty = step.ResultCount;
                            lastPickup = GetPickupIndex(step);

                            controller.ConfirmSelection();
                            yield return new WaitForEndOfFrame();
                            CraftUI.CloseCraftPanel(controller);

                            StateController.BatchMode = false;
                            StateController.ForceRebuild();

                            yield return new WaitForSeconds(0.2f);
                            continue;
                        }
                    }

                    _log.LogWarning($"[Execution] {stepName} failed to sync (Server/Client mismatch). Retrying...");
                    StateController.BatchMode = false;

                    yield return new WaitForSeconds(0.2f);
                }
            }

            if (lastPickup != PickupIndex.none) yield return WaitForPendingPickup(lastPickup, lastQty);
            _log.LogInfo($"[ExecutionHandler] Finished {completedSteps}/{totalSteps} steps.");
            Abort();
        }

        private static IEnumerator HandleAcquisition(PickupIndex pi, int inventoryGoal, string actionPrefix)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var def = PickupCatalog.GetPickupDef(pi);
            if (!body || def == null) yield break;

            string itemName = Language.GetString(def.nameToken);

            while (true)
            {
                int current = GetOwnedCount(def, body);
                int remaining = inventoryGoal - current;

                if (remaining <= 0) break;

                SetObjectiveText($"{actionPrefix} {itemName} <style=cSub>(Need {remaining})</style>"); //

                yield return new WaitForSeconds(0.1f);
            }

            CompleteCurrentObjective();
        }

        private static IEnumerator WaitForPendingPickup(PickupIndex pickupIndex, int expectedGain)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            if (!body || !body.inventory) yield break;

            var def = PickupCatalog.GetPickupDef(pickupIndex);
            if (def == null) yield break;

            int targetCount = GetOwnedCount(def, body) + expectedGain;

            while (true)
            {
                if (GetOwnedCount(def, body) >= targetCount)
                {
                    _log.LogDebug($"[Chain] Confirmed pickup of {def.internalName}.");
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        private static bool SubmitIngredients(CraftingController controller, ChefRecipe recipe)
        {
            var options = controller.options;
            if (options == null || options.Length == 0)
            {
                return false;
            }

            foreach (var ing in recipe.Ingredients)
            {
                PickupIndex target = ing.IsItem
                    ? PickupCatalog.FindPickupIndex(ing.ItemIndex)
                    : PickupCatalog.FindPickupIndex(ing.EquipIndex);

                if (target == PickupIndex.none) continue;

                int choiceIndex = -1;
                for (int j = 0; j < options.Length; j++)
                {
                    if (options[j].pickup.pickupIndex == target)
                    {
                        choiceIndex = j;
                        break;
                    }
                }

                if (choiceIndex == -1) return false;

                for (int i = 0; i < ing.Count; i++)
                {
                    controller.SubmitChoice(choiceIndex);
                }
            }

            return true;
        }

        private static IEnumerator EnsureEquipmentIsActive(CharacterBody body, EquipmentIndex target)
        {
            var inv = body.inventory;
            if (!inv || target == EquipmentIndex.None) yield break;

            int currentSlot = inv.activeEquipmentSlot;
            int slotCount = inv.GetEquipmentSlotCount();

            int targetSlot = -1;
            int targetSet = -1;
            for (int s = 0; s < slotCount; s++)
            {
                int setCount = inv.GetEquipmentSetCount((uint)s);
                for (int set = 0; set < setCount; set++)
                {
                    if (inv.GetEquipment((uint)s, (uint)set).equipmentIndex == target)
                    {
                        targetSlot = s;
                        targetSet = set;
                        break;
                    }
                }
                if (targetSlot != -1) break;
            }

            if (targetSlot == -1) yield break;

            if (targetSlot != currentSlot)
            {
                var skill = body.skillLocator?.special;
                if (skill)
                {
                    if (StateController.ActiveCraftingController)
                        CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);

                    while (!skill.CanExecute())
                    {
                        yield return new WaitForSeconds(0.1f);
                    }

                    _log.LogInfo($"[Execution] Retooling to Slot {targetSlot}.");
                    skill.ExecuteIfReady();

                    float timeout = 1.0f;
                    while (inv.activeEquipmentSlot != targetSlot && timeout > 0)
                    {
                        timeout -= 0.1f;
                        yield return new WaitForSeconds(0.1f);
                    }
                }
            }

            int currentSet = inv.activeEquipmentSet[inv.activeEquipmentSlot];
            int totalSets = inv.GetEquipmentSetCount((uint)inv.activeEquipmentSlot);

            if (targetSet != currentSet)
            {
                if (StateController.ActiveCraftingController)
                    CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);

                int clicks = (targetSet - currentSet + totalSets) % totalSets;
                _log.LogInfo($"[Execution] Cycling sets ({clicks} clicks) for {target}.");
                for (int i = 0; i < clicks; i++) inv.CallCmdSwitchToNextEquipmentInSet();

                float timeout = 2.0f;
                while (inv.activeEquipmentSet[inv.activeEquipmentSlot] != targetSet && timeout > 0)
                {
                    timeout -= 0.1f;
                    yield return new WaitForSeconds(0.1f);
                }
            }
        }

        private static int GetOwnedCount(PickupDef def, CharacterBody body)
        {
            if (def.itemIndex != ItemIndex.None) return body.inventory.GetItemCountPermanent(def.itemIndex);
            if (def.equipmentIndex != EquipmentIndex.None)
            {
                var inv = body.inventory;
                int slotCount = inv.GetEquipmentSlotCount();
                for (int slot = 0; slot < slotCount; slot++)
                {
                    int setCount = inv.GetEquipmentSetCount((uint)slot);
                    for (int set = 0; set < setCount; set++)
                    {
                        if (inv.GetEquipment((uint)slot, (uint)set).equipmentIndex == def.equipmentIndex)
                        {
                            return 1;
                        }
                    }
                }
            }
            return 0;
        }

        private static string GetStepName(ChefRecipe step)
        {
            string baseName = (step.ResultIndex < ItemCatalog.itemCount)
                ? Language.GetString(ItemCatalog.GetItemDef((ItemIndex)step.ResultIndex)?.nameToken)
                : Language.GetString(EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount))?.nameToken);
            if (step is TradeRecipe) return $"<style=cIsUtility>Trade: {baseName}</style>";
            return baseName;
        }

        private static PickupIndex GetPickupIndex(ChefRecipe step)
        {
            if (step.ResultIndex < ItemCatalog.itemCount) return PickupCatalog.FindPickupIndex((ItemIndex)step.ResultIndex);
            return PickupCatalog.FindPickupIndex((EquipmentIndex)(step.ResultIndex - ItemCatalog.itemCount));
        }

        private static string GetDroneName(DroneIndex droneIdx)
        {
            if (droneIdx != DroneIndex.None)
            {
                var prefab = DroneCatalog.GetDroneDef(droneIdx)?.bodyPrefab;
                if (prefab && prefab.GetComponent<CharacterBody>() is CharacterBody b)
                {
                    return Language.GetString(b.baseNameToken);
                }
            }
            return "Drone";
        }

        private static PickupIndex GetPickupIndexFromUnified(int unifiedIndex)
        {
            if (unifiedIndex < ItemCatalog.itemCount)
                return PickupCatalog.FindPickupIndex((ItemIndex)unifiedIndex);
            return PickupCatalog.FindPickupIndex((EquipmentIndex)(unifiedIndex - ItemCatalog.itemCount));
        }

        private static void SetObjectiveText(string text)
        {
            if (_currentObjective == null)
            {
                _currentObjective = ObjectiveTracker.CreateObjective(text);
            }
            else
            {
                _currentObjective.UpdateText(text);
            }
        }

        private static void CompleteCurrentObjective()
        {
            if (_currentObjective != null)
            {
                _currentObjective.Complete();
                _currentObjective = null;
            }
        }

        private static void DumpChain(CraftPlanner.RecipeChain chain, int repeatCount)
        {
            _log.LogInfo("┌──────────────────────────────────────────────────────────┐");
            _log.LogInfo($"│ CHAIN EXECUTION: {GetItemName(chain.ResultIndex)} (x{repeatCount})");
            _log.LogInfo("├──────────────────────────────────────────────────────────┘");

            if (chain.DroneCostSparse.Length > 0)
            {
                _log.LogInfo("│ [Resources] Drones Needed:");
                foreach (var drone in chain.DroneCostSparse)
                    _log.LogInfo($"│   - {GetDroneName(drone.DroneIdx)} -> 1x {GetItemName(drone.ScrapIndex)}");
            }

            if (chain.AlliedTradeSparse.Length > 0)
            {
                _log.LogInfo("│ [Resources] Allied Trades:");
                foreach (var trade in chain.AlliedTradeSparse)
                    _log.LogInfo($"│   - {trade.Donor?.userName ?? "Ally"}: {trade.Count}x {GetItemName(trade.UnifiedIndex)}");
            }

            _log.LogInfo("│ [Workflow] Sequence:");
            var singleChainSteps = chain.Steps.Where(s => !(s is TradeRecipe)).ToList();
            for (int i = 0; i < singleChainSteps.Count; i++)
            {
                var step = singleChainSteps[i];
                string ingredients = string.Join(", ", step.Ingredients.Select(ing => $"{ing.Count}x {GetItemName(ing.UnifiedIndex)}"));
                _log.LogInfo($"│   Step {i + 1}: [{ingredients}] —> {step.ResultCount}x {GetItemName(step.ResultIndex)}");
            }
            _log.LogInfo("└──────────────────────────────────────────────────────────");
        }

        // Helper for the dump
        private static string GetItemName(int unifiedIndex)
        {
            if (unifiedIndex < ItemCatalog.itemCount)
                return Language.GetString(ItemCatalog.GetItemDef((ItemIndex)unifiedIndex)?.nameToken ?? "Unknown Item");
            return Language.GetString(EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(unifiedIndex - ItemCatalog.itemCount))?.nameToken ?? "Unknown Equip");
        }
    }
}