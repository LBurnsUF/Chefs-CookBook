using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {
        // per frame build time so opening stays smooth
        private const double BuildBudgetMs = 4.0;

        internal static void PopulateRecipeList(IReadOnlyList<CraftableEntry> craftables)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_skeletonBuilt || _recipeListContent == null || _runner == null) return;
            if (_activeBuildRoutine != null) _runner.StopCoroutine(_activeBuildRoutine);
            if (_cookbookRoot.activeInHierarchy) _activeBuildRoutine = _runner.StartCoroutine(PopulateRoutine(craftables, sw));
        }

        internal static IEnumerator PopulateRoutine(IReadOnlyList<CraftableEntry> craftables, System.Diagnostics.Stopwatch sw)
        {
            var scrollRect = _recipeListContent.GetComponentInParent<ScrollRect>();

            bool hasItems = _recipeListContent.childCount > 0;
            float previousScrollPos = (scrollRect != null && hasItems) ? scrollRect.verticalNormalizedPosition : 1f;

            if (_selectionReticle != null)
            {
                _selectionReticle.SetParent(_cookbookRoot.transform, false);
                _selectionReticle.gameObject.SetActive(false);
            }

            // half built paths belong to rows about to change
            if (_activeDropdownRoutine != null)
            {
                _runner.StopCoroutine(_activeDropdownRoutine);
                _activeDropdownRoutine = null;
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
            // collapsing the open row below clears the selection
            RecipeChain chainToRestoreSelection = _selectedChainData;

            if (_openRow != null)
            {
                previousEntry = _openRow.Entry;
                CollapseRow(_openRow);
            }

            if (_currentHoveredPath != null) chainToRestoreHover = _currentHoveredPath.Chain;

            _openRow = null;
            _selectedAnchor = null;
            _currentHoveredPath = null;

            _recipeRowUIs.Clear();

            // let the caller store the routine handle first
            yield return null;

            // ---------------- Rebind rows ----------------
            // reuse rows in place, only build what is missing
            int used = 0;
            RecipeRowRuntime rowToRestore = null;
            var frame = System.Diagnostics.Stopwatch.StartNew();

            if (craftables != null)
            {
                foreach (var entry in craftables)
                {
                    if (entry == null) continue;

                    var runtime = used < _recipeListContent.childCount
                        ? _recipeListContent.GetChild(used).GetComponent<RecipeRowRuntime>()
                        : CreateRecipeRow(_recipeListContent);
                    used++;
                    if (runtime == null) continue;

                    BindRecipeRow(runtime, entry);
                    _recipeRowUIs.Add(new RecipeRowUI { Entry = entry, RowGO = runtime.gameObject });

                    if (previousEntry != null && AreEntriesSame(previousEntry, entry))
                    {
                        rowToRestore = runtime;
                    }

                    if (frame.Elapsed.TotalMilliseconds > BuildBudgetMs)
                    {
                        yield return null;
                        frame.Restart();
                    }
                }
            }

            // leftovers hidden first so layout skips them this frame
            for (int i = _recipeListContent.childCount - 1; i >= used; i--)
            {
                var child = _recipeListContent.GetChild(i).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }

            // reused rows keep old filter state
            RecipeFilter.ApplyFiltersToUI(_recipeRowUIs, _searchInputField ? _searchInputField.text : null);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_recipeListContent);
            PixelSnap(_recipeListContent);

            // closed mid build, stay collapsed
            if (rowToRestore != null && _currentController != null)
            {
                ToggleRecipeRow(rowToRestore);

                // paths build over frames
                while (_activeDropdownRoutine != null)
                {
                    yield return null;
                }

                if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == rowToRestore)
                {
                    bool lookingForSelection = chainToRestoreSelection != null;
                    bool lookingForHover = chainToRestoreHover != null;
                    foreach (Transform child in _sharedDropdown.View.Content)
                    {
                        if (!lookingForSelection && !lookingForHover) break;
                        if (!child.gameObject.activeSelf) continue;
                        var pathRuntime = child.GetComponent<PathRowRuntime>();
                        if (pathRuntime == null) continue;

                        if (lookingForSelection && AreChainsSame(pathRuntime.Chain, chainToRestoreSelection))
                        {
                            OnPathSelected(pathRuntime);
                            lookingForSelection = false;
                        }

                        if (lookingForHover && AreChainsSame(pathRuntime.Chain, chainToRestoreHover))
                        {
                            _currentHoveredPath = pathRuntime;
                            AttachReticleTo(pathRuntime.View.VisualRect);
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

            _activeBuildRoutine = null;
            RebuildNavigation();

            sw.Stop();
            _log.LogInfo($"CraftUI: PopulateRecipeList completed in {sw.ElapsedMilliseconds}ms, {used} rows");
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
            // hidden first so layout skips them this frame, rebound below, leftovers stay hidden
            for (int i = contentRoot.childCount - 1; i >= 0; i--) contentRoot.GetChild(i).gameObject.SetActive(false);

            // let the caller store the routine handle first
            yield return null;

            if (owner.Entry == null || owner.Entry.Chains == null)
            {
                _activeDropdownRoutine = null;
                yield break;
            }

            var frame = System.Diagnostics.Stopwatch.StartNew();
            int used = 0;

            foreach (var chain in owner.Entry.Chains)
            {
                BindPathRow(contentRoot, used++, chain, owner);

                if (frame.Elapsed.TotalMilliseconds > BuildBudgetMs)
                {
                    yield return null;
                    frame.Restart();
                }
            }

            _cachedDropdownOwner = owner;
            _activeDropdownRoutine = null;
            RebuildNavigation();
        }
    }
}
