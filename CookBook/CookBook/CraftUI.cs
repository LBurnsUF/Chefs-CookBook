using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

// TODO: set up alternate hover borders to mirror vanilla RoR2 aesthetic
namespace CookBook
{
    internal static class CraftUI
    {
        private static ManualLogSource _log;
        internal static IReadOnlyList<CraftableEntry> LastCraftables { get; private set; }
        private static bool _skeletonBuilt = false;
        private static CraftingController _currentController;

        private static GameObject _cookbookRoot;
        private static RectTransform _recipeListContent;
        private static TMP_InputField _searchInputField;

        private static GameObject _recipeRowTemplate;
        private static GameObject _pathRowTemplate;
        private static GameObject _ingredientSlotTemplate;
        private static GameObject _droneSlotTemplate;
        private static GameObject _tradeSlotTemplate;
        private static GameObject _ResultSlotTemplate;

        private static RecipeRowRuntime _openRow;
        private static CraftUIRunner _runner;

        private static Coroutine _activeBuildRoutine;
        private static Coroutine _activeDropdownRoutine;
        private static RecipeDropdownRuntime _sharedDropdown;
        private static RecipeRowRuntime _cachedDropdownOwner;

        private static RectTransform _selectionReticle;
        private static RectTransform _currentReticleTarget;
        private static RectTransform _selectedAnchor;
        private static PathRowRuntime _currentHoveredPath;

        private static Button _globalCraftButton;
        private static TextMeshProUGUI _globalCraftButtonText;
        private static Image _globalCraftButtonImage;
        private static PathRowRuntime _selectedPathUI;
        private static RecipeChain _selectedChainData;

        private static Sprite _solidPointSprite;
        private static Sprite _taperedGradientSprite;

        private static readonly Dictionary<int, Sprite> _iconCache = new();
        private static readonly Dictionary<DroneIndex, Sprite> _droneIconCache = new();
        private static readonly List<RecipeRowUI> _recipeRowUIs = new();

        private static System.Reflection.MethodInfo _onIngredientsChangedMethod;

        // ---------------- Layout constants ----------------
        private static float _panelWidth;
        private static float _panelHeight;

        // CookBookPanel
        internal const float CookBookPanelPaddingTopNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingBottomNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingLeftNorm = 0f;
        internal const float CookBookPanelPaddingRightNorm = 0f;
        internal const float CookBookPanelElementSpacingNorm = 0.0159744409f; // vertical gap between SearchBar and RecipeList

        // SearchBar container
        internal const float SearchBarContainerNorm = 0.0798722045f;
        internal const float SearchBarWidthNorm = 0.8f;
        internal const float FilterDropDownWidthNorm = 0.2f;
        internal const float SearchBarBottomBorderThicknessNorm = 0.0001f;

        // RecipeList internal layout
        internal const float RecipeListVerticalPaddingNorm = 0f;
        internal const float RecipeListLeftPaddingNorm = 0.0181818182f;
        internal const float RecipeListRightPaddingNorm = 0.0181818182f;
        internal const float RecipeListElementSpacingNorm = 0f;
        internal const float RecipeListScrollbarWidthNorm = 0f;

        // RecipeRow/RowTop
        internal const float RowTopHeightNorm = 0.111821086f;
        internal const float RowTopTopPaddingNorm = 0.00798722045f;
        internal const float RowTopBottomPaddingNorm = 0.00798722045f;
        internal const float RowTopElementSpacingNorm = 0.0181818182f;
        internal const float MetaDataColumnWidthNorm = 0.254545455f;
        internal const float MetaDataElementSpacingNorm = 0.0159744409f;
        internal const float DropDownArrowSizeNorm = 0.0511182109f;
        internal const float textSizeNorm = 0.0383386581f;

        // ----- PathsContainer sizing -----
        private const float PathsContainerLeftPaddingNorm = 0.0181818182f;
        private const float PathsContainerRightPaddingNorm = 0.0181818182f;
        private const int PathsContainerMaxVisibleRows = 4;
        private const float PathsContainerSpacingNorm = 0.00798722045f;

        // ----- PathRow sizing -----
        private const float PathRowHeightNorm = 0.0798722045f;
        private const float PathRowLeftPaddingNorm = 0.00909090909f;
        private const float PathRowRightPaddingNorm = 0.00909090909f;
        private const float PathRowIngredientSpacingNorm = 0.00909090909f;

        //----- Ingredients -------
        private const float IngredientHeightNorm = 0.0670926518f;
        private const float _IngredientStackSizeTextHeightPx = 10f;
        private const float _IngredientStackMargin = 2f;
        private const float _ResultStackMargin = 3f;

        //----- Confirmation -------
        private const float FooterHeightNorm = 0.05f;

        //==================== LifeCycle ====================
        internal static void Init(ManualLogSource log)
        {
            _log = log;
            StateController.OnCraftablesForUIChanged += CraftablesForUIChanged;
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

            // Preserve current on-screen position
            labelRect.SetParent(bgMainRect, worldPositionStays: true);

            // base width references
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

            // Widen BG
            float widthscale = 1.8f;
            float newBgWidth = RoundToEven(baseWidth * widthscale);

            // ensure even label margins
            float innerWidth = baseWidth - padHorizontal;
            float labelGap = RoundToEven((innerWidth - baseLabelWidth) * 0.5f);
            float newInnerWidth = newBgWidth - padHorizontal;
            float newLabelWidth = newInnerWidth - 2f * labelGap;

            // shrink vanilla ui margins
            float invWidthNew = RoundToEven(invBaseWidth * 0.88f);
            float invHeightNew = RoundToEven(invBaseHeight * 0.9f);
            float craftWidthNew = invWidthNew;

            // clamp cookbook width to 30% of total chef UI
            float cookbookWidth = RoundToEven(Mathf.Clamp(newBgWidth * 0.3f, 260f, newInnerWidth - invBaseWidth));

            // clamp gap between cookbook and crafting panel to 5% of the total width
            float gap = RoundToEven(Mathf.Clamp(newBgWidth * 0.05f, 20f, labelGap));

            // extend background and label
            bgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newLabelWidth);

