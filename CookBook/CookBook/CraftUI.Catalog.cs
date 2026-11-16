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
        internal static Sprite GetIcon(int unifiedIndex)
        {
            if (_iconCache.TryGetValue(unifiedIndex, out var sprite)) return sprite;

            if (unifiedIndex < ItemCatalog.itemCount)
            {
                var idef = ItemCatalog.GetItemDef((ItemIndex)unifiedIndex);
                if (idef != null) sprite = LoadIcon(idef.pickupIconSpriteReference, idef.pickupIconSprite);
            }
            else
            {
                int equipIdx = unifiedIndex - ItemCatalog.itemCount;
                var edef = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)equipIdx);
                if (edef != null) sprite = LoadIcon(edef.pickupIconSpriteReference, edef.pickupIconSprite);
            }

            if (sprite != null) _iconCache[unifiedIndex] = sprite;
            return sprite;
        }

        internal static Sprite GetDroneIcon(DroneIndex droneIndex)
        {
            if (_droneIconCache.TryGetValue(droneIndex, out var sprite)) return sprite;

            var def = DroneCatalog.GetDroneDef(droneIndex);
            sprite = def != null ? LoadIcon(def.iconSpriteReference, def.iconSprite) : null;
            if (sprite != null)
            {
                _droneIconCache[droneIndex] = sprite;
                return sprite;
            }
            return null;
        }
#pragma warning restore CS0618

        // v1.5.0: vanilla icons are addressable only, direct sprite is modded content
        private static Sprite LoadIcon(AssetReferenceT<Sprite> reference, Sprite direct)
        {
            if (direct != null) return direct;
            if (reference == null || !reference.RuntimeKeyIsValid()) return null;
            return Addressables.LoadAssetAsync<Sprite>(reference.RuntimeKey).WaitForCompletion();
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
