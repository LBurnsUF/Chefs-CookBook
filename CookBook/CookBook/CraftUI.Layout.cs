namespace CookBook
{
    internal static partial class CraftUI
    {
        // ---------------- Layout constants ----------------
        internal static float _panelWidth;
        internal static float _panelHeight;

        // CookBookPanel
        internal const float CookBookPanelPaddingTopNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingBottomNorm = 0.0159744409f;
        internal const float CookBookPanelPaddingLeftNorm = 0f;
        internal const float CookBookPanelPaddingRightNorm = 0f;
        internal const float CookBookPanelElementSpacingNorm = 0.0159744409f; // vertical gap between SearchBar and RecipeList

        // SearchBar container
        internal const float SearchBarContainerNorm = 0.0798722045f;
        internal const float SearchBarWidthNorm = 0.8f;
        internal const float FilterDropDownWidthNorm = 0.2f;
        internal const float SearchBarBottomBorderThicknessNorm = 0.0001f;

        // RecipeList internal layout
        internal const float RecipeListVerticalPaddingNorm = 0f;
        internal const float RecipeListLeftPaddingNorm = 0.0181818182f;
        internal const float RecipeListRightPaddingNorm = 0.0181818182f;
        internal const float RecipeListElementSpacingNorm = 0f;
        internal const float RecipeListScrollbarWidthNorm = 0f;

        // RecipeRow/RowTop
        internal const float RowTopHeightNorm = 0.111821086f;
        internal const float RowTopTopPaddingNorm = 0.00798722045f;
        internal const float RowTopBottomPaddingNorm = 0.00798722045f;
        internal const float RowTopElementSpacingNorm = 0.0181818182f;
        internal const float MetaDataColumnWidthNorm = 0.254545455f;
        internal const float MetaDataElementSpacingNorm = 0.0159744409f;
        internal const float DropDownArrowSizeNorm = 0.0511182109f;
        internal const float textSizeNorm = 0.0383386581f;

        // ----- PathsContainer sizing -----
        internal const float PathsContainerPaddingNorm = 0.0181818182f;
        internal const int PathsContainerMaxVisibleRows = 4;
        internal const float PathsContainerSpacingNorm = 0.00798722045f;

        // ----- PathRow sizing -----
        internal const float PathRowHeightNorm = 0.0798722045f;
        internal const float PathRowLeftPaddingNorm = 0.00909090909f;
        internal const float PathRowRightPaddingNorm = 0.00909090909f;
        internal const float PathRowIngredientSpacingNorm = 0.00909090909f;

        //----- Ingredients -------
        internal const float IngredientHeightNorm = 0.0670926518f;
        internal const float _IngredientStackSizeTextHeightPx = 10f;
        internal const float _IngredientStackMargin = 2f;
        internal const float _ResultStackMargin = 3f;

        //----- Confirmation -------
        internal const float FooterHeightNorm = 0.05f;
    }
}
