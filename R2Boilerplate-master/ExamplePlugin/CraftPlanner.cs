using System;
using System.Collections.Generic;
using RoR2;

namespace CookBook
{
    /// <summary>
    /// Computes all items craftable from a starting inventory using Wandering CHEF recipes, up to some max crafting depth.
    /// </summary>
    internal sealed class CraftPlanner
    {
        private readonly IReadOnlyList<ChefRecipe> _recipes;
        private readonly int _maxDepth;

        public CraftPlanner(IReadOnlyList<ChefRecipe> recipes, int maxDepth)
        {
            _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            _maxDepth = maxDepth;
        }

        /// <summary>
        /// Describes one craftable result and some metadata about how it was reached.
        /// </summary>
        internal sealed class CraftableEntry
        {
            public ItemIndex ResultItem;
            public int MinDepth;              // minimum number of crafts to obtain it
            public List<ChefRecipe> Chain;    // recipes used
        }

        /// <summary>
        /// Given a snapshot of item stacks, compute all craftable results, up to _maxDepth crafts.
        /// </summary>
        public List<CraftableEntry> ComputeCraftable(int[] startingStacks)
        {
            if (startingStacks == null)
                throw new ArgumentNullException(nameof(startingStacks));

            // TODO: implement BFS/DP here.
            return new List<CraftableEntry>();
        }
    }
}
