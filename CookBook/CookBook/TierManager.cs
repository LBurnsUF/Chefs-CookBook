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
        private const ItemTier EquipmentPseudoTier = (ItemTier)500;

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

        // ---------------------- Events ----------------------------
        internal static event System.Action OnTierOrderChanged;

        internal static void OnTierPriorityChanged(object sender, EventArgs e)
        {
            OnTierOrderChanged?.Invoke();
        }

        /// <summary>
        /// Update the tier order
        /// </summary>
        internal static void SetOrder(ItemTier[] newOrder)
        {
            _orderMap = BuildMapFrom(newOrder);
            OnTierOrderChanged?.Invoke();
        }

        // ---------------------- Sorting Logic ----------------------------
        /// <summary>
        /// Compare two CraftableEntry objects.
        /// Items/Equipment sorted by tier/name;
        /// </summary>
        internal static int CompareCraftableEntries(CraftPlanner.CraftableEntry a, CraftPlanner.CraftableEntry b)
        {
            bool aIsItem = a.ResultIndex < ItemCatalog.itemCount;
            ItemTier tierA = aIsItem ? ItemCatalog.GetItemDef((ItemIndex)a.ResultIndex).tier : EquipmentPseudoTier;
            string nameA = GetDisplayName(a);

            bool bIsItem = b.ResultIndex < ItemCatalog.itemCount;
            ItemTier tierB = bIsItem ? ItemCatalog.GetItemDef((ItemIndex)b.ResultIndex).tier : EquipmentPseudoTier;
            string nameB = GetDisplayName(b);

            int rankA = Rank(tierA);
            int rankB = Rank(tierB);
            int tierCmp = rankA.CompareTo(rankB);

            if (tierCmp != 0) return tierCmp;

            return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the integer rank for a given tier.
        /// </summary>
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

        private static string GetDisplayName(CraftPlanner.CraftableEntry entry)
        {
            if (entry.ResultIndex < ItemCatalog.itemCount)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)entry.ResultIndex);
                return def ? Language.GetString(def.nameToken) : entry.ResultIndex.ToString();
            }
            var equipIdx = (EquipmentIndex)(entry.ResultIndex - ItemCatalog.itemCount);
            var eDef = EquipmentCatalog.GetEquipmentDef(equipIdx);
            return eDef ? Language.GetString(eDef.nameToken) : equipIdx.ToString();
        }

        // ---------------------- Runtime Helpers ----------------------------
        /// <summary>
        /// Collects all tiers used by any item in the ItemCatalog.
        /// </summary>
        internal static ItemTier[] DiscoverTiersFromCatalog()
        {
            var set = new HashSet<ItemTier>(_seenTiers);
            for (int i = 0; i < ItemCatalog.itemCount; i++)
            {
                var def = ItemCatalog.GetItemDef((ItemIndex)i);
                if (def) set.Add(def.tier);
            }

            set.Add(EquipmentPseudoTier);

            foreach (var t in set) _seenTiers.Add(t);
            return set.ToArray();
        }

        public static string GetFriendlyName(ItemTier tier)
        {
            if (tier == EquipmentPseudoTier) return "Equipment";
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

        /// <summary>
        /// Convert an ItemTier[] ordering into the CSV format used by the config.
        /// </summary>
        internal static string ToCsv(ItemTier[] order)
        {
            return string.Join(",", order.Select(t => t.ToString()));
        }

        public static TierPriority GetDefaultPriorityForTier(ItemTier tier)
        {
            if (tier == ItemTier.Tier3 || tier == ItemTier.VoidTier3) return TierPriority.Highest;
            if (tier == ItemTier.Boss) return TierPriority.High;
            if (tier == ItemTier.Tier2 || tier == ItemTier.VoidTier2) return TierPriority.Medium;
            if (tier == ItemTier.Tier1 || tier == ItemTier.VoidTier1) return TierPriority.Low;
            return TierPriority.Lowest;
        }

        private static Dictionary<ItemTier, int> BuildMapFrom(ItemTier[] arr)
        {
            var map = new Dictionary<ItemTier, int>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                map[arr[i]] = i;
            return map;
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

        public enum TierPriority
        {
            Highest,
            High,
            Medium,
            Low,
            Lowest
        }
    }
}
