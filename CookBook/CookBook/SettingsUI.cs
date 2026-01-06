using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using UnityEngine;
using System.IO;
using System.Linq;

namespace CookBook
{
    internal static class SettingsUI
    {
        private static bool _initialized = false;

        public static void Init(CookBook plugin)
        {
            if (_initialized) return;

            Sprite modIcon = LoadSpriteFromDisk(plugin, "icon.png");
            if (modIcon != null) ModSettingsManager.SetModIcon(modIcon);

            ModSettingsManager.SetModDescription("QOL crafting automation for wandering CHEF.");

            ModSettingsManager.AddOption(new KeyBindOption(CookBook.AbortKey));

            ModSettingsManager.AddOption(new CheckBoxOption(CookBook.AllowMultiplayerPooling));
            ModSettingsManager.AddOption(new CheckBoxOption(CookBook.ConsiderDrones));
            ModSettingsManager.AddOption(new CheckBoxOption(CookBook.PreventCorruptedCrafting));
            ModSettingsManager.AddOption(new CheckBoxOption(CookBook.ShowCorruptedResults));

            ModSettingsManager.AddOption(new ChoiceOption(CookBook.InternalSortOrder));
            ModSettingsManager.AddOption(new CheckBoxOption(CookBook.DebugMode));


            ModSettingsManager.AddOption(new IntSliderOption(CookBook.MaxDepth, new IntSliderConfig
            {
                min = 1,
                max = 10,
                formatString = "{0}"
            }));
            ModSettingsManager.AddOption(new IntSliderOption(CookBook.MaxChainsPerResult, new IntSliderConfig
            {
                min = 1,
                max = 100,
                formatString = "{0}"
            }));
            ModSettingsManager.AddOption(new IntSliderOption(CookBook.ComputeThrottleMs, new IntSliderConfig
            {
                min = 100,
                max = 2000,
                formatString = "{0}ms"
            }));

            // --- Hierarchical Tier Sorting ---
            int tierCount = CookBook.TierPriorities.Count;
            string[] rankChoices = Enumerable.Range(1, tierCount).Select(i => i.ToString()).ToArray();

            foreach (var tierEntry in CookBook.TierPriorities)
            {
                string friendlyName = TierManager.GetFriendlyName(tierEntry.Key);

                ModSettingsManager.AddOption(new ChoiceOption(tierEntry.Value, new ChoiceConfig
                {
                    name = friendlyName,
                    description = $"Set sorting priority for {friendlyName} items."
                }));
            }

            _initialized = true;
        }

        private static Sprite LoadSpriteFromDisk(CookBook plugin, string fileName)
        {
            string directory = Path.GetDirectoryName(plugin.Info.Location);
            string path = Path.Combine(directory, fileName);

            if (File.Exists(path))
            {
                byte[] data = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(data))
                {
                    tex.filterMode = FilterMode.Point;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                }
            }
            return null;
        }
    }
}