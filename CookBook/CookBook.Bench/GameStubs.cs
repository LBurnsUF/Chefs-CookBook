using System;
using System.Collections.Generic;

namespace BepInEx.Logging
{
    public class ManualLogSource
    {
        private readonly string _name;
        public ManualLogSource(string name) => _name = name;
        public void LogInfo(object data) => Console.WriteLine($"[Info] {data}");
        public void LogDebug(object data) { }
        public void LogWarning(object data) => Console.WriteLine($"[Warn] {data}");
        public void LogError(object data) => Console.Error.WriteLine($"[Error] {data}");
    }
}

namespace UnityEngine.Networking
{
    public struct NetworkInstanceId : IEquatable<NetworkInstanceId>
    {
        public uint Value;
        public NetworkInstanceId(uint v) => Value = v;
        public bool Equals(NetworkInstanceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is NetworkInstanceId other && Equals(other);
        public override int GetHashCode() => (int)Value;
        public static bool operator ==(NetworkInstanceId a, NetworkInstanceId b) => a.Value == b.Value;
        public static bool operator !=(NetworkInstanceId a, NetworkInstanceId b) => a.Value != b.Value;
    }
}

namespace RoR2
{
    public enum ItemIndex { None = -1 }
    public enum EquipmentIndex { None = -1 }
    public enum ItemTier
    {
        Tier1, Tier2, Tier3,
        VoidTier1, VoidTier2, VoidTier3,
        Boss, VoidBoss, Lunar, NoTier,
        FoodTier
    }

    public class ItemDef
    {
        public string name;
        public string nameToken;
        public ItemTier? tier;
        public bool hidden;
    }

    public class EquipmentDef
    {
        public string name;
        public string nameToken;
    }

    public class NetworkUser
    {
        public UnityEngine.Networking.NetworkInstanceId netId;

        public static implicit operator bool(NetworkUser u) => u != null;
    }

    public static class Language
    {
        private static Dictionary<string, string> _strings = new();

        public static void SetStrings(Dictionary<string, string> strings) => _strings = strings ?? new();

        public static string GetString(string token)
        {
            if (token != null && _strings.TryGetValue(token, out var s)) return s;
            return token ?? "???";
        }
    }

    public static class ItemCatalog
    {
        public static int itemCount;

        private static ItemDef[] _items = Array.Empty<ItemDef>();

        public static void SetItems(ItemDef[] items)
        {
            _items = items ?? Array.Empty<ItemDef>();
            itemCount = _items.Length;
        }

        public static ItemDef GetItemDef(ItemIndex idx)
        {
            int i = (int)idx;
            return (i >= 0 && i < _items.Length) ? _items[i] : null;
        }
    }

    public static class EquipmentCatalog
    {
        public static int equipmentCount;

        private static EquipmentDef[] _equips = Array.Empty<EquipmentDef>();

        public static void SetEquipment(EquipmentDef[] equips)
        {
            _equips = equips ?? Array.Empty<EquipmentDef>();
            equipmentCount = _equips.Length;
        }

        public static EquipmentDef GetEquipmentDef(EquipmentIndex idx)
        {
            int i = (int)idx;
            return (i >= 0 && i < _equips.Length) ? _equips[i] : null;
        }
    }
}

namespace RoR2.Items
{
    public static class ContagiousItemManager
    {
        public struct TransformationInfo
        {
            public RoR2.ItemIndex transformedItem;
            public RoR2.ItemIndex originalItem;
        }

        public static TransformationInfo[] transformationInfos = Array.Empty<TransformationInfo>();
    }
}

// ──────────── CleanerChef ────────────
public enum DroneIndex { None = -1 }

public static class DroneUpgradeUtils
{
    public static int GetDroneCountFromUpgradeCount(int upgradeCount) => upgradeCount;
}
