using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx.Logging;
using RoR2;

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
                    RunBench(data, stacks, physMask, d, multiplier);
            }
            else
            {
                int depth = depthArg > 0 ? depthArg : snapshotDepth;
                RunBench(data, stacks, physMask, depth, multiplier);
            }
        }

        private static void RunBench(SnapshotLoader.BenchData data, int[] stacks, ulong[] physMask, int depth, int multiplier)
        {
            var snapshot = new InventorySnapshot(
                physical: stacks,
                drone: data.DronePotential,
                allCandidates: new Dictionary<int, List<DroneCandidate>>(),
                corrupted: new HashSet<ItemIndex>(),
                scrapperPresent: false,
                poolingEnabled: false,
                maxdepth: depth,
                filteredRecipes: data.FilteredRecipes,
                physMask: physMask,
                droneMask: data.DroneMask,
                alliedPhysicalStacks: new Dictionary<NetworkUser, int[]>(),
                remainingTrades: new Dictionary<NetworkUser, int>()
            );

            var log = new ManualLogSource("Bench");
            int runs = 10;

            Console.WriteLine($"\n--- Depth {depth} ({multiplier}x inventory, {runs} runs) ---");

            // Warm-up
            var warmup = new CraftPlanner(depth, log);
            warmup.ComputeCraftable(in snapshot, forceUpdate: true);

            var times = new double[runs];
            for (int r = 0; r < runs; r++)
            {
                PerfProfile.Reset();
                var sw = Stopwatch.StartNew();

                var p = new CraftPlanner(depth, log);
                p.ComputeCraftable(in snapshot, forceUpdate: true);

                sw.Stop();
                times[r] = sw.Elapsed.TotalMilliseconds;
            }

            PerfProfile.LogSummary(log, topN: 14);

            Array.Sort(times);
            double sum = 0;
            foreach (var t in times) sum += t;

            Console.WriteLine($"\n  Timing: min={times[0]:F2}  med={times[runs / 2]:F2}  max={times[runs - 1]:F2}  avg={sum / runs:F2} ms");

            PrintAlgorithmAnalysis();
        }

        private static void PrintAlgorithmAnalysis()
        {
            int nodes       = PerfProfile.BfsNodesPopped;
            int candidates  = PerfProfile.CandidatesEvaluated;
            int created     = PerfProfile.ChainsCreated;
            int dominated   = PerfProfile.ChainsDominated;
            int sigDuped    = PerfProfile.ChainsSigDuped;
            int domCalls    = PerfProfile.DominatesCallCount;
            int bucketScans = PerfProfile.DominatesBucketScans;
            int shapeExact  = PerfProfile.ShapeHitExact;
            int shapeBetter = PerfProfile.ShapeHitBetter;
            int shapeWorse  = PerfProfile.ShapeHitWorse;
            int shapeNew    = PerfProfile.ShapeMissNew;
            int evictions   = PerfProfile.FrontierEvictions;
            int uniqueRes   = PerfProfile.UniqueResultIndices;

            int totalChecked = dominated + created;
            int shapeTotal   = shapeExact + shapeBetter + shapeWorse + shapeNew;

            Console.WriteLine("\n--- Algorithm Counters ---");
            Console.WriteLine($"  BFS nodes popped:      {nodes}");
            Console.WriteLine($"  Candidates evaluated:  {candidates}");
            Console.WriteLine($"  Chains created:        {created}");
            Console.WriteLine($"  Chains dominated:      {dominated}");
            Console.WriteLine($"  Chains sig-duped:      {sigDuped}");
            Console.WriteLine($"  Unique result items:   {uniqueRes}");

            Console.WriteLine("\n--- Dominance Engine ---");
            Console.WriteLine($"  IsChainDominated calls:  {shapeTotal}");
            Console.WriteLine($"  Dominates() pair checks: {domCalls}");
            Console.WriteLine($"  Bucket entries scanned:  {bucketScans}");
            Console.WriteLine($"  Frontier evictions:      {evictions}");

            Console.WriteLine("\n--- Shape Key Analysis ---");
            Console.WriteLine($"  Exact hit (early prune):  {shapeExact,6}  ({Pct(shapeExact, shapeTotal)})");
            Console.WriteLine($"  Better (replaced old):    {shapeBetter,6}  ({Pct(shapeBetter, shapeTotal)})");
            Console.WriteLine($"  Worse (kept existing):    {shapeWorse,6}  ({Pct(shapeWorse, shapeTotal)})");
            Console.WriteLine($"  Miss (new shape):         {shapeNew,6}  ({Pct(shapeNew, shapeTotal)})");

            Console.WriteLine("\n--- Derived ---");
            Console.WriteLine($"  Rejection rate:                {Pct(dominated, totalChecked)}  (dominated / total chains checked)");
            Console.WriteLine($"  Avg bucket scans per dom call: {Ratio(bucketScans, shapeTotal)}  (pair comparisons per IsChainDominated)");
            Console.WriteLine($"  Shape early-exit rate:         {Pct(shapeExact, shapeTotal)}  (calls avoided bucket scan entirely)");
            Console.WriteLine($"  Candidates per BFS node:       {Ratio(candidates, nodes)}");
            Console.WriteLine($"  Chains per candidate:          {Ratio(created, candidates)}");
        }

        private static string Pct(int num, int den)
            => den > 0 ? $"{(double)num / den * 100:F1}%" : "N/A";

        private static string Ratio(int num, int den)
            => den > 0 ? $"{(double)num / den:F1}" : "N/A";

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
