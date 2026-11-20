using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static RecipeRowRuntime CreateRecipeRow(RectTransform parent)
        {
            if (_recipeRowTemplate == null) return null;

            // ---------------- Initialize root ----------------
            GameObject rowGO = UnityEngine.Object.Instantiate(_recipeRowTemplate, parent);
            rowGO.name = "RecipeRow";
            rowGO.SetActive(true);

            var runtime = rowGO.GetComponent<RecipeRowRuntime>();
            if (runtime == null)
            {
                _log.LogError("CreateRecipeRow: RecipeRowRuntime component missing on template.");
                return null;
            }

            var view = runtime.View;
            if (view.RowTopButton != null)
            {
                view.RowTopButton.onClick.RemoveAllListeners();
                view.RowTopButton.onClick.AddListener(() => ToggleRecipeRow(runtime));
                view.RowTopButton.HighlightChanged = lit => OnRowTopHighlightChanged(runtime, lit);
            }

            return runtime;
        }

        // fills a new or reused row
        internal static void BindRecipeRow(RecipeRowRuntime runtime, CraftableEntry entry)
        {
            runtime.Entry = entry;
            runtime.IsExpanded = false;

            var view = runtime.View;

            // ---------------- Label ----------------
            if (view.ItemLabel != null)
            {
                view.ItemLabel.text = GetEntryDisplayName(entry);
                view.ItemLabel.color = GetEntryColor(entry);
            }

            // ---------------- MetaData: Depth ----------------
            if (view.DepthText != null) view.DepthText.text = $" Min Depth: {entry.MinDepth}";

            // ---------------- MetaData: Paths ----------------
            if (view.PathsText != null) view.PathsText.text = $"Paths: {entry.Chains.Count}";

            // ---------------- ItemIcon ----------------
            if (TryGetIcon(entry.ResultIndex, out var iconRef, out var iconSprite))
            {
                view.ResultSlot.IconLoader.SetAssetOrDirectReference(iconRef, iconSprite);
                view.ResultSlot.Icon.color = Color.white;
            }
            else
            {
                // the loader hides the icon mid load
                view.ResultSlot.IconLoader.Clear();
                view.ResultSlot.Icon.enabled = true;
                view.ResultSlot.Icon.color = new Color(1f, 1f, 1f, 0.1f);
            }

            // ---------------- Stack Text ----------------
            if (view.ResultSlot.StackText != null)
            {
                if (entry.ResultCount > 1)
                {
                    view.ResultSlot.StackText.text = entry.ResultCount.ToString();
                    view.ResultSlot.StackText.gameObject.SetActive(true);
                }
                else view.ResultSlot.StackText.gameObject.SetActive(false);
            }

            // ---------------- Collapsed state ----------------
            SetArrowExpanded(view, false);
            if (view.DropdownLayoutElement != null) view.DropdownLayoutElement.preferredHeight = 0f;
            if (view.RowLayoutElement != null) view.RowLayoutElement.preferredHeight = runtime.CollapsedHeight;
        }

        // reticle marks gamepad focus on row headers
        internal static void OnRowTopHighlightChanged(RecipeRowRuntime runtime, bool lit)
        {
            if (!runtime || !runtime.View.RowTop) return;
            if (lit) AttachReticleTo(runtime.View.RowTop);
            else if (IsReticleAttachedTo(runtime.View.RowTop))
            {
                if (_selectedAnchor) AttachReticleTo(_selectedAnchor);
                else if (_selectionReticle) _selectionReticle.gameObject.SetActive(false);
            }
        }

        internal static GameObject CreatePathRow(RectTransform parent, RecipeChain chain, RecipeRowRuntime owner)
        {
            if (_pathRowTemplate == null) return null;

            GameObject pathRowGO = UnityEngine.Object.Instantiate(_pathRowTemplate, parent);
            pathRowGO.name = "PathRow";
            pathRowGO.SetActive(true);

            var runtime = pathRowGO.GetComponent<PathRowRuntime>();
            if (runtime != null) runtime.Init(owner, chain);
            else _log.LogError("PathRowTemplate missing PathRowRuntime component.");

            if (chain.PhysicalCostSparse != null)
            {
                foreach (var ingredient in chain.PhysicalCostSparse)
                {
                    if (TryGetIcon(ingredient.UnifiedIndex, out var iconRef, out var icon)) InstantiateSlot(_ingredientSlotTemplate, runtime.View.VisualRect, iconRef, icon, ingredient.Count);
                }
            }

            if (chain.AlliedTradeSparse != null)
            {
                foreach (var trade in chain.AlliedTradeSparse)
                {
                    if (TryGetIcon(trade.UnifiedIndex, out var iconRef, out var icon)) InstantiateSlot(_tradeSlotTemplate, runtime.View.VisualRect, iconRef, icon, trade.TradesRequired);
                }
            }

            if (chain.DroneCostSparse != null)
            {
                var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

                foreach (var requirement in chain.DroneCostSparse)
                {
                    if (requirement.DroneIdx != DroneIndex.None)
                    {
                        if (TryGetDroneIcon(requirement.DroneIdx, out var droneRef, out var droneSprite))
                        {
                            bool isAlliedDrone = requirement.Owner != null && requirement.Owner != localUser;
                            GameObject template = isAlliedDrone ? _tradeSlotTemplate : _droneSlotTemplate;

                            InstantiateSlot(template, runtime.View.VisualRect, droneRef, droneSprite, requirement.Count);
                        }
                    }
                }
            }

            return pathRowGO;
        }

        internal static void InstantiateSlot(GameObject template, Transform parentrow, AssetReferenceT<Sprite> iconRef, Sprite icon, int count)
        {
            if (template == null) return;
            GameObject slotGO = UnityEngine.Object.Instantiate(template, parentrow);
            slotGO.SetActive(true);

            var slot = slotGO.GetComponent<SlotView>();
            if (!slot) return;

            slot.IconLoader.SetAssetOrDirectReference(iconRef, icon);

            if (slot.StackText)
            {
                slot.StackText.text = count > 1 ? count.ToString() : string.Empty;
                slot.StackText.gameObject.SetActive(count > 1);
            }
        }

        internal static GameObject InstantiateLayer(GameObject template, Transform parent)
        {
            var layer = UnityEngine.Object.Instantiate(template, parent, false);
            layer.SetActive(true);

            var rt = layer.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var le = layer.GetComponent<LayoutElement>();
            if (le) le.ignoreLayout = true;

            SetupStaticVisuals(layer);

            return layer;
        }

        internal static void SetupStaticVisuals(GameObject root)
        {
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
