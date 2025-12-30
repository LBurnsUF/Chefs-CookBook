using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;

namespace CookBook
{
    internal sealed class CraftPlanner
    {
        public int SourceItemCount { get; }
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _itemCount;
        private readonly int _totalDefCount;
        private int _maxDepth;
        private readonly ManualLogSource _log;

        private readonly Dictionary<int, List<ChefRecipe>> _recipesByIngredient = new();
        private readonly HashSet<int> _allIngredientIndices = new();
        private readonly int[] _maxDemand;

        private readonly int[] _needsBuffer;
        private readonly int[] _productionBuffer;
        private readonly List<Ingredient> _tempPhysList = new();
        private readonly List<Ingredient> _tempDroneList = new();
        private readonly HashSet<int> _dirtyIndices = new();
        private Dictionary<int, CraftableEntry> _entryCache = new();

        internal event Action<List<CraftableEntry>> OnCraftablesUpdated;

        public CraftPlanner(IReadOnlyList<ChefRecipe> recipes, int maxDepth, ManualLogSource log)
        {
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));
            HashSet<ChefRecipe> uniqueRecipes = new HashSet<ChefRecipe>();
            List<ChefRecipe> recipeList = new List<ChefRecipe>();

            for (int i = 0; i < recipes.Count; i++)
            {
                if (uniqueRecipes.Add(recipes[i]))
                {
                    recipeList.Add(recipes[i]);
                }
            }
            _recipes = recipeList;
            _maxDepth = maxDepth;
            _itemCount = ItemCatalog.itemCount;
            _totalDefCount = _itemCount + EquipmentCatalog.equipmentCount;
            _log = log;
            SourceItemCount = ItemCatalog.itemCount;

            int bufferSize = _totalDefCount + 10;
            _maxDemand = new int[bufferSize];
            _needsBuffer = new int[bufferSize];
            _productionBuffer = new int[bufferSize];

