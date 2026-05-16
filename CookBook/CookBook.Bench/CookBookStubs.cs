// Stubs for CookBook types defined in other files that CraftPlanner.cs references.

using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;

namespace CookBook
{
    // ──────────── ChefRecipe + Ingredient (from RecipeProvider.cs) ────────────
    internal class ChefRecipe
    {
        public int ResultIndex { get; }
        public int ResultCount { get; }
        public readonly int IngA;
        public readonly int IngB;
        public readonly byte CountA;
        public readonly byte CountB;
        public bool HasB => CountB != 0;
        public bool IsDouble => (IngA == IngB) && CountA >= 2;

        public ChefRecipe(int resultIndex, int resultCount, int ingA, int ingB, byte countA, byte countB)
        {
            ResultIndex = resultIndex;
            ResultCount = resultCount;
            IngA = ingA;
            IngB = ingB;
            CountA = countA;
            CountB = countB;
        }

        public bool Equals(ChefRecipe other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;
            return ResultIndex == other.ResultIndex
                && ResultCount == other.ResultCount
                && IngA == other.IngA && IngB == other.IngB
                && CountA == other.CountA && CountB == other.CountB;
        }

        public override bool Equals(object obj) => obj is ChefRecipe other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ ResultIndex;
                h = (h * 31) ^ ResultCount;
                h = (h * 31) ^ IngA;
                h = (h * 31) ^ IngB;
                h = (h * 31) ^ CountA;
                h = (h * 31) ^ CountB;
                return h;
            }
        }
    }

    internal readonly struct Ingredient : IEquatable<Ingredient>
    {
        public readonly int UnifiedIndex;
        public readonly int Count;

        public Ingredient(int unifiedIndex, int count)
        {
            UnifiedIndex = unifiedIndex;
            Count = count;
        }

        public bool Equals(Ingredient other)
            => UnifiedIndex == other.UnifiedIndex && Count == other.Count;

        public override bool Equals(object obj)
            => obj is Ingredient other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) ^ UnifiedIndex;
                h = (h * 31) ^ Count;
                return h;
            }
        }
    }

    // ──────────── RecipeProvider (static, from RecipeProvider.cs) ────────────
    internal static class RecipeProvider
    {
        private static readonly List<ChefRecipe> _recipes = new();
        internal static IReadOnlyList<ChefRecipe> Recipes => _recipes;

        internal static int TotalDefCount { get; set; }
        internal static int MaskWords { get; set; }
        internal static ulong[][] ReqMasks { get; set; }
        internal static int[][] ConsumersByIngredient { get; set; }
        internal static int[][] ProducersByResult { get; set; }
        internal static int[] ResultIdxByRecipe { get; set; }
        internal static int[] IngAByRecipe { get; set; }
        internal static int[] IngBByRecipe { get; set; }
        internal static bool[] IsDoubleIngredientRecipe { get; set; }

        private static Dictionary<ChefRecipe, int> _masterIndexByRecipe = new();
        internal static IReadOnlyDictionary<ChefRecipe, int> MasterIndexByRecipe => _masterIndexByRecipe;

        internal static void LoadFromBench(
            List<ChefRecipe> recipes,
            int totalDefCount,
            int maskWords,
            ulong[][] reqMasks,
            int[][] consumersByIngredient,
            int[][] producersByResult,
            int[] resultIdxByRecipe,
            int[] ingAByRecipe,
            int[] ingBByRecipe,
            bool[] isDoubleIngredientRecipe)
        {
            _recipes.Clear();
            _recipes.AddRange(recipes);

            TotalDefCount = totalDefCount;
            MaskWords = maskWords;
            ReqMasks = reqMasks;
            ConsumersByIngredient = consumersByIngredient;
            ProducersByResult = producersByResult;
            ResultIdxByRecipe = resultIdxByRecipe;
            IngAByRecipe = ingAByRecipe;
            IngBByRecipe = ingBByRecipe;
            IsDoubleIngredientRecipe = isDoubleIngredientRecipe;

            _masterIndexByRecipe.Clear();
            for (int i = 0; i < _recipes.Count; i++)
                _masterIndexByRecipe[_recipes[i]] = i;
        }
    }

    // ──────────── InventorySnapshot (from InventoryTracker.cs) ────────────
    internal struct DroneCandidate
    {
        public NetworkUser Owner;
        public DroneIndex DroneIdx;
        public int UpgradeCount;
        public ulong MinionMasterNetId;
    }

    internal readonly struct InventorySnapshot
    {
        public readonly int[] PhysicalStacks;
        public readonly int[] DronePotential;
        public readonly ulong[] PhysicalMask;
        public readonly ulong[] DroneMask;

        public readonly Dictionary<int, List<DroneCandidate>> AllScrapCandidates;
        public readonly HashSet<ItemIndex> CorruptedIndices;

        public readonly bool CanScrapDrones;
        public readonly bool IsPoolingEnabled;
        public readonly int maxDepth;
        public readonly IReadOnlyList<ChefRecipe> FilteredRecipes;

        public readonly Dictionary<NetworkUser, int[]> AlliedPhysicalStacks;
        public readonly Dictionary<NetworkUser, int> TradesRemaining;

        public InventorySnapshot(
            int[] physical,
            int[] drone,
            Dictionary<int, List<DroneCandidate>> allCandidates,
            HashSet<ItemIndex> corrupted,
            bool scrapperPresent,
            bool poolingEnabled,
            int maxdepth,
            IReadOnlyList<ChefRecipe> filteredRecipes,
            ulong[] physMask,
            ulong[] droneMask,
            Dictionary<NetworkUser, int[]> alliedPhysicalStacks,
            Dictionary<NetworkUser, int> remainingTrades)
        {
            PhysicalStacks = physical ?? Array.Empty<int>();
            DronePotential = drone ?? Array.Empty<int>();
            AllScrapCandidates = allCandidates ?? new Dictionary<int, List<DroneCandidate>>();
            CorruptedIndices = corrupted ?? new HashSet<ItemIndex>();
            PhysicalMask = physMask;
            DroneMask = droneMask;
            AlliedPhysicalStacks = alliedPhysicalStacks ?? new Dictionary<NetworkUser, int[]>();
            TradesRemaining = remainingTrades ?? new Dictionary<NetworkUser, int>();
            maxDepth = maxdepth;
            CanScrapDrones = scrapperPresent;
            IsPoolingEnabled = poolingEnabled;
            FilteredRecipes = filteredRecipes ?? new List<ChefRecipe>();
        }
    }

    // ──────────── StateController (only IsChefStage needed) ────────────
    internal static class StateController
    {
        internal static bool IsChefStage() => true;
    }

    // ──────────── DebugLog ────────────
    internal static class DebugLog
    {
        public static void Trace(ManualLogSource log, string message) { }
        public static void CraftTrace(ManualLogSource log, string message) { }
    }

    // ──────────── TierManager (only CompareCraftableEntries used in CraftPlanner) ────────────
    internal static class TierManager
    {
        internal static int CompareCraftableEntries(object a, object b) => 0;
    }

    // ──────────── CookBook config stubs ────────────
    internal static class CookBook
    {
        internal static StubConfig<int> MaxBridgeItemsPerChain = new(3);
        internal static StubConfig<int> MaxChainsPerResult = new(40);
        internal static StubConfig<bool> ShowCorruptedResults = new(false);

        internal static int ChainsLimit => MaxChainsPerResult.Value;

        internal class StubConfig<T>
        {
            public T Value { get; set; }
            public StubConfig(T val) => Value = val;
        }
    }

    // ──────────── InventoryTracker (visual override stub) ────────────
    internal static class InventoryTracker
    {
        internal static int GetVisualResultIndex(int rawIdx) => -1;
    }

    // ──────────── BenchDump stub (no-op in bench, dump already happened in-game) ────────────
    internal static class BenchDump
    {
        internal static bool DumpRequested;
        internal static void DumpSnapshot(in InventorySnapshot snap) { }
    }
}
