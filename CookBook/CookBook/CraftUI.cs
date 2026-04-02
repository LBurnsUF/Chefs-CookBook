using BepInEx.Logging;
using UnityEngine;
using static CookBook.CraftPlanner;

namespace CookBook
{
    internal static partial class CraftUI
    {
        internal static ManualLogSource _log;

        internal struct RecipeRowUI
        {
            public CraftableEntry Entry;
            public GameObject RowGO;
        }
    }
}