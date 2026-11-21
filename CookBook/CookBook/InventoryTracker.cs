using BepInEx.Logging;
using RoR2;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UnityEngine;
using static UnityEngine.UIElements.UxmlAttributeDescription;
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

        private static bool _dronesDirty = true;
        private static bool _itemsDirty = true;
        private static bool _remoteItemsDirty = true;
        private static bool _tradeDirty = true;
        private static bool _hasSnapshot = false;
        private static bool _canScrapDrones = false;
        private static bool _isTransitioning = false;
        private static bool _hooksInstalled;
        private static bool _active;
        private static bool _forceRebuildPending;

        private static Inventory _localInventory;

        private static int[] _cachedLocalPhysical;
        private static readonly Dictionary<Inventory, int[]> _cachedAlliedPhysical = new();
        private static int[] _cachedGlobalDronePotential;

        private static readonly HashSet<Inventory> _liveAlliedInvScratch = new();
        private static readonly List<Inventory> _pruneAlliedInvScratch = new();

        private static InventorySnapshot _snapshot;
        private static Dictionary<NetworkUser, int[]> _snapshotAlliedPhysicalStacks;
        private static Dictionary<NetworkUser, int> _snapshotTradesRemaining;

        private static readonly Dictionary<ItemTier, ItemIndex> _tierToScrapItemIdx = new();
        private static readonly Dictionary<int, List<DroneCandidate>> _globalScrapCandidates = new();

        private static HashSet<ItemIndex> _lastCorruptedIndices = new HashSet<ItemIndex>();
        private static readonly HashSet<ItemIndex> _corruptedScratch = new();

        private const float SnapshotDebounceSeconds = 0.15f;

        private static DebounceRunner _runner;
        private static Coroutine _pendingEmit;
        private static readonly HashSet<int> _pendingChanged = new();
        private static int[] _emitBuffer;

        /// <summary>
        /// Fires when the combined inventory changes.
        /// </summary>
        internal static event Action<InventorySnapshot, int[], int, bool> OnSnapshotChanged;

        // ---------------------- Initialization  ----------------------------
        internal static void Init(ManualLogSource log)
        {
            if (_initialized) return;

            _initialized = true;
            _log = log;

            TradeTracker.Init();
            TradeTracker.OnTradeCountsChanged += OnTradeCountsChanged;
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
        internal static void Start()
        {
            if (_hooksInstalled) return;
            _hooksInstalled = true;
            CookBook.AllowMultiplayerPooling.SettingChanged += OnPoolingSettingChanged;
            CookBook.ConsiderDrones.SettingChanged += OnConsiderDronesChanged;
            CharacterBody.onBodyStartGlobal += OnBodyStart;
            CharacterBody.onBodyDestroyGlobal += OnBodyDestroyed;
            MinionOwnership.onMinionGroupChangedGlobal += OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal += OnGlobalInventoryChanged;
            PlayerCharacterMasterController.onPlayerAdded += OnPlayerJoined;
            PlayerCharacterMasterController.onPlayerRemoved += OnPlayerLeft;
            Stage.onServerStageComplete += OnStageComplete;
            Stage.onStageStartGlobal += OnStageStart;
        }

        internal static void Pause()
        {
            DebugLog.Trace(_log, "InventoryTracker.Pause(): Clearing state and disabling updates.");
            _active = false;

            CancelPendingEmit();
            _pendingChanged.Clear();

            _localInventory = null;
            _cachedLocalPhysical = null;
            _cachedGlobalDronePotential = null;
            _cachedAlliedPhysical.Clear();
            _hasSnapshot = false;
        }

        internal static void Resume()
        {
            DebugLog.Trace(_log, "InventoryTracker.Resume(): Resetting state and attempting late-bind.");
            Start();
            _active = true;

            _localInventory = null;
            _snapshot = default;
            _hasSnapshot = false;

            _dronesDirty = true;
            _itemsDirty = true;
            _remoteItemsDirty = true;
            _tradeDirty = true;
            _canScrapDrones = CanScrapDrones();

            _cachedAlliedPhysical.Clear();
            _cachedLocalPhysical = null;
            _cachedGlobalDronePotential = null;
            _lastCorruptedIndices.Clear();

            if (CookBook.IsPoolingEnabled)
            {
                foreach (var playerController in PlayerCharacterMasterController.instances)
                {
                    RefreshAlliedPhysicalCache(pruneStale: false);
                }
            }

            if (TryBindFromExistingBodies())
            {
                DebugLog.Trace(_log, "InventoryTracker.Resume(): Successfully bound to existing player.");
                int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
                for (int i = 0; i < totalLen; i++) _pendingChanged.Add(i);

                MarkForceRebuild();
                QueueEmit("Resume (Bound)");
            }
            else
            {
                DebugLog.Trace(_log, "InventoryTracker.Resume(): No existing player found to bind.");
            }
        }

        /// <summary>
        /// Unsubscribes to character events and inventory changes, clears all cached data, 
        /// and drains the snapshot to avoid stale reads.
        /// </summary>
        internal static void Shutdown()
        {
            if (!_hooksInstalled) return;
            _hooksInstalled = false;
            _active = false;

            CharacterBody.onBodyStartGlobal -= OnBodyStart;
            CharacterBody.onBodyDestroyGlobal -= OnBodyDestroyed;
            MinionOwnership.onMinionGroupChangedGlobal -= OnMinionGroupChanged;
            Inventory.onInventoryChangedGlobal -= OnGlobalInventoryChanged;
            PlayerCharacterMasterController.onPlayerAdded -= OnPlayerJoined;
            PlayerCharacterMasterController.onPlayerRemoved -= OnPlayerLeft;
            Stage.onServerStageComplete -= OnStageComplete;
            Stage.onStageStartGlobal -= OnStageStart;
            TradeTracker.OnTradeCountsChanged -= OnTradeCountsChanged;
            CookBook.AllowMultiplayerPooling.SettingChanged -= OnPoolingSettingChanged;
            CookBook.ConsiderDrones.SettingChanged -= OnConsiderDronesChanged;

            _cachedAlliedPhysical.Clear();
            _localInventory = null;
            _cachedLocalPhysical = null;
            _cachedGlobalDronePotential = null;
            _lastCorruptedIndices.Clear();
            _snapshot = default;
            _hasSnapshot = false;
        }

        //--------------------------------------- Event Logic ----------------------------------------
        private static void OnPoolingSettingChanged(object sender, EventArgs e)
        {
            if (!_active) return;

            bool poolingEnabled = CookBook.IsPoolingEnabled;

            _cachedAlliedPhysical.Clear();
            _snapshotAlliedPhysicalStacks = null;
            _snapshotTradesRemaining = null;

            if (poolingEnabled)
            {
                RefreshAlliedPhysicalCache(pruneStale: false);
            }

            _remoteItemsDirty = true;
            _tradeDirty = true;

            MarkForceRebuild();
            QueueEmit("OnPoolingSettingChanged");
        }

        internal static void OnConsiderDronesChanged(object sender, EventArgs e)
        {
            if (!_active) return;

            foreach (var kvp in _tierToScrapItemIdx)
            {
                int idx = (int)kvp.Value;
                if (idx >= 0) _pendingChanged.Add(idx);
            }

            _dronesDirty = true;
            MarkForceRebuild();
            QueueEmit("OnConsiderDronesChanged");
        }

        private static void OnTradeCountsChanged(IReadOnlyCollection<NetworkUser> changedUsers, bool forceRebuild)
        {
            if (!_active) return;

            _tradeDirty = true;
            _forceRebuildPending |= forceRebuild;

            QueueEmit("OnTradeCountsChanged");
        }

        private static void OnStageComplete(Stage stage) => _isTransitioning = true;
        private static void OnStageStart(Stage stage) => _isTransitioning = false;

        private static void OnGlobalInventoryChanged(Inventory inv)
        {
            if (!_active || !inv) return;

            if (!IsPlayerInventory(inv, out var pcmc))
                return;

            var user = pcmc.networkUser;
            bool isLocal = user != null && user.isLocalPlayer;

            if (isLocal)
            {
                if (_localInventory == null || inv != _localInventory)
                {
                    DebugLog.Trace(_log, $"OnGlobalInventoryChanged: Binding/Rebinding local inventory to {inv.netId}.");
                    RebindLocal(inv);
                }

                int[] currentStacks = GetUnifiedStacksFor(inv);

                if (!HasPermanentChanges(_cachedLocalPhysical, currentStacks)) return;

                DebugLog.Trace(_log, "OnGlobalInventoryChanged: Local inventory change detected.");

                if (_cachedLocalPhysical != null)
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        if (currentStacks[i] != _cachedLocalPhysical[i])
                            _pendingChanged.Add(i);
                }
                else
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        _pendingChanged.Add(i);
                }

                _cachedLocalPhysical = currentStacks;
                _itemsDirty = true;

                QueueEmit("OnGlobalInventoryChanged: Local");
                return;
            }
            else if (CookBook.IsPoolingEnabled && user != null)
            {
                int[] currentStacks = GetUnifiedStacksFor(inv);
                _cachedAlliedPhysical.TryGetValue(inv, out int[] oldStacks);

                if (!HasPermanentChanges(oldStacks, currentStacks)) return;

                DebugLog.Trace(_log, $"OnGlobalInventoryChanged: Allied inventory change detected. Refreshing inventory snapshot for {user.userName}.");
                if (oldStacks != null)
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        if (currentStacks[i] != oldStacks[i])
                            _pendingChanged.Add(i);
                }
                else
                {
                    for (int i = 0; i < currentStacks.Length; i++)
                        _pendingChanged.Add(i);
                }

                _cachedAlliedPhysical[inv] = currentStacks;
                _remoteItemsDirty = true;

                QueueEmit("OnGlobalInventoryChanged");
                return;
            }
        }

        private static void OnMinionGroupChanged(MinionOwnership minion)
        {
            if (!_active || minion == null || !minion.ownerMaster) return;
            if (!CookBook.IsDroneScrappingEnabled) return;

            CharacterMaster minionMaster = minion.GetComponent<CharacterMaster>();
            var minionBody = minionMaster?.GetBody();
            if (!minionBody) return;

            DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(minionBody.bodyIndex);
            MarkDroneScrapDirty(dIdx, "OnMinionGroupChanged");
        }

        private static void OnPlayerJoined(PlayerCharacterMasterController pmb)
        {
            if (!_active) return;

            _remoteItemsDirty = true;
            _tradeDirty = true;
            MarkForceRebuild();
            QueueEmit("OnPlayerJoined");
        }

        private static void OnPlayerLeft(PlayerCharacterMasterController pmb)
        {
            if (CookBook.IsPoolingEnabled)
            {
                RefreshAlliedPhysicalCache(pruneStale: true);
                DebugLog.Trace(_log, $"OnGlobalInventoryChanged: Plaey left {pmb.networkUser.userName}.");
            }
        }

        public static void TriggerUpdate()
        {
            if (!_active || _localInventory == null) return;

            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
            for (int i = 0; i < totalLen; i++) _pendingChanged.Add(i);

            _itemsDirty = true;
            _dronesDirty = true;
            _remoteItemsDirty = true;
            _tradeDirty = true;

            MarkForceRebuild();
            QueueEmit("TriggerUpdate");
        }

        //--------------------------------------- Snapshot Logic  ----------------------------------------
        private static void EmitSnapshotNow()
        {
            if (!_active)
            {
                return;
            }

            if (_localInventory == null)
            {
                DebugLog.Trace(_log, "EmitSnapshotNow: _localInventory is NULL. Attempting late-bind before emit.");
                if (!TryBindFromExistingBodies())
                {
                    DebugLog.Trace(_log, "EmitSnapshotNow aborted: Could not find local inventory to snapshot.");
                    return;
                }
            }

            if (!BuildSnapshotIfNeeded(out var snapshotBuilt))
            {
                DebugLog.Trace(_log, "EmitSnapshotNow: No changes required a new snapshot. Skipping invoke.");
                return;
            }

            int count = _pendingChanged.Count;
            if (_emitBuffer == null || _emitBuffer.Length < count)
                _emitBuffer = new int[Math.Max(count, (_emitBuffer?.Length ?? 0) * 2 + 16)];

            int i = 0;
            foreach (var idx in _pendingChanged) _emitBuffer[i++] = idx;

            _pendingChanged.Clear();

            bool forceRebuild = _forceRebuildPending;
            _forceRebuildPending = false;

            DebugLog.Trace(_log, "Inventory Snapshot Completed, handing to Craftplanner.");
            OnSnapshotChanged?.Invoke(_snapshot, _emitBuffer, count, forceRebuild);
        }

        private static bool BuildSnapshotIfNeeded(out bool built)
        {
            built = false;
            if (_dronesDirty) _canScrapDrones = CanScrapDrones();

            bool poolingIsEnabled = CookBook.IsPoolingEnabled;
            bool canScrapDrones = _canScrapDrones;
            bool dronesWereDirty = _dronesDirty;
            bool remoteWasDirty = _remoteItemsDirty;
            bool tradesWereDirty = _tradeDirty;
            bool pendingOnly = _pendingChanged.Count > 0 || _forceRebuildPending;
            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;

            // ---------------- Corrupted items ----------------
            _corruptedScratch.Clear();
            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                if (_localInventory.GetItemCountPermanent(info.transformedItem) > 0)
                {
                    _corruptedScratch.Add(info.originalItem);
                }
            }

            if (!_corruptedScratch.SetEquals(_lastCorruptedIndices))
            {
                _itemsDirty = true;
                _lastCorruptedIndices = new HashSet<ItemIndex>(_corruptedScratch);
            }

            if (!_itemsDirty && !dronesWereDirty && !remoteWasDirty && !tradesWereDirty && _hasSnapshot && !pendingOnly)
            {
                return false;
            }

            DebugLog.Trace(_log, $"BuildSnapshotIfNeeded: Rebuilding (Items:{_itemsDirty}, Drones:{_dronesDirty}, Remote:{_remoteItemsDirty}, Trade:{_tradeDirty}, Force:{_forceRebuildPending})");

            // ---------------- Drones ----------------
            if (dronesWereDirty || _cachedGlobalDronePotential == null)
            {
                _globalScrapCandidates.Clear();
                _cachedGlobalDronePotential = new int[totalLen];

                if (canScrapDrones)
                {
                    foreach (var pcmc in PlayerCharacterMasterController.instances)
                    {
                        var netUser = pcmc.networkUser;
                        if (!netUser) continue;
                        AccumulateGlobalDrones(netUser, _cachedGlobalDronePotential);
                    }

                    if (_globalScrapCandidates.Count > 0)
                    {
                        var localNetUser = GetLocalUser()?.currentNetworkUser;
                        foreach (var tierList in _globalScrapCandidates.Values)
                        {
                            tierList.Sort((a, b) =>
                            {
                                int costComparison = a.UpgradeCount.CompareTo(b.UpgradeCount);
                                if (costComparison != 0) return costComparison;

                                bool aLocal = a.Owner == localNetUser;
                                bool bLocal = b.Owner == localNetUser;
                                if (aLocal != bLocal) return aLocal ? -1 : 1;

                                string nameA = DroneCatalog.GetDroneDef(a.DroneIdx)?.nameToken ?? string.Empty;
                                string nameB = DroneCatalog.GetDroneDef(b.DroneIdx)?.nameToken ?? string.Empty;
                                return string.Compare(nameA, nameB, StringComparison.Ordinal);
                            });
                        }
                    }
                }
                _dronesDirty = false;
            }

            // ---------------- Physical stacks ----------------
            int[] physical = (_cachedLocalPhysical != null)
                ? (int[])_cachedLocalPhysical.Clone()
                : new int[totalLen];

            foreach (var itemIdx in _lastCorruptedIndices)
            {
                int idx = (int)itemIdx;
                if ((uint)idx < (uint)physical.Length)
                    physical[idx] = 0;
            }

            // ---------------- Drone stacks ----------------
            int[] drone = (_cachedGlobalDronePotential != null)
                ? (int[])_cachedGlobalDronePotential.Clone()
                : Array.Empty<int>();

            // ---------------- Masks ----------------
            ulong[] physMask = GenerateMask(physical, totalLen);
            ulong[] droneMask = GenerateMask(drone, totalLen);


            // ---------------- Copy candidates ----------------
            Dictionary<int, List<DroneCandidate>> candidatesCopy;
            if (_globalScrapCandidates.Count == 0)
            {
                candidatesCopy = new Dictionary<int, List<DroneCandidate>>();
            }
            else
            {
                candidatesCopy = _globalScrapCandidates.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new List<DroneCandidate>(kvp.Value)
                );
            }

            // ---------------- Allied stacks + remaining trades ----------------
            Dictionary<NetworkUser, int[]> alliedPhysicalStacks = null;
            Dictionary<NetworkUser, int> remainingTrades = null;

            if (poolingIsEnabled)
            {
                if (remoteWasDirty || tradesWereDirty || _snapshotAlliedPhysicalStacks == null || _snapshotTradesRemaining == null)
                {
                    _snapshotAlliedPhysicalStacks = new Dictionary<NetworkUser, int[]>();
                    _snapshotTradesRemaining = new Dictionary<NetworkUser, int>();

                    var localUser = GetLocalUser();

                    RefreshAlliedPhysicalCache(pruneStale: true);

                    foreach (var pcmc in PlayerCharacterMasterController.instances)
                    {
                        if (!pcmc) continue;

                        var netUser = pcmc.networkUser;
                        if (!netUser) continue;
                        if (netUser.localUser == localUser) continue;

                        var inv = pcmc.master ? pcmc.master.inventory : null;
                        if (!inv) continue;

                        int tradesLeft = TradeTracker.GetRemainingTrades(netUser);
                        if (tradesLeft <= 0) continue;

                        if (_cachedAlliedPhysical.TryGetValue(inv, out int[] cachedStacks) && cachedStacks != null)
                        {
                            _snapshotAlliedPhysicalStacks[netUser] = (int[])cachedStacks.Clone();
                            _snapshotTradesRemaining[netUser] = tradesLeft;
                        }
                    }
                }
                alliedPhysicalStacks = _snapshotAlliedPhysicalStacks;
                remainingTrades = _snapshotTradesRemaining;
            }
            else
            {
                _snapshotAlliedPhysicalStacks = null;
                _snapshotTradesRemaining = null;
            }

            // ---------------- Filtered recipes ----------------
            var filteredRecipes = RecipeProvider.GetFilteredRecipes(_lastCorruptedIndices) ?? new List<ChefRecipe>();

            // ---------------- Build snapshot ----------------
            _snapshot = new InventorySnapshot(
                physical,
                drone,
                candidatesCopy,
                _lastCorruptedIndices,
                canScrapDrones,
                poolingIsEnabled,
                CookBook.DepthLimit,
                filteredRecipes,
                physMask,
                droneMask,
                alliedPhysicalStacks,
                remainingTrades
            );

            _hasSnapshot = true;
            _itemsDirty = false;
            _remoteItemsDirty = false;
            _tradeDirty = false;

            built = true;
            return true;
        }

        private static void AccumulateGlobalDrones(NetworkUser user, int[] globalPotentialBuffer)
        {
            if (!user || !user.master) return;

            var minions = CharacterBody.readOnlyInstancesList.Where(b =>
                b?.master &&
                b.master.minionOwnership &&
                b.master.minionOwnership.ownerMaster == user.master);

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
                        int upgradeCount = minionBody.inventory.GetItemCountEffective(DLC3Content.Items.DroneUpgradeHidden);

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
                            UpgradeCount = upgradeCount,
                            MinionMasterNetId = MakeOwnerScopedMinionKey(minionBody)
                        });
                    }
                }
            }
        }

        static ulong MakeOwnerScopedMinionKey(CharacterBody minionBody)
        {
            uint ownerId = GetOwnerMasterId(minionBody);
            uint minionId = GetMinionInstanceId(minionBody);

            return ((ulong)ownerId << 32) | (ulong)minionId;
        }

        static uint GetOwnerMasterId(CharacterBody minionBody)
        {
            var ownerMaster = minionBody?.master?.minionOwnership?.ownerMaster;
            if (ownerMaster) return ownerMaster.netId.Value;

            var ownerBody = minionBody?.GetOwnerBody();
            if (ownerBody?.master) return ownerBody.master.netId.Value;

            return 0u;
        }

        static uint GetMinionInstanceId(CharacterBody minionBody)
        {
            if (!minionBody) return 0u;

            uint id = minionBody.netId.Value;

            if (id == 0u)
                id = unchecked((uint)minionBody.GetInstanceID());

            return id;
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

        private static bool IsPlayerInventory(Inventory inv, out PlayerCharacterMasterController pcmc)
        {
            pcmc = null;
            if (!inv) return false;

            // Inventory is a component on the master object.
            var master = inv.GetComponent<CharacterMaster>();
            if (!master) return false;

            // This is the discriminator: only real players have PCMC.
            pcmc = master.playerCharacterMasterController;
            return pcmc != null;
        }


        /// <summary>
        /// Binds to localuser when body is spawned, ignoring other bodies.
        /// </summary>
        private static void OnBodyStart(CharacterBody body)
        {
            if (!_active || body == null) return;

            var networkUser = body.master?.playerCharacterMasterController?.networkUser;
            if (networkUser && networkUser.localUser == GetLocalUser())
            {
                DebugLog.Trace(_log, $"OnBodyStart: Local body detected ({body.name}). Binding inventory.");
                RebindLocal(body.inventory);
                _cachedLocalPhysical = GetUnifiedStacksFor(body.inventory);

                int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
                for (int i = 0; i < totalLen; i++) _pendingChanged.Add(i);

                _itemsDirty = true;
                _dronesDirty = true;
                _remoteItemsDirty = true;
                _tradeDirty = true;
                MarkForceRebuild();
                QueueEmit("OnBodyStart: Local");
            }

            if (!CookBook.IsDroneScrappingEnabled) return;

            DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex);
            MarkDroneScrapDirty(dIdx, "Drone OnBodyStart");
        }

        private static void OnBodyDestroyed(CharacterBody body)
        {
            if (!_active || _isTransitioning || body == null) return;

            var master = body.master;
            if (master && _localInventory && master.inventory == _localInventory)
            {
                DebugLog.Trace(_log, "OnBodyDestroyed: Local player died or body destroyed. Nulling local inventory.");
                _localInventory = null;
            }

            if (CookBook.IsPoolingEnabled && IsPlayerBody(body, out var deadMaster))
            {
                if (RemoveAlliedFromCache(deadMaster))
                {
                    DebugLog.Trace(_log, "OnBodyDestroyed: Allied body destroyed. Removing from cache.");
                    _remoteItemsDirty = true;
                    MarkForceRebuild();

                    QueueEmit("OnBodyDestroyed: Allied");
                }
            }

            if (CookBook.IsDroneScrappingEnabled)
            {
                DroneIndex dIdx = DroneCatalog.GetDroneIndexFromBodyIndex(body.bodyIndex);
                if (dIdx != DroneIndex.None)
                {
                    DroneDef dDef = DroneCatalog.GetDroneDef(dIdx);
                    if (dDef != null && dDef.canScrap)
                    {
                        int scrapIdx = GetScrapIndexFromDrone(dIdx);
                        if (scrapIdx != -1) _pendingChanged.Add(scrapIdx);
                        _dronesDirty = true;
                        QueueEmit("OnBodyDestroyed: Drone");
                    }
                }
            }

        }

        //----------------------------------- Binding Logic ----------------------------------
        // Fallback Binding (late enable)
        private static bool TryBindFromExistingBodies()
        {
            var localUser = GetLocalUser();
            if (localUser == null) return false;
            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                if (!pcmc) continue;

                var netUser = pcmc.networkUser;
                if (!netUser) continue;

                if (netUser.localUser != localUser) continue;

                var master = pcmc.master;
                var inv = master?.inventory;
                if (!inv) break;

                var body = master.GetBody();

                DebugLog.Trace(_log, body
                        ? $"InventoryTracker.TryBindFromExistingBodies(): late binding to body {body.name}."
                        : "InventoryTracker.TryBindFromExistingBodies(): late binding to local master (body not available)."
                );

                RebindLocal(inv);
                _cachedLocalPhysical = GetUnifiedStacksFor(inv);

                int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
                for (int i = 0; i < totalLen; i++) _pendingChanged.Add(i);

                _itemsDirty = true;
                _dronesDirty = true;
                _remoteItemsDirty = true;
                _tradeDirty = true;

                MarkForceRebuild();
                QueueEmit("TryBindFromExistingBodies");
                return true;
            }
            DebugLog.Trace(_log, "InventoryTracker.TryBindFromExistingBodies(): no matching local PCMController found.");
            return false;
        }

        private static void RebindLocal(Inventory inv)
        {
            _localInventory = inv;
        }


        //--------------------------------- Helpers ------------------------------------------
        private static int RefreshAlliedPhysicalCache(bool pruneStale)
        {
            if (_cachedAlliedPhysical == null)
                return 0;

            var localUser = GetLocalUser();
            int wrote = 0;

            _liveAlliedInvScratch.Clear();

            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                if (!pcmc) continue;

                var master = pcmc.master;
                var inv = master ? master.inventory : null;
                if (!inv) continue;

                _liveAlliedInvScratch.Add(inv);
            }

            if (pruneStale && _cachedAlliedPhysical.Count > 0)
            {
                _pruneAlliedInvScratch.Clear();

                foreach (var kvp in _cachedAlliedPhysical)
                {
                    var inv = kvp.Key;
                    if (!inv || !_liveAlliedInvScratch.Contains(inv))
                        _pruneAlliedInvScratch.Add(inv);
                }

                for (int i = 0; i < _pruneAlliedInvScratch.Count; i++)
                    _cachedAlliedPhysical.Remove(_pruneAlliedInvScratch[i]);
            }

            foreach (var pcmc in PlayerCharacterMasterController.instances)
            {
                if (!pcmc) continue;

                var netUser = pcmc.networkUser;
                if (!netUser) continue;

                if (localUser != null && netUser.localUser == localUser)
                    continue;

                var master = pcmc.master;
                var inv = master ? master.inventory : null;
                if (!inv) continue;

                _cachedAlliedPhysical[inv] = GetUnifiedStacksFor(inv);
                wrote++;
            }

            return wrote;
        }

        private static bool RemoveAlliedFromCache(CharacterMaster ownerMaster)
        {
            if (!ownerMaster) return false;

            // if we lose our local inventory, clear local caches
            if (ownerMaster == _localInventory?.GetComponent<CharacterMaster>())
            {
                _localInventory = null;
                _cachedLocalPhysical = null;
                _itemsDirty = true;
                return false;
            }

            bool removedAny = false;

            if (_cachedAlliedPhysical != null && _cachedAlliedPhysical.Count > 0)
            {
                Inventory toRemove = null;

                foreach (var kvp in _cachedAlliedPhysical)
                {
                    var inv = kvp.Key;
                    if (!inv) continue;

                    var m = inv.GetComponent<CharacterMaster>();
                    if (m == ownerMaster)
                    {
                        toRemove = inv;
                        break;
                    }
                }

                if (toRemove)
                {
                    removedAny |= _cachedAlliedPhysical.Remove(toRemove);
                }
            }

            return removedAny;
        }

        private static bool IsPlayerBody(CharacterBody body, out CharacterMaster master)
        {
            master = body ? body.master : null;
            if (!master) return false;

            if (master.playerCharacterMasterController != null)
                return true;

            if (master.playerCharacterMasterController?.networkUser != null)
                return true;

            return false;
        }

        private static void MarkDroneScrapDirty(DroneIndex dIdx, string reason)
        {
            if (!CookBook.IsDroneScrappingEnabled) return;
            if (dIdx == DroneIndex.None) return;

            DroneDef dDef = DroneCatalog.GetDroneDef(dIdx);
            if (dDef == null || !dDef.canScrap) return;

            int scrapIdx = GetScrapIndexFromDrone(dIdx);
            if (scrapIdx != -1)
                _pendingChanged.Add(scrapIdx);

            _dronesDirty = true;
            QueueEmit(reason);
        }

        private static void MarkForceRebuild()
        {
            _forceRebuildPending = true;
        }

        private static void EnsureRunner()
        {
            if (_runner) return;
            var go = new GameObject("CookBook.InventoryTrackerRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _runner = go.AddComponent<DebounceRunner>();
        }

        private static void CancelPendingEmit()
        {
            if (_pendingEmit != null && _runner)
            {
                _runner.StopCoroutine(_pendingEmit);
                _pendingEmit = null;
            }
        }

        private static void QueueEmit(string trigger)
        {
            if (!_active) return;

            EnsureRunner();
            CancelPendingEmit();

            DebugLog.Trace(_log, $"Rebuild queued ({trigger})");
            _pendingEmit = _runner.StartCoroutine(EmitAfterDelay());
        }

        private static IEnumerator EmitAfterDelay()
        {
            yield return new WaitForSecondsRealtime(SnapshotDebounceSeconds);
            _pendingEmit = null;
            EmitSnapshotNow();
        }

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

        // items only, for viewing any player's recipes off the CHEF
        internal static InventorySnapshot BuildSnapshotFor(Inventory inv)
        {
            int totalLen = ItemCatalog.itemCount + EquipmentCatalog.equipmentCount;
            int[] physical = GetUnifiedStacksFor(inv);

            var corrupted = new HashSet<ItemIndex>();
            foreach (var info in RoR2.Items.ContagiousItemManager.transformationInfos)
            {
                if (inv.GetItemCountPermanent(info.transformedItem) > 0) corrupted.Add(info.originalItem);
            }
            foreach (var itemIdx in corrupted) physical[(int)itemIdx] = 0;

            return new InventorySnapshot(
                physical,
                Array.Empty<int>(),
                null,
                corrupted,
                false,
                false,
                CookBook.DepthLimit,
                RecipeProvider.GetFilteredRecipes(corrupted) ?? new List<ChefRecipe>(),
                GenerateMask(physical, totalLen),
                GenerateMask(null, totalLen),
                null,
                null
            );
        }

        private static int[] GetUnifiedStacksFor(Inventory inv)
        {
            int itemLen = ItemCatalog.itemCount;
            int totalLen = itemLen + EquipmentCatalog.equipmentCount;
            int[] stacks = new int[totalLen];

            for (int i = 0; i < itemLen; i++) stacks[i] = inv.GetItemCountPermanent((ItemIndex)i);

            int slotCount = inv.GetEquipmentSlotCount();
            int setCount = inv.accessibleEquipmentSetCount;
            for (int slot = 0; slot < slotCount; slot++)
            {
                for (int set = 0; set < setCount; set++)
                {
                    var location = new EquipmentLocation { slot = (uint)slot, set = (uint)set };
                    var state = inv.GetEquipment(in location);
                    if (state.equipmentIndex != EquipmentIndex.None)
                    {
                        int unifiedIndex = itemLen + (int)state.equipmentIndex;
                        if (unifiedIndex < totalLen) stacks[unifiedIndex] += 1;
                    }
                }
            }
            return stacks;
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

        private static ulong[] GenerateMask(int[] stacks, int bitCount)
        {
            int words = (bitCount + 63) >> 6;
            ulong[] mask = new ulong[words];
            if (stacks == null) return mask;

            int n = Math.Min(stacks.Length, bitCount);
            for (int i = 0; i < n; i++)
                if (stacks[i] > 0)
                    mask[i >> 6] |= 1UL << (i & 63);

            return mask;
        }

        private sealed class DebounceRunner : MonoBehaviour { }
    }

    //--------------------------------------- Structs  ----------------------------------------
    /// <summary>
    /// Immutable snapshot of the local player's inventory at a moment in time.
    /// </summary>
    internal readonly struct InventorySnapshot
    {
        public readonly int[] PhysicalStacks;
        public readonly int[] DronePotential;
        public readonly ulong[] PhysicalMask;
        public readonly ulong[] DroneMask;

        public readonly Dictionary<int, List<DroneCandidate>> AllScrapCandidates;
        public readonly HashSet<ItemIndex> CorruptedIndices;

        public readonly bool CanScrapDrones;
        public readonly bool IsPoolingEnabled;
        public readonly int maxDepth;
        public readonly IReadOnlyList<ChefRecipe> FilteredRecipes;

        public readonly Dictionary<NetworkUser, int[]> AlliedPhysicalStacks;
        public readonly Dictionary<NetworkUser, int> TradesRemaining;

        public InventorySnapshot(
            int[] physical,
            int[] drone,
            Dictionary<int, List<DroneCandidate>> allCandidates,
            HashSet<ItemIndex> corrupted,
            bool scrapperPresent,
            bool poolingEnabled,
            int maxdepth,
            IReadOnlyList<ChefRecipe> filteredRecipes,
            ulong[] physMask,
            ulong[] droneMask,
            Dictionary<NetworkUser, int[]> alliedPhysicalStacks,
            Dictionary<NetworkUser, int> remainingTrades)
        {
            PhysicalStacks = physical ?? Array.Empty<int>();
            DronePotential = drone ?? Array.Empty<int>();
            AllScrapCandidates = allCandidates ?? new Dictionary<int, List<DroneCandidate>>();
            CorruptedIndices = corrupted ?? new HashSet<ItemIndex>();
            PhysicalMask = physMask;
            DroneMask = droneMask;
            AlliedPhysicalStacks = alliedPhysicalStacks ?? new Dictionary<NetworkUser, int[]>();
            TradesRemaining = remainingTrades ?? new Dictionary<NetworkUser, int>();
            maxDepth = maxdepth;
            CanScrapDrones = scrapperPresent;
            IsPoolingEnabled = poolingEnabled;
            FilteredRecipes = filteredRecipes ?? new List<ChefRecipe>();
        }
    }
    internal struct DroneCandidate
    {
        public NetworkUser Owner;
        public DroneIndex DroneIdx;
        public int UpgradeCount;
        public ulong MinionMasterNetId;
    }
}
