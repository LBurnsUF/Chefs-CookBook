using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CookBook
{
    /// <summary>
    /// Tracks local player's items and raises an event when they change.
    /// </summary>
    internal static class InventoryTracker
    {
        // ---------------------- Fields  ----------------------------
        private static ManualLogSource _log;
        private static bool _initialized;
        private static bool _enabled;
        private static Inventory _localInventory;
        private static InventorySnapshot _snapshot;

        private static int[] _cachedLocalPhysical;
        private static int[] _cachedGlobalDronePotential;
        private static bool _dronesDirty = true;
        private static bool _itemsDirty = true;
        private static bool _hasSnapshot = false;

        internal static void MarkDronesDirty() => _dronesDirty = true;
        internal static void MarkItemsDirty() => _itemsDirty = true;

        public static int LastItemCountUsed { get; private set; }

        // Events
        /// <summary>
        /// Fires when the combined (Physical + Drone) inventory changes.
        /// </summary>
        internal static event Action<int[]> OnInventoryChanged;

        private static readonly Dictionary<ItemTier, ItemIndex> _tierToScrapItemIdx = new();
        private static readonly Dictionary<int, DroneCandidate> _globalScrapCandidates = new();
        // ---------------------- Initialization  ----------------------------

        internal static void Init(ManualLogSource log)
        {
            if (_initialized) return;

            _initialized = true;
            _log = log;

            TradeTracker.Init();
            PickupCatalog.availability.CallWhenAvailable(BuildScrapLookup);
        }

        private static void BuildScrapLookup()
        {
            _tierToScrapItemIdx.Clear();

            foreach (ItemTier tier in Enum.GetValues(typeof(ItemTier)))
            {
                if (tier == ItemTier.AssignedAtRuntime || tier == ItemTier.NoTier) continue;

                PickupIndex scrapPickup = PickupCatalog.FindScrapIndexForItemTier(tier);

                if (scrapPickup != PickupIndex.none)
                {
                    ItemIndex itemIndex = PickupCatalog.GetPickupDef(scrapPickup)?.itemIndex ?? ItemIndex.None;

                    if (itemIndex != ItemIndex.None)
                    {
                        _tierToScrapItemIdx[tier] = itemIndex;
                        _log.LogDebug($"Mapped Tier {tier} -> Scrap Item: {itemIndex}");
                    }
                }
            }
        }

        //--------------------------------------- Status Control ----------------------------------------
        /// <summary>
        /// Refreshes binding and begins tracking localuser inventory.
        /// </summary>
        internal static void Enable()
        {
            if (_enabled) return;
            _enabled = true;
            _localInventory = null;
            _snapshot = default;

            CharacterBody.onBodyStartGlobal += OnBodyStart;
            MinionOwnership.onMinionGroupChangedGlobal += OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal += OnGlobalInventoryChanged;

            if (TryBindFromExistingBodies()) CharacterBody.onBodyStartGlobal -= OnBodyStart;
        }

        /// <summary>
        /// Unsubscribes to character events and inventory changes, clears the localuser, and drains the snapshot to avoid stale reads.
        /// </summary>
        internal static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;

            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            MinionOwnership.onMinionGroupChangedGlobal += OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal -= OnGlobalInventoryChanged;

            // clear stale user
            if (_localInventory != null) _localInventory.onInventoryChanged -= OnLocalInventoryChanged;
            _localInventory = null;
            _snapshot = default;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        private static void OnGlobalInventoryChanged(Inventory inv)
        {
            if (CookBook.AllowMultiplayerPooling.Value || inv == _localInventory)
            {
                UpdateSnapshot();
            }
        }
        private static void OnLocalInventoryChanged()
        {
            if (!_enabled || _localInventory == null) return;
            UpdateSnapshot();
        }

        private static void OnMinionGroupChanged(MinionOwnership minion)
        {
            if (!_enabled || _localInventory == null || minion == null) return;

            var master = _localInventory.GetComponent<CharacterMaster>();
            if (!master) return;

            var myGroup = MinionOwnership.MinionGroup.FindGroup(master.netId);

            if (minion.group == myGroup)
            {
                var minionMaster = minion.GetComponent<CharacterMaster>();
                if (minionMaster && minionMaster.bodyPrefab)
                {
                    var bodyComponent = minionMaster.bodyPrefab.GetComponent<CharacterBody>();
                    if (bodyComponent)
                    {
                        DroneIndex droneIdx = DroneCatalog.GetDroneIndexFromBodyIndex(bodyComponent.bodyIndex);
                        DroneDef droneDef = DroneCatalog.GetDroneDef(droneIdx);

                        if (droneDef != null && droneDef.canScrap)
                        {
                            _log.LogDebug($"[DroneUpdate] {droneDef.droneIndex} (Tier {droneDef.tier}) changed. Rebuilding Planner.");
                            OnLocalInventoryChanged();
                        }
                    }
                }
            }
        }

        public static void TriggerUpdate() => UpdateSnapshot();

        //--------------------------------------- Snapshot Logic  ----------------------------------------
        private static void UpdateSnapshot()
        {
            if (!_enabled || _localInventory == null) return;

            if (!_itemsDirty && !_dronesDirty && _hasSnapshot) return;

            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;

            if (_itemsDirty || _cachedLocalPhysical == null)
            {
                _cachedLocalPhysical = GetUnifiedStacksFor(_localInventory);
                _itemsDirty = false;
            }

            if (_dronesDirty || _cachedGlobalDronePotential == null)
            {
                _globalScrapCandidates.Clear();
                _cachedGlobalDronePotential = new int[totalLen];

                var localUser = GetLocalUser()?.currentNetworkUser;
                AccumulateGlobalDrones(localUser, _cachedGlobalDronePotential);

                if (CookBook.AllowMultiplayerPooling.Value)
                {
                    foreach (var playerController in PlayerCharacterMasterController.instances)
                    {
                        var netUser = playerController.networkUser;
                        if (!netUser || netUser.localUser == GetLocalUser()) continue;

                        AccumulateGlobalDrones(netUser, _cachedGlobalDronePotential);
                    }
                }
                _dronesDirty = false;
            }

            int[] combinedTotal = new int[totalLen];
            for (int i = 0; i < totalLen; i++)
            {
                combinedTotal[i] = _cachedLocalPhysical[i] + _cachedGlobalDronePotential[i];
            }

            _snapshot = new InventorySnapshot(
                (int[])_cachedLocalPhysical.Clone(),
                (int[])_cachedGlobalDronePotential.Clone(),
                combinedTotal,
                new Dictionary<int, DroneCandidate>(_globalScrapCandidates)
            );

            _hasSnapshot = true;
            OnInventoryChanged?.Invoke((int[])combinedTotal.Clone());
        }

        private static void AccumulateGlobalDrones(NetworkUser user, int[] globalPotentialBuffer)
        {
            if (!user || !user.master) return;

            var minions = CharacterBody.readOnlyInstancesList
                .Where(b => b.master && b.master.minionOwnership.ownerMaster == user.master);

            foreach (var minionBody in minions)
            {
                if (!minionBody || !minionBody.healthComponent.alive) continue;

                DroneIndex droneIdx = DroneCatalog.GetDroneIndexFromBodyIndex(minionBody.bodyIndex);
                DroneDef droneDef = DroneCatalog.GetDroneDef(droneIdx);

                if (droneDef != null && droneDef.canScrap)
                {
                    int scrapIdx = MapTierToScrapIndex(droneDef.tier);
                    if (scrapIdx != -1)
                    {
                        int upgradeCount = minionBody.inventory
                            ? minionBody.inventory.GetItemCountEffective(DLC3Content.Items.DroneUpgradeHidden)
                            : 0;

                        globalPotentialBuffer[scrapIdx] += DroneUpgradeUtils.GetDroneCountFromUpgradeCount(upgradeCount);

                        if (!_globalScrapCandidates.TryGetValue(scrapIdx, out var best) || upgradeCount < best.UpgradeCount)
                        {
                            _globalScrapCandidates[scrapIdx] = new DroneCandidate
                            {
                                Owner = user,
                                DroneIdx = droneIdx,
                                UpgradeCount = upgradeCount
                            };
                        }
                    }
                }
            }
        }

        //----------------------------------- Binding Logic -----------------------------------
        /// <summary>
        /// Binds to localuser when body is spawned, ignoring other bodies.
        /// </summary>
        private static void OnBodyStart(CharacterBody body)
        {
            if (!_enabled || body == null || !body.master) return;
            var networkUser = body.master.playerCharacterMasterController?.networkUser;
            if (networkUser && networkUser.localUser == GetLocalUser())
            {
                CharacterBody.onBodyStartGlobal -= OnBodyStart;
                _log.LogInfo("InventoryTracker.OnBodyStart(): Binding complete, unsubscribed from onBodyStartGlobal.");

                RebindLocal(body.inventory);
                UpdateSnapshot();
            }
        }

        // Fallback Binding (late enable)
        private static bool TryBindFromExistingBodies()
        {
            var localUser = GetLocalUser();
            if (localUser == null) return false;
            foreach (var body in CharacterBody.readOnlyInstancesList)
            {
                if (body && body.inventory && body.master?.playerCharacterMasterController?.networkUser?.localUser == localUser)
                {
                    _log.LogInfo($"InventoryTracker.TryBindFromExistingBodies(): late binding to body {body.name}.");

                    RebindLocal(body.inventory);
                    UpdateSnapshot();
                    return true;
                }
            }
            _log.LogDebug("InventoryTracker.TryBindFromExistingBodies(): no matching local body found.");
            return false;
        }

        private static void RebindLocal(Inventory inv)
        {
            if (_localInventory != null && _localInventory != inv) _localInventory.onInventoryChanged -= OnLocalInventoryChanged;
            _localInventory = inv;
            if (_localInventory != null) _localInventory.onInventoryChanged += OnLocalInventoryChanged;
        }

        /// <summary>
        /// get the first local user
        /// </summary>
        private static LocalUser GetLocalUser()
        {
            var list = LocalUserManager.readOnlyLocalUsersList;
            return list.Count > 0 ? list[0] : null;
        }

        //--------------------------------- Helpers ------------------------------------------
        private static int[] GetUnifiedStacksFor(Inventory inv)
        {
            int itemLen = ItemCatalog.itemCount;
            int totalLen = itemLen + EquipmentCatalog.equipmentCount;
            int[] stacks = new int[totalLen];

            for (int i = 0; i < itemLen; i++) stacks[i] = inv.GetItemCountPermanent((ItemIndex)i);

            int slotCount = inv.GetEquipmentSlotCount();
            for (int slot = 0; slot < slotCount; slot++)
            {
                var state = inv.GetEquipment((uint)slot, 0u);
                if (state.equipmentIndex != EquipmentIndex.None)
                {
                    int unifiedIndex = itemLen + (int)state.equipmentIndex;
                    if (unifiedIndex < totalLen) stacks[unifiedIndex] += 1;
                }
            }
            return stacks;
        }

        internal static int GetGlobalDronePotentialCount(int index) =>
            (_snapshot.DronePotential != null && index < _snapshot.DronePotential.Length) ? _snapshot.DronePotential[index] : 0;

        private static int MapTierToScrapIndex(ItemTier tier)
        {
            if (_tierToScrapItemIdx.TryGetValue(tier, out var index)) return (int)index;
            return -1;
        }

        /// <summary>
        /// Returns the ID of the cheapest owned drone that provides this scrap tier.
        /// </summary>
        internal static DroneCandidate GetScrapCandidate(int scrapIdx)
        {
            if (_snapshot.CheapestDrones != null && _snapshot.CheapestDrones.TryGetValue(scrapIdx, out var candidate))
                return candidate;
            return default;
        }

        internal static int GetPhysicalCount(int index) => (_snapshot.PhysicalStacks != null && index < _snapshot.PhysicalStacks.Length) ? _snapshot.PhysicalStacks[index] : 0;
        internal static int GetDronePotentialCount(int index) => (_snapshot.DronePotential != null && index < _snapshot.DronePotential.Length) ? _snapshot.DronePotential[index] : 0;

        /// <summary>
        /// Returns the aggregate stack (Local Items + Drones + Pooled Allied Items).
        /// Used by the planner for the initial "Is this possible?" BFS check.
        /// </summary>
        internal static int[] GetUnifiedStacksCopy() => (_snapshot.TotalStacks != null) ? (int[])_snapshot.TotalStacks.Clone() : Array.Empty<int>();

        /// <summary>
        /// Returns only what the local player physically owns (Items + Equipment).
        /// Used to determine if a TradeStep is actually required.
        /// </summary>
        internal static int[] GetLocalPhysicalStacks() => (_snapshot.PhysicalStacks != null) ? (int[])_snapshot.PhysicalStacks.Clone() : Array.Empty<int>();

        /// <summary>
        /// Provides the planner with specific snapshots of allied inventories.
        /// Necessary for identifying which player can provide a specific ingredient.
        /// </summary>
        internal static Dictionary<NetworkUser, int[]> GetAlliedSnapshots()
        {
            var alliedData = new Dictionary<NetworkUser, int[]>();
            if (!CookBook.AllowMultiplayerPooling.Value) return alliedData;

            var localUser = GetLocalUser();
            foreach (var playerController in PlayerCharacterMasterController.instances)
            {
                var netUser = playerController.networkUser;
                if (!netUser || netUser.localUser == localUser) continue;

                var master = playerController.master;
                if (master?.inventory != null && TradeTracker.GetRemainingTrades(netUser) > 0)
                {
                    alliedData[netUser] = GetUnifiedStacksFor(master.inventory);
                }
            }
            return alliedData;
        }
    }

    //--------------------------------------- Structs  ----------------------------------------
    /// <summary>
    /// Immutable snapshot of the local player's inventory at a moment in time.
    /// Contains Unified stacks (Items + Equipment flattened).
    /// </summary>
    internal readonly struct InventorySnapshot
    {
        public readonly int[] PhysicalStacks;
        public readonly int[] DronePotential;
        public readonly int[] TotalStacks;

        public readonly Dictionary<int, DroneCandidate> CheapestDrones;

        public InventorySnapshot(int[] physical, int[] drone, int[] total, Dictionary<int, DroneCandidate> cheapest)
        {
            PhysicalStacks = physical ?? Array.Empty<int>();
            DronePotential = drone ?? Array.Empty<int>();
            TotalStacks = total ?? Array.Empty<int>();
            CheapestDrones = cheapest ?? new Dictionary<int, DroneCandidate>();
        }
    }
    internal struct DroneCandidate
    {
        public NetworkUser Owner;
        public DroneIndex DroneIdx;
        public int UpgradeCount;
    }
}
