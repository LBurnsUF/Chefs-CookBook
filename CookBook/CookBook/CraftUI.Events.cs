using RoR2;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;
namespace CookBook
{
    internal static partial class CraftUI
    {
        //=========================== Events ===========================
        internal static void OnRepeatInputEndEdit(string val)
        {
            if (_selectedChainData == null)
            {
                _repeatInputField.text = string.Empty;
                return;
            }

            if (int.TryParse(val, out int requested))
            {
                if (!TryGetSnapshot(out var snap)) return;
                int max = _selectedChainData.GetMaxAffordable(snap);

                if (requested > max)
                {
                    _repeatInputField.text = $"{max.ToString()} (max)";
                }
                else if (requested < 1) _repeatInputField.text = "1";
            }
        }

        internal static void OnPathSelected(PathRowRuntime clickedPath)
        {
            _currentHoveredPath = clickedPath;
            if (_selectedPathUI == clickedPath)
            {
                DeselectCurrentPath();
                return;
            }

            if (_selectedPathUI != null) _selectedPathUI.SetSelected(false);

            _selectedPathUI = clickedPath;
            _selectedPathUI.SetSelected(true);
            _selectedChainData = clickedPath.Chain;

            _selectedAnchor = clickedPath.VisualRect;
            AttachReticleTo(_selectedAnchor);

            if (_globalCraftButton)
            {
                _globalCraftButton.interactable = true;
                _globalCraftButtonImage.color = new Color32(206, 198, 143, 200);
                _globalCraftButtonText.text = "Combine";
            }

            if (!TryGetSnapshot(out var snap)) return;
            int max = clickedPath.Chain.GetMaxAffordable(_snap);

            _repeatInputField.text = "1";
            if (_repeatInputField.placeholder is TextMeshProUGUI ph) ph.text = $"max {max}";

        }

        internal static void OnGlobalCraftButtonClicked()
        {
            if (_selectedChainData == null) return;
            if (!int.TryParse(_repeatInputField.text, out int count)) count = 1;

            if (!TryGetSnapshot(out var snap)) return;
            int max = _selectedChainData.GetMaxAffordable(_snap);
            if (max <= 0) return;

            int finalCount = Mathf.Clamp(count, 1, max);
            StateController.RequestCraft(_selectedChainData, finalCount);
        }

        internal static void CraftablesForUIChanged(IReadOnlyList<CraftableEntry> craftables, InventorySnapshot snap)
        {
            _snap = snap;
            LastCraftables = craftables;
            if (!_skeletonBuilt) return;
            PopulateRecipeList(LastCraftables);
        }

        internal static void ToggleRecipeRow(RecipeRowRuntime runtime)
        {
            if (runtime == null) return;

            if (_openRow == runtime)
            {
                CollapseRow(runtime);
                _openRow = null;
                return;
            }

            if (_openRow != null) CollapseRow(_openRow);

            ExpandRow(runtime);
            _openRow = runtime;
        }

        internal static void ExpandRow(RecipeRowRuntime runtime)
        {
            if (runtime.Entry == null || runtime.Entry.Chains == null) return;

            int chainCount = runtime.Entry.Chains.Count;

            if (chainCount != 0)
            {
                float rowHeightPx = RoundToEven(PathRowHeightNorm * _panelHeight);
                float spacingPx = RoundToEven(PathsContainerSpacingNorm * _panelHeight);
                int visibleRows = Mathf.Min(chainCount, PathsContainerMaxVisibleRows);

                float targetHeight = (visibleRows * rowHeightPx) + visibleRows * spacingPx + spacingPx;

                if (runtime.DropdownLayoutElement != null) runtime.DropdownLayoutElement.preferredHeight = targetHeight;
                else DebugLog.Trace(_log, "DropDownLayoutElement was null, cant expand row.");

                if (runtime.RowLayoutElement != null) runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight + targetHeight;
                else DebugLog.Trace(_log, "RowLayoutElement was null, cant expand row.");

                if (_sharedDropdown != null) _sharedDropdown.OpenFor(runtime);
            }

            runtime.IsExpanded = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(runtime.RowTransform);
            PixelSnap(runtime.RowTransform);
            PixelSnap(runtime.RowTop);
            if (runtime.ArrowText != null) runtime.ArrowText.text = "v";
        }

        internal static void CollapseRow(RecipeRowRuntime runtime)
        {
            if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == runtime)
            {
                if (_selectionReticle != null && _selectionReticle.IsChildOf(_sharedDropdown.transform))
                {
                    _selectionReticle.SetParent(_cookbookRoot.transform, false);
                    _selectionReticle.gameObject.SetActive(false);
                }

                _sharedDropdown.gameObject.SetActive(false);
            }

            if (_selectedPathUI != null && _selectedPathUI.OwnerRow == runtime) DeselectCurrentPath();

            if (runtime.DropdownLayoutElement != null) runtime.DropdownLayoutElement.preferredHeight = 0f;
            else DebugLog.Trace(_log, "DropDownLayoutElement was null, cant retract row.");

            if (runtime.RowLayoutElement != null) runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight;
            else DebugLog.Trace(_log, "RowLayoutElement was null, cant retract row.");

            runtime.IsExpanded = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(runtime.RowTransform);
            PixelSnap(runtime.RowTransform);
            PixelSnap(runtime.RowTop);
            if (runtime.ArrowText != null) runtime.ArrowText.text = ">";
        }

        // Path Selection Logic
        internal static void DeselectCurrentPath()
        {
            if (_selectedPathUI != null)
            {
                _selectedPathUI.SetSelected(false);
                _selectedPathUI = null;
            }

            _selectedChainData = null;

            if (_globalCraftButton)
            {
                _globalCraftButton.interactable = false;
                _globalCraftButtonImage.color = new Color32(26, 22, 22, 100);
                _globalCraftButtonText.text = "Select a Recipe";
                _globalCraftButtonText.color = new Color32(100, 100, 100, 255);
            }

            if (_repeatInputField)
            {
                _repeatInputField.text = string.Empty;
                if (_repeatInputField.placeholder is TextMeshProUGUI ph) ph.text = "";
            }
        }


        // Filtering logic
        public static void UpdateCycleButtonVisuals(TextMeshProUGUI label)
        {
            if (!label) return;

            label.spriteAsset = RecipeFilter.CurrentCategory switch
            {
                RecipeFilter.RecipeFilterCategory.Damage => RegisterAssets.CombatIconAsset,
                RecipeFilter.RecipeFilterCategory.Healing => RegisterAssets.HealingIconAsset,
                RecipeFilter.RecipeFilterCategory.Utility => RegisterAssets.UtilityIconAsset,
                _ => null
            };

            label.color = RecipeFilter.CurrentCategory switch
            {
                RecipeFilter.RecipeFilterCategory.Damage => new Color32(255, 75, 50, 255),
                RecipeFilter.RecipeFilterCategory.Healing => new Color32(119, 255, 117, 255),
                RecipeFilter.RecipeFilterCategory.Utility => new Color32(172, 104, 248, 255),
                _ => Color.white
            };

            label.text = RecipeFilter.GetLabel();
            label.ForceMeshUpdate();
        }

        internal static void RefreshUIVisibility()
        {
            RecipeFilter.ApplyFiltersToUI(_recipeRowUIs, _searchInputField?.text);

            if (_currentController != null)
            {
                if (_onIngredientsChangedMethod == null)
                {
                    _onIngredientsChangedMethod = typeof(CraftingController).GetMethod("OnIngredientsChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                _onIngredientsChangedMethod?.Invoke(_currentController, null);
            }
        }
    }
}
