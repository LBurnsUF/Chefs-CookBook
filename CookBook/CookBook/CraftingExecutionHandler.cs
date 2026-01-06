using BepInEx.Logging;
using RoR2;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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

        private static void Cleanup(bool closeUi = false)
        {
            _craftingRoutine = null;

            StateController.BatchMode = false;

            CompleteCurrentObjective();

            if (closeUi && StateController.ActiveCraftingController)
                CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);
        }


        public static void Abort()
        {
            if (_runner != null && _craftingRoutine != null)
                _runner.StopCoroutine(_craftingRoutine);

            Cleanup(closeUi: true);
        }

        private static IEnumerator CraftChainRoutine(CraftPlanner.RecipeChain chain, int repeatCount)
        {
            CompleteCurrentObjective();
            StateController.BatchMode = false;

            var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
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

            StateController.BatchMode = true;

            while (craftQueue.Count > 0)
            {
                if (CookBook.AbortKey.Value.IsPressed()) { Abort(); yield break; }

                body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                if (!body) { Abort(); yield break; }

                ChefRecipe step = craftQueue.Peek();
                string stepName = GetStepName(step);

                if (lastPickup != PickupIndex.none)
                {
                    var def = PickupCatalog.GetPickupDef(lastPickup);
                    SetObjectiveText($"Collect {Language.GetString(def?.nameToken ?? "Result")}");
                    yield return WaitForPendingPickup(lastPickup, lastQty);
                    CompleteCurrentObjective();
                    lastPickup = PickupIndex.none;
                }

                if (step.Ingredients != null)
                {
                    foreach (var ing in step.Ingredients)
                    {
                        if (CookBook.AbortKey.Value.IsPressed()) { Abort(); yield break; }

                        if (!ing.IsItem && ing.EquipIndex != EquipmentIndex.None)
                        {
                            SetObjectiveText($"Preparing {stepName}...");
                            yield return EnsureEquipmentIsActive(body, ing.EquipIndex);
                        }
                    }
                }

                if (StateController.ActiveCraftingController == null)
                {
                    yield return EnsureCraftingControllerOpen();
                }

                var controller = StateController.ActiveCraftingController;
                if (!controller)
                {
                    Cleanup(closeUi: true);
                    yield break;
                }

                var desired = ResolveStepIngredients(step);
                yield return SubmitIngredientsClientSafe(controller, desired);

                if (controller != null && controller.AllSlotsFilled() && IngredientsMatchMultiset(controller.ingredients, ResolveStepIngredients(step)))
                {
                    _log.LogInfo($"[Execution] {stepName} verified. Confirming craft...");

                    craftQueue.Dequeue();
                    completedSteps++;

                    lastQty = step.ResultCount;
                    lastPickup = GetPickupIndex(step);

                    var prompt = controller.GetComponent<NetworkUIPromptController>();
                    if (!prompt || prompt.currentParticipantMaster == null)
                    {
                        _log.LogWarning("[Execution] Not confirming: participant master is null.");
                        yield break;
                    }

                    controller.ConfirmSelection();

                    yield return new WaitForEndOfFrame();
                    CraftUI.CloseCraftPanel(controller);
                    yield return new WaitForSeconds(0.2f);

                    continue;
                }

                if (StateController.ActiveCraftingController)
                    CraftUI.CloseCraftPanel(StateController.ActiveCraftingController);
            }

            if (lastPickup != PickupIndex.none)
            {
                StateController.BatchMode = false;

                var def = PickupCatalog.GetPickupDef(lastPickup);
                SetObjectiveText($"Collect {Language.GetString(def?.nameToken ?? "Result")}");
                yield return WaitForPendingPickup(lastPickup, lastQty);
                CompleteCurrentObjective();
            }

            _log.LogInfo($"[ExecutionHandler] Finished {completedSteps}/{totalSteps} steps.");
            Cleanup(closeUi: true);
        }

        private static bool IsCraftingSessionReady(CraftingController c)
        {
            if (!c) return false;

            var prompt = c.GetComponent<NetworkUIPromptController>();
            if (!prompt) return false;

            if (prompt.currentParticipantMaster == null) return false;
            if (!prompt.isDisplaying) return false;

            var panels = UnityEngine.Object.FindObjectsOfType<RoR2.UI.CraftingPanel>();
            for (int i = 0; i < panels.Length; i++)
                if (panels[i] && panels[i].craftingController == c) return true;

            return false;
        }

        private static IEnumerator EnsureCraftingControllerOpen()
        {
            const float pollInterval = 0.2f;
            const float hardTimeout = 12.0f;

            float t = 0f;

            while (!IsCraftingSessionReady(StateController.ActiveCraftingController))
            {
                if (CookBook.AbortKey.Value.IsPressed())
                {
                    Abort();
                    yield break;
                }

                var body = LocalUserManager.GetFirstLocalUser()?.cachedBody;
                var interactor = body ? body.GetComponent<Interactor>() : null;

                if (!StateController.TargetCraftingObject && body)
                    StateController.TargetCraftingObject = TryResolveChefStationObject(body);

                if (!StateController.TargetCraftingObject || !body || !interactor)
                {
                    _log.LogWarning("[Execution] Lost target/body/interactor while trying to open crafting UI. Aborting.");
                    Abort();
                    yield break;
                }

                SetObjectiveText("Approach Wandering CHEF");

                float maxDist = interactor.maxInteractionDistance + 6f;
                float distSqr = (body.corePosition - StateController.TargetCraftingObject.transform.position).sqrMagnitude;

                if (distSqr <= maxDist * maxDist)
                    interactor.AttemptInteraction(StateController.TargetCraftingObject);

                t += Time.unscaledDeltaTime;
                if (t >= hardTimeout)
                {
                    _log.LogWarning("[Execution] Timed out opening crafting UI.");
                    Cleanup(closeUi: true);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(pollInterval);
            }
        }

        private static List<PickupIndex> ResolveStepIngredients(ChefRecipe step)
        {
            var list = new List<PickupIndex>(step.Ingredients.Sum(i => Mathf.Max(0, i.Count)));

            foreach (var ing in step.Ingredients)
            {
                int c = Mathf.Max(0, ing.Count);
                if (c == 0) continue;

                PickupIndex pickup;
                if (ing.IsItem)
                    pickup = PickupCatalog.FindPickupIndex(ing.ItemIndex);
                else
                    pickup = PickupCatalog.FindPickupIndex(ing.EquipIndex);

                for (int k = 0; k < c; k++)
                    list.Add(pickup);
            }

            return list;
        }

        private static bool TryFindChoiceIndex(CraftingController c, PickupIndex desired, out int choiceIndex)
        {
            choiceIndex = -1;
            var opts = c.options;
            if (opts == null) return false;

            for (int i = 0; i < opts.Length; i++)
            {
                if (opts[i].pickup.pickupIndex == desired)
                {
                    choiceIndex = i;
                    return true;
                }
            }
            return false;
        }

        private static bool IsChoiceIndexStillValid(CraftingController c, int idx, PickupIndex desired)
        {
            var opts = c.options;
            if (opts == null) return false;
            if ((uint)idx >= (uint)opts.Length) return false;

            var opt = opts[idx];
            return opt.available && opt.pickup.pickupIndex == desired;
        }

        private static void SubmitChoiceToCraftingController(CraftingController c, int choiceIndex)
        {
            var prompt = c.GetComponent<NetworkUIPromptController>();
            if (!prompt) return;

            var w = prompt.BeginMessageToServer();
            if (w == null) return;

            w.Write((byte)0);
            w.Write(choiceIndex);
            prompt.FinishMessageToServer(w);
        }

        private static IEnumerator SubmitIngredientsClientSafe(CraftingController controller, List<PickupIndex> desired)
        {
            if (!controller || desired == null) yield break;

            var prompt = controller.GetComponent<NetworkUIPromptController>();

            float t = 2.0f;
            while (t > 0f && (!controller || !prompt || prompt.currentParticipantMaster == null))
            {
                t -= Time.unscaledDeltaTime;
                yield return null;
            }
            if (!controller || !prompt || prompt.currentParticipantMaster == null)
            {
                _log.LogError("[Crafting] Participant Master not ready. Station not synced.");
                yield break;
            }

            if (desired.Count != controller.ingredientCount)
            {
                _log.LogWarning($"[Crafting] Ingredient count mismatch. step needs {desired.Count}, controller has {controller.ingredientCount} slots.");
                yield break;
            }

            for (int i = 0; i < controller.ingredientCount; i++)
            {
                if (controller.ingredients != null && i < controller.ingredients.Length && controller.ingredients[i] != PickupIndex.none)
                    controller.ClearSlot(i);
            }

            float clearBudget = 1.0f;
            while (clearBudget > 0f)
            {
                if (!controller) yield break;

                var slots = controller.ingredients;
                if (slots != null)
                {
                    bool anyFilled = false;
                    for (int i = 0; i < slots.Length; i++)
                    {
                        if (slots[i] != PickupIndex.none) { anyFilled = true; break; }
                    }
                    if (!anyFilled) break;
                }

                clearBudget -= Time.unscaledDeltaTime;
                yield return null;
            }

            for (int k = 0; k < desired.Count; k++)
            {
                if (!controller) yield break;

                var want = desired[k];
                bool inserted = false;

                float insertBudget = 2.0f;
                while (!inserted && insertBudget > 0f)
                {
                    if (!controller) yield break;

                    if (controller.AllSlotsFilled() && !IngredientsMatchMultiset(controller.ingredients, desired))
                    {
                        _log.LogWarning("[Crafting] Station became full with wrong ingredients. Aborting insert to avoid accidental confirm.");
                        yield break;
                    }

                    var opts = controller.options;
                    if (opts == null || opts.Length == 0)
                    {
                        insertBudget -= Time.unscaledDeltaTime;
                        yield return null;
                        continue;
                    }

                    if (!TryFindChoiceIndex(controller, want, out int idx) || !IsChoiceIndexStillValid(controller, idx, want))
                    {
                        insertBudget -= Time.unscaledDeltaTime;
                        yield return null;
                        continue;
                    }

                    var opt = controller.options[idx];
                    _log.LogDebug($"[Crafting] Pick want={want} choiceIndex={idx} optPickup={opt.pickup.pickupIndex} available={opt.available}");

                    int filledBefore = CountFilled(controller.ingredients);
                    int wantCountBefore = CountOf(controller.ingredients, want);

                    SubmitChoiceToCraftingController(controller, idx);

                    yield return new WaitForSecondsRealtime(0.10f);

                    float progressBudget = 1.0f;
                    bool progressed = false;
                    while (progressBudget > 0f)
                    {
                        if (!controller) yield break;

                        var slots = controller.ingredients;
                        if (CountFilled(slots) >= filledBefore + 1 && CountOf(slots, want) > wantCountBefore)
                        {
                            progressed = true;
                            break;
                        }

                        progressBudget -= Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (!progressed)
                    {
                        _log.LogDebug($"[Crafting] No fill-progress observed for {want}, retrying...");
                        insertBudget -= 0.25f;
                        continue;
                    }

                    float settleBudget = 0.75f;
                    while (settleBudget > 0f)
                    {
                        if (!controller) yield break;

                        if (IngredientsMatchPrefixMultiset(controller.ingredients, desired, k + 1))
                        {
                            inserted = true;
                            break;
                        }

                        settleBudget -= Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (!inserted)
                    {
                        _log.LogDebug($"[Crafting] Change observed but state did not settle for {want}, retrying...");
                        insertBudget -= 0.25f;
                    }
                }

                if (!inserted)
                {
                    _log.LogWarning($"[Crafting] Failed to insert ingredient {k + 1}/{desired.Count}: {want}");
                    yield break;
                }
            }

            float syncBudget = 2.0f;
            while (syncBudget > 0f)
            {
                if (!controller) yield break;

                if (IngredientsMatchMultiset(controller.ingredients, desired))
                    yield break;

                syncBudget -= Time.unscaledDeltaTime;
                yield return null;
            }

            _log.LogWarning("[Crafting] Timed out waiting for ingredient sync.");
        }

        private static bool IngredientsMatchPrefixMultiset(PickupIndex[] current, List<PickupIndex> desired, int insertedCount)
        {
            if (current == null || desired == null) return false;

            var expected = new Dictionary<int, int>();
            for (int i = 0; i < insertedCount; i++)
            {
                int v = desired[i].value;
                expected.TryGetValue(v, out int c);
                expected[v] = c + 1;
            }

            var actual = new Dictionary<int, int>();
            for (int i = 0; i < current.Length; i++)
            {
                var p = current[i];
                if (p == PickupIndex.none) continue;

                int v = p.value;
                actual.TryGetValue(v, out int c);
                actual[v] = c + 1;
            }

            if (actual.Count != expected.Count) return false;
            foreach (var kv in expected)
            {
                if (!actual.TryGetValue(kv.Key, out int c)) return false;
                if (c != kv.Value) return false;
            }
            return true;
        }

        private static bool IngredientsMatchMultiset(PickupIndex[] actualSlots, List<PickupIndex> desired)
        {
            if (actualSlots == null) return false;
            if (actualSlots.Length != desired.Count) return false;

            var a = new Dictionary<PickupIndex, int>();
            var b = new Dictionary<PickupIndex, int>();

            for (int i = 0; i < actualSlots.Length; i++)
            {
                var p = actualSlots[i];
                if (p == PickupIndex.none) return false;
                a[p] = a.TryGetValue(p, out int c) ? c + 1 : 1;
            }

            for (int i = 0; i < desired.Count; i++)
            {
                var p = desired[i];
                b[p] = b.TryGetValue(p, out int c) ? c + 1 : 1;
            }

            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out int bc)) return false;
                if (bc != kv.Value) return false;
            }

            return true;
        }

        private static int HashIngredients(PickupIndex[] arr)
        {
            if (arr == null) return 0;
            unchecked
            {
                int h = 17;
                for (int i = 0; i < arr.Length; i++)
                    h = h * 31 + arr[i].value;
                return h;
            }
        }

        private static int CountFilled(PickupIndex[] slots)
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] != PickupIndex.none) n++;
            return n;
        }

        private static int CountOf(PickupIndex[] slots, PickupIndex p)
        {
            if (slots == null) return 0;
            int n = 0;
            for (int i = 0; i < slots.Length; i++)
                if (slots[i] == p) n++;
            return n;
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

        private static GameObject TryResolveChefStationObject(CharacterBody body)
        {
            if (!body) return null;

            var stations = UnityEngine.Object.FindObjectsOfType<RoR2.MealPrepController>();
            if (stations == null || stations.Length == 0) return null;

            RoR2.MealPrepController best = null;
            float bestDistSqr = float.PositiveInfinity;

            Vector3 p = body.corePosition;
            foreach (var s in stations)
            {
                if (!s) continue;
                float d = (s.transform.position - p).sqrMagnitude;
                if (d < bestDistSqr)
                {
                    bestDistSqr = d;
                    best = s;
                }
            }

            if (!best) return null;

            var interactable = best.GetComponentInChildren<RoR2.IInteractable>() as Component;
            return interactable ? interactable.gameObject : best.gameObject;
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

                SetObjectiveText($"{actionPrefix} {itemName} <style=cSub>(Need {remaining})</style>");

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