using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    //==================== Runtimes ====================
    internal sealed class RecipeRowRuntime : MonoBehaviour
    {
        internal CraftableEntry Entry;
        public RecipeRowView View;

        public bool IsExpanded;
        public float CollapsedHeight;

        private void OnDestroy()
        {
            if (View != null && View.RowTopButton != null)
            {
                View.RowTopButton.onClick.RemoveAllListeners();
                View.RowTopButton.HighlightChanged = null;
            }
            Entry = null;
            CraftUI.NotifyRecipeRowDestroyed(this);
        }
    }

    internal sealed class PathRowRuntime : MonoBehaviour
    {
        internal RecipeRowRuntime OwnerRow;
        internal RecipeChain Chain;
        public PathRowView View;

        internal bool isSelected;
        internal bool isHovered;

        internal const float FadeDuration = 0.1f;

        internal static readonly Color Col_BG_Normal = new Color32(26, 26, 26, 50);
        internal static readonly Color Col_BG_Active = new Color32(206, 198, 143, 255);
        internal static readonly Color Col_BG_Hover = new Color32(206, 198, 143, 75);

        private void Awake()
        {
            if (View != null && View.PathButton != null)
            {
                View.PathButton.onClick.AddListener(OnClicked);
                View.PathButton.HighlightChanged = OnHighlightChanged;
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
            if (View != null && View.PathButton != null)
            {
                View.PathButton.onClick.RemoveListener(OnClicked);
                View.PathButton.HighlightChanged = null;
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
                CraftUI.AttachReticleTo(View.VisualRect);
            }
            else
            {
                isHovered = false;
                if (CraftUI.IsReticleAttachedTo(View.VisualRect))
                    CraftUI.RestoreReticleToSelection();
            }
            UpdateVisuals(false);
        }
        public void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;

            if (isSelected) CraftUI.AttachReticleTo(View.VisualRect);
            UpdateVisuals(false);
        }

        private void UpdateVisuals(bool instant)
        {
            if (View == null || View.BackgroundImage == null) return;

            Color target =
                isSelected ? Col_BG_Active :
                isHovered ? Col_BG_Hover :
                Col_BG_Normal;

            if (instant || View.Fader == null) View.BackgroundImage.color = target;
            else View.Fader.CrossFadeColor(target, FadeDuration, ignoreTimeScale: true);
        }
    }

    internal sealed class CraftUIRunner : MonoBehaviour { }

    internal class RecipeDropdownRuntime : MonoBehaviour
    {
        public DropdownView View;
        public RecipeRowRuntime CurrentOwner;

        public int SelectedPathIndex = -1;

        public void OpenFor(RecipeRowRuntime owner)
        {
            CurrentOwner = owner;
            gameObject.SetActive(true);

            transform.SetParent(owner.View.DropdownMountPoint, false);

            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            if (View.ScrollRect != null)
            {
                View.ScrollRect.enabled = owner.Entry.Chains.Count > CraftUI.PathsContainerMaxVisibleRows;
                View.ScrollRect.verticalNormalizedPosition = 1f;
            }

            CraftUI.PopulateDropdown(View.Content, owner);
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
}
