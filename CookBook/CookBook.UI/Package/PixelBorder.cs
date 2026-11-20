using UnityEngine;

namespace CookBook
{
    // bar thickness in screen pixels, refit whenever the panel lands on a canvas
    public sealed class PixelBorder : MonoBehaviour
    {
        public float Top;
        public float Bottom;
        public float Left;
        public float Right;

        [OptionalRef] public RectTransform TopBar;
        [OptionalRef] public RectTransform BottomBar;
        [OptionalRef] public RectTransform LeftBar;
        [OptionalRef] public RectTransform RightBar;

        //------------------------------- LifeCycle -------------------------------
        private void OnEnable() { Refit(); }

        // remount moves us to a new canvas
        private void OnTransformParentChanged() { Refit(); }

        //------------------------------- Visuals -------------------------------
        public void Refit()
        {
            float unitsPerPixel = UnitsPerPixel();
            SetHeight(TopBar, Mathf.Round(Top) * unitsPerPixel);
            SetHeight(BottomBar, Mathf.Round(Bottom) * unitsPerPixel);
            SetWidth(LeftBar, Mathf.Round(Left) * unitsPerPixel);
            SetWidth(RightBar, Mathf.Round(Right) * unitsPerPixel);
        }

        //------------------------------- Helpers -------------------------------
        private float UnitsPerPixel()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (!canvas) return 1f;

            // canvas scale times any scale between the root canvas and us
            var root = canvas.rootCanvas;
            float scale = root.scaleFactor * (transform.lossyScale.y / root.transform.lossyScale.y);
            return scale > 0f ? 1f / scale : 1f;
        }

        // bars stretch along their edge, so the other axis is always 0
        private static void SetHeight(RectTransform bar, float height)
        {
            if (!bar) return;
            bar.sizeDelta = new Vector2(0f, height);
        }

        private static void SetWidth(RectTransform bar, float width)
        {
            if (!bar) return;
            bar.sizeDelta = new Vector2(width, 0f);
        }
    }
}
