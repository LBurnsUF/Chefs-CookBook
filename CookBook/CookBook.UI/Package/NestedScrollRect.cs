using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CookBook
{
    public class NestedScrollRect : ScrollRect
    {
        public ScrollRect ParentScroll;

        public override void OnScroll(PointerEventData data)
        {
            if (!this.IsActive()) return;

            bool canScrollVertical = content.rect.height > viewport.rect.height;
            if (!canScrollVertical)
            {
                if (ParentScroll) ParentScroll.OnScroll(data);
                return;
            }

            float deltaY = data.scrollDelta.y;
            float currentPos = verticalNormalizedPosition;
            const float boundaryThreshold = 0.001f;

            if (ParentScroll && ((deltaY > 0 && currentPos >= (1f - boundaryThreshold)) || (deltaY < 0 && currentPos <= boundaryThreshold))) ParentScroll.OnScroll(data);
            else base.OnScroll(data);
        }
    }
}
