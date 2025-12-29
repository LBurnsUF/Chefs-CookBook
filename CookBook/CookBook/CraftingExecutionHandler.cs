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

        public static void ExecuteChain(CraftPlanner.RecipeChain chain)
        {
            Abort();
            _craftingRoutine = _runner.StartCoroutine(CraftChainRoutine(chain));
        }

        public static void Abort()
        {
            if (_craftingRoutine != null) _runner.StopCoroutine(_craftingRoutine);
            _craftingRoutine = null;

            if (_currentObjective != null)
            {
                _currentObjective.Complete();
                _currentObjective = null;
            }

            if (StateController.ActiveCraftingController)
                CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);

            _log.LogInfo("[ExecutionHandler] Local craft aborted.");
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

        private static IEnumerator CraftChainRoutine(CraftPlanner.RecipeChain chain)
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

            Queue<ChefRecipe> craftQueue = new Queue<ChefRecipe>(chain.Steps.Where(s => !(s is TradeRecipe)));
            PickupIndex lastPickup = PickupIndex.none;
            int lastQty = 0;

            while (craftQueue.Count > 0)
            {
                body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                var interactor = body?.GetComponent<Interactor>();

                if (lastPickup != PickupIndex.none)
                {
                    var def = PickupCatalog.GetPickupDef(lastPickup);
                    SetObjectiveText($"Collect {Language.GetString(def?.nameToken ?? "Result")}");
                    yield return WaitForPendingPickup(lastPickup, lastQty);
                    CompleteCurrentObjective();
                    lastPickup = PickupIndex.none;
                }

                SetObjectiveText("Approach Wandering CHEF");

                while (StateController.ActiveCraftingController == null)
                {
                    if (!StateController.TargetCraftingObject || !body)
                    {
                        Abort();
                        yield break;
                    }

                    float maxDist = interactor.maxInteractionDistance + 6f;
                    float distSqr = (body.corePosition - StateController.TargetCraftingObject.transform.position).sqrMagnitude;

                    if (distSqr > (maxDist * maxDist))
                    {
                        yield return new WaitForSeconds(0.2f);
                        continue;
                    }

                    if (interactor) interactor.AttemptInteraction(StateController.TargetCraftingObject);
                    for (int i = 0; i < 5; i++)
                    {
                        if (StateController.ActiveCraftingController != null) break;
                        yield return new WaitForFixedUpdate();
                    }
                    if (StateController.ActiveCraftingController == null) yield return new WaitForSeconds(0.2f);
                }

                if (StateController.ActiveCraftingController == null)
                {
                    _log.LogWarning("Chef UI closed during interaction. Aborting chain.");
                    Abort();
                    yield break;
                }

                float initTimeout = 1.0f;
                while (StateController.ActiveCraftingController &&
                       (StateController.ActiveCraftingController.options == null ||
                        StateController.ActiveCraftingController.options.Length == 0))
                {
                    initTimeout -= Time.fixedDeltaTime;
                    if (initTimeout <= 0f) break;
                    yield return new WaitForFixedUpdate();
                }

                if (StateController.ActiveCraftingController == null)
                {
                    _log.LogWarning("Chef UI closed while waiting for options. Aborting.");
                    Abort();
                    yield break;
                }

                ChefRecipe step = craftQueue.Dequeue();
                string stepName = GetStepName(step);
                SetObjectiveText($"Processing {stepName}...");

                StateController.ActiveCraftingController.ClearAllSlots();
                if (!SubmitIngredients(StateController.ActiveCraftingController, step))
                {
                    _log.LogWarning($"Missing ingredients for {stepName}. Aborting.");
                    Abort();
                    yield break;
                }

                while (!StateController.ActiveCraftingController.AllSlotsFilled())
                {
                    yield return new WaitForFixedUpdate();
                }

                StateController.ActiveCraftingController.ConfirmSelection();
                CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);

                lastQty = step.ResultCount;
                lastPickup = GetPickupIndex(step);

                if (craftQueue.Count > 0) yield return new WaitForSeconds(0.2f);
            }

            _log.LogInfo("[ExecutionHandler] Chain Complete.");
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
            var options = controller.options;
            HashSet<PickupIndex> available = new HashSet<PickupIndex>();

            foreach (var opt in options) if (opt.available) available.Add(opt.pickup.pickupIndex);

            foreach (var ing in recipe.Ingredients)
            {
                PickupIndex target = ing.IsItem
                    ? PickupCatalog.FindPickupIndex(ing.ItemIndex)
                    : PickupCatalog.FindPickupIndex(ing.EquipIndex);

                if (target == PickupIndex.none || !available.Contains(target))
                {
                    _log.LogWarning($"[CookBook] Missing ingredient: {target}");
                    return false;
                }
                controller.SendToSlot(target.value);
            }
            return true;
        }

        private static int GetOwnedCount(PickupDef def, CharacterBody body)
        {
            if (def.itemIndex != ItemIndex.None) return body.inventory.GetItemCountPermanent(def.itemIndex);
            if (def.equipmentIndex != EquipmentIndex.None) return body.inventory.currentEquipmentIndex == def.equipmentIndex ? 1 : 0;
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
    }
}