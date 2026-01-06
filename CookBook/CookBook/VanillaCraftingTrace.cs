using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;

namespace CookBook
{
    /// <summary>
    /// Diagnostic-only: traces vanilla CraftingPanel / CraftingController index flow.
    /// </summary>
    internal static class VanillaCraftingTrace
    {
        private static ManualLogSource _log;
        private static bool _enabled = true;

        public static void Init(ManualLogSource log)
        {
            _log = log;

            On.RoR2.UI.CraftingPanel.UpdateSlotVisuals += CraftingPanel_UpdateSlotVisuals;
            On.RoR2.UI.CraftingPanel.UpdateAllVisuals += CraftingPanel_UpdateAllVisuals;

            On.RoR2.CraftingController.ClearSlot += CraftingController_ClearSlot;
            On.RoR2.CraftingController.SendToSlot += CraftingController_SendToSlot;
            On.RoR2.CraftingController.HandlePickupSelected += CraftingController_HandlePickupSelected;
            On.RoR2.CraftingController.ConfirmButtonHit += CraftingController_ConfirmButtonHit;
            On.RoR2.CraftingController.ConfirmSelection += CraftingController_ConfirmSelection;
            On.RoR2.CraftingController.FilterAvailableOptions += CraftingController_FilterAvailableOptions;
            On.RoR2.CraftingController.OnIngredientsChanged += CraftingController_OnIngredientsChanged;

            On.RoR2.PickupPickerController.SubmitChoice += PickupPickerController_SubmitChoice;
            On.RoR2.PickupPickerController.SetAvailable += (orig, self, avail) =>
            {
                if (!UnityEngine.Networking.NetworkServer.active)
                {
                    _log.LogWarning($"[Trace] SetAvailable({avail}) on CLIENT. obj={self?.name}");
                    _log.LogWarning(System.Environment.StackTrace);
                }
                orig(self, avail);
            };
            _log.LogInfo("[CraftTrace] VanillaCraftingTrace hooks installed.");
        }

        // -----------------------------
        // UI-layer hooks
        // -----------------------------

        private static void CraftingPanel_UpdateSlotVisuals(On.RoR2.UI.CraftingPanel.orig_UpdateSlotVisuals orig, CraftingPanel self, int slotIndex)
        {
            orig(self, slotIndex);
            if (!_enabled || !self || self.craftingController == null) return;

            _log.LogInfo($"[CraftTrace][UI] CraftingPanel.UpdateSlotVisuals(slotIndex={slotIndex})  " +
                         $"slotPickup={SafePickup(self.craftingController.ingredients, slotIndex)}");
        }

        private static void CraftingPanel_UpdateAllVisuals(On.RoR2.UI.CraftingPanel.orig_UpdateAllVisuals orig, CraftingPanel self)
        {
            orig(self);
            if (!_enabled || !self || self.craftingController == null) return;

            _log.LogInfo($"[CraftTrace][UI] CraftingPanel.UpdateAllVisuals()  controller={self.craftingController.GetInstanceID()} " +
                         $"filled={self.craftingController.AllSlotsFilled()} bestFit={(self.craftingController.bestFitRecipe != null ? self.craftingController.bestFitRecipe.result.ToString() : "null")}");
        }

        // -----------------------------
        // Picker-level hook
        // -----------------------------

        private static void PickupPickerController_SubmitChoice(On.RoR2.PickupPickerController.orig_SubmitChoice orig, PickupPickerController self, int choiceIndex)
        {
            if (_enabled && self != null)
            {
                var optInfo = DescribePickerOption(self, choiceIndex);
                _log.LogInfo($"[CraftTrace][Picker] PickupPickerController.SubmitChoice(choiceIndex={choiceIndex}) {optInfo}");
            }

            orig(self, choiceIndex);
        }

        private static string DescribePickerOption(PickupPickerController picker, int choiceIndex)
        {
            try
            {
                if (picker == null || picker.options == null) return "(no options)";

                ref var opt = ref picker.options[choiceIndex];
                var pickup = opt.pickup.pickupIndex;
                var def = PickupCatalog.GetPickupDef(pickup);
                string name = def != null ? def.internalName : "null-def";
                return $"-> pickup={pickup} name={name} available={opt.available}";
            }
            catch
            {
                return "(option inspect failed)";
            }
        }

        // -----------------------------
        // Controller-layer hooks
        // -----------------------------

        private static void CraftingController_ClearSlot(On.RoR2.CraftingController.orig_ClearSlot orig, CraftingController self, int index)
        {
            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ClearSlot(slotIndex={index}) BEFORE slotPickup={SafePickup(self.ingredients, index)}");
            }

