using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;

namespace CookBook
{
    /// <summary>
    /// Computes all items craftable from a starting inventory using Wandering CHEF recipes, up to some max crafting depth.
    /// </summary>
    internal sealed class CraftPlanner
    {
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _itemCount;
        private readonly int _equipmentCount;
        private readonly int _maxDepth;

        private static ManualLogSource _log;

        // Recipes grouped by result (item/equipment)
        private readonly Dictionary<ResultKey, List<ChefRecipe>> _recipesByResult = new();

        // all unique chains per result, deduped by input signature
        private readonly Dictionary<ResultKey, PlanEntry> _plans = new();

        /// <summary>
        /// Offline computed plans; keyed by desired result
        /// </summary>
        internal IReadOnlyDictionary<ResultKey, PlanEntry> Plans => _plans;

        /// <summary>
        /// Creates a new CraftPlanner given a recipe list and max traversal depth.
        /// </summary>
        public CraftPlanner(IReadOnlyList<ChefRecipe> recipes, int maxDepth, ManualLogSource log)
        {
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _maxDepth = maxDepth;
            _itemCount = ItemCatalog.itemCount;
            _equipmentCount = EquipmentCatalog.equipmentCount;
            _log = log;
            RebuildAllPlans();
        }

        // ------------------------ Public query API ---------------------------
        /// <summary>
        /// Describes one craftable result (item or equipment) and the chains that
        /// are currently affordable from a given inventory snapshot.
        /// Chains are sorted by increasing depth, where MinDepth is simply Chains[0].Depth when Chains.Count &gt; 0
        /// </summary>
        internal sealed class CraftableEntry
        {
            public RecipeResultKind ResultKind;
            public ItemIndex ResultItem;
            public EquipmentIndex ResultEquipment;

            public int MinDepth;
            public List<RecipeChain> Chains = new();
        }

        /// <summary>
        /// useful for externally triggering a dictionary rebuild if maxdepth is changed.
        /// </summary>
        internal void RebuildAllPlans()
        {
            _log.LogInfo($"ChefStateController: RebuildAllPlans() Building Recipe Index.");
            BuildRecipeIndex();
            _log.LogInfo($"ChefStateController: RebuildAllPlans() Finished building Recipe Index.");
            _log.LogInfo($"ChefStateController: RebuildAllPlans() Building Plans.");
            BuildPlans();
            _log.LogInfo($"ChefStateController: RebuildAllPlans() Finished building Plans."); // add debug info for # of plans
        }

        /// <summary>
        /// Given a snapshot of item stacks (indexed by ItemCatalog.itemCount),
        /// compute all craftable results, up to the preconfigured _maxDepth.
        ///  currently only checks item costs.
        /// </summary>
        public List<CraftableEntry> ComputeCraftable(int[] itemStacks, int[] equipmentStacks)
        {
            if (itemStacks == null)
            {
                throw new ArgumentNullException(nameof(itemStacks));
            }
            if (equipmentStacks == null) 
            {
                throw new ArgumentNullException(nameof(equipmentStacks));
            }

            var result = new List<CraftableEntry>();

            foreach (var kvp in _plans)
            {
                var rk = kvp.Key;
                var plan = kvp.Value;

                var affordableChains = new List<RecipeChain>();

                foreach (var chain in plan.Chains)
                {
                    if (CanAffordChain(itemStacks, equipmentStacks, chain))
                    {
                        affordableChains.Add(chain);
                    }
                }

                if (affordableChains.Count == 0)
                {
                    continue;
                }

                // sort by depth, ascending
                affordableChains.Sort((a, b) => a.Depth.CompareTo(b.Depth));

                var entry = new CraftableEntry
                {
                    ResultKind = rk.Kind,
                    ResultItem = rk.Item,
                    ResultEquipment = rk.Equipment,
                    MinDepth = affordableChains[0].Depth,
                    Chains = affordableChains
                };

                result.Add(entry);
            }

            // sort primary item tier, secondary alphanumeric
            result.Sort(TierManager.CompareCraftableEntries);

            // DumpCraftables(result);
            _log.LogDebug($"ChefStateController: ComputeCraftable -> {result.Count} entries from " + $"{itemStacks.Length} items / {equipmentStacks.Length} equipment.");
            return result;
        }

