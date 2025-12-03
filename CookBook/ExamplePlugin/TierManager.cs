using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CookBook
{
    internal class TierManager
    {
        // Raised on tier order change
        internal static event System.Action OnTierOrderChanged;

        private static bool _initialized;
        private static ManualLogSource _log;

        private static Dictionary<ItemTier, int> _orderMap = new Dictionary<ItemTier, int>();
        private static readonly HashSet<ItemTier> _seenTiers = new HashSet<ItemTier>();

        // Default sorting order
        private static readonly ItemTier[] DefaultOrder =
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

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;

            log.LogInfo("TierManager.Init()");
        }

        static TierManager()
        {
            _seenTiers = new HashSet<ItemTier>(DefaultOrder);
            _orderMap = BuildMapFrom(DefaultOrder);
        }

        private static Dictionary<ItemTier, int> BuildMapFrom(ItemTier[] arr)
        {
            var map = new Dictionary<ItemTier, int>(arr.Length);
            for (int i = 0; i < arr.Length; i++)
                map[arr[i]] = i;
            return map;
        }

        /// <summary>
        /// Gets the integer rank for a given tier.
        /// </summary>
        /// 
        internal static int Rank(ItemTier tier)
        {
            _seenTiers.Add(tier);

            // If explicitly mapped, use it
            if (_orderMap.TryGetValue(tier, out int rank))
            {
                return rank; 
            }

            // Default: unknown/modded tiers after natives
            return _orderMap.Count + (int)tier;
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

        // ---------------------- Sorting Helpers ----------------------------
        /// <summary>
        /// Compare two CraftableEntry objects.
        /// Items sorted by tier/name; Equipment sorted by name.
        /// </summary>
        internal static int CompareCraftableEntries(
            CraftPlanner.CraftableEntry a,
            CraftPlanner.CraftableEntry b)
        {
            // Group by result kind | items, equipment
            int kindCmp = a.ResultKind.CompareTo(b.ResultKind);
            if (kindCmp != 0)
            {
                return kindCmp;
            }

            // Sort Items
            if (a.ResultKind == RecipeResultKind.Item)
            {
                return CompareItems(a.ResultItem, b.ResultItem);
            }
                
            // Order Equipment Alphabetically
            var defA = EquipmentCatalog.GetEquipmentDef(a.ResultEquipment);
            var defB = EquipmentCatalog.GetEquipmentDef(b.ResultEquipment);

            string nameA = defA ? defA.nameToken : a.ResultEquipment.ToString();
            string nameB = defB ? defB.nameToken : b.ResultEquipment.ToString();
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
                return tierCmp;

            string nameA = defA ? defA.nameToken : a.ToString();
            string nameB = defB ? defB.nameToken : b.ToString();
            return string.Compare(nameA, nameB, StringComparison.Ordinal);
        }
    }
}