            BuildRecipeIndex();
        }

        internal void SetMaxDepth(int newDepth) => _maxDepth = Math.Max(0, newDepth);

        private void BuildRecipeIndex()
        {
            _recipesByIngredient.Clear();
            _allIngredientIndices.Clear();
            Array.Clear(_maxDemand, 0, _maxDemand.Length);

            _log.LogInfo($"[Planner] Building Demand Index for {_recipes.Count} recipes...");

            foreach (var r in _recipes)
            {
                foreach (var ing in r.Ingredients)
                {
                    int idx = ing.UnifiedIndex;
                    _allIngredientIndices.Add(idx);

                    if (ing.Count > _maxDemand[idx])
                    {
                        _maxDemand[idx] = ing.Count;
                    }

                    if (!_recipesByIngredient.TryGetValue(idx, out var list))
                    {
                        list = new List<ChefRecipe>();
                        _recipesByIngredient[idx] = list;
                    }
                    list.Add(r);
                }
            }
        }

        public void ComputeCraftable(int[] unifiedStacks, HashSet<int> changedIndices = null, bool forceUpdate = false)
        {
            if (!StateController.IsChefStage() || unifiedStacks == null) return;

            if (!forceUpdate && changedIndices != null && changedIndices.Count > 0 && _entryCache.Count > 0)
            {
                bool impacted = false;
                foreach (var idx in changedIndices)
                {
                    if (_allIngredientIndices.Contains(idx) || _entryCache.ContainsKey(idx))
                    {
                        impacted = true;
                        break;
                    }
                }

                if (!impacted)
                {
                    List<CraftableEntry> resultList = new List<CraftableEntry>(_entryCache.Count);
                    foreach (var kvp in _entryCache) resultList.Add(kvp.Value);
                    resultList.Sort(TierManager.CompareCraftableEntries);
                    OnCraftablesUpdated?.Invoke(resultList);
                    return;
                }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var discovered = new Dictionary<int, List<RecipeChain>>();
            var seenSignatures = new HashSet<long>();
            var queue = new Queue<RecipeChain>();

            ResetBuffers();

            foreach (var recipe in _recipes)
            {
                long initialHash = RecipeChain.CalculateInitialHash(recipe);
                TallyStep(recipe);

                if (IsProductiveFromBuffers(recipe.ResultIndex) && isAffordableFromBuffers(unifiedStacks))
                {
                    var (phys, drone, trades) = FinalizeSplitCosts(null, recipe);
                    if (phys != null)
                    {
                        var chain = new RecipeChain(new[] { recipe }, phys, drone, trades, initialHash);
                        if (seenSignatures.Add(chain.CanonicalSignature)) AddChainToResults(discovered, queue, chain);
                    }
                }
                UntallyStep(recipe);
            }

            for (int d = 2; d <= _maxDepth; d++)
            {
                int layerSize = queue.Count;
                if (layerSize == 0) break;

                for (int i = 0; i < layerSize; i++)
                {
                    var existingChain = queue.Dequeue();

                    ResetBuffers();
                    for (int s = 0; s < existingChain.Steps.Count; s++) TallyStep(existingChain.Steps[s]);

                    long currentBaseHash = existingChain.BaseRecipeHash;

                    if (CookBook.AllowMultiplayerPooling.Value)
                    {
                        InjectTradeRecipes(existingChain, discovered, queue, seenSignatures);
                    }

                    foreach (var nextRecipe in _recipes)
                    {
                        long potentialBaseHash = RecipeChain.PredictHash(currentBaseHash, nextRecipe);
                        if (seenSignatures.Contains(potentialBaseHash)) continue;

                        if (!IsCausallyLinked(existingChain, nextRecipe)) continue;

                        TallyStep(nextRecipe);

                        if (IsProductiveFromBuffers(nextRecipe.ResultIndex) && isAffordableFromBuffers(unifiedStacks))
                        {
                            var (phys, drone, trades) = FinalizeSplitCosts(existingChain, nextRecipe);
                            if (phys != null)
                            {
                                ChefRecipe[] extendedSteps = new ChefRecipe[existingChain.Steps.Count + 1];
                                for (int s = 0; s < existingChain.Steps.Count; s++) extendedSteps[s] = existingChain.Steps[s];
                                extendedSteps[existingChain.Steps.Count] = nextRecipe;

                                var newChain = new RecipeChain(extendedSteps, phys, drone, trades, potentialBaseHash);
                                if (seenSignatures.Add(newChain.CanonicalSignature)) AddChainToResults(discovered, queue, newChain);
                            }
                        }
                        UntallyStep(nextRecipe);
                    }
                }
            }

            _entryCache.Clear();
            foreach (var kvp in discovered)
            {
                int resultIdx = kvp.Key;
                List<RecipeChain> validChains = new List<RecipeChain>();

                foreach (var c in kvp.Value)
                {
                    if (c.ResultIndex != resultIdx || !IsChainEfficient(c)) continue;
                    if (c.Steps.Count == 1 && c.Steps[0] is TradeRecipe) continue;
                    validChains.Add(c);
                }

                if (validChains.Count > 0)
                {
                    // Sort by lowest drone cost, then lowest depth
                    validChains.Sort((a, b) =>
                    {
                        int droneCompare = a.DroneCostSparse.Length.CompareTo(b.DroneCostSparse.Length);
                        return droneCompare != 0 ? droneCompare : a.Depth.CompareTo(b.Depth);
                    });

                    _entryCache[resultIdx] = new CraftableEntry
                    {
                        ResultIndex = resultIdx,
                        ResultCount = validChains[0].ResultCount,
                        MinDepth = validChains[0].Depth,
                        Chains = validChains
                    };
                }
            }

            List<CraftableEntry> finalResult = new List<CraftableEntry>(_entryCache.Count);
            foreach (var entry in _entryCache) finalResult.Add(entry.Value);
            finalResult.Sort(TierManager.CompareCraftableEntries);

            sw.Stop();
            _log.LogDebug($"[Planner] Rebuild complete: {sw.ElapsedMilliseconds}ms for {finalResult.Count} entries.");
            OnCraftablesUpdated?.Invoke(finalResult);
        }

        // ---------------------------- Filters -------------------------------------
        private bool IsCausallyLinked(RecipeChain chain, ChefRecipe next)
        {
            foreach (var ing in next.Ingredients)
            {
                if (ing.UnifiedIndex == next.ResultIndex && ing.Count >= next.ResultCount) return false;
                if (ing.UnifiedIndex == chain.ResultIndex) return true;
            }

            int surplus = _productionBuffer[next.ResultIndex] - _needsBuffer[next.ResultIndex];
            int maxReq = _maxDemand[next.ResultIndex];

            if (maxReq == 0) return surplus <= 0;
            return surplus < maxReq;
        }

        private bool IsChainEfficient(RecipeChain chain)
        {
            if (chain.Steps.Count <= 1) return true;

            for (int i = 0; i < chain.Steps.Count - 1; i++)
            {
                var step = chain.Steps[i];
                bool consumedLater = false;

                for (int j = i + 1; j < chain.Steps.Count; j++)
                {
                    foreach (var ing in chain.Steps[j].Ingredients)
                    {
                        if (ing.UnifiedIndex == step.ResultIndex)
                        {
                            consumedLater = true;
                            break;
                        }
                    }
                    if (consumedLater) break;
                }

                if (!consumedLater) return false;
            }

            return true;
        }

        private bool isAffordableFromBuffers(int[] totalStacks)
        {
            foreach (int idx in _dirtyIndices)
            {
                int net = _needsBuffer[idx] - _productionBuffer[idx];
                if (net <= 0) continue;

                if (totalStacks[idx] + InventoryTracker.GetGlobalDronePotentialCount(idx) < net)
                    return false;
            }
            return true;
        }

        private void InjectTradeRecipes(RecipeChain chain, Dictionary<int, List<RecipeChain>> discovered, Queue<RecipeChain> queue, HashSet<long> signatures)
        {
            var alliedSnapshots = InventoryTracker.GetAlliedSnapshots();
            foreach (var ally in alliedSnapshots)
            {
                int tradesLeft = TradeTracker.GetRemainingTrades(ally.Key);
                // Sparse count
                if (chain.AlliedTradeSparse != null)
                {
                    for (int i = 0; i < chain.AlliedTradeSparse.Length; i++)
                        if (chain.AlliedTradeSparse[i].Donor == ally.Key) tradesLeft -= chain.AlliedTradeSparse[i].Count;
                }

                if (tradesLeft <= 0) continue;

                int[] inv = ally.Value;
                foreach (int idx in _allIngredientIndices)
                {
                    // Use buffer for O(1) surplus check
                    if (inv[idx] > 0 && (_productionBuffer[idx] - _needsBuffer[idx]) < _maxDemand[idx])
                    {
                        var trade = new TradeRecipe(ally.Key, idx);
                        long potentialHash = RecipeChain.PredictHash(chain.BaseRecipeHash, trade);
                        if (signatures.Contains(potentialHash)) continue;

                        ChefRecipe[] newSteps = AppendTradetoArray(chain.Steps, trade);
                        TradeRequirement[] tradeReqs = UpdateTradesIncrementally(chain.AlliedTradeSparse, trade);

                        var newChain = new RecipeChain(newSteps, chain.PhysicalCostSparse, chain.DroneCostSparse, tradeReqs, potentialHash);
                        if (signatures.Add(newChain.CanonicalSignature)) AddChainToResults(discovered, queue, newChain);
                    }
                }
            }
        }

        private ChefRecipe[] AppendTradetoArray(IReadOnlyList<ChefRecipe> existing, ChefRecipe next)
        {
            int count = existing.Count;
            ChefRecipe[] newArray = new ChefRecipe[count + 1];
            for (int i = 0; i < count; i++)
            {
                newArray[i] = existing[i];
            }
            newArray[count] = next;
            return newArray;
        }

        private (Ingredient[] phys, Ingredient[] drone, TradeRequirement[] trades) FinalizeSplitCosts(RecipeChain old, ChefRecipe next)
        {
            _tempPhysList.Clear();
            _tempDroneList.Clear();

            var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

            foreach (int idx in _dirtyIndices)
            {
                int net = _needsBuffer[idx] - _productionBuffer[idx];
                if (net <= 0) continue;

                int physOwned = InventoryTracker.GetPhysicalCount(idx);
                int payWithPhysical = Math.Min(physOwned, net);
                int deficit = net - payWithPhysical;

                if (deficit > 0)
                {
                    int totalPotential = InventoryTracker.GetGlobalDronePotentialCount(idx);
                    _tempDroneList.Add(new Ingredient(idx, deficit));
                }

                if (payWithPhysical > 0) _tempPhysList.Add(new Ingredient(idx, payWithPhysical));
            }

            TradeRequirement[] trades = (next is TradeRecipe newTrade)
                ? UpdateTradesIncrementally(old?.AlliedTradeSparse, newTrade)
                : (old?.AlliedTradeSparse ?? Array.Empty<TradeRequirement>());

            return (ManualCopyPhysical(_tempPhysList), ManualSortDrones(_tempDroneList, localUser), trades);
        }

        private void TallyStep(ChefRecipe step)
        {
            _productionBuffer[step.ResultIndex] += step.ResultCount;
            _dirtyIndices.Add(step.ResultIndex);
            foreach (var ing in step.Ingredients)
            {
                _needsBuffer[ing.UnifiedIndex] += ing.Count;
                _dirtyIndices.Add(ing.UnifiedIndex);
            }
        }

        private void ResetBuffers()
        {
            foreach (int idx in _dirtyIndices)
            {
                _needsBuffer[idx] = 0;
                _productionBuffer[idx] = 0;
            }
            _dirtyIndices.Clear();
        }

        private void UntallyStep(ChefRecipe step)
        {
            foreach (var ing in step.Ingredients)
            {
                _needsBuffer[ing.UnifiedIndex] -= ing.Count;
            }
            _productionBuffer[step.ResultIndex] -= step.ResultCount;
        }

        private bool IsProductiveFromBuffers(int resultIdx)
        {
            return (_productionBuffer[resultIdx] - _needsBuffer[resultIdx]) > 0;
        }

        private TradeRequirement[] UpdateTradesIncrementally(TradeRequirement[] existing, TradeRecipe newTrade)
        {
            if (existing == null || existing.Length == 0)
            {
                return new[] { new TradeRequirement { Donor = newTrade.Donor, UnifiedIndex = newTrade.ItemUnifiedIndex, Count = 1 } };
            }

            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].Donor == newTrade.Donor && existing[i].UnifiedIndex == newTrade.ItemUnifiedIndex)
                {
                    TradeRequirement[] updated = new TradeRequirement[existing.Length];
                    Array.Copy(existing, updated, existing.Length);
                    updated[i].Count++;
                    return updated;
                }
            }

            TradeRequirement[] expanded = new TradeRequirement[existing.Length + 1];
            Array.Copy(existing, expanded, existing.Length);
            expanded[existing.Length] = new TradeRequirement { Donor = newTrade.Donor, UnifiedIndex = newTrade.ItemUnifiedIndex, Count = 1 };
            return expanded;
        }

        // ------------------------------- Sorting -------------------------------------
        private Ingredient[] ManualSortDrones(List<Ingredient> list, NetworkUser localUser)
        {
            if (list.Count == 0) return Array.Empty<Ingredient>();
            Ingredient[] array = list.ToArray();

            // Simple Insertion Sort: Preferred for the very small arrays typical of drone costs
            for (int i = 1; i < array.Length; i++)
            {
                Ingredient key = array[i];
                int j = i - 1;

                while (j >= 0 && CompareDronePriority(array[j], key, localUser) > 0)
                {
                    array[j + 1] = array[j];
                    j--;
                }
                array[j + 1] = key;
            }
            return array;
        }

        private Ingredient[] ManualCopyPhysical(List<Ingredient> list)
        {
            if (list.Count == 0) return Array.Empty<Ingredient>();
            Ingredient[] result = new Ingredient[list.Count];
            for (int i = 0; i < list.Count; i++) result[i] = list[i];
            return result;
        }

        private int CompareDronePriority(Ingredient a, Ingredient b, NetworkUser localUser)
        {
            var candA = InventoryTracker.GetScrapCandidate(a.UnifiedIndex);
            var candB = InventoryTracker.GetScrapCandidate(b.UnifiedIndex);

            bool isLocalA = (candA.Owner == null || candA.Owner == localUser);
            bool isLocalB = (candB.Owner == null || candB.Owner == localUser);

            if (isLocalA != isLocalB)
            {
                return isLocalA ? -1 : 1;
            }

            return 0;
        }

        private void AddChainToResults(Dictionary<int, List<RecipeChain>> results, Queue<RecipeChain> queue, RecipeChain chain)
        {
            if (!results.TryGetValue(chain.ResultIndex, out var list))
            {
                list = new List<RecipeChain>();
                results[chain.ResultIndex] = list;
            }
            list.Add(chain);
            queue.Enqueue(chain);
        }

        // -------------------------------- Types --------------------------------------

        internal sealed class TradeRecipe : ChefRecipe
        {
            public NetworkUser Donor;
            public int ItemUnifiedIndex;

            public TradeRecipe(NetworkUser donor, int itemIndex)
                : base(itemIndex, 1, Array.Empty<Ingredient>())
            {
                Donor = donor;
                ItemUnifiedIndex = itemIndex;
            }

            public override int GetHashCode()
            {
                return (Donor.netId.GetHashCode() * 31) + ItemUnifiedIndex;
            }
        }

        internal sealed class CraftableEntry
        {
            public int ResultIndex;
            public int ResultCount;
            public int MinDepth;
            public List<RecipeChain> Chains = new();
            public bool IsItem => ResultIndex < ItemCatalog.itemCount;
            public ItemIndex ResultItem => IsItem ? (ItemIndex)ResultIndex : ItemIndex.None;
            public EquipmentIndex ResultEquipment => IsItem ? EquipmentIndex.None : (EquipmentIndex)(ResultIndex - ItemCatalog.itemCount);
        }

        internal struct TradeRequirement
        {
            public NetworkUser Donor;
            public int UnifiedIndex;
            public int Count;
        }

        internal sealed class RecipeChain
        {
            internal IReadOnlyList<ChefRecipe> Steps { get; }
            internal Ingredient[] PhysicalCostSparse { get; }
            internal Ingredient[] DroneCostSparse { get; }
            internal TradeRequirement[] AlliedTradeSparse { get; }
            internal int ResultIndex => Steps.Count > 0 ? Steps[Steps.Count - 1].ResultIndex : -1;
            internal int ResultCount => Steps.Count > 0 ? Steps[Steps.Count - 1].ResultCount : 0;
            internal int Depth => Steps.Count;

            internal long BaseRecipeHash { get; }
            internal long CanonicalSignature { get; }

            internal RecipeChain(ChefRecipe[] steps,
                                 Ingredient[] phys,
                                 Ingredient[] drones,
                                 TradeRequirement[] trades,
                                 long baseRecipeHash)
            {
                Steps = steps;
                PhysicalCostSparse = phys;
                DroneCostSparse = drones;
                AlliedTradeSparse = trades;
                BaseRecipeHash = baseRecipeHash;

                CanonicalSignature = CalculateFinalSignature(this);
            }

            private static long CalculateFinalSignature(RecipeChain chain)
            {
                long sig = chain.BaseRecipeHash;

                for (int i = 0; i < chain.DroneCostSparse.Length; i++)
                {
                    sig = sig * 31 + chain.DroneCostSparse[i].UnifiedIndex;
                    sig = sig * 31 + chain.DroneCostSparse[i].Count;
                }

                for (int i = 0; i < chain.AlliedTradeSparse.Length; i++)
                {
                    sig = sig * 31 + chain.AlliedTradeSparse[i].Donor.netId.GetHashCode();
                    sig = sig * 31 + chain.AlliedTradeSparse[i].UnifiedIndex;
                }

                return sig;
            }

            internal static long PredictHash(long currentHash, ChefRecipe next)
            {
                return currentHash + (long)next.GetHashCode();
            }

            internal static long CalculateInitialHash(ChefRecipe recipe)
            {
                return (long)recipe.GetHashCode();
            }
        }
    }
}