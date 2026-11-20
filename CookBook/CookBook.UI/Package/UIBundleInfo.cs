using System;

namespace CookBook
{
    // what the bundle holds, shared by the build, the tests and the loader
    public static class UIBundleInfo
    {
        public const string FileName = "cookbookui";
        public const string PrefabFolder = "Assets/UI";

        // panel size prefabs are authored at, other sizes scale the panel root
        public const float PanelWidth = 554f;
        public const float PanelHeight = 624f;

        // prefab name and the view on its root
        public static readonly (string Name, Type View)[] Prefabs =
        {
            ("ResultSlot", typeof(SlotView)),
            ("PhysicalSlot", typeof(SlotView)),
            ("DroneSlot", typeof(SlotView)),
            ("TradeSlot", typeof(SlotView)),
            ("PathRow", typeof(PathRowView)),
            ("Dropdown", typeof(DropdownView)),
            ("RecipeRow", typeof(RecipeRowView)),
        };
    }
}
