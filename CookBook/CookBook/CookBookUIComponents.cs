using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static CookBook.CraftPlanner;
using System.Collections;

namespace CookBook
{
    //==================== Runtimes ====================
    internal sealed class RecipeRowRuntime : MonoBehaviour
    {
        internal CraftableEntry Entry;

        public RectTransform RowTransform;
        public LayoutElement RowLayoutElement;
        public RectTransform RowTop;
        public Button RowTopButton;
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
            if (RowTopButton != null) RowTopButton.onClick.RemoveAllListeners();
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
        public Button pathButton;
        public EventTrigger buttonEvent;
        private ColorFaderRuntime fader;
        public ScrollRect parentScroll;

        internal bool isSelected;
        internal bool isHovered;

        internal const float FadeDuration = 0.1f;

        internal static readonly Color Col_BG_Normal = new Color32(26, 26, 26, 50);
        internal static readonly Color Col_BG_Active = new Color32(206, 198, 143, 200);
        internal static readonly Color Col_BG_Hover = new Color32(206, 198, 143, 75);

        private void Awake()
        {
            if (pathButton == null) pathButton = GetComponent<Button>();
            if (buttonEvent == null) buttonEvent = GetComponent<EventTrigger>();
            if (pathButton != null) pathButton.onClick.AddListener(OnClicked);
            if (parentScroll == null) parentScroll = GetComponentInParent<ScrollRect>();

            EventTrigger.Entry entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            entryEnter.callback.AddListener((data) => OnHighlightChanged(true));
            buttonEvent.triggers.Add(entryEnter);
            EventTrigger.Entry entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            entryExit.callback.AddListener((data) => OnHighlightChanged(false));
            buttonEvent.triggers.Add(entryExit);
            EventTrigger.Entry entryScroll = new EventTrigger.Entry { eventID = EventTriggerType.Scroll };
            entryScroll.callback.AddListener((data) => BubbleScroll(data));
            buttonEvent.triggers.Add(entryScroll);

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
            if (pathButton != null) pathButton.onClick.RemoveListener(OnClicked);
            if (buttonEvent != null) buttonEvent.triggers.Clear();

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
        private void BubbleScroll(BaseEventData data)
        {
            if (parentScroll != null && data is PointerEventData ped)
                parentScroll.OnScroll(ped);
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
