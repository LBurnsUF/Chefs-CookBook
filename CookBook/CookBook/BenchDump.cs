#if COOKBOOK_PERF
using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using SysPath = System.IO.Path;
using SysDir = System.IO.Directory;
using SysFile = System.IO.File;
using System.Text;

namespace CookBook
{
    internal static class BenchDump
    {
        private static ManualLogSource _log;
        internal static bool DumpRequested;

        internal static void Init(ManualLogSource log) => _log = log;

        /// <summary>
        /// Dumps the current snapshot and recipe state to a JSON file.
        /// </summary>
        internal static void DumpSnapshot(in InventorySnapshot snap)
        {
            try
            {
                string dir = SysPath.Combine(
                    BepInEx.Paths.PluginPath,
                    "CookBook_BenchData");

                SysDir.CreateDirectory(dir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string path = SysPath.Combine(dir, $"bench_snapshot_{timestamp}.json");

                var sb = new StringBuilder(64 * 1024);

                sb.AppendLine("{");


                sb.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow:O}\",");
                sb.AppendLine($"  \"itemCount\": {ItemCatalog.itemCount},");
                sb.AppendLine($"  \"equipmentCount\": {EquipmentCatalog.equipmentCount},");
                sb.AppendLine($"  \"totalDefCount\": {RecipeProvider.TotalDefCount},");
                sb.AppendLine($"  \"maskWords\": {RecipeProvider.MaskWords},");
                sb.AppendLine($"  \"maxDepth\": {snap.maxDepth},");
                sb.AppendLine($"  \"canScrapDrones\": {BoolStr(snap.CanScrapDrones)},");
                sb.AppendLine($"  \"isPoolingEnabled\": {BoolStr(snap.IsPoolingEnabled)},");

                WriteIntArray(sb, "physicalStacks", snap.PhysicalStacks);
                sb.AppendLine(",");

                WriteIntArray(sb, "dronePotential", snap.DronePotential);
                sb.AppendLine(",");

                WriteUlongArray(sb, "physicalMask", snap.PhysicalMask);
                sb.AppendLine(",");
                WriteUlongArray(sb, "droneMask", snap.DroneMask);
                sb.AppendLine(",");

                sb.Append("  \"corruptedIndices\": [");
                if (snap.CorruptedIndices != null && snap.CorruptedIndices.Count > 0)
                {
                    bool first = true;
                    foreach (var idx in snap.CorruptedIndices)
                    {
                        if (!first) sb.Append(", ");
                        sb.Append((int)idx);
                        first = false;
                    }
                }
                sb.AppendLine("],");

                WriteScrapCandidates(sb, snap.AllScrapCandidates);
                sb.AppendLine(",");

                WriteAlliedStacks(sb, snap.AlliedPhysicalStacks);
                sb.AppendLine(",");

                WriteTradesRemaining(sb, snap.TradesRemaining);
                sb.AppendLine(",");

                WriteRecipes(sb, "filteredRecipes", snap.FilteredRecipes);
                sb.AppendLine(",");

                WriteRecipes(sb, "masterRecipes", RecipeProvider.Recipes);
                sb.AppendLine(",");

                WriteDerivedIndices(sb);
                sb.AppendLine(",");

                WriteItemTierMap(sb);
                sb.AppendLine(",");

                WriteItemNameMap(sb);
                sb.AppendLine();

                sb.AppendLine("}");

                SysFile.WriteAllText(path, sb.ToString());
                _log?.LogInfo($"[BenchDump] Snapshot written to: {path}");
            }
            catch (Exception ex)
            {
                _log?.LogError($"[BenchDump] Failed to write snapshot: {ex}");
            }
        }

        private static string BoolStr(bool v) => v ? "true" : "false";

