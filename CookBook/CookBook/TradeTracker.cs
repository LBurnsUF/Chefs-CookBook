using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CookBook
{
    internal static class TradeTracker
    {
        private static readonly Dictionary<NetworkInstanceId, int> _usedTrades = new();
        private static int _maxTradesCache = 3; // Default fallback

        internal static void Init()
        {
            SolusVendorShrineController.OnItemScrapped += OnItemScrapped;

            StateController.OnChefStageEntered += RefreshLimits;
        }

        private static void RefreshLimits()
        {
            _usedTrades.Clear();

            var shrine = ComputationalExchangeController.instance?.solusVendorShrine?.GetComponent<SolusVendorShrineController>();
            if (shrine)
            {
                _maxTradesCache = shrine.maxTrades;
            }
        }

        private static void OnItemScrapped(ItemTier tier, Interactor interactor)
        {
            if (!interactor) return;

            var body = interactor.GetComponent<CharacterBody>();
            var networkUser = body?.master?.playerCharacterMasterController?.networkUser;

            if (networkUser)
            {
                var netId = networkUser.netId;
                if (!_usedTrades.ContainsKey(netId)) _usedTrades[netId] = 0;

                _usedTrades[netId]++;
                CookBook.Log.LogDebug($"[TradeTracker] {networkUser.userName} used a trade ({_usedTrades[netId]}/{_maxTradesCache})");

                InventoryTracker.TriggerUpdate();
            }
        }

        public static int GetRemainingTrades(NetworkUser user)
        {
            if (!user) return 0;
            int used = _usedTrades.TryGetValue(user.netId, out int count) ? count : 0;
            return Mathf.Max(0, _maxTradesCache - used);
        }

        public static Dictionary<NetworkUser, int> GetRemainingTradeCounts()
        {
            var map = new Dictionary<NetworkUser, int>();
            foreach (var playerController in PlayerCharacterMasterController.instances)
            {
                var netUser = playerController.networkUser;
                if (!netUser) continue;
                map[netUser] = GetRemainingTrades(netUser);
            }
            return map;
        }
    }
}