using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class CraftUI
    {
        private static ManualLogSource _log;
        private static IReadOnlyList<CraftableEntry> _lastCraftables;
        private static readonly List<RecipeRowUI> _recipeRowUIs = new();
        private static bool _skeletonBuilt = false;
        private static CraftingController _currentController;

        private static GameObject _cookbookRoot;
        private static RectTransform _recipeListContent;
        private static TMP_InputField _searchInputField;
        private static RectTransform _templateIconSlot;
        private static Sprite _borderPixelSprite;


        // ---------------- Layout constants (normalized) ----------------
        static float  _panelWidth;
        static float _panelHeight;

        // CookBookPanel
        internal const float CookBookPanelPaddingTopNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingBottomNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingLeftNorm = 0f;
        internal const float CookBookPanelPaddingRightNorm = 0f;
        internal const float CookBookPanelElementSpacingNorm = 0.0159744409f; // vertical gap between SearchBar and RecipeList

        // SearchBar container
        internal const float SearchBarHeightNorm = 0.0798722045f;
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

        //------------------------- LifeCycle ----------------------------
        internal static void Init(ManualLogSource log)
        {
            _log = log;
            StateController.OnCraftablesForUIChanged += CraftablesForUIChanged;
        }

        internal static void Attach(CraftingController controller)
        {
            
            _currentController = controller;
            if (_cookbookRoot != null)
            {
                _log.LogDebug("CraftUI.Attach: already attached, skipping.");
                return;
            }
            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();
            if (!craftingPanel)
            {
                _log.LogWarning("CraftUI.Attach: CraftingPanel not found; cannot attach UI.");
                return;
            }

            // hierarchy pieces
            Transform bgContainerTr = craftingPanel.transform.Find("MainPanel/Juice/BGContainer");
            RectTransform bgRect = bgContainerTr.GetComponent<RectTransform>(); // contains bgmain
            RectTransform bgMainRect = bgContainerTr ? bgContainerTr.Find("BGMain")?.GetComponent<RectTransform>(): null;
            RectTransform labelRect = craftingPanel.transform.Find("MainPanel/Juice/LabelContainer")?.GetComponent<RectTransform>();
            RectTransform craftBgRect = bgContainerTr.Find("CraftingContainer/Background")?.GetComponent<RectTransform>();
            RectTransform craftRect = bgContainerTr.Find("CraftingContainer")?.GetComponent<RectTransform>();
            RectTransform submenuRect = bgContainerTr.Find("SubmenuContainer")?.GetComponent<RectTransform>();
            RectTransform invRect = bgContainerTr.Find("InventoryContainer")?.GetComponent<RectTransform>();

            if (!labelRect)
            {
                _log.LogError("CraftUI.Attach: LabelContainer RectTransform not found; aborting border setup.");
                return; // or just skip the border part
            }

            // Preserve current on-screen position
            labelRect.SetParent(bgMainRect, worldPositionStays: true);

            // base width references
            float invBaseWidth = invRect ? invRect.rect.width : 0f;
            float invBaseHeight = invRect ? invRect.rect.height : 0f;
            float craftBaseWidth = craftBgRect ? craftBgRect.rect.width : 0;
            float baseWidth = bgMainRect.rect.width;
            float baseHeight = bgMainRect.rect.height;
            float baseLabelWidth = labelRect.rect.width;

            //--------------------------------- base UI scaling -----------------------------------------
            var img = bgMainRect.GetComponent<Image>();
            var sprite = img ? img.sprite : null;
            float ppu = sprite ? sprite.pixelsPerUnit : 1f;
            float padLeft = sprite ? sprite.border.x / ppu : 0f;
            float padRight = sprite ? sprite.border.z / ppu : 0f;
            float padTop = sprite ? sprite.border.w / ppu : 0f;
            float padBottom = sprite ? sprite.border.y / ppu : 0f;
            float padHorizontal = padLeft + padRight;

            // Widen BG
            float widthscale = 1.8f;
            float newBgWidth = baseWidth * widthscale;
            
            // ensure even label margins
            float innerWidth = baseWidth - padHorizontal;
            float labelGap = (innerWidth - baseLabelWidth) * 0.5f;
            float newInnerWidth = newBgWidth - padHorizontal;
            float newLabelWidth = newInnerWidth - 2f * labelGap;

            // shrink vanilla ui margins
            float invWidthNew = invBaseWidth * 0.88f;
            float invHeightNew = invBaseHeight * 0.9f;
            float craftWidthNew = invWidthNew;

            // clamp cookbook width to 30% of total chef UI
            float cookbookWidth = Mathf.Clamp(newBgWidth * 0.3f, 260f, newInnerWidth - invBaseWidth);

            // clamp gap between cookbook and crafting panel to 5% of the total width
            float gap = Mathf.Clamp(newBgWidth * 0.05f, 20f, labelGap);

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
            float craftWidth = craftBgRect.rect.width;
            float sideMargin = (newInnerWidth - (craftWidth + gap + cookbookWidth)) * 0.5f;
            float centerCraftPanel = -newInnerWidth * 0.5f + sideMargin + craftWidth * 0.5f;

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
                if (hasBounds)
                {
                    contentBounds.Encapsulate(b);
                }
                else
                {
                    contentBounds = b;
                    hasBounds = true;
                }
            }

            // create CookBook panel in the new right-hand strip
            _cookbookRoot = new GameObject("CookBookPanel", typeof(RectTransform));
            _cookbookRoot.transform.SetParent(bgContainerTr, false);
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            cbRT.anchorMin = new Vector2(1f, 0.5f);
            cbRT.anchorMax = new Vector2(1f, 0.5f);
            cbRT.pivot = new Vector2(1f, 0.5f);

            // match vertical span of vanilla content
            cbRT.sizeDelta = new Vector2(cookbookWidth, contentBounds.size.y);

            /// ensure equal margins, same y position
            cbRT.anchoredPosition = new Vector2(-sideMargin, contentBounds.center.y);

            // replace old outline
            labelRect.GetComponent<Image>().enabled = false;
            CreateRectOverlay(labelRect, Color.clear, "_border");

            _log.LogInfo(
                $"CraftUI.Attach: CookBook panel attached. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}"
            );
            _panelWidth = cbRT.rect.width;
            _panelHeight = cbRT.rect.height;

            DumpHierarchy(bgContainerTr, 0);
            // TODO: set up timer for perf analysis here
            CookBookSkeleton(cbRT);

            if (_lastCraftables != null && _lastCraftables.Count > 0)
            {
                PopulateRecipeList(_lastCraftables);
            }
        }

        internal static void Detach()
        {
            if (_cookbookRoot != null)
            {
                UnityEngine.Object.Destroy(_cookbookRoot);
                _cookbookRoot = null;
                _log.LogInfo("CraftUI.Detach: CookBook panel destroyed.");
            }

            _currentController = null;
            _skeletonBuilt = false;
            _recipeListContent = null;
        }

        internal static void Shutdown()
        {
            StateController.OnCraftablesForUIChanged -= CraftablesForUIChanged;
            _searchInputField.onValueChanged.RemoveAllListeners();
        }

        //----------------------- Events --------------------------------
        private static void CraftablesForUIChanged(IReadOnlyList<CraftableEntry> craftables)
        {
            _lastCraftables = craftables;

            if (!_skeletonBuilt)
                return;

            PopulateRecipeList(_lastCraftables);
        }

        // TODO: set up timer for perf analysis here
        internal static void PopulateRecipeList(IReadOnlyList<CraftableEntry> craftables)
        {
            if (!_skeletonBuilt || _recipeListContent == null)
            {
                return;
            }

            for (int i = _recipeListContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(_recipeListContent.GetChild(i).gameObject);

            }

            _recipeRowUIs.Clear();

            if (craftables == null || craftables.Count == 0)
            {
                return;
            }

            foreach (var entry in craftables)
            {
                if (entry == null)
                {
                    _log.LogDebug($"entry {entry} is null");
                    continue; // safety for now
                }
                var rowGO = CreateRecipeRow(_recipeListContent, entry);
                _recipeRowUIs.Add(new RecipeRowUI
                {
                    Entry = entry,
                    RowGO = rowGO
                });
            }
        }

        //----------------------- Attach Helpers --------------------------------
        private static RectTransform GetTemplateIconSlot()
        {
            if (_templateIconSlot) return _templateIconSlot;

            // Find any existing item icon slot in the vanilla UI
            // Adjust this path to whatever you’ve inspected in the scene
            var inv = UnityEngine.Object.FindObjectOfType<ItemIcon>(); // or some known root
            if (inv)
            {
                _templateIconSlot = inv.GetComponent<RectTransform>();
            }

            return _templateIconSlot;
        }

        static void DumpHierarchy(Transform t, int depth = 0)
        {
            string indent = new string(' ', depth * 2);
            _log.LogInfo(indent + t.name);

            foreach (Transform child in t)
                DumpHierarchy(child, depth + 1);
        }

        private static Sprite GetBorderPixelSprite()
        {
            if (_borderPixelSprite)
                return _borderPixelSprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;

            tex.SetPixel(0, 0, Color.white);
            tex.Apply();

            _borderPixelSprite = Sprite.Create(
                tex,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect
            );
            _borderPixelSprite.name = "CookBookBorderPixel";
            return _borderPixelSprite;
        }

        private static void CreateRectOverlay(RectTransform src, Color color, string suffix)
        {
            var parent = src.parent;
            var go = new GameObject(src.name + suffix, typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);

            rt.anchorMin = src.anchorMin;
            rt.anchorMax = src.anchorMax;
            rt.pivot = src.pivot;
            rt.anchoredPosition = src.anchoredPosition;
            rt.sizeDelta = src.sizeDelta;

            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = new Color(color.r, color.g, color.b, 0.25f); // translucent

            AddRectBorder(rt, new Color(234, 235, 235), 2f);
        }

        private static void AddRectBorder(RectTransform rect, Color borderColor, float thicknessPixels)
        {
            var containerGO = new GameObject("Border", typeof(RectTransform));
            var container = containerGO.GetComponent<RectTransform>();
            container.SetParent(rect, false);

            // Match the rect exactly
            container.anchorMin = Vector2.zero;
            container.anchorMax = Vector2.one;
            container.pivot = new Vector2(0.5f, 0.5f);
            container.offsetMin = Vector2.zero;
            container.offsetMax = Vector2.zero;
            container.localScale = Vector3.one;

            // Make sure border is on top of the overlay fill
            container.SetAsLastSibling();

            var sprite = GetBorderPixelSprite();

            Image MakeSide(string name)
            {
                var go = new GameObject(name, typeof(RectTransform), typeof(Image));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(container, false);
                rt.localScale = Vector3.one;

                var img = go.GetComponent<Image>();
                img.sprite = sprite;
                img.color = borderColor;
                img.raycastTarget = false;
                return img;
            }
            // top
            {
                var img = MakeSide("Top");
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.offsetMin = new Vector2(0f, 0f);
                rt.offsetMax = new Vector2(0f, thicknessPixels);
            }
            // bottom
            {
                var img = MakeSide("Bottom");
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(0f, -thicknessPixels);
                rt.offsetMax = new Vector2(0f, 0f);
            }
            // left
            {
                var img = MakeSide("Left");
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.offsetMin = new Vector2(0f, 0f);
                rt.offsetMax = new Vector2(thicknessPixels * 4, 0f);
            }
            // right
            {
                var img = MakeSide("Right");
                var rt = img.rectTransform;
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.offsetMin = new Vector2(-thicknessPixels * 4, 0f);
                rt.offsetMax = new Vector2(0f, 0f);
            }
        }

        private static void AlignLabelVerticallyBetween(RectTransform labelRect, RectTransform bgMainRect, RectTransform submenuRect)
        {
            if (!labelRect || !bgMainRect || !submenuRect)
                return;

            var corners = new Vector3[4];

            // Top center of BGMain
            bgMainRect.GetWorldCorners(corners);
            // corners[1] = top-left, corners[2] = top-right
            Vector3 bgTopCenter = (corners[1] + corners[2]) * 0.5f;

            // Top center of SubmenuContainer
            submenuRect.GetWorldCorners(corners);
            Vector3 submenuTopCenter = (corners[1] + corners[2]) * 0.5f;

            // Midpoint in world space
            float midY = (bgTopCenter.y + submenuTopCenter.y) * 0.5f;

            // Keep X/Z, only adjust Y
            var labelWorldPos = labelRect.position;
            labelWorldPos.y = midY;
            labelRect.position = labelWorldPos;
        }


        //----------------------- Cookbook Builders ------------------------------
        internal static void CookBookSkeleton(RectTransform cookbookRoot)
        {
            if (!cookbookRoot) return;

            // Clear any leftovers if you re-enter the UI
            for (int i = cookbookRoot.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(cookbookRoot.GetChild(i).gameObject);
            }

            //--------------------------- CookBook Border ----------------------------------
            GameObject frameClone = UnityEngine.Object.Instantiate(
                    UnityEngine.Object.FindObjectOfType<CraftingPanel>().transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background").gameObject,
                    _cookbookRoot.transform
                );
            frameClone.name = "CookBookBorder";
            var borderRect = frameClone.GetComponent<RectTransform>();
            borderRect.anchorMin = new Vector2(0f, 0f);
            borderRect.anchorMax = new Vector2(1f, 1f);
            borderRect.pivot = new Vector2(0.5f, 0.5f);
            borderRect.anchoredPosition = Vector2.zero;
            borderRect.sizeDelta = Vector2.zero;

            // strip out the crafting contents
            foreach (Transform child in frameClone.transform)
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
            // ensure stays top level
            borderRect.SetAsLastSibling();

            //----------------------------- Add Top Level Elements ------------------------------

            // Internal padding / spacing inside the cookbook panel
            float padTopPx = CookBookPanelPaddingTopNorm * _panelHeight;
            float padBottomPx = CookBookPanelPaddingBottomNorm * _panelHeight;
            float padLeftPx = CookBookPanelPaddingLeftNorm * _panelWidth;
            float padRightPx = CookBookPanelPaddingRightNorm * _panelWidth;
            float spacingPx = CookBookPanelElementSpacingNorm * _panelHeight;

            float searchBarHeightPx = SearchBarHeightNorm * _panelHeight;

            // Total usable vertical region inside the panel padding
            float innerHeight = _panelHeight - padTopPx - padBottomPx;

            // Remaining space for the RecipeList after SearchBar + spacing
            float recipeListHeightPx = innerHeight - searchBarHeightPx - spacingPx;
            if (recipeListHeightPx < 0f)
                recipeListHeightPx = 0f;

            //------------------------ SearchBarContainer ------------------------
            GameObject searchGO = new GameObject("SearchBarContainer", typeof(RectTransform));
            var searchRect = searchGO.GetComponent<RectTransform>();
            searchRect.SetParent(cookbookRoot, false);

            // stretch horizontally with fixed height
            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(1f, 1f);
            searchRect.pivot = new Vector2(0.5f, 1f);

            searchRect.sizeDelta = new Vector2(0f, searchBarHeightPx);
            searchRect.anchoredPosition = new Vector2(0f, -padTopPx);

            // internal horizontal padding from panel
            var sbOffsetMin = searchRect.offsetMin;
            var sbOffsetMax = searchRect.offsetMax;
            sbOffsetMin.x = padLeftPx;
            sbOffsetMax.x = -padRightPx;
            searchRect.offsetMin = sbOffsetMin;
            searchRect.offsetMax = sbOffsetMax;

            //------------------------ SearchBar internals ------------------------
            GameObject inputGO = new GameObject("SearchInput", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));

            var inputRect = inputGO.GetComponent<RectTransform>();
            var bgImage = inputGO.GetComponent<Image>();
            TMP_InputField searchInput = inputGO.GetComponent<TMP_InputField>();
            _searchInputField = searchInput;

            inputRect.SetParent(searchRect, false);

            // Fill the entire SearchBarContainer
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(1f, 1f);
            inputRect.pivot = new Vector2(0.5f, 0.5f);
            inputRect.anchoredPosition = Vector2.zero;
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;

            //----------------------------------- SearchBar Cosmetics -------------------------------------------
            // Background: 40% opaque black         
            bgImage = searchInput.GetComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.4f);

            // Bottom border: 20% opaque white, 1px
            GameObject borderGO = new GameObject("BottomBorder", typeof(RectTransform), typeof(Image));
            var borderRT = borderGO.GetComponent<RectTransform>();
            var borderImg = borderGO.GetComponent<Image>();

            float borderThicknessPx = Mathf.Max(1f, SearchBarBottomBorderThicknessNorm * _panelHeight);
            borderRT.SetParent(inputRect, false);
            borderRT.anchorMin = new Vector2(0f, 0f);
            borderRT.anchorMax = new Vector2(1f, 0f);
            borderRT.pivot = new Vector2(0.5f, 0f);
            borderRT.anchoredPosition = Vector2.zero;
            borderRT.sizeDelta = new Vector2(0f, borderThicknessPx);
            borderImg.color = new Color(1f, 1f, 1f, 0.2f);

            borderImg.color = new Color(1f, 1f, 1f, 0.2f);
            borderImg.raycastTarget = false;

            // ---------------- Setup Text Fields ----------------
            GameObject textAreaGO = new GameObject("Text Area", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var textAreaRT = textAreaGO.GetComponent<RectTransform>();
            var textAreaImg = textAreaGO.GetComponent<Image>();
            textAreaRT.SetParent(inputRect, false);
            textAreaRT.anchorMin = new Vector2(0f, 0f);
            textAreaRT.anchorMax = new Vector2(1f, 1f);
            textAreaRT.pivot = new Vector2(0.5f, 0.5f);
            textAreaRT.offsetMin = new Vector2(0f, 0f);
            textAreaRT.offsetMax = new Vector2(0f, 0f);

            textAreaImg.color = new Color(0f, 0f, 0f, 0f);
            textAreaImg.raycastTarget = false;

            GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRT = textGO.GetComponent<RectTransform>();
            var textTMP = textGO.GetComponent<TextMeshProUGUI>();

            textRT.SetParent(textAreaRT, false);
            textRT.anchorMin = new Vector2(0f, 0f);
            textRT.anchorMax = new Vector2(1f, 1f);
            textRT.pivot = new Vector2(0.5f, 0.5f);
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            textTMP.fontSize = 20f;
            textTMP.alignment = TextAlignmentOptions.Center;
            textTMP.color = Color.white;
            textTMP.raycastTarget = true;
            textTMP.enableWordWrapping = false;

            // Placeholder text
            GameObject phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            var phRT = phGO.GetComponent<RectTransform>();
            var placeholderTMP = phGO.GetComponent<TextMeshProUGUI>();

            phRT.SetParent(textAreaRT, false);
            phRT.anchorMin = new Vector2(0f, 0f);
            phRT.anchorMax = new Vector2(1f, 1f);
            phRT.pivot = new Vector2(0.5f, 0.5f);
            phRT.offsetMin = Vector2.zero;
            phRT.offsetMax = Vector2.zero;

            placeholderTMP.text = "search";
            placeholderTMP.fontSize = 20f;
            placeholderTMP.alignment = TextAlignmentOptions.Center;
            placeholderTMP.color = new Color(1f, 1f, 1f, 0.5f);
            placeholderTMP.raycastTarget = false; // don't steal clicks

            // ---------------- Wire TMP_InputField ----------------
            searchInput.textViewport = textAreaRT;
            searchInput.textComponent = textTMP;
            searchInput.placeholder = placeholderTMP;

            searchInput.text = string.Empty;
            searchInput.interactable = true;
            searchInput.readOnly = false;

            searchInput.caretBlinkRate = 0.5f;
            searchInput.caretWidth = 2;
            searchInput.customCaretColor = true;
            searchInput.caretColor = Color.white;
            searchInput.selectionColor = new Color(0.6f, 0.8f, 1f, 0.35f);

            searchInput.contentType = TMP_InputField.ContentType.Standard;
            searchInput.lineType = TMP_InputField.LineType.SingleLine;

            // search callback for later filtering
            searchInput.onValueChanged.AddListener(OnSearchTextChanged);

            //------------------------ RecipeListContainer ------------------------
            GameObject listGO = new GameObject("RecipeListContainer", typeof(RectTransform));
            var listRect = listGO.GetComponent<RectTransform>();
            listRect.SetParent(cookbookRoot, false);

            // stretch horizontally, top-anchored, fixed vertical size
            listRect.anchorMin = new Vector2(0f, 1f);
            listRect.anchorMax = new Vector2(1f, 1f);
            listRect.pivot = new Vector2(0.5f, 1f);

            listRect.sizeDelta = new Vector2(0f, recipeListHeightPx);

            // place directly below the search bar + spacing
            float recipeListTopOffset = padTopPx + searchBarHeightPx + spacingPx;
            listRect.anchoredPosition = new Vector2(0f, -recipeListTopOffset);

            // internal horizontal padding from panel
            var rlOffsetMin = listRect.offsetMin;
            var rlOffsetMax = listRect.offsetMax;
            rlOffsetMin.x = padLeftPx;
            rlOffsetMax.x = -padRightPx;
            listRect.offsetMin = rlOffsetMin;
            listRect.offsetMax = rlOffsetMax;

            //------------------------ RecipeListContainer Internals ------------------------
            var scroll = listRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = RowTopHeightNorm * _panelHeight * 0.5f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.16f;
            scroll.elasticity = 0.1f;

            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D), typeof(Image));
            var viewportRT = viewportGO.GetComponent<RectTransform>();

            viewportRT.SetParent(listRect, false);
            viewportRT.anchorMin = new Vector2(0f, 0f);
            viewportRT.anchorMax = new Vector2(1f, 1f);
            viewportRT.pivot = new Vector2(0.5f, 0.5f);
            viewportRT.anchoredPosition = Vector2.zero;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;
            scroll.viewport = viewportRT;

            // enable raycasts for scrolling
            var viewportImg = viewportGO.GetComponent<Image>();
            viewportImg.color = new Color(0f, 0f, 0f, 0f);
            viewportImg.raycastTarget = true;

            GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));

            var contentRT = contentGO.GetComponent<RectTransform>();
            contentRT.SetParent(viewportRT, false);
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.offsetMin = Vector2.zero;
            contentRT.offsetMax = Vector2.zero;
            scroll.content = contentRT;
            _recipeListContent = contentRT;

            float recipeListLeftPx = RecipeListLeftPaddingNorm * _panelWidth;
            float recipeListRightPx = RecipeListRightPaddingNorm * _panelWidth;
            float recipeListVertPadPx = RecipeListVerticalPaddingNorm * _panelHeight;
            float recipeListSpacingPx = RecipeListElementSpacingNorm * _panelHeight;

            // rows stacked from top, padded
            var vLayout = contentGO.GetComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(
                Mathf.RoundToInt(recipeListLeftPx),
                Mathf.RoundToInt(recipeListRightPx),
                Mathf.RoundToInt(recipeListVertPadPx),
                Mathf.RoundToInt(recipeListVertPadPx)
            );
            vLayout.spacing = recipeListSpacingPx;
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

        internal static GameObject CreateRecipeRow(RectTransform parent, CraftableEntry entry)
        {
            // ---------------- Size Constants ----------------
            float topPadPx = RowTopTopPaddingNorm * _panelHeight;
            float bottomPadPx = RowTopBottomPaddingNorm * _panelHeight;
            float elementSpacingPx = RowTopElementSpacingNorm * _panelWidth;
            float rowTopHeightPx = RowTopHeightNorm * _panelHeight;
            float innerHeight = rowTopHeightPx - (topPadPx + bottomPadPx);
            float metaWidthPx = MetaDataColumnWidthNorm * _panelWidth;
            float metaSpacingPx = MetaDataElementSpacingNorm * _panelHeight;
            float dropDownArrowSize = DropDownArrowSizeNorm * _panelHeight;
            float textSize = textSizeNorm * _panelHeight;

            // ---------------- RecipeRow root ----------------
            GameObject rowGO = new GameObject("RecipeRow", typeof(RectTransform), typeof(LayoutElement));
            var rowRT = rowGO.GetComponent<RectTransform>();
            var rowLayoutEl = rowGO.GetComponent<LayoutElement>();

            rowRT.SetParent(parent, false);
            rowRT.anchorMin = new Vector2(0f, 1f);
            rowRT.anchorMax = new Vector2(1f, 1f);
            rowRT.pivot = new Vector2(0.5f, 1f);
            rowRT.anchoredPosition = Vector2.zero;
            rowRT.offsetMin = Vector2.zero;
            rowRT.offsetMax = Vector2.zero;

            rowLayoutEl.preferredHeight = rowTopHeightPx;
            rowLayoutEl.flexibleHeight = 0f;

            // ---------------- RowTop ----------------
            GameObject rowTopGO = new GameObject("RowTop", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var rowTopRT = rowTopGO.GetComponent<RectTransform>();
            var rowTopH = rowTopGO.GetComponent<HorizontalLayoutGroup>();

            rowTopRT.SetParent(rowRT, false);
            rowTopRT.anchorMin = new Vector2(0f, 0f);
            rowTopRT.anchorMax = new Vector2(1f, 1f);
            rowTopRT.pivot = new Vector2(0.5f, 0.5f);
            rowTopRT.anchoredPosition = Vector2.zero;
            rowTopRT.offsetMin = Vector2.zero;
            rowTopRT.offsetMax = Vector2.zero;

            rowTopH.spacing = elementSpacingPx;
            rowTopH.childAlignment = TextAnchor.MiddleLeft;
            rowTopH.childControlHeight = true;
            rowTopH.childControlWidth = true;
            rowTopH.childForceExpandHeight = true;
            rowTopH.childForceExpandWidth = false;
            rowTopH.padding = new RectOffset(
        0,
        0,
        Mathf.RoundToInt(topPadPx),
        Mathf.RoundToInt(bottomPadPx)
        );

            float borderThickness = 1f;
            GameObject topBorder = new GameObject("TopBorder", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var topRT = topBorder.GetComponent<RectTransform>();
            var topImg = topBorder.GetComponent<Image>();
            var topLE = topBorder.GetComponent<LayoutElement>();

            topRT.SetParent(rowTopRT, false);
            topRT.anchorMin = new Vector2(0f, 1f);
            topRT.anchorMax = new Vector2(1f, 1f);
            topRT.pivot = new Vector2(0.5f, 1f);
            topRT.offsetMin = new Vector2(0f, -borderThickness);
            topRT.offsetMax = new Vector2(0f, 0f);
            topImg.color = new Color32(209, 209, 210, 255);
            topImg.raycastTarget = false;
            topLE.ignoreLayout = true;

            GameObject bottomBorder = new GameObject("BottomBorder", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var bottomRT = bottomBorder.GetComponent<RectTransform>();
            var bottomImg = bottomBorder.GetComponent<Image>();
            var bottomLE = bottomBorder.GetComponent<LayoutElement>();

            bottomRT.SetParent(rowTopRT, false);
            bottomRT.anchorMin = new Vector2(0f, 0f);
            bottomRT.anchorMax = new Vector2(1f, 0f);
            bottomRT.pivot = new Vector2(0.5f, 0f);
            bottomRT.offsetMin = new Vector2(0f, 0f);
            bottomRT.offsetMax = new Vector2(0f, borderThickness);
            bottomImg.color = new Color32(209, 209, 210, 255);
            bottomImg.raycastTarget = false;
            bottomLE.ignoreLayout =true;

            // ---------------- DropDown ----------------
            GameObject dropGO = new GameObject("DropDown", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var dropBtn = dropGO.GetComponent<Button>();
            var dropRT = dropGO.GetComponent<RectTransform>();
            var dropLE = dropGO.GetComponent<LayoutElement>();
            var dropImg = dropGO.GetComponent<Image>();

            dropRT.SetParent(rowTopRT, false);

            // Square container, size dictated by height of row
            dropLE.preferredWidth = innerHeight;
            dropLE.preferredHeight = innerHeight;
            dropLE.flexibleWidth = 0f;
            dropLE.flexibleHeight = 0f;

            dropImg.color = Color.clear;
            dropImg.raycastTarget = true; // TODO: allow user to click anywhere in rowtop to extend/retract the pathscontainer, without impeding vertical scroll

            // Arrow child
            GameObject arrowGO = new GameObject("Arrow", typeof(RectTransform), typeof(TextMeshProUGUI));
            var arrowRT = arrowGO.GetComponent<RectTransform>();
            var arrowTMP = arrowGO.GetComponent<TextMeshProUGUI>();

            arrowRT.SetParent(dropRT, false);
            arrowRT.anchorMin = new Vector2(0f, 0f);
            arrowRT.anchorMax = new Vector2(1f, 1f);
            arrowRT.pivot = new Vector2(0.5f, 0.5f);
            arrowRT.offsetMin = Vector2.zero;
            arrowRT.offsetMax = Vector2.zero;

            arrowTMP.text = ">";
            arrowTMP.alignment = TextAlignmentOptions.Center;
            arrowTMP.fontSize = dropDownArrowSize;
            arrowTMP.color = Color.white;
            arrowTMP.raycastTarget = false;

            // (TODO: add listener to dropBtn.onClick to toggle PathsContainer)

            // ---------------- IconBackground (fixed square) ----------------
            GameObject iconBGGO = new GameObject("IconBackground", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var iconBGRT = iconBGGO.GetComponent<RectTransform>();
            var iconBGImg = iconBGGO.GetComponent<Image>();
            var iconBGLE = iconBGGO.GetComponent<LayoutElement>();

            iconBGRT.SetParent(rowTopRT, false);

            iconBGLE.preferredHeight = innerHeight;
            iconBGLE.preferredWidth = iconBGLE.preferredHeight;

            iconBGImg.color = new Color32(26, 22, 23, 255);
            iconBGImg.raycastTarget = false;

            // ensure square
            var bgAspect = iconBGGO.AddComponent<AspectRatioFitter>();
            bgAspect.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
            bgAspect.aspectRatio = 1f;

            GameObject iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRT = iconGO.GetComponent<RectTransform>();
            var iconImg = iconGO.GetComponent<Image>();

            iconRT.SetParent(iconBGRT, false);
            iconRT.anchorMin = Vector2.zero;
            iconRT.anchorMax = Vector2.one;
            iconRT.offsetMin = Vector2.zero;
            iconRT.offsetMax = Vector2.zero;

            iconImg.raycastTarget = false;
            iconImg.preserveAspect = true;

            Sprite iconSprite = GetEntryIcon(entry);
            if (iconSprite != null)
            {
                iconImg.sprite = iconSprite;
                iconImg.color = Color.white;
            }
            else
            {
                iconImg.color = new Color(1f, 1f, 1f, 0.1f);
            }

            // Item label
            GameObject labelGO = new GameObject("ItemLabel", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            var labelRT = labelGO.GetComponent<RectTransform>();
            var labelTMP = labelGO.GetComponent<TextMeshProUGUI>();
            var labelLE = labelGO.GetComponent<LayoutElement>();

            labelRT.SetParent(rowTopRT, false);
            labelLE.flexibleWidth = 1f;
            labelLE.preferredHeight = innerHeight;

            labelTMP.text = GetEntryDisplayName(entry);
            labelTMP.fontSize = textSize;
            labelTMP.enableWordWrapping = false;
            labelTMP.alignment = TextAlignmentOptions.Center;
            labelTMP.color = GetEntryColor(entry);
            labelTMP.raycastTarget = false;

            // ---------------- MetaData ----------------
            GameObject metaGO = new GameObject("MetaData", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var metaRT = metaGO.GetComponent<RectTransform>();
            var metaV = metaGO.GetComponent<VerticalLayoutGroup>();
            var metaLE = metaGO.GetComponent<LayoutElement>();

            metaRT.SetParent(rowTopRT, false);

            metaLE.preferredWidth = metaWidthPx;

            metaV.spacing = metaSpacingPx;
            metaV.childAlignment = TextAnchor.MiddleRight;
            metaV.childForceExpandHeight = false;

            // MinDepth line
            GameObject depthGO = new GameObject("MinimumDepth", typeof(RectTransform), typeof(TextMeshProUGUI));
            var depthRT = depthGO.GetComponent<RectTransform>();
            var depthTMP = depthGO.GetComponent<TextMeshProUGUI>();

            depthRT.SetParent(metaRT, false);
            depthRT.anchorMin = new Vector2(0f, 0.5f);
            depthRT.anchorMax = new Vector2(1f, 0.5f);
            depthRT.pivot = new Vector2(0.5f, 0.5f);

            depthTMP.text = $"Depth: {entry.MinDepth}";
            depthTMP.fontSize = 16f;
            depthTMP.alignment = TextAlignmentOptions.MidlineRight;
            depthTMP.color = Color.white;
            depthTMP.raycastTarget = false;

            // AvailablePaths line
            GameObject pathsGO = new GameObject("AvailablePaths", typeof(RectTransform), typeof(TextMeshProUGUI));
            var pathsRT = pathsGO.GetComponent<RectTransform>();
            var pathsTMP = pathsGO.GetComponent<TextMeshProUGUI>();

            pathsRT.SetParent(metaRT, false);
            pathsRT.anchorMin = new Vector2(0f, 0.5f);
            pathsRT.anchorMax = new Vector2(1f, 0.5f);
            pathsRT.pivot = new Vector2(0.5f, 0.5f);

            int pathCount = entry.Chains?.Count ?? 0;
            pathsTMP.text = $"Paths: {pathCount}";
            pathsTMP.fontSize = 16f;
            pathsTMP.alignment = TextAlignmentOptions.MidlineRight;
            pathsTMP.color = Color.white;
            pathsTMP.raycastTarget = false;

            return rowGO;
        }

        // ------------------------ Helpers  ------------------------------
        private static void OnSearchTextChanged(string text)
        {
            if (_recipeRowUIs == null)
                return;

            if (text == null)
            {
                text = string.Empty;
            }

            text = text.Trim().ToLowerInvariant();

            foreach (var row in _recipeRowUIs)
            {

                if (row.RowGO == null)
                {
                    _log.LogDebug($"row {row.RowGO.name} is null");
                    continue;
                }
                bool matches = EntryMatchesSearch(row.Entry, text);
                row.RowGO.SetActive(matches);
            }
        }
        private static bool EntryMatchesSearch(CraftableEntry entry, string term)
        {
            if (string.IsNullOrEmpty(term))
            {
                return true;
            }
            if (entry == null)
            {
                return false; 
            }

            string name = GetEntryDisplayName(entry);
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            return name.ToLowerInvariant().Contains(term);
        }

        private static string GetEntryDisplayName(CraftableEntry entry)
        {
            if (entry == null)
            {
                _log.LogDebug($"entry {entry} is null");
                return "Unknown Result";
            }
            switch (entry.ResultKind)
            {
                case RecipeResultKind.Item:
                    var idef = ItemCatalog.GetItemDef(entry.ResultItem);
                    if (idef == null || string.IsNullOrEmpty(idef.nameToken))
                    {
                        return $"bad Item {entry.ResultItem}";
                    }
                    return Language.GetString(idef.nameToken);
                case RecipeResultKind.Equipment:
                    var edef = EquipmentCatalog.GetEquipmentDef(entry.ResultEquipment);
                    if (edef == null || string.IsNullOrEmpty(edef.nameToken))
                    {
                        return $"bad Equipment {entry.ResultEquipment}";
                    }
                    return Language.GetString(edef.nameToken);
                default:
                    return "Unknown Result";
            }
        }

        private static Sprite GetEntryIcon(CraftableEntry entry)
        {
            switch (entry.ResultKind)
            {
                case RecipeResultKind.Item:
                    {
                        var itemDef = ItemCatalog.GetItemDef(entry.ResultItem);
                        return itemDef != null ? itemDef.pickupIconSprite : null;
                    }
                case RecipeResultKind.Equipment:
                    {
                        var eqDef = EquipmentCatalog.GetEquipmentDef(entry.ResultEquipment);
                        return eqDef != null ? eqDef.pickupIconSprite : null;
                    }
                default:
                    return null;
            }
        }

        private static int GetEntryStackCount(CraftableEntry entry)
        {
            if (entry == null || entry.Chains == null || entry.Chains.Count == 0)
                return 1;

            var chain = entry.Chains[0];
            if (chain.Steps == null || chain.Steps.Count == 0)
                return 1;

            var finalStep = chain.Steps[chain.Steps.Count - 1];
            return Mathf.Max(1, finalStep.ResultCount);
        }


        private static Color GetEntryColor(CraftableEntry entry)
        {
            PickupIndex pickupIndex = PickupIndex.none;

            switch (entry.ResultKind)
            {
                case RecipeResultKind.Item:
                    pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultItem);
                    break;

                case RecipeResultKind.Equipment:
                    pickupIndex = PickupCatalog.FindPickupIndex(entry.ResultEquipment);
                    break;

                default:
                    return Color.white;
            }

            if (!pickupIndex.isValid)
                return Color.white;

            var pickupDef = PickupCatalog.GetPickupDef(pickupIndex);
            if (pickupDef == null)
            {
                return Color.white;
            }

            return pickupDef.baseColor;
        }

        private struct RecipeRowUI
        {
            public CraftableEntry Entry;
            public GameObject RowGO;
        }

    }
}