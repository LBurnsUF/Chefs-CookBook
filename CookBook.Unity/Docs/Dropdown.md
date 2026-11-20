# Dropdown prefab

The shared drawer that opens under a recipe row and lists that recipe's crafting paths. There is only one. It moves to whichever row is expanded.

All sizes are canvas units at the reference panel (554 x 624). See [Workflow](Workflow.md) for open, edit, build and test.

```
Dropdown             RectTransform (stretched), Image (clear, no raycast), NestedScrollRect,
│                    DropdownView, ScrollFollowSelection
└─ Viewport          RectMask2D, stretched
   └─ Content        VerticalLayoutGroup, ContentSizeFitter (vertical preferred)
                     top stretch, pivot top
```

**DropdownView refs**
- `ScrollRect` → the `NestedScrollRect` on `Dropdown`
- `Content` → `Content`

**Scrolling**
- `NestedScrollRect` scrolls vertically: clamped, inertia on, deceleration 0.16, sensitivity 34.888 (half a recipe row).
- At the top or bottom edge, or when the paths all fit, the wheel goes to the recipe list instead.
- `ScrollFollowSelection.Scroll` → the same `NestedScrollRect`. It keeps the gamepad selection in view.

**Content layout**
- Padding 10 left and right, 2 top and bottom. Spacing 0.
- Controls child width and height. Force expands width only.
- Path rows (see [PathRow](PathRow.md)) are added here at runtime.

**Added at runtime**
- `RecipeDropdownRuntime`, which opens, closes and moves the drawer. It lives in `CookBook.dll`, so the prefab can't hold it.
- `NestedScrollRect.ParentScroll`, which points at the recipe list's scroll, outside the prefab.
- On open, the drawer stretches into the row's mount point. Scrolling is on only when there are more paths than fit, and it starts at the top.

## Rules

- **Keep the root `Image` clear with raycast off.** Path rows take the clicks.
- **Leave `ParentScroll` empty.** It's marked optional, so the build passes, and the binder fills it.
- **Keep every `DropdownView` ref wired.** The build and the tests fail on an empty ref.
