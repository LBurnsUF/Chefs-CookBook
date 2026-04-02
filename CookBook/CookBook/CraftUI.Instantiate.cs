using RoR2;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {

        //==================== Instantiation ====================
        internal static GameObject CreateRecipeRow(RectTransform parent, CraftableEntry entry)
        {
            if (_recipeRowTemplate == null) return null;

            // ---------------- Initialize root ----------------
            GameObject rowGO = UnityEngine.Object.Instantiate(_recipeRowTemplate, parent);
            rowGO.name = "RecipeRow";
            rowGO.SetActive(true);

            var runtime = rowGO.GetComponent<RecipeRowRuntime>();
            if (runtime == null)
            {
                _log.LogError("CreateRecipeRow: RecipeRowRuntime component missing on template.");
                return rowGO;
            }

            runtime.Entry = entry;
            runtime.IsExpanded = false;

            // ---------------- Label ----------------
            if (runtime.ItemLabel != null)
            {
                runtime.ItemLabel.text = GetEntryDisplayName(entry);
                runtime.ItemLabel.color = GetEntryColor(entry);
            }

            // ---------------- MetaData: Depth ----------------
            if (runtime.DepthText != null) runtime.DepthText.text = $" Min Depth: {entry.MinDepth}";

            // ---------------- MetaData: Paths ----------------
            if (runtime.PathsText != null) runtime.PathsText.text = $"Paths: {entry.Chains.Count}";

            // ---------------- ItemIcon ----------------
            if (runtime.ResultIcon != null)
            {
                Sprite iconSprite = GetIcon(entry.ResultIndex);

                if (iconSprite != null)
                {
                    runtime.ResultIcon.sprite = iconSprite;
                    runtime.ResultIcon.color = Color.white;
                }
                else
                {
                    runtime.ResultIcon.sprite = null;
                    runtime.ResultIcon.color = new Color(1f, 1f, 1f, 0.1f);
                }
            }

            // ---------------- Stack Text ----------------
            if (runtime.ResultStackText != null)
            {
                if (entry.ResultCount > 1)
                {
                    runtime.ResultStackText.text = entry.ResultCount.ToString();
                    runtime.ResultStackText.gameObject.SetActive(true);
                }
                else runtime.ResultStackText.gameObject.SetActive(false);
            }

            if (runtime.RowTopButton != null)
            {
                runtime.RowTopButton.onClick.RemoveAllListeners();
                runtime.RowTopButton.onClick.AddListener(() => ToggleRecipeRow(runtime));
            }

            if (runtime.DropdownLayoutElement != null) runtime.DropdownLayoutElement.preferredHeight = 0f;

            return rowGO;
        }

        internal static GameObject CreatePathRow(RectTransform parent, RecipeChain chain, RecipeRowRuntime owner)
        {
            if (_pathRowTemplate == null) return null;

            GameObject pathRowGO = UnityEngine.Object.Instantiate(_pathRowTemplate, parent);
            pathRowGO.name = "PathRow";
            pathRowGO.SetActive(true);

            var runtime = pathRowGO.GetComponent<PathRowRuntime>();
            if (runtime != null) runtime.Init(owner, chain);
            else _log.LogError("PathRowTemplate missing PathRowRuntime component.");

            if (chain.PhysicalCostSparse != null)
            {
                foreach (var ingredient in chain.PhysicalCostSparse)
                {
                    Sprite icon = GetIcon(ingredient.UnifiedIndex);
                    if (icon != null) InstantiateSlot(_ingredientSlotTemplate, runtime.VisualRect, icon, ingredient.Count);
                }
            }

            if (chain.AlliedTradeSparse != null)
            {
                foreach (var trade in chain.AlliedTradeSparse)
                {
                    Sprite icon = GetIcon(trade.UnifiedIndex);
                    if (icon != null) InstantiateSlot(_tradeSlotTemplate, runtime.VisualRect, icon, trade.TradesRequired);
                }
            }

            if (chain.DroneCostSparse != null)
            {
                var localUser = LocalUserManager.GetFirstLocalUser()?.currentNetworkUser;

                foreach (var requirement in chain.DroneCostSparse)
                {
                    if (requirement.DroneIdx != DroneIndex.None)
                    {
                        Sprite droneSprite = GetDroneIcon(requirement.DroneIdx);
                        if (droneSprite != null)
                        {
                            bool isAlliedDrone = requirement.Owner != null && requirement.Owner != localUser;
                            GameObject template = isAlliedDrone ? _tradeSlotTemplate : _droneSlotTemplate;

                            InstantiateSlot(template, runtime.VisualRect, droneSprite, requirement.Count);
                        }
                    }
                }
            }

            return pathRowGO;
        }

        internal static void InstantiateSlot(GameObject template, Transform parentrow, Sprite icon, int count)
        {
            if (template == null) return;
            GameObject slotGO = UnityEngine.Object.Instantiate(template, parentrow);
            slotGO.SetActive(true);

            var iconImg = slotGO.transform.Find("Icon")?.GetComponent<Image>();
            if (iconImg) iconImg.sprite = icon;

            var stackTmp = slotGO.transform.Find("StackText")?.GetComponent<TextMeshProUGUI>();
            if (stackTmp)
            {
                stackTmp.text = count > 1 ? count.ToString() : string.Empty;
                stackTmp.gameObject.SetActive(count > 1);
            }
        }

        internal static GameObject InstantiateLayer(GameObject template, Transform parent)
        {
            var layer = UnityEngine.Object.Instantiate(template, parent, false);
            layer.SetActive(true);

            var rt = layer.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var le = layer.GetComponent<LayoutElement>();
            if (le) le.ignoreLayout = true;

            SetupStaticVisuals(layer);

            return layer;
        }

        internal static void SetupStaticVisuals(GameObject root)
        {
            var rt = root.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
