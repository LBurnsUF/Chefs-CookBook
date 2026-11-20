using RoR2;
using System.Collections.Generic;
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

        // says why the list is empty, nothing craftable or all filtered out
        internal static void RefreshEmptyRow()
        {
            if (_recipeListContent == null) return;

            bool anyShown = false;
            foreach (var row in _recipeRowUIs)
            {
                if (row.RowGO != null && row.RowGO.activeSelf)
                {
                    anyShown = true;
                    break;
                }
            }

            if (anyShown)
            {
                if (_emptyRow != null) _emptyRow.gameObject.SetActive(false);
                return;
            }

            if (_emptyRow == null)
            {
                _emptyRow = CreateRecipeRow(_recipeListContent);
                if (_emptyRow == null) return;
                BindEmptyRow(_emptyRow);
            }

            _emptyRow.transform.SetParent(_recipeListContent, false);
            _emptyRow.transform.SetAsLastSibling();
            _emptyRow.gameObject.SetActive(true);
            _emptyRow.View.ItemLabel.text = _recipeRowUIs.Count == 0 ? "NONE" : "FILTER TOO NARROW";
        }

        internal static void BindEmptyRow(RecipeRowRuntime runtime)
        {
            runtime.Entry = null;
            runtime.IsExpanded = false;

            var view = runtime.View;
            if (view.RowTopButton != null) view.RowTopButton.interactable = false;
            if (view.ArrowText != null) view.ArrowText.gameObject.SetActive(false);
            if (view.ItemLabel != null) view.ItemLabel.color = Color.gray;
            if (view.DepthText != null) view.DepthText.text = "";
            if (view.PathsText != null) view.PathsText.text = "";

            // hatched empty slot, this row is never rebound to a recipe
            view.ResultSlot.Icon.type = Image.Type.Tiled;
            view.ResultSlot.Icon.pixelsPerUnitMultiplier = 2f;
            view.ResultSlot.Icon.color = new Color(1f, 1f, 1f, 0.3f);
            view.ResultSlot.IconLoader.SetAssetOrDirectReference(HatchingTileRef, null);
            if (view.ResultSlot.StackText != null) view.ResultSlot.StackText.gameObject.SetActive(false);

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

        // reuses the path row at index, its slots come from the pool
        internal static void BindPathRow(RectTransform parent, int index, RecipeChain chain, RecipeRowRuntime owner)
        {
            if (_pathRowTemplate == null) return;

            PathRowRuntime runtime;
            if (index < parent.childCount) runtime = parent.GetChild(index).GetComponent<PathRowRuntime>();
            else
            {
                GameObject pathRowGO = UnityEngine.Object.Instantiate(_pathRowTemplate, parent);
                pathRowGO.name = "PathRow";
                runtime = pathRowGO.GetComponent<PathRowRuntime>();
            }

            runtime.gameObject.SetActive(true);
            runtime.Init(owner, chain);
            ReleaseSlots(runtime);

            if (chain.PhysicalCostSparse != null)
            {
                foreach (var ingredient in chain.PhysicalCostSparse)
                {
                    if (TryGetIcon(ingredient.UnifiedIndex, out var iconRef, out var icon)) AddSlot(runtime, _ingredientSlotTemplate, iconRef, icon, ingredient.Count);
                }
            }

            if (chain.AlliedTradeSparse != null)
            {
                foreach (var trade in chain.AlliedTradeSparse)
                {
                    if (TryGetIcon(trade.UnifiedIndex, out var iconRef, out var icon)) AddSlot(runtime, _tradeSlotTemplate, iconRef, icon, trade.TradesRequired);
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

                            AddSlot(runtime, template, droneRef, droneSprite, requirement.Count);
                        }
                    }
                }
            }
        }

        internal static void AddSlot(PathRowRuntime row, GameObject template, AssetReferenceT<Sprite> iconRef, Sprite icon, int count)
        {
            if (template == null) return;
            if (!_slotPools.TryGetValue(template, out var pool)) _slotPools[template] = pool = new Stack<SlotView>();

            SlotView slot;
            if (pool.Count > 0)
            {
                slot = pool.Pop();
                slot.transform.SetParent(row.View.VisualRect, false);
            }
            else slot = UnityEngine.Object.Instantiate(template, row.View.VisualRect).GetComponent<SlotView>();

            slot.gameObject.SetActive(true);
            row.Slots.Add((template, slot));

            slot.IconLoader.SetAssetOrDirectReference(iconRef, icon);

            if (slot.StackText)
            {
                slot.StackText.text = count > 1 ? count.ToString() : string.Empty;
                slot.StackText.gameObject.SetActive(count > 1);
            }
        }

        internal static void ReleaseSlots(PathRowRuntime row)
        {
            foreach (var (template, slot) in row.Slots)
            {
                slot.gameObject.SetActive(false);
                slot.transform.SetParent(_slotPoolRoot, false);
                _slotPools[template].Push(slot);
            }
            row.Slots.Clear();
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
