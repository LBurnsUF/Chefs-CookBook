using RoR2.UI;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    //==================== Runtimes ====================
    internal sealed class RecipeRowRuntime : MonoBehaviour
    {
        internal CraftUI Panel;
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
            Panel?.NotifyRecipeRowDestroyed(this);
        }
    }

    internal sealed class PathRowRuntime : MonoBehaviour
    {
        internal CraftUI Panel;
        internal RecipeRowRuntime OwnerRow;
        internal RecipeChain Chain;
        public PathRowView View;

        // slots on loan from the pool
        internal readonly List<(GameObject Template, SlotView Slot)> Slots = new();

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

            Panel?.NotifyPathRowDestroyed(this);
            OwnerRow = null;
            Chain = null;
        }

        internal void OnClicked() { Panel.OnPathSelected(this); }

        internal void OnHighlightChanged(bool isHighlighted)
        {
            if (isHighlighted)
            {
                isHovered = true;
                Panel._currentHoveredPath = this;
                Panel.AttachReticleTo(View.VisualRect);
            }
            else
            {
                isHovered = false;
                if (Panel.IsReticleAttachedTo(View.VisualRect))
                    Panel.RestoreReticleToSelection();
            }
            UpdateVisuals(false);
        }
        public void SetSelected(bool selected)
        {
            if (isSelected == selected) return;
            isSelected = selected;

            if (isSelected) Panel.AttachReticleTo(View.VisualRect);
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

    // vanilla strip buttons ignore the mouse, catch the click here
    internal sealed class ScoreboardStripHook : MonoBehaviour, IPointerClickHandler
    {
        internal ScoreboardStrip Strip;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            CraftUI.Scoreboard.ToggleStrip(Strip);
        }
    }

    // scoreboard closes when TAB is let go
    internal sealed class ScoreboardCloseHook : MonoBehaviour
    {
        private void OnDisable() => CraftUI.Scoreboard.Collapse();
    }

    // settles the list on row tops so only whole rows show, moved by wheel, nav or refresh
    internal sealed class RowSnapScroll : MonoBehaviour
    {
        internal ScrollRect Scroll;
        internal float PadTop;
        private float _settled;

        private void LateUpdate()
        {
            var content = Scroll.content;
            float pos = content.anchoredPosition.y;
            if (Mathf.Abs(pos - _settled) < 0.5f) return;

            bool down = pos > _settled;
            float max = Mathf.Max(0f, content.rect.height - Scroll.viewport.rect.height);
            float target = down ? max : 0f;

            // nearest row top at or past where it moved to
            foreach (RectTransform row in content)
            {
                if (!row.gameObject.activeSelf) continue;

                float top = -(row.localPosition.y + row.rect.yMax) - PadTop;
                if (down && top > _settled + 0.5f && top >= pos - 0.5f && top < target) target = top;
                if (!down && top < _settled - 0.5f && top <= pos + 0.5f && top > target) target = top;
            }

            target = Mathf.Clamp(target, 0f, max);
            Scroll.StopMovement();
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, target);
            _settled = target;
        }
    }

    // keeps the scoreboard list over its spacer as the scoreboard lays out
    internal sealed class ScoreboardPanelFollow : MonoBehaviour
    {
        internal RectTransform Target;
        private readonly Vector3[] _corners = new Vector3[4];

        private void LateUpdate() => Sync();

        internal void Sync()
        {
            if (!Target || !Target.gameObject.activeInHierarchy) return;

            var rt = (RectTransform)transform;
            var parent = (RectTransform)rt.parent;
            Target.GetWorldCorners(_corners);
            Vector3 min = parent.InverseTransformPoint(_corners[0]);
            Vector3 max = parent.InverseTransformPoint(_corners[2]);

            // the scoreboard sits deeper under the perspective camera, match its depth or it draws bigger
            rt.anchoredPosition3D = new Vector3(min.x - parent.rect.center.x, min.y - parent.rect.center.y, min.z);
            rt.sizeDelta = (Vector2)(max - min);
        }
    }

    internal class RecipeDropdownRuntime : MonoBehaviour
    {
        internal CraftUI Panel;
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

            Panel.PopulateDropdown(View.Content, owner);
        }

        public void Close()
        {
            if (Panel._activeDropdownRoutine != null && Panel._runner != null)
            {
                Panel._runner.StopCoroutine(Panel._activeDropdownRoutine);
                Panel._activeDropdownRoutine = null;
            }

            if (CurrentOwner == null) return;

            SelectedPathIndex = -1;
            transform.SetParent(Panel._cookbookRoot.transform, false);
            gameObject.SetActive(false);
            CurrentOwner = null;
        }

        private void OnDestroy() { CurrentOwner = null; }
    }
}
