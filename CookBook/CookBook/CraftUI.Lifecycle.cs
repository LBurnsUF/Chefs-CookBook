using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static void Init(ManualLogSource log)
        {
            _log = log;
            StateController.OnCraftablesForUIChanged += CraftablesForUIChanged;
        }

        internal static void Shutdown()
        {
            StateController.OnCraftablesForUIChanged -= CraftablesForUIChanged;

            if (_searchInputField != null)
            {
                _searchInputField.onValueChanged.RemoveAllListeners();
                _searchInputField.onEndEdit.RemoveAllListeners();
            }

            if (_repeatInputField != null)
            {
                _repeatInputField.onEndEdit.RemoveAllListeners();
            }

            if (_globalCraftButton != null)
            {
                _globalCraftButton.onClick.RemoveAllListeners();
            }

            if (_runner != null && (bool)_runner)
            {
                if (_activeBuildRoutine != null) _runner.StopCoroutine(_activeBuildRoutine);
                if (_activeDropdownRoutine != null) _runner.StopCoroutine(_activeDropdownRoutine);
            }

            try { DestroyPanel(); }
            catch { }

            if (_panelHolder) UnityEngine.Object.Destroy(_panelHolder);
            _panelHolder = null;

            _activeBuildRoutine = null;
            _activeDropdownRoutine = null;
            _cachedDropdownOwner = null;
            _sharedDropdown = null;

            _searchInputField = null;
            _repeatInputField = null;
            _globalCraftButton = null;
            _globalCraftButtonText = null;
            _globalCraftButtonImage = null;
            ClearButtonRefs();

            _selectionReticle = null;
            _reticleClip = null;
            _currentReticleTarget = null;
            _selectedAnchor = null;
            _currentHoveredPath = null;
            _selectedPathUI = null;
            _selectedChainData = null;
            _openRow = null;

            _currentController = null;
            _skeletonBuilt = false;
        }

        //==================== Attach ====================
        internal static void Attach(CraftingController controller)
        {
            _currentController = controller;
            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();
            if (!craftingPanel) return;

            Transform bgContainerTr = craftingPanel.transform.Find("MainPanel/Juice/BGContainer");
            if (!bgContainerTr) return;

            // open fires twice
            if (_cookbookRoot != null && _cookbookRoot.transform.parent == bgContainerTr) return;

            // no bundle, no panel, vanilla stays as is
            if (!UIBundle.EnsureLoaded()) return;

            if (!LayoutVanillaPanel(craftingPanel, bgContainerTr, out Vector2 size, out Vector2 position)) return;

            // sizes are baked in, rebuild on resolution change
            if (_cookbookRoot == null || size != _builtSize)
            {
                DestroyPanel();
                BuildPanel(craftingPanel, bgContainerTr, size, position);
                if (LastCraftables != null && LastCraftables.Count > 0) PopulateRecipeList(LastCraftables);
            }
            else MountPanel(craftingPanel, bgContainerTr, position);
        }

        // widens the vanilla panel and returns the cookbook strip
        private static bool LayoutVanillaPanel(CraftingPanel craftingPanel, Transform bgContainerTr, out Vector2 size, out Vector2 position)
        {
            size = Vector2.zero;
            position = Vector2.zero;

            // hierarchy pieces
            RectTransform bgRect = bgContainerTr.GetComponent<RectTransform>(); // contains bgmain
            RectTransform bgMainRect = bgContainerTr ? bgContainerTr.Find("BGMain")?.GetComponent<RectTransform>() : null;
            RectTransform labelRect = craftingPanel.transform.Find("MainPanel/Juice/LabelContainer")?.GetComponent<RectTransform>();
            RectTransform craftBgRect = bgContainerTr.Find("CraftingContainer/Background")?.GetComponent<RectTransform>();
            RectTransform craftRect = bgContainerTr.Find("CraftingContainer")?.GetComponent<RectTransform>();
            RectTransform submenuRect = bgContainerTr.Find("SubmenuContainer")?.GetComponent<RectTransform>();
            RectTransform invRect = bgContainerTr.Find("InventoryContainer")?.GetComponent<RectTransform>();

            if (!labelRect) return false;

            labelRect.SetParent(bgMainRect, worldPositionStays: true);

            float invBaseWidth = RoundToEven(invRect ? invRect.rect.width : 0f);
            float invBaseHeight = RoundToEven(invRect ? invRect.rect.height : 0f);
            float craftBaseWidth = RoundToEven(craftBgRect ? craftBgRect.rect.width : 0);
            float baseWidth = RoundToEven(bgMainRect.rect.width);
            float baseHeight = RoundToEven(bgMainRect.rect.height);
            float baseLabelWidth = RoundToEven(labelRect.rect.width);

            //==================== base UI scaling ====================
            var img = bgMainRect.GetComponent<Image>();
            var sprite = img ? img.sprite : null;
            float ppu = RoundToEven(sprite ? sprite.pixelsPerUnit : 1f);
            float padLeft = RoundToEven(sprite ? sprite.border.x / ppu : 0f);
            float padRight = RoundToEven(sprite ? sprite.border.z / ppu : 0f);
            float padTop = RoundToEven(sprite ? sprite.border.w / ppu : 0f);
            float padBottom = RoundToEven(sprite ? sprite.border.y / ppu : 0f);
            float padHorizontal = padLeft + padRight;

            float widthscale = 1.8f;
            float newBgWidth = RoundToEven(baseWidth * widthscale);

            float innerWidth = baseWidth - padHorizontal;
            float labelGap = RoundToEven((innerWidth - baseLabelWidth) * 0.5f);
            float newInnerWidth = newBgWidth - padHorizontal;
            float newLabelWidth = newInnerWidth - 2f * labelGap;

            float invWidthNew = RoundToEven(invBaseWidth * 0.88f);
            float invHeightNew = RoundToEven(invBaseHeight * 0.9f);
            float craftWidthNew = invWidthNew;

            float cookbookWidth = RoundToEven(Mathf.Clamp(newBgWidth * 0.3f, 260f, newInnerWidth - invBaseWidth));

            float gap = RoundToEven(Mathf.Clamp(newBgWidth * 0.05f, 20f, labelGap));

            bgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newLabelWidth);

            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, invWidthNew);
            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, invHeightNew);
            craftBgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, craftWidthNew);

            AlignLabelVerticallyBetween(labelRect, bgMainRect, craftBgRect);

            float craftWidth = RoundToEven(craftBgRect.rect.width);
            float sideMargin = RoundToEven((newInnerWidth - (craftWidth + gap + cookbookWidth)) * 0.5f);
            float centerCraftPanel = RoundToEven(-newInnerWidth * 0.5f + sideMargin + craftWidth * 0.5f);

            var pos = craftBgRect.anchoredPosition;
            pos.x = centerCraftPanel;
            craftBgRect.anchoredPosition = pos;

            pos = invRect.anchoredPosition;
            pos.x = centerCraftPanel;
            invRect.anchoredPosition = pos;

            pos = submenuRect.anchoredPosition;
            pos.x = centerCraftPanel;
            submenuRect.anchoredPosition = pos;

            // --- compute combined vertical bounds of craft + inventory in BGContainer space ---
            Bounds contentBounds = default;
            bool hasBounds = false;

            if (craftBgRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, craftBgRect);
                contentBounds = b;
                hasBounds = true;
            }

            if (invRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, invRect);
                if (hasBounds) contentBounds.Encapsulate(b);
                else
                {
                    contentBounds = b;
                    hasBounds = true;
                }
            }

            float boundsHeight = RoundToEven(contentBounds.size.y);
            float boundsCenterY = RoundToEven(contentBounds.center.y);

            /// ensure equal margins, same y position
            size = new Vector2(cookbookWidth, boundsHeight);
            position = new Vector2(-sideMargin, boundsCenterY);

            labelRect.GetComponent<Image>().enabled = false;
            AddBorder(labelRect, new Color32(209, 209, 210, 255), 2f, 2f, 6f, 6f);

            DebugLog.Trace(_log, $"CraftUI.Attach: vanilla panel laid out. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}");
            return true;
        }

        //==================== Panel ====================
        // built once, kept across opens
        private static void BuildPanel(CraftingPanel craftingPanel, Transform bgContainerTr, Vector2 size, Vector2 position)
        {
            if (!_panelHolder)
            {
                _panelHolder = new GameObject("CookBookHolder");
                UnityEngine.Object.DontDestroyOnLoad(_panelHolder);
            }

            // own provider so children keep an event system between vanilla panels
            _cookbookRoot = CreateUIObject("CookBookPanel", typeof(RectTransform), typeof(MPEventSystemProvider), typeof(CraftUIRunner), typeof(Canvas), typeof(GraphicRaycaster));

            _eventSystemProvider = _cookbookRoot.GetComponent<MPEventSystemProvider>();
            _runner = _cookbookRoot.GetComponent<CraftUIRunner>();
            _cookbookRoot.GetComponent<Canvas>().pixelPerfect = true;

            // layout runs at the authored size, scale fits it to this panel
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            float fit = size.y / UIBundleInfo.PanelHeight;
            cbRT.sizeDelta = new Vector2(size.x / fit, UIBundleInfo.PanelHeight);
            cbRT.localScale = new Vector3(fit, fit, 1f);
            _builtSize = size;

            // build mounted so canvas scale matches
            MountPanel(craftingPanel, bgContainerTr, position);

            _panelHeight = cbRT.rect.height;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            AcquireTemplates(craftingPanel, cbRT);
            sw.Stop();
            _log.LogInfo($"CraftUI: Skeleton & Templates built in {sw.ElapsedMilliseconds}ms");
        }

        // puts the built panel on a fresh vanilla panel
        private static void MountPanel(CraftingPanel craftingPanel, Transform bgContainerTr, Vector2 position)
        {
            _craftingPanel = craftingPanel;
            _vanillaTopLayer = craftingPanel.confirmButton ? craftingPanel.confirmButton.requiredTopLayer : null;

            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            cbRT.SetParent(bgContainerTr, false);
            cbRT.anchorMin = new Vector2(1f, 0.5f);
            cbRT.anchorMax = new Vector2(1f, 0.5f);
            cbRT.pivot = new Vector2(1f, 0.5f);
            cbRT.anchoredPosition = position;

            var vanillaProvider = bgContainerTr.GetComponentInParent<MPEventSystemProvider>();
            _eventSystemProvider.eventSystem = vanillaProvider ? vanillaProvider.resolvedEventSystem : null;

            BindTopLayer(_vanillaTopLayer, true);
            _cookbookRoot.GetComponent<Canvas>().enabled = true;
        }

        // parks the panel instead of destroying it
        internal static void Detach()
        {
            _currentController = null;

            if (_cookbookRoot == null || !_panelHolder) return;
            if (_cookbookRoot.transform.parent == _panelHolder.transform) return;

            ResetPanelState();

            _cookbookRoot.transform.SetParent(_panelHolder.transform, false);
            _cookbookRoot.GetComponent<Canvas>().enabled = false;
            _eventSystemProvider.eventSystem = null;

            // gamepad bindings would fire from the holder otherwise
            BindTopLayer(null, false);
            _craftingPanel = null;
            _vanillaTopLayer = null;
            _vanillaButtonTemplate = null;
        }

        // next open looks like a new panel
        private static void ResetPanelState()
        {
            if (_activeDropdownRoutine != null && _runner != null)
            {
                _runner.StopCoroutine(_activeDropdownRoutine);
                _activeDropdownRoutine = null;
            }

            if (_openRow != null)
            {
                CollapseRow(_openRow);
                _openRow = null;
            }

            if (_sharedDropdown != null) _sharedDropdown.Close();
            _cachedDropdownOwner = null;

            DeselectCurrentPath();
            if (_selectionReticle != null) _selectionReticle.gameObject.SetActive(false);

            if (_searchInputField != null) _searchInputField.text = string.Empty;

            var scroll = _recipeListContent ? _recipeListContent.GetComponentInParent<ScrollRect>() : null;
            if (scroll != null) scroll.verticalNormalizedPosition = 1f;
        }

        // drops the built panel, next attach builds fresh
        internal static void DestroyPanel()
        {
            if (_runner != null)
            {
                if (_activeBuildRoutine != null) _runner.StopCoroutine(_activeBuildRoutine);
                if (_activeDropdownRoutine != null) _runner.StopCoroutine(_activeDropdownRoutine);
            }

            if (_globalCraftButton != null) _globalCraftButton.onClick.RemoveAllListeners();
            if (_searchInputField != null) _searchInputField.onValueChanged.RemoveAllListeners();
            if (_repeatInputField != null) _repeatInputField.onEndEdit.RemoveAllListeners();

            if (_cookbookRoot != null) UnityEngine.Object.Destroy(_cookbookRoot);
            _cookbookRoot = null;
            _runner = null;
            _eventSystemProvider = null;

            // destroy is deferred, drop refs so a same frame rebuild makes new ones
            _recipeRowTemplate = null;
            _pathRowTemplate = null;
            _ingredientSlotTemplate = null;
            _droneSlotTemplate = null;
            _tradeSlotTemplate = null;
            _ResultSlotTemplate = null;
            _sharedDropdown = null;
            _selectionReticle = null;
            _reticleClip = null;
            _currentReticleTarget = null;
            _selectedAnchor = null;
            _currentHoveredPath = null;
            _selectedPathUI = null;
            _cachedDropdownOwner = null;
            _globalCraftButton = null;
            _globalCraftButtonText = null;
            _globalCraftButtonImage = null;
            _searchInputField = null;
            _repeatInputField = null;

            _recipeRowUIs.Clear();
            _iconCache.Clear();
            _droneIconCache.Clear();
            ClearButtonRefs();

            _skeletonBuilt = false;
            _recipeListContent = null;
            _openRow = null;
            _activeBuildRoutine = null;
            _activeDropdownRoutine = null;
        }

        // vanilla layer and gamepad bindings belong to each vanilla panel
        private static void BindTopLayer(UILayerKey layer, bool gamepadActive)
        {
            foreach (var btn in _cookbookRoot.GetComponentsInChildren<MPButton>(true)) btn.requiredTopLayer = layer;
            foreach (var ev in _cookbookRoot.GetComponentsInChildren<HGGamepadInputEvent>(true))
            {
                ev.requiredTopLayer = layer;
                ev.enabled = gamepadActive;
            }
        }

        internal static void ClearButtonRefs()
        {
            _repeatMinusButton = null;
            _repeatPlusButton = null;
            _categoryButton = null;
            _globalCraftButtonIsCanon = false;
            _craftingPanel = null;
            _vanillaButtonTemplate = null;
            _vanillaTopLayer = null;
            _navOrder.Clear();
        }

        internal static void CloseCraftPanel(CraftingController specificController = null)
        {
            var target = specificController ? specificController : _currentController;
            if (!target) return;

            StateController.TryReleasePromptParticipant(target);

            if (_runner != null && _runner.isActiveAndEnabled)
                _runner.StartCoroutine(DestroyPanelNextFrame(target));
            else
                DestroyPanelForController(target);
        }

        internal static IEnumerator DestroyPanelNextFrame(CraftingController target)
        {
            yield return null;
            DestroyPanelForController(target);
        }

        internal static void DestroyPanelForController(CraftingController target)
        {
            if (!target) return;

            var openPanels = UnityEngine.Object.FindObjectsOfType<CraftingPanel>();
            for (int i = 0; i < openPanels.Length; i++)
            {
                var panel = openPanels[i];
                if (panel && panel.craftingController == target)
                {
                    // park ours before the vanilla panel takes it down
                    if (_cookbookRoot != null && _cookbookRoot.transform.IsChildOf(panel.transform)) Detach();

                    var ev = panel.eventFunctions ? panel.eventFunctions : panel.GetComponent<EventFunctions>();
                    if (ev != null) ev.DestroySelf();
                    else UnityEngine.Object.Destroy(panel.gameObject);
                    break;
                }
            }

            if (StateController.ActiveCraftingController == target)
                StateController.ActiveCraftingController = null;

            if (_currentController == target)
                _currentController = null;
        }

        internal static void SetSnapshot(InventorySnapshot snap)
        {
            _snap = snap;
            _hasSnap = true;
        }

        internal static bool TryGetSnapshot(out InventorySnapshot snap)
        {
            snap = _snap;
            return _hasSnap;
        }
    }
}
