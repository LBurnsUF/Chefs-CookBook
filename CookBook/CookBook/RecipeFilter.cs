using CookBook;
using RoR2;
using System.Collections.Generic;
using static CookBook.CraftPlanner;

internal static class RecipeFilter
{
    public enum RecipeFilterCategory { All, Damage, Healing, Utility }


    public static RecipeFilterCategory CurrentCategory = RecipeFilterCategory.All;

    private static readonly Dictionary<RecipeFilterCategory, ItemTag> CategoryToTag = new()
    {
        { RecipeFilterCategory.Damage,  ItemTag.Damage  },
        { RecipeFilterCategory.Healing, ItemTag.Healing },
        { RecipeFilterCategory.Utility, ItemTag.Utility }
    };

    public static void ApplyFiltersToUI(List<CraftUI.RecipeRowUI> rows, string searchTerm)
    {
        if (rows == null) return;

        string term = searchTerm?.Trim().ToLowerInvariant();

        foreach (var row in rows)
        {
            if (row.RowGO == null) continue;

            bool searchMatch = string.IsNullOrEmpty(term) || EntryMatchesSearch(row.Entry, term);
            bool filterMatch = EntryMatchesFilter(row.Entry);

            row.RowGO.SetActive(searchMatch && filterMatch);
        }
    }

    public static void CycleCategory()
    {
        int next = (int)CurrentCategory + 1;
        if (next > (int)RecipeFilterCategory.Utility) next = 0;
        CurrentCategory = (RecipeFilterCategory)next;
    }

    public static string GetLabel()
    {
        return CurrentCategory switch
        {
            RecipeFilterCategory.Damage => "<size=150%><sprite name=\"icon\"></size> DMG",
            RecipeFilterCategory.Healing => "<size=150%><sprite name=\"icon\"></size> HEAL",
            RecipeFilterCategory.Utility => "<size=150%><sprite name=\"icon\"></size> UTIL",
            _ => "ALL"
        };
    }

    private static bool EntryMatchesFilter(CraftableEntry entry)
    {
        if (entry == null) return false;
        if (CurrentCategory == RecipeFilterCategory.All) return true;

        int idx = entry.ResultIndex;
        if (idx < 0) return false;

        if (idx >= ItemCatalog.itemCount) return true;

        ItemDef def = ItemCatalog.GetItemDef((ItemIndex)idx);
        return def != null && def.ContainsTag(CategoryToTag[CurrentCategory]);
    }

    private static bool EntryMatchesSearch(CraftableEntry entry, string term)
    {
        if (string.IsNullOrEmpty(term)) return true;
        if (entry == null) return false;

        string name = CraftUI.GetEntryDisplayName(entry);
        return !string.IsNullOrEmpty(name) && name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static void PatchVanillaNRE(On.RoR2.CraftingController.orig_FilterAvailableOptions orig, CraftingController self)
    {
        var prompt = self.GetComponent<NetworkUIPromptController>();
        if (prompt == null || prompt.currentParticipantMaster == null)
        {
            return;
        }
        orig(self);
    }
}