        private static void DumpCraftables(List<CraftableEntry> currentcraftables)
        {
            if (currentcraftables == null)
            {
                _log.LogInfo("=== Craftables = null (nothing to dump) ===");
                return;
            }

            _log.LogInfo("=== DUMPING SORTED CRAFTABLES BEGIN ===");

            foreach (var entry in currentcraftables)
            {
                string name;
                if (entry.ResultKind == RecipeResultKind.Item)
                {
                    var def = ItemCatalog.GetItemDef(entry.ResultItem);
                    name = def ? def.nameToken : entry.ResultItem.ToString();
                }
                else
                {
                    var def = EquipmentCatalog.GetEquipmentDef(entry.ResultEquipment);
                    name = def ? def.nameToken : entry.ResultEquipment.ToString();
                }

                _log.LogInfo($"[{entry.ResultKind}] {name} | MinDepth={entry.MinDepth} | Chains={entry.Chains.Count}");

                for (int i = 0; i < entry.Chains.Count; i++)
                {
                    var chain = entry.Chains[i];
                    _log.LogInfo($"    Chain {i}: depth={chain.Depth}");
                }
            }

            _log.LogInfo("=== DUMPING SORTED CRAFTABLES END ===");
        }


        // ------------------------ Private query Helpers ---------------------------
        /// <summary>
        /// Checks whether the given inventory satisfies the required external item costs for the specified chain.
        /// </summary>
        private bool CanAffordChain(int[] itemStacks, int[] equipmentStacks, RecipeChain chain)
        {
            var itemCost = chain.TotalItemCost;
            for (int i = 0; i < itemCost.Length; i++)
            {
                int need = itemCost[i];
                if (need > 0 && itemStacks[i] < need)
                    return false;
            }

            foreach (var kvp in chain.TotalEquipmentCost)
            {
                var idx = kvp.Key;
                int need = kvp.Value;

                int have = equipmentStacks[(int)idx];
                if (have < need)
                    return false;
            }

            return true;
        }

