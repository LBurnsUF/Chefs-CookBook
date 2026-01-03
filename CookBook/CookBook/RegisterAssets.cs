using RoR2;
using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TextCore;

namespace CookBook
{
    internal static class RegisterAssets
    {
        public static TMP_SpriteAsset CombatIconAsset { get; private set; }
        public static TMP_SpriteAsset HealingIconAsset { get; private set; }
        public static TMP_SpriteAsset UtilityIconAsset { get; private set; }

        public const string CombatAssetName = "CookBookIcon_Combat";
        public const string HealingAssetName = "CookBookIcon_Healing";
        public const string UtilityAssetName = "CookBookIcon_Utility";
        public const string SingleIconName = "icon";

        private static bool _built;

        public static void Init()
        {
            TryBuild();
            RoR2Application.onLoad += TryBuild;
        }

        private static void TryBuild()
        {
            if (_built) return;

            var combatTex = LoadTexture2D("RoR2/DLC3/Drone Tech/texCombatIcon.png");
            var healingTex = LoadTexture2D("RoR2/DLC3/Drone Tech/texHealingIcon.png");
            var utilityTex = LoadTexture2D("RoR2/DLC3/Drone Tech/texUtilityIcon.png");

            if (!combatTex || !healingTex || !utilityTex)
            {
                CookBook.Log.LogWarning(
                    $"CookBook: Operator icon textures missing. combat={(combatTex ? "OK" : "NULL")} healing={(healingTex ? "OK" : "NULL")} utility={(utilityTex ? "OK" : "NULL")}"
                );
                return;
            }

            CombatIconAsset = CreateSingleIconSpriteAsset(combatTex, CombatAssetName, SingleIconName, new Color32(255, 75, 50, 255));
            HealingIconAsset = CreateSingleIconSpriteAsset(healingTex, HealingAssetName, SingleIconName, new Color32(119, 255, 117, 255));
            UtilityIconAsset = CreateSingleIconSpriteAsset(utilityTex, UtilityAssetName, SingleIconName, new Color32(172, 104, 248, 255));

            _built = (CombatIconAsset && HealingIconAsset && UtilityIconAsset);

            if (_built)
            {
                CookBook.Log.LogInfo("CookBook: Operator icon TMP_SpriteAssets registered successfully.");
            }
        }

        private static Texture2D LoadTexture2D(string key)
        {
            try
            {
                var tex = Addressables.LoadAssetAsync<Texture2D>(key).WaitForCompletion();
                return tex;
            }
            catch (Exception e)
            {
                CookBook.Log.LogWarning($"CookBook: Exception loading Texture2D key '{key}': {e.GetType().Name}: {e.Message}");
                return null;
            }
        }

        private static TMP_SpriteAsset CreateSingleIconSpriteAsset(Texture2D tex, string assetName, string spriteName, Color tint)
        {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = assetName;
            asset.spriteSheet = tex;

            ForceSpriteAssetVersion(asset, "1.1.0");

            asset.spriteGlyphTable.Clear();
            asset.spriteCharacterTable.Clear();

            int w = tex.width;
            int h = tex.height;

            var glyph = new TMP_SpriteGlyph
            {
                index = 0,
                glyphRect = new GlyphRect(0, 0, w, h),
                metrics = new GlyphMetrics(w, h, 0, h * 0.8f, w),
                scale = 1.0f
            };

            var character = new TMP_SpriteCharacter(0, glyph)
            {
                name = spriteName,
                scale = 1.0f
            };

            asset.spriteGlyphTable.Add(glyph);
            asset.spriteCharacterTable.Add(character);

            var shader = Shader.Find("TextMeshPro/Sprite");
            var mat = new Material(shader);

            TMPro.ShaderUtilities.GetShaderPropertyIDs();
            mat.SetTexture(TMPro.ShaderUtilities.ID_MainTex, tex);

            mat.color = tint;

            mat.hideFlags = HideFlags.HideInHierarchy;
            asset.material = mat;

            asset.UpdateLookupTables();
            MaterialReferenceManager.AddSpriteAsset(asset);

            return asset;
        }

        private static void ForceSpriteAssetVersion(TMP_SpriteAsset asset, string version)
        {
            var f = typeof(TMP_SpriteAsset).GetField("m_Version", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                CookBook.Log.LogWarning("CookBook: Could not find TMP_SpriteAsset.m_Version via reflection. UpgradeSpriteAsset may run.");
                return;
            }

            f.SetValue(asset, version);
        }
    }
}
