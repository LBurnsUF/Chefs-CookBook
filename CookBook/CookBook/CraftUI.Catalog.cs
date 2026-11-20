using RoR2;
using UnityEngine;
using UnityEngine.AddressableAssets;
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

        // obsolete direct sprite still needed for modded content
#pragma warning disable CS0618
        internal static bool TryGetIcon(int unifiedIndex, out AssetReferenceT<Sprite> reference, out Sprite direct)
        {
            if (unifiedIndex < ItemCatalog.itemCount)
            {
                var idef = ItemCatalog.GetItemDef((ItemIndex)unifiedIndex);
                if (idef != null) return PickIconSource(idef.pickupIconSpriteReference, idef.pickupIconSprite, out reference, out direct);
            }
            else
            {
                int equipIdx = unifiedIndex - ItemCatalog.itemCount;
                var edef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)equipIdx);
                if (edef != null) return PickIconSource(edef.pickupIconSpriteReference, edef.pickupIconSprite, out reference, out direct);
            }

            reference = null;
            direct = null;
            return false;
        }

        internal static bool TryGetDroneIcon(DroneIndex droneIndex, out AssetReferenceT<Sprite> reference, out Sprite direct)
        {
            var def = DroneCatalog.GetDroneDef(droneIndex);
            if (def != null) return PickIconSource(def.iconSpriteReference, def.iconSprite, out reference, out direct);

            reference = null;
            direct = null;
            return false;
        }
#pragma warning restore CS0618

        // v1.5.0: vanilla icons are addressable only, direct sprite is modded content and wins
        private static bool PickIconSource(AssetReferenceT<Sprite> address, Sprite sprite, out AssetReferenceT<Sprite> reference, out Sprite direct)
        {
            direct = sprite;
            reference = direct == null && address != null && address.RuntimeKeyIsValid() ? address : null;
            return direct != null || reference != null;
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
