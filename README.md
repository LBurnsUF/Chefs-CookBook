# Chef’s CookBook

**CookBook** is a quality-of-life mod for *Risk of Rain 2* that provides a structured, searchable, and expandable view of craftable items and their possible crafting paths.

Instead of mentally tracking multi-step recipes or juggling Wiki tabs, CookBook presents crafting information **in-game** with a clean, scalable UI designed for large inventories and complex recipe graphs.

![CookBook Overview](https://raw.githubusercontent.com/LBurnsUF/Chefs-CookBook/refs/heads/master/images/UI.png)

## Feedback and Issues

- Have ideas or suggestions? Post them in the [Feature Suggestions discussion](https://github.com/LBurnsUF/Chefs-CookBook/discussions/3)
- Encountered performance issues? Create a [performance issue report](https://github.com/LBurnsUF/Chefs-CookBook/issues?q=state%3Aopen%20label%3A%22performance%20issue%22)
- Encounted a bug? Create a [bug report](https://github.com/LBurnsUF/Chefs-CookBook/issues?q=state%3Aopen%20label%3Abug)

--- 

## Features

### Frontend
- Lists all currently craftable results based on your inventory, grouped by result type, rarity, and quantity
- Live filtering via a search bar
- Each recipe row can be expanded to reveal all valid crafting paths
- Highly configurable via ROO settings integration for your performance needs
![Settings Integration](https://raw.githubusercontent.com/LBurnsUF/Chefs-CookBook/refs/heads/master/images/SettingsUI.png)
- Paths are displayed hierarchically, showing exact ingredient requirements
- Accordion-style UI keeps only one recipe expanded at a time to reduce clutter
- Rows grow and collapse naturally with content
- Support for repeatedly executing a specific craft chain N times in a single automated sequence
- Automatically calculates and displays the "Max Affordable" repetitions for any selected path, physically clamping user input to current inventory limits
- Event system to prompt other mod users if someone is requesting their assistance in a recipe!
### Backend
- Robust against other mods
- Highly encapsulated, doesn't touch any external logic
- Automated sequences include logic to orient active equipment slots and sets
- Backend cleanly separates data, logic, and UI for extensibility
- Optimization layer that kills redundant crafting chains early if a more efficient path (based on item weighting) is already discovered for the same result
- Recipes are culled early in the compute phase using IsCausallyLinked to prevent the generation of circular or impossible crafting paths
- Hooks specifically monitor permanent item stacks and equipment sets, ignoring temporary buffs and activations
- Affordability logic accounts for the entire team's potential by tracking scrapable drones and allied inventory trade limits

## Controls

- Configure various keybinds and crafting features in the Mod's settings menu
- Click a recipe row to expand or collapse its available crafting paths
- The search bar filters results in real time
- Expand a recipe row, select the path you want, then select **craft**
	- The *Objectives* panel will be updated to visualize the status of the craft chain
	- Hold *left alt* to cancel an in progress craft


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


## Development Status

**paused**!
Will continue in the interim between semesters in a few weeks! 

Planned features include:

- Visual polish and UI refinements for UI frontend (this is my first mod, and Unity UI is painful)
- In-Progress run recipe tracking
	- An in-progress view to view all recipes and their completion status, accessed by holding tab (inspectable so that the game can be paused while reading)

---

## Compatibility

- Does not modify gameplay logic
- Does not interact with networking (no host required!)
- Entirely client-sided, other players are NOT required to have the mod installed.
- Should be compatible with most content mods that introduce new items or recipes
