using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    // vanilla ScrollToSelection needs a scrollbar
    [RequireComponent(typeof(MPEventSystemLocator))]
    public sealed class ScrollFollowSelection : MonoBehaviour
    {
        public ScrollRect Scroll;
        private MPEventSystemLocator _loc;
        private GameObject _last;
        private readonly Vector3[] _tc = new Vector3[4];
        private readonly Vector3[] _vc = new Vector3[4];

        private void Awake() { _loc = GetComponent<MPEventSystemLocator>(); }

        private void LateUpdate()
        {
            var es = _loc ? _loc.eventSystem : null;
            var cur = es ? es.currentSelectedGameObject : null;
            if (cur == _last) return;
            _last = cur;

            if (!cur || !Scroll || !Scroll.content || !cur.transform.IsChildOf(Scroll.content)) return;
            if (es.currentInputSource != MPEventSystem.InputSource.Gamepad) return;

            var vp = Scroll.viewport ? Scroll.viewport : (RectTransform)Scroll.transform;
            ((RectTransform)cur.transform).GetWorldCorners(_tc);
            vp.GetWorldCorners(_vc);

            float over = _tc[1].y - _vc[1].y;
            float under = _tc[0].y - _vc[0].y;
            float delta = over > 0f ? over : (under < 0f ? under : 0f);
            if (delta == 0f) return;

            var local = Scroll.content.parent.InverseTransformVector(new Vector3(0f, delta, 0f));
            Scroll.StopMovement();
            var p = Scroll.content.anchoredPosition;
            p.y -= local.y;
            Scroll.content.anchoredPosition = p;
        }
    }
}
