using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace CookBook
{
    internal sealed partial class CraftUI
    {
        internal static readonly CraftUI Scoreboard = new() { _cutDown = true };

        // RoR2/DLC3/MealPrep/MealPrepPickerPanel.prefab, the wok panel, for vanilla art off the CHEF
        private static readonly AssetReferenceGameObject WokPanelRef = new AssetReferenceGameObject("f503de92590d0ee40bae54e7a7d969c8");
        private static CraftingPanel _wokPanel;

        private const float ListBottomMargin = 10f; // panel prefab bottom padding

        internal bool _cutDown;
        private ScoreboardStrip _expandedStrip;
        private GameObject _inspectPanel;
        private bool _paused;

        internal static void InitScoreboard()
        {
            ScoreboardController.onScoreboardOpen += OnScoreboardOpen;
        }

        internal static void ShutdownScoreboard()
        {
            ScoreboardController.onScoreboardOpen -= OnScoreboardOpen;
            Scoreboard.Collapse();
            Scoreboard.Shutdown();
        }

        // strips are pooled, hook each once
        private static void OnScoreboardOpen()
        {
            var controller = Object.FindObjectOfType<ScoreboardController>();
            if (!controller) return;
            if (!controller.GetComponent<ScoreboardCloseHook>()) controller.gameObject.AddComponent<ScoreboardCloseHook>();

            foreach (var strip in controller.container.GetComponentsInChildren<ScoreboardStrip>(true))
            {
                if (strip.GetComponent<ScoreboardStripHook>()) continue;
                strip.gameObject.AddComponent<ScoreboardStripHook>().Strip = strip;
            }
        }

        internal void ToggleStrip(ScoreboardStrip strip)
        {
            bool same = _expandedStrip == strip;
            Collapse();
            if (!same) Expand(strip);
        }

        private void Expand(ScoreboardStrip strip)
        {
            var inv = strip.keyItemIcon ? strip.keyItemIcon.targetInventory : null;
            var master = inv ? inv.GetComponent<CharacterMaster>() : null;
            if (!master || !UIBundle.EnsureLoaded() || !LoadWokPanel()) return;

            // the inspect panel's space goes to the list
            var container = (RectTransform)strip.transform.parent.parent;
            _inspectPanel = container.Find("InspectPanel")?.gameObject;
            if (_inspectPanel) _inspectPanel.SetActive(false);
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);

            float used = 0f;
            foreach (RectTransform child in container)
            {
                if (child.gameObject.activeSelf) used += LayoutUtility.GetPreferredHeight(child);
            }
            var size = new Vector2(RoundToEven(((RectTransform)strip.transform).rect.width), RoundToEven(container.rect.height - used));

            // rows are laid out to the strip width, rebuild if it changes
            if (_cookbookRoot == null || size.x != _builtSize.x)
            {
                DestroyPanel();
                BuildScoreboardPanel(strip, size);
            }
            MountScoreboardPanel(strip, size.y);
            _expandedStrip = strip;

            // clicked in, stays up after TAB is let go, next TAB closes it
            var hud = strip.GetComponentInParent<HUD>();
            if (hud) hud.shouldForceScoreboardToggle = true;

            var craftables = RecipeViewer.Compute(master, out var snap);
            CraftablesForUIChanged(craftables, snap);

            SetSoloPause(true);
        }

        internal void Collapse()
        {
            _expandedStrip = null;
            Detach();

            if (_inspectPanel) _inspectPanel.SetActive(true);
            _inspectPanel = null;

            SetSoloPause(false);
        }

        private static bool LoadWokPanel()
        {
            if (_wokPanel) return true;

            var prefab = WokPanelRef.LoadAssetAsync().WaitForCompletion();
            _wokPanel = prefab ? prefab.GetComponent<CraftingPanel>() : null;
            if (!_wokPanel) _log.LogError("CraftUI: no CraftingPanel on the wok panel prefab, scoreboard list disabled.");
            return _wokPanel;
        }

        // same build as the CHEF panel, at scoreboard units
        private void BuildScoreboardPanel(ScoreboardStrip strip, Vector2 size)
        {
            if (!_panelHolder)
            {
                _panelHolder = new GameObject("CookBookScoreboardHolder");
                Object.DontDestroyOnLoad(_panelHolder);
            }

            _cookbookRoot = CreateUIObject("CookBookScoreboardPanel", typeof(RectTransform), typeof(MPEventSystemProvider), typeof(CraftUIRunner), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image));

            // same backdrop as the strip above it
            var stripBg = strip.transform.Find("LongBackground")?.GetComponent<Image>();
            var bg = _cookbookRoot.GetComponent<Image>();
            if (stripBg)
            {
                bg.sprite = stripBg.sprite;
                bg.type = stripBg.type;
                bg.color = stripBg.color;
            }
            else bg.enabled = false;

            _eventSystemProvider = _cookbookRoot.GetComponent<MPEventSystemProvider>();
            _runner = _cookbookRoot.GetComponent<CraftUIRunner>();
            _cookbookRoot.GetComponent<Canvas>().pixelPerfect = true;

            // build mounted so canvas scale matches
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            cbRT.SetParent(strip.transform.parent, false);
            cbRT.sizeDelta = size;
            _builtSize = size;

            // path rows size off the authored height, not the list's
            _panelHeight = UIBundleInfo.PanelHeight;

            AcquireTemplates(_wokPanel, cbRT);

            // no crafting here, the list takes the footer's room
            var view = _cookbookRoot.GetComponentInChildren<PanelView>(true);
            view.CraftSlot.parent.gameObject.SetActive(false);

            // tapered streaks don't fit the scoreboard, row dividers stay
            view.transform.Find("BorderGroup_Tapered")?.gameObject.SetActive(false);
            view.transform.Find("SearchBarContainer/BorderGroup_Tapered")?.gameObject.SetActive(false);
        }

        // under the strip, at the space left
        private void MountScoreboardPanel(ScoreboardStrip strip, float height)
        {
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            cbRT.SetParent(strip.transform.parent, false);
            cbRT.SetSiblingIndex(strip.transform.GetSiblingIndex() + 1);
            cbRT.sizeDelta = new Vector2(cbRT.sizeDelta.x, height);

            var listRT = (RectTransform)_recipeListContent.GetComponentInParent<ScrollRect>().transform;
            listRT.sizeDelta = new Vector2(listRT.sizeDelta.x, height + listRT.anchoredPosition.y - ListBottomMargin);

            var vanillaProvider = strip.GetComponentInParent<MPEventSystemProvider>();
            _eventSystemProvider.eventSystem = vanillaProvider ? vanillaProvider.resolvedEventSystem : null;

            // same layer the strip buttons answer to
            _vanillaTopLayer = strip.GetComponent<HGButton>()?.requiredTopLayer;
            BindTopLayer(_vanillaTopLayer, true);
            _cookbookRoot.GetComponent<Canvas>().enabled = true;
        }

        // solo only, a shared run keeps going
        private void SetSoloPause(bool pause)
        {
            if (pause == _paused) return;
            if (pause && !RoR2Application.isInSinglePlayer) return;
            if (!PauseStopController.instance) return;

            PauseStopController.instance.Pause(pause);
            _paused = pause;
        }
    }
}
