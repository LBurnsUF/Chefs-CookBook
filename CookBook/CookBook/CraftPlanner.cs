using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using static CookBook.PerfProfile;

namespace CookBook
{
    internal sealed class CraftPlanner
    {
        private readonly ManualLogSource _log;
        private int _maxDepth;

        public int SourceItemCount { get; }
        private readonly int _itemCount;
        internal readonly int _totalDefCount;

        private readonly HashSet<int> _allIngredientIndices = new();

        private bool isPoolingEnabled;
        private bool canScrapDrones;

        private readonly int[] _maxDemand;
        private readonly int _maskWords;
        private ulong[] _haveMaskBuffer;
        private Dictionary<int, CraftableEntry> _entryCache = new();

        private readonly Dictionary<long, Dictionary<CostShapeKey, BestCostRecord>> _bestByOutputAndShape = new();
        private readonly Dictionary<long, Dictionary<int, List<BestCostRecord>>> _frontierBucketsByOutput = new();
        private readonly Dictionary<int, HashSet<ulong>> _seenChainSigByResult = new();

        private int[] _candidateMark;
        private int _candidateStamp = 1;
        private int[] _candidatesScratch;
        private int _candidateCount;
        private int[] _candidateBridgeMark;
        private int _candidateBridgeStamp = 1;

        private int[] _missingMark;
        private int _missingStamp = 1;
        private int[] _missingScratch;
        private int _missingCount;

        private int[] _deficitsScratch;

        private bool[] _activeMasterRecipe;
        private ChefRecipe[] _activeRecipeByMaster;

        private int[] _droneNeedScratch;
        private int[] _droneMark;
        private int _droneStamp = 1;
        private int[] _droneTouched;
        private int _droneTouchedCount;
        private readonly HashSet<ulong> _scrappedDronesThisChain = new();
        private int[] _scrapSurplusThisChain;

        private int[] _profileKeys;
        private int[] _profileVals;
        private int _profileCount;

        private int[] _posKeys;
        private int _posCount;

        private int[] _defKeys;
        private int _defCount;

        private int _pMaxLen;
        private int _dMaxLen;
        private int _tMaxLen;

        private readonly ulong[] _surplusMaskScratch;

        private readonly List<int> _bucketKeysScratch;

        private readonly List<DroneRequirement> _tempDroneReqList = new();

        internal event Action<List<CraftableEntry>, InventorySnapshot> OnCraftablesUpdated;

        // ------------ Initialization ------------
        public CraftPlanner(int maxDepth, ManualLogSource log)
        {
            _maxDepth = maxDepth;
            _log = log;

            _itemCount = ItemCatalog.itemCount;
            _totalDefCount = RecipeProvider.TotalDefCount;
            SourceItemCount = _itemCount;

            int masterCount = RecipeProvider.Recipes.Count;
            _maskWords = RecipeProvider.MaskWords;

            _maxDemand = new int[_totalDefCount];
            _haveMaskBuffer = new ulong[_maskWords];
            _deficitsScratch = new int[_totalDefCount];

            _activeMasterRecipe = new bool[masterCount];
            _activeRecipeByMaster = new ChefRecipe[masterCount];
            _candidateBridgeMark = new int[masterCount];

            _candidateMark = new int[masterCount];
            _candidatesScratch = new int[masterCount];

            _missingMark = new int[_totalDefCount];
            _missingScratch = new int[_totalDefCount];

            _droneNeedScratch = new int[_totalDefCount];
            _droneMark = new int[_totalDefCount];
            _droneTouched = new int[_maxDepth * 2 + 2];
            _scrapSurplusThisChain = new int[_totalDefCount];

            int maxKeys = (_maxDepth * 3) + 3;
            _profileKeys = new int[maxKeys];
            _profileVals = new int[maxKeys];
            _posKeys = new int[maxKeys];
            _defKeys = new int[maxKeys];

            _pMaxLen = 2 * _maxDepth;
            _dMaxLen = 2 * _maxDepth;
            _tMaxLen = _maxDepth;

            _surplusMaskScratch = new ulong[_maskWords];

            _bucketKeysScratch = new List<int>(MaxBucketKeysCapacity(_maxDepth));
        }

        private void BuildRecipeIndex(IReadOnlyList<ChefRecipe> recipes)
        {
            _allIngredientIndices.Clear();
            Array.Clear(_maxDemand, 0, _maxDemand.Length);

            for (int i = 0; i < recipes.Count; i++)
            {
                var r = recipes[i];
                if (r == null) continue;

                ForEachRequirement(r, (idx, count) =>
                {
                    if ((uint)idx >= (uint)_totalDefCount) return;

                    _allIngredientIndices.Add(idx);
                    if (count > _maxDemand[idx]) _maxDemand[idx] = count;
                });
            }
        }