        private static void WriteIntArray(StringBuilder sb, string name, int[] arr)
        {
            sb.Append($"  \"{name}\": [");
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(arr[i]);
                }
            }
            sb.Append(']');
        }

        private static void WriteUlongArray(StringBuilder sb, string name, ulong[] arr)
        {
            sb.Append($"  \"{name}\": [");
            if (arr != null)
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append('"');
                    sb.Append(arr[i]);
                    sb.Append('"');
                }
            }
            sb.Append(']');
        }

        private static void WriteRecipes(StringBuilder sb, string name, IReadOnlyList<ChefRecipe> recipes)
        {
            sb.AppendLine($"  \"{name}\": [");
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Count; i++)
                {
                    var r = recipes[i];
                    if (i > 0) sb.AppendLine(",");
                    sb.Append($"    {{\"resultIndex\":{r.ResultIndex},\"resultCount\":{r.ResultCount},");
                    sb.Append($"\"ingA\":{r.IngA},\"ingB\":{r.IngB},");
                    sb.Append($"\"countA\":{r.CountA},\"countB\":{r.CountB}}}");
                }
                sb.AppendLine();
            }
            sb.Append("  ]");
        }

        private static void WriteScrapCandidates(StringBuilder sb, Dictionary<int, List<DroneCandidate>> candidates)
        {
            sb.AppendLine("  \"scrapCandidates\": {");
            if (candidates != null && candidates.Count > 0)
            {
                bool firstKey = true;
                foreach (var kvp in candidates)
                {
                    if (!firstKey) sb.AppendLine(",");
                    firstKey = false;
                    sb.Append($"    \"{kvp.Key}\": [");
                    for (int i = 0; i < kvp.Value.Count; i++)
                    {
                        var c = kvp.Value[i];
                        if (i > 0) sb.Append(", ");
                        sb.Append($"{{\"minionId\":\"{c.MinionMasterNetId}\",");
                        sb.Append($"\"upgradeCount\":{c.UpgradeCount},");
                        sb.Append($"\"droneIdx\":{(int)c.DroneIdx}}}");
                    }
                    sb.Append(']');
                }
                sb.AppendLine();
            }
            sb.Append("  }");
        }

        private static void WriteAlliedStacks(StringBuilder sb, Dictionary<NetworkUser, int[]> alliedStacks)
        {
            sb.AppendLine("  \"alliedPhysicalStacks\": {");
            if (alliedStacks != null && alliedStacks.Count > 0)
            {
                bool firstKey = true;
                int playerIdx = 0;
                foreach (var kvp in alliedStacks)
                {
                    if (!firstKey) sb.AppendLine(",");
                    firstKey = false;
                    sb.Append($"    \"{playerIdx}\": [");
                    var stacks = kvp.Value;
                    if (stacks != null)
                    {
                        for (int i = 0; i < stacks.Length; i++)
                        {
                            if (i > 0) sb.Append(", ");
                            sb.Append(stacks[i]);
                        }
                    }
                    sb.Append(']');
                    playerIdx++;
                }
                sb.AppendLine();
            }
            sb.Append("  }");
        }

        private static void WriteTradesRemaining(StringBuilder sb, Dictionary<NetworkUser, int> trades)
        {
            sb.AppendLine("  \"tradesRemaining\": {");
            if (trades != null && trades.Count > 0)
            {
                bool firstKey = true;
                int playerIdx = 0;
                foreach (var kvp in trades)
                {
                    if (!firstKey) sb.AppendLine(",");
                    firstKey = false;
                    sb.Append($"    \"{playerIdx}\": {kvp.Value}");
                    playerIdx++;
                }
                sb.AppendLine();
            }
            sb.Append("  }");
        }

        private static void WriteDerivedIndices(StringBuilder sb)
        {
            sb.AppendLine("  \"consumersByIngredient\": [");
            var consumers = RecipeProvider.ConsumersByIngredient;
            if (consumers != null)
            {
                for (int i = 0; i < consumers.Length; i++)
                {
                    if (i > 0) sb.AppendLine(",");
                    sb.Append("    [");
                    var arr = consumers[i];
                    if (arr != null)
                    {
                        for (int j = 0; j < arr.Length; j++)
                        {
                            if (j > 0) sb.Append(", ");
                            sb.Append(arr[j]);
                        }
                    }
                    sb.Append(']');
                }
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            sb.AppendLine("  \"producersByResult\": [");
            var producers = RecipeProvider.ProducersByResult;
            if (producers != null)
            {
                for (int i = 0; i < producers.Length; i++)
                {
                    if (i > 0) sb.AppendLine(",");
                    sb.Append("    [");
                    var arr = producers[i];
                    if (arr != null)
                    {
                        for (int j = 0; j < arr.Length; j++)
                        {
                            if (j > 0) sb.Append(", ");
                            sb.Append(arr[j]);
                        }
                    }
                    sb.Append(']');
                }
                sb.AppendLine();
            }
            sb.AppendLine("  ],");

            WriteIntArray(sb, "resultIdxByRecipe", RecipeProvider.ResultIdxByRecipe);
            sb.AppendLine(",");
            WriteIntArray(sb, "ingAByRecipe", RecipeProvider.IngAByRecipe);
            sb.AppendLine(",");
            WriteIntArray(sb, "ingBByRecipe", RecipeProvider.IngBByRecipe);
            sb.AppendLine(",");

            sb.Append("  \"isDoubleIngredientRecipe\": [");
            var isDouble = RecipeProvider.IsDoubleIngredientRecipe;
            if (isDouble != null)
            {
                for (int i = 0; i < isDouble.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(isDouble[i] ? "true" : "false");
                }
            }
            sb.AppendLine("],");

            sb.AppendLine("  \"reqMasks\": [");
            var masks = RecipeProvider.ReqMasks;
            if (masks != null)
            {
                for (int i = 0; i < masks.Length; i++)
                {
                    if (i > 0) sb.AppendLine(",");
                    sb.Append("    [");
                    var m = masks[i];
                    if (m != null)
                    {
                        for (int j = 0; j < m.Length; j++)
                        {
                            if (j > 0) sb.Append(", ");
                            sb.Append('"');
                            sb.Append(m[j]);
                            sb.Append('"');
                        }
                    }
                    sb.Append(']');
                }
                sb.AppendLine();
            }
            sb.Append("  ]");
        }

        private static void WriteItemNameMap(StringBuilder sb)
        {
            sb.AppendLine("  \"itemNames\": {");
            int totalDef = RecipeProvider.TotalDefCount;
            int itemCount = ItemCatalog.itemCount;
            bool first = true;

            for (int i = 0; i < totalDef; i++)
            {
                string name = null;
                if (i < itemCount)
                {
                    var def = ItemCatalog.GetItemDef((ItemIndex)i);
                    if (def != null) name = def.name;
                }
                else
                {
                    int eqIdx = i - itemCount;
                    var def = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)eqIdx);
                    if (def != null) name = def.name;
                }

                if (name == null) continue;

                if (!first) sb.AppendLine(",");
                first = false;
                sb.Append($"    \"{i}\": \"{EscapeJson(name)}\"");
            }
            sb.AppendLine();
            sb.Append("  }");
        }

        private static void WriteItemTierMap(StringBuilder sb)
        {
            sb.AppendLine("  \"itemTiers\": {");
            int itemCount = ItemCatalog.itemCount;
            bool first = true;

            for (int i = 0; i < itemCount; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (def == null) continue;

                if (!first) sb.AppendLine(",");
                first = false;
                sb.Append($"    \"{i}\": {(int)def.tier}");
            }
            sb.AppendLine();
            sb.Append("  }");
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
#endif
