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
        public int SourceItemCount { get; }
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _itemCount;
        private readonly int _totalDefCount;
        private int _maxDepth;
        private readonly ManualLogSource _log;

        private int[] _bufExternalCost;
        private int[] _bufCurrentState;

        // Lookup tables
        private readonly Dictionary<ResultKey, List<ChefRecipe>> _recipesByResult = new();
        private readonly Dictionary<ResultKey, PlanEntry> _plans = new();

        /// <summary>
        /// Offline computed plans; keyed by desired result.
        /// </summary>
        internal IReadOnlyDictionary<ResultKey, PlanEntry> Plans => _plans;

        /// <summary>
        /// Thrown when the list of craftable chains has been updated, caught by CraftUI.
        /// </summary>
        internal event Action<List<CraftableEntry>> OnCraftablesUpdated;

        /// <summary>
        /// Creates a new CraftPlanner given a recipe list and max traversal depth.
        /// </summary>
        public CraftPlanner(IReadOnlyList<ChefRecipe> recipes, int maxDepth, ManualLogSource log)
        {
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _maxDepth = maxDepth;
            _itemCount = ItemCatalog.itemCount;
            _totalDefCount = _itemCount + EquipmentCatalog.equipmentCount;
            _log = log;
            SourceItemCount = ItemCatalog.itemCount;

            RebuildAllPlans();
        }

        internal void SetMaxDepth(int newDepth)
        {
            if (newDepth < 0)
            {
                newDepth = 0;
            }

            if (newDepth == _maxDepth)
            {
                return;
            }

            _maxDepth = newDepth;
            RebuildAllPlans();
        }

        /// <summary>
        /// useful for externally triggering a dictionary rebuild if maxdepth is changed.
        /// </summary>
        internal void RebuildAllPlans()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            BuildRecipeIndex();
            BuildPlans();

            sw.Stop();

            _log.LogInfo($"CraftPlanner: Built plans for {_plans.Count} results.");
            _log.LogInfo($"CraftPlanner: RebuildAllPlans completed in {sw.ElapsedMilliseconds}ms");
        }

        // ------------------------ LUT Build Logic ---------------------------
        private void BuildRecipeIndex()
        {
            _recipesByResult.Clear();

            foreach (var r in _recipes)
            {
                var key = new ResultKey(r.ResultIndex);

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
            var stack = new HashSet<ResultKey>();

            foreach (var kvp in _recipesByResult)
            {
                var target = kvp.Key;
                var plan = new PlanEntry(target);

                EnumerateChainsForTarget(target, plan, stack);

                if (plan.Chains.Count > 0)
                {
                    _plans[target] = plan;
                }
            }
        }

        private void EnumerateChainsForTarget(ResultKey target, PlanEntry plan, HashSet<ResultKey> stack)
        {
            stack.Clear();
            var currentChain = new List<ChefRecipe>();


            DFS(target, target, 0, currentChain, stack, plan);
        }

        // TODO: modify cycle prevention logic to allow certain types of cyclic crafts to occur, like if the user wants more of a given color of scrap, since the inputs generally require at least 1 of the inputted scrap.
        private void DFS(
           ResultKey current,
           ResultKey rootTarget,
           int depth,
           List<ChefRecipe> chain,
           HashSet<ResultKey> stack,
           PlanEntry plan)
        {
            if (depth >= _maxDepth)
            {
                return;
            }

            if (!_recipesByResult.TryGetValue(current, out var options))
            {
                return;
            }


            bool isRecursiveLoop = stack.Contains(current);
            if (!isRecursiveLoop)
            {
                stack.Add(current);
            }

            foreach (var recipe in options)
            {
                if (IsCycle1Recipe(recipe))
                {
                    continue;
                }

                chain.Add(recipe);

                TryFinalizeChain(chain, plan, rootTarget);

                if (!isRecursiveLoop)
                {
                    foreach (var ingredient in recipe.Ingredients)
                    {
                        ResultKey subKey = new ResultKey(ingredient.UnifiedIndex);
                        DFS(subKey, rootTarget, depth + 1, chain, stack, plan);
                    }
                }
                chain.RemoveAt(chain.Count - 1);
            }
            if (!isRecursiveLoop) stack.Remove(current);
        }

        /// <summary>
        /// Validates cost/yield, calculates net cost, and stores unique chains.
        /// if it's new for this result, store it as a RecipeChain.
        /// </summary>
        private void TryFinalizeChain(
            List<ChefRecipe> chain,
            PlanEntry plan,
            ResultKey targetKey)
        {
            if (chain.Count == 0)
            {
                return;
            }

            var externalCost = GetCleanBuffer(ref _bufExternalCost, _totalDefCount);
            var currentState = GetCleanBuffer(ref _bufCurrentState, _totalDefCount);

            bool hasCost = false;

            for (int s = chain.Count - 1; s >= 0; s--)
            {
                var step = chain[s];

                foreach (var ing in step.Ingredients)
                {
                    int needed = ing.Count;
                    int idx = ing.UnifiedIndex;

                    if (currentState[idx] < needed)
                    {
                        int missing = needed - currentState[idx];
                        externalCost[idx] += missing;
                        currentState[idx] += missing;
                        hasCost = true;
                    }
                    currentState[idx] -= needed;
                }
                currentState[step.ResultIndex] += step.ResultCount;
            }

            // ---------- Yield Calculation -----------
            int targetIdx = targetKey.Index;
            int finalBalance = currentState[targetIdx];
            int startupCost = externalCost[targetIdx];

            if ((finalBalance - startupCost) <= 0)
            {
                return;
            }

            if (!hasCost)
            {
                return;
            }

            long sig = RecipeChain.CalculateCanonicalSignature(chain);

            if (plan.CanonicalSignatures.Contains(sig))
            {
                return;
            }

            int[] storedCost = new int[_totalDefCount];
            Array.Copy(externalCost, storedCost, _totalDefCount);

            var newChain = new RecipeChain(chain, storedCost, sig);

            plan.CanonicalSignatures.Add(sig);
            plan.Chains.Add(newChain);
        }

        // ------------------------ Runtime query API ---------------------------
        /// <summary>
        /// Given a snapshot of item stacks (indexed by ItemCatalog.itemCount),
        /// compute all craftable results, up to the preconfigured _maxDepth.
        /// </summary>
        public void ComputeCraftable(int[] unifiedStacks)
        {
            if (unifiedStacks == null || unifiedStacks.Length != _totalDefCount)
            {
                return;
            }

            var result = new List<CraftableEntry>();
            var affordableChains = new List<RecipeChain>();

            foreach (var (rk, plan) in _plans)
            {
                affordableChains.Clear();

                foreach (var chain in plan.Chains)
                {
                    if (CanAffordChain(unifiedStacks, chain))
                    {
                        affordableChains.Add(chain);
                    }
                }

                if (affordableChains.Count == 0)
                {
                    continue;
                }

                affordableChains.Sort((a, b) =>
                {
                    int c = a.ResultCount.CompareTo(b.ResultCount);
                    if (c != 0) return c;
                    return a.Depth.CompareTo(b.Depth);
                });

                int currentCount = -1;
                var currentGroup = new List<RecipeChain>();

                foreach (var chain in affordableChains)
                {
                    if (chain.ResultCount != currentCount)
                    {
                        if (currentGroup.Count > 0)
                        {
                            EmitCraftableGroup(result, rk, currentCount, currentGroup);
                        }
                        currentCount = chain.ResultCount;
                        currentGroup.Clear();
                    }
                    currentGroup.Add(chain);
                }
                if (currentGroup.Count > 0)
                {
                    EmitCraftableGroup(result, rk, currentCount, currentGroup);
                }
            }

            result.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesUpdated?.Invoke(result);
        }

        /// <summary>
        /// Checks whether the given inventory satisfies the required external item costs for the specified chain.
        /// </summary>
        private bool CanAffordChain(int[] currentInventory, RecipeChain chain)
        {
            var cost = chain.TotalCost;
            for (int i = 0; i < cost.Length; i++)
            {
                if (cost[i] > 0 && currentInventory[i] < cost[i])
                {
                    return false;
                }
            }
            return true;
        }

        private static void EmitCraftableGroup(List<CraftableEntry> result, ResultKey rk, int count, List<RecipeChain> group)
        {
            result.Add(new CraftableEntry
            {
                ResultIndex = rk.Index,
                ResultCount = count,
                MinDepth = group[0].Depth,
                Chains = new List<RecipeChain>(group)
            });
        }

        private bool IsCycle1Recipe(ChefRecipe recipe)
        {
            foreach (var ing in recipe.Ingredients)
            {
                if (ing.UnifiedIndex == recipe.ResultIndex) return true;
            }
            return false;
        }

        private static int[] GetCleanBuffer(ref int[] buffer, int requiredSize)
        {
            if (buffer == null || buffer.Length != requiredSize)
            {
                buffer = new int[requiredSize];
            }
            else
            {
                Array.Clear(buffer, 0, buffer.Length);
            }
            return buffer;
        }

        // ------------------------ Types ---------------------------
        /// <summary>
        /// Describes one craftable result (item or equipment) and the chains that are currently affordable from a given inventory snapshot.
        /// Chains are sorted by increasing depth, where MinDepth is simply Chains[0].Depth
        /// </summary>
        internal sealed class CraftableEntry
        {
            public int ResultIndex;
            public int ResultCount;
            public int MinDepth;
            public List<RecipeChain> Chains = new();

            public bool IsItem => ResultIndex < ItemCatalog.itemCount;
            public ItemIndex ResultItem => IsItem ? (ItemIndex)ResultIndex : ItemIndex.None;
            public EquipmentIndex ResultEquipment => IsItem ? EquipmentIndex.None : (EquipmentIndex)(ResultIndex - ItemCatalog.itemCount);
            public bool IsEquipment => !IsItem;
        }

        /// <summary>
        /// A single recipe chain: ordered list of recipes plus cached external costs.
        /// </summary>
        internal sealed class RecipeChain
        {
            internal IReadOnlyList<ChefRecipe> Steps { get; }
            internal int Depth => Steps.Count;
            internal int[] TotalCost { get; }
            internal int ResultCount { get; }
            private readonly long _canonicalSignature;

            internal RecipeChain(List<ChefRecipe> steps, int[] totalCost, long signature)
            {
                Steps = steps.ToArray();
                TotalCost = totalCost;
                ResultCount = (steps.Count > 0) ? Math.Max(1, steps[0].ResultCount) : 1;
                _canonicalSignature = signature;
            }

            internal static long CalculateCanonicalSignature(IReadOnlyList<ChefRecipe> chain)
            {
                if (chain == null || chain.Count == 0) return 0xDEADBEEF;

                var recipeHashes = new List<int>(chain.Count);
                foreach (var recipe in chain)
                {
                    recipeHashes.Add(recipe.GetHashCode());
                }
                recipeHashes.Sort();

                long signature = 17;
                foreach (int hash in recipeHashes)
                {
                    signature = signature * 31 + hash;
                }
                return signature;
            }
        }

        internal sealed class PlanEntry
        {
            public ResultKey Result { get; }
            internal List<RecipeChain> Chains { get; } = new();
            public HashSet<long> CanonicalSignatures { get; } = new();
            internal PlanEntry(ResultKey result) { Result = result; }
        }

        /// <summary>
        /// Value type key representing "what result does this recipe produce"
        /// </summary>
        internal readonly struct ResultKey : IEquatable<ResultKey>
        {
            public readonly int Index;

            public ResultKey(int index)
            {
                Index = index;
            }
            public bool IsItem => Index < ItemCatalog.itemCount;
            public ItemIndex Item => IsItem ? (ItemIndex)Index : ItemIndex.None;
            public EquipmentIndex Equipment => IsItem ? EquipmentIndex.None : (EquipmentIndex)(Index - ItemCatalog.itemCount);
            public bool Equals(ResultKey other) => Index == other.Index;
            public override bool Equals(object obj) => obj is ResultKey other && Equals(other);
            public override int GetHashCode() => Index;
        }
    }
}
