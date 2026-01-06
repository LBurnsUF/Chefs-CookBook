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

        private static bool _dronesDirty = true;
        private static bool _itemsDirty = true;
        private static bool _remoteItemsDirty = true;
        private static bool _tradeDirty = true;
        private static bool _hasSnapshot = false;
        private static bool _canScrapDrones = false;
        private static bool _isTransitioning = false;

        private static Inventory _localInventory;
        private static InventorySnapshot _snapshot;

        private static int[] _cachedLocalPhysical;
        private static readonly Dictionary<Inventory, int[]> _cachedAlliedPhysical = new();
        private static int[] _cachedGlobalDronePotential;

        private static readonly HashSet<int> _changedIndices = new();
        private static HashSet<ItemIndex> _lastCorruptedIndices = new HashSet<ItemIndex>();
        private static readonly Dictionary<NetworkUser, int> _lastTradeCounts = new();

        internal static void TradesDirty() => _tradeDirty = true;
        internal static void DronesDirty() => _dronesDirty = true;
        internal static void ItemsDirty() => _itemsDirty = true;
        internal static bool DroneScrapperPresent() => _canScrapDrones;

        // Events
        /// <summary>
        /// Fires when the combined (Physical + Drone) inventory changes.
        /// </summary>
        internal static event Action<int[], HashSet<int>> OnInventoryChangedWithIndices;

        private static readonly Dictionary<ItemTier, ItemIndex> _tierToScrapItemIdx = new();
        private static readonly Dictionary<int, List<DroneCandidate>> _globalScrapCandidates = new();
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

            _dronesDirty = true;
            _itemsDirty = true;
            _remoteItemsDirty = true;
            _tradeDirty = true;
            _canScrapDrones = CanScrapDrones();

            CharacterBody.onBodyStartGlobal += OnBodyStart;
            MinionOwnership.onMinionGroupChangedGlobal += OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal += OnGlobalInventoryChanged;
            PlayerCharacterMasterController.onPlayerAdded += OnPlayerJoined;
            PlayerCharacterMasterController.onPlayerRemoved += OnPlayerLeft;
            Stage.onServerStageComplete += OnStageComplete;
            Stage.onStageStartGlobal += OnStageStart;
            CharacterBody.onBodyDestroyGlobal += OnBodyDestroyed;

            if (CookBook.IsPoolingEnabled)
            {
                foreach (var playerController in PlayerCharacterMasterController.instances)
                {
                    var netUser = playerController.networkUser;
                    if (!netUser || netUser.localUser != null) continue;
                    var inv = playerController.master?.inventory;
                    if (inv != null) _cachedAlliedPhysical[inv] = GetUnifiedStacksFor(inv);
                }
            }

            if (TryBindFromExistingBodies()) CharacterBody.onBodyStartGlobal -= OnBodyStart;
        }

        /// <summary>
        /// Unsubscribes to character events and inventory changes, clears all cached data, 
        /// and drains the snapshot to avoid stale reads.
        /// </summary>
        internal static void Disable()
        {
            if (!_enabled) return;
            _enabled = false;

            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            MinionOwnership.onMinionGroupChangedGlobal -= OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal -= OnGlobalInventoryChanged;
            PlayerCharacterMasterController.onPlayerAdded -= OnPlayerJoined;
            PlayerCharacterMasterController.onPlayerRemoved -= OnPlayerLeft;
            Stage.onServerStageComplete -= OnStageComplete;
            Stage.onStageStartGlobal -= OnStageStart;
            CharacterBody.onBodyDestroyGlobal -= OnBodyDestroyed;

            _cachedAlliedPhysical.Clear();
            _lastTradeCounts.Clear();
            _localInventory = null;
            _cachedLocalPhysical = null;
            _cachedGlobalDronePotential = null;
            _lastCorruptedIndices.Clear();
            _snapshot = default;
            _hasSnapshot = false;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        private static void OnStageComplete(Stage stage) => _isTransitioning = true;
        private static void OnStageStart(Stage stage) => _isTransitioning = false;
        private static void OnGlobalInventoryChanged(Inventory inv)
        {
            if (!_enabled || inv == null) return;

            int[] currentStacks = GetUnifiedStacksFor(inv);

            if (inv == _localInventory)
            {
                if (!HasPermanentChanges(_cachedLocalPhysical, currentStacks)) return;

                _changedIndices.Clear();
                if (_cachedLocalPhysical != null)
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        if (currentStacks[i] != _cachedLocalPhysical[i]) _changedIndices.Add(i);
                }

                _cachedLocalPhysical = currentStacks;
                _itemsDirty = true;
            }
            else if (CookBook.IsPoolingEnabled)
            {
                _cachedAlliedPhysical.TryGetValue(inv, out int[] oldStacks);
                if (!HasPermanentChanges(oldStacks, currentStacks)) return;

                if (oldStacks != null)
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        if (currentStacks[i] != oldStacks[i]) _changedIndices.Add(i);
                }
                else
                {
                    for (int i = 0; i < currentStacks.Length; i++) _changedIndices.Add(i);
                }

                _cachedAlliedPhysical[inv] = currentStacks;
                _remoteItemsDirty = true;
            }
            else return;

            UpdateSnapshot();
        }

        private static void OnMinionGroupChanged(MinionOwnership minion)
        {
            if (!_enabled || minion == null || !minion.ownerMaster) return;

            CharacterMaster minionMaster = minion.GetComponent<CharacterMaster>();
            var minionBody = minionMaster?.GetBody();
            if (!minionBody) return;

            DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(minionBody.bodyIndex);
            DroneDef dDef = DroneCatalog.GetDroneDef(dIdx);

            if (dDef == null || !dDef.canScrap) return;

            CharacterMaster ownerMaster = minion.ownerMaster;
            bool isRelevant = (ownerMaster == _localInventory?.GetComponent<CharacterMaster>());
            if (!isRelevant && CookBook.IsPoolingEnabled)
            {
                isRelevant = _cachedAlliedPhysical.Keys.Any(inv => inv.GetComponent<CharacterMaster>() == ownerMaster);
            }

            if (isRelevant)
            {
                int scrapIdx = GetScrapIndexFromDrone(dIdx);
                if (scrapIdx != -1) _changedIndices.Add(scrapIdx);

                _dronesDirty = true;
                UpdateSnapshot();
            }
        }

        internal static void OnConsiderDronesChanged(object sender, EventArgs e)
        {
            _dronesDirty = true;
            UpdateSnapshot();
        }

        private static void OnPlayerJoined(PlayerCharacterMasterController pmb)
        {
            _remoteItemsDirty = true;
        }

        private static void OnPlayerLeft(PlayerCharacterMasterController pmb)
        {
            if (pmb && pmb.master && pmb.master.inventory)
            {
                _cachedAlliedPhysical.Remove(pmb.master.inventory);
                _remoteItemsDirty = true;
                UpdateSnapshot();
            }
        }

        public static void TriggerUpdate() => UpdateSnapshot();

        //--------------------------------------- Snapshot Logic  ----------------------------------------
        private static void UpdateSnapshot()
        {
            if (!_enabled || _localInventory == null) return;
            _canScrapDrones = CanScrapDrones();

            bool dronesWereDirty = _dronesDirty;
            bool remoteWasDirty = _remoteItemsDirty;
            bool tradesWereDirty = CheckTradeAvailabilityChanged() || _tradeDirty;

            HashSet<ItemIndex> currentCorrupted = new HashSet<ItemIndex>();
            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                if (_localInventory.GetItemCountPermanent(info.transformedItem) > 0)
                {
                    currentCorrupted.Add(info.originalItem);
                }
            }

            if (!currentCorrupted.SetEquals(_lastCorruptedIndices))
            {
                _itemsDirty = true;
                _lastCorruptedIndices = currentCorrupted;
            }

            if (!_itemsDirty && !dronesWereDirty && !remoteWasDirty && !tradesWereDirty && _hasSnapshot) return;

            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;

            if (dronesWereDirty || _cachedGlobalDronePotential == null)
            {
                _globalScrapCandidates.Clear();
                _cachedGlobalDronePotential = new int[totalLen];

                if (CookBook.ConsiderDrones.Value)
                {
                    AccumulateGlobalDrones(GetLocalUser()?.currentNetworkUser, _cachedGlobalDronePotential);

                    if (CookBook.IsPoolingEnabled)
                    {
                        foreach (var playerController in PlayerCharacterMasterController.instances)
                        {
                            var netUser = playerController.networkUser;
                            if (!netUser || netUser.localUser == GetLocalUser()) continue;
                            AccumulateGlobalDrones(netUser, _cachedGlobalDronePotential);
                        }
                    }
                }
                _dronesDirty = false;
            }

            int[] localClone = (int[])_cachedLocalPhysical.Clone();

            foreach (var itemIdx in _lastCorruptedIndices)
            {
                int idx = (int)itemIdx;
                if (idx >= 0 && idx < localClone.Length)
                {
                    localClone[idx] = 0;
                }
            }

            _snapshot = new InventorySnapshot(
                localClone,
                (int[])_cachedGlobalDronePotential.Clone(),
                (int[])localClone.Clone(),
                _globalScrapCandidates.ToDictionary(kvp => kvp.Key, kvp => new List<DroneCandidate>(kvp.Value)),
                _lastCorruptedIndices,
                _canScrapDrones,
                RecipeProvider.GetFilteredRecipes(_lastCorruptedIndices) ?? new List<ChefRecipe>()
            );

            _hasSnapshot = true;
            _itemsDirty = false;
            _remoteItemsDirty = false;
            _tradeDirty = false;

            OnInventoryChangedWithIndices?.Invoke((int[])localClone.Clone(), new HashSet<int>(_changedIndices));
            _changedIndices.Clear();
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
                        int upgradeCount = minionBody.inventory ? minionBody.inventory.GetItemCountPermanent(DLC3Content.Items.DroneUpgradeHidden) : 0;

                        globalPotentialBuffer[scrapIdx] += DroneUpgradeUtils.GetDroneCountFromUpgradeCount(upgradeCount);

                        if (!_globalScrapCandidates.TryGetValue(scrapIdx, out var list))
                        {
                            list = new List<DroneCandidate>();
                            _globalScrapCandidates[scrapIdx] = list;
                        }

                        list.Add(new DroneCandidate
                        {
                            Owner = user,
                            DroneIdx = droneIdx,
                            UpgradeCount = upgradeCount
                        });
                    }
                }
            }

            var localUser = GetLocalUser()?.currentNetworkUser;
            foreach (var tierList in _globalScrapCandidates.Values)
            {
                tierList.Sort((a, b) =>
                {
                    int costComparison = a.UpgradeCount.CompareTo(b.UpgradeCount);
                    if (costComparison != 0) return costComparison;

                    bool aLocal = a.Owner == localUser;
                    bool bLocal = b.Owner == localUser;
                    if (aLocal != bLocal) return aLocal ? -1 : 1;

                    string nameA = DroneCatalog.GetDroneDef(a.DroneIdx)?.nameToken ?? string.Empty;
                    string nameB = DroneCatalog.GetDroneDef(b.DroneIdx)?.nameToken ?? string.Empty;

                    return string.Compare(nameA, nameB, StringComparison.Ordinal);
                });
            }
        }

        private static bool HasPermanentChanges(int[] oldStacks, int[] newStacks)
        {
            if (oldStacks == null) return true;
            if (newStacks == null) return false;

            if (oldStacks.Length != newStacks.Length) return true;

            for (int i = 0; i < oldStacks.Length; i++)
            {
                if (oldStacks[i] != newStacks[i]) return true;
            }

            return false;
        }

        private static bool CheckTradeAvailabilityChanged()
        {
            bool changed = false;
            if (!CookBook.AllowMultiplayerPooling.Value) return false;

            foreach (var playerController in PlayerCharacterMasterController.instances)
            {
                var netUser = playerController.networkUser;
                if (!netUser || netUser.localUser == GetLocalUser()) continue;

                int currentTrades = TradeTracker.GetRemainingTrades(netUser);
                _lastTradeCounts.TryGetValue(netUser, out int lastTrades);

                if (currentTrades != lastTrades)
                {
                    _lastTradeCounts[netUser] = currentTrades;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Binds to localuser when body is spawned, ignoring other bodies.
        /// </summary>
        private static void OnBodyStart(CharacterBody body)
        {
            if (!_enabled || body == null) return;

            var networkUser = body.master?.playerCharacterMasterController?.networkUser;
            if (networkUser && networkUser.localUser == GetLocalUser())
            {
                RebindLocal(body.inventory);
                _cachedLocalPhysical = GetUnifiedStacksFor(body.inventory);
                UpdateSnapshot();
            }

            DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex);
            if (dIdx != DroneIndex.None)
            {
                DroneDef dDef = DroneCatalog.GetDroneDef(dIdx);
                if (dDef == null || !dDef.canScrap) return;

                CharacterMaster ownerMaster = body.master?.minionOwnership?.ownerMaster;
                if (ownerMaster)
                {
                    bool isRelevant = (ownerMaster == _localInventory?.GetComponent<CharacterMaster>());
                    if (!isRelevant && CookBook.IsPoolingEnabled)
                    {
                        isRelevant = _cachedAlliedPhysical.Keys.Any(inv => inv.GetComponent<CharacterMaster>() == ownerMaster);
                    }

                    if (isRelevant)
                    {
                        _dronesDirty = true;
                        int scrapIdx = GetScrapIndexFromDrone(dIdx);
                        if (scrapIdx != -1) _changedIndices.Add(scrapIdx);
                        UpdateSnapshot();
                    }
                }
            }
        }

        private static void OnBodyDestroyed(CharacterBody body)
        {
            if (!_enabled || _isTransitioning || body == null) return;

            DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex);
            if (dIdx == DroneIndex.None) return;

            DroneDef dDef = DroneCatalog.GetDroneDef(dIdx);
            if (dDef == null || !dDef.canScrap) return;

            CharacterMaster ownerMaster = body.master?.minionOwnership?.ownerMaster;
            if (!ownerMaster) return;

            bool isRelevant = (ownerMaster == _localInventory?.GetComponent<CharacterMaster>());
            if (!isRelevant && CookBook.IsPoolingEnabled)
            {
                isRelevant = _cachedAlliedPhysical.Keys.Any(inv => inv.GetComponent<CharacterMaster>() == ownerMaster);
            }

            if (isRelevant)
            {
                int scrapIdx = GetScrapIndexFromDrone(dIdx);
                if (scrapIdx != -1) _changedIndices.Add(scrapIdx);

                _dronesDirty = true;
                UpdateSnapshot();
            }
        }

        //----------------------------------- Binding Logic ----------------------------------
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
            _localInventory = inv;
        }



        //--------------------------------- Helpers ------------------------------------------
        internal static int GetVisualResultIndex(int unifiedIndex)
        {
            if (unifiedIndex >= ItemCatalog.itemCount) return -1;

            ItemIndex original = (ItemIndex)unifiedIndex;

            if (_lastCorruptedIndices.Contains(original))
            {
                foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
                {
                    if (info.originalItem == original)
                    {
                        return (int)info.transformedItem;
                    }
                }
            }
            return -1;
        }

        private static bool CanScrapDrones()
        {
            if (!CookBook.ConsiderDrones.Value) return false;

            if (UnityEngine.Object.FindObjectOfType<DroneScrapperController>() != null) return true;

            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.Contains("DroneScrapper")) return true;
            }

            return false;
        }

        private static int MapTierToScrapIndex(ItemTier tier)
        {
            if (_tierToScrapItemIdx.TryGetValue(tier, out var index)) return (int)index;
            return -1;
        }
        private static LocalUser GetLocalUser()
        {
            var list = LocalUserManager.readOnlyLocalUsersList;
            return list.Count > 0 ? list[0] : null;
        }
        internal static InventorySnapshot GetSnapshot() => _snapshot;
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
        /// <summary>
        /// Returns all valid drones for a scrap tier.
        /// </summary>
        internal static List<DroneCandidate> GetScrapCandidates(int scrapIdx)
        {
            if (_snapshot.AllScrapCandidates != null && _snapshot.AllScrapCandidates.TryGetValue(scrapIdx, out var list))
                return list;
            return null;
        }
        internal static int GetScrapIndexFromDrone(DroneIndex droneIdx)
        {
            DroneDef dDef = DroneCatalog.GetDroneDef(droneIdx);
            if (dDef != null && dDef.canScrap)
            {
                return MapTierToScrapIndex(dDef.tier);
            }
            return -1;
        }
        internal static int GetPhysicalCount(int index) => (_snapshot.PhysicalStacks != null && index < _snapshot.PhysicalStacks.Length) ? _snapshot.PhysicalStacks[index] : 0;
        internal static int GetGlobalDronePotentialCount(int index) => (_snapshot.DronePotential != null && index < _snapshot.DronePotential.Length) ? _snapshot.DronePotential[index] : 0;
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

            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                var netUser = pcmc.networkUser;
                if (!netUser || netUser.localUser != null) continue;

                var inv = pcmc.master?.inventory;
                if (inv != null && _cachedAlliedPhysical.TryGetValue(inv, out int[] cachedStacks))
                {
                    if (TradeTracker.GetRemainingTrades(netUser) > 0)
                    {
                        alliedData[netUser] = (int[])cachedStacks.Clone();
                    }
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

        public readonly Dictionary<int, List<DroneCandidate>> AllScrapCandidates;
        public readonly HashSet<ItemIndex> CorruptedIndices;

        public readonly bool CanScrapDrones;
        public readonly IReadOnlyList<ChefRecipe> FilteredRecipes;

        public InventorySnapshot(int[] physical, int[] drone, int[] total,
                             Dictionary<int, List<DroneCandidate>> allCandidates,
                             HashSet<ItemIndex> corrupted,
                             bool scrapperPresent,
                             IReadOnlyList<ChefRecipe> filteredRecipes)
        {
            PhysicalStacks = physical ?? Array.Empty<int>();
            DronePotential = drone ?? Array.Empty<int>();
            TotalStacks = total ?? Array.Empty<int>();
            AllScrapCandidates = allCandidates ?? new Dictionary<int, List<DroneCandidate>>();
            CorruptedIndices = corrupted ?? new HashSet<ItemIndex>();

            CanScrapDrones = scrapperPresent;
            FilteredRecipes = filteredRecipes ?? new List<ChefRecipe>();
        }
    }
    internal struct DroneCandidate
    {
        public NetworkUser Owner;
        public DroneIndex DroneIdx;
        public int UpgradeCount;
    }
}
