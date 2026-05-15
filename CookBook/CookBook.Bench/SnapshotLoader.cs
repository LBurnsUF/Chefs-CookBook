using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace CookBook.Bench
{
    internal static class SnapshotLoader
    {
        internal class BenchData
        {
            public int ItemCount { get; set; }
            public int EquipmentCount { get; set; }
            public int TotalDefCount { get; set; }
            public int MaskWords { get; set; }
            public int MaxDepth { get; set; }
            public bool CanScrapDrones { get; set; }
            public bool IsPoolingEnabled { get; set; }

            public int[] PhysicalStacks { get; set; }
            public int[] DronePotential { get; set; }
            public ulong[] PhysicalMask { get; set; }
            public ulong[] DroneMask { get; set; }
            public int[] CorruptedIndices { get; set; }

            public List<ChefRecipe> FilteredRecipes { get; set; }
            public List<ChefRecipe> MasterRecipes { get; set; }

            public int[][] ConsumersByIngredient { get; set; }
            public int[][] ProducersByResult { get; set; }
            public int[] ResultIdxByRecipe { get; set; }
            public int[] IngAByRecipe { get; set; }
            public int[] IngBByRecipe { get; set; }
            public bool[] IsDoubleIngredientRecipe { get; set; }
            public ulong[][] ReqMasks { get; set; }

            public Dictionary<string, string> ItemNames { get; set; }
        }

        internal static BenchData Load(string path)
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var data = new BenchData
            {
                ItemCount = root.GetProperty("itemCount").GetInt32(),
                EquipmentCount = root.GetProperty("equipmentCount").GetInt32(),
                TotalDefCount = root.GetProperty("totalDefCount").GetInt32(),
                MaskWords = root.GetProperty("maskWords").GetInt32(),
                MaxDepth = root.GetProperty("maxDepth").GetInt32(),
                CanScrapDrones = root.GetProperty("canScrapDrones").GetBoolean(),
                IsPoolingEnabled = root.GetProperty("isPoolingEnabled").GetBoolean(),
            };

            data.PhysicalStacks = ReadIntArray(root, "physicalStacks");
            data.DronePotential = ReadIntArray(root, "dronePotential");
            data.PhysicalMask = ReadUlongArray(root, "physicalMask");
            data.DroneMask = ReadUlongArray(root, "droneMask");
            data.CorruptedIndices = ReadIntArray(root, "corruptedIndices");

            data.FilteredRecipes = ReadRecipes(root, "filteredRecipes");
            data.MasterRecipes = ReadRecipes(root, "masterRecipes");

            data.ConsumersByIngredient = ReadJaggedIntArray(root, "consumersByIngredient");
            data.ProducersByResult = ReadJaggedIntArray(root, "producersByResult");
            data.ResultIdxByRecipe = ReadIntArray(root, "resultIdxByRecipe");
            data.IngAByRecipe = ReadIntArray(root, "ingAByRecipe");
            data.IngBByRecipe = ReadIntArray(root, "ingBByRecipe");
            data.IsDoubleIngredientRecipe = ReadBoolArray(root, "isDoubleIngredientRecipe");
            data.ReqMasks = ReadJaggedUlongArray(root, "reqMasks");

            data.ItemNames = new Dictionary<string, string>();
            if (root.TryGetProperty("itemNames", out var namesEl))
            {
                foreach (var prop in namesEl.EnumerateObject())
                    data.ItemNames[prop.Name] = prop.Value.GetString();
            }

            return data;
        }

        private static int[] ReadIntArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var arr)) return Array.Empty<int>();
            var result = new int[arr.GetArrayLength()];
            int i = 0;
            foreach (var el in arr.EnumerateArray())
                result[i++] = el.GetInt32();
            return result;
        }

        private static ulong[] ReadUlongArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var arr)) return Array.Empty<ulong>();
            var result = new ulong[arr.GetArrayLength()];
            int i = 0;
            foreach (var el in arr.EnumerateArray())
                result[i++] = ulong.Parse(el.GetString());
            return result;
        }

        private static bool[] ReadBoolArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var arr)) return Array.Empty<bool>();
            var result = new bool[arr.GetArrayLength()];
            int i = 0;
            foreach (var el in arr.EnumerateArray())
                result[i++] = el.GetBoolean();
            return result;
        }

        private static int[][] ReadJaggedIntArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var arr)) return Array.Empty<int[]>();
            var result = new int[arr.GetArrayLength()][];
            int i = 0;
            foreach (var outer in arr.EnumerateArray())
            {
                var inner = new int[outer.GetArrayLength()];
                int j = 0;
                foreach (var el in outer.EnumerateArray())
                    inner[j++] = el.GetInt32();
                result[i++] = inner;
            }
            return result;
        }

        private static ulong[][] ReadJaggedUlongArray(JsonElement root, string prop)
        {
            if (!root.TryGetProperty(prop, out var arr)) return Array.Empty<ulong[]>();
            var result = new ulong[arr.GetArrayLength()][];
            int i = 0;
            foreach (var outer in arr.EnumerateArray())
            {
                var inner = new ulong[outer.GetArrayLength()];
                int j = 0;
                foreach (var el in outer.EnumerateArray())
                    inner[j++] = ulong.Parse(el.GetString());
                result[i++] = inner;
            }
            return result;
        }

        private static List<ChefRecipe> ReadRecipes(JsonElement root, string prop)
        {
            var list = new List<ChefRecipe>();
            if (!root.TryGetProperty(prop, out var arr)) return list;

            foreach (var el in arr.EnumerateArray())
            {
                list.Add(new ChefRecipe(
                    resultIndex: el.GetProperty("resultIndex").GetInt32(),
                    resultCount: el.GetProperty("resultCount").GetInt32(),
                    ingA: el.GetProperty("ingA").GetInt32(),
                    ingB: el.GetProperty("ingB").GetInt32(),
                    countA: (byte)el.GetProperty("countA").GetInt32(),
                    countB: (byte)el.GetProperty("countB").GetInt32()
                ));
            }
            return list;
        }
    }
}