            orig(self, index);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ClearSlot(slotIndex={index}) AFTER  slotPickup={SafePickup(self.ingredients, index)}");
            }
        }

        private static void CraftingController_SendToSlot(On.RoR2.CraftingController.orig_SendToSlot orig, CraftingController self, int choiceIndex)
        {
            if (_enabled && self != null)
            {
                var optInfo = DescribeCraftingOption(self, choiceIndex);
                _log.LogInfo($"[CraftTrace][Ctrl] SendToSlot(choiceIndex={choiceIndex}) {optInfo}  slots={DumpSlots(self.ingredients)}");
            }

            orig(self, choiceIndex);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] SendToSlot(choiceIndex={choiceIndex}) AFTER slots={DumpSlots(self.ingredients)}");
            }
        }

        private static void CraftingController_HandlePickupSelected(On.RoR2.CraftingController.orig_HandlePickupSelected orig, CraftingController self, int choiceIndex)
        {
            if (_enabled && self != null)
            {
                var optInfo = DescribeCraftingOption(self, choiceIndex);
                _log.LogInfo($"[CraftTrace][Ctrl] HandlePickupSelected(choiceIndex={choiceIndex}) {optInfo}  slots={DumpSlots(self.ingredients)}");
            }

            orig(self, choiceIndex);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] HandlePickupSelected(choiceIndex={choiceIndex}) AFTER slots={DumpSlots(self.ingredients)}");
            }
        }

        private static void CraftingController_ConfirmButtonHit(On.RoR2.CraftingController.orig_ConfirmButtonHit orig, CraftingController self, int index)
        {
            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ConfirmButtonHit(index={index}) filled={self.AllSlotsFilled()} bestFit={(self.bestFitRecipe != null ? self.bestFitRecipe.result.ToString() : "null")} slots={DumpSlots(self.ingredients)}");
            }

            orig(self, index);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ConfirmButtonHit(index={index}) AFTER filled={self.AllSlotsFilled()} bestFit={(self.bestFitRecipe != null ? self.bestFitRecipe.result.ToString() : "null")} slots={DumpSlots(self.ingredients)}");
            }
        }

        private static void CraftingController_ConfirmSelection(On.RoR2.CraftingController.orig_ConfirmSelection orig, CraftingController self)
        {
            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ConfirmSelection() filled={self.AllSlotsFilled()} bestFit={(self.bestFitRecipe != null ? self.bestFitRecipe.result.ToString() : "null")} slots={DumpSlots(self.ingredients)}");
            }

            orig(self);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] ConfirmSelection() AFTER slots={DumpSlots(self.ingredients)}");
            }
        }

        private static void CraftingController_FilterAvailableOptions(On.RoR2.CraftingController.orig_FilterAvailableOptions orig, CraftingController self)
        {
            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] FilterAvailableOptions() options={self.options?.Length ?? 0} slots={DumpSlots(self.ingredients)}");
            }

            orig(self);
        }

        private static void CraftingController_OnIngredientsChanged(On.RoR2.CraftingController.orig_OnIngredientsChanged orig, CraftingController self)
        {
            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] OnIngredientsChanged() slots={DumpSlots(self.ingredients)}");
            }

            orig(self);

            if (_enabled && self != null)
            {
                _log.LogInfo($"[CraftTrace][Ctrl] OnIngredientsChanged() AFTER bestFit={(self.bestFitRecipe != null ? self.bestFitRecipe.result.ToString() : "null")} filled={self.AllSlotsFilled()}");
            }
        }

        private static string DescribeCraftingOption(CraftingController controller, int choiceIndex)
        {
            try
            {
                if (controller == null || controller.options == null) return "(no options)";

                var opt = controller.options[choiceIndex];
                var pickup = opt.pickup.pickupIndex;
                var def = PickupCatalog.GetPickupDef(pickup);
                string name = def != null ? def.internalName : "null-def";
                return $"-> pickup={pickup} name={name} available={opt.available}";
            }
            catch
            {
                return "(option inspect failed)";
            }
        }

        // -----------------------------
        // Helpers
        // -----------------------------

        private static string SafePickup(PickupIndex[] arr, int slotIndex)
        {
            if (arr == null) return "null";
            if ((uint)slotIndex >= (uint)arr.Length) return $"OOB({slotIndex}/{arr.Length})";
            return arr[slotIndex].ToString();
        }

        private static string DumpSlots(PickupIndex[] arr)
        {
            if (arr == null) return "null";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < arr.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                sb.Append(i).Append(':').Append(arr[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
