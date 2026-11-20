# RecipeRow prefab

One recipe in the list: the clickable top strip with an arrow, the result item, its name and stats. Underneath is a mount point that the shared [Dropdown](Dropdown.md) moves into when the row expands.

All sizes are canvas units at the reference panel (554 x 624). See [Workflow](Workflow.md) for open, edit, build and test.

```
RecipeRow                  RectTransform (top stretch, 70 tall), VerticalLayoutGroup, LayoutElement (70), RecipeRowView
├─ RowTop                  Image (clear hitbox), CookBookButton, DeselectOnPointerExit, MPEventSystemLocator,
│  │                       LayoutElement (70)
│  ├─ DropDown             58 x 58, left middle
│  │  └─ Arrow             TextMeshProUGUI ">", 32, centered
│  ├─ ItemSlot             nested ResultSlot prefab, 58 x 58, left middle at x 68
│  ├─ Label                HGTextMeshProUGUI "NAME", 24, centered, ellipsis
│  │                       stretches from x 136 to 152 short of the right edge
│  ├─ MetaData             142 x 58, right middle
│  │  ├─ MinimumDepth      TextMeshProUGUI "Depth: 0", 16, bottom right, sits 6 above the middle
│  │  └─ AvailablePaths    TextMeshProUGUI "Paths: 0", 16, top right, sits 6 below the middle
│  └─ BorderGroup_Tapered  LayoutElement (ignore layout), TaperedLine top and bottom (209, 209, 210, 200), 1 tall
└─ DropdownMountPoint      LayoutElement (0 tall), top stretch
```

**RecipeRowView refs**
- `RowTransform` → `RecipeRow`
- `RowLayoutElement` → the `LayoutElement` on `RecipeRow`
- `RowTop` → `RowTop`
- `RowTopButton` → the `CookBookButton` on `RowTop`
- `ArrowText` → `Arrow`
- `ResultSlot` → the `SlotView` on `ItemSlot`
- `ItemLabel` → `Label`
- `DepthText` → `MinimumDepth`
- `PathsText` → `AvailablePaths`
- `DropdownMountPoint` → `DropdownMountPoint`
- `DropdownLayoutElement` → the `LayoutElement` on `DropdownMountPoint`

**Layout**
- RowTop is laid out by hand with anchors, not a layout group. Padding is 6 top and bottom, so the inner height is 58. Elements are 10 apart.
- The root `VerticalLayoutGroup` stacks `RowTop` over `DropdownMountPoint`. It controls child sizes and force expands width only.
- `ItemSlot` is a nested prefab instance. Edits to `ResultSlot.prefab` show up here too. Changes made on the instance stay on this row only.

**Added at runtime**
- `RecipeRowRuntime`, which holds the recipe entry and the expanded state. It lives in `CookBook.dll`, so the prefab can't hold it.
- `CollapsedHeight`, read from the root's height (70).
- `requiredTopLayer` on the button, which belongs to each vanilla panel.
- The vanilla result outlines, which are copied into `ItemSlot` the same way as for `ResultSlot`.
- The text values, the item icon and the expanded height, set per recipe.

**Arrow**
- It always shows `>`. Expanding rotates it 90 degrees clockwise to point down, so both states use the same glyph at the same size.
- Keep `DropDown` square and the arrow centered in it, so the rotation stays in place.

## Rules

- **Keep the hitbox `Image` on `RowTop`**, with `raycastTarget` on and alpha 0. It's what the button clicks on.
- **Leave `CookBookButton.transition` at None.** The row draws its own highlight.
- **The root height is the collapsed height.** Change it together with both `LayoutElement` heights.
- **Leave the fonts empty.** The game's default font fills in at runtime.
- **Keep every `RecipeRowView` ref wired.** The build and the tests fail on an empty ref.