        // --------------- Craftable Computation ------------------
        public void ComputeCraftable(
            in InventorySnapshot snap,
            int[] changedIndices = null,
            int changedCount = 0,
            bool forceUpdate = false)
        {
            PerfProfile.Reset();
            using (PerfProfile.Measure(PerfProfile.Region.TotalCompute))
            {
                canScrapDrones = snap.CanScrapDrones;
                isPoolingEnabled = snap.IsPoolingEnabled;
                if (!StateController.IsChefStage()) return;

                if (!forceUpdate && changedCount <= 0)
                {
                    DebugLog.CraftTrace(_log, "Skipping recompute, using cache (no emit needed).");
                    return;
                }

                if (!forceUpdate && changedIndices == null && changedCount > 0)
                {
                    _log.LogWarning("ComputeCraftable called with changedCount>0 but changedIndices==null. This is invalid.");
                }

                var recipes = snap.FilteredRecipes;
                if (recipes == null || recipes.Count == 0) return;

                var physicalStacks = snap.PhysicalStacks;
                if (physicalStacks == null || physicalStacks.Length == 0) return;

                if (!forceUpdate && changedIndices != null && changedCount > 0)
                {
                    bool impacted = false;
                    for (int i = 0; i < changedCount; i++)
                    {
                        int idx = changedIndices[i];

                        if (_allIngredientIndices.Contains(idx) || _entryCache.ContainsKey(idx) || IsTransformedItemRelevant(idx))
                        {
                            impacted = true;
                            break;
                        }
                    }

                    if (!impacted)
                    {
                        DebugLog.CraftTrace(_log, "Skipping recompute, using cache (no emit needed).");
                        return;
                    }
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (PerfProfile.Measure(PerfProfile.Region.BuildRecipeIndex))
                {
                    BuildRecipeIndex(recipes);
                }

                var producersByResultMaster = RecipeProvider.ProducersByResult;
                var reqMasksMaster = RecipeProvider.ReqMasks;
                var consumersByIngredientMaster = RecipeProvider.ConsumersByIngredient;

                int masterCount = RecipeProvider.Recipes.Count;

                Array.Clear(_activeMasterRecipe, 0, _activeMasterRecipe.Length);
                Array.Clear(_activeRecipeByMaster, 0, _activeRecipeByMaster.Length);
                _bestByOutputAndShape.Clear();
                _frontierBucketsByOutput.Clear();
                _seenChainSigByResult.Clear();

                for (int i = 0; i < recipes.Count; i++)
                {
                    var rcp = recipes[i];
                    if (rcp == null) continue;

                    if (!RecipeProvider.MasterIndexByRecipe.TryGetValue(rcp, out int masterIdx))
                        continue;

                    _activeMasterRecipe[masterIdx] = true;
                    _activeRecipeByMaster[masterIdx] = rcp;
                }

                ulong[] haveMask = snap.PhysicalMask;
                var droneMask = snap.DroneMask;

                if (canScrapDrones && droneMask != null)
                {
                    int words = _maskWords;

                    if (_haveMaskBuffer == null || _haveMaskBuffer.Length != words)
                        _haveMaskBuffer = new ulong[words];

                    var physMask = snap.PhysicalMask;
                    int physLen = physMask?.Length ?? 0;
                    int droneLen = droneMask.Length;

                    for (int w = 0; w < words; w++)
                    {
                        ulong p = (w < physLen) ? physMask[w] : 0UL;
                        ulong d = (w < droneLen) ? droneMask[w] : 0UL;
                        _haveMaskBuffer[w] = p | d;
                    }

                    haveMask = _haveMaskBuffer;
                }

                var discovered = new Dictionary<int, List<RecipeChain>>();
                var queue = new Queue<RecipeChain>();

                using (PerfProfile.Measure(PerfProfile.Region.SeedLayer))
                {
                    for (int masterIdx = 0; masterIdx < _activeRecipeByMaster.Length; masterIdx++)
                    {
                        var recipe = _activeRecipeByMaster[masterIdx];
                        if (recipe == null) continue;

                        var needMask = reqMasksMaster[masterIdx];
                        if (!MaskContainsAll(haveMask, null, needMask))
                        {
                            continue;
                        }

                        if (!IsRecipeAffordable(physicalStacks, snap.DronePotential, recipe, canScrapDrones, null))
                        {
                            continue;
                        }

                        (Ingredient[] phys, DroneRequirement[] droneReqs, TradeRequirement[] trades) costs;
                        using (PerfProfile.Measure(PerfProfile.Region.CalculateSplitCosts))
                        {
                            costs = CalculateSplitCosts(null, recipe, canScrapDrones, physicalStacks, snap.AllScrapCandidates);
                        }

                        var (phys, droneReqs, trades) = costs;
                        if (phys == null)
                        {
                            continue;
                        }

                        bool dominated;
                        (int scrapIdx, int need)[] dn;
                        using (PerfProfile.Measure(PerfProfile.Region.IsChainDominated))
                        {
                            dominated = IsChainDominated(recipe.ResultIndex, recipe.ResultCount, phys, droneReqs, trades, out dn);
                        }
                        if (dominated)
                        {
                            continue;
                        }

                        RecipeChain chain;
                        using (PerfProfile.Measure(PerfProfile.Region.NewChainAlloc))
                        {
                            chain = new RecipeChain(recipe, phys, droneReqs, trades);
                        }

                        using (PerfProfile.Measure(Region.AddChainToResults))
                        {
                            AddChainToResults(discovered, queue, chain, dn);
                        }
                    }
                }

                using (PerfProfile.Measure(PerfProfile.Region.BfsExpand))
                {
                    for (int d = 2; d <= _maxDepth; d++)
                    {
                        int layerSize = queue.Count;
                        if (layerSize == 0) break;

                        for (int i = 0; i < layerSize; i++)
                        {
                            var existingChain = queue.Dequeue();
                            DebugLog.CraftTrace(_log, $"[Planner][BFS] POP: chain={ChainSummary(existingChain)}");

                            using (PerfProfile.Measure(PerfProfile.Region.CandidateBuild))
                            {
                                BuildCandidatesForChain(existingChain, masterCount, consumersByIngredientMaster, producersByResultMaster, haveMask);
                            }

                            using (PerfProfile.Measure(PerfProfile.Region.ExpandTrades))
                            {
                                ExpandTradesForDeficits(snap, existingChain, _defKeys, _defCount, discovered, queue);
                            }

                            using (PerfProfile.Measure(Region.CandidateLoopOverhead))
                            {
                                for (int c = 0; c < _candidateCount; c++)
                                {
                                    int masterIdx = _candidatesScratch[c];
                                    var nextRecipe = _activeRecipeByMaster[masterIdx];
                                    var needMask = reqMasksMaster[masterIdx];

                                    if (RecipeProvider.IsDoubleIngredientRecipe[masterIdx])
                                    {
                                        int a = RecipeProvider.IngAByRecipe[masterIdx];
                                        if (a != -1)
                                        {
                                            int net = existingChain?.GetNetSurplusFor(a) ?? 0;
                                            int needed = Math.Max(0, 2 - Math.Max(0, net));

                                            if (needed > 0)
                                            {
                                                int snapPhys = ((uint)a < (uint)physicalStacks.Length) ? physicalStacks[a] : 0;
                                                int snapDrone = 0;
                                                var dronePotential = snap.DronePotential;

                                                if (canScrapDrones && dronePotential != null && (uint)a < (uint)dronePotential.Length)
                                                {
                                                    snapDrone = dronePotential[a];

                                                }

                                                if (snapPhys + snapDrone < needed)
                                                {
                                                    DebugLog.CraftTrace(_log, $"[Planner] CULLED (AA Count Check): chain={ChainSummary(existingChain)} | candidate={GetItemName(nextRecipe.ResultIndex)}");
                                                    continue;
                                                }
                                            }
                                        }
                                    }

                                    (Ingredient[] phys, DroneRequirement[] droneReqs, TradeRequirement[] trades) costs;
                                    using (PerfProfile.Measure(PerfProfile.Region.CalculateSplitCosts))
                                    {
                                        costs = CalculateSplitCosts(existingChain, nextRecipe, canScrapDrones, physicalStacks, snap.AllScrapCandidates);
                                    }

                                    var (phys, droneReqs, trades) = costs;
                                    if (phys == null)
                                    {
                                        TraceChainDrop("Expand", "CalculateSplitCosts failed (phys==null)", existingChain, nextRecipe);
                                        continue;
                                    }

                                    bool isBridgeCand = IsBridgeCandidate(masterIdx);
                                    bool dominated = false;
                                    (int scrapIdx, int need)[] dn;
                                    if (!isBridgeCand)
                                    {
                                        using (PerfProfile.Measure(PerfProfile.Region.IsChainDominated))
                                        {
                                            dominated = IsChainDominated(nextRecipe.ResultIndex, nextRecipe.ResultCount, phys, droneReqs, trades, out dn);
                                        }
                                        if (dominated)
                                        {
                                            TraceChainDrop("Expand", "Dominated", existingChain, nextRecipe);
                                            continue;
                                        }
                                    }
                                    else
                                    {
                                        dn = CollapseDroneNeedsByScrapIndex(droneReqs);
                                    }

                                    RecipeChain newChain;
                                    using (PerfProfile.Measure(PerfProfile.Region.NewChainAlloc))
                                    {
                                        newChain = new RecipeChain(existingChain, nextRecipe, phys, droneReqs, trades, isBridgeCand);
                                    }

                                    using (PerfProfile.Measure(Region.AddChainToResults))
                                    {
                                        AddChainToResults(discovered, queue, newChain, dn);
                                    }
                                }
                            }
                        }
                    }
                }

                using (PerfProfile.Measure(PerfProfile.Region.FinalEntryBuild))
                {
                    _entryCache = discovered.Select(kvp =>
                    {
                        var all = kvp.Value;
                        int total = all.Count;

                        int badResult = 0, badTradeFirst = 0, badSurplus = 0;

                        var validChains = all
                            .Where(c =>
                            {
                                bool ok = c.ResultIndex == kvp.Key;
                                if (!ok) badResult++;
                                return ok;
                            })
                            .Where(c =>
                            {
                                bool ok = !(c.Depth == 1 && c.FirstStep is TradeRecipe);
                                if (!ok) badTradeFirst++;
                                return ok;
                            })
                            .Where(c =>
                            {
                                bool ok = (c.ResultSurplus == c.ResultCount);
                                if (!ok) badSurplus++;
                                return ok;
                            })
                            .OrderBy(c => c.DroneCostSparse.Length)
                            .ThenBy(c => c.Depth)
                            .ToList();

                        if (validChains.Count == 0)
                        {
                            DebugLog.CraftTrace(_log, $"[Planner][Final] DROP ENTRY: {GetItemName(kvp.Key)} | total={total} badResult={badResult} badTradeFirst={badTradeFirst} badSurplus={badSurplus}");
                            return null;
                        }

                        DebugLog.CraftTrace(_log, $"[Planner][Final] KEEP ENTRY: {GetItemName(kvp.Key)} | total={total} kept={validChains.Count} badResult={badResult} badTradeFirst={badTradeFirst} badSurplus={badSurplus}");

                        return new CraftableEntry
                        {
                            ResultIndex = kvp.Key,
                            ResultCount = validChains[0].ResultCount,
                            MinDepth = validChains[0].Depth,
                            Chains = validChains
                        };
                    }).Where(e => e != null).ToDictionary(e => e.ResultIndex);

                    var finalResults = _entryCache.Values.ToList();
                    finalResults.Sort(TierManager.CompareCraftableEntries);
                    sw.Stop();
                    _log.LogDebug($"[Planner] Rebuild complete: {sw.ElapsedMilliseconds}ms for {finalResults.Count} entries.");
                }
                RefreshVisualOverridesAndEmit(snap);
            }
            PerfProfile.LogSummary(_log);
        }

        // ------------- Level-Order Adjacency-based Candidacy -----------------
        private void BuildCandidatesForChain(
            RecipeChain chain,
            int masterCount,
            int[][] consumersByIngredientMaster,
            int[][] producersByResultMaster,
            ulong[] haveMask)
        {
            // ---- stamps ----
            _candidateBridgeStamp++;
            if (_candidateBridgeStamp == int.MaxValue)
            {
                Array.Clear(_candidateBridgeMark, 0, _candidateBridgeMark.Length);
                _candidateBridgeStamp = 1;
            }

            _candidateStamp++;
            if (_candidateStamp == int.MaxValue)
            {
                Array.Clear(_candidateMark, 0, _candidateMark.Length);
                _candidateStamp = 1;
            }

            _missingStamp++;
            if (_missingStamp == int.MaxValue)
            {
                Array.Clear(_missingMark, 0, _missingMark.Length);
                _missingStamp = 1;
            }

            _candidateCount = 0;
            _missingCount = 0;

            BuildProfileScratch(chain);

            Array.Clear(_surplusMaskScratch, 0, _surplusMaskScratch.Length);

            for (int p = 0; p < _posCount; p++)
            {
                UpdateMaskBit(_surplusMaskScratch, _posKeys[p], true);
            }

            ulong[] surplusMask = _surplusMaskScratch;

            // -------------------------
            for (int p = 0; p < _posCount; p++)
            {
                int haveIdx = _posKeys[p];
                if ((uint)haveIdx >= (uint)consumersByIngredientMaster.Length) continue;

                var consumers = consumersByIngredientMaster[haveIdx];
                if (consumers == null) continue;

                for (int i = 0; i < consumers.Length; i++)
                {
                    int consumerMaster = consumers[i];
                    if ((uint)consumerMaster >= (uint)masterCount) continue;
                    if (!_activeMasterRecipe[consumerMaster]) continue;

                    var next = _activeRecipeByMaster[consumerMaster];
                    if (next == null) continue;

                    var needMask = RecipeProvider.ReqMasks[consumerMaster];
                    if (MaskContainsAll(haveMask, surplusMask, needMask))
                    {
                        AddCandidate(consumerMaster, masterCount, isBridge: false);
                        continue;
                    }

                    int a = RecipeProvider.IngAByRecipe[consumerMaster];
                    int b = RecipeProvider.IngBByRecipe[consumerMaster];

                    if (a < 0 || RecipeProvider.IsDoubleIngredientRecipe[consumerMaster]) continue;

                    int other = -1;
                    if (a == haveIdx) other = b;
                    else if (b == haveIdx) other = a;
                    else continue;

                    if (other < 0) continue;

                    bool otherAvailable =
                        MaskHasBit(haveMask, other) ||
                        (chain.GetNetSurplusFor(other) > 0);

                    if (!otherAvailable)
                    {
                        AddMissing(other);
                    }
                }
            }

            bool canBridge = chain.Depth < (_maxDepth - 1);
            if (canBridge)
            {
                int bridgeItems = 0;

                for (int i = 0; i < _missingCount; i++)
                {
                    if (bridgeItems++ >= CookBook.MaxBridgeItemsPerChain.Value) break;

                    int missingIdx = _missingScratch[i];
                    if ((uint)missingIdx >= (uint)producersByResultMaster.Length) continue;

                    var producers = producersByResultMaster[missingIdx];
                    if (producers == null || producers.Length == 0) continue;

                    for (int j = 0; j < producers.Length; j++)
                    {
                        int prodMaster = producers[j];

                        if ((uint)prodMaster >= (uint)masterCount) continue;
                        if (!_activeMasterRecipe[prodMaster]) continue;

                        var needMask = RecipeProvider.ReqMasks[prodMaster];
                        if (!MaskContainsAll(haveMask, surplusMask, needMask)) continue;
                        AddCandidate(prodMaster, masterCount, isBridge: true); break;
                    }
                }
            }
        }

        private void AddCandidate(int masterIdx, int masterCount, bool isBridge)
        {
            if ((uint)masterIdx >= (uint)masterCount) return;
            if (!_activeMasterRecipe[masterIdx]) return;

            int stamp = _candidateStamp;

            if (_candidateMark[masterIdx] == stamp)
            {
                if (isBridge)
                    _candidateBridgeMark[masterIdx] = _candidateBridgeStamp;

                return;
            }

            _candidateMark[masterIdx] = stamp;

            if (isBridge)
                _candidateBridgeMark[masterIdx] = _candidateBridgeStamp;
            else
                _candidateBridgeMark[masterIdx] = 0;

            if ((uint)_candidateCount < (uint)_candidatesScratch.Length)
                _candidatesScratch[_candidateCount++] = masterIdx;
        }

        private bool IsBridgeCandidate(int masterIdx)
        {
            return _candidateBridgeMark[masterIdx] == _candidateBridgeStamp;
        }


        private void AddMissing(int idx)
        {
            if ((uint)idx >= (uint)_totalDefCount) return;

            int stamp = _missingStamp;
            if (_missingMark[idx] == stamp) return;

            _missingMark[idx] = stamp;
            if ((uint)_missingCount < (uint)_missingScratch.Length)
                _missingScratch[_missingCount++] = idx;
        }

        /// <summary>
        /// _posKeys[0.._posCount) are indices with net > 0
        /// _defKeys[0.._defCount) are indices with net < 0
        /// </summary>
        private void BuildProfileScratch(RecipeChain chain)
        {
            _profileCount = 0;

            for (var n = chain; n != null; n = n.Parent)
            {
                var d = n.Delta;

                if (d.I0 >= 0 && d.V0 != 0) AddToProfile(d.I0, d.V0);
                if (d.I1 >= 0 && d.V1 != 0) AddToProfile(d.I1, d.V1);
                if (d.I2 >= 0 && d.V2 != 0) AddToProfile(d.I2, d.V2);
            }

            _posCount = 0;
            _defCount = 0;

            for (int i = 0; i < _profileCount; i++)
            {
                int idx = _profileKeys[i];
                int val = _profileVals[i];

                if (val > 0) _posKeys[_posCount++] = idx;
                else if (val < 0) _defKeys[_defCount++] = idx;
            }
        }

        private void AddToProfile(int idx, int delta)
        {
            for (int i = 0; i < _profileCount; i++)
            {
                if (_profileKeys[i] == idx)
                {
                    _profileVals[i] += delta;
                    return;
                }
            }

            if ((uint)_profileCount >= (uint)_profileKeys.Length)
                return;

            _profileKeys[_profileCount] = idx;
            _profileVals[_profileCount] = delta;
            _profileCount++;
        }

        // --------- Efficiency Gating ------------------
        private bool IsChainInefficient(RecipeChain chain)
        {
            if (chain.IsBridgeIntermediate)
                return false;

            int inputWeight = GetWeightedCost(chain);
            int value = chain.ResultSurplus * GetItemWeight(chain.ResultIndex);

            if (value <= 0) return true;
            return inputWeight > value * 2;
        }

        // ------------------- Dominance Gating ------------------
        /// <summary>
        /// A dominates B if:
        /// For every phys key: A.count <= B.count && for every trade key: A.trades <= B.trades && for every drone scrap key: A.need <= B.need, && >=1 dimensions strictly smaller || B has extra nonzero keys not in A
        /// </summary>
        private bool IsChainDominated(
            int resultIdx,
            int resultCount,
            Ingredient[] physSortedByIdx,
            DroneRequirement[] droneReqs,
            TradeRequirement[] tradesSorted,
            out (int scrapIdx, int need)[] droneNeedsSorted)
        {
            long outKey = OutputKey(resultIdx, resultCount);

            physSortedByIdx ??= Array.Empty<Ingredient>();
            tradesSorted ??= Array.Empty<TradeRequirement>();

            droneNeedsSorted = CollapseDroneNeedsByScrapIndex(droneReqs);

            // ---------- exact-keyset ----------
            var shape = BuildCostShapeKey_FromCosts(physSortedByIdx, droneNeedsSorted, tradesSorted);

            if (!_bestByOutputAndShape.TryGetValue(outKey, out var byShape))
            {
                byShape = new Dictionary<CostShapeKey, BestCostRecord>();
                _bestByOutputAndShape[outKey] = byShape;
            }

            if (byShape.TryGetValue(shape, out var bestSameShape))
            {
                if (IsStrictlyWorse(bestSameShape.Phys, bestSameShape.Drone, bestSameShape.Trades,
                          physSortedByIdx, droneNeedsSorted, tradesSorted))
                    return true;

                if (IsStrictlyWorse(physSortedByIdx, droneNeedsSorted, tradesSorted,
                          bestSameShape.Phys, bestSameShape.Drone, bestSameShape.Trades))
                    byShape[shape] = new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted);
            }
            else
            {
                byShape[shape] = new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted);
            }

