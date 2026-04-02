using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static GameObject CreateUIObject(string name, params System.Type[] components)
        {
            var go = new GameObject(name, components);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }
        internal static RectTransform CreateInternal(string name, Transform parent, params System.Type[] components)
        {
            var go = CreateUIObject(name, components);
            var rt = go.GetComponent<RectTransform>();

            rt.SetParent(parent, false);

            // Standard Full-Stretch Anchoring
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);

            return rt;
        }
        internal static void AlignLabelVerticallyBetween(RectTransform labelRect, RectTransform bgMainRect, RectTransform submenuRect)
        {
            if (!labelRect || !bgMainRect || !submenuRect) return;

            var corners = new Vector3[4];

            bgMainRect.GetWorldCorners(corners);
            Vector3 bgTopCenter = (corners[1] + corners[2]) * 0.5f;

            submenuRect.GetWorldCorners(corners);
            Vector3 submenuTopCenter = (corners[1] + corners[2]) * 0.5f;

            float midY = (bgTopCenter.y + submenuTopCenter.y) * 0.5f;

            var labelWorldPos = labelRect.position;
            labelWorldPos.y = midY;
            labelRect.position = labelWorldPos;
        }

        // =============== Border Logic ===================
        public static GameObject AddBorder(RectTransform parent, Color32 color, float top = 0f, float bottom = 0f, float left = 0f, float right = 0f)
        {
            if (top > 0) top = GetPixelCorrectThickness(top);
            if (bottom > 0) bottom = GetPixelCorrectThickness(bottom);
            if (left > 0) left = GetPixelCorrectThickness(left);
            if (right > 0) right = GetPixelCorrectThickness(right);
            // ----------------

            var containerGO = CreateUIObject("BorderGroup_Solid", typeof(RectTransform), typeof(LayoutElement));
            var containerRT = containerGO.GetComponent<RectTransform>();

            var le = containerGO.GetComponent<LayoutElement>();
            le.ignoreLayout = true;

            containerRT.SetParent(parent, false);
            containerRT.anchorMin = Vector2.zero;
            containerRT.anchorMax = Vector2.one;
            containerRT.sizeDelta = Vector2.zero;
            containerRT.anchoredPosition = Vector2.zero;

            RectTransform CreateBar(string name)
            {
                var go = CreateUIObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(containerRT, false);
                var img = go.GetComponent<Image>();
                img.sprite = GetSolidPointSprite();
                img.color = color;
                img.raycastTarget = false;
                return go.GetComponent<RectTransform>();
            }

            // Use Integer Pivots (0 or 1) to ensure we grow inward from the exact edge

            if (top > 0)
            {
                var t = CreateBar("Top");
                t.anchorMin = new Vector2(0, 1); t.anchorMax = new Vector2(1, 1);
                t.pivot = new Vector2(0.5f, 1); // Pivot Top
                t.anchoredPosition = Vector2.zero;
                t.sizeDelta = new Vector2(0, top);
            }

            if (bottom > 0)
            {
                var b = CreateBar("Bottom");
                b.anchorMin = new Vector2(0, 0); b.anchorMax = new Vector2(1, 0);
                b.pivot = new Vector2(0.5f, 0); // Pivot Bottom
                b.anchoredPosition = Vector2.zero;
                b.sizeDelta = new Vector2(0, bottom);
            }

            if (left > 0)
            {
                var l = CreateBar("Left");
                l.anchorMin = new Vector2(0, 0); l.anchorMax = new Vector2(0, 1);
                l.pivot = new Vector2(0, 0.5f); // Pivot Left
                l.anchoredPosition = Vector2.zero;
                l.sizeDelta = new Vector2(left, 0);
            }

            if (right > 0)
            {
                var r = CreateBar("Right");
                r.anchorMin = new Vector2(1, 0); r.anchorMax = new Vector2(1, 1);
                r.pivot = new Vector2(1, 0.5f); // Pivot Right
                r.anchoredPosition = Vector2.zero;
                r.sizeDelta = new Vector2(right, 0);
            }

            return containerGO;
        }
        public static GameObject AddBorderTapered(RectTransform parent, Color32 color, float top = 0f, float bottom = 0f)
        {
            var containerGO = CreateUIObject("BorderGroup_Tapered", typeof(RectTransform), typeof(LayoutElement));
            var containerRT = containerGO.GetComponent<RectTransform>();
            containerGO.GetComponent<LayoutElement>().ignoreLayout = true;

            containerRT.SetParent(parent, false);
            containerRT.anchorMin = Vector2.zero;
            containerRT.anchorMax = Vector2.one;
            containerRT.offsetMin = Vector2.zero;
            containerRT.offsetMax = Vector2.zero;

            RectTransform MakeTaper(string name)
            {
                var go = CreateUIObject(name, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(containerRT, false);
                var img = go.GetComponent<Image>();
                img.sprite = GetTaperedSprite();
                img.color = color;
                img.raycastTarget = false;
                return go.GetComponent<RectTransform>();
            }

            var t = MakeTaper("Top");
            t.anchorMin = new Vector2(0, 1); t.anchorMax = new Vector2(1, 1);
            t.pivot = new Vector2(0.5f, 1);
            t.anchoredPosition = Vector2.zero;
            t.sizeDelta = new Vector2(0, top);

            if ((int)bottom > 0)
            {
                var b = MakeTaper("Bottom");
                b.anchorMin = new Vector2(0, 0); b.anchorMax = new Vector2(1, 0);
                b.pivot = new Vector2(0.5f, 0);
                b.anchoredPosition = Vector2.zero;
                b.sizeDelta = new Vector2(0, bottom);
            }
            return containerGO;
        }
        internal static Sprite GetSolidPointSprite()
        {
            if (_solidPointSprite != null) return _solidPointSprite;

            var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.filterMode = FilterMode.Point;
            tex.Apply();

            _solidPointSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            return _solidPointSprite;
        }
        internal static Sprite GetTaperedSprite()
        {
            if (_taperedGradientSprite != null) return _taperedGradientSprite;

            int width = 256;
            int height = 1;
            var tex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color[] colors = new Color[width * height];
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);
                float alpha = Mathf.Sin(t * Mathf.PI);
                colors[x] = new Color(1, 1, 1, alpha);
            }

            tex.SetPixels(colors);
            tex.Apply();

            _taperedGradientSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
            return _taperedGradientSprite;
        }

        // ========= Pixel rounding for clean borders =========
        internal static void PixelSnap(RectTransform rt)
        {
            if (!rt) return;

            var p = rt.anchoredPosition;
            p.x = Mathf.Round(p.x);
            p.y = Mathf.Round(p.y);
            rt.anchoredPosition = p;

            var omin = rt.offsetMin;
            var omax = rt.offsetMax;
            omin.x = Mathf.Round(omin.x);
            omin.y = Mathf.Round(omin.y);
            omax.x = Mathf.Round(omax.x);
            omax.y = Mathf.Round(omax.y);
            rt.offsetMin = omin;
            rt.offsetMax = omax;
        }
        internal static float RoundToEven(float value)
        {
            float result = Mathf.Round(value);
            if (result % 2 != 0) result += 1f;
            return result;
        }
        internal static float GetPixelCorrectThickness(float desiredPixels)
        {
            Canvas rootCanvas = _cookbookRoot ? _cookbookRoot.GetComponentInParent<Canvas>() : UnityEngine.Object.FindObjectOfType<Canvas>();

            if (rootCanvas != null)
            {
                float pixelRatio = 1f / rootCanvas.scaleFactor;
                return Mathf.Round(desiredPixels) * pixelRatio;
            }

            return desiredPixels;
        }

        // =================== Hover Reticle ========================
        internal static void AttachReticleTo(RectTransform target)
        {
            if (!_selectionReticle || !target) return;

            _selectionReticle.SetParent(target, false);

            _selectionReticle.localPosition = Vector3.zero;
            _selectionReticle.localRotation = Quaternion.identity;
            _selectionReticle.localScale = Vector3.one;

            float outset = 4f;
            _selectionReticle.offsetMin = new Vector2(-outset, -outset);
            _selectionReticle.offsetMax = new Vector2(outset, outset);

            _selectionReticle.gameObject.SetActive(true);
            _selectionReticle.SetAsLastSibling();
        }
        internal static bool IsReticleAttachedTo(Transform t)
        {
            if (t == null || _selectionReticle == null) return false;
            return _selectionReticle.parent == t;
        }
        internal static void RestoreReticleToSelection()
        {
            if (_selectedAnchor != null) _currentReticleTarget = _selectedAnchor;
            else if (_selectionReticle) _selectionReticle.gameObject.SetActive(false);
        }
    }
}
