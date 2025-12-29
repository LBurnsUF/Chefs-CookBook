using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

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
            _recipes = recipes?.Distinct().ToList() ?? throw new ArgumentNullException(nameof(recipes));
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
            Array.Clear(_maxDemand, 0, _maxDemand.Length);

            _log.LogInfo($"[Planner] Building Demand Index for {_recipes.Count} recipes...");

            foreach (var r in _recipes)
            {
                foreach (var ing in r.Ingredients)
                {
                    if (ing.Count > _maxDemand[ing.UnifiedIndex])
                    {
                        _maxDemand[ing.UnifiedIndex] = ing.Count;
                    }

                    if (!_recipesByIngredient.TryGetValue(ing.UnifiedIndex, out var list))
                    {
                        list = new List<ChefRecipe>();
                        _recipesByIngredient[ing.UnifiedIndex] = list;
                    }
                    list.Add(r);
                }
            }
        }

        public void ComputeCraftable(int[] unifiedStacks, HashSet<int> changedIndices = null)
        {
            if (!StateController.IsChefStage() || unifiedStacks == null) return;

            if (changedIndices != null && changedIndices.Count > 0 && _entryCache.Count > 0)
            {
                bool impacted = false;
                foreach (var idx in changedIndices)
                {
                    if (_recipesByIngredient.ContainsKey(idx) || _entryCache.ContainsKey(idx))
                    {
                        impacted = true;
                        break;
                    }
                }
                if (!impacted) return;
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var discovered = new Dictionary<int, List<RecipeChain>>();
            var seenSignatures = new HashSet<long>();
            var queue = new Queue<RecipeChain>();

            foreach (var recipe in _recipes)
            {
                if (CanAffordRecipe(unifiedStacks, recipe, null))
                {
                    var (phys, drone, trades) = CalculateSplitCosts(null, recipe);
                    if (phys == null) continue;

                    var chain = new RecipeChain(new[] { recipe }, phys, drone, trades);
                    if (seenSignatures.Add(chain.CanonicalSignature)) AddChainToResults(discovered, queue, chain);
                }
            }

            if (CookBook.AllowMultiplayerPooling.Value)
            {
                InjectTradeRecipes(null, discovered, queue, seenSignatures);
            }

            for (int d = 2; d <= _maxDepth; d++)
            {
                int layerSize = queue.Count;
                if (layerSize == 0) break;

                for (int i = 0; i < layerSize; i++)
                {
                    var existingChain = queue.Dequeue();
                    foreach (var nextRecipe in _recipes)
                    {
                        if (!IsCausallyLinked(existingChain, nextRecipe)) continue;

                        if (CanAffordRecipe(unifiedStacks, nextRecipe, existingChain))
                        {
                            long newSig = RecipeChain.CalculateSignatureWithPotentialStep(existingChain.Steps, nextRecipe);
                            if (!seenSignatures.Add(newSig)) continue;

                            var (phys, drone, trades) = CalculateSplitCosts(existingChain, nextRecipe);
                            if (phys == null) continue;

                            var extendedSteps = existingChain.Steps.Concat(new[] { nextRecipe }).ToList();
                            var newChain = new RecipeChain(extendedSteps, phys, drone, trades);
                            AddChainToResults(discovered, queue, newChain);
                        }
                    }
                }
            }

            // 5. Finalize Results and Update Cache
            _entryCache = discovered.Select(kvp =>
            {
                var validChains = kvp.Value
                    .Where(c => c.ResultIndex == kvp.Key && IsChainEfficient(c))
                    .OrderBy(c => c.DroneCostSparse.Length)
                    .ThenBy(c => c.Depth)
                    .ToList();

                if (validChains.Count == 0) return null;

                return new CraftableEntry
                {
                    ResultIndex = kvp.Key,
                    ResultCount = validChains[0].ResultCount,
                    MinDepth = validChains[0].Depth,
                    Chains = validChains
                };
            }).Where(e => e != null).ToDictionary(e => e.ResultIndex);

            var finalResult = _entryCache.Values.ToList();
            finalResult.Sort(TierManager.CompareCraftableEntries);

            sw.Stop();
            _log.LogDebug($"[Planner] Delta Rebuild: {sw.ElapsedMilliseconds}ms for {finalResult.Count} entries.");
            OnCraftablesUpdated?.Invoke(finalResult);
        }

        private bool IsCausallyLinked(RecipeChain chain, ChefRecipe next)
        {
            int lastProducedIndex = chain.ResultIndex;
            foreach (var ing in next.Ingredients)
            {
                if (ing.UnifiedIndex == lastProducedIndex) return true;
            }

            int candidateResultIndex = next.ResultIndex;
            int globalMaxDemand = _maxDemand[candidateResultIndex];

            if (globalMaxDemand > 1)
            {
                int surplus = GetNetSurplus(chain, candidateResultIndex);

                if (surplus < globalMaxDemand) return true;
            }

            return false;
        }

        private int GetNetSurplus(RecipeChain chain, int itemIndex)
        {
            int net = 0;
            foreach (var step in chain.Steps)
            {
                if (step.ResultIndex == itemIndex) net += step.ResultCount;
                foreach (var ing in step.Ingredients)
                    if (ing.UnifiedIndex == itemIndex) net -= ing.Count;
            }
            return net;
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

        private bool CanAffordRecipe(int[] totalStacks, ChefRecipe recipe, RecipeChain existingChain)
        {
            foreach (var ing in recipe.Ingredients)
            {
                int idx = ing.UnifiedIndex;
                int totalProduced = 0;
                int totalConsumed = ing.Count;

                if (existingChain != null)
                {
                    foreach (var step in existingChain.Steps)
                    {
                        if (step.ResultIndex == idx) totalProduced += step.ResultCount;
                        foreach (var sIng in step.Ingredients) if (sIng.UnifiedIndex == idx) totalConsumed += sIng.Count;
                    }
                }

                int netDeficit = totalConsumed - totalProduced;
                if (netDeficit > 0 && totalStacks[idx] < netDeficit) return false;
            }
            return true;
        }

        private void InjectTradeRecipes(RecipeChain chain, Dictionary<int, List<RecipeChain>> discovered, Queue<RecipeChain> queue, HashSet<long> signatures)
        {
            var alliedSnapshots = InventoryTracker.GetAlliedSnapshots();
            int[] localPhysical = InventoryTracker.GetLocalPhysicalStacks();

            foreach (var ally in alliedSnapshots)
            {
                int tradesLeft = TradeTracker.GetRemainingTrades(ally.Key);

                if (chain != null)
                {
                    tradesLeft -= chain.Steps.OfType<TradeRecipe>().Count(t => t.Donor == ally.Key);
                }

                if (tradesLeft <= 0) continue;

                int[] inv = ally.Value;
                for (int idx = 0; idx < inv.Length; idx++)
                {
                    if (inv[idx] > 0 && _recipesByIngredient.ContainsKey(idx) && !LocalPhysicallyHasOrProduces(chain, localPhysical, idx))
                    {
                        var trade = new TradeRecipe(ally.Key, idx);

                        long sig = chain == null
                            ? RecipeChain.CalculateCanonicalSignature(new[] { (ChefRecipe)trade })
                            : RecipeChain.CalculateSignatureWithPotentialStep(chain.Steps, trade);

                        if (!signatures.Add(sig)) continue;

                        var newSteps = (chain == null)
                            ? new List<ChefRecipe> { trade }
                            : chain.Steps.Concat(new[] { (ChefRecipe)trade }).ToList();

                        // Generate the sparse TradeRequirement array for this new path
                        var tradeRequirements = newSteps.OfType<TradeRecipe>()
                            .GroupBy(t => new { t.Donor, t.ItemUnifiedIndex })
                            .Select(g => new TradeRequirement
                            {
                                Donor = g.Key.Donor,
                                UnifiedIndex = g.Key.ItemUnifiedIndex,
                                Count = g.Count()
                            })
                            .ToArray();

                        var newChain = new RecipeChain(
                            newSteps,
                            chain?.PhysicalCostSparse ?? Array.Empty<Ingredient>(),
                            chain?.DroneCostSparse ?? Array.Empty<Ingredient>(),
                            tradeRequirements
                        );

                        AddChainToResults(discovered, queue, newChain);
                    }
                }
            }
        }

        private bool LocalPhysicallyHasOrProduces(RecipeChain chain, int[] localInv, int itemIdx)
        {
            if (localInv[itemIdx] > 0) return true;
            if (chain != null && chain.Steps.Any(s => s.ResultIndex == itemIdx)) return true;
            return false;
        }

        /// <summary>
        /// Highly optimized cost calculation using reusable array buffers.
        /// </summary>
        private (Ingredient[] phys, Ingredient[] drone, TradeRequirement[] trades) CalculateSplitCosts(RecipeChain old, ChefRecipe next)
        {
            foreach (int idx in _dirtyIndices) { _needsBuffer[idx] = 0; _productionBuffer[idx] = 0; }
            _dirtyIndices.Clear();
            _tempPhysList.Clear();
            _tempDroneList.Clear();

            if (old != null) foreach (var step in old.Steps) TallyStep(step);
            TallyStep(next);

            _productionBuffer[next.ResultIndex] -= next.ResultCount;

            var trades = (old != null ? old.Steps.Concat(new[] { next }) : new[] { next })
                .OfType<TradeRecipe>()
                .GroupBy(t => new { t.Donor, t.ItemUnifiedIndex })
                .Select(g => new TradeRequirement
                {
                    Donor = g.Key.Donor,
                    UnifiedIndex = g.Key.ItemUnifiedIndex,
                    Count = g.Count()
                })
                .ToArray();

            var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

            foreach (int idx in _dirtyIndices)
            {
                int net = _needsBuffer[idx] - _productionBuffer[idx];
                if (net <= 0) continue;

                int physOwned = InventoryTracker.GetPhysicalCount(idx);
                int payWithPhysical = Math.Min(physOwned, net);
                int deficit = net - payWithPhysical;

                int globalDronePotential = InventoryTracker.GetGlobalDronePotentialCount(idx);
                if (deficit > globalDronePotential) return (null, null, null);

                if (payWithPhysical > 0) _tempPhysList.Add(new Ingredient(idx, payWithPhysical));
                if (deficit > 0) _tempDroneList.Add(new Ingredient(idx, deficit));
            }

            Ingredient[] sortedDrones = _tempDroneList
                .OrderBy(d =>
                {
                    var candidate = InventoryTracker.GetScrapCandidate(d.UnifiedIndex);
                    bool isLocal = (candidate.Owner == null || candidate.Owner == localUser);
                    return isLocal ? 1 : 0;
                })
                .ToArray();

            return (_tempPhysList.ToArray(), sortedDrones, trades);
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
            internal int ResultIndex => Steps.Last().ResultIndex;
            internal int ResultCount => Steps.Last().ResultCount;
            internal long CanonicalSignature { get; }
            internal int Depth => Steps.Count;

            internal RecipeChain(IEnumerable<ChefRecipe> steps,
                         IEnumerable<Ingredient> phys,
                         IEnumerable<Ingredient> drones,
                         IEnumerable<TradeRequirement> trades)
            {
                Steps = steps.ToArray();
                PhysicalCostSparse = phys?.Where(i => i.Count > 0).ToArray() ?? Array.Empty<Ingredient>();
                DroneCostSparse = drones?.Where(i => i.Count > 0).ToArray() ?? Array.Empty<Ingredient>();
                AlliedTradeSparse = trades?.ToArray() ?? Array.Empty<TradeRequirement>();
                CanonicalSignature = CalculateCanonicalSignature(Steps);
            }

            internal static long CalculateCanonicalSignature(IReadOnlyList<ChefRecipe> chain)
            {
                if (chain == null || chain.Count == 0) return 0xDEADBEEF;
                var hashes = chain.Select(r => r.GetHashCode()).ToList();
                hashes.Sort();
                long sig = 17;
                foreach (int h in hashes) sig = sig * 31 + h;
                return sig;
            }

            internal static long CalculateSignatureWithPotentialStep(IReadOnlyList<ChefRecipe> currentSteps, ChefRecipe next)
            {
                var hashes = currentSteps.Select(r => r.GetHashCode()).ToList();
                hashes.Add(next.GetHashCode());
                hashes.Sort();
                long signature = 17;
                foreach (int h in hashes) signature = signature * 31 + h;
                return signature;
            }
        }
    }
}