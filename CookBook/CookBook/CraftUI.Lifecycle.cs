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
        //==================== LifeCycle ====================
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

            try
            {
                if (_cookbookRoot != null && (bool)_cookbookRoot)
                    Detach();
            }
            catch { }

            _activeBuildRoutine = null;
            _activeDropdownRoutine = null;
            _cachedDropdownOwner = null;
            _sharedDropdown = null;

            _searchInputField = null;
            _repeatInputField = null;
            _globalCraftButton = null;
            _globalCraftButtonText = null;
            _globalCraftButtonImage = null;

            _selectionReticle = null;
            _currentReticleTarget = null;
            _selectedAnchor = null;
            _currentHoveredPath = null;
            _selectedPathUI = null;
            _selectedChainData = null;
            _openRow = null;

            _currentController = null;
            _skeletonBuilt = false;
        }

        internal static void Attach(CraftingController controller)
        {
            _currentController = controller;
            if (_cookbookRoot != null) return;
            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();

            if (!craftingPanel) return;

            // hierarchy pieces
            Transform bgContainerTr = craftingPanel.transform.Find("MainPanel/Juice/BGContainer");
            RectTransform bgRect = bgContainerTr.GetComponent<RectTransform>(); // contains bgmain
            RectTransform bgMainRect = bgContainerTr ? bgContainerTr.Find("BGMain")?.GetComponent<RectTransform>() : null;
            RectTransform labelRect = craftingPanel.transform.Find("MainPanel/Juice/LabelContainer")?.GetComponent<RectTransform>();
            RectTransform craftBgRect = bgContainerTr.Find("CraftingContainer/Background")?.GetComponent<RectTransform>();
            RectTransform craftRect = bgContainerTr.Find("CraftingContainer")?.GetComponent<RectTransform>();
            RectTransform submenuRect = bgContainerTr.Find("SubmenuContainer")?.GetComponent<RectTransform>();
            RectTransform invRect = bgContainerTr.Find("InventoryContainer")?.GetComponent<RectTransform>();

            if (!labelRect) return;

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

            // create CookBook panel in the new right-hand strip
            _cookbookRoot = CreateUIObject("CookBookPanel", typeof(RectTransform), typeof(CraftUIRunner), typeof(Canvas), typeof(GraphicRaycaster));

            _runner = _cookbookRoot.GetComponent<CraftUIRunner>();
            var canvas = _cookbookRoot.GetComponent<Canvas>();
            canvas.pixelPerfect = true;
            _cookbookRoot.GetComponent<GraphicRaycaster>();
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();

            _cookbookRoot.transform.SetParent(bgContainerTr, false);
            cbRT.anchorMin = new Vector2(1f, 0.5f);
            cbRT.anchorMax = new Vector2(1f, 0.5f);
            cbRT.pivot = new Vector2(1f, 0.5f);

            /// ensure equal margins, same y position
            cbRT.sizeDelta = new Vector2(cookbookWidth, boundsHeight);
            cbRT.anchoredPosition = new Vector2(-sideMargin, boundsCenterY);

            labelRect.GetComponent<Image>().enabled = false;
            AddBorder(labelRect, new Color32(209, 209, 210, 255), 2f, 2f, 6f, 6f);

            DebugLog.Trace(_log, $"CraftUI.Attach: CookBook panel attached. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}");
            _panelWidth = cbRT.rect.width;
            _panelHeight = cbRT.rect.height;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            AcquireTemplates(craftingPanel, cbRT);
            sw.Stop();
            _log.LogInfo($"CraftUI: Skeleton & Templates built in {sw.ElapsedMilliseconds}ms");

            if (LastCraftables != null && LastCraftables.Count > 0) PopulateRecipeList(LastCraftables);
        }

        internal static void Detach()
        {
            if (_cookbookRoot != null)
            {
                UnityEngine.Object.Destroy(_cookbookRoot);
                _cookbookRoot = null;
            }

            if (_globalCraftButton != null) _globalCraftButton.onClick.RemoveAllListeners();
            if (_searchInputField != null) _searchInputField.onValueChanged.RemoveAllListeners();
            if (_repeatInputField != null) _repeatInputField.onEndEdit.RemoveAllListeners();

            _recipeRowUIs.Clear();
            _iconCache.Clear();
            _droneIconCache.Clear();

            _currentController = null;
            _skeletonBuilt = false;
            _recipeListContent = null;
            _openRow = null;
            _activeBuildRoutine = null;
            _activeDropdownRoutine = null;
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
