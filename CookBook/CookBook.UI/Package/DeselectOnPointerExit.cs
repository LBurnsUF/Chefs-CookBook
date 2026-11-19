using RoR2.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CookBook
{
    // mouse leaving drops selection so highlight clears
    [RequireComponent(typeof(MPEventSystemLocator))]
    public sealed class DeselectOnPointerExit : MonoBehaviour, IPointerExitHandler
    {
        private MPEventSystemLocator _loc;

        private void Awake() { _loc = GetComponent<MPEventSystemLocator>(); }

        public void OnPointerExit(PointerEventData eventData)
        {
            var es = _loc ? _loc.eventSystem : null;
            if (!es || es.currentInputSource != MPEventSystem.InputSource.MouseAndKeyboard) return;
            if (es.currentSelectedGameObject == gameObject) es.SetSelectedGameObject(null);
        }
    }
}
