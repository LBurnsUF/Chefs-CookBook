using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CookBook
{
    internal static class TierManager
    {
        // ---------------------- Fields  ----------------------------
        private static bool _initialized;
        private static ManualLogSource _log;
        private static readonly ItemTier[] DefaultOrder;
        private static readonly HashSet<ItemTier> _seenTiers;
        private static Dictionary<ItemTier, int> _orderMap;

        // Events
        internal static event System.Action OnTierOrderChanged;

        // ---------------------- Initialization  ----------------------------
        static TierManager()
        {
            DefaultOrder = new[]
            {
                ItemTier.Tier3,
                ItemTier.Boss,
                ItemTier.Tier2,
                ItemTier.Tier1,
                ItemTier.VoidTier3,
                ItemTier.VoidTier2,
                ItemTier.VoidTier1,
                ItemTier.Lunar,
                ItemTier.AssignedAtRuntime,
                ItemTier.NoTier
            };
            _seenTiers = new HashSet<ItemTier>(DefaultOrder);
            _orderMap = BuildMapFrom(DefaultOrder);
        }

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
        }

        // ---------------------- Tier Events ----------------------------
        internal static void OnTierPriorityChanged(object sender, EventArgs e)
        {
            OnTierOrderChanged?.Invoke();
        }

        // ---------------------- Runtime Helpers ----------------------------
        /// <summary>
        /// Collects all tiers used by any item in the ItemCatalog.
        /// </summary>
        internal static ItemTier[] DiscoverTiersFromCatalog()
        {
            var set = new HashSet<ItemTier>(_seenTiers);
            int len = ItemCatalog.itemCount;
            for (int i = 0; i < len; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (!def)
                    continue;

                set.Add(def.tier);
            }

            foreach (var t in set)
            {
                _seenTiers.Add(t);
            }

            return set.ToArray();
        }

        /// <summary>
        /// Convert an ItemTier[] ordering into the CSV format used by the config.
        /// </summary>
        internal static string ToCsv(ItemTier[] order)
        {
            return string.Join(",", order.Select(t => t.ToString()));
        }

        /// <summary>
        /// Merge the current config order with a discovered set of tiers.
        /// Preserves config order.
        /// Appends newly discovered tiers to the end.
        /// </summary>
        internal static ItemTier[] MergeOrder(ItemTier[] fromConfig, ItemTier[] discovered)
        {
            var list = new List<ItemTier>(fromConfig.Length + discovered.Length);
            var seen = new HashSet<ItemTier>();

            foreach (var t in fromConfig)
            {
                if (seen.Add(t))
                    list.Add(t);
            }

            foreach (var t in discovered)
            {
                if (seen.Add(t))
                    list.Add(t);
            }

            // fallback
            return list.Count > 0 ? list.ToArray() : DefaultOrder;
        }

        // ---------------------- Sorting Helpers ----------------------------
        /// <summary>
        /// Compare two CraftableEntry objects.
        /// Items sorted by tier/name; Equipment sorted by name.
        /// </summary>
        internal static int CompareCraftableEntries(
            CraftPlanner.CraftableEntry a,
            CraftPlanner.CraftableEntry b)
        {
            bool aIsItem = a.ResultIndex < ItemCatalog.itemCount;
            bool bIsItem = b.ResultIndex < ItemCatalog.itemCount;

            // Group by type
            if (aIsItem != bIsItem)
            {
                return aIsItem ? -1 : 1;
            }

            // Both are items
            if (aIsItem)
            {
                return CompareItems((ItemIndex)a.ResultIndex, (ItemIndex)b.ResultIndex);
            }

            // Compare Equipment (Alphabetical)
            int offset = ItemCatalog.itemCount;
            var equipA = (EquipmentIndex)(a.ResultIndex - offset);
            var equipB = (EquipmentIndex)(b.ResultIndex - offset);

            var defA = EquipmentCatalog.GetEquipmentDef(equipA);
            var defB = EquipmentCatalog.GetEquipmentDef(equipB);

            string nameA = defA ? defA.nameToken : equipA.ToString();
            string nameB = defB ? defB.nameToken : equipB.ToString();
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }

        /// <summary>
        /// Compare two ItemIndex values (tier first, then name).
        /// </summary>
        internal static int CompareItems(ItemIndex a, ItemIndex b)
        {
            var defA = ItemCatalog.GetItemDef(a);
            var defB = ItemCatalog.GetItemDef(b);

            var tierA = defA ? defA.tier : ItemTier.NoTier;
            var tierB = defB ? defB.tier : ItemTier.NoTier;

            int rankA = Rank(tierA);
            int rankB = Rank(tierB);
            int tierCmp = rankA.CompareTo(rankB);
            if (tierCmp != 0)
            {
                return tierCmp;
            }

            string nameA = defA ? defA.nameToken : a.ToString();
            string nameB = defB ? defB.nameToken : b.ToString();
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }

        /// <summary>
        /// Gets the integer rank for a given tier.
        /// </summary>
        /// 
        internal static int Rank(ItemTier tier)
        {
            int bucketWeight = 500;
            if (CookBook.TierPriorities.TryGetValue(tier, out var configEntry))
            {
                bucketWeight = (int)configEntry.Value * 100;
            }

            int tieBreaker = 50;
            if (_orderMap.TryGetValue(tier, out int csvPosition))
            {
                tieBreaker = csvPosition;
            }

            return bucketWeight + tieBreaker;
        }

        // ---------------------- Helpers ----------------------------
        private static readonly Dictionary<string, string> FriendlyTierNames = new()
{
            { "Tier1", "Common" },
            { "Tier2", "Uncommon" },
            { "Tier3", "Legendary" },
            { "Boss", "Boss" },
            { "Lunar", "Lunar" },
            { "VoidTier1", "Void Common" },
            { "VoidTier2", "Void Uncommon" },
            { "VoidTier3", "Void Legendary" },
            { "AssignedAtRuntime", "Adaptive" },
            { "NoTier", "Misc" },
            { "FoodTier", "Chef Ingredients" },
            { "VoidBoss", "Void Boss" }
        };

        public static TierPriority GetDefaultPriorityForTier(ItemTier tier)
        {
            if (tier == ItemTier.Tier3 || tier == ItemTier.VoidTier3) return TierPriority.Highest;
            if (tier == ItemTier.Boss) return TierPriority.High;
            if (tier == ItemTier.Tier2 || tier == ItemTier.VoidTier2) return TierPriority.Medium;
            if (tier == ItemTier.Tier1 || tier == ItemTier.VoidTier1) return TierPriority.Low;
            return TierPriority.Lowest;
        }

        public enum TierPriority
        {
            Highest,
            High,
            Medium,
            Low,
            Lowest
        }

        public static string GetFriendlyName(ItemTier tier)
        {
            string internalName = tier.ToString();

            var tierDef = ItemTierCatalog.GetItemTierDef(tier);
            if (tierDef != null && !string.IsNullOrEmpty(tierDef.name))
            {
                internalName = tierDef.name;
            }

            if (internalName.EndsWith("Def"))
                internalName = internalName.Substring(0, internalName.Length - 3);
            if (internalName.EndsWith(" Tier Def"))
                internalName = internalName.Replace(" Tier Def", "");

            if (FriendlyTierNames.TryGetValue(internalName, out var friendly))
                return friendly;

            return System.Text.RegularExpressions.Regex.Replace(internalName, "([a-z])([A-Z])", "$1 $2");
        }

        private static Dictionary<ItemTier, int> BuildMapFrom(ItemTier[] arr)
        {
            var map = new Dictionary<ItemTier, int>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                map[arr[i]] = i;
            return map;
        }

        /// <summary>
        /// Update the tier order
        /// </summary>
        internal static void SetOrder(ItemTier[] newOrder)
        {
            _orderMap = BuildMapFrom(newOrder);
            OnTierOrderChanged?.Invoke();
        }

        internal static ItemTier[] GetAllKnownTiers()
        {
            return _seenTiers.ToArray();
        }
        internal static ItemTier[] ParseTierOrder(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
                return DefaultOrder;

            var parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<ItemTier>();
            foreach (var p in parts)
            {
                if (Enum.TryParse<ItemTier>(p.Trim(), out var tier))
                    list.Add(tier);
            }

            // Fall back if user nukes config
            return list.Count > 0 ? list.ToArray() : DefaultOrder;
        }
    }
}
