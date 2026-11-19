using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    public sealed class RecipeRowView : MonoBehaviour
    {
        public RectTransform RowTransform;
        public LayoutElement RowLayoutElement;
        public RectTransform RowTop;
        public CookBookButton RowTopButton;
        public TextMeshProUGUI ArrowText;

        public RectTransform DropdownMountPoint;
        public LayoutElement DropdownLayoutElement;

        public SlotView ResultSlot;
        public TextMeshProUGUI ItemLabel;
        public TextMeshProUGUI DepthText;
        public TextMeshProUGUI PathsText;
    }
}
