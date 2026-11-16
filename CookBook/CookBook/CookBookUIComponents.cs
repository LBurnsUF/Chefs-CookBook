using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static CookBook.CraftPlanner;
using System.Collections;

namespace CookBook
{
    //==================== Buttons ====================
    // hover and gamepad select share one highlight
    internal sealed class CookBookButton : HGButton
    {
        internal System.Action<bool> HighlightChanged;
        private bool _lit;

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            bool lit = state == SelectionState.Highlighted || state == SelectionState.Selected || state == SelectionState.Pressed;
            if (lit == _lit) return;
            _lit = lit;
            HighlightChanged?.Invoke(lit);
        }
    }

    // vanilla ScrollToSelection needs a scrollbar
    [RequireComponent(typeof(MPEventSystemLocator))]
    internal sealed class ScrollFollowSelection : MonoBehaviour
    {
        internal ScrollRect Scroll;
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

    // mouse leaving drops selection so highlight clears
    [RequireComponent(typeof(MPEventSystemLocator))]
    internal sealed class DeselectOnPointerExit : MonoBehaviour, IPointerExitHandler
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

    //==================== Runtimes ====================
    internal sealed class RecipeRowRuntime : MonoBehaviour
    {
        internal CraftableEntry Entry;

        public RectTransform RowTransform;
        public LayoutElement RowLayoutElement;
        public RectTransform RowTop;
        public CookBookButton RowTopButton;
        public TextMeshProUGUI ArrowText;

        public RectTransform DropdownMountPoint;
        public LayoutElement DropdownLayoutElement;

        public Image ResultIcon;
        public TextMeshProUGUI ResultStackText;
        public TextMeshProUGUI ItemLabel;
        public TextMeshProUGUI DepthText;
        public TextMeshProUGUI PathsText;

        public bool IsExpanded;
        public float CollapsedHeight;

        private void OnDestroy()
        {
            if (RowTopButton != null)
            {
                RowTopButton.onClick.RemoveAllListeners();
                RowTopButton.HighlightChanged = null;
            }
            Entry = null;
            CraftUI.NotifyRecipeRowDestroyed(this);
        }
    }

    internal sealed class PathRowRuntime : MonoBehaviour
    {
        internal RecipeRowRuntime OwnerRow;
        internal RecipeChain Chain;
        internal Image BackgroundImage;
        public RectTransform VisualRect;
        public CookBookButton pathButton;
        private ColorFaderRuntime fader;

        internal bool isSelected;
        internal bool isHovered;

        internal const float FadeDuration = 0.1f;

        internal static readonly Color Col_BG_Normal = new Color32(26, 26, 26, 50);
        internal static readonly Color Col_BG_Active = new Color32(206, 198, 143, 255);
        internal static readonly Color Col_BG_Hover = new Color32(206, 198, 143, 75);

        private void Awake()
        {
            if (pathButton == null) pathButton = GetComponent<CookBookButton>();
            if (pathButton != null)
            {
                pathButton.onClick.AddListener(OnClicked);
                pathButton.HighlightChanged = OnHighlightChanged;
            }

            if (VisualRect == null)
            {
                var visuals = transform.Find("Visuals");
                if (visuals != null) VisualRect = visuals.GetComponent<RectTransform>();
            }

            if (VisualRect != null)
            {
                if (BackgroundImage == null) BackgroundImage = VisualRect.GetComponent<Image>();
                fader = VisualRect.GetComponent<ColorFaderRuntime>();
            }
        }

        internal void Init(RecipeRowRuntime owner, RecipeChain chain)
        {
            OwnerRow = owner;
            Chain = chain;
            isSelected = false;
            isHovered = false;

            UpdateVisuals(true);
        }

        private void OnDestroy()
        {
            if (pathButton != null)
            {
                pathButton.onClick.RemoveListener(OnClicked);
                pathButton.HighlightChanged = null;
            }

            CraftUI.NotifyPathRowDestroyed(this);
            OwnerRow = null;
            Chain = null;
        }

        internal void OnClicked() { CraftUI.OnPathSelected(this); }

        internal void OnHighlightChanged(bool isHighlighted)
        {
            if (isHighlighted)
            {
                isHovered = true;
                CraftUI._currentHoveredPath = this;
                CraftUI.AttachReticleTo(VisualRect);
            }
            else
            {
                isHovered = false;
                if (CraftUI.IsReticleAttachedTo(VisualRect))
                    CraftUI.RestoreReticleToSelection();
            }
            UpdateVisuals(false);
        }
        public void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;

            if (isSelected) CraftUI.AttachReticleTo(VisualRect);
            UpdateVisuals(false);
        }

        private void UpdateVisuals(bool instant)
        {
            if (BackgroundImage == null) return;

            Color target =
                isSelected ? Col_BG_Active :
                isHovered ? Col_BG_Hover :
                Col_BG_Normal;

            if (instant || fader == null) BackgroundImage.color = target;
            else fader.CrossFadeColor(target, FadeDuration, ignoreTimeScale: true);
        }
    }

    internal sealed class CraftUIRunner : MonoBehaviour { }

    internal class RecipeDropdownRuntime : MonoBehaviour
    {
        public ScrollRect ScrollRect;
        public RectTransform Content;
        public RectTransform Background;
        public RecipeRowRuntime CurrentOwner;

        public int SelectedPathIndex = -1;

        public void OpenFor(RecipeRowRuntime owner)
        {
            CurrentOwner = owner;
            gameObject.SetActive(true);

            transform.SetParent(owner.DropdownMountPoint, false);

            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (ScrollRect != null)
            {
                ScrollRect.enabled = owner.Entry.Chains.Count > CraftUI.PathsContainerMaxVisibleRows;
                ScrollRect.verticalNormalizedPosition = 1f;
            }

            CraftUI.PopulateDropdown(Content, owner);
        }

        public void Close()
        {
            if (CraftUI._activeDropdownRoutine != null && CraftUI._runner != null)
            {
                CraftUI._runner.StopCoroutine(CraftUI._activeDropdownRoutine);
                CraftUI._activeDropdownRoutine = null;
            }

            if (CurrentOwner == null) return;

            SelectedPathIndex = -1;
            transform.SetParent(CraftUI._cookbookRoot.transform, false);
            gameObject.SetActive(false);
            CurrentOwner = null;
        }

        private void OnDestroy() { CurrentOwner = null; }
    }

    internal class NestedScrollRect : ScrollRect
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
