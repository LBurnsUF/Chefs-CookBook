# Workflow

How the UI prefabs get from Unity into the game.

## Pieces

- **`CookBook.Unity/Assets/UI/`**: the prefabs. These are the source of truth for the UI's look.
- **`CookBook/CookBook.UI/Package/`**: the view scripts (`SlotView`, `PixelBorder`, ...).
  - It's a Unity package, and also the `CookBook.UI.dll` the game loads.
  - Prefabs hold these components only. Game logic lives in `CookBook.dll` and gets added at runtime.
- **`UIBundleInfo`**: the list of prefabs that go in the bundle, and the panel size they're authored at.
- **`cookbookui`**: the built bundle. It ships next to `CookBook.dll`.

## First time setup

1. Open `CookBook.Unity` in Unity Hub with editor 2021.3.45f2.
2. Import the game. In ThunderKit Settings, set the game path and run the import. Or headless:
   ```
   Unity.exe -batchmode -projectPath . -executeMethod CookBook.Editor.HeadlessImport.Run -gamePath "<Risk of Rain 2 dir>"
   ```
   This takes about 6 minutes. The game files it creates are gitignored.

## Editing a prefab

1. Double click a prefab in `Assets/UI` to open it in Prefab Mode.
2. Select an object in the Hierarchy and change it in the Inspector. Wire a ref by dragging an object from the Hierarchy onto the field.
3. Save with Ctrl+S, then leave with the arrow at the top left of the Hierarchy.

Text shows no font in the editor. That's expected: see the Rules section in each prefab guide.

## Build, test, deploy

From `CookBook.Unity`:

1. **Tests**: validate every listed prefab.
   ```
   Unity.exe -batchmode -projectPath . -runTests -testPlatform EditMode -testResults results.xml
   ```
2. **Bundle**: validates, then writes `cookbookui`.
   ```
   Unity.exe -batchmode -quit -projectPath . -executeMethod CookBook.Editor.BuildBundles.Run -output AssetBundles
   ```
3. **Deploy**: copy `AssetBundles/cookbookui` next to `CookBook.dll` in the plugin folder.

Unity must be closed for the batch commands. Opening the project in the editor locks it.

## In game

- The log line `UIBundle: 5/5 prefabs loaded` means the bundle is in use.
- A missing or broken prefab logs why and falls back to its code-built version.
- No bundle at all means everything is code-built.
- Debug builds also check the code-built templates and log any empty refs.

## Guides

- [Slot](Slot.md)
- [PathRow](PathRow.md)
