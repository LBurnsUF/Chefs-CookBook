using RoR2;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace CookBook
{
    internal static class TradeTracker
    {
        // Proxy to track trades per player locally
        private static readonly Dictionary<NetworkInstanceId, int> _usedTrades = new();
        private static int _maxTradesCache = 3; // Default fallback

        internal static void Init()
        {
            // Subscribe to the public static event provided by the game
            SolusVendorShrineController.OnItemScrapped += OnItemScrapped;

            // Sync maxTrades when the stage starts
            StateController.OnChefStageEntered += RefreshLimits;
        }

        private static void RefreshLimits()
        {
            _usedTrades.Clear();

            // Safely grab the limit from the actual instance in the scene
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

                // Alert the tracker to re-evaluate what allies can provide
                InventoryTracker.TriggerUpdate();
            }
        }

        public static int GetRemainingTrades(NetworkUser user)
        {
            if (!user) return 0;
            int used = _usedTrades.TryGetValue(user.netId, out int count) ? count : 0;
            return Mathf.Max(0, _maxTradesCache - used);
        }
    }
}