            // ---------- cross-keyset ----------
            if (!_frontierBucketsByOutput.TryGetValue(outKey, out var buckets))
            {
                buckets = new Dictionary<int, List<BestCostRecord>>();
                _frontierBucketsByOutput[outKey] = buckets;
            }

            int pLen = physSortedByIdx.Length;
            int dLen = droneNeedsSorted.Length;
            int tLen = tradesSorted.Length;

            for (int bp = 0; bp <= pLen; bp++)
            {
                for (int bd = 0; bd <= dLen; bd++)
                {
                    for (int bt = 0; bt <= tLen; bt++)
                    {
                        int key = EncodeLens(bp, bd, bt);
                        if (!buckets.TryGetValue(key, out var list) || list == null) continue;

                        for (int i = 0; i < list.Count; i++)
                        {
                            var ex = list[i];
                            if (Dominates(ex.Phys, ex.Drone, ex.Trades, physSortedByIdx, droneNeedsSorted, tradesSorted))
                                return true;
                        }
                    }
                }
            }

            _bucketKeysScratch.Clear();

            for (int bp = pLen; bp <= _pMaxLen; bp++)
            {
                for (int bd = dLen; bd <= _dMaxLen; bd++)
                {
                    for (int bt = tLen; bt <= _tMaxLen; bt++)
                    {
                        int key = EncodeLens(bp, bd, bt);
                        if (!buckets.TryGetValue(key, out var list) || list == null) continue;

                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var ex = list[i];
                            if (Dominates(physSortedByIdx, droneNeedsSorted, tradesSorted, ex.Phys, ex.Drone, ex.Trades))
                                list.RemoveAt(i);
                        }

                        if (list.Count == 0)
                            _bucketKeysScratch.Add(key);
                    }
                }
            }


            for (int i = 0; i < _bucketKeysScratch.Count; i++)
                buckets.Remove(_bucketKeysScratch[i]);

            // Insert candidate
            int myBucket = EncodeLens(pLen, dLen, tLen);
            if (!buckets.TryGetValue(myBucket, out var myList))
            {
                myList = new List<BestCostRecord>(4);
                buckets[myBucket] = myList;
            }

