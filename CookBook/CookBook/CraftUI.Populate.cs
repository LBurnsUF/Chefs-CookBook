using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static void PopulateRecipeList(IReadOnlyList<CraftableEntry> craftables)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_skeletonBuilt || _recipeListContent == null || _runner == null) return;
            if (_activeBuildRoutine != null) _runner.StopCoroutine(_activeBuildRoutine);
            if (_cookbookRoot.activeInHierarchy) _activeBuildRoutine = _runner.StartCoroutine(PopulateRoutine(craftables, sw));
        }

        internal static IEnumerator PopulateRoutine(IReadOnlyList<CraftableEntry> craftables, System.Diagnostics.Stopwatch sw)
        {
            var vlg = _recipeListContent.GetComponent<VerticalLayoutGroup>();
            var canvasGroup = _recipeListContent.GetComponent<CanvasGroup>();
            var scrollRect = _recipeListContent.GetComponentInParent<ScrollRect>();

            if (canvasGroup)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }
            if (vlg) vlg.enabled = false;

            bool hasItems = _recipeListContent.childCount > 0;
            float previousScrollPos = (scrollRect != null && hasItems) ? scrollRect.verticalNormalizedPosition : 1f;

            if (_selectionReticle != null)
            {
                _selectionReticle.SetParent(_cookbookRoot.transform, false);
                _selectionReticle.gameObject.SetActive(false);
            }

            if (_sharedDropdown != null)
            {
                _sharedDropdown.transform.SetParent(_cookbookRoot.transform, false);
                _sharedDropdown.gameObject.SetActive(false);
                _cachedDropdownOwner = null;
                _sharedDropdown.CurrentOwner = null;
            }

            CraftableEntry previousEntry = null;
            RecipeChain chainToRestoreHover = null;

            if (_openRow != null)
            {
                previousEntry = _openRow.Entry;
                CollapseRow(_openRow);
            }

            if (_currentHoveredPath != null) chainToRestoreHover = _currentHoveredPath.Chain;

            _openRow = null;
            _selectedAnchor = null;
            _currentHoveredPath = null;

            foreach (Transform child in _recipeListContent) UnityEngine.Object.Destroy(child.gameObject);
            _recipeRowUIs.Clear();

            yield return null;

            // Rebuild
            if (craftables == null || craftables.Count == 0)
            {
                if (vlg) vlg.enabled = true;
                if (canvasGroup)
                {
                    canvasGroup.alpha = 1f;
                    canvasGroup.blocksRaycasts = true;
                }

                _activeBuildRoutine = null;
                sw.Stop();
                _log.LogInfo($"CraftUI: PopulateRecipeList completed in {sw.ElapsedMilliseconds}ms");
                yield break;
            }

            int builtCount = 0;
            RecipeRowRuntime rowToRestore = null;

            foreach (var entry in craftables)
            {
                if (entry == null) continue;

                var rowGO = CreateRecipeRow(_recipeListContent, entry);
                var runtime = rowGO.GetComponent<RecipeRowRuntime>();

                _recipeRowUIs.Add(new RecipeRowUI { Entry = entry, RowGO = rowGO });

                if (previousEntry != null && AreEntriesSame(previousEntry, entry))
                {
                    rowToRestore = runtime;
                }

                builtCount++;
                if (builtCount % 5 == 0) yield return null;
            }

            if (vlg)
            {
                vlg.enabled = true;
                LayoutRebuilder.ForceRebuildLayoutImmediate(_recipeListContent);
                PixelSnap(_recipeListContent);
            }

            while (_activeDropdownRoutine != null)
            {
                yield return null;
            }

            if (rowToRestore != null)
            {
                ToggleRecipeRow(rowToRestore);

                if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == rowToRestore)
                {
                    bool lookingForSelection = _selectedChainData != null;
                    bool lookingForHover = chainToRestoreHover != null;
                    foreach (Transform child in _sharedDropdown.Content)
                    {
                        if (!lookingForSelection && !lookingForHover) break;
                        var pathRuntime = child.GetComponent<PathRowRuntime>();
                        if (pathRuntime == null) continue;

                        if (lookingForSelection && pathRuntime.Chain == _selectedChainData)
                        {
                            OnPathSelected(pathRuntime);
                            lookingForSelection = false;
                        }

                        if (lookingForHover && pathRuntime.Chain == chainToRestoreHover)
                        {
                            _currentHoveredPath = pathRuntime;
                            AttachReticleTo(pathRuntime.VisualRect);
                            lookingForHover = false;
                        }
                    }
                }
            }
            else DeselectCurrentPath();

            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = previousScrollPos;
            }

            if (canvasGroup)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.blocksRaycasts = true;
            }

            _activeBuildRoutine = null;

            sw.Stop();
            _log.LogInfo($"CraftUI: PopulateRecipeList (Empty) completed in {sw.ElapsedMilliseconds}ms");
        }

        internal static void PopulateDropdown(RectTransform contentRoot, RecipeRowRuntime owner)
        {
            if (_runner == null || !_cookbookRoot.activeInHierarchy) return;

            bool isSameOwner = _cachedDropdownOwner == owner;
            bool hasContent = contentRoot.childCount > 0;

            if (isSameOwner && hasContent) return;
            if (_activeDropdownRoutine != null) _runner.StopCoroutine(_activeDropdownRoutine);

            _cachedDropdownOwner = null;
            _activeDropdownRoutine = _runner.StartCoroutine(PopulateDropdownRoutine(contentRoot, owner));
        }

        internal static IEnumerator PopulateDropdownRoutine(RectTransform contentRoot, RecipeRowRuntime owner)
        {
            foreach (Transform child in contentRoot)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }

            yield return null;

            if (owner.Entry == null || owner.Entry.Chains == null)
            {
                _activeDropdownRoutine = null;
                yield break;
            }

            int builtCount = 0;

            foreach (var chain in owner.Entry.Chains)
            {
                CreatePathRow(contentRoot, chain, owner);

                builtCount++;

                if (builtCount % 4 == 0) yield return null;
            }

            _cachedDropdownOwner = owner;
            _activeDropdownRoutine = null;
        }
    }
}
