using RoR2;
using RoR2.UI;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static class RecipeTrackerUI
    {
        public static void Init()
        {
            On.RoR2.UI.ScoreboardController.Awake += OnScoreboardAwake;
            On.RoR2.UI.ScoreboardStrip.SetMaster += OnScoreboardStripSetMaster;
        }

        private static void OnScoreboardAwake(On.RoR2.UI.ScoreboardController.orig_Awake orig, ScoreboardController self)
        {
            orig(self);

            GameObject scrollGO = new GameObject("CookBookScoreboardScroll", typeof(RectTransform), typeof(ScrollRect), typeof(Image));
            scrollGO.transform.SetParent(self.container.parent, false);

            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();

            scrollRT.anchorMin = self.container.anchorMin;
            scrollRT.anchorMax = self.container.anchorMax;
            scrollRT.pivot = self.container.pivot;
            scrollRT.sizeDelta = self.container.sizeDelta;
            scrollRT.anchoredPosition = self.container.anchoredPosition;

            GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewport.transform.SetParent(scrollRT, false);
            RectTransform viewRT = viewport.GetComponent<RectTransform>();

            viewRT.anchorMin = Vector2.zero;
            viewRT.anchorMax = Vector2.one;
            viewRT.pivot = new Vector2(0.5f, 1f);
            viewRT.sizeDelta = Vector2.zero;

            self.container.SetParent(viewRT, false);

            ScrollRect scroll = scrollGO.GetComponent<ScrollRect>();
            scroll.content = self.container;
            scroll.viewport = viewRT;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f; // Ensure it feels responsive

            var img = scrollGO.GetComponent<Image>();
            img.color = new Color(0, 0, 0, 0);

            var fitter = self.container.gameObject.GetComponent<ContentSizeFitter>() ?? self.container.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var vlg = self.container.GetComponent<VerticalLayoutGroup>();
            if (vlg)
            {
                vlg.childControlHeight = true;
                vlg.childForceExpandHeight = false;
                vlg.childAlignment = TextAnchor.UpperCenter;
            }

            CraftUI.AddBorder(scrollRT, Color.red, 2f, 2f, 2f, 2f);
            CraftUI.AddBorder(viewRT, Color.green, 2f, 2f, 2f, 2f);
        }

        private static void OnScoreboardStripSetMaster(On.RoR2.UI.ScoreboardStrip.orig_SetMaster orig, ScoreboardStrip self, CharacterMaster newMaster)
        {
            orig(self, newMaster);

            var tracker = self.GetComponent<PlayerRecipeTracker>();
            if (!tracker)
            {
                tracker = self.gameObject.AddComponent<PlayerRecipeTracker>();
                tracker.InitializeUI();
            }

            tracker.BindToMaster(newMaster);
        }
    }

    /// <summary>
    /// Component that handles the per-player recipe list inside the scoreboard strip.
    /// </summary>
    internal class PlayerRecipeTracker : MonoBehaviour
    {
        private ScoreboardStrip _strip;
        private CharacterMaster _master;
        private RectTransform _dropdownRoot;
        private bool _isExpanded;

        public void InitializeUI()
        {
            _strip = GetComponent<ScoreboardStrip>();

            var le = gameObject.GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 72f;

            _dropdownRoot = new GameObject("RecipeDropdown", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            _dropdownRoot.SetParent(transform, false);

            var dropLE = _dropdownRoot.GetComponent<LayoutElement>();
            dropLE.preferredHeight = 100f;
            dropLE.flexibleWidth = 1f;

            CraftUI.AddBorderTapered(_dropdownRoot, new Color32(209, 209, 210, 150), top: 1f);

            CraftUI.AddBorder(_dropdownRoot, Color.blue, 2f, 2f, 2f, 2f);
        }

        public void BindToMaster(CharacterMaster master)
        {
            _master = master;
            RefreshRecipeList();
        }

        private void RefreshRecipeList()
        {
            if (!_master || !_master.inventory) return;
            // Future logic: Populate items here
        }
    }
}