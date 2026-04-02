//#define COOKBOOK_TRACE_LOGS
using System;
using System.Diagnostics;
using System.Threading;
using BepInEx.Logging;

namespace CookBook
{
    public static partial class PerfProfile
    {
        public enum Region
        {
            TotalCompute,
            BuildRecipeIndex,
            SeedLayer,
            CandidateBuild,
            ExpandTrades,
            CalculateSplitCosts,
            ResolveRequirement,
            IsChainDominated,
            NewChainAlloc,
            PhysConsolidateAlloc,
            FinalEntryBuild,
            BfsExpand,
            AddChainToResults,
            CandidateLoopOverhead,
            Count
        }
#if COOKBOOK_PERF
        private static long[] _incTicks = new long[(int)Region.Count];
        private static long[] _excTicks = new long[(int)Region.Count];
        private static int[] _calls = new int[(int)Region.Count];
        private static long[] _maxExcTicks = new long[(int)Region.Count];
        private static long[] _minExcTicks = new long[(int)Region.Count];

        private static long NowTicks() => Stopwatch.GetTimestamp();

        public static void Reset()
        {
            Array.Clear(_incTicks, 0, _incTicks.Length);
            Array.Clear(_excTicks, 0, _excTicks.Length);
            Array.Clear(_calls, 0, _calls.Length);
            Array.Clear(_maxExcTicks, 0, _maxExcTicks.Length);
            for (int i = 0; i < _minExcTicks.Length; i++) _minExcTicks[i] = long.MaxValue;
            _stack.Value?.Reset();
        }

        // Per-thread stack of active scopes
        private sealed class ScopeStack
        {
            public int Depth;
            public Frame[] Frames = new Frame[128];

            public void Reset() => Depth = 0;

            [Conditional("COOKBOOK_PERF")]
            public void Push(Region r, long t0)
            {
                if (Depth >= Frames.Length)
                    Array.Resize(ref Frames, Frames.Length * 2);

                Frames[Depth++] = new Frame(r, t0);
            }

            public Frame Pop() => Frames[--Depth];
        }

        private struct Frame
        {
            public Region R;
            public long T0;
            public long ChildTicks;
            public Frame(Region r, long t0)
            {
                R = r;
                T0 = t0;
                ChildTicks = 0;
            }
        }

        private static readonly ThreadLocal<ScopeStack> _stack =
            new ThreadLocal<ScopeStack>(() => new ScopeStack());

        public readonly struct Scope : IDisposable
        {
            private readonly Region _r;
            private readonly long _t0;
            private readonly int _depthAtPush;

            public Scope(Region r)
            {
                _r = r;
                _t0 = NowTicks();

                var st = _stack.Value;
                _depthAtPush = st.Depth;
                st.Push(r, _t0);
            }

            public void Dispose()
            {
                var st = _stack.Value;
                if (st.Depth <= 0) return;

                var frame = st.Pop();
                long dt = NowTicks() - frame.T0;

                int i = (int)frame.R;
                long exc = dt - frame.ChildTicks;
                _incTicks[i] += dt;
                _excTicks[i] += exc;
                _calls[i]++;
                if (exc > _maxExcTicks[i]) _maxExcTicks[i] = exc;
                if (exc < _minExcTicks[i]) _minExcTicks[i] = exc;

                if (st.Depth > 0)
                {
                    st.Frames[st.Depth - 1].ChildTicks += dt;
                }
            }
        }

