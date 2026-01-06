using System;
using BepInEx.Logging;
using RoR2;
using UnityEngine.Networking;

namespace CookBook
{
    internal static class DialogueHooks
    {
        private static ManualLogSource _log;

        /// <summary>
        /// Fired when the Chef crafting UI opens for the local user.
        /// </summary>
        internal static event Action<CraftingController> ChefUiOpened;

        /// <summary>
        /// Fired when the Chef crafting UI closes for the local user.
        /// </summary>
        internal static event Action<CraftingController> ChefUiClosed;

        // ----------------  lifecycle ----------------
        internal static void Init(ManualLogSource log)
        {
            _log = log;
            _log.LogInfo("[CookBook] Installing CraftingController UI hooks.");
            On.RoR2.CraftingController.Awake += CraftingController_Awake;
        }

        private static void CraftingController_Awake(
            On.RoR2.CraftingController.orig_Awake orig,
            RoR2.CraftingController self)
        {
            orig(self);

            if (!NetworkClient.active) return;

            var prompt = self.GetComponent<NetworkUIPromptController>();
            if (!prompt) return;

            prompt.onDisplayBegin += OnPromptDisplayBegin;
            prompt.onDisplayEnd += OnPromptDisplayEnd;

            _log.LogDebug("[CookBook] Hooked NetworkUIPromptController for CraftingController.");
        }

        internal static void Shutdown() { On.RoR2.CraftingController.Awake -= CraftingController_Awake; }

        // ------------------------ Events ------------------------
        private static void OnPromptDisplayBegin(
            NetworkUIPromptController prompt,
            LocalUser localUser,
            CameraRigController cameraRig)
        {
            CraftingController controller = prompt.GetComponent<CraftingController>();
            if (controller == null)
            {
                _log.LogError("[CookBook] UI opened but no CraftingController found on GameObject.");
                return;
            }
            ChefUiOpened?.Invoke(controller);
        }

        private static void OnPromptDisplayEnd(
            NetworkUIPromptController prompt,
            LocalUser localUser,
            CameraRigController cameraRig)
        {
            CraftingController controller = prompt.GetComponent<CraftingController>();
            if (controller == null)
            {
                _log.LogError("[CookBook] UI closed but no CraftingController found on GameObject.");
                return;
            }
            ChefUiClosed?.Invoke(controller);
        }
    }
}
