using BepInEx.Logging;
using RoR2;
using System;

namespace CookBook
{
    /// <summary>
    /// Tracks local player's items and raises an event when they change
    /// </summary>
    internal static class InventoryTracker
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _enabled;

        // Init local player's inventory
        private static Inventory _localInventory;
        private static int[] _stacks;

        /// <summary>
        /// Raised whenever an inventory's item counts change
        /// int[] is the current snapshot (do NOT mutate externally)
        /// </summary>
        internal static event Action<int[]> OnInventoryChanged;

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _log.LogInfo("InventoryTracker.Init()");

        }

        internal static void Enable()
        {
            if (_enabled)
                return;

            _enabled = true;
            _log.LogInfo("InventoryTracker.Enable()");

            CharacterBody.onBodyInventoryChangedGlobal += OnBodyInventoryChanged;
        }

        internal static void Disable()
        {
            if (!_enabled)
                return;

            _enabled = false;
            _log.LogInfo("InventoryTracker.Disable()");

            CharacterBody.onBodyInventoryChangedGlobal -= OnBodyInventoryChanged;

        }

        /// <summary>
        /// Called by RoR2 whenever ANY body's inventory changes.
        /// We filter it down to just the local player's body.
        /// </summary>
        private static void OnBodyInventoryChanged(CharacterBody body) {
            if (body == null || body.inventory == null)
                return;

            var localUser = GetLocalUser();
            if (localUser == null || localUser.cachedBody == null)
                return;

            var localMaster = localUser.cachedBody.master;
            if (localMaster == null)
                return;

            // handle non localuser cases
            if (body.master != localMaster)
            {
                return;
            }

            var inv = body.inventory;

            if (_localInventory == null)
            {
                _localInventory = inv;
                _log.LogInfo("InventoryTracker: bound to local player inventory (via CharacterBody event)");
            }
            
            const int len = (int)ItemIndex.Count;
             
            // Build or refresh the snapshot.
            if (_stacks == null || _stacks.Length != len)
                _stacks = new int[len];

            for (int i = 0; i < len; i++)
                _stacks[i] = inv.GetItemCount((ItemIndex)i);


            _log.LogInfo("InventoryTracker: snapshot after change:");
            for (int i = 0; i < len; i++)
            {
                int count = _stacks[i];
                if (count <= 0)
                    continue;

                ItemIndex idx = (ItemIndex)i;
                ItemDef def = ItemCatalog.GetItemDef(idx);
                string name = def ? def.nameToken : idx.ToString();

                _log.LogInfo($"  [Tracker] {name} x{count}");
            }

            OnInventoryChanged?.Invoke(_stacks);
        }

        /// <summary>
        /// returns a copy of inventory stacks
        /// </summary>
        internal static int[] GetStacksCopy()
        {
            if (_stacks == null)
                return null;

            var copy = new int[_stacks.Length];
            Array.Copy(_stacks, copy, _stacks.Length);
            return copy;
        }

        /// <summary>
        /// get the first local user
        /// </summary>
        private static LocalUser GetLocalUser()
        {
            var list = LocalUserManager.readOnlyLocalUsersList;
            return list.Count > 0 ? list[0] : null;
        }
    }
}
