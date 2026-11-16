using RoR2.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CookBook
{
    internal static partial class CraftUI
    {
        private static readonly List<Selectable> _navOrder = new();

        // =================== Gamepad Navigation ========================
        internal static void InstallGamepadBindings(GameObject root)
        {
            if (!root) return;
            AddGamepadAction(root, "UITabRight", FocusCookBook);
            AddGamepadAction(root, "UITabLeft", FocusVanilla);
        }

        private static void AddGamepadAction(GameObject root, string action, UnityAction cb)
        {
            var ev = root.AddComponent<HGGamepadInputEvent>();
            ev.actionName = action;
            ev.actionEvent = new UnityEvent();
            ev.actionEvent.AddListener(cb);
            ev.enabledObjectsIfActive = Array.Empty<GameObject>();
            ev.requiredTopLayer = _vanillaTopLayer;
        }

        internal static void FocusCookBook()
        {
            Selectable target = _selectedPathUI ? _selectedPathUI.pathButton : null;
            if (!target) target = _navOrder.Count > 0 ? _navOrder[0] : _categoryButton;
            SelectForGamepad(target);
        }

        internal static void FocusVanilla()
        {
            if (!_craftingPanel) return;
            Selectable target = null;
            if (_craftingPanel.inventoryContainer)
            {
                foreach (var b in _craftingPanel.inventoryContainer.GetComponentsInChildren<MPButton>(false))
                {
                    if (b && b.interactable && b.CanBeSelected()) { target = b; break; }
                }
            }
            if (!target) target = _craftingPanel.confirmButton;
            SelectForGamepad(target);
        }

        private static void SelectForGamepad(Selectable target)
        {
            if (!target) return;
            var loc = target.GetComponent<MPEventSystemLocator>();
            var es = loc ? loc.eventSystem : null;
            if (es) es.SetSelectedGameObject(target.gameObject);
        }

        // explicit vertical order from filter button through rows to footer
        internal static void RebuildNavigation()
        {
            _navOrder.Clear();

            foreach (var row in _recipeRowUIs)
            {
                if (!row.RowGO || !row.RowGO.activeSelf) continue;
                var runtime = row.RowGO.GetComponent<RecipeRowRuntime>();
                if (!runtime || !runtime.RowTopButton) continue;

                _navOrder.Add(runtime.RowTopButton);

                if (runtime.IsExpanded && _sharedDropdown && _sharedDropdown.CurrentOwner == runtime && _sharedDropdown.Content)
                {
                    foreach (Transform child in _sharedDropdown.Content)
                    {
                        if (!child.gameObject.activeSelf) continue;
                        var path = child.GetComponent<PathRowRuntime>();
                        if (path && path.pathButton) _navOrder.Add(path.pathButton);
                    }
                }
            }

            Selectable first = _navOrder.Count > 0 ? _navOrder[0] : null;
            Selectable last = _navOrder.Count > 0 ? _navOrder[_navOrder.Count - 1] : null;
            Selectable footerUp = _selectedPathUI && _selectedPathUI.pathButton ? _selectedPathUI.pathButton : last;
            if (!footerUp) footerUp = _categoryButton;

            for (int i = 0; i < _navOrder.Count; i++)
            {
                var s = _navOrder[i];
                var path = s.GetComponent<PathRowRuntime>();
                SetNav(s,
                    up: i > 0 ? _navOrder[i - 1] : _categoryButton,
                    down: i < _navOrder.Count - 1 ? _navOrder[i + 1] : _globalCraftButton,
                    left: path && path.OwnerRow ? path.OwnerRow.RowTopButton : null,
                    right: path ? _globalCraftButton : null);
            }

            SetNav(_categoryButton, up: null, down: first ? first : _globalCraftButton, left: null, right: null);
            SetNav(_globalCraftButton, up: footerUp, down: null, left: null, right: _repeatMinusButton);
            SetNav(_repeatMinusButton, up: footerUp, down: null, left: _globalCraftButton, right: _repeatPlusButton);
            SetNav(_repeatPlusButton, up: footerUp, down: null, left: _repeatMinusButton, right: null);
        }

        private static void SetNav(Selectable s, Selectable up, Selectable down, Selectable left, Selectable right)
        {
            if (!s) return;
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = up,
                selectOnDown = down,
                selectOnLeft = left,
                selectOnRight = right
            };
            s.navigation = nav;
        }
    }
}
