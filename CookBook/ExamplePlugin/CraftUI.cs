using System;
using BepInEx.Logging;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static class CraftUI
    {
        private static ManualLogSource _log;
        private static GameObject _cookbookRoot;   // our custom panel root
        private static CraftingController _currentController;
        private static bool _dumpedHierarchy = false;

        private static float _origBgWidth;
        private static float _origBgHeight;
        private static float _origInvWidth;
        private static float _origInvHeight;
        private static float _origCraftWidth;
        private static bool _sizesCached;

        internal static void Init(ManualLogSource log)
        {
            _log = log;
        }

        internal static void Attach(CraftingController controller)
        {
            _currentController = controller;

            if (_cookbookRoot != null)
            {
                _log.LogDebug("CraftUI.Attach: already attached, skipping.");
                return;
            }

            var craftingPanel = UnityEngine.Object.FindObjectOfType<CraftingPanel>();
            if (!craftingPanel)
            {
                _log.LogWarning("CraftUI.Attach: CraftingPanel not found; cannot attach UI.");
                return;
            }

            // locate hierarchy pieces
            Transform bgContainerTr = craftingPanel.transform.Find("MainPanel/Juice/BGContainer");
            if (!bgContainerTr)
            {
                _log.LogWarning("CraftUI.Attach: BGContainer not found.");
                return;
            }

            RectTransform bgRect = bgContainerTr.GetComponent<RectTransform>();
            if (!bgRect)
            {
                _log.LogWarning("CraftUI.Attach: BGContainer has no RectTransform.");
                return;
            }

            var bgMainTr = bgContainerTr.Find("BGMain");
            RectTransform bgMainRect = bgMainTr ? bgMainTr.GetComponent<RectTransform>() : null;
            if (!bgMainRect)
            {
                _log.LogWarning("CraftUI.Attach: BGMain not found.");
                return;
            }
            
            RectTransform craftBgRect = bgContainerTr.Find("CraftingContainer/Background")?.GetComponent<RectTransform>();
            RectTransform craftRect = bgContainerTr.Find("CraftingContainer")?.GetComponent<RectTransform>();
            RectTransform invRect = bgContainerTr.Find("InventoryContainer")?.GetComponent<RectTransform>();
            RectTransform submenuRect = bgContainerTr.Find("SubmenuContainer")?.GetComponent<RectTransform>();
            RectTransform labelRect = craftingPanel.transform.Find("MainPanel/Juice/LabelContainer")?.GetComponent<RectTransform>();
            Transform juiceTr = craftingPanel.transform.Find("MainPanel/Juice");
            RectTransform juiceRect = juiceTr ? juiceTr.GetComponent<RectTransform>() : null;

            // cache original sizes
            if (!_sizesCached)
            {
                _origBgWidth = bgMainRect.rect.width;
                _origBgHeight = bgMainRect.rect.height;
                _origInvWidth = invRect ? invRect.rect.width : 0f;
                _origInvHeight = invRect ? invRect.rect.height : 0f;
                _origCraftWidth = craftBgRect ? craftBgRect.rect.width : 0f;
                _sizesCached = true;
            }

            float baseWidth = _origBgWidth;
            float baseHeight = _origBgHeight;
            float invBaseWidth = _origInvWidth;
            float invBaseHeight = _origInvHeight;
            float craftBaseWidth = _origCraftWidth;

            float margin = 20f; // fallback
            if (labelRect && juiceRect)
            {
                var bgBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(juiceTr, bgMainRect);
                var labelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(juiceTr, labelRect);
                float derived = labelBounds.min.x - bgBounds.min.x;
                if (derived > 0f)
                    margin = derived;
            }

            // clamp cookbook width safely
            float cookbookWidth = Mathf.Clamp(baseWidth * 0.7f, 260f, baseWidth * 1.4f - invBaseWidth);

            // extend the main chef panel
            float extraWidth = baseWidth * 0.4f + margin;
            float newBgWidth = baseWidth + extraWidth;
            bgMainRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            bgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            if (labelRect)
            {
                labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, newBgWidth);
            }

            // shrink vanilla inventory/crafting margins slightly
            float invWidthNew = invBaseWidth * 0.85f;
            float invHeightNew = invBaseHeight * 0.9f;
            float craftWidthNew = invWidthNew;

            if (invRect)
            {
                invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, invWidthNew);
                invRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, invHeightNew);
            }

            if (craftBgRect)
            {
                craftBgRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, craftWidthNew);
            }

            // horizontal layout
            float sideMargin = margin; // recomputed craftrect present
            float gap = margin; // distance between vanilla block and cookbook

            if (craftBgRect)
            {
                float craftWidth = craftBgRect.rect.width;

                // clamp cookbook width
                float availableForCookbook = newBgWidth - craftWidth - gap - 2f * margin; // remaining width after craft + gap + min side margins

                if (availableForCookbook > 0f)
                {
                    cookbookWidth = availableForCookbook;
                }

                // recompute side margin: 2*sideMargin + craftWidth + gap + cookbookWidth
                float computedSideMargin =
                    (newBgWidth - craftWidth - gap - cookbookWidth) * 0.5f;

                sideMargin = Mathf.Max(margin, computedSideMargin);

                // position craft+inventory block using left sideMargin
                float halfBg = newBgWidth * 0.5f;
                float craftCenterX = -halfBg + sideMargin + craftWidth * 0.5f;

                var craftPos = craftBgRect.anchoredPosition;
                craftPos.x = craftCenterX;
                craftBgRect.anchoredPosition = craftPos;

                if (invRect)
                {
                    var pos = invRect.anchoredPosition;
                    pos.x = craftCenterX;
                    invRect.anchoredPosition = pos;
                }

                if (submenuRect)
                {
                    var pos = submenuRect.anchoredPosition;
                    pos.x = craftCenterX;
                    submenuRect.anchoredPosition = pos;
                }
            }

            // create CookBook panel in the new right-hand strip
            _cookbookRoot = new GameObject("CookBookPanel", typeof(RectTransform));
            _cookbookRoot.transform.SetParent(bgContainerTr, false);

            RectTransform rt = _cookbookRoot.GetComponent<RectTransform>();

            // anchored to right side; we use sideMargin for the right padding
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);

            // --- compute combined vertical bounds of craft + inventory in BGContainer space ---
            Bounds contentBounds = default;
            bool hasBounds = false;

            if (craftBgRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, craftBgRect);
                contentBounds = b;
                hasBounds = true;
            }

            if (invRect)
            {
                var b = RectTransformUtility.CalculateRelativeRectTransformBounds(bgContainerTr, invRect);
                if (hasBounds)
                {
                    contentBounds.Encapsulate(b);
                }
                else
                {
                    contentBounds = b;
                    hasBounds = true;
                }
            }

            // Fallback if something failed
            float cookbookHeight = hasBounds ? contentBounds.size.y : baseHeight * 0.6f;
            float cookbookCenterY = hasBounds ? contentBounds.center.y : 0f;

            // match vertical span of vanilla content, and right padding = sideMargin
            rt.sizeDelta = new Vector2(cookbookWidth, cookbookHeight);
            rt.anchoredPosition = new Vector2(-sideMargin, cookbookCenterY);

            // clone craftBgRect style to get the RoR2 frame / border
            if (craftBgRect)
            {
                GameObject frameClone = UnityEngine.Object.Instantiate(
                    craftBgRect.gameObject,
                    _cookbookRoot.transform
                );
                frameClone.name = "CookBookFrame";

                var frameRect = frameClone.GetComponent<RectTransform>();

                frameRect.anchorMin = new Vector2(0f, 0f);
                frameRect.anchorMax = new Vector2(1f, 1f);
                frameRect.pivot = new Vector2(0.5f, 0.5f);
                frameRect.anchoredPosition = Vector2.zero;
                frameRect.sizeDelta = Vector2.zero;

                // strip out the crafting contents – keep only the frame visuals
                foreach (Transform child in frameClone.transform)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            else
            {
                // fallback: invisible panel so code still has a RectTransform
                var img = _cookbookRoot.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
            }

            CreateTitleLabel(rt);

            _log.LogInfo(
                $"CraftUI.Attach: CookBook panel attached. baseWidth={baseWidth:F1}, newWidth={newBgWidth:F1}, cookbookWidth={cookbookWidth:F1}, invBaseWidth={invBaseWidth:F1}"
            );
        }

        private static void CreateTitleLabel(RectTransform parent)
        {
            var go = new GameObject("CookBookTitle", typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -10f);
            rt.sizeDelta = new Vector2(parent.rect.width - 20f, 30f);

            // If you have TextMeshPro in refs, use TMP_Text; otherwise use plain Text.
            var text = go.AddComponent<Text>();
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 18;
            text.text = "CookBook (WIP)";
            text.color = Color.white;
        }

        internal static void Detach()
        {
            if (_cookbookRoot != null)
            {
                UnityEngine.Object.Destroy(_cookbookRoot);
                _cookbookRoot = null;
                _log.LogInfo("CraftUI.Detach: CookBook panel destroyed.");
            }

            _currentController = null;
        }
    }
}
