using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static void AcquireTemplates(CraftingPanel craftingPanel, RectTransform cbRT)
        {


            CookBookSkeleton(cbRT);
            EnsureResultSlotArtTemplates(craftingPanel);
            EnsureIngredientSlotTemplate();
            BuildRecipeRowTemplate();
            BuildPathRowTemplate();
            BuildSharedDropdown();
            BuildSharedHoverRect();

            _skeletonBuilt = true;
        }



        internal static void CookBookSkeleton(RectTransform cookbookRoot)
        {
            if (!cookbookRoot) return;

            // Clear any leftovers if you re-enter the UI 
            for (int i = cookbookRoot.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(cookbookRoot.GetChild(i).gameObject);

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

            // --- Search Input ---
            GameObject inputGO = CreateUIObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var inputRect = inputGO.GetComponent<RectTransform>();
            inputRect.SetParent(searchRect, false);
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = new Vector2(0.75f, 1f);

            float borderThickness = Mathf.Max(1f, SearchBarBottomBorderThicknessNorm * _panelHeight);
            inputRect.offsetMin = new Vector2(0f, borderThickness);
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

            placeholderTMP.text = "Search";
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
            cycleRT.anchorMin = new Vector2(0.8f, 0f);
            cycleRT.anchorMax = new Vector2(1f, 1f);
            cycleRT.offsetMin = new Vector2(0f, borderThickness);
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
            labelTMP.richText = true;
            labelTMP.fontSize = 20f;
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

            AddBorderTapered(searchRect, new Color32(209, 209, 210, 200), bottom: borderThickness);
            AddBorder(labelGO.GetComponent<RectTransform>(), new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);

            // ------------------------ Footer ------------------------
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

            // craft button
            GameObject craftBtnGO = CreateUIObject("GlobalCraftButton", typeof(RectTransform), typeof(Image), typeof(Button));
            var craftBtnRT = craftBtnGO.GetComponent<RectTransform>();
            var craftBtnImg = craftBtnGO.GetComponent<Image>();
            var craftBtn = craftBtnGO.GetComponent<Button>();

            craftBtnRT.SetParent(footerRT, false);
            craftBtnRT.anchorMin = Vector2.zero;
            craftBtnRT.anchorMax = new Vector2(0.8f, 1f);
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
            btnTextTMP.fontSize = footerHeightPx * 0.45f;
            btnTextTMP.color = new Color32(100, 100, 100, 255);

            _globalCraftButton = craftBtn;
            _globalCraftButtonText = btnTextTMP;
            _globalCraftButtonImage = craftBtnImg;
            _globalCraftButton.onClick.AddListener(OnGlobalCraftButtonClicked);

            // --- Repeat Craft Box ---
            GameObject repeatGO = CreateUIObject("RepeatInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            var repeatRT = repeatGO.GetComponent<RectTransform>();
            repeatRT.SetParent(footerRT, false);
            repeatRT.anchorMin = new Vector2(0.8f, 0f);
            repeatRT.anchorMax = Vector2.one;
            repeatRT.sizeDelta = Vector2.zero;

            repeatGO.GetComponent<Image>().color = new Color32(20, 20, 20, 255);
            _repeatInputField = repeatGO.GetComponent<TMP_InputField>();
            _repeatInputField.characterValidation = TMP_InputField.CharacterValidation.Digit;

            var RepeatTextAreaRT = CreateInternal("Text Area", repeatRT, typeof(RectMask2D));

            var repeatPhTMP = CreateInternal("Placeholder", RepeatTextAreaRT, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            repeatPhTMP.text = "1";
            repeatPhTMP.fontSize = footerHeightPx * 0.45f;
            repeatPhTMP.alignment = TextAlignmentOptions.Center;
            repeatPhTMP.color = new Color(1f, 1f, 1f, 0.3f);
            repeatPhTMP.raycastTarget = false;

            var repeatTMP = CreateInternal("Text", RepeatTextAreaRT, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
            repeatTMP.fontSize = footerHeightPx * 0.45f;
            repeatTMP.alignment = TextAlignmentOptions.Center;
            repeatTMP.color = Color.white;

            // Wiring
            _repeatInputField.textViewport = RepeatTextAreaRT;
            _repeatInputField.placeholder = repeatPhTMP;
            _repeatInputField.textComponent = repeatTMP;

            _repeatInputField.text = string.Empty;
            _repeatInputField.onEndEdit.AddListener(OnRepeatInputEndEdit);

            AddBorder(craftBtnRT, new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);
            AddBorder(footerRT, new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);

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
        }

        //==================== Prefabs ====================
        internal static void BuildRecipeRowTemplate()
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
            GameObject labelGO = CreateUIObject("Label", typeof(RectTransform), typeof(RoR2.UI.HGTextMeshProUGUI));
            var labelTMP = labelGO.GetComponent<RoR2.UI.HGTextMeshProUGUI>();
            var labelRT = labelGO.GetComponent<RectTransform>();

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

        internal static void BuildPathRowTemplate()
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

        internal static void BuildSharedDropdown()
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
            int paddingHorizontal = Mathf.RoundToInt(PathsContainerPaddingNorm * _panelWidth);
            vlg.padding = new RectOffset(paddingHorizontal, paddingHorizontal, paddingVertical, paddingVertical);

            contentGO.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sharedDropdown.ScrollRect = scroll;
            _sharedDropdown.Content = contentRT;
            _sharedDropdown.Background = drawerRT;
            drawerGO.transform.SetParent(_cookbookRoot.transform, false);
            drawerGO.SetActive(false);
        }

        internal static void BuildSharedHoverRect()
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

        internal static void EnsureResultSlotArtTemplates(CraftingPanel craftingPanel)
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

        internal static void EnsureIngredientSlotTemplate()
        {
            if (_ingredientSlotTemplate != null) return;
            _ingredientSlotTemplate = BuildIngredientTemplate("PhysicalSlot", new Color32(16, 8, 10, 255));
            _droneSlotTemplate = BuildIngredientTemplate("DroneSlot", new Color32(20, 50, 45, 255));
            _tradeSlotTemplate = BuildIngredientTemplate("TradeSlot", new Color32(75, 65, 25, 255));
        }

        internal static GameObject BuildIngredientTemplate(string name, Color32 bgColor)
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

    }
}
