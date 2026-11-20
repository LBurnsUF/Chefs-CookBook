using System;

namespace CookBook
{
    // what the bundle holds, shared by the build, the tests and the loader
    public static class UIBundleInfo
    {
        public const string FileName = "cookbookui";
        public const string PrefabFolder = "Assets/UI";

        // prefab name and the view on its root, filled as prefabs land
        public static readonly (string Name, Type View)[] Prefabs = Array.Empty<(string, Type)>();
    }
}
