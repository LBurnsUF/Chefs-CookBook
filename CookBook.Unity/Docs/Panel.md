# Panel prefab

The CookBook panel's frame: the search bar, the recipe list and the footer with the craft button and Qty stepper. The rows, dropdown and reticle are separate prefabs that live inside it at runtime.

All sizes are canvas units at the reference panel (554 x 624). See [Workflow](Workflow.md) for open, edit, build and test.

```
Panel                       stretched over the CookBook root, PanelView
├─ BorderGroup_Tapered      TaperedLine top and bottom (209, 209, 210, 255), 2 tall
├─ SearchBarContainer       Canvas (override sorting, order 31), GraphicRaycaster
│  │                        top stretch, 50 tall, 10 below the top
│  ├─ SearchInput           Image (0, 0, 0, 0.4), TMP_InputField
│  │  │                     left 75%, 5 short on the right, inset 6 at the bottom and 3 above the top
│  │  └─ Text Area          RectMask2D, stretched
│  │     ├─ Text            TextMeshProUGUI, 20, centered
│  │     └─ Placeholder     TextMeshProUGUI "Search", 20, centered, half white
│  ├─ CategorySlot          right 20%, same insets as SearchInput
│  └─ BorderGroup_Tapered   TaperedLine bottom only (209, 209, 210, 200), 1 tall
├─ Footer                   Canvas (override sorting, order 31), GraphicRaycaster
│  │                        bottom stretch, 32 tall, 10 above the bottom
│  ├─ PlusSlot              32 wide, at the right edge
│  ├─ RepeatInput           Image (0, 0, 0, 0.4), TMP_InputField (digits, no navigation)
│  │  │                     52 wide, 2 left of PlusSlot
│  │  └─ Text Area          RectMask2D, stretched
│  │     ├─ Placeholder     TextMeshProUGUI "Qty", 14.4, centered, half white
│  │     └─ Text            TextMeshProUGUI, 14.4, centered
│  ├─ MinusSlot             32 wide, 2 left of RepeatInput
│  └─ CraftSlot             the rest, 2 left of MinusSlot
└─ RecipeListContainer      ScrollRect, ScrollFollowSelection
   │                        top stretch, 502 tall, 70 below the top
   └─ Viewport              RectMask2D, Image (clear, no raycast), stretched
      └─ Content            VerticalLayoutGroup, ContentSizeFitter (vertical preferred), CanvasGroup
                            top stretch, pivot top
```

**PanelView refs**
- `SearchInput` → the `TMP_InputField` on `SearchInput`
- `CategorySlot` → `CategorySlot`
- `ListContent` → `Content`
- `CraftSlot`, `MinusSlot`, `PlusSlot` → the matching slots
- `RepeatInput` → the `TMP_InputField` on `RepeatInput`

**Slots**
- The four buttons (category, +, −, craft) are clones of the vanilla crafting panel's button, so they match the game's look and skin. The prefab can't hold them.
- Each slot is an empty rect. At runtime the button clone fills it exactly.
- Move or resize a button by moving or resizing its slot.

**Layering**
- The search bar and footer canvases sort at 31, above the reticle (30). A frame on a row scrolled under either bar stays hidden.
- Each needs its own `GraphicRaycaster`, or its buttons stop taking clicks.

**List**
- Scrolls vertically: clamped, inertia on, deceleration 0.16, sensitivity 34.888 (half a recipe row).
- Content padding is 10 left and right. Rows stack from the top with no spacing.
- `ScrollFollowSelection` keeps the gamepad selection in view.
- [RecipeRow](RecipeRow.md)s are added to `Content` at runtime. The [Dropdown](Dropdown.md) hands wheel scrolling to this list at its edges.

**Added at runtime**
- The four button clones, their labels, colors and click handlers.
- Search filtering on `SearchInput`, and the Qty handling on `RepeatInput`.

## Rules

- **Keep the input field `Target Graphic` empty.** A target graphic tints the field background on hover, which the panel doesn't do.
- **Keep `RepeatInput` on digits with navigation None.** The gamepad uses the stepper buttons instead.
- **Leave the fonts empty.** The game's default font fills in at runtime.
- **Keep every `PanelView` ref wired.** The build and the tests fail on an empty ref.
