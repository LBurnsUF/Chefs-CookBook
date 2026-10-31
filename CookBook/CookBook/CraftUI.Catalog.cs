using RoR2;
using RoR2.UI;
using RoR2.ContentManagement;
using UnityEngine.UI;
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

        internal static void BindPickupIcon(Image image, int unifiedIndex)
        {
            if (!image) return;

            // Configure while inactive so OnEnable cannot initialize a new loader
            // before its renderer is assigned. This only hides the icon child.
            bool wasActive = image.gameObject.activeSelf;
            image.gameObject.SetActive(false);
            try
            {
                var asyncImage = image.gameObject.GetComponent<AsyncImage>();
                if (!asyncImage) asyncImage = image.gameObject.AddComponent<AsyncImage>();
                asyncImage.renderer = image;
                asyncImage.loadOnAssigned = true;
                asyncImage.disableWhileLoading = true;
                asyncImage.unloadType = AsyncReferenceHandleUnloadType.AtWill;
                asyncImage.EnsureInitialization(true);

                // Rebinding must retire the previous request even when the next
                // icon is already loaded or has no corresponding pickup.
                asyncImage.Clear();
                image.sprite = null;
                image.color = Color.white;
                image.enabled = true;

                Sprite sprite = GetIcon(unifiedIndex);
                if (sprite)
                {
                    asyncImage.SetImage(sprite);
                    return;
                }

                PickupIndex pickupIndex = unifiedIndex < ItemCatalog.itemCount
                    ? PickupCatalog.FindPickupIndex((ItemIndex)unifiedIndex)
                    : PickupCatalog.FindPickupIndex((EquipmentIndex)(unifiedIndex - ItemCatalog.itemCount));
                var pickup = PickupCatalog.GetPickupDef(pickupIndex);
                if (pickup == null)
                {
                    image.enabled = false;
                    return;
                }

                asyncImage.SetAssetOrDirectReference(pickup);
            }
            finally
            {
                image.gameObject.SetActive(wasActive);
            }
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
