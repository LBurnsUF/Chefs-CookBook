# Chef’s CookBook

**CookBook** is a quality-of-life mod for *Risk of Rain 2* that provides a structured, searchable, and expandable view of craftable items and their possible crafting paths.

Instead of mentally tracking multi-step recipes or juggling external spreadsheets, CookBook presents crafting information **in-game** with a clean, scalable UI designed for large inventories and complex recipe graphs.

![CookBook Overview](images/UI.png)
---

## Features

- Lists all currently craftable results based on your inventory
- Live filtering via a search bar
- Clear grouping by result type, rarity, and quantity
- Each recipe row can be expanded to reveal all valid crafting paths
- Paths are displayed hierarchically, showing exact ingredient requirements
- Accordion-style UI keeps only one recipe expanded at a time to reduce clutter
- Unique crafting paths are treated as distinct recipes even if they produce the same item
- Crafting paths are grouped by quantity of the result item produced
- Rows grow and collapse naturally with content
- Built with optimization in mind:
  - Unified Equipment & Item arrays
  - Hash-based recipe deduplication
- Backend cleanly separates data, logic, and UI for extensibility
- Robust against other mods:
  - Includes hooks to refresh recipe logic for custom or modded recipes

---

## Controls

- Click a recipe row to expand or collapse its available crafting paths
- The search bar filters results in real time
- Result counts are displayed inline, appended as `[xN]`
- Expand a recipe row, select the path you want, then select **Craft**
- Hold *left alt* to cancel an in progress craft
 - The full chain of crafts will be completed automatically

---

## Technical Overview

- Extensive use of templating to maximize rendering performance
- No prefabs baked into the scene
- Minimal layout group usage for predictable behavior

### UI Architecture

- Dynamic layout groups
- Content size fitting for automatic row expansion
- Explicit hierarchy control for stable rendering
- UI logic structured to avoid per-frame allocations (no performance dips)

> Future updates may introduce pooling for recipe rows if necessary.

---

## Development Status

This mod is under **active development**.

Planned but not yet implemented features include:

- Configuration menu with in-built sorting controls  
  (alphabetical, depth, path count, etc.)
- Visual polish and UI refinements for UI frontend (this is my first mod, and Unity UI is painful)

---

## Compatibility

- Does not modify gameplay logic
- Does not interact with networking (no host required)
- Should be compatible with most content mods that introduce new items or recipes

---

## Feedback and Issues

- Have ideas or suggestions? Post them in the [Feature Suggestions discussion](https://github.com/LBurnsUF/Chefs-CookBook/discussions/3).
- Encountered performance issues? Create a [performance issue report](https://github.com/LBurnsUF/Chefs-CookBook/issues?q=state%3Aopen%20label%3A%22performance%20issue%22).
- Encounted a bug? Create a [bug report](https://github.com/LBurnsUF/Chefs-CookBook/issues?q=state%3Aopen%20label%3Abug).