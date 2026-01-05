using BepInEx.Logging;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
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
        private static bool _isDroneScrapperPresent = false;

        private static Inventory _localInventory;
        private static InventorySnapshot _snapshot;

        private static int[] _cachedLocalPhysical;
        private static int[] _cachedGlobalDronePotential;
        private static bool _dronesDirty = true;
        private static bool _itemsDirty = true;
        private static bool _remoteDirty = true;
        private static bool _hasSnapshot = false;
        private static readonly HashSet<int> _changedIndices = new();
        private static HashSet<ItemIndex> _lastCorruptedIndices = new();

        internal static void DronesDirty() => _dronesDirty = true;
        internal static void ItemsDirty() => _itemsDirty = true;
        internal static bool DroneScrapperPresent() => _isDroneScrapperPresent;

        public static int LastItemCountUsed { get; private set; }

        // Events
        /// <summary>
        /// Fires when the combined (Physical + Drone) inventory changes.
        /// </summary>
        internal static event Action<int[], HashSet<int>> OnInventoryChangedWithIndices;

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
                    if (itemIndex != ItemIndex.None) _tierToScrapItemIdx[tier] = itemIndex;
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

            if (_localInventory != null) _localInventory.onInventoryChanged -= OnLocalInventoryChanged;
            _localInventory = null;
            _snapshot = default;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        private static void OnGlobalInventoryChanged(Inventory inv)
        {
            if (!_enabled) return;

            int[] currentStacks = GetUnifiedStacksFor(inv);

            if (!HasPermanentChanges(inv, currentStacks)) return;

            if (inv == _localInventory)
            {
                _itemsDirty = true;
                UpdateSnapshot();
            }
            else if (CookBook.AllowMultiplayerPooling.Value)
            {
                _remoteDirty = true;
                UpdateSnapshot();
            }
        }

        private static void OnLocalInventoryChanged()
        {
            if (!_enabled || _localInventory == null) return;

            int[] currentStacks = GetUnifiedStacksFor(_localInventory);
            if (!HasPermanentChanges(_localInventory, currentStacks)) return;

            _itemsDirty = true;
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
                            _dronesDirty = true;
                            _log.LogDebug("Drone Change detected, refreshing snapshot.");
                            UpdateSnapshot();
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
            _isDroneScrapperPresent = CheckForScrapper();

            // This logic is to hide recipes that that involve the uncorrupted version of an item that the user has a corrupted version of
            HashSet<ItemIndex> currentCorrupted = new HashSet<ItemIndex>();
            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                if (_localInventory.GetItemCountPermanent(info.transformedItem) > 0) currentCorrupted.Add(info.originalItem);
            }

            if (!currentCorrupted.SetEquals(_lastCorruptedIndices))
            {
                _itemsDirty = true;
                _lastCorruptedIndices = currentCorrupted;
            }

            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
            _changedIndices.Clear();

            if (!_itemsDirty && !_dronesDirty && !_remoteDirty && _hasSnapshot) return;

            if (_itemsDirty || _cachedLocalPhysical == null)
            {
                int[] newPhysical = GetUnifiedStacksFor(_localInventory);

                if (_cachedLocalPhysical != null)
                {
                    for (int i = 0; i < totalLen; i++)
                    {
                        if (newPhysical[i] != _cachedLocalPhysical[i]) _changedIndices.Add(i);
                    }
                }
                else
                {
                    for (int i = 0; i < totalLen; i++) _changedIndices.Add(i);
                }

                _cachedLocalPhysical = newPhysical;
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

            _remoteDirty = false;

            if (_changedIndices.Count == 0 && _hasSnapshot && !_dronesDirty && !_remoteDirty) return;

            int[] combinedTotal = new int[totalLen];
            for (int i = 0; i < totalLen; i++) combinedTotal[i] = _cachedLocalPhysical[i];

            _snapshot = new InventorySnapshot(
                (int[])_cachedLocalPhysical.Clone(),
                (int[])_cachedGlobalDronePotential.Clone(),
                combinedTotal,
                new Dictionary<int, DroneCandidate>(_globalScrapCandidates),
                currentCorrupted
            );

            _hasSnapshot = true;
            OnInventoryChangedWithIndices?.Invoke((int[])combinedTotal.Clone(), new HashSet<int>(_changedIndices));
        }

        private static void AccumulateGlobalDrones(NetworkUser user, int[] globalPotentialBuffer)
        {
            if (!user || !user.master) return;

            var minions = CharacterBody.readOnlyInstancesList.Where(b => b.master && b.master.minionOwnership.ownerMaster == user.master);

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
                        int upgradeCount = minionBody.inventory ? minionBody.inventory.GetItemCountEffective(DLC3Content.Items.DroneUpgradeHidden) : 0;

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

        private static bool HasPermanentChanges(Inventory inv, int[] currentUnifiedStacks)
        {
            if (inv == null || _cachedLocalPhysical == null || inv != _localInventory) return true;

            for (int i = 0; i < currentUnifiedStacks.Length; i++)
            {
                if (currentUnifiedStacks[i] != _cachedLocalPhysical[i]) return true;
            }

            return false;
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
        private static bool CheckForScrapper()
        {
            if (UnityEngine.Object.FindObjectOfType<DroneScrapperController>() != null) return true;
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.Contains("DroneScrapper")) return true;
            }

            return false;
        }

        /// <summary>
        /// Exposes the corrupted indices from the current snapshot for transient recipe filtering.
        /// </summary>
        internal static HashSet<ItemIndex> GetCorruptedIndices() { return _snapshot.CorruptedIndices ?? new HashSet<ItemIndex>(); }

        private static int[] GetUnifiedStacksFor(Inventory inv)
        {
            int itemLen = ItemCatalog.itemCount;
            int totalLen = itemLen + EquipmentCatalog.equipmentCount;
            int[] stacks = new int[totalLen];

            for (int i = 0; i < itemLen; i++) stacks[i] = inv.GetItemCountPermanent((ItemIndex)i);

            int slotCount = inv.GetEquipmentSlotCount();
            for (int slot = 0; slot < slotCount; slot++)
            {
                int setCount = inv.GetEquipmentSetCount((uint)slot);
                for (int set = 0; set < setCount; set++)
                {
                    var state = inv.GetEquipment((uint)slot, (uint)set);
                    if (state.equipmentIndex != EquipmentIndex.None)
                    {
                        int unifiedIndex = itemLen + (int)state.equipmentIndex;
                        if (unifiedIndex < totalLen) stacks[unifiedIndex] += 1;
                    }
                }
            }
            return stacks;
        }

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
            if (_snapshot.CheapestDrones != null && _snapshot.CheapestDrones.TryGetValue(scrapIdx, out var candidate)) return candidate;
            return default;
        }

        internal static int GetGlobalDronePotentialCount(int index) => (_snapshot.DronePotential != null && index < _snapshot.DronePotential.Length) ? _snapshot.DronePotential[index] : 0;
        internal static int GetPhysicalCount(int index) => (_snapshot.PhysicalStacks != null && index < _snapshot.PhysicalStacks.Length) ? _snapshot.PhysicalStacks[index] : 0;
        internal static int GetDronePotentialCount(int index) => (_snapshot.DronePotential != null && index < _snapshot.DronePotential.Length) ? _snapshot.DronePotential[index] : 0;
        internal static int[] GetDronePotentialStacks() => (_snapshot.DronePotential != null) ? (int[])_snapshot.DronePotential.Clone() : Array.Empty<int>();
        /// <summary>
        /// Returns the aggregate stack (Local Items + Drones + Pooled Allied Items).
        /// </summary>
        internal static int[] GetUnifiedStacksCopy() => (_snapshot.TotalStacks != null) ? (int[])_snapshot.TotalStacks.Clone() : Array.Empty<int>();
        /// <summary>
        /// Returns only what the local player physically owns (Items + Equipment).
        /// Used to determine if a TradeStep is actually required.
        /// </summary>
        internal static int[] GetLocalPhysicalStacks() => (_snapshot.PhysicalStacks != null) ? (int[])_snapshot.PhysicalStacks.Clone() : Array.Empty<int>();
        /// <summary>
        /// Provides the planner with specific snapshots of allied inventories.
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
        public readonly HashSet<ItemIndex> CorruptedIndices;

        public InventorySnapshot(int[] physical, int[] drone, int[] total,
                             Dictionary<int, DroneCandidate> cheapest,
                             HashSet<ItemIndex> corrupted)
        {
            PhysicalStacks = physical ?? Array.Empty<int>();
            DronePotential = drone ?? Array.Empty<int>();
            TotalStacks = total ?? Array.Empty<int>();
            CheapestDrones = cheapest ?? new Dictionary<int, DroneCandidate>();
            CorruptedIndices = corrupted ?? new HashSet<ItemIndex>();
        }
    }
    internal struct DroneCandidate
    {
        public NetworkUser Owner;
        public DroneIndex DroneIdx;
        public int UpgradeCount;
    }
}