        public static Scope Measure(Region r) => new Scope(r);
        [Conditional("COOKBOOK_PERF")]
        public static void LogSummary(ManualLogSource log, int topN = 12, bool sortByExclusive = true)
        {
            if (log == null) return;

            long total = _incTicks[(int)Region.TotalCompute];
            if (total <= 0) return;

            double invFreqMs = 1000.0 / Stopwatch.Frequency;

            log.LogInfo($"[Perf] TotalCompute: {(total * invFreqMs):F2} ms");

            int cap = Math.Max(1, Math.Min(topN, (int)Region.Count - 1));

            Span<int> topIdx = cap <= 32 ? stackalloc int[cap] : new int[cap];
            Span<long> topKey = cap <= 32 ? stackalloc long[cap] : new long[cap];
            for (int k = 0; k < cap; k++) { topIdx[k] = -1; topKey[k] = 0; }

            for (int i = 0; i < (int)Region.Count; i++)
            {
                if (i == (int)Region.TotalCompute) continue;

                long key = sortByExclusive ? _excTicks[i] : _incTicks[i];
                if (key <= 0) continue;

                for (int k = 0; k < cap; k++)
                {
                    if (key <= topKey[k]) continue;

                    for (int s = cap - 1; s > k; s--)
                    {
                        topKey[s] = topKey[s - 1];
                        topIdx[s] = topIdx[s - 1];
                    }
                    topKey[k] = key;
                    topIdx[k] = i;
                    break;
                }
            }

            string mode = sortByExclusive ? "EXCLUSIVE" : "INCLUSIVE";
            log.LogInfo($"[Perf] Top regions by {mode}:");

            for (int k = 0; k < cap; k++)
            {
                int idx = topIdx[k];
                if (idx < 0) break;

                long inc = _incTicks[idx];
                long exc = _excTicks[idx];
                double incMs = inc * invFreqMs;
                double excMs = exc * invFreqMs;

                double pctInc = (double)inc * 100.0 / total;
                double pctExc = (double)exc * 100.0 / total;

                int calls = _calls[idx];

                long minRaw = _minExcTicks[idx];
                long maxRaw = _maxExcTicks[idx];
                double minMs = (minRaw == long.MaxValue) ? 0 : minRaw * invFreqMs;
                double maxMs = maxRaw * invFreqMs;

                log.LogInfo($"[Perf] {(Region)idx,-20} inc={incMs,8:F2}ms ({pctInc,6:F2}%)  exc={excMs,8:F2}ms ({pctExc,6:F2}%)  calls={calls}  min={minMs:F4}ms  max={maxMs:F4}ms");
            }
        }
#else
        public static void Reset()
        { }

        private sealed class ScopeStack
        {
            public int Depth;
            public Frame[] Frames;

            public void Reset() => Depth = 0;

            [Conditional("COOKBOOK_PERF")]
            public void Push(Region r, long t0) { }
            public Frame Pop() => Frames[--Depth];
        }

        private struct Frame
        { }

        private static readonly ThreadLocal<ScopeStack> _stack = new ThreadLocal<ScopeStack>(() => new ScopeStack());
        public readonly struct Scope : IDisposable
        {
            public Scope(Region r) { }
            public void Dispose() { }
        }

        public static Scope Measure(Region r) => new Scope(r);

        [Conditional("COOKBOOK_PERF")]
        public static void LogSummary(ManualLogSource log, int topN = 0, bool sortByExclusive = true) { }
#endif
    }
    public static partial class PerfProfile
    {
#if COOKBOOK_TRACE_LOGS
        [Conditional("COOKBOOK_TRACE_LOGS")]
        public static void TraceChainDrop(
            ManualLogSource log,
            string stage,
            string reason,
            Func<string> chainSummary,
            Func<string> candidateName = null)
        {
            if (log == null) return;

            string chainText = chainSummary != null ? chainSummary() : "<null>";
            if (candidateName != null)
            {
                string cand = candidateName();
                log.LogInfo($"[Planner][{stage}] DROP: {reason} | chain={chainText} | cand={cand}");
            }
            else
            {
                log.LogInfo($"[Planner][{stage}] DROP: {reason} | chain={chainText}");
            }
        }

        [Conditional("COOKBOOK_TRACE_LOGS")]
        public static void TraceChainAdd(
            ManualLogSource log,
            string stage,
            Func<string> chainSummary)
        {
            if (log == null) return;

            string chainText = chainSummary != null ? chainSummary() : "<null>";
            log.LogInfo($"[Planner][{stage}] ADD: chain={chainText}");
        }
#else
        [Conditional("COOKBOOK_TRACE_LOGS")]
        public static void TraceChainDrop(
           ManualLogSource log,
           string stage,
           string reason,
           Func<string> chainSummary,
           Func<string> candidateName = null)
        { }

        [Conditional("COOKBOOK_TRACE_LOGS")]
        public static void TraceChainAdd(
            ManualLogSource log,
            string stage,
            Func<string> chainSummary)
        { }
#endif
    }
}
