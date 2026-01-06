using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    public enum RecipeFilterCategory { All, Damage, Healing, Utility }

    internal static class RecipeFilter
    {
        public static RecipeFilterCategory CurrentCategory = RecipeFilterCategory.All;

        private static readonly Dictionary<RecipeFilterCategory, ItemTag> CategoryToTag = new()
        {
            { RecipeFilterCategory.Damage, ItemTag.Damage },
            { RecipeFilterCategory.Healing, ItemTag.Healing },
            { RecipeFilterCategory.Utility, ItemTag.Utility }
        };

        public static void InterceptIngredientAvailability(On.RoR2.CraftingController.orig_FilterAvailableOptions orig, CraftingController self)
        {
            var prompt = self.GetComponent<NetworkUIPromptController>();
            if (prompt == null || prompt.currentParticipantMaster == null)
            {
                return;
            }

            orig(self);

            if (!StateController.IsChefStage() || CurrentCategory == RecipeFilterCategory.All) return;

            var validIndices = GetValidIngredientsForFilter(CraftUI.LastCraftables);
            if (validIndices == null) return;

            for (int i = 0; i < self.options.Length; i++)
            {
                PickupDef pDef = PickupCatalog.GetPickupDef(self.options[i].pickup.pickupIndex);
                if (pDef == null) continue;

                int unified = (pDef.itemIndex != ItemIndex.None)
                    ? (int)pDef.itemIndex
                    : (ItemCatalog.itemCount + (int)pDef.equipmentIndex);

                if (!validIndices.Contains(unified))
                {
                    self.options[i].available = false;
                    self.options[i].overrideUnavailableBGColor = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                }
            }
        }

        // --- UI Logic Helpers ---
        public static void CycleCategory()
        {
            int next = (int)CurrentCategory + 1;
            if (next > (int)RecipeFilterCategory.Utility) next = 0;
            CurrentCategory = (RecipeFilterCategory)next;
        }

        public static string GetLabel()
        {
            return CurrentCategory switch
            {
                RecipeFilterCategory.Damage => "<size=150%><sprite name=\"icon\"></size> DMG",
                RecipeFilterCategory.Healing => "<size=150%><sprite name=\"icon\"></size> HEAL",
                RecipeFilterCategory.Utility => "<size=150%><sprite name=\"icon\"></size> UTIL",
                _ => "ALL"
            };
        }

        public static void ApplyFiltersToUI(List<CraftUI.RecipeRowUI> rows, string searchTerm)
        {
            if (rows == null) return;

            string term = searchTerm?.Trim().ToLowerInvariant();

            foreach (var row in rows)
            {
                if (row.RowGO == null) continue;

                bool searchMatch = string.IsNullOrEmpty(term) || EntryMatchesSearch(row.Entry, term);
                bool filterMatch = EntryMatchesFilter(row.Entry);

                row.RowGO.SetActive(searchMatch && filterMatch);
            }
        }

        public static bool EntryMatchesFilter(CraftableEntry entry)
        {
            if (CurrentCategory == RecipeFilterCategory.All) return true;

            int displayIdx = entry.ResultIndex;
            if (displayIdx < ItemCatalog.itemCount)
            {
                ItemDef def = ItemCatalog.GetItemDef((ItemIndex)displayIdx);
                return def != null && def.ContainsTag(CategoryToTag[CurrentCategory]);
            }

            return false;
        }

        internal static bool EntryMatchesSearch(CraftableEntry entry, string term)
        {
            if (string.IsNullOrEmpty(term)) return true;
            if (entry == null) return false;

            string name = CraftUI.GetEntryDisplayName(entry);
            if (string.IsNullOrEmpty(name)) return false;
            return name.ToLowerInvariant().Contains(term);
        }

        public static HashSet<int> GetValidIngredientsForFilter(IReadOnlyList<CraftableEntry> allCraftables)
        {
            if (CurrentCategory == RecipeFilterCategory.All || allCraftables == null) return null;

            HashSet<int> validIndices = new HashSet<int>();
            ItemTag targetTag = CategoryToTag[CurrentCategory];

            foreach (var entry in allCraftables)
            {
                // Ensure we check the visual result index for the tag
                if (entry.ResultIndex >= ItemCatalog.itemCount) continue;

                ItemDef def = ItemCatalog.GetItemDef((ItemIndex)entry.ResultIndex);
                if (def != null && def.ContainsTag(targetTag))
                {
                    if (entry.Chains != null && entry.Chains.Count > 0)
                    {
                        var immediateStep = entry.Chains[0].Steps.Last();
                        foreach (var ing in immediateStep.Ingredients)
                        {
                            validIndices.Add(ing.UnifiedIndex);
                        }
                    }
                }
            }
            return validIndices;
        }
    }
}