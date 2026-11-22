using RoR2;
using RoR2.UI;
using System.Collections;
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
        private const float RefreshDelay = 1f; // seconds after the last pickup

        internal bool _cutDown;
        private ScoreboardStrip _expandedStrip;
        private CharacterMaster _filledMaster;
        private GameObject _inspectPanel;
        private RectTransform _spacer;
        private Coroutine _refreshRoutine;
        private bool _paused;

        internal static void InitScoreboard()
        {
            ScoreboardController.onScoreboardOpen += OnScoreboardOpen;
            HUD.onHudTargetChangedGlobal += Scoreboard.Prefetch;
            Inventory.onInventoryChangedGlobal += Scoreboard.OnInventoryChanged;
        }

        internal static void ShutdownScoreboard()
        {
            ScoreboardController.onScoreboardOpen -= OnScoreboardOpen;
            HUD.onHudTargetChangedGlobal -= Scoreboard.Prefetch;
            Inventory.onInventoryChangedGlobal -= Scoreboard.OnInventoryChanged;
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

            Scoreboard.Prefetch(controller.GetComponentInParent<HUD>());
        }

        // built with the HUD and kept filled with your list, TAB only shows it
        private void Prefetch(HUD hud)
        {
            if (!EnsureBuilt(hud) || _expandedStrip != null) return;

            var local = LocalUserManager.GetFirstLocalUser()?.cachedMaster;
            if (local && local != _filledMaster) Fill(local);
        }

        // pickups refresh whoever is filled, once they settle
        private void OnInventoryChanged(Inventory inv)
        {
            if (_runner == null || !_filledMaster || inv != _filledMaster.inventory) return;
            if (_refreshRoutine != null) _runner.StopCoroutine(_refreshRoutine);
            _refreshRoutine = _runner.StartCoroutine(RefreshAfterDelay());
        }

        private IEnumerator RefreshAfterDelay()
        {
            yield return new WaitForSecondsRealtime(RefreshDelay);
            _refreshRoutine = null;
            Fill(_filledMaster);
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
            var hud = strip.GetComponentInParent<HUD>();
            if (!master || !EnsureBuilt(hud)) return;

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
            float height = RoundToEven(container.rect.height - used);

            // pushes the strips below down, the panel sits over it
            if (!_spacer) _spacer = CreateInternal("CookBookSpacer", strip.transform.parent, typeof(RectTransform));
            _spacer.SetSiblingIndex(strip.transform.GetSiblingIndex() + 1);
            _spacer.sizeDelta = new Vector2(_spacer.sizeDelta.x, height);
            _spacer.gameObject.SetActive(true);
            LayoutRebuilder.ForceRebuildLayoutImmediate(container);

            Show(height);
            _expandedStrip = strip;

            // clicked in, stays up after TAB is let go, next TAB closes it
            hud.shouldForceScoreboardToggle = true;

            // your list is usually filled already
            if (master != _filledMaster) Fill(master);

            SetSoloPause(true);
        }

        internal void Collapse()
        {
            _expandedStrip = null;
            if (_cookbookRoot != null) Hide();
            if (_spacer) _spacer.gameObject.SetActive(false);

            if (_inspectPanel) _inspectPanel.SetActive(true);
            _inspectPanel = null;

            SetSoloPause(false);
        }

        // one per HUD, a new stage builds a new one
        private bool EnsureBuilt(HUD hud)
        {
            if (!hud || !hud.scoreboardPanel || !UIBundle.EnsureLoaded() || !LoadWokPanel()) return false;
            if (_cookbookRoot != null && _cookbookRoot.transform.parent == hud.mainContainer.transform) return true;

            DestroyPanel();
            _filledMaster = null;
            _refreshRoutine = null;
            BuildScoreboardPanel(hud);
            Hide();
            return true;
        }

        private void Fill(CharacterMaster master)
        {
            if (!master) return;

            _filledMaster = master;
            var craftables = RecipeViewer.Compute(master, out var snap);
            CraftablesForUIChanged(craftables, snap);
        }

        private static bool LoadWokPanel()
        {
            if (_wokPanel) return true;

            var prefab = WokPanelRef.LoadAssetAsync().WaitForCompletion();
            _wokPanel = prefab ? prefab.GetComponent<CraftingPanel>() : null;
            if (!_wokPanel) _log.LogError("CraftUI: no CraftingPanel on the wok panel prefab, scoreboard list disabled.");
            return _wokPanel;
        }

        // same build as the CHEF panel, outside the scoreboard so TAB never wakes the rows
        private void BuildScoreboardPanel(HUD hud)
        {
            // live strips only exist once TAB is held, their prefab has the art
            var controller = hud.scoreboardPanel.GetComponent<ScoreboardController>();
            var strip = controller && controller.stripPrefab ? controller.stripPrefab.GetComponent<ScoreboardStrip>() : null;

            _cookbookRoot = CreateUIObject("CookBookScoreboardPanel", typeof(RectTransform), typeof(MPEventSystemProvider), typeof(CraftUIRunner), typeof(Canvas), typeof(GraphicRaycaster), typeof(Image), typeof(CanvasGroup), typeof(ScoreboardPanelFollow));

            // same backdrop as the strip above it
            var stripBgTr = strip ? strip.transform.Find("LongBackground") : null;
            var stripBg = stripBgTr ? stripBgTr.GetComponent<Image>() : null;
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

            // last in the HUD so it draws over the scoreboard, the follow places it
            RectTransform cbRT = _cookbookRoot.GetComponent<RectTransform>();
            cbRT.SetParent(hud.mainContainer.transform, false);
            cbRT.SetAsLastSibling();
            cbRT.anchorMin = new Vector2(0.5f, 0.5f);
            cbRT.anchorMax = new Vector2(0.5f, 0.5f);
            cbRT.pivot = Vector2.zero;
            cbRT.sizeDelta = new Vector2(UIBundleInfo.PanelWidth, UIBundleInfo.PanelHeight);

            // path rows size off the authored height, not the list's
            _panelHeight = UIBundleInfo.PanelHeight;

            AcquireTemplates(_wokPanel, cbRT);

            // no crafting here, the list takes the footer's room
            var view = _cookbookRoot.GetComponentInChildren<PanelView>(true);
            view.CraftSlot.parent.gameObject.SetActive(false);

            // tapered streaks don't fit the scoreboard, row dividers stay
            view.transform.Find("BorderGroup_Tapered")?.gameObject.SetActive(false);
            view.transform.Find("SearchBarContainer/BorderGroup_Tapered")?.gameObject.SetActive(false);

            // plain fill reads bare on the backdrop, use the strip's item slot
            var slotBg = strip && strip.itemInventoryDisplay ? strip.itemInventoryDisplay.GetComponent<Image>() : null;
            var searchBg = view.SearchInput.GetComponent<Image>();
            if (slotBg && searchBg)
            {
                searchBg.sprite = slotBg.sprite;
                searchBg.type = slotBg.type;
                searchBg.color = slotBg.color;
            }

            // inside the backdrop's border, same padding as the strip's contents
            var stripPad = stripBgTr ? stripBgTr.GetComponent<HorizontalLayoutGroup>() : null;
            if (stripPad)
            {
                var panelRT = (RectTransform)view.transform;
                panelRT.offsetMin = new Vector2(stripPad.padding.left, panelRT.offsetMin.y);
                panelRT.offsetMax = new Vector2(-stripPad.padding.right, panelRT.offsetMax.y);
            }

            // same layer the strip buttons answer to
            _vanillaTopLayer = hud.scoreboardPanel.GetComponent<UILayerKey>();
            BindTopLayer(_vanillaTopLayer, true);
        }

        // over the spacer, at the space left
        private void Show(float height)
        {
            var follow = _cookbookRoot.GetComponent<ScoreboardPanelFollow>();
            follow.Target = _spacer;
            follow.Sync();

            var listRT = (RectTransform)_recipeListContent.GetComponentInParent<ScrollRect>().transform;
            listRT.sizeDelta = new Vector2(listRT.sizeDelta.x, height + listRT.anchoredPosition.y - ListBottomMargin);

            var vanillaProvider = _cookbookRoot.transform.parent.GetComponentInParent<MPEventSystemProvider>();
            _eventSystemProvider.eventSystem = vanillaProvider ? vanillaProvider.resolvedEventSystem : null;

            SetShown(true);
        }

        private void Hide()
        {
            ResetPanelState();
            SetShown(false);
        }

        // the group also hides the vanilla button clones, they carry canvases of their own
        private void SetShown(bool shown)
        {
            var group = _cookbookRoot.GetComponent<CanvasGroup>();
            group.alpha = shown ? 1f : 0f;
            group.interactable = shown;
            group.blocksRaycasts = shown;
            _cookbookRoot.GetComponent<Canvas>().enabled = shown;
            _cookbookRoot.GetComponent<GraphicRaycaster>().enabled = shown;
            foreach (var ev in _cookbookRoot.GetComponents<HGGamepadInputEvent>()) ev.enabled = shown;
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
