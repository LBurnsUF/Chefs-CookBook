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
        // ---------------------- Fields  ----------------------------
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _enabled;
        private static Inventory _localInventory;
        private static InventorySnapshot _snapshot;

        // Events
        /// <summary>
        /// Do NOT mutate externally.
        /// </summary>
        internal static event Action<int[], int[]> OnInventoryChanged;

        // ---------------------- Initialization  ----------------------------

        internal static void Init(ManualLogSource log)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
        }

        //--------------------------------------- Status Control ----------------------------------------
        /// <summary>
        /// Refreshes and binds to the localuser.
        /// </summary>
        internal static void Enable()
        {
            if (_enabled)
                return;

            _enabled = true;

            // clear stale user
            _localInventory = null;
            _snapshot = default;

            CharacterBody.onBodyStartGlobal += OnBodyStart; // subscribe for character initialization

            if (TryBindFromExistingBodies())
            {
                CharacterBody.onBodyStartGlobal -= OnBodyStart;
                _log.LogInfo("InventoryTracker: unsubscribed from onBodyStartGlobal (binding complete)");
            }
        }

        /// <summary>
        /// Unsubscribes to character events and inventory changes, clears the localuser, and drains the snapshot to avoid stale reads.
        /// </summary>
        internal static void Disable()
        {
            if (!_enabled)
                return;

            _enabled = false;
            CharacterBody.onBodyStartGlobal -= OnBodyStart; // remove subscription to character initialization

            // clear stale user
            if (_localInventory != null)
            {
                _localInventory.onInventoryChanged -= OnLocalInventoryChanged; // remove subscription to local inventory updates
                _localInventory = null;
            }
            _snapshot = default;
        }

        //----------------------------------- Binding Logic -----------------------------------
        /// <summary>
        /// Binds to localuser when body is spawned, ignoring other bodies.
        /// </summary>
        private static void OnBodyStart(CharacterBody body)
        {
            if (!_enabled || body == null)
                return;

            var master = body.master;
            if (!master)
                return;

            var pcmc = master.playerCharacterMasterController;
            if (!pcmc)
                return;

            var networkUser = pcmc.networkUser;
            if (!networkUser)
                return;

            var localUser = GetLocalUser();
            if (networkUser.localUser != localUser)
                return;

            // Only care about the local player's body.
            if (networkUser.localUser != localUser)
                return;

            var inv = body.inventory;
            if (inv == null)
                return;

            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            _log.LogDebug("InventoryTracker.OnBodyStart(): Bound to local player inventory (OnBodyStart event).");
            _log.LogInfo("InventoryTracker.OnBodyStart(): Binding complete, unsubscribed from onBodyStartGlobal.");

            //rebind to local inventory, can occur if player fab gets altered or someone makes a poorly written mod
            RebindLocal(inv);
            SnapshotFromInventory(_localInventory);
        }


        // Fallback Binding (late enable)
        private static bool TryBindFromExistingBodies()
        {
            var localUser = GetLocalUser();
            if (localUser == null)
            {
                _log.LogDebug("InventoryTracker.TryBindFromExistingBodies(): no LocalUsers present");
                return false;
            }

            // iterate over all existing bodies to find local player
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (!body || body.inventory == null)
                    continue;

                var master = body.master;
                if (!master)
                    continue;

                var pcmc = master.playerCharacterMasterController;
                if (!pcmc)
                    continue;

                var networkUser = pcmc.networkUser;
                if (!networkUser)
                    continue;

                // Is this body controlled by our local user?
                if (networkUser.localUser != localUser)
                    continue;

                _log.LogInfo($"InventoryTracker.TryBindFromExistingBodies(): late binding to body {body.name}.");

                RebindLocal(body.inventory);
                SnapshotFromInventory(_localInventory);
                return true;
            }

            _log.LogDebug("InventoryTracker.TryBindFromExistingBodies(): no matching local body found.");
            return false;
        }

        private static void RebindLocal(Inventory inv)
        {
            if (_localInventory != null && _localInventory != inv)
                _localInventory.onInventoryChanged -= OnLocalInventoryChanged;

            _localInventory = inv;

            if (_localInventory != null)
                _localInventory.onInventoryChanged += OnLocalInventoryChanged;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        /// <summary>
        /// Called by RoR2 whenever any body's inventory changes 
        /// </summary>
        private static void OnLocalInventoryChanged()
        {
            if (!_enabled || _localInventory == null)
            {
                return;
            }

            SnapshotFromInventory(_localInventory);
        }

        //--------------------------------------- Snapshot Handling ----------------------------------------
        private static void SnapshotFromInventory(Inventory inv)
        {
            int itemLen = ItemCatalog.itemCount;
            int equipLen = EquipmentCatalog.equipmentCount;


            var itemstacks = new int[itemLen];
            var equipmentstacks = new int[equipLen];

            // fill Item snapshot
            for (int i = 0; i < itemLen; i++)
            {
                itemstacks[i] = inv.GetItemCountPermanent((ItemIndex)i);
            }

            // fill Equipment snapshot
            int slotCount = inv.GetEquipmentSlotCount();

            // step through all equipment slots, recording all seen equipment
            for (int slot = 0; slot < slotCount; slot++)
            {
                // set = 0u for the active equipment set
                var state = inv.GetEquipment((uint)slot, 0u);
                var eqIndex = state.equipmentIndex;

                if (eqIndex != EquipmentIndex.None)
                {
                    int idx = (int)eqIndex;
                    if ((uint)idx < (uint)equipLen)
                    {
                        // Each occupied slot counts as 1 of that equipment
                        equipmentstacks[idx] += 1;
                    }
                }
            }

            _snapshot = new InventorySnapshot(itemstacks, equipmentstacks);
            OnInventoryChanged?.Invoke(clone(itemstacks), clone(equipmentstacks)); // Notify listeners without mutating stacks.
        }

        //--------------------------------------- Snapshot Helpers ----------------------------------------
        private static int[] clone(int[] src)
        {
            if (src == null || src.Length == 0)
            {
                return Array.Empty<int>();
            }
            var copy = new int[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        /// <summary>
        /// returns a copy of item inventory stacks
        /// </summary>
        internal static int[] GetItemStacksCopy()
        {
            var src = _snapshot.ItemStacks;
            return (src == null || src.Length == 0)
                ? Array.Empty<int>()
                : (int[])src.Clone();
        }

        /// <summary>
        /// returns a copy of equipment inventory stacks
        /// </summary>
        internal static int[] GetEquipmentStacksCopy()
        {
            var src = _snapshot.EquipmentStacks;
            return (src == null || src.Length == 0)
                ? Array.Empty<int>()
                : (int[])src.Clone();
        }

        //--------------------------------------- General Helpers  ----------------------------------------
        /// <summary>
        /// get the first local user
        /// </summary>
        private static LocalUser GetLocalUser()
        {
            var list = LocalUserManager.readOnlyLocalUsersList;
            return list.Count > 0 ? list[0] : null;
        }
    }
    //--------------------------------------- Structs  ----------------------------------------
    /// <summary>
    /// Immutable snapshot of the local player's inventory at a moment in time.
    /// Contains item and equipment stacks indexed by their respective catalogs.
    /// </summary>
    internal readonly struct InventorySnapshot
    {
        public readonly int[] ItemStacks;
        public readonly int[] EquipmentStacks;

        public InventorySnapshot(int[] itemStacks, int[] equipmentStacks)
        {
            ItemStacks = itemStacks ?? Array.Empty<int>();
            EquipmentStacks = equipmentStacks ?? Array.Empty<int>();
        }
    }
}