            // shrink vanilla ui margins
            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, invWidthNew);
            invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, invHeightNew);
            craftBgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, craftWidthNew);

            // recenter labelcontainer
            AlignLabelVerticallyBetween(labelRect, bgMainRect, craftBgRect);

            // position craft+inventory block using left sideMargin
            float craftWidth = RoundToEven(craftBgRect.rect.width);
            float sideMargin = RoundToEven((newInnerWidth - (craftWidth + gap + cookbookWidth)) * 0.5f);
            float centerCraftPanel = RoundToEven(-newInnerWidth * 0.5f + sideMargin + craftWidth * 0.5f);

            // shift vanilla UI left
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

            _log.LogDebug($"CraftUI.Attach: CookBook panel attached. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}");
            _panelWidth = cbRT.rect.width;
            _panelHeight = cbRT.rect.height;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            On.RoR2.CraftingController.FilterAvailableOptions += RecipeFilter.InterceptIngredientAvailability;
            CookBookSkeleton(cbRT);
            EnsureResultSlotArtTemplates(craftingPanel);
            EnsureIngredientSlotTemplate();
            BuildRecipeRowTemplate();
            BuildPathRowTemplate();
            BuildSharedDropdown();
            BuildSharedHoverRect();
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
            On.RoR2.CraftingController.FilterAvailableOptions -= RecipeFilter.InterceptIngredientAvailability;
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

            if (_runner != null && _activeBuildRoutine != null)
            {
                _runner.StopCoroutine(_activeBuildRoutine);
                _activeBuildRoutine = null;
            }

            var openPanels = UnityEngine.Object.FindObjectsOfType<CraftingPanel>();
            foreach (var panel in openPanels)
            {
                if (panel.craftingController == target)
                {
                    UnityEngine.Object.Destroy(panel.gameObject);
                    Detach();
                    return;
                }
            }
        }

        internal static void Shutdown()
        {
            StateController.OnCraftablesForUIChanged -= CraftablesForUIChanged;
            _searchInputField.onValueChanged.RemoveAllListeners();
        }

        //==================== Runtimes ====================

        internal sealed class RecipeRowRuntime : MonoBehaviour
        {
            public CraftPlanner.CraftableEntry Entry;

            public RectTransform RowTransform;
            public LayoutElement RowLayoutElement;
            public RectTransform RowTop;
            public Button RowTopButton;
            public TextMeshProUGUI ArrowText;

            public RectTransform DropdownMountPoint;
            public LayoutElement DropdownLayoutElement;

            public Image ResultIcon;
            public TextMeshProUGUI ResultStackText;
            public TextMeshProUGUI ItemLabel;
            public TextMeshProUGUI DepthText;
            public TextMeshProUGUI PathsText;

            public bool IsExpanded;
            public float CollapsedHeight;

            private void OnDestroy()
            {
                if (RowTopButton != null)
                {
                    RowTopButton.onClick.RemoveAllListeners();
                }
                Entry = null;
                _openRow = null;
            }
        }

        internal sealed class PathRowRuntime : MonoBehaviour
        {
            internal RecipeRowRuntime OwnerRow;
            internal RecipeChain Chain;
            internal Image BackgroundImage;
            public RectTransform VisualRect;
            public Button pathButton;
            public EventTrigger buttonEvent;
            private ColorFaderRuntime fader;
            public ScrollRect parentScroll;

            private bool isSelected;
            private bool isHovered;


            private const float FadeDuration = 0.1f;

            private static readonly Color Col_BG_Normal = new Color32(26, 26, 26, 50); // deselected color
            private static readonly Color Col_BG_Active = new Color32(206, 198, 143, 200); // selected color
            private static readonly Color Col_BG_Hover = new Color32(206, 198, 143, 75); // hover color

            private void Awake()
            {
                if (pathButton == null)
                {
                    pathButton = GetComponent<Button>();
                }
                if (buttonEvent == null)
                {
                    buttonEvent = GetComponent<EventTrigger>();
                }
                if (pathButton != null)
                {
                    pathButton.onClick.AddListener(OnClicked);
                }
                if (parentScroll == null)
                {
                    parentScroll = GetComponentInParent<ScrollRect>();
                }

                EventTrigger.Entry entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entryEnter.callback.AddListener((data) => OnHighlightChanged(true));
                buttonEvent.triggers.Add(entryEnter);
                EventTrigger.Entry entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                entryExit.callback.AddListener((data) => OnHighlightChanged(false));
                buttonEvent.triggers.Add(entryExit);
                EventTrigger.Entry entryScroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
                entryScroll.callback.AddListener((data) => BubbleScroll(data));
                buttonEvent.triggers.Add(entryScroll);

                if (VisualRect == null)
                {
                    var visuals = transform.Find("Visuals");
                    if (visuals != null) VisualRect = visuals.GetComponent<RectTransform>();
                }

                if (VisualRect != null)
                {
                    if (BackgroundImage == null) BackgroundImage = VisualRect.GetComponent<Image>();
                    fader = VisualRect.GetComponent<ColorFaderRuntime>();
                }
            }

            public void Init(RecipeRowRuntime owner, RecipeChain chain)
            {
                OwnerRow = owner;
                Chain = chain;
                isSelected = false;
                isHovered = false;

                UpdateVisuals(true);
            }

            private void OnDestroy()
            {
                if (pathButton != null)
                {
                    pathButton.onClick.RemoveListener(OnClicked);
                }

                if (buttonEvent != null)
                {
                    buttonEvent.triggers.Clear();
                }

                if (_currentHoveredPath == this) _currentHoveredPath = null;
                if (_selectedPathUI == this) _selectedPathUI = null;
                OwnerRow = null;
                Chain = null;
            }

            private void OnClicked()
            {
                CraftUI.OnPathSelected(this);
            }

            private void OnHighlightChanged(bool isHighlighted)
            {
                if (isHighlighted)
                {
                    isHovered = true;
                    if (_currentHoveredPath == this) _currentHoveredPath = null;
                    AttachReticleTo(VisualRect);
                }
                else
                {
                    isHovered = false;
                    if (IsReticleAttachedTo(VisualRect)) RestoreReticleToSelection();
                }
                UpdateVisuals(false);
            }
            private void BubbleScroll(BaseEventData data)
            {
                if (parentScroll != null && data is PointerEventData pointerData)
                {
                    parentScroll.OnScroll(pointerData);
                }
            }

            public void SetSelected(bool selected)
            {
                if (isSelected != selected)
                {
                    isSelected = selected;

                    if (isSelected) CraftUI.AttachReticleTo(VisualRect);
                    UpdateVisuals(false);
                }
            }

            private void UpdateVisuals(bool instant)
            {
                if (BackgroundImage == null) return;

                Color targetColor;

                if (isSelected) targetColor = Col_BG_Active;
                else if (isHovered) targetColor = Col_BG_Hover;
                else targetColor = Col_BG_Normal;

                if (instant) BackgroundImage.color = targetColor;
                else fader.CrossFadeColor(targetColor, FadeDuration, ignoreTimeScale: true);
            }
        }

        private class NestedScrollRect : ScrollRect
        {
            public ScrollRect ParentScroll;

            public override void OnScroll(UnityEngine.EventSystems.PointerEventData data)
            {
                if (!this.IsActive()) return;

                bool canScrollVertical = content.rect.height > viewport.rect.height;
                if (!canScrollVertical)
                {
                    if (ParentScroll) ParentScroll.OnScroll(data);
                    return;
                }

                float deltaY = data.scrollDelta.y;
                float currentPos = verticalNormalizedPosition;
                const float boundaryThreshold = 0.001f;

                if (ParentScroll && ((deltaY > 0 && currentPos >= (1f - boundaryThreshold)) || (deltaY < 0 && currentPos <= boundaryThreshold))) ParentScroll.OnScroll(data);
                else base.OnScroll(data);
            }
        }

        private sealed class CraftUIRunner : MonoBehaviour { }

        private class RecipeDropdownRuntime : MonoBehaviour
        {
            public ScrollRect ScrollRect;
            public RectTransform Content;
            public RectTransform Background;
            public RecipeRowRuntime CurrentOwner;

            public int SelectedPathIndex = -1;

            public void OpenFor(RecipeRowRuntime owner)
            {
                CurrentOwner = owner;
                gameObject.SetActive(true);

                transform.SetParent(owner.DropdownMountPoint, false);

                var rt = (RectTransform)transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                if (ScrollRect != null)
                {
                    ScrollRect.enabled = owner.Entry.Chains.Count > PathsContainerMaxVisibleRows;
                    ScrollRect.verticalNormalizedPosition = 1f;
                }

                PopulateDropdown(Content, owner);
            }

            public void Close()
            {
                if (_activeDropdownRoutine != null && _runner != null)
                {
                    _runner.StopCoroutine(_activeDropdownRoutine);
                    _activeDropdownRoutine = null;
                }

                if (CurrentOwner == null) return;

                SelectedPathIndex = -1;
                transform.SetParent(_cookbookRoot.transform, false);
                gameObject.SetActive(false);
                CurrentOwner = null;
            }

            private void OnDestroy()
            {
                CurrentOwner = null;
            }

        }

        //==================== Cookbook Builders ====================
        internal static void CookBookSkeleton(RectTransform cookbookRoot)
        {
            if (!cookbookRoot) return;

            // Clear any leftovers if you re-enter the UI
            for (int i = cookbookRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(cookbookRoot.GetChild(i).gameObject);
            }

            //------------------------ Border ------------------------
            AddBorderTapered((RectTransform)_cookbookRoot.transform, new Color32(209, 209, 210, 255), 2f, 2f);

            // ----------------------------- Dimensions ------------------------------
            float padTopPx = RoundToEven(CookBookPanelPaddingTopNorm * _panelHeight);
            float padBottomPx = RoundToEven(CookBookPanelPaddingBottomNorm * _panelHeight);
            float padLeftPx = RoundToEven(CookBookPanelPaddingLeftNorm * _panelWidth);
            float padRightPx = RoundToEven(CookBookPanelPaddingRightNorm * _panelWidth);

            float spacingPx = RoundToEven(CookBookPanelElementSpacingNorm * _panelHeight);
            float searchBarHeightPx = RoundToEven(SearchBarContainerNorm * _panelHeight);
            float footerHeightPx = RoundToEven(FooterHeightNorm * _panelHeight);

            float innerHeight = _panelHeight - padTopPx - padBottomPx;
            float recipeListHeightPx = innerHeight - searchBarHeightPx - footerHeightPx - (spacingPx * 2);

            int recipeListVertPadPx = Mathf.RoundToInt(RecipeListVerticalPaddingNorm * _panelHeight);
            if (recipeListHeightPx < 0f) recipeListHeightPx = 0f;

            //------------------------ SearchBarContainer ------------------------
            GameObject searchGO = CreateUIObject("SearchBarContainer", typeof(RectTransform));

            var searchRect = searchGO.GetComponent<RectTransform>();
            searchRect.SetParent(cookbookRoot, false);

            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(0.5f, 1f);
            searchRect.sizeDelta = new Vector2(0f, searchBarHeightPx);
            searchRect.anchoredPosition = new Vector2(0f, -padTopPx);
            searchRect.offsetMin = new Vector2(padLeftPx, searchRect.offsetMin.y);
            searchRect.offsetMax = new Vector2(-padRightPx, searchRect.offsetMax.y);

            AddBorderTapered(searchRect, new Color32(209, 209, 210, 200), bottom: Mathf.Max(1f, SearchBarBottomBorderThicknessNorm * _panelHeight));

            // --- Search Input ---
            GameObject inputGO = CreateUIObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.SetParent(searchRect, false);
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = new Vector2(0.75f, 1f);
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = new Vector2(-5f, 0f);

            var bgImage = inputGO.GetComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.4f);
            bgImage.raycastTarget = false;

            _searchInputField = inputGO.GetComponent<TMP_InputField>();

            GameObject textAreaGO = CreateUIObject("Text Area", typeof(RectTransform), typeof(RectMask2D));
            var textAreaRT = textAreaGO.GetComponent<RectTransform>();

            textAreaRT.SetParent(inputRect, false);
            textAreaRT.anchorMin = Vector2.zero;
            textAreaRT.anchorMax = Vector2.one;
            textAreaRT.sizeDelta = Vector2.zero;

            GameObject textGO = CreateUIObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));

            var textTMP = textGO.GetComponent<TextMeshProUGUI>();
            textTMP.fontSize = 20f;
            textTMP.alignment = TextAlignmentOptions.Center;
            textTMP.color = Color.white;

            var textRT = textGO.GetComponent<RectTransform>();
            textRT.SetParent(textAreaRT, false);
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = Vector2.zero;

            GameObject phGO = CreateUIObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));

            var phRT = phGO.GetComponent<RectTransform>();
            var placeholderTMP = phGO.GetComponent<TextMeshProUGUI>();

            phRT.SetParent(textAreaRT, false);
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.sizeDelta = Vector2.zero;

            placeholderTMP.text = "search";
            placeholderTMP.fontSize = 20f;
            placeholderTMP.alignment = TextAlignmentOptions.Center;
            placeholderTMP.color = new Color(1f, 1f, 1f, 0.5f);
            placeholderTMP.raycastTarget = false;

            _searchInputField.textViewport = textAreaRT;
            _searchInputField.textComponent = textTMP;
            _searchInputField.placeholder = placeholderTMP;
            _searchInputField.onValueChanged.AddListener(_ => RefreshUIVisibility());

            GameObject cycleBtnGO = CreateUIObject("CategoryCycleButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var cycleRT = cycleBtnGO.GetComponent<RectTransform>();
            cycleRT.SetParent(searchRect, false);
            cycleRT.anchorMin = new Vector2(0.8f, 0f); // Occupy the right 20%
            cycleRT.anchorMax = new Vector2(1f, 1f);    // Match full height of container
            cycleRT.offsetMin = new Vector2(0f, 0f);    // Flush with right edge
            cycleRT.offsetMax = Vector2.zero;

            var cycleImg = cycleBtnGO.GetComponent<Image>();
            cycleImg.color = new Color(0f, 0f, 0f, 0.4f);
            cycleImg.raycastTarget = true;

            var Btn = cycleBtnGO.GetComponent<Button>();
            Btn.transition = Selectable.Transition.None;
            Btn.targetGraphic = cycleImg;
            Btn.interactable = true;

            // Label inside the button
            GameObject labelGO = CreateUIObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelTMP = labelGO.GetComponent<TextMeshProUGUI>();
            labelTMP.fontSize = 14f;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.raycastTarget = false;

            labelGO.transform.SetParent(cycleRT, false);
            var labelRT = (RectTransform)labelGO.transform;
            labelRT.anchorMin = Vector2.zero;
            labelRT.anchorMax = Vector2.one;
            labelRT.sizeDelta = Vector2.zero;

            UpdateCycleButtonVisuals(labelTMP);

            Btn.onClick.AddListener(() =>
            {
                RecipeFilter.CycleCategory();
                UpdateCycleButtonVisuals(labelTMP);
                RefreshUIVisibility();
            });

            // TODO: update general shape to match ROR2 style
            // ------------------------ Global Craft Button) ------------------------
            GameObject footerGO = CreateUIObject("Footer", typeof(RectTransform));
            var footerRT = footerGO.GetComponent<RectTransform>();

            footerRT.SetParent(cookbookRoot, false);
            footerRT.anchorMin = new Vector2(0f, 0f);
            footerRT.anchorMax = new Vector2(1f, 0f);
            footerRT.pivot = new Vector2(0.5f, 0f);

            footerRT.sizeDelta = new Vector2(0f, footerHeightPx);
            footerRT.anchoredPosition = new Vector2(0f, padBottomPx);

            footerRT.offsetMin = new Vector2(padLeftPx, footerRT.offsetMin.y);
            footerRT.offsetMax = new Vector2(-padRightPx, footerRT.offsetMax.y);

            GameObject craftBtnGO = CreateUIObject("GlobalCraftButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var craftBtnRT = craftBtnGO.GetComponent<RectTransform>();
            var craftBtnImg = craftBtnGO.GetComponent<Image>();
            var craftBtn = craftBtnGO.GetComponent<Button>();

            craftBtnRT.SetParent(footerRT, false);
            craftBtnRT.anchorMin = Vector2.zero;
            craftBtnRT.anchorMax = Vector2.one;
            craftBtnRT.sizeDelta = Vector2.zero;

            craftBtnImg.color = new Color32(40, 40, 40, 255);
            craftBtnImg.raycastTarget = false;
            craftBtn.interactable = false;

            var btnTextGO = CreateUIObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var btnTextRT = btnTextGO.GetComponent<RectTransform>();
            var btnTextTMP = btnTextGO.GetComponent<TextMeshProUGUI>();

            btnTextRT.SetParent(craftBtnRT, false);
            btnTextRT.anchorMin = Vector2.zero;
            btnTextRT.anchorMax = Vector2.one;

            btnTextTMP.text = "select a recipe";
            btnTextTMP.alignment = TextAlignmentOptions.Center;
            btnTextTMP.fontSize = footerHeightPx * 0.5f;
            btnTextTMP.color = new Color32(100, 100, 100, 255); // Dim text

            _globalCraftButton = craftBtn;
            _globalCraftButtonText = btnTextTMP;
            _globalCraftButtonImage = craftBtnImg;

            AddBorder(footerRT, new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);
            _globalCraftButton.onClick.AddListener(OnGlobalCraftButtonClicked);

            //------------------------ RecipeListContainer ------------------------
            GameObject listGO = CreateUIObject("RecipeListContainer", typeof(RectTransform), typeof(ScrollRect));

            var listRect = listGO.GetComponent<RectTransform>();
            var scroll = listGO.GetComponent<ScrollRect>();

            listRect.SetParent(cookbookRoot, false);
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = Vector2.one;
            listRect.pivot = new Vector2(0.5f, 1f);
            listRect.sizeDelta = new Vector2(0f, recipeListHeightPx);

            float listTop = padTopPx + searchBarHeightPx + spacingPx;
            listRect.anchoredPosition = new Vector2(0f, -listTop);

            listRect.offsetMin = new Vector2(padLeftPx, listRect.offsetMin.y);
            listRect.offsetMax = new Vector2(-padRightPx, listRect.offsetMax.y);

            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = RowTopHeightNorm * _panelHeight * 0.5f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.16f;
            scroll.elasticity = 0.1f;

            GameObject viewportGO = CreateUIObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRT = viewportGO.GetComponent<RectTransform>();
            var viewportImg = viewportGO.GetComponent<Image>();

            viewportRT.SetParent(listRect, false);
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRT;

            viewportImg.color = Color.clear;
            viewportImg.raycastTarget = false;

            GameObject contentGO = CreateUIObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(CanvasGroup));

            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.SetParent(viewportRT, false);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = Vector2.one;
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = Vector2.zero;

            scroll.content = contentRT;
            _recipeListContent = contentRT;

            // rows stacked from top
            var vLayout = contentGO.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(
                Mathf.RoundToInt(RecipeListLeftPaddingNorm * _panelWidth),
                Mathf.RoundToInt(RecipeListRightPaddingNorm * _panelWidth),
                recipeListVertPadPx, recipeListVertPadPx
            );
            vLayout.spacing = RecipeListElementSpacingNorm * _panelHeight;
            vLayout.childAlignment = TextAnchor.UpperCenter;
            vLayout.childControlHeight = true;
            vLayout.childControlWidth = true;
            vLayout.childForceExpandHeight = false;
            vLayout.childForceExpandWidth = true;

            var fitter = contentGO.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _skeletonBuilt = true;
        }

        //==================== Instantiation ====================
        internal static GameObject CreateRecipeRow(RectTransform parent, CraftableEntry entry)
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
                return rowGO;
            }

            runtime.Entry = entry;
            runtime.IsExpanded = false;

            // ---------------- Label ----------------
            if (runtime.ItemLabel != null)
            {
                runtime.ItemLabel.text = GetEntryDisplayName(entry);
                runtime.ItemLabel.color = GetEntryColor(entry);
            }

            // ---------------- MetaData: Depth ----------------
            if (runtime.DepthText != null) runtime.DepthText.text = $" Min Depth: {entry.MinDepth}";

            // ---------------- MetaData: Paths ----------------
            if (runtime.PathsText != null) runtime.PathsText.text = $"Paths: {entry.Chains.Count}";

            // ---------------- ItemIcon ----------------
            if (runtime.ResultIcon != null)
            {
                Sprite iconSprite = GetIcon(entry.ResultIndex);

                if (iconSprite != null)
                {
                    runtime.ResultIcon.sprite = iconSprite;
                    runtime.ResultIcon.color = Color.white;
                }
                else
                {
                    runtime.ResultIcon.sprite = null;
                    runtime.ResultIcon.color = new Color(1f, 1f, 1f, 0.1f);
                }
            }

            // ---------------- Stack Text ----------------
            if (runtime.ResultStackText != null)
            {
                if (entry.ResultCount > 1)
                {
                    runtime.ResultStackText.text = entry.ResultCount.ToString();
                    runtime.ResultStackText.gameObject.SetActive(true);
                }
                else runtime.ResultStackText.gameObject.SetActive(false);
            }

            if (runtime.RowTopButton != null)
            {
                runtime.RowTopButton.onClick.RemoveAllListeners();
                runtime.RowTopButton.onClick.AddListener(() => ToggleRecipeRow(runtime));
            }

            if (runtime.DropdownLayoutElement != null) runtime.DropdownLayoutElement.preferredHeight = 0f;

            return rowGO;
        }

        private static GameObject CreatePathRow(RectTransform parent, RecipeChain chain, RecipeRowRuntime owner)
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
                    Sprite icon = GetIcon(ingredient.UnifiedIndex);
                    if (icon != null) InstantiateSlot(_ingredientSlotTemplate, runtime.VisualRect, icon, ingredient.Count);
                }
            }

            if (chain.AlliedTradeSparse != null)
            {
                foreach (var trade in chain.AlliedTradeSparse)
                {
                    Sprite icon = GetIcon(trade.UnifiedIndex);
                    if (icon != null) InstantiateSlot(_tradeSlotTemplate, runtime.VisualRect, icon, trade.Count);
                }
            }

            if (chain.DroneCostSparse != null)
            {
                var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

                foreach (var ingredient in chain.DroneCostSparse)
                {
                    DroneCandidate candidate = InventoryTracker.GetScrapCandidate(ingredient.UnifiedIndex);

                    if (candidate.DroneIdx != DroneIndex.None)
                    {
                        Sprite droneSprite = GetDroneIcon(candidate.DroneIdx);
                        if (droneSprite != null)
                        {
                            bool isAlliedDrone = candidate.Owner != null && candidate.Owner != localUser;
                            GameObject template = isAlliedDrone ? _tradeSlotTemplate : _droneSlotTemplate;

                            InstantiateSlot(template, runtime.VisualRect, droneSprite, ingredient.Count);
                        }
                    }
                }
            }
            return pathRowGO;
        }

        private static void InstantiateSlot(GameObject template, Transform parentrow, Sprite icon, int count)
        {
            if (template == null) return;
            GameObject slotGO = UnityEngine.Object.Instantiate(template, parentrow);
            slotGO.SetActive(true);

            var iconImg = slotGO.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImg) iconImg.sprite = icon;

            var stackTmp = slotGO.transform.Find("StackText")?.GetComponent<TextMeshProUGUI>();
            if (stackTmp)
            {
                stackTmp.text = count > 1 ? count.ToString() : string.Empty;
                stackTmp.gameObject.SetActive(count > 1);
            }
        }

        //==================== Prefabs ====================
        private static void BuildRecipeRowTemplate()
        {
            if (_recipeRowTemplate != null) return;

            float topPadPx = RoundToEven(RowTopTopPaddingNorm * _panelHeight);
            float bottomPadPx = RoundToEven(RowTopBottomPaddingNorm * _panelHeight);
            float elementSpacingPx = RoundToEven(RowTopElementSpacingNorm * _panelWidth);
            float rowTopHeightPx = RoundToEven(RowTopHeightNorm * _panelHeight);
            float metaWidthPx = RoundToEven(MetaDataColumnWidthNorm * _panelWidth);
            float metaSpacingPx = RoundToEven(MetaDataElementSpacingNorm * _panelHeight);
            float dropDownArrowSize = RoundToEven(DropDownArrowSizeNorm * _panelHeight);
            float textSize = RoundToEven(textSizeNorm * _panelHeight);
            float innerHeight = rowTopHeightPx - (topPadPx + bottomPadPx);

            // ---------------- RecipeRow root ----------------
            GameObject rowGO = CreateUIObject("RecipeRowTemplate", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement), typeof(RecipeRowRuntime));

            var rowRT = rowGO.GetComponent<RectTransform>();
            var rowVLG = rowGO.GetComponent<VerticalLayoutGroup>();
            var rowLE = rowGO.GetComponent<LayoutElement>();

            rowRT.SetParent(_cookbookRoot.transform, false);
            rowGO.SetActive(false);

            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = Vector2.zero;
            rowRT.sizeDelta = new Vector2(0f, rowTopHeightPx);

            rowVLG.spacing = 0f;
            rowVLG.childAlignment = TextAnchor.UpperCenter;
            rowVLG.childControlWidth = true;
            rowVLG.childControlHeight = true;
            rowVLG.childForceExpandWidth = true;
            rowVLG.childForceExpandHeight = false;

            rowLE.minHeight = rowTopHeightPx;
            rowLE.preferredHeight = rowTopHeightPx;
            rowLE.flexibleHeight = 0f;
            rowLE.flexibleWidth = 1f;

            // ---------------- RowTop ----------------
            GameObject rowTopGO = CreateUIObject("RowTop", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));

            var rowTopRT = rowTopGO.GetComponent<RectTransform>();
            var rowTopImg = rowTopGO.GetComponent<Image>();
            var rowTopLE = rowTopGO.GetComponent<LayoutElement>();

            rowTopRT.SetParent(rowRT, false);

            rowTopLE.minHeight = rowTopHeightPx;
            rowTopLE.preferredHeight = rowTopHeightPx;
            rowTopLE.flexibleHeight = 0f;
            rowTopLE.flexibleWidth = 1f;

            rowTopImg.color = new Color(0f, 0f, 0f, 0f);
            rowTopImg.raycastTarget = true;

            // ---------------- DropDown ----------------
            GameObject dropGO = CreateUIObject("DropDown", typeof(RectTransform));

            var dropRT = dropGO.GetComponent<RectTransform>();

            dropRT.SetParent(rowTopRT, false);
            dropRT.anchorMin = new Vector2(0f, 0.5f);
            dropRT.anchorMax = new Vector2(0f, 0.5f);
            dropRT.pivot = new Vector2(0f, 0.5f);
            dropRT.sizeDelta = new Vector2(innerHeight, innerHeight);
            dropRT.anchoredPosition = Vector2.zero;

            GameObject arrowGO = CreateUIObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));

            var arrowRT = arrowGO.GetComponent<RectTransform>();
            var arrowTMP = arrowGO.GetComponent<TextMeshProUGUI>();

            arrowRT.SetParent(dropRT, false);
            arrowRT.anchorMin = Vector2.zero;
            arrowRT.anchorMax = Vector2.one;
            arrowRT.offsetMin = Vector2.zero;
            arrowRT.offsetMax = Vector2.zero;

            arrowTMP.text = ">";
            arrowTMP.alignment = TextAlignmentOptions.Center;
            arrowTMP.fontSize = dropDownArrowSize;
            arrowTMP.color = Color.white;
            arrowTMP.raycastTarget = false;
            arrowTMP.enableWordWrapping = false;
            arrowTMP.overflowMode = TextOverflowModes.Overflow;

            // ---------------- Item Slot ----------------
            GameObject slotGO = UnityEngine.Object.Instantiate(_ResultSlotTemplate, rowTopRT, false);
            slotGO.name = "ItemSlot";
            slotGO.SetActive(true);

            var slotRT = slotGO.GetComponent<RectTransform>();
            slotRT.SetParent(rowTopRT, false);

            float slotX = innerHeight + elementSpacingPx;

            slotRT.anchorMin = new Vector2(0f, 0.5f);
            slotRT.anchorMax = new Vector2(0f, 0.5f);
            slotRT.pivot = new Vector2(0f, 0.5f);
            slotRT.sizeDelta = new Vector2(innerHeight, innerHeight);
            slotRT.anchoredPosition = new Vector2(slotX, 0f);

            // ---------------- Item Label ----------------
            GameObject labelGO = CreateUIObject("ItemLabel", typeof(RectTransform), typeof(TextMeshProUGUI));

            var labelRT = labelGO.GetComponent<RectTransform>();
            var labelTMP = labelGO.GetComponent<TextMeshProUGUI>();

            labelRT.SetParent(rowTopRT, false);

            float labelLeftOffset = slotX + innerHeight + elementSpacingPx;
            float labelRightOffset = -(metaWidthPx + elementSpacingPx);

            labelRT.anchorMin = new Vector2(0f, 0.5f);
            labelRT.anchorMax = new Vector2(1f, 0.5f);
            labelRT.pivot = new Vector2(0.5f, 0.5f);

            labelRT.offsetMin = new Vector2(labelLeftOffset, -innerHeight * 0.5f);
            labelRT.offsetMax = new Vector2(labelRightOffset, innerHeight * 0.5f);

            labelTMP.text = "NAME";
            labelTMP.fontSize = textSize;
            labelTMP.enableWordWrapping = false;
            labelTMP.overflowMode = TextOverflowModes.Ellipsis;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = Color.white;
            labelTMP.raycastTarget = false;

            // ---------------- MetaData ----------------
            GameObject metaGO = CreateUIObject("MetaData", typeof(RectTransform));

            var metaRT = metaGO.GetComponent<RectTransform>();

            metaRT.SetParent(rowTopRT, false);

            metaRT.anchorMin = new Vector2(1f, 0.5f);
            metaRT.anchorMax = new Vector2(1f, 0.5f);
            metaRT.pivot = new Vector2(1f, 0.5f);
            metaRT.sizeDelta = new Vector2(metaWidthPx, innerHeight);
            metaRT.anchoredPosition = Vector2.zero;

            float halfGap = RoundToEven(metaSpacingPx / 2f);

            var depthGO = CreateUIObject("MinimumDepth", typeof(RectTransform), typeof(TextMeshProUGUI));
            var depthRT = depthGO.GetComponent<RectTransform>();
            var depthTMP = depthGO.GetComponent<TextMeshProUGUI>();

            depthRT.SetParent(metaRT, false);
            depthRT.anchorMin = new Vector2(1f, 0.5f);
            depthRT.anchorMax = new Vector2(1f, 0.5f);
            depthRT.pivot = new Vector2(1f, 0f);
            depthRT.anchoredPosition = new Vector2(0f, halfGap);
            depthRT.sizeDelta = new Vector2(metaWidthPx, 0f);

            depthTMP.text = "Depth: 0";
            depthTMP.fontSize = 16f;
            depthTMP.alignment = TextAlignmentOptions.BottomRight;
            depthTMP.color = Color.white;
            depthTMP.raycastTarget = false;

            var pathsGO = CreateUIObject("AvailablePaths", typeof(RectTransform), typeof(TextMeshProUGUI));
            var pathsRT = pathsGO.GetComponent<RectTransform>();
            var pathsTMP = pathsGO.GetComponent<TextMeshProUGUI>();

            pathsRT.SetParent(metaRT, false);
            pathsRT.anchorMin = new Vector2(1f, 0.5f);
            pathsRT.anchorMax = new Vector2(1f, 0.5f);
            pathsRT.pivot = new Vector2(1f, 1f); // Top-Right
            pathsRT.anchoredPosition = new Vector2(0f, -halfGap); // Shift Down
            pathsRT.sizeDelta = new Vector2(metaWidthPx, 0f);

            pathsTMP.text = "Paths: 0";
            pathsTMP.fontSize = 16f;
            pathsTMP.alignment = TextAlignmentOptions.TopRight;
            pathsTMP.color = Color.white;
            pathsTMP.raycastTarget = false;

            AddBorderTapered(rowTopRT, new Color32(209, 209, 210, 200), 1f, 1f);

            // ---------------- PathsContainer ----------------
            GameObject mountGO = CreateUIObject("DropdownMountPoint", typeof(RectTransform), typeof(LayoutElement));
            var mountRT = mountGO.GetComponent<RectTransform>();
            var mountLE = mountGO.GetComponent<LayoutElement>();

            mountRT.SetParent(rowRT, false);

            mountRT.pivot = new Vector2(0.5f, 1f);
            mountRT.anchorMin = new Vector2(0f, 1f);
            mountRT.anchorMax = new Vector2(1f, 1f);
            mountRT.sizeDelta = Vector2.zero;

            mountLE.minHeight = 0f;
            mountLE.preferredHeight = 0f;
            mountLE.flexibleHeight = 0f;
            mountLE.flexibleWidth = 1f;

            // ---------------- Runtime wiring ----------------
            var runtime = rowGO.GetComponent<RecipeRowRuntime>();
            runtime.Entry = null;
            runtime.RowTransform = rowRT;
            runtime.RowTop = rowTopRT;
            runtime.RowLayoutElement = rowLE;
            runtime.RowTopButton = rowTopGO.GetComponent<Button>();
            runtime.ArrowText = arrowTMP;
            runtime.DropdownMountPoint = mountRT;
            runtime.DropdownLayoutElement = mountLE;
            runtime.ResultIcon = slotGO.transform.Find("Icon").GetComponent<Image>();
            runtime.ResultStackText = slotGO.transform.Find("StackText").GetComponent<TextMeshProUGUI>();
            runtime.ItemLabel = labelTMP;
            runtime.DepthText = depthTMP;
            runtime.PathsText = pathsTMP;

            runtime.CollapsedHeight = rowRT.sizeDelta.y;
            runtime.IsExpanded = false;

            _recipeRowTemplate = rowGO;
        }

        private static void BuildPathRowTemplate()
        {
            if (_pathRowTemplate != null) return;

            float visualHeightPx = RoundToEven(PathRowHeightNorm * _panelHeight);
            float spacingPx = RoundToEven(PathsContainerSpacingNorm * _panelHeight);
            float slotSpacingPx = RoundToEven(PathRowIngredientSpacingNorm * _panelWidth);
            int leftPadPx = Mathf.RoundToInt(PathRowLeftPaddingNorm * _panelWidth);
            int rightPadPx = Mathf.RoundToInt(PathRowRightPaddingNorm * _panelWidth);
            float totalRowHeightPx = visualHeightPx + spacingPx;
            float paddingY = RoundToEven(spacingPx / 2f);

            var rowGO = CreateUIObject("PathRowTemplate", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(Button), typeof(EventTrigger), typeof(PathRowRuntime));

            var rowRT = (RectTransform)rowGO.transform;
            var rowLE = rowGO.GetComponent<LayoutElement>();
            var rowHitbox = rowGO.GetComponent<Image>();
            var runtime = rowGO.GetComponent<PathRowRuntime>();
            var pathButton = rowGO.GetComponent<Button>();
            var buttonEvent = rowGO.GetComponent<EventTrigger>();

            rowRT.SetParent(_cookbookRoot.transform, false);
            rowGO.SetActive(false);

            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = Vector2.zero;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            rowLE.preferredHeight = totalRowHeightPx;
            rowLE.flexibleHeight = 0f;
            rowLE.flexibleWidth = 1f;

            rowHitbox.color = Color.clear;
            rowHitbox.raycastTarget = true;

            pathButton.targetGraphic = rowHitbox;
            pathButton.transition = Selectable.Transition.None;

            var visualGO = CreateUIObject("Visuals", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ColorFaderRuntime));

            var visualRT = (RectTransform)visualGO.transform;
            var visualImg = visualGO.GetComponent<Image>();
            var hlg = visualGO.GetComponent<HorizontalLayoutGroup>();

            visualRT.SetParent(rowRT, false);

            visualRT.anchorMin = Vector2.zero;
            visualRT.anchorMax = Vector2.one;
            visualRT.offsetMin = new Vector2(0f, paddingY);
            visualRT.offsetMax = new Vector2(0f, -paddingY);

            hlg.spacing = slotSpacingPx;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.padding = new RectOffset(leftPadPx, rightPadPx, 0, 0);

            visualImg.color = new Color32(26, 26, 26, 100);
            visualImg.raycastTarget = false;

            AddBorderTapered(visualRT, new Color32(209, 209, 210, 200), top: 1f, bottom: 1f);
            runtime.BackgroundImage = visualImg;
            runtime.VisualRect = visualRT;
            runtime.pathButton = pathButton;
            runtime.buttonEvent = buttonEvent;

            _pathRowTemplate = rowGO;
        }

        private static void BuildSharedDropdown()
        {
            if (_sharedDropdown != null) return;

            GameObject drawerGO = CreateUIObject("SharedDropdown", typeof(RectTransform), typeof(Image), typeof(NestedScrollRect), typeof(RecipeDropdownRuntime));

            var drawerRT = drawerGO.GetComponent<RectTransform>();
            var img = drawerGO.GetComponent<Image>();
            _sharedDropdown = drawerGO.GetComponent<RecipeDropdownRuntime>();

            drawerRT.anchorMin = new Vector2(0f, 0f);
            drawerRT.anchorMax = new Vector2(1f, 1f);
            drawerRT.pivot = new Vector2(0.5f, 0.5f);
            drawerRT.offsetMin = Vector2.zero;
            drawerRT.offsetMax = Vector2.zero;

            img.color = Color.clear;
            img.raycastTarget = false;

            var scroll = drawerGO.GetComponent<NestedScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = RowTopHeightNorm * _panelHeight * 0.5f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.16f;
            scroll.elasticity = 0.1f;

            if (_cookbookRoot)
            {
                var mainScroll = _cookbookRoot.GetComponentInChildren<ScrollRect>();
                scroll.ParentScroll = mainScroll;
            }

            var viewportGO = CreateUIObject("Viewport", typeof(RectTransform), typeof(RectMask2D));

            var viewportRT = viewportGO.GetComponent<RectTransform>();
            viewportRT.SetParent(drawerRT, false);
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            viewportRT.sizeDelta = Vector2.zero;
            scroll.viewport = viewportRT;

            var contentGO = CreateUIObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.SetParent(viewportRT, false);

            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            contentRT.anchoredPosition = Vector2.zero;
            scroll.content = contentRT;

            var vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 0f;

            int paddingVertical = Mathf.RoundToInt(PathsContainerSpacingNorm * _panelHeight / 2f);
            int paddingHorizontal = Mathf.RoundToInt(PathsContainerLeftPaddingNorm * _panelWidth);
            vlg.padding = new RectOffset(paddingHorizontal, paddingHorizontal, paddingVertical, paddingVertical);

            contentGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sharedDropdown.ScrollRect = scroll;
            _sharedDropdown.Content = contentRT;
            _sharedDropdown.Background = drawerRT;
            drawerGO.transform.SetParent(_cookbookRoot.transform, false);
            drawerGO.SetActive(false);
        }

        private static void BuildSharedHoverRect()
        {
            if (_selectionReticle != null) return;

            var reticleGO = CreateUIObject("SelectionReticle", typeof(RectTransform), typeof(Image), typeof(Canvas), typeof(LayoutElement));
            _selectionReticle = reticleGO.GetComponent<RectTransform>();
            var le = reticleGO.GetComponent<LayoutElement>();
            le.ignoreLayout = true;

            _selectionReticle.SetParent(_cookbookRoot.transform, false);

            _selectionReticle.anchorMin = Vector2.zero;
            _selectionReticle.anchorMax = Vector2.one;
            _selectionReticle.pivot = new Vector2(0.5f, 0.5f);
            _selectionReticle.sizeDelta = Vector2.zero;

            var canvas = reticleGO.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 30;

            AddBorder(_selectionReticle, new Color32(255, 255, 255, 255), top: 3f, bottom: 3f, left: 3f, right: 3f);

            var img = reticleGO.GetComponent<Image>();
            img.color = new Color32(0, 0, 0, 0);
            img.raycastTarget = false;

            reticleGO.SetActive(false);
        }

        private static void EnsureResultSlotArtTemplates(CraftingPanel craftingPanel)
        {
            if (_ResultSlotTemplate != null) return;

            // configure base dimensions
            float topPadPx = RoundToEven(RowTopTopPaddingNorm * _panelHeight);
            float bottomPadPx = RoundToEven(RowTopBottomPaddingNorm * _panelHeight);
            float rowTopHeightPx = RoundToEven(RowTopHeightNorm * _panelHeight);
            float SlotHeightPx = rowTopHeightPx - (topPadPx + bottomPadPx);
            float iconInsetPx = RoundToEven(SlotHeightPx * 0.125f);

            var slotGO = CreateUIObject("ResultSlotTemplate", typeof(RectTransform), typeof(LayoutElement));
            var slotRT = (RectTransform)slotGO.transform;
            var slotLE = slotGO.GetComponent<LayoutElement>();

            slotRT.SetParent(_cookbookRoot.transform, false);
            slotGO.SetActive(false);

            slotRT.sizeDelta = new Vector2(SlotHeightPx, SlotHeightPx);

            slotRT.anchorMin = new Vector2(0f, 0.5f);
            slotRT.anchorMax = new Vector2(0f, 0.5f);
            slotRT.pivot = new Vector2(0f, 0.5f);
            slotRT.anchoredPosition = Vector2.zero;
            slotRT.sizeDelta = Vector2.zero;

            slotLE.minWidth = SlotHeightPx;
            slotLE.preferredWidth = SlotHeightPx;
            slotLE.minHeight = SlotHeightPx;
            slotLE.preferredHeight = SlotHeightPx;
            slotLE.flexibleWidth = 0f;

            // add standard dark background
            var bgGO = CreateUIObject("IconBackground", typeof(RectTransform), typeof(Image));
            var bgRT = (RectTransform)bgGO.transform;
            var bgImg = bgGO.GetComponent<Image>();

            bgRT.SetParent(slotRT, false);
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;
            bgRT.pivot = new Vector2(0.5f, 0.5f);

            bgImg.color = new Color32(5, 5, 5, 255);
            bgImg.raycastTarget = false;

            // Instantiate vanilla borders for cleanliness
            var outlineInnerloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline (1)");
            var outlineOuterloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline");

            if (outlineInnerloc && outlineOuterloc)
            {
                var inner = InstantiateLayer(outlineInnerloc.gameObject, slotRT);
                inner.name = "ResultOutlineInner";
                inner.SetActive(true);

                var outer = InstantiateLayer(outlineOuterloc.gameObject, slotRT);
                outer.name = "ResultOutlineOuter";
                outer.SetActive(true);
            }

            // initialize icon container
            var iconGO = CreateUIObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRT = (RectTransform)iconGO.transform;
            var iconImg = iconGO.GetComponent<Image>();

            iconRT.SetParent(slotRT, false);
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = new Vector2(iconInsetPx, iconInsetPx);
            iconRT.offsetMax = new Vector2(-iconInsetPx, -iconInsetPx);

            iconImg.sprite = null;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            // initialize stack text
            var stackGO = CreateUIObject("StackText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var stackRT = (RectTransform)stackGO.transform;
            var stackTMP = stackGO.GetComponent<TextMeshProUGUI>();
            var stackLE = stackGO.GetComponent<LayoutElement>();

            stackRT.SetParent(slotRT, false);
            stackRT.anchorMin = new Vector2(1f, 1f);
            stackRT.anchorMax = new Vector2(1f, 1f);
            stackRT.pivot = new Vector2(1f, 1f);
            stackRT.sizeDelta = Vector2.zero;

            float totalRightInset = _ResultStackMargin + _ResultStackMargin;
            stackRT.anchoredPosition = new Vector2(-totalRightInset, -_ResultStackMargin);

            stackTMP.text = string.Empty;
            stackTMP.fontSize = _IngredientStackSizeTextHeightPx;
            stackTMP.alignment = TextAlignmentOptions.TopRight;
            stackTMP.color = Color.white;
            stackTMP.raycastTarget = false;

            stackGO.transform.SetAsLastSibling();
            stackLE.ignoreLayout = true;
            stackGO.SetActive(true);

            _ResultSlotTemplate = slotGO;
        }

        private static void EnsureIngredientSlotTemplate()
        {
            if (_ingredientSlotTemplate != null) return;

            _ingredientSlotTemplate = BuildIngredientTemplate("PhysicalSlot", new Color32(16, 8, 10, 255));

            _droneSlotTemplate = BuildIngredientTemplate("DroneSlot", new Color32(20, 50, 45, 255));

            _tradeSlotTemplate = BuildIngredientTemplate("TradeSlot", new Color32(75, 65, 25, 255));
        }

        private static GameObject BuildIngredientTemplate(string name, Color32 bgColor)
        {
            float IngredientHeightPx = RoundToEven(IngredientHeightNorm * _panelHeight);
            float iconInsetPx = RoundToEven(IngredientHeightNorm * _panelHeight * 0.1f);
            var slotGO = CreateUIObject("IngredientSlotTemplate", typeof(RectTransform), typeof(LayoutElement));
            var slotRT = (RectTransform)slotGO.transform;
            var slotLE = slotGO.GetComponent<LayoutElement>();

            slotRT.SetParent(_cookbookRoot.transform, false);
            slotGO.SetActive(false);

            slotRT.anchorMin = new Vector2(0f, 0.5f);
            slotRT.anchorMax = new Vector2(0f, 0.5f);
            slotRT.pivot = new Vector2(0f, 0.5f);
            slotRT.anchoredPosition = Vector2.zero;
            slotRT.sizeDelta = Vector2.zero;

            slotLE.minWidth = IngredientHeightPx;
            slotLE.minHeight = IngredientHeightPx;
            slotLE.preferredWidth = IngredientHeightPx;
            slotLE.preferredHeight = IngredientHeightPx;
            slotLE.flexibleWidth = 0f;
            slotLE.flexibleHeight = 0f;

            var bgGO = CreateUIObject("Background", typeof(RectTransform), typeof(Image), typeof(Outline));
            var bgRT = (RectTransform)bgGO.transform;
            var bgImg = bgGO.GetComponent<Image>();

            bgRT.SetParent(slotRT, false);
            bgRT.anchorMin = Vector2.zero;
            bgRT.anchorMax = Vector2.one;
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.offsetMin = Vector2.zero;
            bgRT.offsetMax = Vector2.zero;

            bgImg.color = bgColor;
            bgImg.raycastTarget = false;

            var iconGO = CreateUIObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRT = (RectTransform)iconGO.transform;
            var iconImg = iconGO.GetComponent<Image>();

            iconRT.SetParent(slotRT, false);
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.pivot = new Vector2(0.5f, 0.5f);
            iconRT.offsetMin = new Vector2(iconInsetPx, iconInsetPx);
            iconRT.offsetMax = new Vector2(-iconInsetPx, -iconInsetPx);

            iconImg.sprite = null;
            iconImg.color = Color.white;
            iconImg.preserveAspect = true;
            iconImg.raycastTarget = false;

            var stackGO = CreateUIObject("StackText", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var stackRT = (RectTransform)stackGO.transform;
            var stackTMP = stackGO.GetComponent<TextMeshProUGUI>();
            var stackLE = stackGO.GetComponent<LayoutElement>();

            stackRT.SetParent(slotRT, false);

            stackRT.anchorMin = new Vector2(1f, 1f);
            stackRT.anchorMax = new Vector2(1f, 1f);
            stackRT.pivot = new Vector2(1f, 1f);
            stackRT.anchoredPosition = new Vector2(-_IngredientStackMargin, -_IngredientStackMargin);
            stackRT.sizeDelta = Vector2.zero;

            stackTMP.text = string.Empty;
            stackTMP.fontSize = _IngredientStackSizeTextHeightPx;
            stackTMP.alignment = TextAlignmentOptions.TopRight;
            stackTMP.color = Color.white;
            stackTMP.raycastTarget = false;

            stackLE.ignoreLayout = true;
            stackGO.transform.SetAsLastSibling();
            stackGO.SetActive(false);

            AddBorder(bgRT, new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);
            return slotGO;
        }

        //==================== Helpers ====================
        private static void UpdateCycleButtonVisuals(TextMeshProUGUI label)
        {
            if (label == null) return;

            label.text = RecipeFilter.GetLabel();

            // Apply vanilla-themed colors based on category
            label.color = RecipeFilter.CurrentCategory switch
            {
                RecipeFilterCategory.Damage => new Color(1f, 0.3f, 0.3f), // Soft Red
                RecipeFilterCategory.Healing => new Color(0.3f, 1f, 0.3f), // Soft Green
                RecipeFilterCategory.Utility => new Color(0.3f, 0.6f, 1f), // Soft Blue
                _ => Color.white
            };
        }

        private static GameObject CreateUIObject(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static GameObject InstantiateLayer(GameObject template, Transform parent)
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

        private static void SetupStaticVisuals(GameObject root)
        {
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Sprite GetSolidPointSprite()
        {
            if (_solidPointSprite != null) return _solidPointSprite;

            var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();

            _solidPointSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            return _solidPointSprite;
        }

        private static Sprite GetTaperedSprite()
        {
            if (_taperedGradientSprite != null) return _taperedGradientSprite;

            int width = 256;
            int height = 1;
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color[] colors = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);
                // Sin wave: 0 at start, 1 at middle, 0 at end
                float alpha = Mathf.Sin(t * Mathf.PI);
                colors[x] = new Color(1, 1, 1, alpha);
            }

            tex.SetPixels(colors);
            tex.Apply();

            _taperedGradientSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            return _taperedGradientSprite;
        }

        public static GameObject AddBorder(RectTransform parent, Color32 color, float top = 0f, float bottom = 0f, float left = 0f, float right = 0f)
        {
            if (top > 0) top = GetPixelCorrectThickness(top);
            if (bottom > 0) bottom = GetPixelCorrectThickness(bottom);
            if (left > 0) left = GetPixelCorrectThickness(left);
            if (right > 0) right = GetPixelCorrectThickness(right);
            // ----------------

            var containerGO = CreateUIObject("BorderGroup_Solid", typeof(RectTransform), typeof(LayoutElement));
            var containerRT = containerGO.GetComponent<RectTransform>();

            var le = containerGO.GetComponent<LayoutElement>();
            le.ignoreLayout = true;

            containerRT.SetParent(parent, false);
            containerRT.anchorMin = Vector2.zero;
            containerRT.anchorMax = Vector2.one;
            containerRT.sizeDelta = Vector2.zero;
            containerRT.anchoredPosition = Vector2.zero;

            RectTransform CreateBar(string name)
            {
                var go = CreateUIObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(containerRT, false);
                var img = go.GetComponent<Image>();
                img.sprite = GetSolidPointSprite();
                img.color = color;
                img.raycastTarget = false;
                return go.GetComponent<RectTransform>();
            }

            // Use Integer Pivots (0 or 1) to ensure we grow inward from the exact edge

            if (top > 0)
            {
                var t = CreateBar("Top");
                t.anchorMin = new Vector2(0, 1); t.anchorMax = new Vector2(1, 1);
                t.pivot = new Vector2(0.5f, 1); // Pivot Top
                t.anchoredPosition = Vector2.zero;
                t.sizeDelta = new Vector2(0, top);
            }

            if (bottom > 0)
            {
                var b = CreateBar("Bottom");
                b.anchorMin = new Vector2(0, 0); b.anchorMax = new Vector2(1, 0);
                b.pivot = new Vector2(0.5f, 0); // Pivot Bottom
                b.anchoredPosition = Vector2.zero;
                b.sizeDelta = new Vector2(0, bottom);
            }

            if (left > 0)
            {
                var l = CreateBar("Left");
                l.anchorMin = new Vector2(0, 0); l.anchorMax = new Vector2(0, 1);
                l.pivot = new Vector2(0, 0.5f); // Pivot Left
                l.anchoredPosition = Vector2.zero;
                l.sizeDelta = new Vector2(left, 0);
            }

            if (right > 0)
            {
                var r = CreateBar("Right");
                r.anchorMin = new Vector2(1, 0); r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 0.5f); // Pivot Right
                r.anchoredPosition = Vector2.zero;
                r.sizeDelta = new Vector2(right, 0);
            }

            return containerGO;
        }

        public static GameObject AddBorderTapered(RectTransform parent, Color32 color, float top = 0f, float bottom = 0f)
        {
            var containerGO = CreateUIObject("BorderGroup_Tapered", typeof(RectTransform), typeof(LayoutElement));
            var containerRT = containerGO.GetComponent<RectTransform>();
            containerGO.GetComponent<LayoutElement>().ignoreLayout = true;

            containerRT.SetParent(parent, false);
            containerRT.anchorMin = Vector2.zero;
            containerRT.anchorMax = Vector2.one;
            containerRT.offsetMin = Vector2.zero;
            containerRT.offsetMax = Vector2.zero;

            RectTransform MakeTaper(string name)
            {
                var go = CreateUIObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(containerRT, false);
                var img = go.GetComponent<Image>();
                img.sprite = GetTaperedSprite();
                img.color = color;
                img.raycastTarget = false;
                return go.GetComponent<RectTransform>();
            }

            var t = MakeTaper("Top");
            t.anchorMin = new Vector2(0, 1); t.anchorMax = new Vector2(1, 1);
            t.pivot = new Vector2(0.5f, 1);
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = new Vector2(0, top);

            if ((int)bottom > 0)
            {
                var b = MakeTaper("Bottom");
                b.anchorMin = new Vector2(0, 0); b.anchorMax = new Vector2(1, 0);
                b.pivot = new Vector2(0.5f, 0);
                b.anchoredPosition = Vector2.zero;
                b.sizeDelta = new Vector2(0, bottom);
            }
            return containerGO;
        }

        private static void AlignLabelVerticallyBetween(RectTransform labelRect, RectTransform bgMainRect, RectTransform submenuRect)
        {
            if (!labelRect || !bgMainRect || !submenuRect) return;

            var corners = new Vector3[4];

            bgMainRect.GetWorldCorners(corners);
            Vector3 bgTopCenter = (corners[1] + corners[2]) * 0.5f;

            submenuRect.GetWorldCorners(corners);
            Vector3 submenuTopCenter = (corners[1] + corners[2]) * 0.5f;

            float midY = (bgTopCenter.y + submenuTopCenter.y) * 0.5f;

            var labelWorldPos = labelRect.position;
            labelWorldPos.y = midY;
            labelRect.position = labelWorldPos;
        }

        //=========================== Events ===========================
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
                _globalCraftButtonText.text = "craft";
            }
        }

        private static void OnGlobalCraftButtonClicked()
        {
            if (_selectedChainData == null) return;
            StateController.RequestCraft(_selectedChainData);
        }

        private static void CraftablesForUIChanged(IReadOnlyList<CraftableEntry> craftables)
        {
            LastCraftables = craftables;
            if (!_skeletonBuilt) return;
            PopulateRecipeList(LastCraftables);
        }

        private static void ToggleRecipeRow(RecipeRowRuntime runtime)
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

        private static void ExpandRow(RecipeRowRuntime runtime)
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
                else _log.LogDebug("DropDownLayoutElement was null, cant expand row.");

                if (runtime.RowLayoutElement != null) runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight + targetHeight;
                else _log.LogDebug("RowLayoutElement was null, cant expand row.");

                if (_sharedDropdown != null) _sharedDropdown.OpenFor(runtime);
            }

            runtime.IsExpanded = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(runtime.RowTransform);
            PixelSnap(runtime.RowTransform);
            PixelSnap(runtime.RowTop);
            if (runtime.ArrowText != null) runtime.ArrowText.text = "v";
        }

        private static void CollapseRow(RecipeRowRuntime runtime)
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
            else _log.LogDebug("DropDownLayoutElement was null, cant retract row.");

            if (runtime.RowLayoutElement != null) runtime.RowLayoutElement.preferredHeight = runtime.CollapsedHeight;
            else _log.LogDebug("RowLayoutElement was null, cant retract row.");

            runtime.IsExpanded = false;
            LayoutRebuilder.ForceRebuildLayoutImmediate(runtime.RowTransform);
            PixelSnap(runtime.RowTransform);
            PixelSnap(runtime.RowTop);
            if (runtime.ArrowText != null) runtime.ArrowText.text = ">";
        }

        private static void OnFilterDropdownChanged(int index)
        {
            RecipeFilter.CurrentCategory = (RecipeFilterCategory)index;
            RefreshUIVisibility();
        }

        private static void RefreshUIVisibility()
        {
            RecipeFilter.ApplyFiltersToUI(_recipeRowUIs, _searchInputField?.text);

            if (_currentController != null)
            {
                if (_onIngredientsChangedMethod == null)
                {
                    _onIngredientsChangedMethod = typeof(CraftingController).GetMethod("OnIngredientsChanged",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                }
                _onIngredientsChangedMethod?.Invoke(_currentController, null);
            }
        }

        //=========================== Coroutines =========================== 
        internal static void PopulateRecipeList(IReadOnlyList<CraftableEntry> craftables)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!_skeletonBuilt || _recipeListContent == null || _runner == null) return;
            if (_activeBuildRoutine != null) _runner.StopCoroutine(_activeBuildRoutine);
            if (_cookbookRoot.activeInHierarchy) _activeBuildRoutine = _runner.StartCoroutine(PopulateRoutine(craftables, sw));
        }

        private static IEnumerator PopulateRoutine(IReadOnlyList<CraftableEntry> craftables, System.Diagnostics.Stopwatch sw)
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

            // Destroy all objects
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
                _log.LogInfo($"CraftUI: PopulateRecipeList (Empty) completed in {sw.ElapsedMilliseconds}ms");
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
            if (_runner == null || !_cookbookRoot.activeInHierarchy)
            {
                return;
            }

            bool isSameOwner = _cachedDropdownOwner == owner;
            bool hasContent = contentRoot.childCount > 0;

            if (isSameOwner && hasContent)
            {
                return;
            }

            if (_activeDropdownRoutine != null)
            {
                _runner.StopCoroutine(_activeDropdownRoutine);
            }

            _cachedDropdownOwner = null;
            _activeDropdownRoutine = _runner.StartCoroutine(PopulateDropdownRoutine(contentRoot, owner));
        }

        private static IEnumerator PopulateDropdownRoutine(RectTransform contentRoot, RecipeRowRuntime owner)
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

        internal sealed class ColorFaderRuntime : MonoBehaviour
        {
            private Graphic _targetGraphic;
            private Coroutine _activeRoutine;

            private void Awake()
            {
                _targetGraphic = GetComponent<Graphic>();
            }

            public void CrossFadeColor(Color targetColor, float duration, bool ignoreTimeScale = true)
            {
                if (_targetGraphic == null) return;
                if (_activeRoutine != null) StopCoroutine(_activeRoutine);
                if (_targetGraphic.color == targetColor) return;
                if (duration <= 0f || !gameObject.activeInHierarchy)
                {
                    _targetGraphic.color = targetColor;
                    return;
                }

                _activeRoutine = StartCoroutine(FadeRoutine(targetColor, duration, ignoreTimeScale));
            }

            private IEnumerator FadeRoutine(Color target, float duration, bool ignoreTimeScale)
            {
                Color start = _targetGraphic.color;
                float timer = 0f;

                while (timer < duration)
                {
                    timer += ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;

                    float t = Mathf.Clamp01(timer / duration);

                    _targetGraphic.color = Color.Lerp(start, target, t);
                    yield return null;
                }

                _targetGraphic.color = target;
                _activeRoutine = null;
            }

            private void OnDisable()
            {
                if (_activeRoutine != null) StopCoroutine(_activeRoutine);
                _activeRoutine = null;
            }
        }

        //=========================== Helpers =========================== 
        internal static void AttachReticleTo(RectTransform target)
        {
            if (!_selectionReticle || !target) return;

            _selectionReticle.SetParent(target, false);

            _selectionReticle.localPosition = Vector3.zero;
            _selectionReticle.localRotation = Quaternion.identity;
            _selectionReticle.localScale = Vector3.one;

            float outset = 4f;
            _selectionReticle.offsetMin = new Vector2(-outset, -outset);
            _selectionReticle.offsetMax = new Vector2(outset, outset);

            _selectionReticle.gameObject.SetActive(true);
            _selectionReticle.SetAsLastSibling();
        }

        internal static void RestoreReticleToSelection()
        {
            if (_selectedAnchor != null) _currentReticleTarget = _selectedAnchor;
            else if (_selectionReticle) _selectionReticle.gameObject.SetActive(false);
        }

        internal static bool IsReticleAttachedTo(Transform t)
        {
            if (t == null || _selectionReticle == null) return false;
            return _selectionReticle.parent == t;
        }

        private static bool AreEntriesSame(CraftableEntry a, CraftableEntry b)
        {
            if (a == null || b == null) return false;
            return a.ResultIndex == b.ResultIndex && a.ResultCount == b.ResultCount;
        }

        private static void DeselectCurrentPath()
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
                _globalCraftButtonText.text = "select a recipe";
                _globalCraftButtonText.color = new Color32(255, 255, 255, 255);
            }
        }

        internal static string GetEntryDisplayName(CraftableEntry entry)
        {
            if (entry == null)
            {
                return "Unknown Result";
            }
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

        private static Sprite GetIcon(int unifiedIndex)
        {
            if (_iconCache.TryGetValue(unifiedIndex, out var sprite)) return sprite;

            if (unifiedIndex < ItemCatalog.itemCount)
                sprite = ItemCatalog.GetItemDef((ItemIndex)unifiedIndex)?.pickupIconSprite;
            else
            {
                int equipIdx = unifiedIndex - ItemCatalog.itemCount;
                sprite = EquipmentCatalog.GetEquipmentDef((EquipmentIndex)equipIdx)?.pickupIconSprite;
            }

            if (sprite != null) _iconCache[unifiedIndex] = sprite;
            return sprite;
        }

        private static Sprite GetDroneIcon(DroneIndex droneIndex)
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

        internal struct RecipeRowUI
        {
            public CraftableEntry Entry;
            public GameObject RowGO;
        }

        private static float RoundToEven(float value)
        {
            float result = Mathf.Round(value);
            if (result % 2 != 0) result += 1f;
            return result;
        }

        internal static void PixelSnap(RectTransform rt)
        {
            if (!rt) return;

            var p = rt.anchoredPosition;
            p.x = Mathf.Round(p.x);
            p.y = Mathf.Round(p.y);
            rt.anchoredPosition = p;

            var omin = rt.offsetMin;
            var omax = rt.offsetMax;
            omin.x = Mathf.Round(omin.x);
            omin.y = Mathf.Round(omin.y);
            omax.x = Mathf.Round(omax.x);
            omax.y = Mathf.Round(omax.y);
            rt.offsetMin = omin;
            rt.offsetMax = omax;
        }

        private static float GetPixelCorrectThickness(float desiredPixels)
        {
            Canvas rootCanvas = _cookbookRoot ? _cookbookRoot.GetComponentInParent<Canvas>() : Object.FindObjectOfType<Canvas>();

            if (rootCanvas != null)
            {
                float pixelRatio = 1f / rootCanvas.scaleFactor;

                return Mathf.Round(desiredPixels) * pixelRatio;
            }

            return desiredPixels; // Fallback
        }


    }
}