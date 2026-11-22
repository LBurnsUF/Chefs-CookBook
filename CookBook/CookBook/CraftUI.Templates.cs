using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal sealed partial class CraftUI
    {
        internal void AcquireTemplates(CraftingPanel craftingPanel, RectTransform cbRT)
        {
            _craftingPanel = craftingPanel;
            _vanillaTopLayer = craftingPanel.confirmButton ? craftingPanel.confirmButton.requiredTopLayer : null;
            _vanillaButtonTemplate = FindVanillaButtonTemplate(craftingPanel);
            if (!_vanillaButtonTemplate) _log.LogError("CraftUI: no vanilla HGButton found in CraftingPanel, buttons fall back to flat style.");

            CookBookSkeleton(cbRT);
            InstallGamepadBindings(_cookbookRoot);
            EnsureResultSlotArtTemplates(craftingPanel);
            EnsureIngredientSlotTemplate();
            EnsureSlotPool();
            EnsureRecipeRowTemplate();
            EnsurePathRowTemplate();
            EnsureSharedDropdown();
            EnsureSharedHoverRect();

            _skeletonBuilt = true;
        }

        internal void CookBookSkeleton(RectTransform cookbookRoot)
        {
            if (!cookbookRoot) return;

            // Clear any leftovers if you re-enter the UI 
            for (int i = cookbookRoot.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(cookbookRoot.GetChild(i).gameObject);

            var panelGO = InstantiateBundleTemplate("Panel");
            panelGO.SetActive(true);
            BindPanelSkeleton(panelGO.GetComponent<PanelView>());
        }

        // vanilla button clones and listeners can't live in the prefab
        private void BindPanelSkeleton(PanelView view)
        {
            _recipeListContent = view.ListContent;

            // whole rows only, it settles on row tops instead of drifting
            var scroll = _recipeListContent.GetComponentInParent<ScrollRect>();
            scroll.inertia = false;
            var snap = scroll.gameObject.AddComponent<RowSnapScroll>();
            snap.Scroll = scroll;
            snap.PadTop = _recipeListContent.GetComponent<VerticalLayoutGroup>().padding.top;

            _searchInputField = view.SearchInput;
            _searchInputField.onValueChanged.AddListener(_ => RefreshUIVisibility());

            _repeatInputField = view.RepeatInput;
            _repeatInputField.onEndEdit.AddListener(OnRepeatInputEndEdit);

            // ------------------------ Category ------------------------
            var cycle = CreateSlotButton("CategoryCycleButton", view.CategorySlot, "ALL");

            var labelTMP = cycle.Label;
            if (labelTMP)
            {
                labelTMP.richText = true;
                labelTMP.raycastTarget = false;
                // center on drawn glyphs since the icon stretches the line
                labelTMP.alignment = TextAlignmentOptions.Midline;
            }

            _categoryButton = cycle.Button;
            // category colors own this label
            var cycleSkin = cycle.Button.GetComponent<RoR2.UI.SkinControllers.ButtonSkinController>();
            if (cycleSkin) cycleSkin.useRecommendedLabel = false;
            UpdateCycleButtonVisuals(labelTMP);

            cycle.Button.onClick.AddListener(() =>
            {
                _category = RecipeFilter.Next(_category);
                UpdateCycleButtonVisuals(labelTMP);
                RefreshUIVisibility();
            });

            // ------------------------ Stepper ------------------------
            var plus = CreateSlotButton("RepeatPlus", view.PlusSlot, "+");
            plus.Button.onClick.AddListener(() => StepRepeatCount(1));
            _repeatPlusButton = plus.Button;

            var minus = CreateSlotButton("RepeatMinus", view.MinusSlot, "-");
            minus.Button.onClick.AddListener(() => StepRepeatCount(-1));
            _repeatMinusButton = minus.Button;

            // ------------------------ Craft ------------------------
            var craft = CreateSlotButton("GlobalCraftButton", view.CraftSlot, "Select a Recipe");
            craft.Button.interactable = false;
            // center on drawn glyphs so descenders clear the frame
            var craftSkin = craft.Button.GetComponent<RoR2.UI.SkinControllers.ButtonSkinController>();
            if (craftSkin) craftSkin.useRecommendedAlignment = false;
            if (craft.Label) craft.Label.alignment = TextAlignmentOptions.Midline;

            // lighter disabled tint so the frame reads like the stepper
            var craftColors = craft.Button.colors;
            craftColors.disabledColor = Color.Lerp(craftColors.disabledColor, craftColors.normalColor, 0.5f);
            craft.Button.colors = craftColors;

            _globalCraftButton = craft.Button;
            _globalCraftButtonText = craft.Label;
            _globalCraftButtonImage = craft.Image;
            _globalCraftButtonIsCanon = craft.IsCanon;
            _globalCraftButton.onClick.AddListener(OnGlobalCraftButtonClicked);
        }

        //==================== Prefabs ====================
        internal void EnsureRecipeRowTemplate()
        {
            if (_recipeRowTemplate != null) return;

            _recipeRowTemplate = InstantiateBundleTemplate("RecipeRow");
            // the nested slot comes from the prefab, so it still needs the vanilla outlines
            AddResultOutlines(_craftingPanel, _recipeRowTemplate.GetComponent<RecipeRowView>().ResultSlot.transform);
            AddSlotTooltip(_recipeRowTemplate.GetComponent<RecipeRowView>().ResultSlot);
            BindRecipeRowTemplate(_recipeRowTemplate);
        }

        // runtime state and the vanilla layer can't live in the prefab
        private void BindRecipeRowTemplate(GameObject rowGO)
        {
            var view = rowGO.GetComponent<RecipeRowView>();
            view.RowTopButton.requiredTopLayer = _vanillaTopLayer;

            if (_cutDown)
            {
                view.DepthText.gameObject.SetActive(false);
                view.PathsText.gameObject.SetActive(false);
            }

            var runtime = rowGO.AddComponent<RecipeRowRuntime>();
            runtime.View = view;
            runtime.CollapsedHeight = view.RowTransform.sizeDelta.y;
        }

        internal void EnsurePathRowTemplate()
        {
            if (_pathRowTemplate != null) return;
            _pathRowTemplate = InstantiateBundleTemplate("PathRow");
            BindPathRowTemplate(_pathRowTemplate);
        }

        // runtime state and the vanilla layer can't live in the prefab
        private void BindPathRowTemplate(GameObject rowGO)
        {
            var view = rowGO.GetComponent<PathRowView>();
            view.PathButton.requiredTopLayer = _vanillaTopLayer;
            rowGO.AddComponent<PathRowRuntime>().View = view;
        }

        internal void EnsureSharedDropdown()
        {
            if (_sharedDropdown != null) return;
            BindSharedDropdown(InstantiateBundleTemplate("Dropdown"));
        }

        // runtime and the list scroll it hands off to live outside the prefab
        private void BindSharedDropdown(GameObject drawerGO)
        {
            var view = drawerGO.GetComponent<DropdownView>();
            view.ScrollRect.ParentScroll = _recipeListContent.GetComponentInParent<ScrollRect>();

            _sharedDropdown = drawerGO.AddComponent<RecipeDropdownRuntime>();
            _sharedDropdown.Panel = this;
            _sharedDropdown.View = view;
        }

        internal void EnsureSharedHoverRect()
        {
            if (_selectionReticle != null) return;

            var reticleGO = InstantiateBundleTemplate("Reticle");
            _selectionReticle = (RectTransform)reticleGO.transform;
            _reticleClip = reticleGO.GetComponent<ReticleView>().Clip;
        }

        internal void EnsureResultSlotArtTemplates(CraftingPanel craftingPanel)
        {
            if (_ResultSlotTemplate != null) return;
            _ResultSlotTemplate = InstantiateBundleTemplate("ResultSlot");
            AddResultOutlines(craftingPanel, _ResultSlotTemplate.transform);
        }

        // vanilla art can't ship in our bundle, clone it in above the background
        private static void AddResultOutlines(CraftingPanel craftingPanel, Transform slotRT)
        {
            var outlineInnerloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline (1)");
            var outlineOuterloc = craftingPanel.transform.Find("MainPanel/Juice/BGContainer/CraftingContainer/Background/Result/Holder/Outline");
            if (!outlineInnerloc || !outlineOuterloc) return;

            var inner = InstantiateLayer(outlineInnerloc.gameObject, slotRT);
            inner.name = "ResultOutlineInner";
            inner.SetActive(true);
            inner.transform.SetSiblingIndex(1);

            var outer = InstantiateLayer(outlineOuterloc.gameObject, slotRT);
            outer.name = "ResultOutlineOuter";
            outer.SetActive(true);
            outer.transform.SetSiblingIndex(2);
        }

        internal void EnsureIngredientSlotTemplate()
        {
            if (_ingredientSlotTemplate != null) return;
            _ingredientSlotTemplate = InstantiateBundleTemplate("PhysicalSlot");
            _droneSlotTemplate = InstantiateBundleTemplate("DroneSlot");
            _tradeSlotTemplate = InstantiateBundleTemplate("TradeSlot");

            AddSlotTooltip(_ingredientSlotTemplate.GetComponent<SlotView>());
            AddSlotTooltip(_droneSlotTemplate.GetComponent<SlotView>());
            AddSlotTooltip(_tradeSlotTemplate.GetComponent<SlotView>());
        }

        // vanilla's hover card, the icon has to catch the pointer for it
        private static void AddSlotTooltip(SlotView slot)
        {
            slot.gameObject.AddComponent<TooltipProvider>();
            slot.Icon.raycastTarget = true;
        }

        // inactive holder for pooled dropdown slots
        internal void EnsureSlotPool()
        {
            if (_slotPoolRoot != null) return;

            var poolGO = new GameObject("SlotPool");
            poolGO.SetActive(false);
            _slotPoolRoot = poolGO.transform;
            _slotPoolRoot.SetParent(_cookbookRoot.transform, false);
        }

        //==================== Bundle ====================
        // bundle prefab as an inactive template
        private GameObject InstantiateBundleTemplate(string name)
        {
            var go = UnityEngine.Object.Instantiate(UIBundle.GetPrefab(name), _cookbookRoot.transform, false);
            go.name = name;
            go.SetActive(false);
            return go;
        }
    }
}
