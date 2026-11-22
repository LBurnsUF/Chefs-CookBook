using RoR2;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal sealed partial class CraftUI
    {
        internal static readonly CraftUI Crafting = new();

        internal IReadOnlyList<CraftableEntry> LastCraftables { get; private set; }
        internal bool _skeletonBuilt = false;
        internal CraftingController _currentController;
        internal InventorySnapshot _snap;
        internal bool _hasSnap;

        internal GameObject _cookbookRoot;
        internal GameObject _panelHolder;
        internal RoR2.UI.MPEventSystemProvider _eventSystemProvider;
        internal Vector2 _builtSize;
        internal ReticleClip _reticleClip;
        internal RectTransform _recipeListContent;
        internal TMP_InputField _searchInputField;

        internal GameObject _recipeRowTemplate;
        internal GameObject _pathRowTemplate;
        internal GameObject _ingredientSlotTemplate;
        internal GameObject _droneSlotTemplate;
        internal GameObject _tradeSlotTemplate;
        internal GameObject _ResultSlotTemplate;

        internal Coroutine _activeBuildRoutine;
        internal Coroutine _activeDropdownRoutine;

        internal RectTransform _selectionReticle;
        internal RectTransform _currentReticleTarget;
        internal RectTransform _selectedAnchor;

        internal RoR2.UI.HGButton _globalCraftButton;
        internal TextMeshProUGUI _globalCraftButtonText;
        internal Image _globalCraftButtonImage;
        internal bool _globalCraftButtonIsCanon;

        internal RecipeChain _selectedChainData;
        internal TMP_InputField _repeatInputField;
        internal RoR2.UI.HGButton _repeatMinusButton;
        internal RoR2.UI.HGButton _repeatPlusButton;
        internal RoR2.UI.HGButton _categoryButton;

        internal RoR2.UI.CraftingPanel _craftingPanel;
        internal RoR2.UI.HGButton _vanillaButtonTemplate;
        internal RoR2.UI.UILayerKey _vanillaTopLayer;

        internal static Sprite _solidPointSprite;

        internal readonly List<RecipeRowUI> _recipeRowUIs = new();

        internal System.Reflection.MethodInfo _onIngredientsChangedMethod;

        internal RecipeRowRuntime _openRow;
        internal RecipeFilter.RecipeFilterCategory _category;
        internal CraftUIRunner _runner;

        internal RecipeDropdownRuntime _sharedDropdown;
        internal RecipeRowRuntime _cachedDropdownOwner;
        internal RecipeRowRuntime _emptyRow;

        // dropdown slots wait in SlotPool between path rows, one stack per template
        internal readonly Dictionary<GameObject, Stack<SlotView>> _slotPools = new();
        internal Transform _slotPoolRoot;

        internal PathRowRuntime _currentHoveredPath;
        internal PathRowRuntime _selectedPathUI;

        internal void NotifyRecipeRowDestroyed(RecipeRowRuntime row)
        {
            if (_openRow == row) _openRow = null;
            if (_cachedDropdownOwner == row) _cachedDropdownOwner = null;
            if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == row)
                _sharedDropdown.CurrentOwner = null;
        }

        internal void NotifyPathRowDestroyed(PathRowRuntime row)
        {
            if (_currentHoveredPath == row) _currentHoveredPath = null;
            if (_selectedPathUI == row) _selectedPathUI = null;
            if (_selectedAnchor == row?.View.VisualRect) _selectedAnchor = null;
        }
    }
}
