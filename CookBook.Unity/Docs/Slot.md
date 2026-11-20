# Slot prefabs

Item icon boxes. `ResultSlot` sits at the left of each recipe row. The ingredient slots fill the path rows.

| Prefab | Used for | In bundle |
|---|---|---|
| `ResultSlot` | recipe row result | yes |
| `IngredientSlot` | base for the three below, never used directly | pulled in by the variants |
| `PhysicalSlot` | item you hold | yes |
| `DroneSlot` | item from scrapping a drone | yes |
| `TradeSlot` | item from a trade | yes |

All sizes are canvas units at the reference panel (554 x 624). See [Workflow](Workflow.md) for open, edit, build and test.

## ResultSlot

```
ResultSlot            RectTransform, LayoutElement (58 x 58), SlotView
├─ IconBackground     Image (5, 5, 5), stretched
├─ Icon               Image, stretched, 8 inset, preserve aspect
└─ StackText          TextMeshProUGUI 10pt top right, anchored top right at (-6, -3)
```

**SlotView refs**
- `Icon` → `Icon`
- `StackText` → `StackText`

**Added at runtime**
- Two vanilla outline layers, cloned from the crafting panel's result box, go in at index 1 and 2, between `IconBackground` and `Icon`. Keep `IconBackground` first.
- The sprite and stack count are set per row.

## IngredientSlot and its variants

```
IngredientSlot        RectTransform, LayoutElement (42 x 42), SlotView
├─ Background         Image, Outline, stretched
│  └─ BorderGroup_Solid   LayoutElement (ignore layout), PixelBorder
│     ├─ Top / Bottom / Left / Right   Image (209, 209, 210, 200)
├─ Icon               Image, stretched, 4 inset, preserve aspect
└─ StackText          TextMeshProUGUI 10pt top right, anchored top right at (-2, -2), starts inactive
```

**SlotView refs**
- `Icon` → `Icon`
- `StackText` → `StackText`

**Variants**
- `PhysicalSlot`, `DroneSlot` and `TradeSlot` are prefab variants of `IngredientSlot`.
- Each one only overrides the `Background` color:
  - Physical (16, 8, 10)
  - Drone (20, 50, 45)
  - Trade (75, 65, 25)
- An edit to `IngredientSlot` reaches all three. An edit inside a variant applies to that variant only, and the field shows in **bold** in the Inspector.

**PixelBorder**
- Thickness is in screen pixels, not canvas units.
- On enable and on every remount it resizes the four bars, so borders stay 1 px sharp at any resolution.
- Change the thickness through `Top` / `Bottom` / `Left` / `Right` on `PixelBorder`, not the bar sizes. The bar sizes get overwritten.

## Rules

- **Text fonts stay empty.** The game's default font fills in at runtime. A font set here would be packed into the bundle.
- **Keep every `SlotView` ref wired.** The build and the tests fail on an empty ref. In game, a broken slot falls back to the code-built version and logs why.
- **Renaming a prefab** means updating its name in `UIBundleInfo.Prefabs` too.
