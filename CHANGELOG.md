# Changelog

Dates are listed in `MM/DD/YY` format.

---

## v1.0.0 — 12/18/25
### Release
- First stable public release
- Core UI, crafting logic, and backend systems finalized

---

## v0.7.2 — 12/17/25
### Changed
- Combined pickup logic refactor completed
- Crafting control logic finalized and stabilized

---

## v0.7.1 — 12/16/25
### Changed
- Fully working version prior to backend refactor for combined pickup logic

---

## v0.7.0 — 12/15/25
### Added
- Completed automated crafting system
- Crafting automation now relies on **user pickup events** rather than fixed delays

### Changed
- Removed naive time-based polling for inventory checks

---

## v0.6.1 — 12/15/25
### Changed
- Hardened autocrafting logic
- Craft chains now wait for the user to collect produced items before continuing

---

## v0.6.0 — 12/15/25
### Added
- Fully implemented backend for sending craft chains to `CraftingController`
- Crafting logic now obeys all internal network constraints
- Imitates local user actions for reliability

### Compatibility
- Reliable in multiplayer scenarios
- No host-based dependency

---

## v0.5.3 — 12/14/25
### Changed
- Optimized menu rendering to avoid reattaching modified UI on repeat renders
- Prevented UI duplication during craft chain execution

### Added
- Crafting chain support directly in the UI (visual polish pending)

---

## v0.5.2 — 12/13/25
### Changed
- Major backend overhaul of `CraftPlanner`
- Clean indexing-based planning logic
- Proper aborts on circular, recursive, or redundant recipe paths

### Performance
- Cached all major UI references at template creation
- ~20% UI rendering performance improvement

---

## v0.5.1 — 12/11/25
### Added
- Proper dropdown event handling
- Isolated CookBook UI via its own canvas

### Changed
- Reduced menu stutter
- Implemented simple time slicing for row rendering to eliminate menu-open hitching

---

## v0.4.0 — 12/11/25
### Added
- Initial implementation of dropdown menus

### Changed
- Frontend UI refactor

---

## v0.4.2 — 12/08/25
### Changed
- Updated UI icon prefab

---

## v0.4.1 — 12/08/25
### Added
- `RecipeCount` field to recipe chains
- Recipes producing different quantities are now treated as distinct entries

### Changed
- Partial `CraftUI` update
- Planned migration toward RoR2-native prefab styling

---

## v0.4.0 — 12/07/25
### Added
- Seamless insertion of CookBook into vanilla crafting UI
- Dynamic resizing based on vanilla UI dimensions
- Recipe list populated directly from `CurrentCraftables` events

### Performance
- Cached each built row for fast, render-only searching
- No row recomputation or culling required

---

## v0.3.3 — 12/04/25
### Changed
- Hardened UI positioning against resolution scaling
- All layout derived from base UI dimensions

---

## v0.3.2 — 12/04/25
### Changed
- Reduced debug and print verbosity for proven systems

---

## v0.3.1 — 12/07/25
### Fixed
- Edge case where Chef UI could fail to detach on stage transitions

---

## v0.3.0 — 12/04/25

### Added
- Began prototyping UI layout and injection for custom features

### Changed
- Cleaned up Chef NPC dialogue hooks

---

## v0.2.4 — 12/03/25
### Removed
- Several external dependencies
- Removed R2API entirely
- Removed **all** reflection usage

### Changed
- General codebase cleanup

---

## v0.2.3 — 12/03/25
### Changed
- Refactored overall architecture
- `Plugin` now handles initialization only
- Introduced `StateController` to manage events and UI interaction

---

## v0.2.2 — 12/03/25
### Fixed
- Minor bugs in `TierManager`

### Changed
- Proper initialization of tier manager
- Reduced verbosity in state controller (logic proven stable)

---

## v0.2.1 — 12/03/25
### Changed
- Hardened `CraftPlanner` logic

---

## v0.2.0 — 12/02/25
### Added
- Inventory tracking logic
- Complete recipe provider
- Tier manager for clean sorting and UI organization

---

## v0.1.0 — 12/01/25
### Added
- Initial DFS-based dictionary generation
- Fast-read backend foundations
