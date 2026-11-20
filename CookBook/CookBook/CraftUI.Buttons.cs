using RoR2.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static partial class CraftUI
    {
        // =================== Canon Buttons ========================
        internal struct ButtonParts
        {
            public HGButton Button;
            public TextMeshProUGUI Label;
            public Image Image;
            public bool IsCanon;
        }

        // prefer cancel button, fall back to confirm
        internal static HGButton FindVanillaButtonTemplate(CraftingPanel craftingPanel)
        {
            if (!craftingPanel) return null;

            foreach (var b in craftingPanel.GetComponentsInChildren<HGButton>(true))
            {
                if (!b || b == craftingPanel.confirmButton) continue;
                if (craftingPanel.inventoryContainer && b.transform.IsChildOf(craftingPanel.inventoryContainer)) continue;
                if (craftingPanel.ingredientContainer && b.transform.IsChildOf(craftingPanel.ingredientContainer)) continue;
                if (!b.targetGraphic || !b.GetComponentInChildren<TextMeshProUGUI>(true)) continue;
                return b;
            }

            return craftingPanel.confirmButton as HGButton;
        }

        internal static ButtonParts CreateCanonButton(string name, RectTransform parent, string text)
        {
            if (_vanillaButtonTemplate)
            {
                var parts = CloneVanillaButton(name, parent, text);
                if (parts.Button) return parts;
            }
            return CreateFlatButton(name, parent, text);
        }

        // clone inactive so vanilla components never wake
        internal static ButtonParts CloneVanillaButton(string name, RectTransform parent, string text)
        {
            var staging = CreateUIObject("ButtonStaging", typeof(RectTransform));
            staging.SetActive(false);
            staging.transform.SetParent(parent, false);

            var go = UnityEngine.Object.Instantiate(_vanillaButtonTemplate.gameObject, staging.transform, false);
            go.name = name;
            StripForeignBehaviours(go);

            var btn = go.GetComponent<HGButton>();
            if (!btn || !btn.targetGraphic)
            {
                UnityEngine.Object.Destroy(staging);
                _log.LogError($"CraftUI: cloned button '{name}' missing HGButton/targetGraphic, using flat fallback.");
                return default;
            }

            btn.onClick = new Button.ButtonClickedEvent();
            btn.updateTextOnHover = false;
            btn.hoverLanguageTextMeshController = null;
            btn.requiredTopLayer = _vanillaTopLayer;
            btn.interactable = true;

            // skin owns label style, we own size
            var skin = go.GetComponent<RoR2.UI.SkinControllers.ButtonSkinController>();
            if (skin)
            {
                skin.useRecommendedButtonWidth = false;
                skin.useRecommendedButtonHeight = false;
            }

            var label = go.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label)
            {
                label.text = text;
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;

                // our buttons are shorter than vanilla, shrink text to clear the inner frame
                label.enableAutoSizing = true;
                label.fontSizeMax = label.fontSize;
                label.fontSizeMin = 8f;
                label.margin = new Vector4(0f, ButtonFrameInsetPx, 0f, ButtonFrameInsetPx);
            }

            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            var le = go.GetComponent<LayoutElement>();
            if (le) le.ignoreLayout = true;
            go.AddComponent<DeselectOnPointerExit>();

            UnityEngine.Object.Destroy(staging);

            return new ButtonParts { Button = btn, Label = label, Image = btn.targetGraphic as Image, IsCanon = true };
        }

        // strip anything wired to vanilla
        private static readonly Type[] _keepTypes =
        {
            typeof(Graphic), typeof(Selectable), typeof(MPEventSystemLocator), typeof(LayoutElement),
            typeof(LayoutGroup), typeof(ContentSizeFitter), typeof(RectMask2D), typeof(Mask),
            typeof(BaseMeshEffect), typeof(CanvasGroup), typeof(Canvas), typeof(GraphicRaycaster),
            typeof(RoR2.UI.SkinControllers.ButtonSkinController)
        };

        internal static void StripForeignBehaviours(GameObject root)
        {
            var removed = new List<string>();
            bool any = true;
            while (any)
            {
                any = false;
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!mb) continue;
                    var t = mb.GetType();
                    bool keep = false;
                    foreach (var k in _keepTypes) if (k.IsAssignableFrom(t)) { keep = true; break; }
                    if (keep) continue;

                    // dependents go first, retry next pass
                    try { UnityEngine.Object.DestroyImmediate(mb); removed.Add(t.Name); any = true; }
                    catch { }
                }
            }
            if (removed.Count > 0) DebugLog.Trace(_log, $"CraftUI: stripped from button clone: {string.Join(", ", removed)}");
        }

        internal static ButtonParts CreateFlatButton(string name, RectTransform parent, string text)
        {
            var go = CreateUIObject(name, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);

            var img = go.AddComponent<Image>();
            img.color = new Color32(40, 40, 40, 255);
            var btn = go.AddComponent<HGButton>();
            btn.targetGraphic = img;
            btn.requiredTopLayer = _vanillaTopLayer;
            go.AddComponent<DeselectOnPointerExit>();

            var labelRT = CreateInternal("Text", go.transform, typeof(TextMeshProUGUI));
            var label = labelRT.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.raycastTarget = false;

            AddBorder((RectTransform)go.transform, new Color32(209, 209, 210, 200), 1f, 1f, 1f, 1f);
            go.SetActive(true);

            return new ButtonParts { Button = btn, Label = label, Image = img, IsCanon = false };
        }

        internal static void StretchRight(RectTransform rt, float width, float rightOffset)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(width, 0f);
            rt.anchoredPosition = new Vector2(-rightOffset, 0f);
        }
    }
}
