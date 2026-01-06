using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CookBook
{
    internal sealed class CraftPlanner
    {
        private readonly ManualLogSource _log;
        private int _maxDepth;

        public int SourceItemCount { get; }
        private readonly int _itemCount;
        private readonly int _totalDefCount;

        private readonly HashSet<int> _dirtyIndices = new();
        private readonly HashSet<int> _allIngredientIndices = new();
        private readonly HashSet<int> _transientIngredients = new();
        private HashSet<ItemIndex> _lastKnownCorrupted = new();

        private readonly int[] _maxDemand;
        private readonly int[] _needsBuffer;
        private readonly int[] _productionBuffer;

        private readonly List<Ingredient> _tempPhysList = new();
        private readonly List<DroneRequirement> _tempDroneReqList = new();
        private readonly IReadOnlyList<ChefRecipe> _recipes;

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
            _allIngredientIndices.Clear();
            Array.Clear(_maxDemand, 0, _maxDemand.Length);

            _log.LogInfo($"[Planner] Building Demand Index for {_recipes.Count} recipes...");

            foreach (var r in _recipes)
            {
                foreach (var ing in r.Ingredients)
                {
                    int idx = ing.UnifiedIndex;
                    _allIngredientIndices.Add(idx);

                    if (ing.Count > _maxDemand[idx]) _maxDemand[idx] = ing.Count;
                }
            }
        }

        public void ComputeCraftable(int[] unifiedStacks, IReadOnlyList<ChefRecipe> recipes, bool canScrapDrones, HashSet<int> changedIndices = null, bool forceUpdate = false)
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

                    if (IsTransformedItemRelevant(idx))
                    {
                        impacted = true;
                        break;
                    }
                }

                if (!impacted)
                {
                    RefreshVisualOverridesAndEmit();
                    return;
                }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();

            _transientIngredients.Clear();
            foreach (var r in recipes)
                foreach (var ing in r.Ingredients) _transientIngredients.Add(ing.UnifiedIndex);

            var discovered = new Dictionary<int, List<RecipeChain>>();
            var seenSignatures = new HashSet<long>();
            var queue = new Queue<RecipeChain>();

            foreach (var recipe in recipes)
            {
                if (isRecipeAffordable(unifiedStacks, recipe, canScrapDrones, null))
                {
                    var (phys, drone, trades) = CalculateSplitCosts(null, recipe, canScrapDrones, unifiedStacks);
                    if (phys == null) continue;

                    long sig = (long)recipe.GetHashCode();
                    var chain = new RecipeChain(new[] { recipe }, phys, drone, trades, sig);
                    if (seenSignatures.Add(chain.CanonicalSignature)) AddChainToResults(discovered, queue, chain);
                }
            }

            if (CookBook.IsPoolingEnabled) InjectTradeRecipes(null, discovered, queue, seenSignatures, _transientIngredients);

            for (int d = 2; d <= _maxDepth; d++)
            {
                int layerSize = queue.Count;
                if (layerSize == 0) break;

                for (int i = 0; i < layerSize; i++)
                {
                    var existingChain = queue.Dequeue();
                    foreach (var nextRecipe in recipes)
                    {
                        if (!IsCausallyLinked(existingChain, nextRecipe)) continue;

                        long newSig = RecipeChain.CalculateRollingSignature(existingChain.CanonicalSignature, nextRecipe);
                        if (!seenSignatures.Add(newSig)) continue;

                        if (isRecipeAffordable(unifiedStacks, nextRecipe, canScrapDrones, existingChain))
                        {
                            var (phys, drone, trades) = CalculateSplitCosts(existingChain, nextRecipe, canScrapDrones, unifiedStacks);
                            if (phys == null) continue;

                            var extendedSteps = existingChain.Steps.Concat(new[] { nextRecipe }).ToList();
                            var newChain = new RecipeChain(extendedSteps, phys, drone, trades, newSig);

                            AddChainToResults(discovered, queue, newChain);
                        }
                    }
                }
            }

            _entryCache = discovered.Select(kvp =>
            {
                var validChains = kvp.Value
                    .Where(c => c.ResultIndex == kvp.Key)
                    .Where(c => !(c.Steps.Count == 1 && c.Steps[0] is TradeRecipe))
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

            var finalResults = _entryCache.Values.ToList();

            RefreshVisualOverridesAndEmit();

            finalResults.Sort(TierManager.CompareCraftableEntries);
            sw.Stop();

            _log.LogDebug($"[Planner] Rebuild complete: {sw.ElapsedMilliseconds}ms for {finalResults.Count} entries.");
        }

        private bool IsCausallyLinked(RecipeChain chain, ChefRecipe next)
        {
            foreach (var ing in next.Ingredients)
            {
                if (GetNetSurplus(chain, ing.UnifiedIndex) > 0) return true;
            }

            int resultIdx = next.ResultIndex;
            int maxDemandForThisItem = _maxDemand[resultIdx];

            if (maxDemandForThisItem > 1)
            {
                int currentSurplus = GetNetSurplus(chain, resultIdx);
                if (currentSurplus > 0 && currentSurplus < maxDemandForThisItem)
                    return true;
            }

            return false;
        }

        private bool IsChainInefficient(RecipeChain chain)
        {
            int inputWeight = GetWeightedCost(chain);
            int intentionalYield = chain.ResultCount;
            int goalValue = intentionalYield * GetItemWeight(chain.ResultIndex);

            return inputWeight > goalValue * 2;
        }

        private bool IsChainDominated(RecipeChain newChain, Dictionary<int, List<RecipeChain>> discovered)
        {
            if (!discovered.TryGetValue(newChain.ResultIndex, out var existingList)) return false;

            int newWeight = GetWeightedCost(newChain);

            foreach (var existing in existingList)
            {
                int existingWeight = GetWeightedCost(existing);

                if (existing.Depth < newChain.Depth && existingWeight <= newWeight && HasSuperiorSurplusProfile(existing, newChain))
                {
                    return true;
                }
            }
            return false;
        }

        private bool isRecipeAffordable(int[] totalStacks, ChefRecipe recipe, bool scrapperPresent, RecipeChain existingChain)
        {
            foreach (var ing in recipe.Ingredients)
            {
                int idx = ing.UnifiedIndex;

                int surplus = (existingChain == null) ? 0 : GetNetSurplus(existingChain, idx);

                int netDeficit = Math.Max(0, ing.Count - surplus);

                if (netDeficit <= 0) continue;

                int physical = totalStacks[idx];
                int potential = scrapperPresent ? InventoryTracker.GetGlobalDronePotentialCount(idx) : 0;

                if (physical + potential < netDeficit) return false;
            }
            return true;
        }

        private void InjectTradeRecipes(RecipeChain chain, Dictionary<int, List<RecipeChain>> discovered,
                               Queue<RecipeChain> queue, HashSet<long> signatures,
                               HashSet<int> validIngredients)
        {
            var alliedSnapshots = InventoryTracker.GetAlliedSnapshots();
            int[] localPhysical = InventoryTracker.GetLocalPhysicalStacks();

            foreach (var ally in alliedSnapshots)
            {
                int tradesLeft = TradeTracker.GetRemainingTrades(ally.Key);

                if (chain != null) tradesLeft -= chain.Steps.OfType<TradeRecipe>().Count(t => t.Donor == ally.Key);

                if (tradesLeft <= 0) continue;

                int[] inv = ally.Value;
                for (int idx = 0; idx < inv.Length; idx++)
                {
                    if (inv[idx] > 0 && validIngredients.Contains(idx) && !LocalPhysicallyHasOrProduces(chain, localPhysical, idx))
                    {
                        var trade = new TradeRecipe(ally.Key, idx);

                        long sig = (chain == null)
                            ? (long)trade.GetHashCode()
                            : RecipeChain.CalculateRollingSignature(chain.CanonicalSignature, trade);

                        if (!signatures.Add(sig)) continue;

                        var newSteps = (chain == null)
                            ? new List<ChefRecipe> { trade }
                            : chain.Steps.Concat(new[] { (ChefRecipe)trade }).ToList();

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
                            chain?.DroneCostSparse ?? Array.Empty<DroneRequirement>(),
                            tradeRequirements,
                            sig
                        );

                        AddChainToResults(discovered, queue, newChain);
                    }
                }
            }
        }

        /// <summary>
        /// Highly optimized cost calculation using reusable array buffers.
        /// </summary>
        private (Ingredient[] phys, DroneRequirement[] drones, TradeRequirement[] trades) CalculateSplitCosts(
            RecipeChain old,
            ChefRecipe next,
            bool canScrapDrones,
            int[] inventory)
        {
            // 1. Reset ONLY the temporary resolution lists
            ClearCostBuffers();

            // 2. INHERIT: Confirmation of previous sacrifices
            if (old != null)
            {
                _tempPhysList.AddRange(old.PhysicalCostSparse);
                _tempDroneReqList.AddRange(old.DroneCostSparse);
            }

            // 3. RESOLVE NEXT: Check ingredients for ONLY the next recipe
            foreach (var ing in next.Ingredients)
            {
                int idx = ing.UnifiedIndex;

                // See what we have available internally from steps already completed
                int surplus = (old == null) ? 0 : GetNetSurplus(old, idx);

                // Deficit is only what we can't cover with existing surplus
                int deficit = Math.Max(0, ing.Count - surplus);

                if (deficit > 0)
                {
                    if (!ResolveRequirement(idx, deficit, canScrapDrones, inventory))
                    {
                        return (null, null, null);
                    }
                }
            }

            var trades = ExtractTrades(old, next);

            // Consolidated Physical list to prevent UI row duplication
            var consolidatedPhys = _tempPhysList
                .GroupBy(i => i.UnifiedIndex)
                .Select(g => new Ingredient(g.Key, g.Sum(i => i.Count)))
                .ToArray();

            return (consolidatedPhys, _tempDroneReqList.ToArray(), trades);
        }

        private void ClearCostBuffers()
        {
            _tempPhysList.Clear();
            _tempDroneReqList.Clear();
        }

        private TradeRequirement[] ExtractTrades(RecipeChain old, ChefRecipe next)
        {
            var allSteps = old != null ? old.Steps.Append(next) : new[] { next };

            return allSteps
                .OfType<TradeRecipe>()
                .GroupBy(t => new { t.Donor, t.ItemUnifiedIndex })
                .Select(g => new TradeRequirement
                {
                    Donor = g.Key.Donor,
                    UnifiedIndex = g.Key.ItemUnifiedIndex,
                    Count = g.Count()
                })
                .ToArray();
        }

        private bool ResolveRequirement(int unifiedIndex, int amountNeeded, bool scrapperPresent, int[] inventory)
        {
            // High-performance direct access
            int physOwned = inventory[unifiedIndex];
            int payWithPhysical = Math.Min(physOwned, amountNeeded);
            int deficit = amountNeeded - payWithPhysical;

            if (payWithPhysical > 0)
            {
                _tempPhysList.Add(new Ingredient(unifiedIndex, payWithPhysical));
            }

            if (deficit > 0)
            {
                if (!scrapperPresent) return false;

                var candidates = InventoryTracker.GetScrapCandidates(unifiedIndex);
                if (candidates == null) return false;

                int remainingDeficit = deficit;
                foreach (var candidate in candidates)
                {
                    int availableInDrone = DroneUpgradeUtils.GetDroneCountFromUpgradeCount(candidate.UpgradeCount);
                    int take = Math.Min(remainingDeficit, availableInDrone);

                    if (take > 0)
                    {
                        _tempDroneReqList.Add(new DroneRequirement
                        {
                            Owner = candidate.Owner,
                            DroneIdx = candidate.DroneIdx,
                            Count = take,
                            TotalUpgradeCount = candidate.UpgradeCount,
                            ScrapIndex = unifiedIndex
                        });
                        remainingDeficit -= take;
                    }
                    if (remainingDeficit <= 0) break;
                }

                if (remainingDeficit > 0) return false;
            }

            return true;
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

        private static int GetItemWeight(int unifiedIndex)
        {
            if (unifiedIndex < ItemCatalog.itemCount)
            {
                var tier = ItemCatalog.GetItemDef((ItemIndex)unifiedIndex)?.tier;
                return tier switch
                {
                    ItemTier.Tier1 => 1,
                    ItemTier.Tier2 => 2,
                    ItemTier.Tier3 => 4,
                    ItemTier.VoidTier1 => 1,
                    ItemTier.VoidTier2 => 2,
                    ItemTier.VoidTier3 => 4,
                    ItemTier.Boss => 4,
                    ItemTier.VoidBoss => 4,
                    ItemTier.FoodTier => 4,
                    ItemTier.Lunar => 1,
                    ItemTier.NoTier => 1, // consumed items
                    _ => 2
                };
            }
            return 3; // Equipment
        }

        private int GetWeightedCost(RecipeChain chain)
        {
            int total = 0;

            // Physical items cost what is consumed
            foreach (var ing in chain.PhysicalCostSparse)
                total += GetItemWeight(ing.UnifiedIndex) * ing.Count;

            // Drones cost their FULL value because the entity is destroyed
            foreach (var drone in chain.DroneCostSparse)
            {
                // Use the total potential of the drone, not just the 'Count' used for this recipe
                int totalDronePotential = DroneUpgradeUtils.GetDroneCountFromUpgradeCount(drone.TotalUpgradeCount);
                total += GetItemWeight(drone.ScrapIndex) * totalDronePotential;
            }

            foreach (var trade in chain.AlliedTradeSparse)
                total += GetItemWeight(trade.UnifiedIndex) * trade.Count;

            return total;
        }

        private bool HasSuperiorSurplusProfile(RecipeChain baseline, RecipeChain candidate)
        {
            foreach (var step in candidate.Steps)
            {
                int itemIdx = step.ResultIndex;

                if (itemIdx == candidate.ResultIndex) continue;

                if (GetNetSurplus(baseline, itemIdx) < GetNetSurplus(candidate, itemIdx))
                {
                    return false;
                }
            }
            return true;
        }

        private bool LocalPhysicallyHasOrProduces(RecipeChain chain, int[] localInv, int itemIdx)
        {
            if (localInv[itemIdx] > 0) return true;
            if (chain != null && chain.Steps.Any(s => s.ResultIndex == itemIdx)) return true;
            return false;
        }

        private void AddChainToResults(Dictionary<int, List<RecipeChain>> results, Queue<RecipeChain> queue, RecipeChain chain)
        {
            if (!results.TryGetValue(chain.ResultIndex, out var list))
            {
                list = new List<RecipeChain>();
                results[chain.ResultIndex] = list;
            }

            if (IsChainInefficient(chain) || IsChainDominated(chain, results)) return;
            if (list.Count >= CookBook.ChainsLimit) return;

            list.Add(chain);
            queue.Enqueue(chain);
        }

        internal void RefreshVisualOverridesAndEmit()
        {
            if (_entryCache == null || _entryCache.Count == 0) return;

            var finalResults = _entryCache.Values.ToList();

            foreach (var entry in finalResults)
            {
                int rawIdx = entry.Chains.FirstOrDefault()?.Steps.LastOrDefault()?.ResultIndex ?? entry.ResultIndex;

                if (CookBook.ShowCorruptedResults.Value)
                {
                    int visualOverride = InventoryTracker.GetVisualResultIndex(rawIdx);
                    entry.ResultIndex = (visualOverride != -1) ? visualOverride : rawIdx;
                }
                else
                {
                    entry.ResultIndex = rawIdx;
                }
            }

            finalResults.Sort(TierManager.CompareCraftableEntries);
            OnCraftablesUpdated?.Invoke(finalResults);
        }

        private bool IsTransformedItemRelevant(int unifiedIndex)
        {
            if (unifiedIndex >= ItemCatalog.itemCount) return false;
            ItemIndex itemIdx = (ItemIndex)unifiedIndex;

            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                // If the changed item is the void counterpart to something we can craft, it is relevant
                if (info.transformedItem == itemIdx && _entryCache.ContainsKey((int)info.originalItem))
                    return true;
            }
            return false;
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
            internal DroneRequirement[] DroneCostSparse { get; }
            internal TradeRequirement[] AlliedTradeSparse { get; }
            internal int ResultIndex => Steps.Last().ResultIndex;
            internal int ResultCount => Steps.Last().ResultCount;
            internal long CanonicalSignature { get; }
            internal int Depth => Steps.Count;

            internal RecipeChain(IEnumerable<ChefRecipe> steps,
                         IEnumerable<Ingredient> phys,
                         IEnumerable<DroneRequirement> drones,
                         IEnumerable<TradeRequirement> trades,
                         long? signature = null)
            {
                Steps = steps.ToArray();
                PhysicalCostSparse = phys?.Where(i => i.Count > 0).ToArray() ?? Array.Empty<Ingredient>();
                DroneCostSparse = drones?.Where(i => i.Count > 0).ToArray() ?? Array.Empty<DroneRequirement>();
                AlliedTradeSparse = trades?.ToArray() ?? Array.Empty<TradeRequirement>();

                CanonicalSignature = signature ?? CalculateCanonicalSignature(Steps);
            }

            public int GetMaxAffordable(
            int[] localPhysical,
            int[] dronePotential,
            Dictionary<NetworkUser, int[]> alliedSnapshots,
            Dictionary<NetworkUser, int> remainingTrades)
            {
                int max = int.MaxValue;

                foreach (var cost in PhysicalCostSparse)
                {
                    if (cost.Count == 0) continue;
                    max = Math.Min(max, localPhysical[cost.UnifiedIndex] / cost.Count);
                }

                var tierNeeds = new Dictionary<int, int>();
                foreach (var drone in DroneCostSparse)
                {
                    if (drone.Count == 0) continue;
                    if (!tierNeeds.ContainsKey(drone.ScrapIndex)) tierNeeds[drone.ScrapIndex] = 0;
                    tierNeeds[drone.ScrapIndex] += drone.Count;
                }

                foreach (var kvp in tierNeeds)
                {
                    max = Math.Min(max, dronePotential[kvp.Key] / kvp.Value);
                }

                foreach (var trade in AlliedTradeSparse)
                {
                    if (trade.Count == 0) continue;
                    if (!alliedSnapshots.TryGetValue(trade.Donor, out int[] donorInv)) return 0;
                    if (!remainingTrades.TryGetValue(trade.Donor, out int tradesLeft)) return 0;

                    max = Math.Min(max, Math.Min(donorInv[trade.UnifiedIndex] / trade.Count, tradesLeft / trade.Count));
                }

                return max == int.MaxValue ? 0 : max;
            }

            internal static long CalculateCanonicalSignature(IEnumerable<ChefRecipe> chain)
            {
                if (chain == null) return 0;
                long sig = 0;
                foreach (var r in chain) sig ^= (long)r.GetHashCode();
                return sig;
            }

            internal static long CalculateRollingSignature(long currentSignature, ChefRecipe next) { return currentSignature ^ (long)next.GetHashCode(); }
        }

        internal struct DroneRequirement
        {
            public NetworkUser Owner;
            public DroneIndex DroneIdx;
            public int Count;
            public int TotalUpgradeCount;
            public int ScrapIndex;
        }
    }
}