using RoR2.UI;

namespace CookBook
{
    // hover and gamepad select share one highlight
    public sealed class CookBookButton : HGButton
    {
        public System.Action<bool> HighlightChanged;
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
}
