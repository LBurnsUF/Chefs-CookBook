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
        internal static IReadOnlyList<CraftableEntry> LastCraftables { get; private set; }
        internal static bool _skeletonBuilt = false;
        internal static CraftingController _currentController;
        internal static InventorySnapshot _snap;
        internal static bool _hasSnap;

        internal static GameObject _cookbookRoot;
        internal static RectTransform _recipeListContent;
        internal static TMP_InputField _searchInputField;

        internal static GameObject _recipeRowTemplate;
        internal static GameObject _pathRowTemplate;
        internal static GameObject _ingredientSlotTemplate;
        internal static GameObject _droneSlotTemplate;
        internal static GameObject _tradeSlotTemplate;
        internal static GameObject _ResultSlotTemplate;

        internal static Coroutine _activeBuildRoutine;
        internal static Coroutine _activeDropdownRoutine;

        internal static RectTransform _selectionReticle;
        internal static RectTransform _currentReticleTarget;
        internal static RectTransform _selectedAnchor;

        internal static Button _globalCraftButton;
        internal static TextMeshProUGUI _globalCraftButtonText;
        internal static Image _globalCraftButtonImage;

        internal static RecipeChain _selectedChainData;
        internal static TMP_InputField _repeatInputField;

        internal static Sprite _solidPointSprite;
        internal static Sprite _taperedGradientSprite;

        internal static readonly Dictionary<int, Sprite> _iconCache = new();
        internal static readonly Dictionary<DroneIndex, Sprite> _droneIconCache = new();
        internal static readonly List<RecipeRowUI> _recipeRowUIs = new();

        internal static System.Reflection.MethodInfo _onIngredientsChangedMethod;

        internal static RecipeRowRuntime _openRow;
        internal static CraftUIRunner _runner;

        internal static RecipeDropdownRuntime _sharedDropdown;
        internal static RecipeRowRuntime _cachedDropdownOwner;

        internal static PathRowRuntime _currentHoveredPath;
        internal static PathRowRuntime _selectedPathUI;

        internal static void NotifyRecipeRowDestroyed(RecipeRowRuntime row)
        {
            if (_openRow == row) _openRow = null;
            if (_cachedDropdownOwner == row) _cachedDropdownOwner = null;
            if (_sharedDropdown != null && _sharedDropdown.CurrentOwner == row)
                _sharedDropdown.CurrentOwner = null;
        }

        internal static void NotifyPathRowDestroyed(PathRowRuntime row)
        {
            if (_currentHoveredPath == row) _currentHoveredPath = null;
            if (_selectedPathUI == row) _selectedPathUI = null;
            if (_selectedAnchor == row?.VisualRect) _selectedAnchor = null;
        }
    }
}
