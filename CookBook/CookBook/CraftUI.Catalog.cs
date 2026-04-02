using RoR2;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static string GetEntryDisplayName(CraftableEntry entry)
        {
            if (entry == null) return "Unknown Result";
            if (entry.IsItem)
            {
                var idef = ItemCatalog.GetItemDef(entry.ResultItem);
                return idef != null ? Language.GetString(idef.nameToken) : $"bad Item {entry.ResultIndex}";
            }
            else
            {
                var edef = EquipmentCatalog.GetEquipmentDef(entry.ResultEquipment);
                return edef != null ? Language.GetString(edef.nameToken) : $"bad Equipment {entry.ResultIndex}";
            }
        }

        internal static Sprite GetIcon(int unifiedIndex)
        {
            if (_iconCache.TryGetValue(unifiedIndex, out var sprite)) return sprite;

            if (unifiedIndex < ItemCatalog.itemCount) sprite = ItemCatalog.GetItemDef((ItemIndex)unifiedIndex)?.pickupIconSprite;
            else
            {
                int equipIdx = unifiedIndex - ItemCatalog.itemCount;
                sprite = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)equipIdx)?.pickupIconSprite;
            }

            if (sprite != null) _iconCache[unifiedIndex] = sprite;
            return sprite;
        }

        internal static Sprite GetDroneIcon(DroneIndex droneIndex)
        {
            if (_droneIconCache.TryGetValue(droneIndex, out var sprite)) return sprite;

            var def = DroneCatalog.GetDroneDef(droneIndex);
            if (def != null && def.iconSprite != null)
            {
                sprite = def.iconSprite;
                _droneIconCache[droneIndex] = sprite;
                return sprite;
            }
            return null;
        }

        internal static Color GetEntryColor(CraftableEntry entry)
        {
            PickupIndex pickupIndex = PickupIndex.none;

            if (entry.IsItem) pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultItem);
            else pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultEquipment);

            if (!pickupIndex.isValid) return Color.white;
            var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
            return pickupDef != null ? pickupDef.baseColor : Color.white;
        }

        internal static bool AreEntriesSame(CraftableEntry a, CraftableEntry b)
        {
            if (a == null || b == null) return false;
            return a.ResultIndex == b.ResultIndex && a.ResultCount == b.ResultCount;
        }
    }
}
