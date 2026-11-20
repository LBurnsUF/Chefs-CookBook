using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CookBook
{
    public sealed class SlotView : MonoBehaviour
    {
        public Image Icon;
        public AsyncImage IconLoader; // on Icon, loads the sprite like vanilla
        public TextMeshProUGUI StackText;
    }
}
