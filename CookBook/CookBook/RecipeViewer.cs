using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using static CookBook.CraftPlanner;

namespace CookBook
{
    // recipe lists for the TAB scoreboard, any player, any stage
    internal static class RecipeViewer
    {
        private static ManualLogSource _log;
        private static CraftPlanner _planner;
        private static List<CraftableEntry> _craftables;

        internal static void Init(ManualLogSource log)
        {
            _log = log;
        }

        // craftable, then partial, then none
        internal static List<CraftableEntry> Compute(CharacterMaster master, out InventorySnapshot snap)
        {
            snap = default;
            var inv = master ? master.inventory : null;
            if (!inv) return new List<CraftableEntry>();

            // your CHEF stage list already has drones and trades
            if (master == LocalUserManager.GetFirstLocalUser()?.cachedMaster && StateController.IsChefStage())
            {
                snap = InventoryTracker.GetSnapshot();
                return AddProgress(StateController.NewestCraftables, snap);
            }

            snap = InventoryTracker.BuildSnapshotFor(inv);
            return AddProgress(ComputeCraftables(snap), snap);
        }

        private static IReadOnlyList<CraftableEntry> ComputeCraftables(InventorySnapshot snap)
        {
            if (_planner == null || _planner.SourceItemCount != ItemCatalog.itemCount)
            {
                _planner = new CraftPlanner(CookBook.DepthLimit, _log) { AnyStage = true };
                _planner.OnCraftablesUpdated += (list, _) => _craftables = list;
            }
            _planner.SetMaxDepth(CookBook.DepthLimit);

            _craftables = null;
            _planner.ComputeCraftable(snap, forceUpdate: true);
            return _craftables ?? new List<CraftableEntry>();
        }

        private static List<CraftableEntry> AddProgress(IReadOnlyList<CraftableEntry> craftables, InventorySnapshot snap)
        {
            var stacks = snap.PhysicalStacks;

            // visual overrides can rename results, chains keep the real one
            var progressed = new HashSet<int>();
            foreach (var entry in craftables)
            {
                progressed.Add(entry.Chains.Count > 0 ? entry.Chains[0].ResultIndex : entry.ResultIndex);
            }

            // best direct recipe per result, highest share owned
            var best = new Dictionary<int, CraftableEntry>();
            foreach (var recipe in snap.FilteredRecipes)
            {
                if (progressed.Contains(recipe.ResultIndex)) continue;

                int owned = GetOwned(stacks, recipe);
                int needed = recipe.CountA + recipe.CountB;

                if (best.TryGetValue(recipe.ResultIndex, out var entry))
                {
                    if (owned * entry.Needed <= entry.Owned * needed) continue;
                }
                else
                {
                    entry = new CraftableEntry { ResultIndex = recipe.ResultIndex };
                    best[recipe.ResultIndex] = entry;
                }

                entry.ResultCount = recipe.ResultCount;
                entry.Owned = owned;
                entry.Needed = needed;
                entry.ProgressRecipe = recipe;
                entry.Progress = owned > 0 ? RecipeProgress.Partial : RecipeProgress.None;
            }

            foreach (var entry in best.Values)
            {
                if (entry.Progress == RecipeProgress.Partial) progressed.Add(entry.ResultIndex);
            }

            // an ingredient with progress of its own counts, noisy so opt in
            if (CookBook.ShowMultistepPartials.Value)
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var recipe in snap.FilteredRecipes)
                    {
                        if (progressed.Contains(recipe.ResultIndex)) continue;
                        if (!progressed.Contains(recipe.IngA) && !(recipe.HasB && progressed.Contains(recipe.IngB))) continue;

                        best[recipe.ResultIndex].Progress = RecipeProgress.Partial;
                        progressed.Add(recipe.ResultIndex);
                        changed = true;
                    }
                }
            }

            var rest = new List<CraftableEntry>(best.Values);
            rest.Sort(CompareProgress);

            var result = new List<CraftableEntry>(craftables);
            result.AddRange(rest);
            return result;
        }

        // owned toward the recipe, capped at what it takes
        private static int GetOwned(int[] stacks, ChefRecipe recipe)
        {
            int Have(int idx) => (uint)idx < (uint)stacks.Length ? stacks[idx] : 0;

            if (!recipe.HasB) return Math.Min(Have(recipe.IngA), recipe.CountA);
            if (recipe.IngA == recipe.IngB) return Math.Min(Have(recipe.IngA), recipe.CountA + recipe.CountB);
            return Math.Min(Have(recipe.IngA), recipe.CountA) + Math.Min(Have(recipe.IngB), recipe.CountB);
        }

        // partial before none, closer to done first, then the list's usual order
        private static int CompareProgress(CraftableEntry a, CraftableEntry b)
        {
            if (a.Progress != b.Progress) return a.Progress.CompareTo(b.Progress);

            int share = (b.Owned * a.Needed).CompareTo(a.Owned * b.Needed);
            if (share != 0) return share;

            return TierManager.CompareCraftableEntries(a, b);
        }
    }
}