            myList.Add(new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted));
            return false;
        }

        private bool IsChainDominated(
            int resultIdx,
            int resultCount,
            Ingredient[] physSortedByIdx,
            (int scrapIdx, int need)[] droneNeedsSorted,
            TradeRequirement[] tradesSorted)
        {
            long outKey = OutputKey(resultIdx, resultCount);

            physSortedByIdx ??= Array.Empty<Ingredient>();
            tradesSorted ??= Array.Empty<TradeRequirement>();

            // ---------- exact-keyset ----------
            var shape = BuildCostShapeKey_FromCosts(physSortedByIdx, droneNeedsSorted, tradesSorted);

            if (!_bestByOutputAndShape.TryGetValue(outKey, out var byShape))
            {
                byShape = new Dictionary<CostShapeKey, BestCostRecord>();
                _bestByOutputAndShape[outKey] = byShape;
            }

            if (byShape.TryGetValue(shape, out var bestSameShape))
            {
                if (IsStrictlyWorse(bestSameShape.Phys, bestSameShape.Drone, bestSameShape.Trades,
                          physSortedByIdx, droneNeedsSorted, tradesSorted))
                    return true;

                if (IsStrictlyWorse(physSortedByIdx, droneNeedsSorted, tradesSorted,
                          bestSameShape.Phys, bestSameShape.Drone, bestSameShape.Trades))
                    byShape[shape] = new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted);
            }
            else
            {
                byShape[shape] = new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted);
            }

            // ---------- cross-keyset ----------
            if (!_frontierBucketsByOutput.TryGetValue(outKey, out var buckets))
            {
                buckets = new Dictionary<int, List<BestCostRecord>>();
                _frontierBucketsByOutput[outKey] = buckets;
            }

            int pLen = physSortedByIdx.Length;
            int dLen = droneNeedsSorted.Length;
            int tLen = tradesSorted.Length;

            for (int bp = 0; bp <= pLen; bp++)
            {
                for (int bd = 0; bd <= dLen; bd++)
                {
                    for (int bt = 0; bt <= tLen; bt++)
                    {
                        int key = EncodeLens(bp, bd, bt);
                        if (!buckets.TryGetValue(key, out var list) || list == null) continue;

                        for (int i = 0; i < list.Count; i++)
                        {
                            var ex = list[i];
                            if (Dominates(ex.Phys, ex.Drone, ex.Trades, physSortedByIdx, droneNeedsSorted, tradesSorted))
                                return true;
                        }
                    }
                }
            }

            _bucketKeysScratch.Clear();

            for (int bp = pLen; bp <= _pMaxLen; bp++)
            {
                for (int bd = dLen; bd <= _dMaxLen; bd++)
                {
                    for (int bt = tLen; bt <= _tMaxLen; bt++)
                    {
                        int key = EncodeLens(bp, bd, bt);
                        if (!buckets.TryGetValue(key, out var list) || list == null) continue;

                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            var ex = list[i];
                            if (Dominates(physSortedByIdx, droneNeedsSorted, tradesSorted, ex.Phys, ex.Drone, ex.Trades))
                                list.RemoveAt(i);
                        }

                        if (list.Count == 0)
                            _bucketKeysScratch.Add(key);
                    }
                }
            }


            for (int i = 0; i < _bucketKeysScratch.Count; i++)
                buckets.Remove(_bucketKeysScratch[i]);

            // Insert candidate
            int myBucket = EncodeLens(pLen, dLen, tLen);
            if (!buckets.TryGetValue(myBucket, out var myList))
            {
                myList = new List<BestCostRecord>(4);
                buckets[myBucket] = myList;
            }

            myList.Add(new BestCostRecord(physSortedByIdx, droneNeedsSorted, tradesSorted));
            return false;
        }

        private static int EncodeLens(int physLen, int droneLen, int tradeLen)
        {
            return (physLen & 1023) | ((droneLen & 1023) << 10) | ((tradeLen & 1023) << 20);
        }

        private static bool IsStrictlyWorse(
            Ingredient[] bestPhys, (int scrapIdx, int need)[] bestDrone, TradeRequirement[] bestTrades,
            Ingredient[] candPhys, (int scrapIdx, int need)[] candDrone, TradeRequirement[] candTrades)
        {
            bool strictlyHigher = false;

            if (!StrictlyWorsePhys(bestPhys, candPhys, ref strictlyHigher)) return false;
            if (!StrictlyWorseDrone(bestDrone, candDrone, ref strictlyHigher)) return false;
            if (!StrictlyWorseTrades(bestTrades, candTrades, ref strictlyHigher)) return false;

            return strictlyHigher;
        }

        private static bool StrictlyWorsePhys(Ingredient[] best, Ingredient[] cand, ref bool strictlyHigher)
        {
            int i = 0, j = 0;

            while (i < best.Length || j < cand.Length)
            {
                int bk = (i < best.Length) ? best[i].UnifiedIndex : int.MaxValue;
                int ck = (j < cand.Length) ? cand[j].UnifiedIndex : int.MaxValue;

                if (bk == ck)
                {
                    int bq = best[i].Count;
                    int cq = cand[j].Count;

                    if (cq < bq) return false;
                    if (cq > bq) strictlyHigher = true;

                    i++; j++;
                }
                else if (bk < ck)
                {
                    int bq = best[i].Count;
                    if (0 < bq) return false;
                    i++;
                }
                else
                {
                    int cq = cand[j].Count;
                    if (cq > 0) strictlyHigher = true;
                    j++;
                }
            }

            return true;
        }

        private static bool StrictlyWorseDrone((int scrapIdx, int need)[] best, (int scrapIdx, int need)[] cand, ref bool strictlyHigher)
        {
            int i = 0, j = 0;

            while (i < best.Length || j < cand.Length)
            {
                int bk = (i < best.Length) ? best[i].scrapIdx : int.MaxValue;
                int ck = (j < cand.Length) ? cand[j].scrapIdx : int.MaxValue;

                if (bk == ck)
                {
                    int bq = best[i].need;
                    int cq = cand[j].need;

                    if (cq < bq) return false;
                    if (cq > bq) strictlyHigher = true;

                    i++; j++;
                }
                else if (bk < ck)
                {
                    int bq = best[i].need;
                    if (0 < bq) return false;
                    i++;
                }
                else
                {
                    int cq = cand[j].need;
                    if (cq > 0) strictlyHigher = true;
                    j++;
                }
            }

            return true;
        }

        private static bool StrictlyWorseTrades(TradeRequirement[] best, TradeRequirement[] cand, ref bool strictlyHigher)
        {
            int i = 0, j = 0;

            while (i < best.Length || j < cand.Length)
            {
                long bd = (i < best.Length && best[i].Donor) ? best[i].Donor.netId.Value : long.MaxValue;
                int bi = (i < best.Length) ? best[i].UnifiedIndex : int.MaxValue;

                long cd = (j < cand.Length && cand[j].Donor) ? cand[j].Donor.netId.Value : long.MaxValue;
                int ci = (j < cand.Length) ? cand[j].UnifiedIndex : int.MaxValue;

                int cmp = bd.CompareTo(cd);
                if (cmp == 0) cmp = bi.CompareTo(ci);

                if (cmp == 0)
                {
                    int bq = best[i].TradesRequired;
                    int cq = cand[j].TradesRequired;

                    if (cq < bq) return false;
                    if (cq > bq) strictlyHigher = true;

                    i++; j++;
                }
                else if (cmp < 0)
                {
                    // key exists in best only
                    int bq = best[i].TradesRequired;
                    if (0 < bq) return false;
                    i++;
                }
                else
                {
                    // key exists in cand only
                    int cq = cand[j].TradesRequired;
                    if (cq > 0) strictlyHigher = true;
                    j++;
                }
            }

            return true;
        }

        private static bool Dominates(
          Ingredient[] aPhys, (int scrapIdx, int need)[] aDrone, TradeRequirement[] aTrades,
          Ingredient[] bPhys, (int scrapIdx, int need)[] bDrone, TradeRequirement[] bTrades)
        {
            aPhys ??= Array.Empty<Ingredient>();
            bPhys ??= Array.Empty<Ingredient>();
            aDrone ??= Array.Empty<(int, int)>();
            bDrone ??= Array.Empty<(int, int)>();
            aTrades ??= Array.Empty<TradeRequirement>();
            bTrades ??= Array.Empty<TradeRequirement>();

            bool strict = false;

            if (!DominatesPhys(aPhys, bPhys, ref strict)) return false;
            if (!DominatesDrone(aDrone, bDrone, ref strict)) return false;
            if (!DominatesTrades(aTrades, bTrades, ref strict)) return false;

            return strict;
        }

        private static bool DominatesPhys(Ingredient[] a, Ingredient[] b, ref bool strict)
        {
            int i = 0, j = 0;

            while (i < a.Length || j < b.Length)
            {
                int ai = (i < a.Length) ? a[i].UnifiedIndex : int.MaxValue;
                int bi = (j < b.Length) ? b[j].UnifiedIndex : int.MaxValue;

                if (ai == bi)
                {
                    int av = a[i].Count;
                    int bv = b[j].Count;

                    if (av > bv) return false;
                    if (av < bv) strict = true;

                    i++; j++;
                }
                else if (ai < bi)
                {
                    // Key exists in A but not in B
                    if (a[i].Count > 0) return false;
                    i++;
                }
                else // bi < ai
                {
                    // Key exists in B but not in A
                    if (b[j].Count > 0) strict = true;
                    j++;
                }
            }

            return true;
        }

        private static bool DominatesDrone((int scrapIdx, int need)[] a, (int scrapIdx, int need)[] b, ref bool strict)
        {
            int i = 0, j = 0;

            while (i < a.Length || j < b.Length)
            {
                int ak = (i < a.Length) ? a[i].scrapIdx : int.MaxValue;
                int bk = (j < b.Length) ? b[j].scrapIdx : int.MaxValue;

                if (ak == bk)
                {
                    int av = a[i].need;
                    int bv = b[j].need;

                    if (av > bv) return false;
                    if (av < bv) strict = true;

                    i++; j++;
                }
                else if (ak < bk)
                {
                    // A has extra key
                    if (a[i].need > 0) return false;
                    i++;
                }
                else
                {
                    // B has extra key
                    if (b[j].need > 0) strict = true;
                    j++;
                }
            }

            return true;
        }

        private static bool DominatesTrades(TradeRequirement[] a, TradeRequirement[] b, ref bool strict)
        {
            int i = 0, j = 0;

            while (i < a.Length || j < b.Length)
            {
                (long donorId, int item) aKey = (long.MaxValue, int.MaxValue);
                (long donorId, int item) bKey = (long.MaxValue, int.MaxValue);

                if (i < a.Length)
                    aKey = (a[i].Donor ? a[i].Donor.netId.Value : 0L, a[i].UnifiedIndex);

                if (j < b.Length)
                    bKey = (b[j].Donor ? b[j].Donor.netId.Value : 0L, b[j].UnifiedIndex);

                int cmp = aKey.donorId.CompareTo(bKey.donorId);
                if (cmp == 0) cmp = aKey.item.CompareTo(bKey.item);

                if (cmp == 0)
                {
                    int av = a[i].TradesRequired;
                    int bv = b[j].TradesRequired;

                    if (av > bv) return false;
                    if (av < bv) strict = true;

                    i++; j++;
                }
                else if (cmp < 0)
                {
                    // exists in A but not B
                    if (a[i].TradesRequired > 0) return false;
                    i++;
                }
                else
                {
                    // exists in B but not A
                    if (b[j].TradesRequired > 0) strict = true;
                    j++;
                }
            }
            return true;
        }

        private static int Fold64To32(long x)
        {
            unchecked { return (int)x ^ (int)(x >> 32); }
        }

        private static CostShapeKey BuildCostShapeKey_FromCosts(
            Ingredient[] physSortedByIdx,
            (int scrapIdx, int need)[] droneNeedsSorted,
            TradeRequirement[] tradesSorted)
        {
            unchecked
            {
                int h1 = 17;
                int h2 = 23;

                int pl = physSortedByIdx?.Length ?? 0;
                for (int i = 0; i < pl; i++)
                {
                    if (physSortedByIdx[i].Count <= 0) continue;

                    int k = physSortedByIdx[i].UnifiedIndex;

                    h1 = (h1 * 31) ^ k;
                    h2 = (h2 * 1315423911) ^ k;
                }

                int dl = droneNeedsSorted?.Length ?? 0;
                for (int i = 0; i < dl; i++)
                {
                    int k = droneNeedsSorted[i].scrapIdx;

                    h1 = (h1 * 31) ^ k;
                    int mix = unchecked(k + (int)0x9E3779B9);
                    h2 = (h2 * 1315423911) ^ mix;
                }

                int tl = tradesSorted?.Length ?? 0;
                for (int i = 0; i < tl; i++)
                {
                    if (tradesSorted[i].TradesRequired <= 0) continue;

                    int donor32 = Fold64To32(tradesSorted[i].Donor.netId.Value);

                    int item = tradesSorted[i].UnifiedIndex;

                    h1 = (h1 * 31) ^ donor32;
                    h1 = (h1 * 31) ^ item;

                    // Different combine path for h2
                    h2 = (h2 * 1315423911) ^ donor32;
                    h2 = (h2 * 1315423911) ^ item;
                }

                return new CostShapeKey(h1, h2, pl, dl, tl);
            }
        }

        private static long OutputKey(int resultIdx, int resultCount)
        {
            unchecked
            {
                return ((long)resultIdx << 32) ^ (uint)resultCount;
            }
        }

        private (int scrapIdx, int need)[] CollapseDroneNeedsByScrapIndex(DroneRequirement[] drones)
        {
            if (drones == null || drones.Length == 0)
                return Array.Empty<(int, int)>();

            int stamp = ++_droneStamp;
            if (_droneStamp == int.MaxValue)
            {
                Array.Clear(_droneMark, 0, _droneMark.Length);
                _droneStamp = 1;
                stamp = 1;
            }

            _droneTouchedCount = 0;

            for (int i = 0; i < drones.Length; i++)
            {
                int cnt = drones[i].Count;
                if (cnt <= 0) continue;

                int s = drones[i].ScrapIndex;
                if ((uint)s >= (uint)_droneNeedScratch.Length) continue;

                if (_droneMark[s] != stamp)
                {
                    _droneMark[s] = stamp;
                    _droneNeedScratch[s] = 0;

                    if (_droneTouchedCount >= _droneTouched.Length) // can't happen, sized to worst case. 
                        return Array.Empty<(int, int)>();

                    _droneTouched[_droneTouchedCount++] = s;
                }

                _droneNeedScratch[s] += cnt;
            }

            if (_droneTouchedCount == 0)
                return Array.Empty<(int, int)>();

            var arr = new (int scrapIdx, int need)[_droneTouchedCount];
            for (int i = 0; i < _droneTouchedCount; i++)
            {
                int s = _droneTouched[i];
                arr[i] = (s, _droneNeedScratch[s]);
            }

            // Canonical order by scrapIdx
            if (_droneTouchedCount > 1)
                Array.Sort(arr, _cmpScrapIdx);
            return arr;
        }

        private static readonly Comparison<(int scrapIdx, int need)> _cmpScrapIdx =
      (a, b) => a.scrapIdx.CompareTo(b.scrapIdx);

        // ---------------- Affordability ------------------
        private static bool IsRecipeAffordable(
            int[] physicalStacks,
            int[] dronePotential,
            ChefRecipe recipe,
            bool scrapperPresent,
            RecipeChain existingChain)
        {
            if (recipe == null) return true;

            bool ok = true;

            ForEachRequirement(recipe, (idx, needCount) =>
            {
                if (!ok) return;

                int surplus = (existingChain != null) ? existingChain.GetNetSurplusFor(idx) : 0;
                int availableFromChain = Math.Max(0, surplus);

                int netDeficit = needCount - availableFromChain;
                if (netDeficit <= 0) return;

                int physical = ((uint)idx < (uint)(physicalStacks?.Length ?? 0)) ? physicalStacks[idx] : 0;

                int potential = 0;
                if (scrapperPresent && dronePotential != null && (uint)idx < (uint)dronePotential.Length)
                    potential = dronePotential[idx];

                if (physical + potential < netDeficit)
                    ok = false;
            });

            return ok;
        }

        private bool IsTransformedItemRelevant(int unifiedIndex)
        {
            if (unifiedIndex >= ItemCatalog.itemCount) return false;
            ItemIndex itemIdx = (ItemIndex)unifiedIndex;

            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                if (info.transformedItem == itemIdx && _entryCache.ContainsKey((int)info.originalItem))
                    return true;
            }
            return false;
        }

        private static bool MaskContainsAll(ulong[] have, ulong[] surplus, ulong[] need)
        {
            int needLen = need?.Length ?? 0;
            int haveLen = have?.Length ?? 0;
            int surLen = surplus?.Length ?? 0;

            for (int i = 0; i < needLen; i++)
            {
                ulong h = (i < haveLen) ? have[i] : 0UL;
                ulong s = (i < surLen) ? surplus[i] : 0UL;
                ulong hs = h | s;

                if ((hs & need[i]) != need[i])
                    return false;
            }
            return true;
        }


        // -------------- Trade Injection -----------------
        private void ExpandTradesForDeficits(
            in InventorySnapshot snap,
            RecipeChain chain,
            int[] deficitKeys,
            int deficitKeyCount,
            Dictionary<int, List<RecipeChain>> discovered,
            Queue<RecipeChain> queue)
        {
            if (!isPoolingEnabled) return;
            if (chain == null) return;
            if (deficitKeyCount <= 0) return;

            var alliedSnapshots = snap.AlliedPhysicalStacks;
            var remainingTrades = snap.TradesRemaining;

            if (alliedSnapshots.Count == 0) return;
            if (remainingTrades.Count == 0) return;

            var collapsedDroneNeedsForChain = CollapseDroneNeedsByScrapIndex(chain.DroneCostSparse);

            const int MaxTradeChildrenPerChain = 32;
            int added = 0;

            for (int i = 0; i < deficitKeyCount; i++)
            {
                int idx = deficitKeys[i];
                if ((uint)idx >= (uint)_totalDefCount) continue;

                int net = chain.GetNetSurplusFor(idx);
                int needed = Math.Max(0, -net);

                if (needed != 1) continue;

                foreach (var ally in alliedSnapshots)
                {
                    var donor = ally.Key;
                    int[] donorInv = ally.Value;
                    if (donorInv == null) continue;
                    if ((uint)idx >= (uint)donorInv.Length) continue;

                    if (!remainingTrades.TryGetValue(donor, out int tradesLeft) || tradesLeft <= 0) continue;

                    int donorCount = donorInv[idx];
                    if (donorCount <= 0) continue;

                    int alreadyTradedThisItem = GetAlreadyTradedCount(chain.AlliedTradeSparse, donor, idx);
                    int remainingInDonor = donorCount - alreadyTradedThisItem;
                    if (remainingInDonor <= 0) continue;

                    var existingTradeReqs = chain.AlliedTradeSparse;
                    for (int t = 0; t < existingTradeReqs.Length; t++)
                    {
                        if (existingTradeReqs[t].Donor == donor)
                        {
                            tradesLeft -= existingTradeReqs[t].TradesRequired;
                        }
                        if (tradesLeft <= 0) break;
                    }

                    if (tradesLeft <= 0) continue;

                    var updatedTradeReqs = SortTradesCanonical(UpdateTradeRequirements(existingTradeReqs, donor, idx));

                    ulong sig = HashCosts(chain.PhysicalCostSparse, collapsedDroneNeedsForChain, updatedTradeReqs);
                    if (_seenChainSigByResult.TryGetValue(idx, out var seen) && seen.Contains(sig))
                        continue;

                    bool dominated;
                    using (PerfProfile.Measure(Region.IsChainDominated))
                    {
                        dominated = IsChainDominated(idx, 1,
                            chain.PhysicalCostSparse,
                            collapsedDroneNeedsForChain,
                            updatedTradeReqs);
                    }
                    if (dominated) continue;

                    var trade = new TradeRecipe(donor, idx);
                    var newChain = new RecipeChain(chain, trade, chain.PhysicalCostSparse, chain.DroneCostSparse, updatedTradeReqs, isIntermediate: false);

                    using (PerfProfile.Measure(Region.AddChainToResults))
                    {
                        AddChainToResults(discovered, queue, newChain, collapsedDroneNeedsForChain);
                    }
                    added++;
                    break;
                }

                if (added >= MaxTradeChildrenPerChain) return;
            }
        }

        private TradeRequirement[] UpdateTradeRequirements(TradeRequirement[] existing, NetworkUser donor, int itemIdx)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i].Donor && donor &&
                  existing[i].Donor.netId == donor.netId &&
                  existing[i].UnifiedIndex == itemIdx)
                {
                    var next = (TradeRequirement[])existing.Clone();
                    next[i].TradesRequired++;
                    return next;
                }
            }

            var result = new TradeRequirement[existing.Length + 1];
            Array.Copy(existing, result, existing.Length);
            result[existing.Length] = new TradeRequirement { Donor = donor, UnifiedIndex = itemIdx, TradesRequired = 1 };
            return result;
        }

        private static int GetAlreadyTradedCount(TradeRequirement[] reqs, NetworkUser donor, int idx)
        {
            for (int i = 0; i < reqs.Length; i++)
                if (reqs[i].Donor == donor && reqs[i].UnifiedIndex == idx)
                    return reqs[i].TradesRequired;
            return 0;
        }

        // -------------- Cost Calculation --------------
        private (Ingredient[] phys, DroneRequirement[] drones, TradeRequirement[] trades) CalculateSplitCosts(
          RecipeChain old,
          ChefRecipe next,
          bool canScrapDrones,
          int[] physicalStacks,
          Dictionary<int, List<DroneCandidate>> allScrapCandidates)
        {
            _scrappedDronesThisChain.Clear();
            Array.Clear(_scrapSurplusThisChain, 0, _scrapSurplusThisChain.Length);
            _tempDroneReqList.Clear();

            Ingredient[] phys = old?.PhysicalCostSparse ?? Array.Empty<Ingredient>();

            int add0Idx = -1, add0Cnt = 0;
            int add1Idx = -1, add1Cnt = 0;

            bool failed = false;

            ForEachRequirement(next, (idx, needCount) =>
            {
                if (failed) return;

                int netSurplus = (old == null) ? 0 : old.GetNetSurplusFor(idx);

                int deficit = Math.Max(0, needCount - Math.Max(0, netSurplus));
                if (deficit <= 0) return;

                int alreadySpent = (old == null) ? 0 : Math.Max(0, -netSurplus);

                using (PerfProfile.Measure(PerfProfile.Region.ResolveRequirement))
                {
                    if (!ResolveRequirement(idx, deficit, canScrapDrones, physicalStacks, alreadySpent, allScrapCandidates, _scrappedDronesThisChain, _scrapSurplusThisChain, ref add0Idx, ref add0Cnt, ref add1Idx, ref add1Cnt))
                    {
                        failed = true;
                    }
                }
            });

            if (failed)
                return (null, null, null);

            using (PerfProfile.Measure(PerfProfile.Region.PhysConsolidateAlloc))
            {
                phys = MergePhysAdds(phys, add0Idx, add0Cnt, add1Idx, add1Cnt);
            }

            var trades = ExtractTrades(old, next);
            trades = SortTradesCanonical(trades);
            return (phys, _tempDroneReqList.ToArray(), trades);
        }

        private static Ingredient[] MergePhysAdds(Ingredient[] basePhys, int aIdx, int aCnt, int bIdx, int bCnt)
        {
            if (aCnt <= 0 && bCnt <= 0) return basePhys;

            // Normalize
            if (aCnt > 0 && bCnt > 0)
            {
                if (aIdx == bIdx)
                {
                    aCnt += bCnt;
                    bCnt = 0;
                }
                else if (bIdx < aIdx)
                {
                    (aIdx, bIdx) = (bIdx, aIdx);
                    (aCnt, bCnt) = (bCnt, aCnt);
                }
            }

            int baseLen = basePhys?.Length ?? 0;

            // compute exact new length by checking presence with linear scan
            bool aExists = false, bExists = false;

            if (baseLen > 0)
            {
                if (aCnt > 0) aExists = ContainsIndex(basePhys, aIdx);
                if (bCnt > 0) bExists = ContainsIndex(basePhys, bIdx);
            }

            int newLen = baseLen + (aCnt > 0 && !aExists ? 1 : 0) + (bCnt > 0 && !bExists ? 1 : 0);

            var dst = new Ingredient[newLen];

            int i = 0, j = 0;

            // Merge base
            void emitAdd(int addIdx, int addCnt)
            {
                if (addCnt <= 0) return;

                while (i < baseLen && basePhys[i].UnifiedIndex < addIdx)
                    dst[j++] = basePhys[i++];

                if (i < baseLen && basePhys[i].UnifiedIndex == addIdx)
                {
                    var cur = basePhys[i++];
                    dst[j++] = new Ingredient(cur.UnifiedIndex, cur.Count + addCnt);
                    return;
                }

                dst[j++] = new Ingredient(addIdx, addCnt);
            }

            if (aCnt > 0) emitAdd(aIdx, aCnt);
            if (bCnt > 0) emitAdd(bIdx, bCnt);

            // Copy remaining base
            while (i < baseLen)
                dst[j++] = basePhys[i++];

            return dst;
        }

        private bool ResolveRequirement(
          int unifiedIndex,
          int amountNeeded,
          bool scrapperPresent,
          int[] physicalStacks,
          int alreadySpent,
          Dictionary<int, List<DroneCandidate>> allScrapCandidates,
          HashSet<ulong> scrappedDroneIds,
          int[] scrapSurplusByUnifiedIdx,
          ref int add0Idx, ref int add0Cnt,
          ref int add1Idx, ref int add1Cnt)
        {
            int physOwned = (((uint)unifiedIndex < (uint)physicalStacks.Length) ? physicalStacks[unifiedIndex] : 0) - alreadySpent;
            if (physOwned < 0) physOwned = 0;

            int payWithPhysical = Math.Min(physOwned, amountNeeded);
            int deficit = amountNeeded - payWithPhysical;

            if (payWithPhysical > 0)
                AccumulatePhysCost(unifiedIndex, payWithPhysical, ref add0Idx, ref add0Cnt, ref add1Idx, ref add1Cnt);

            if (deficit <= 0)
                return true;

            if (!scrapperPresent)
                return false;

            if (allScrapCandidates == null || !allScrapCandidates.TryGetValue(unifiedIndex, out var candidates) || candidates == null || candidates.Count == 0)
            {
                return false;
            }

            if ((uint)unifiedIndex < (uint)scrapSurplusByUnifiedIdx.Length)
            {
                int surplus = scrapSurplusByUnifiedIdx[unifiedIndex];
                if (surplus > 0)
                {
                    int use = Math.Min(surplus, deficit);
                    scrapSurplusByUnifiedIdx[unifiedIndex] = surplus - use;
                    deficit -= use;
                    if (deficit <= 0)
                        return true;
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];

                if (scrappedDroneIds != null && scrappedDroneIds.Contains(c.MinionMasterNetId))
                    continue;

                int capacity = DroneUpgradeUtils.GetDroneCountFromUpgradeCount(c.UpgradeCount);
                if (capacity <= 0)
                    continue;

                scrappedDroneIds?.Add(c.MinionMasterNetId);

                _tempDroneReqList.Add(new DroneRequirement
                {
                    Owner = c.Owner,
                    DroneIdx = c.DroneIdx,
                    Count = 1,
                    TotalUpgradeCount = c.UpgradeCount,
                    ScrapIndex = unifiedIndex
                });

                int useNow = Math.Min(deficit, capacity);
                deficit -= useNow;

                int leftover = capacity - useNow;
                if (leftover > 0 && (uint)unifiedIndex < (uint)scrapSurplusByUnifiedIdx.Length)
                    scrapSurplusByUnifiedIdx[unifiedIndex] += leftover;

                if (deficit <= 0)
                    return true;
            }

            return false;
        }

        private TradeRequirement[] ExtractTrades(RecipeChain old, ChefRecipe next)
        {
            var prev = old?.AlliedTradeSparse ?? Array.Empty<TradeRequirement>();

            if (next is not TradeRecipe tr)
                return prev;

            return UpdateTradeRequirements(prev, tr.Donor, tr.ItemUnifiedIndex);
        }

        private static void AccumulatePhysCost(int idx, int cnt,
          ref int add0Idx, ref int add0Cnt,
          ref int add1Idx, ref int add1Cnt)
        {
            if (cnt <= 0) return;

            if (add0Cnt <= 0) { add0Idx = idx; add0Cnt = cnt; return; }
            if (add0Idx == idx) { add0Cnt += cnt; return; }

            if (add1Cnt <= 0) { add1Idx = idx; add1Cnt = cnt; return; }
            if (add1Idx == idx) { add1Cnt += cnt; return; }

            add0Cnt += cnt;
        }

        // --------------- Chain Extension ----------------
        private void AddChainToResults(
            Dictionary<int, List<RecipeChain>> results,
            Queue<RecipeChain> queue,
            RecipeChain chain,
            (int scrapIdx, int need)[] collapsedDroneNeeds)
        {
            if (chain == null)
            {
                DebugLog.CraftTrace(_log, "[Planner][Add] DROP: chain==null");
                return;
            }

            if (chain.IsBridgeIntermediate)
            {
                queue.Enqueue(chain);
                return;
            }

            if (!results.TryGetValue(chain.ResultIndex, out var list))
            {
                list = new List<RecipeChain>();
                results[chain.ResultIndex] = list;
            }

            if (list.Count >= CookBook.ChainsLimit) return;
            if (IsChainInefficient(chain)) return;

            if (!_seenChainSigByResult.TryGetValue(chain.ResultIndex, out var seen))
            {
                seen = new HashSet<ulong>();
                _seenChainSigByResult[chain.ResultIndex] = seen;
            }

            collapsedDroneNeeds ??= CollapseDroneNeedsByScrapIndex(chain.DroneCostSparse);

            if (!seen.Add(HashCosts(chain.PhysicalCostSparse, collapsedDroneNeeds, chain.AlliedTradeSparse)))
            {
                return;
            }

            list.Add(chain);
            queue.Enqueue(chain);
            TraceChainAdd("Add", chain);
        }

        // -------- Getters -------------
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

            foreach (var ing in chain.PhysicalCostSparse)
                total += GetItemWeight(ing.UnifiedIndex) * ing.Count;

            foreach (var drone in chain.DroneCostSparse)
            {
                int totalDronePotential = DroneUpgradeUtils.GetDroneCountFromUpgradeCount(drone.TotalUpgradeCount);
                total += GetItemWeight(drone.ScrapIndex) * totalDronePotential;
            }

            foreach (var trade in chain.AlliedTradeSparse)
                total += GetItemWeight(trade.UnifiedIndex) * trade.TradesRequired;

            return total;
        }

        private string GetItemName(int unifiedIndex)
        {
            if (unifiedIndex < ItemCatalog.itemCount)
            {
                return Language.GetString(ItemCatalog.GetItemDef((ItemIndex)unifiedIndex)?.nameToken ?? "Unknown Item");
            }
            return Language.GetString(EquipmentCatalog.GetEquipmentDef((EquipmentIndex)(unifiedIndex - ItemCatalog.itemCount))?.nameToken ?? "Unknown Equip");
        }

        // --------- Setters ----------------
        internal void SetMaxDepth(int newDepth)
        {
            newDepth = Math.Max(0, newDepth);

            int newDroneTouchedLen = newDepth * 2 + 2;
            int newMaxKeys = (newDepth * 3) + 3;

            if (_droneTouched == null)
            {
                _droneTouched = new int[newDroneTouchedLen];
            }
            else if (_droneTouched.Length != newDroneTouchedLen)
            {
                int oldLen = _droneTouched.Length;
                Array.Resize(ref _droneTouched, newDroneTouchedLen);

                if (newDroneTouchedLen > oldLen)
                    Array.Clear(_droneTouched, oldLen, newDroneTouchedLen - oldLen);
            }

            // ---- profiling / key arrays ----
            ResizeOrAlloc(ref _profileKeys, newMaxKeys);
            ResizeOrAlloc(ref _profileVals, newMaxKeys);
            ResizeOrAlloc(ref _posKeys, newMaxKeys);
            ResizeOrAlloc(ref _defKeys, newMaxKeys);
            _bucketKeysScratch.Capacity = MaxBucketKeysCapacity(newDepth);

            _maxDepth = newDepth;

            _pMaxLen = 2 * _maxDepth;
            _dMaxLen = 2 * _maxDepth;
            _tMaxLen = _maxDepth;
        }

        private static void ResizeOrAlloc<T>(ref T[] arr, int newLen)
        {
            if (arr == null)
                arr = new T[newLen];
            else if (arr.Length != newLen)
                Array.Resize(ref arr, newLen);
        }

        // --------- Helpers ----------------
        private static ulong HashCosts(
            Ingredient[] phys,
            (int scrapIdx, int need)[] droneNeeds,
            TradeRequirement[] trades)
        {
            unchecked
            {
                // FNV-1a 64-bit
                const ulong offset = 1469598103934665603UL;
                const ulong prime = 1099511628211UL;

                ulong h = offset;

                phys ??= Array.Empty<Ingredient>();
                for (int i = 0; i < phys.Length; i++)
                {
                    int idx = phys[i].UnifiedIndex;
                    int cnt = phys[i].Count;
                    if (cnt <= 0) continue;

                    h ^= (uint)idx; h *= prime;
                    h ^= (uint)cnt; h *= prime;
                }

                droneNeeds ??= Array.Empty<(int, int)>();
                for (int i = 0; i < droneNeeds.Length; i++)
                {
                    h ^= (uint)droneNeeds[i].scrapIdx; h *= prime;
                    h ^= (uint)droneNeeds[i].need; h *= prime;
                }

                trades ??= Array.Empty<TradeRequirement>();
                for (int i = 0; i < trades.Length; i++)
                {
                    int cnt = trades[i].TradesRequired;
                    if (cnt <= 0) continue;

                    int donor32 = Fold64To32((trades[i].Donor ? trades[i].Donor.netId.Value : 0L));

                    h ^= (uint)donor32; h *= prime;
                    h ^= (uint)trades[i].UnifiedIndex; h *= prime;
                    h ^= (uint)cnt; h *= prime;
                }

                return h;
            }
        }

        private static int MaxBucketKeysCapacity(int maxDepth)
        {
            int pMax = 2 * maxDepth;
            int dMax = 2 * maxDepth;
            int tMax = maxDepth;
            return (pMax + 1) * (dMax + 1) * (tMax + 1);
        }

        private static TradeRequirement[] SortTradesCanonical(TradeRequirement[] trades)
        {
            if (trades == null || trades.Length <= 1)
                return trades ?? Array.Empty<TradeRequirement>();

            Array.Sort(trades, (x, y) =>
            {
                long xd = x.Donor ? x.Donor.netId.Value : 0L;
                long yd = y.Donor ? y.Donor.netId.Value : 0L;

                int c = xd.CompareTo(yd);
                if (c != 0) return c;

                return x.UnifiedIndex.CompareTo(y.UnifiedIndex);
            });

            return trades;
        }

        internal void RefreshVisualOverridesAndEmit(InventorySnapshot snap)
        {
            if (_entryCache == null || _entryCache.Count == 0) return;

            var finalResults = _entryCache.Values.ToList();

            foreach (var entry in finalResults)
            {
                int rawIdx = (entry.Chains != null && entry.Chains.Count > 0)
                  ? entry.Chains[0].ResultIndex
                  : entry.ResultIndex;

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
            OnCraftablesUpdated?.Invoke(finalResults, snap);
        }


        private static bool MaskHasBit(ulong[] mask, int idx)
        {
            int w = idx >> 6;
            int b = idx & 63;
            if ((uint)w >= (uint)(mask?.Length ?? 0)) return false;
            return ((mask[w] >> b) & 1UL) != 0UL;
        }

        private static void UpdateMaskBit(ulong[] mask, int idx, bool set)
        {
            int word = idx >> 6;
            int bit = idx & 63;
            if ((uint)word >= (uint)mask.Length) return;

            ulong flag = 1UL << bit;
            if (set) mask[word] |= flag;
            else mask[word] &= ~flag;
        }

        private static bool ContainsIndex(Ingredient[] arr, int idx)
        {
            for (int i = 0; i < arr.Length; i++)
                if (arr[i].UnifiedIndex == idx) return true;
            return false;
        }

        private static void ForEachRequirement(ChefRecipe r, Action<int, int> visit)
        {
            if (r == null) return;

            int a = r.IngA;
            int ca = r.CountA;
            if (ca > 0 && a >= 0)
                visit(a, ca);

            if (r.HasB)
            {
                int b = r.IngB;
                int cb = r.CountB;
                if (cb > 0 && b >= 0)
                    visit(b, cb);
            }
        }

        // --------- Logging ----------------
        private void TraceChainDrop(string stage, string reason, RecipeChain chain, ChefRecipe candidate = null)
        {
            PerfProfile.TraceChainDrop(
                _log,
                stage,
                reason,
                () => ChainSummary(chain),
                candidate != null ? () => GetItemName(candidate.ResultIndex) : null
            );
        }

        private void TraceChainAdd(string stage, RecipeChain chain)
        {
            PerfProfile.TraceChainAdd(
                _log,
                stage,
                () => ChainSummary(chain)
            );
        }

        private string ChainSummary(RecipeChain chain)
        {
            if (chain == null) return "<null>";

            var parts = new string[Math.Min(chain.Depth, _maxDepth)];
            int count = 0;

            for (var n = chain; n != null && count < parts.Length; n = n.Parent)
            {
                var s = n.LastStep;
                parts[count++] =
                  (s is TradeRecipe t) ? $"Trade({GetItemName(t.ItemUnifiedIndex)})"
                            : GetItemName(s.ResultIndex);
            }

            Array.Reverse(parts, 0, count);

            string steps = string.Join(" -> ", parts, 0, count);

            int weight = GetWeightedCost(chain);
            int surplus = chain.ResultSurplus;

            return $"[Depth {chain.Depth}, Weight {weight}, Surplus {surplus}] {steps}";
        }

        // ---------------- Types ---------------
        internal readonly struct StepDelta3
        {
            public readonly int I0, V0;
            public readonly int I1, V1;
            public readonly int I2, V2;

            public StepDelta3(int i0, int v0, int i1, int v1, int i2, int v2)
            {
                I0 = i0; V0 = v0;
                I1 = i1; V1 = v1;
                I2 = i2; V2 = v2;
            }

            public int GetFor(int idx)
            {
                if (idx == I0) return V0;
                if (idx == I1) return V1;
                if (idx == I2) return V2;
                return 0;
            }
        }
        internal readonly struct CostShapeKey : IEquatable<CostShapeKey>
        {
            public readonly int Hash1;
            public readonly int Hash2;
            public readonly int PhysLen;
            public readonly int DroneLen;
            public readonly int TradeLen;

            public CostShapeKey(int hash1, int hash2, int physLen, int droneLen, int tradeLen)
            {
                Hash1 = hash1;
                Hash2 = hash2;
                PhysLen = physLen;
                DroneLen = droneLen;
                TradeLen = tradeLen;
            }

            public bool Equals(CostShapeKey other)
              => Hash1 == other.Hash1 && Hash2 == other.Hash2
              && PhysLen == other.PhysLen && DroneLen == other.DroneLen && TradeLen == other.TradeLen;

            public override int GetHashCode()
            {
                unchecked { return (Hash1 * 397) ^ Hash2; }
            }
        }
        internal readonly struct BestCostRecord
        {
            public readonly Ingredient[] Phys;
            public readonly (int scrapIdx, int need)[] Drone;
            public readonly TradeRequirement[] Trades;

            public BestCostRecord(Ingredient[] phys, (int scrapIdx, int need)[] drone, TradeRequirement[] trades)
            {
                Phys = phys ?? Array.Empty<Ingredient>();
                Drone = drone ?? Array.Empty<(int, int)>();
                Trades = trades ?? Array.Empty<TradeRequirement>();
            }
        }
        private readonly struct DroneKey : IEquatable<DroneKey>
        {
            public readonly uint OwnerNetId;
            public readonly int DroneIdx;
            public DroneKey(uint ownerNetId, int droneIdx) { OwnerNetId = ownerNetId; DroneIdx = droneIdx; }
            public bool Equals(DroneKey other) => OwnerNetId == other.OwnerNetId && DroneIdx == other.DroneIdx;
            public override int GetHashCode() => unchecked(((int)OwnerNetId * 397) ^ DroneIdx);
        }
        internal class TradeRecipe : ChefRecipe
        {
            public NetworkUser Donor;
            public int ItemUnifiedIndex;

            public TradeRecipe(NetworkUser donor, int itemIndex)
              : base(resultIndex: itemIndex, resultCount: 1, ingA: -1, ingB: -1, countA: 0, countB: 0)
            {
                Donor = donor;
                ItemUnifiedIndex = itemIndex;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = 17;
                    h = (h * 31) ^ (Donor?.netId.GetHashCode() ?? 0);
                    h = (h * 31) ^ ItemUnifiedIndex;
                    return h;
                }
            }
        }
        internal struct TradeRequirement
        {
            public NetworkUser Donor;
            public int UnifiedIndex;
            public int TradesRequired;
        }
        internal struct DroneRequirement
        {
            public NetworkUser Owner;
            public DroneIndex DroneIdx;
            public int Count;
            public int TotalUpgradeCount;
            public int ScrapIndex;
        }
        internal class CraftableEntry
        {
            public int ResultIndex;
            public int ResultCount;
            public int MinDepth;
            public List<RecipeChain> Chains = new();
            public bool IsItem => ResultIndex < ItemCatalog.itemCount;
            public ItemIndex ResultItem => IsItem ? (ItemIndex)ResultIndex : ItemIndex.None;
            public EquipmentIndex ResultEquipment => IsItem ? EquipmentIndex.None : (EquipmentIndex)(ResultIndex - ItemCatalog.itemCount);
        }
        internal class RecipeChain
        {
            internal RecipeChain Parent { get; }
            internal ChefRecipe LastStep { get; }
            internal ChefRecipe FirstStep { get; }
            internal int Depth { get; }

            private List<ChefRecipe> _stepsCache;
            internal IReadOnlyList<ChefRecipe> Steps => MaterializeSteps();
            internal Ingredient[] PhysicalCostSparse { get; }
            internal DroneRequirement[] DroneCostSparse { get; }
            internal TradeRequirement[] AlliedTradeSparse { get; }
            internal int ResultIndex { get; }
            internal int ResultCount { get; }
            internal int ResultSurplus { get; }
            internal StepDelta3 Delta { get; }
            internal bool IsBridgeIntermediate { get; }

            private IReadOnlyList<ChefRecipe> MaterializeSteps()
            {
                if (_stepsCache != null) return _stepsCache;

                var list = new List<ChefRecipe>(Depth);
                for (var n = this; n != null; n = n.Parent)
                    list.Add(n.LastStep);

                list.Reverse();
                _stepsCache = list;
                return list;
            }

            internal RecipeChain(
              ChefRecipe recipe,
              Ingredient[] phys,
              DroneRequirement[] drones,
              TradeRequirement[] trades)
            {
                Parent = null;
                LastStep = recipe;
                FirstStep = recipe;
                Depth = 1;

                ResultIndex = recipe.ResultIndex;
                ResultCount = recipe.ResultCount;
                PhysicalCostSparse = phys;
                DroneCostSparse = drones;
                AlliedTradeSparse = trades;

                // Delta = result +count, ingA -countA, ingB -countB
                int i0 = recipe.ResultIndex, v0 = recipe.ResultCount;
                int i1 = recipe.IngA, v1 = (recipe.CountA > 0 && recipe.IngA >= 0) ? -recipe.CountA : 0;
                int i2 = recipe.HasB ? recipe.IngB : -1;
                int v2 = (recipe.HasB && recipe.CountB > 0 && recipe.IngB >= 0) ? -recipe.CountB : 0;

                // normalize
                if (v1 == 0) i1 = -1;
                if (v2 == 0) i2 = -1;

                Delta = new StepDelta3(i0, v0, i1, v1, i2, v2);

                ResultSurplus = GetNetSurplusFor(recipe.ResultIndex);
                IsBridgeIntermediate = false;
            }

            internal RecipeChain(
              RecipeChain parent,
              ChefRecipe next,
              Ingredient[] phys,
              DroneRequirement[] drones,
              TradeRequirement[] trades,
              bool isIntermediate)
            {
                Parent = parent;
                LastStep = next;
                FirstStep = parent.FirstStep;
                Depth = parent.Depth + 1;

                ResultIndex = next.ResultIndex;
                ResultCount = next.ResultCount;
                PhysicalCostSparse = phys;
                DroneCostSparse = drones;
                AlliedTradeSparse = trades;

                int i0 = next.ResultIndex, v0 = next.ResultCount;
                int i1 = next.IngA, v1 = (next.CountA > 0 && next.IngA >= 0) ? -next.CountA : 0;
                int i2 = next.HasB ? next.IngB : -1;
                int v2 = (next.HasB && next.CountB > 0 && next.IngB >= 0) ? -next.CountB : 0;

                if (v1 == 0) i1 = -1;
                if (v2 == 0) i2 = -1;

                Delta = new StepDelta3(i0, v0, i1, v1, i2, v2);

                ResultSurplus = GetNetSurplusFor(next.ResultIndex);
                IsBridgeIntermediate = isIntermediate;
            }

            public int GetNetSurplusFor(int itemIndex)
            {
                int sum = 0;
                for (var n = this; n != null; n = n.Parent)
                    sum += n.Delta.GetFor(itemIndex);
                return sum;
            }
            public int GetMaxAffordable(InventorySnapshot snap)
            {
                int[] localPhysical = snap.PhysicalStacks;
                int[] dronePotential = snap.DronePotential;
                Dictionary<NetworkUser, int[]> alliedSnapshots = snap.AlliedPhysicalStacks;
                Dictionary<NetworkUser, int> TradesRemaining = snap.TradesRemaining;

                int max = int.MaxValue;

                // ---------------- Physical costs ----------------
                if (PhysicalCostSparse != null && localPhysical != null)
                {
                    foreach (var cost in PhysicalCostSparse)
                    {
                        if (cost.Count <= 0) continue;

                        int idx = cost.UnifiedIndex;
                        if ((uint)idx >= (uint)localPhysical.Length) return 0;

                        max = Math.Min(max, localPhysical[idx] / cost.Count);
                        if (max == 0) return 0;
                    }
                }

                // ---------------- Drone costs (by scrap tier) ----------------
                if (DroneCostSparse != null && dronePotential != null && dronePotential.Length > 0)
                {
                    int[] needs = null;

                    foreach (var drone in DroneCostSparse)
                    {
                        if (drone.Count <= 0) continue;

                        int tier = drone.ScrapIndex;
                        if ((uint)tier >= (uint)dronePotential.Length) return 0;

                        needs ??= new int[dronePotential.Length];
                        needs[tier] += drone.Count;
                    }

                    if (needs != null)
                    {
                        for (int tier = 0; tier < needs.Length; tier++)
                        {
                            int need = needs[tier];
                            if (need <= 0) continue;

                            max = Math.Min(max, dronePotential[tier] / need);
                            if (max == 0) return 0;
                        }
                    }
                }

                // ---------------- Allied trades ----------------
                if (AlliedTradeSparse != null)
                {
                    if (alliedSnapshots == null || TradesRemaining == null) return 0;

                    foreach (var trade in AlliedTradeSparse)
                    {
                        if (trade.TradesRequired <= 0) continue;

                        if (trade.Donor == null) return 0;
                        if (!alliedSnapshots.TryGetValue(trade.Donor, out int[] donorInv) || donorInv == null) return 0;
                        if (!TradesRemaining.TryGetValue(trade.Donor, out int tradesLeft)) return 0;

                        int idx = trade.UnifiedIndex;
                        if ((uint)idx >= (uint)donorInv.Length) return 0;

                        int byInv = donorInv[idx] / trade.TradesRequired;
                        int byTrades = tradesLeft / trade.TradesRequired;

                        max = Math.Min(max, Math.Min(byInv, byTrades));
                        if (max == 0) return 0;
                    }
                }

                return max == int.MaxValue ? 0 : max;
            }
        }
    }
}