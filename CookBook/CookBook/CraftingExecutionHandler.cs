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

        private static CraftingObjectiveTracker.ObjectiveToken _currentObjective;
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

            if (chain.DroneCostSparse != null && chain.DroneCostSparse.Length > 0)
            {
                foreach (var req in chain.DroneCostSparse)
                {
                    PickupIndex pi = GetPickupIndexFromUnified(req.UnifiedIndex);
                    DroneCandidate candidate = InventoryTracker.GetScrapCandidate(req.UnifiedIndex);
                    string droneName = GetDroneName(req.UnifiedIndex);

                    bool isLocal = (candidate.Owner == null || candidate.Owner == LocalUserManager.GetFirstLocalUser()?.currentNetworkUser);

                    if (isLocal)
                    {
                        yield return HandleAcquisition(pi, req.Count, $"Scrap your {droneName}");
                    }
                    else
                    {
                        ChatNetworkHandler.SendObjectiveRequest(candidate.Owner, "SCRAP", req.UnifiedIndex, req.Count);

                        string teammateName = candidate.Owner?.userName ?? "Teammate";
                        yield return HandleAcquisition(pi, req.Count, $"Wait for {teammateName} to scrap {droneName}");

                        ChatNetworkHandler.SendObjectiveSuccess(candidate.Owner, req.UnifiedIndex);
                    }
                }
            }

            if (chain.AlliedTradeSparse != null && chain.AlliedTradeSparse.Length > 0)
            {
                foreach (var req in chain.AlliedTradeSparse)
                {
                    PickupIndex pi = GetPickupIndexFromUnified(req.UnifiedIndex);

                    ChatNetworkHandler.SendObjectiveRequest(req.Donor, "TRADE", req.UnifiedIndex, req.Count);

                    string donorName = req.Donor?.userName ?? "Ally";
                    yield return HandleAcquisition(pi, req.Count, $"Wait for {donorName} to trade");

                    ChatNetworkHandler.SendObjectiveSuccess(req.Donor, req.UnifiedIndex);
                }
            }

            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>();
            var singleChainSteps = chain.Steps.Where(s => !(s is TradeRecipe)).ToList();

            for (int i = 0; i < repeatCount; i++)
            {
                foreach (var step in singleChainSteps)
                {
                    craftQueue.Enqueue(step);
                }
            }

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
                    string stepName = GetStepName(step);
                    SetObjectiveText($"Processing {stepName}...");

                    StateController.BatchMode = true;
                    StateController.ActiveCraftingController.ClearAllSlots();

                    bool submitAttempted = SubmitIngredients(StateController.ActiveCraftingController, step);

                    if (submitAttempted)
                    {
                        float syncTimeout = 2.0f;
                        while (StateController.ActiveCraftingController != null &&
                               !StateController.ActiveCraftingController.AllSlotsFilled() &&
                               syncTimeout > 0)
                        {
                            syncTimeout -= Time.deltaTime;
                            yield return null;
                        }

                        var controller = StateController.ActiveCraftingController;
                        if (controller != null && controller.AllSlotsFilled())
                        {
                            _log.LogInfo($"[Execution] {stepName} verified and server-synced.");

                            craftQueue.Dequeue();
                            completedSteps++;

                            lastQty = step.ResultCount;
                            lastPickup = GetPickupIndex(step);

                            controller.ConfirmSelection();
                            CraftUI.CloseCraftPanel(controller);

                            StateController.BatchMode = false;
                            StateController.ForceRebuild();

                            if (craftQueue.Count > 0) yield return new WaitForSeconds(0.1f);
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

        private static IEnumerator HandleAcquisition(PickupIndex pi, int totalNeeded, string actionPrefix)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var def = PickupCatalog.GetPickupDef(pi);
            if (!body || def == null) yield break;

            int current = GetOwnedCount(def, body);
            if (current >= totalNeeded) yield break;

            int remaining = totalNeeded - current;
            string itemName = Language.GetString(def.nameToken);

            SetObjectiveText($"{actionPrefix} {itemName} <style=cSub>(Need {remaining})</style>");

            yield return WaitForPendingPickup(pi, remaining);
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
                    _log.LogInfo($"[Chain] Confirmed pickup of {def.internalName}.");
                    yield break;
                }
                yield return new WaitForSeconds(0.1f);
            }
        }

        private static bool SubmitIngredients(CraftingController controller, ChefRecipe recipe)
        {
            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
            var options = controller.options;

            foreach (var ing in recipe.Ingredients)
            {
                PickupIndex target = ing.IsItem
                    ? PickupCatalog.FindPickupIndex(ing.ItemIndex)
                    : PickupCatalog.FindPickupIndex(ing.EquipIndex);

                if (target == PickupIndex.none) continue;

                int choiceIndex = -1;
                for (int j = 0; j < options.Length; j++)
                {
                    if (options[j].pickup.pickupIndex == target && options[j].available)
                    {
                        choiceIndex = j;
                        break;
                    }
                }

                if (choiceIndex == -1) return false;

                for (int i = 0; i < ing.Count; i++)
                {
                    if (UnityEngine.Networking.NetworkServer.active)
                    {
                        controller.SendToSlot(target.value);
                    }
                    else
                    {
                        var promptController = controller.GetComponent<RoR2.NetworkUIPromptController>();
                        var writer = promptController.BeginMessageToServer();
                        writer.Write((byte)0); // msgSubmit
                        writer.Write(choiceIndex);
                        promptController.FinishMessageToServer(writer);
                    }
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

        private static string GetDroneName(int unifiedIndex)
        {
            DroneCandidate candidate = InventoryTracker.GetScrapCandidate(unifiedIndex);

            if (candidate.DroneIdx != DroneIndex.None)
            {
                var prefab = DroneCatalog.GetDroneDef(candidate.DroneIdx)?.bodyPrefab;
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
                _currentObjective = CraftingObjectiveTracker.CreateObjective(text);
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
    }
}