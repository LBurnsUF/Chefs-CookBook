using RoR2;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal sealed partial class CraftUI
    {
        internal RecipeRowRuntime CreateRecipeRow(RectTransform parent)
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
            runtime.Panel = this;

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
            if (view.PathsText != null)
            {
                // the scoreboard swaps paths for how close you are
                bool progress = entry.ProgressRecipe != null;
                view.PathsText.text = progress ? $"Owned: {entry.Owned}/{entry.Needed}" : $"Paths: {entry.Chains.Count}";
                view.PathsText.gameObject.SetActive(progress || !runtime.Panel._cutDown);
            }

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

            SetSlotTooltip(view.ResultSlot, entry.ResultIndex);

            // ---------------- Progress ----------------
            // further from done reads dimmer
            if (entry.ProgressRecipe != null)
            {
                float alpha = entry.Progress == RecipeProgress.Partial ? 0.7f : 0.45f;
                if (view.ItemLabel != null) view.ItemLabel.color = view.ItemLabel.color.AlphaMultiplied(alpha);
                if (entry.Progress == RecipeProgress.None) view.ResultSlot.Icon.color = new Color(0.5f, 0.5f, 0.5f, 1f);
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
        internal void RefreshEmptyRow()
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
            view.ResultSlot.Icon.pixelsPerUnitMultiplier = 5f;
            view.ResultSlot.Icon.color = new Color(1f, 1f, 1f, 0.3f);
            view.ResultSlot.IconLoader.SetAssetOrDirectReference(HatchingTileRef, null);
            SetSlotTooltip(view.ResultSlot, -1);
            if (view.ResultSlot.StackText != null) view.ResultSlot.StackText.gameObject.SetActive(false);

            if (view.DropdownLayoutElement != null) view.DropdownLayoutElement.preferredHeight = 0f;
            if (view.RowLayoutElement != null) view.RowLayoutElement.preferredHeight = runtime.CollapsedHeight;
        }

        // reticle marks gamepad focus on row headers
        internal void OnRowTopHighlightChanged(RecipeRowRuntime runtime, bool lit)
        {
            if (!runtime || !runtime.View.RowTop) return;
            if (lit) AttachReticleTo(runtime.View.RowTop);
            if (lit && _cutDown) AttachPinButton(runtime);
            else if (IsReticleAttachedTo(runtime.View.RowTop))
            {
                if (_selectedAnchor) AttachReticleTo(_selectedAnchor);
                else if (_selectionReticle) _selectionReticle.gameObject.SetActive(false);
            }
        }

        // reuses the path row at index, its slots come from the pool
        internal void BindPathRow(RectTransform parent, int index, RecipeChain chain, RecipeRowRuntime owner)
        {
            if (_pathRowTemplate == null) return;

            PathRowRuntime runtime = AcquirePathRow(parent, index);
            runtime.Init(owner, chain);
            ReleaseSlots(runtime);

            if (chain.PhysicalCostSparse != null)
            {
                foreach (var ingredient in chain.PhysicalCostSparse)
                {
                    if (TryGetIcon(ingredient.UnifiedIndex, out var iconRef, out var icon)) SetSlotTooltip(AddSlot(runtime, _ingredientSlotTemplate, iconRef, icon, ingredient.Count), ingredient.UnifiedIndex);
                }
            }

            if (chain.AlliedTradeSparse != null)
            {
                foreach (var trade in chain.AlliedTradeSparse)
                {
                    if (TryGetIcon(trade.UnifiedIndex, out var iconRef, out var icon)) SetSlotTooltip(AddSlot(runtime, _tradeSlotTemplate, iconRef, icon, trade.TradesRequired), trade.UnifiedIndex);
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

                            SetDroneTooltip(AddSlot(runtime, template, droneRef, droneSprite, requirement.Count), requirement.DroneIdx);
                        }
                    }
                }
            }
        }

        private PathRowRuntime AcquirePathRow(RectTransform parent, int index)
        {
            PathRowRuntime runtime;
            if (index < parent.childCount) runtime = parent.GetChild(index).GetComponent<PathRowRuntime>();
            else
            {
                GameObject pathRowGO = UnityEngine.Object.Instantiate(_pathRowTemplate, parent);
                pathRowGO.name = "PathRow";
                runtime = pathRowGO.GetComponent<PathRowRuntime>();
                runtime.Panel = this;
            }

            runtime.gameObject.SetActive(true);
            return runtime;
        }

        // the recipe behind a row you can't make yet, missing ingredients hatched
        internal void BindProgressRow(RectTransform parent, int index, RecipeRowRuntime owner)
        {
            if (_pathRowTemplate == null) return;

            PathRowRuntime runtime = AcquirePathRow(parent, index);
            runtime.Init(owner, null);
            ReleaseSlots(runtime);

            var recipe = owner.Entry.ProgressRecipe;
            var stacks = _snap.PhysicalStacks;

            if (recipe.HasB && recipe.IngA == recipe.IngB) AddProgressSlot(runtime, stacks, recipe.IngA, recipe.CountA + recipe.CountB);
            else
            {
                AddProgressSlot(runtime, stacks, recipe.IngA, recipe.CountA);
                if (recipe.HasB) AddProgressSlot(runtime, stacks, recipe.IngB, recipe.CountB);
            }
        }

        private void AddProgressSlot(PathRowRuntime row, int[] stacks, int idx, int needed)
        {
            if (!TryGetIcon(idx, out var iconRef, out var icon)) return;

            var slot = AddSlot(row, _ingredientSlotTemplate, iconRef, icon, needed);
            if (slot == null) return;
            SetSlotTooltip(slot, idx);

            int owned = (uint)idx < (uint)stacks.Length ? stacks[idx] : 0;
            if (owned >= needed) return;

            SetSlotMissing(slot, true);
            if (slot.StackText)
            {
                slot.StackText.text = $"{owned}/{needed}";
                slot.StackText.gameObject.SetActive(true);
            }
        }

        // dimmed icon under a faint hatch, built on first use
        private static void SetSlotMissing(SlotView slot, bool missing)
        {
            // dark enough for the white count to read
            slot.Icon.color = missing ? new Color(0.4f, 0.4f, 0.4f, 1f) : Color.white;

            var hatch = slot.Icon.transform.Find("MissingHatch");
            if (!hatch)
            {
                if (!missing) return;

                var rt = CreateInternal("MissingHatch", slot.Icon.transform, typeof(Image));
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                var img = rt.GetComponent<Image>();
                img.sprite = GetHatchSprite();
                img.type = Image.Type.Tiled;
                img.pixelsPerUnitMultiplier = 5f;
                img.color = new Color(1f, 1f, 1f, 0.2f);
                img.raycastTarget = false;
                hatch = rt;
            }

            hatch.gameObject.SetActive(missing);
        }

        internal SlotView AddSlot(PathRowRuntime row, GameObject template, AssetReferenceT<Sprite> iconRef, Sprite icon, int count)
        {
            if (template == null) return null;
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

            // pooled slots can come back hatched
            SetSlotMissing(slot, false);
            return slot;
        }

        internal void ReleaseSlots(PathRowRuntime row)
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
