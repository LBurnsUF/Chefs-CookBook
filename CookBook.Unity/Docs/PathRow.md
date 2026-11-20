# PathRow prefab

One crafting path inside a recipe's dropdown. It's a clickable strip of ingredient slots.

All sizes are canvas units at the reference panel (554 x 624). See [Workflow](Workflow.md) for open, edit, build and test.

```
PathRow                     RectTransform (top stretch), Image (clear hitbox), LayoutElement (56 tall, flex width 1),
                            CookBookButton, DeselectOnPointerExit, MPEventSystemLocator, PathRowView
└─ Visuals                  Image (26, 26, 26, 100), HorizontalLayoutGroup, ColorFaderRuntime
   │                        stretched, 4 inset top and bottom
   └─ BorderGroup_Tapered   LayoutElement (ignore layout), stretched
      ├─ Top                Image, TaperedLine sprite (209, 209, 210, 200), 1 tall
      └─ Bottom             same, along the bottom edge
```

**PathRowView refs**
- `PathButton` → `PathRow` (the `CookBookButton`)
- `VisualRect` → `Visuals`
- `BackgroundImage` → the `Image` on `Visuals`
- `Fader` → the `ColorFaderRuntime` on `Visuals`

**Visuals layout**
- The `HorizontalLayoutGroup` lines up the ingredient slots: spacing 6, padding 5 left and right, middle left.
- It controls child width and height, without force expand.
- The border group ignores layout, so slots lay out past it.

**Added at runtime**
- `PathRowRuntime`, which holds the path's chain and owner row and handles clicks. It lives in `CookBook.dll`, so the prefab can't hold it.
- `requiredTopLayer` on the button, which belongs to each vanilla panel.
- The ingredient slots, instantiated into `Visuals` per path.
- Selection and hover colors, faded through `Fader` on the `Visuals` image. The color set here is the idle color.

**TaperedLine**
- `Assets/UI/Art/TaperedLine.png` is 256 x 1 white, with alpha following a sine from 0 at the ends to 1 in the middle.
- It stretches along the bar, so the line fades out at both ends.
- It's our own art, so it ships in the bundle. Repaint it to change the fade.

## Rules

- **Keep the hitbox `Image` on the root**, with `raycastTarget` on and alpha 0. It's what the button clicks on.
- **Leave `CookBookButton.transition` at None.** Highlighting comes from `Fader`. A transition would fight it.
- **Keep every `PathRowView` ref wired.** The build and the tests fail on an empty ref.
