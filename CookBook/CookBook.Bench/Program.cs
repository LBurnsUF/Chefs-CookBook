using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Logging;
using RoR2;
using UnityEngine.Networking;

namespace CookBook.Bench
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: CookBook.Bench <snapshot.json> [inventory_multiplier] [max_depth]");
                Console.WriteLine("  inventory_multiplier: scale all physicalStacks by this factor (default: 1)");
                Console.WriteLine("  max_depth: override max chain depth, or 0 to sweep 1..snapshot_depth (default: snapshot value)");
                return;
            }

            string snapshotPath = args[0];
            int multiplier = args.Length >= 2 ? int.Parse(args[1]) : 1;
            int depthArg = args.Length >= 3 ? int.Parse(args[2]) : -1;

            var data = SnapshotLoader.Load(snapshotPath);

            Console.WriteLine($"Loading snapshot: {snapshotPath}");
            Console.WriteLine($"Inventory multiplier: {multiplier}x");
            Console.WriteLine($"Items: {data.ItemCount}, Equipment: {data.EquipmentCount}, TotalDefs: {data.TotalDefCount}");
            Console.WriteLine($"Master recipes: {data.MasterRecipes.Count}, Filtered recipes: {data.FilteredRecipes.Count}");

            SetupGameStubs(data);

            var stacks = new int[data.TotalDefCount];
            var ingredientSet = new HashSet<int>();
            foreach (var r in data.MasterRecipes)
            {
                ingredientSet.Add(r.IngA);
                ingredientSet.Add(r.IngB);
            }
            foreach (int idx in ingredientSet)
            {
                if (idx >= 0 && idx < stacks.Length)
                    stacks[idx] = multiplier;
            }

            var physMask = new ulong[data.MaskWords];
            int nonZero = 0;
            for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i] > 0)
                {
                    physMask[i >> 6] |= (1UL << (i & 63));
                    nonZero++;
                }
            }

            Console.WriteLine($"Non-zero inventory slots: {nonZero} (every recipe ingredient gets {multiplier})");

            var synth = BuildSyntheticData(data, stacks, ingredientSet, multiplier);

            Console.WriteLine($"Drone items: {synth.DroneItemCount}, Drone candidates: {synth.TotalDroneCandidates}");
            Console.WriteLine($"Allied players: {synth.AlliedStacks.Count}, Trades per ally: {synth.TradesPerAlly}");

            RecipeProvider.LoadFromBench(
                data.MasterRecipes,
                data.TotalDefCount,
                data.MaskWords,
                data.ReqMasks,
                data.ConsumersByIngredient,
                data.ProducersByResult,
                data.ResultIdxByRecipe,
                data.IngAByRecipe,
                data.IngBByRecipe,
                data.IsDoubleIngredientRecipe
            );

            bool sweep = depthArg == 0;
            int snapshotDepth = data.MaxDepth;

            if (sweep)
            {
                Console.WriteLine($"\n{'=',-60}");
                Console.WriteLine($"  DEPTH SWEEP: 1..{snapshotDepth}  ({multiplier}x inventory)");
                Console.WriteLine($"{'=',-60}");

                for (int d = 1; d <= snapshotDepth; d++)
                    RunBench(data, stacks, physMask, d, multiplier, synth);
            }
            else
            {
                int depth = depthArg > 0 ? depthArg : snapshotDepth;
                RunBench(data, stacks, physMask, depth, multiplier, synth);
            }
        }

        private static void RunBench(SnapshotLoader.BenchData data, int[] stacks, ulong[] physMask, int depth, int multiplier, SyntheticData synth)
        {
            var snapshot = new InventorySnapshot(
                physical: stacks,
                drone: synth.DronePotential,
                allCandidates: synth.ScrapCandidates,
                corrupted: new HashSet<ItemIndex>(),
                scrapperPresent: true,
                poolingEnabled: true,
                maxdepth: depth,
                filteredRecipes: data.FilteredRecipes,
                physMask: physMask,
                droneMask: synth.DroneMask,
                alliedPhysicalStacks: synth.AlliedStacks,
                remainingTrades: synth.TradesRemaining
            );

            var log = new ManualLogSource("Bench");
            int runs = 10;

            Console.WriteLine($"\n--- Depth {depth} ({multiplier}x inventory, {runs} runs) ---");

            // Warm-up
            var warmup = new CraftPlanner(depth, log);
            warmup.ComputeCraftable(in snapshot, forceUpdate: true);

            var times = new double[runs];
            CraftPlanner lastPlanner = null;
            for (int r = 0; r < runs; r++)
            {
                PerfProfile.Reset();
                var sw = Stopwatch.StartNew();

                var p = new CraftPlanner(depth, log);
                p.ComputeCraftable(in snapshot, forceUpdate: true);

                sw.Stop();
                times[r] = sw.Elapsed.TotalMilliseconds;
                lastPlanner = p;
            }

            PerfProfile.LogSummary(log, topN: 14);

            Array.Sort(times);
            double sum = 0;
            foreach (var t in times) sum += t;

            Console.WriteLine($"\n  Timing: min={times[0]:F2}  med={times[runs / 2]:F2}  max={times[runs - 1]:F2}  avg={sum / runs:F2} ms");

            PrintAlgorithmAnalysis();
            PrintChainDiagnostics(lastPlanner, data);

            string dumpPath = Environment.GetEnvironmentVariable("BENCH_DUMP_PATH");
            if (!string.IsNullOrEmpty(dumpPath))
            {
                DumpAllChains(lastPlanner, data, dumpPath);
                Console.WriteLine($"\n  Full chain dump written to: {dumpPath}");
            }
        }

        private static void PrintAlgorithmAnalysis()
        {
#if COOKBOOK_PERF
            int nodes       = PerfProfile.BfsNodesPopped;
            int candidates  = PerfProfile.CandidatesEvaluated;
            int created     = PerfProfile.ChainsCreated;
            int dominated   = PerfProfile.ChainsDominated;
            int domCalls    = PerfProfile.DominatesCallCount;
            int bucketScans = PerfProfile.DominatesBucketScans;
            int evictions   = PerfProfile.FrontierEvictions;
            int uniqueRes   = PerfProfile.UniqueResultIndices;
            int totalChecked = dominated + created;

            Console.WriteLine("\n--- Algorithm Counters ---");
            Console.WriteLine($"  BFS nodes popped:      {nodes}");
            Console.WriteLine($"  Candidates evaluated:  {candidates}");
            Console.WriteLine($"  Chains created:        {created}");
            Console.WriteLine($"  Chains dominated:      {dominated}");
            Console.WriteLine($"  Unique result items:   {uniqueRes}");

            Console.WriteLine("\n--- Dominance Engine ---");
            Console.WriteLine($"  IsChainDominated calls:  {totalChecked}");
            Console.WriteLine($"  Dominates() pair checks: {domCalls}");
            Console.WriteLine($"  Bucket entries scanned:  {bucketScans}");
            Console.WriteLine($"  Frontier evictions:      {evictions}");

            Console.WriteLine("\n--- Derived ---");
            Console.WriteLine($"  Rejection rate:                {Pct(dominated, totalChecked)}  (dominated / total chains checked)");
            Console.WriteLine($"  Avg bucket scans per dom call: {Ratio(bucketScans, totalChecked)}  (pair comparisons per IsChainDominated)");
            Console.WriteLine($"  Candidates per BFS node:       {Ratio(candidates, nodes)}");
            Console.WriteLine($"  Chains per candidate:          {Ratio(created, candidates)}");
#else
            Console.WriteLine("\n  (Algorithm counters disabled — build without COOKBOOK_PERF)");
#endif
        }

        private static string Pct(int num, int den)
            => den > 0 ? $"{(double)num / den * 100:F1}%" : "N/A";

        private static string Ratio(int num, int den)
            => den > 0 ? $"{(double)num / den:F1}" : "N/A";

        private static void PrintChainDiagnostics(CraftPlanner planner, SnapshotLoader.BenchData data)
        {
            var entries = planner.EntryCache;
            if (entries == null || entries.Count == 0) return;

            var counts = entries.Values
                .Select(e => (name: GetName(e.ResultIndex, data), count: e.Chains.Count, idx: e.ResultIndex, chains: e.Chains))
                .OrderByDescending(x => x.count)
                .ToList();

            Console.WriteLine("\n--- Chain Distribution ---");
            Console.WriteLine($"  Total results: {counts.Count}");

            var histogram = counts.GroupBy(x => x.count).OrderBy(g => g.Key).ToList();
            Console.WriteLine("  Histogram (chains per result -> result count):");
            foreach (var g in histogram)
                Console.WriteLine($"    {g.Key,4} chains: {g.Count()} results");

            Console.WriteLine("\n  Top 10 results by chain count:");
            foreach (var item in counts.Take(10))
            {
                Console.WriteLine($"\n    [{item.name}] ({item.count} chains):");

                foreach (var chain in item.chains.Take(20))
                {
                    var physParts = chain.PhysicalCostSparse?
                        .Where(p => p.Count > 0)
                        .Select(p => $"{GetName(p.UnifiedIndex, data)}x{p.Count}")
                        .ToList() ?? new List<string>();

                    var droneParts = chain.DroneCostSparse?
                        .Where(d => d.Count > 0)
                        .Select(d => $"drone({GetName(d.ScrapIndex, data)}x{d.Count})")
                        .ToList() ?? new List<string>();

                    var tradeParts = chain.AlliedTradeSparse?
                        .Where(t => t.TradesRequired > 0)
                        .Select(t => $"trade({GetName(t.UnifiedIndex, data)}x{t.TradesRequired})")
                        .ToList() ?? new List<string>();

                    var allCosts = physParts.Concat(droneParts).Concat(tradeParts).ToList();
                    string costStr = allCosts.Count > 0 ? string.Join(" + ", allCosts) : "(free)";

                    var steps = chain.Steps;
                    string path = string.Join(" -> ", steps.Select(s => GetName(s.ResultIndex, data)));

                    Console.WriteLine($"      d{chain.Depth}: {costStr}");
                    Console.WriteLine($"        path: {path}");
                }
            }
        }

        internal static void DumpAllChains(CraftPlanner planner, SnapshotLoader.BenchData data, string outPath)
        {
            var entries = planner.EntryCache;
            if (entries == null || entries.Count == 0) return;

            var sorted = entries.Values
                .OrderBy(e => GetName(e.ResultIndex, data))
                .ToList();

            using var writer = new System.IO.StreamWriter(outPath);
            foreach (var entry in sorted)
            {
                string resultName = GetName(entry.ResultIndex, data);
                var chains = entry.Chains
                    .OrderBy(c => c.Depth)
                    .ThenBy(c => ChainSortKey(c, data))
                    .ToList();

                writer.WriteLine($"[{resultName}] ({chains.Count} chains)");
                foreach (var chain in chains)
                {
                    var physParts = chain.PhysicalCostSparse?
                        .Where(p => p.Count > 0)
                        .OrderBy(p => GetName(p.UnifiedIndex, data))
                        .Select(p => $"{GetName(p.UnifiedIndex, data)}x{p.Count}")
                        .ToList() ?? new List<string>();

                    var tradeParts = chain.AlliedTradeSparse?
                        .Where(t => t.TradesRequired > 0)
                        .OrderBy(t => GetName(t.UnifiedIndex, data))
                        .Select(t => $"trade({GetName(t.UnifiedIndex, data)}x{t.TradesRequired})")
                        .ToList() ?? new List<string>();

                    var allCosts = physParts.Concat(tradeParts).ToList();
                    string costStr = allCosts.Count > 0 ? string.Join(" + ", allCosts) : "(free)";

                    var steps = chain.Steps;
                    string path = string.Join(" -> ", steps.Select(s => GetName(s.ResultIndex, data)));

                    writer.WriteLine($"  d{chain.Depth}: {costStr} | {path}");
                }
                writer.WriteLine();
            }
        }

        private static string ChainSortKey(CraftPlanner.RecipeChain chain, SnapshotLoader.BenchData data)
        {
            var parts = chain.PhysicalCostSparse?
                .Where(p => p.Count > 0)
                .OrderBy(p => GetName(p.UnifiedIndex, data))
                .Select(p => $"{GetName(p.UnifiedIndex, data)}x{p.Count}")
                .ToList() ?? new List<string>();
            return string.Join("+", parts);
        }

        private static string GetName(int unifiedIndex, SnapshotLoader.BenchData data)
        {
            if (data.ItemNames.TryGetValue(unifiedIndex.ToString(), out var n))
                return n;
            return $"#{unifiedIndex}";
        }

        internal class SyntheticData
        {
            public int[] DronePotential;
            public ulong[] DroneMask;
            public Dictionary<int, List<DroneCandidate>> ScrapCandidates;
            public Dictionary<NetworkUser, int[]> AlliedStacks;
            public Dictionary<NetworkUser, int> TradesRemaining;
            public int DroneItemCount;
            public int TotalDroneCandidates;
            public int TradesPerAlly;
        }

        private static SyntheticData BuildSyntheticData(SnapshotLoader.BenchData data, int[] stacks, HashSet<int> ingredientSet, int multiplier)
        {
            int totalDef = data.TotalDefCount;
            int maskWords = data.MaskWords;

            var dronePotential = new int[totalDef];
            var scrapCandidates = new Dictionary<int, List<DroneCandidate>>();
            ulong nextMinionId = 1000;
            int droneItemCount = 0;
            int totalDroneCandidates = 0;

            var ingredientList = new List<int>(ingredientSet);
            ingredientList.Sort();

            for (int i = 0; i < ingredientList.Count; i++)
            {
                int idx = ingredientList[i];
                if (i % 3 != 0) continue;

                int potential = Math.Max(1, multiplier / 2);
                dronePotential[idx] = potential;
                droneItemCount++;

                int candidateCount = 1 + (i % 4);
                var candidates = new List<DroneCandidate>(candidateCount);
                for (int c = 0; c < candidateCount; c++)
                {
                    candidates.Add(new DroneCandidate
                    {
                        Owner = null,
                        DroneIdx = (DroneIndex)(c % 5),
                        UpgradeCount = 1 + (c % 3),
                        MinionMasterNetId = nextMinionId++
                    });
                    totalDroneCandidates++;
                }
                scrapCandidates[idx] = candidates;
            }

            var droneMask = new ulong[maskWords];
            for (int i = 0; i < totalDef; i++)
            {
                if (dronePotential[i] > 0)
                    droneMask[i >> 6] |= (1UL << (i & 63));
            }

            int allyCount = 3;
            int tradesPerAlly = 5;
            var alliedStacks = new Dictionary<NetworkUser, int[]>();
            var tradesRemaining = new Dictionary<NetworkUser, int>();

            for (int a = 0; a < allyCount; a++)
            {
                var ally = new NetworkUser { netId = new NetworkInstanceId((uint)(900 + a)) };
                var allyInv = new int[totalDef];

                for (int i = 0; i < ingredientList.Count; i++)
                {
                    int idx = ingredientList[i];
                    if ((i + a) % 4 == 0)
                        allyInv[idx] = Math.Max(1, multiplier);
                }

                alliedStacks[ally] = allyInv;
                tradesRemaining[ally] = tradesPerAlly;
            }

            return new SyntheticData
            {
                DronePotential = dronePotential,
                DroneMask = droneMask,
                ScrapCandidates = scrapCandidates,
                AlliedStacks = alliedStacks,
                TradesRemaining = tradesRemaining,
                DroneItemCount = droneItemCount,
                TotalDroneCandidates = totalDroneCandidates,
                TradesPerAlly = tradesPerAlly
            };
        }

        private static void SetupGameStubs(SnapshotLoader.BenchData data)
        {
            var items = new ItemDef[data.ItemCount];
            for (int i = 0; i < data.ItemCount; i++)
            {
                string name = data.ItemNames.TryGetValue(i.ToString(), out var n) ? n : $"Item{i}";
                items[i] = new ItemDef { name = name, nameToken = name, tier = ItemTier.Tier1 };
            }
            ItemCatalog.SetItems(items);

            var equips = new EquipmentDef[data.EquipmentCount];
            for (int i = 0; i < data.EquipmentCount; i++)
            {
                int idx = data.ItemCount + i;
                string name = data.ItemNames.TryGetValue(idx.ToString(), out var n) ? n : $"Equip{i}";
                equips[i] = new EquipmentDef { name = name, nameToken = name };
            }
            EquipmentCatalog.SetEquipment(equips);
        }
    }
}