        // ------------------------ Internal plan types ---------------------------
        /// <summary>
        /// Value type key representing "what result does this recipe produce"
        /// </summary>
        internal readonly struct ResultKey : IEquatable<ResultKey>
        {
            public readonly RecipeResultKind Kind;
            public readonly ItemIndex Item;
            public readonly EquipmentIndex Equipment;

            public ResultKey(RecipeResultKind kind, ItemIndex item, EquipmentIndex equipment)
            {
                Kind = kind;
                Item = item;
                Equipment = equipment;
            }
            public bool Equals(ResultKey other)
            {
                return Kind == other.Kind &&
                       Item == other.Item &&
                       Equipment == other.Equipment;
            }

            public override bool Equals(object obj)
            {
                return obj is ResultKey other && Equals(other);
            }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)Kind;
                    hash = (hash * 397) ^ (int)Item;
                    hash = (hash * 397) ^ (int)Equipment;
                    return hash;
                }
            }
        }

        /// <summary>
        /// Canonical signature for an input combination. 
        /// Used to dedupe chains that require the same base inputs.
        /// </summary>
        private readonly struct InputSignature : IEquatable<InputSignature>
        {
            public readonly (ItemIndex index, int count)[] Items;
            public readonly (EquipmentIndex index, int count)[] Equipment;

            public InputSignature(
                List<(ItemIndex index, int count)> items,
                List<(EquipmentIndex index, int count)> equipment)
            {
                Items = items?.ToArray() ?? Array.Empty<(ItemIndex, int)>();
                Equipment = equipment?.ToArray() ?? Array.Empty<(EquipmentIndex, int)>();
            }

            public bool Equals(InputSignature other)
            {
                if (Items.Length != other.Items.Length ||
                    Equipment.Length != other.Equipment.Length)
                    return false;

                for (int i = 0; i < Items.Length; i++)
                {
                    if (Items[i].index != other.Items[i].index ||
                        Items[i].count != other.Items[i].count)
                        return false;
                }

                for (int i = 0; i < Equipment.Length; i++)
                {
                    if (Equipment[i].index != other.Equipment[i].index ||
                        Equipment[i].count != other.Equipment[i].count)
                        return false;
                }

                return true;
            }
            public override bool Equals(object obj)
            {
                return obj is InputSignature other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;

                    for (int i = 0; i < Items.Length; i++)
                    {
                        hash = hash * 31 + (int)Items[i].index;
                        hash = hash * 31 + Items[i].count;
                    }

                    for (int i = 0; i < Equipment.Length; i++)
                    {
                        hash = hash * 31 + (int)Equipment[i].index;
                        hash = hash * 31 + Equipment[i].count;
                    }

                    return hash;
                }
            }
        }

        /// <summary>
        /// A single recipe chain: ordered list of recipes plus cached external costs.
        /// </summary>
        internal sealed class RecipeChain
        {
            public IReadOnlyList<ChefRecipe> Steps { get; }
            public int Depth => Steps.Count;

            // Indexed by ItemCatalog.itemCount
            public int[] TotalItemCost { get; }
            public Dictionary<EquipmentIndex, int> TotalEquipmentCost { get; }

            public RecipeChain(
                List<ChefRecipe> steps,
                int[] totalItemCost,
                Dictionary<EquipmentIndex, int> totalEquipmentCost)
            {
                Steps = steps.ToArray();
                TotalItemCost = totalItemCost;
                TotalEquipmentCost = totalEquipmentCost;
            }
        }

        /// <summary>
        /// All chains for a given result.
        /// </summary>
        internal sealed class PlanEntry
        {
            public ResultKey Result { get; }
            public List<RecipeChain> Chains { get; } = new();

            public PlanEntry(ResultKey result)
            {
                Result = result;
            }
        }

        // ------------------------ Offline plan building ---------------------------
        private void BuildRecipeIndex()
        {
            _recipesByResult.Clear();

            foreach (var r in _recipes)
            {
                var key = new ResultKey(r.ResultKind, r.ResultItem, r.ResultEquipment);

                if (!_recipesByResult.TryGetValue(key, out var list))
                {
                    list = new List<ChefRecipe>();
                    _recipesByResult[key] = list;
                }

                list.Add(r);
            }
        }

        private void BuildPlans()
        {
            _plans.Clear();

            foreach (var kvp in _recipesByResult)
            {
                var target = kvp.Key;
                var plan = new PlanEntry(target);

                EnumerateChainsForTarget(target, plan);

                if (plan.Chains.Count > 0)
                {
                    _plans[target] = plan;
                }
            }
        }

        // ------------------------ Offline Build ---------------------------
        /// <summary>
        /// Given the current chain, compute its external input signature and,
        /// if it's new for this result, store it as a RecipeChain.
        /// </summary>
        private void TryFinalizeChain(
            List<ChefRecipe> chain,
            PlanEntry plan,
            Dictionary<InputSignature, RecipeChain> bestBySignature)
        {
            if (chain.Count == 0)
            {
                return;
            }

            // Aggregate full chain resources
            var externalItems = new List<(ItemIndex, int)>();
            var externalEquip = new List<(EquipmentIndex, int)>();
            var neededItems = new int[_itemCount];
            var producedItems = new int[_itemCount];
            var neededEquip = new Dictionary<EquipmentIndex, int>();
            var producedEquip = new Dictionary<EquipmentIndex, int>();

            // step through chain
            foreach (var step in chain)
            {
                // ingredients
                foreach (var ingredient in step.Ingredients)
                {
                    if (ingredient.Kind == IngredientKind.Item)
                    {
                        int idx = (int)ingredient.Item;
                        if (idx < 0 || idx >= _itemCount) continue;
                        neededItems[idx] += ingredient.Count;
                    }
                    else
                    {
                        if (!neededEquip.TryGetValue(ingredient.Equipment, out var c)) c = 0;
                        neededEquip[ingredient.Equipment] = c + ingredient.Count;
                    }
                }

                // results
                if (step.ResultKind == RecipeResultKind.Item)
                {
                    int idx = (int)step.ResultItem;
                    if (idx >= 0 && idx < _itemCount)
                    {
                        producedItems[idx] += step.ResultCount;
                    }
                }
                else
                {
                    if (!producedEquip.TryGetValue(step.ResultEquipment, out var c)) c = 0;
                    producedEquip[step.ResultEquipment] = c + step.ResultCount;
                }
            }

            // Compute external item requirements (needed - produced)
            for (int i = 0; i < _itemCount; i++)
            {
                int need = neededItems[i];
                if (need <= 0) continue;

                int made = producedItems[i];
                int ext = need - made;

                if (ext > 0)
                {
                    externalItems.Add(((ItemIndex)i, ext));
                }
            }

            // Compute external equipment requirements (needed - produced)
            foreach (var kvp in neededEquip)
            {
                var idx = kvp.Key;
                int need = kvp.Value;
                producedEquip.TryGetValue(idx, out var made);
                int ext = need - made;
                if (ext > 0)
                {
                    externalEquip.Add((idx, ext));
                }
            }

            // If this chain requires no external resources, it's not useful for the player
            if (externalItems.Count == 0 && externalEquip.Count == 0)
            {
                return;
            }

            // Sort by canonical signature (dedupe)
            externalItems.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            externalEquip.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            var sig = new InputSignature(externalItems, externalEquip);

            // Dense item cost array for cheap lookups
            var totalItemCost = new int[_itemCount];
            foreach (var (idx, count) in externalItems)
            {
                int i = (int)idx;
                if (i >= 0 && i < _itemCount)
                {
                    totalItemCost[i] = count;
                }
            }

            var eqCost = new Dictionary<EquipmentIndex, int>();
            foreach (var (idx, count) in externalEquip)
            {
                eqCost[idx] = count;
            }

            var newChain = new RecipeChain(chain, totalItemCost, eqCost);

            // keep only the shallowest chain per signature
            if (bestBySignature.TryGetValue(sig, out var existing))
            {
                if (newChain.Depth < existing.Depth)
                {
                    bestBySignature[sig] = newChain;
                }
            }
            else
            {
                bestBySignature[sig] = newChain;
            }
        }

        private void DFS(
            ResultKey current,
            int depth,
            List<ChefRecipe> chain,
            HashSet<ResultKey> stack,
            PlanEntry plan,
            Dictionary<InputSignature, RecipeChain> bestBySignature)
        {
            if (depth >= _maxDepth)
            {
                return;
            }

            if (!_recipesByResult.TryGetValue(current, out var options))
            {
                return;
            }

            foreach (var recipe in options)
            {
                chain.Add(recipe);

                TryFinalizeChain(chain, plan, bestBySignature);

                // explore crafting ingredients for multi-step chains 
                foreach (var ingredient in recipe.Ingredients)
                {
                    ResultKey subKey;

                    if (ingredient.Kind == IngredientKind.Item)
                    {
                        subKey = new ResultKey(RecipeResultKind.Item, ingredient.Item, EquipmentIndex.None);
                    }
                    else // Equipment ingredient
                    {
                        subKey = new ResultKey(RecipeResultKind.Equipment, ItemIndex.None, ingredient.Equipment);
                    }

                    if (!_recipesByResult.ContainsKey(subKey))
                    {
                        continue; // this ingredient is base-only in this chain
                    }

                    if (!stack.Add(subKey))
                        continue; // cycle protection

                    DFS(subKey, depth + 1, chain, stack, plan, bestBySignature);
                    stack.Remove(subKey);
                }
                chain.RemoveAt(chain.Count - 1);
            }
        }

        private void EnumerateChainsForTarget(ResultKey target, PlanEntry plan)
        {
            var currentChain = new List<ChefRecipe>();
            var stack = new HashSet<ResultKey> { target };
            // sig -> shortest chain
            var bestBySignature = new Dictionary<InputSignature, RecipeChain>();

            DFS(target, depth: 0, currentChain, stack, plan, bestBySignature);

            // fill plan.Chains
            plan.Chains.Clear();
            plan.Chains.AddRange(bestBySignature.Values);

            string targetName =
            target.Kind == RecipeResultKind.Item
                ? (ItemCatalog.GetItemDef(target.Item)?.nameToken ?? target.Item.ToString())
                : (EquipmentCatalog.GetEquipmentDef(target.Equipment)?.nameToken ?? target.Equipment.ToString());

            _log.LogDebug($"[Planner] Target {targetName} ({target.Kind}): {plan.Chains.Count} deduped chains.");
        }
    }
}
