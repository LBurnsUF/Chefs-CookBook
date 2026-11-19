using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    // override sorting skips parent RectMask2Ds, clip to them by hand
    [RequireComponent(typeof(RectMask2D))]
    public class ReticleClip : MonoBehaviour
    {
        public RectTransform Frame;

        private readonly List<RectMask2D> _masks = new List<RectMask2D>();
        private readonly Vector3[] _corners = new Vector3[4];

        //------------------------------- LifeCycle -------------------------------
        public void SetTarget(RectTransform target)
        {
            target.GetComponentsInParent(false, _masks);
        }

        private void LateUpdate()
        {
            var owner = (RectTransform)transform.parent;
            var clip = (RectTransform)transform;
            Rect full = owner.rect;
            Rect visible = full;

            foreach (var mask in _masks)
            {
                if (!mask) continue;
                visible = Intersect(visible, LocalRect(owner, mask.rectTransform));
            }

            // only touch rects on change, avoids canvas rebuild each frame
            Vector2 clipPos = visible.min - full.min;
            if (clip.anchoredPosition != clipPos || clip.sizeDelta != visible.size)
            {
                clip.anchoredPosition = clipPos;
                clip.sizeDelta = visible.size;
            }

            Vector2 framePos = full.min - visible.min;
            if (Frame && (Frame.anchoredPosition != framePos || Frame.sizeDelta != full.size))
            {
                Frame.anchoredPosition = framePos;
                Frame.sizeDelta = full.size;
            }
        }

        //------------------------------- Helpers -------------------------------
        private Rect LocalRect(RectTransform space, RectTransform other)
        {
            other.GetWorldCorners(_corners);
            Vector2 min = space.InverseTransformPoint(_corners[0]);
            Vector2 max = space.InverseTransformPoint(_corners[2]);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin);
            float yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Max(xMin, Mathf.Min(a.xMax, b.xMax));
            float yMax = Mathf.Max(yMin, Mathf.Min(a.yMax, b.yMax));
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
    }
}
