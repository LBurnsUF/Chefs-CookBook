# Reticle prefab

The white selection frame that sits over whatever is hovered or selected: a recipe row, a path or the craft button. There is only one. It moves to each new target.

See [Workflow](Workflow.md) for open, edit, build and test.

```
Reticle                    RectTransform (stretched), Image (clear, no raycast), Canvas (override sorting, order 30),
│                          LayoutElement (ignore layout), ReticleView
└─ Clip                    RectMask2D, ReticleClip, bottom left, sized at runtime
   └─ Frame                bottom left, sized at runtime
      └─ BorderGroup_Solid LayoutElement (ignore layout), PixelBorder (3 px each side), stretched
         ├─ Top            Image (white), along the top edge
         ├─ Bottom         same, bottom edge
         ├─ Left           same, left edge
         └─ Right          same, right edge
```

**ReticleView refs**
- `Clip` → the `ReticleClip` on `Clip`

**ReticleClip refs**
- `Frame` → `Frame`

**How it draws**
- The `Canvas` override sorting puts the frame above neighbouring rows, so it isn't covered by the next row down.
- That same override means parent `RectMask2D`s no longer clip it. `ReticleClip` fixes that each frame. It shrinks `Clip` to the part of the target that the parent masks leave visible, and keeps `Frame` at the target's full size inside it. The frame is cut off at the list edge, not squashed.
- `PixelBorder` keeps the bars 3 screen pixels thick at any panel scale.

**Added at runtime**
- On each new target, the reticle is reparented to it, pushed out 4 units past its edges, and moved to the last sibling.
- `ReticleClip.SetTarget` collects the target's parent masks.
- It hides when nothing is selected. When the list repopulates or a row collapses, it goes back to the panel root.

## Rules

- **Keep the `Canvas` with override sorting on.** Without it, the next row draws over the frame.
- **Leave `Clip` and `Frame` sizes alone.** `ReticleClip` overwrites them every frame.
- **Keep the root `Image` clear with raycast off.** The reticle must never block clicks.
- **Keep every ref wired.** The build and the tests fail on an empty ref.
