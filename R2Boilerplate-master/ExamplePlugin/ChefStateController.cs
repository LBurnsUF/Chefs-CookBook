using BepInEx.Logging;
using UnityEngine;
using RoR2;


namespace CookBook
{
    internal static class ChefStateController
    {
        private static ManualLogSource _log;
        private static bool _initialized;
        private static CraftPlanner _planner;


        internal static void Init(ManualLogSource log, CraftPlanner planner)
        {
            if (_initialized)
                return;

            _initialized = true;
            _log = log;
            _planner = planner;

            InventoryTracker.Init(log);
            InventoryTracker.Enable();

            log.LogInfo("ChefStateController.Init()");
            RoR2Application.onUpdate += OnUpdate;
        }



        /// Debug Functions. When F8 is pressed, log inventory contents
        private static void OnUpdate()
        {
            if (Input.GetKeyDown(KeyCode.F8))
            {
                LogInventoryContents();
            }
        }
        
        private static void LogInventoryContents()
        {
            var stacks = InventoryTracker.GetStacksCopy();
            if (stacks == null)
            {
                _log.LogInfo("InventoryTracker has not bound to the local player yet");
                return;
            }

            _log.LogInfo("=== Current Inventory Items ===");

            for (int i = 0; i < stacks.Length; i++)
            {
                int count = stacks[i];
                if (count <= 0)
                    continue;

                ItemIndex idx = (ItemIndex)i;
                ItemDef def = ItemCatalog.GetItemDef(idx);
                string name = def ? def.nameToken : idx.ToString();

                _log.LogInfo($"{name} x{count}");
            }

            _log.LogInfo("=== End of Inventory ===");
        }
    }
}
